using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocalControl.Core;
using LocalControl.Server;

var directory = Path.Combine(Path.GetTempPath(), "LocalControl-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); Console.WriteLine("PASS " + name); passed++; }
void Reject(Action action, string name) { try { action(); } catch (ControlException) { Check(true, name); return; } throw new Exception("FAILED: " + name); }
try
{
    var clock = new Clock();
    var trust = new TrustStore(Path.Combine(directory, "trust"), clock);
    var admin = trust.CreateDesktop();
    Check(trust.Authenticate(admin.Credential, true)?.Desktop == true, "desktop credential only on loopback listener");
    Check(trust.Authenticate(admin.Credential, false) is null, "desktop credential rejected on LAN");
    Reject(() => trust.RequestPair(TrustStore.Token(), TrustStore.Token(), "Phone", "10.0.0.2"), "closed pairing rejects invite");
    var invite = trust.OpenPairing();
    var nonce = TrustStore.Token();
    var ticket = trust.RequestPair(invite.Token, nonce, "Phone", "10.0.0.2");
    Check(trust.Claim(ticket.Id, ticket.Secret).Status == "pending", "invite grants no command rights");
    Reject(() => trust.RequestPair(invite.Token, nonce, "Phone", "10.0.0.2"), "duplicate nonce rejected");
    Reject(() => trust.Claim(ticket.Id, TrustStore.Token()), "wrong claim secret rejected");
    Reject(() => trust.Decide(ticket.Id, true, [Permissions.Status, "remote.input"]), "unsupported permissions rejected");
    trust.Decide(ticket.Id, true, [Permissions.Status]);
    var claims = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
    { try { return trust.Claim(ticket.Id, ticket.Secret); } catch (ControlException) { return ("invalid", (string?)null, (Lease?)null); } })));
    var approved = claims.Single(x => x.Item1 == "approved");
    Check(claims.Count(x => x.Item1 == "approved") == 1, "claim exchange is atomic and single-use");
    var lease = approved.Item3!;
    Check(trust.Authenticate(approved.Item2, false) == lease, "approved phone authenticates");
    Check(trust.Authenticate(approved.Item2, true) is null, "phone credential rejected on desktop listener");
    Check(!lease.Allows(Permissions.Audio), "declined permission remains denied");
    Check(!trust.ValidateCsrf(lease, TrustStore.Token()) && trust.ValidateCsrf(lease, lease.Csrf), "CSRF token exact match");
    var connections = new Connections(); var aborted = false;
    connections.Add("phone", lease, () => aborted = true);
    trust.Logout(approved.Item2);
    Check(connections.Valid(trust).Length == 0 && aborted, "logout aborts existing live connection");
    var second = trust.RequestPair(invite.Token, TrustStore.Token(), "Phone 2", "10.0.0.2");
    trust.Decide(second.Id, true, Permissions.Default);
    var claim = trust.Claim(second.Id, second.Secret);
    trust.Revoke(claim.Lease!.DeviceId);
    Check(trust.Authenticate(claim.Credential, false) is null, "revocation invalidates active credentials");
    var restart = new TrustStore(Path.Combine(directory, "trust"), clock);
    Check(restart.Devices().Length == 1 && restart.Authenticate(approved.Item2, false) is null, "device persists but HTTP credentials do not survive restart");
    var rejected = trust.RequestPair(invite.Token, TrustStore.Token(), "Rejected", "10.0.0.2");
    trust.Decide(rejected.Id, false, []);
    Check(trust.Claim(rejected.Id, rejected.Secret).Status == "rejected", "explicit rejection never creates session");
    clock.Advance(TimeSpan.FromMinutes(3));
    Reject(() => trust.RequestPair(invite.Token, TrustStore.Token(), "Late", "10.0.0.2"), "invite expires after 120 seconds");
    invite = trust.OpenPairing();
    var expiring = trust.RequestPair(invite.Token, TrustStore.Token(), "Expiry", "10.0.0.2");
    trust.Decide(expiring.Id, true, Permissions.Default);
    var expiringClaim = trust.Claim(expiring.Id, expiring.Secret);
    clock.Advance(TimeSpan.FromMinutes(31));
    Check(trust.Authenticate(expiringClaim.Credential, false) is null && !trust.StillValid(expiringClaim.Lease!), "HTTP session expires after 30 minutes");
    Check(!new VolumeChange(float.NaN, null).IsValid && !new VolumeChange(1.1f, null).IsValid && !new VolumeChange(null, null).IsValid && new VolumeChange(.5f, true).IsValid, "audio rejects NaN, out-of-range and empty command");

    var intents = new Intents();
    var intent = intents.Create("phone-a", "shutdown");
    Reject(() => intents.Consume("phone-b", intent), "power intent cannot be consumed by another device");
    Check(intents.Consume("phone-a", intent) == "shutdown", "power action bound to approved intent");
    Reject(() => intents.Consume("phone-a", intent), "power confirmation cannot be replayed");
    Reject(() => intents.Create("phone-a", "cmd.exe"), "power actions use fixed allowlist");
    Reject(() => Transfers.SafeName("CON.txt"), "Windows reserved transfer filenames rejected");
    var settings = new SettingsStore(Path.Combine(directory,"transfer-settings"));
    var outputFolder = Path.Combine(directory,"received");
    settings.Set(settings.Get() with {TransferDirectory=outputFolder});
    var transfers = new Transfers(settings,Path.Combine(directory,"transfer-data"));
    var upload = transfers.Begin("phone-a","hello.txt",3);
    Reject(() => transfers.Cancel(upload.Id,"phone-b"), "foreign device cannot cancel upload");
    await transfers.Upload(upload.Id,"phone-a",new MemoryStream(new byte[]{1,2,3}),CancellationToken.None);
    Check(Directory.GetFiles(outputFolder).Length==1,"exact-length upload commits one file");
    var offered = transfers.Offer("phone-a",Directory.GetFiles(outputFolder).Single());
    Reject(() => transfers.Download(offered.Id,"phone-b"), "offered file is private to selected paired device");
    using(var publisher=System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256)) {
        var archive=Path.Combine(directory,"package.zip");File.WriteAllBytes(archive,new byte[]{1,2,3});
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archive))).ToLowerInvariant();
        var manifest=new UpdateManifest("1.0.1","package.zip",hash,3);
        var signature=publisher.SignData(UpdatePackage.SigningBytes(manifest),System.Security.Cryptography.HashAlgorithmName.SHA256);
        var manifestPath=archive+".update.json";File.WriteAllText(manifestPath,JsonSerializer.Serialize(new SignedUpdate(manifest,Convert.ToBase64String(signature))));
        var keyPath=Path.Combine(directory,"publisher.pem");File.WriteAllText(keyPath,publisher.ExportSubjectPublicKeyInfoPem());
        Check(UpdatePackage.Verify(manifestPath,keyPath).Archive==archive,"signed archive verified against pinned publisher");
        File.WriteAllBytes(archive,new byte[]{3,2,1});
        Reject(() => UpdatePackage.Verify(manifestPath,keyPath),"archive tampering rejected despite valid manifest signature");
    }

    var web = Path.Combine(directory, "web"); Directory.CreateDirectory(web); await File.WriteAllTextAsync(Path.Combine(web, "index.html"), "<!doctype html><title>Test</title>");
    var apiTrust = new TrustStore(Path.Combine(directory, "api"));
    var computer = new FakeComputer();
    await using var host = new ControlHost(computer, apiTrust, web, Path.Combine(directory,"host"));
    await host.Start();
    using var client = new HttpClient(new HttpClientHandler { UseCookies = false, UseProxy = false }) { BaseAddress = new(host.DesktopUrl) };
    Check((await client.GetAsync("/health")).IsSuccessStatusCode, "health contains no authentication requirement");
    Check((await client.GetAsync("/api/v1/state")).StatusCode == HttpStatusCode.Unauthorized, "anonymous state rejected");
    var desktop = apiTrust.CreateDesktop();
    client.DefaultRequestHeaders.Add("Cookie", "lc_session=" + desktop.Credential);
    Check((await client.GetAsync("/api/v1/state")).IsSuccessStatusCode, "authenticated state delivered");
    var spoof = new HttpRequestMessage(HttpMethod.Get, "/api/v1/state"); spoof.Headers.Host = "evil.invalid";
    Check((await client.SendAsync(spoof)).StatusCode == HttpStatusCode.BadRequest, "DNS rebinding Host rejected");
    Check((await client.PutAsJsonAsync("/api/v1/audio/known", new VolumeChange(.3f, null))).StatusCode == HttpStatusCode.Forbidden, "mutation without exact Origin rejected");
    client.DefaultRequestHeaders.Add("Origin", host.DesktopUrl);
    Check((await client.PutAsJsonAsync("/api/v1/audio/known", new VolumeChange(.3f, null))).StatusCode == HttpStatusCode.Forbidden, "mutation without CSRF rejected");
    client.DefaultRequestHeaders.Add("X-LC-CSRF", desktop.Lease.Csrf);
    Check((await client.PutAsJsonAsync("/api/v1/audio/known", new VolumeChange(.3f, null))).StatusCode == HttpStatusCode.NoContent && computer.Commands == 1, "authorized audio command reaches adapter");
    Check((await client.PutAsJsonAsync("/api/v1/audio/known", new VolumeChange(5f, null))).StatusCode == HttpStatusCode.BadRequest && computer.Commands == 1, "invalid audio never reaches adapter");
    Check((await client.PostAsJsonAsync("/api/v1/apps/arbitrary/launch", new { executable = "cmd.exe", args = "/c anything" })).StatusCode == HttpStatusCode.NotFound, "arbitrary executable request cannot launch");
    client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://evil.invalid");
    Check((await client.PutAsJsonAsync("/api/v1/audio/known", new VolumeChange(.3f, null))).StatusCode == HttpStatusCode.Forbidden && computer.Commands == 1, "foreign Origin rejected even with valid CSRF");
    client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", host.DesktopUrl);
    var addresses = ControlHost.LanAddresses();
    if (addresses.Length > 0)
    {
        await host.SetNetwork(addresses[0], CancellationToken.None);
        using var phone = new HttpClient(new HttpClientHandler { UseCookies = false, UseProxy = false }) { BaseAddress = new(host.LanUrl!) };
        phone.DefaultRequestHeaders.Add("Origin", host.LanUrl);
        phone.DefaultRequestHeaders.Add("Cookie", "lc_session=" + desktop.Credential);
        Check((await phone.GetAsync("/api/v1/state")).StatusCode == HttpStatusCode.Unauthorized, "LAN refuses native desktop cookie over HTTP");
        phone.DefaultRequestHeaders.Remove("Cookie");
        var apiInvite = apiTrust.OpenPairing();
        var requestResponse = await phone.PostAsJsonAsync("/api/v1/pairing/requests", new PairRequest(apiInvite.Token, TrustStore.Token(), "HTTP phone"));
        requestResponse.EnsureSuccessStatusCode();
        var apiTicket = (await requestResponse.Content.ReadFromJsonAsync<PairTicket>())!;
        apiTrust.Decide(apiTicket.Id, true, [Permissions.Status]);
        var claimResponse = await phone.PostAsJsonAsync("/api/v1/pairing/claim", new ClaimRequest(apiTicket.Id, apiTicket.Secret));
        claimResponse.EnsureSuccessStatusCode();
        var cookie = claimResponse.Headers.GetValues("Set-Cookie").Single();
        Check(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) && !cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase), "HTTP claim cookie is HttpOnly Strict and nonpersistent");
        phone.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        var phoneSession = await phone.GetFromJsonAsync<JsonElement>("/api/v1/session");
        phone.DefaultRequestHeaders.Add("X-LC-CSRF", phoneSession.GetProperty("csrf").GetString());
        Check((await phone.GetAsync("/api/v1/state")).IsSuccessStatusCode, "paired phone reads real API snapshot");
        Check((await phone.GetAsync("/api/v1/desktop/devices")).StatusCode == HttpStatusCode.NotFound, "desktop route physically absent from LAN host");
        Check((await phone.PutAsJsonAsync("/api/v1/audio/known", new VolumeChange(.8f, null))).StatusCode == HttpStatusCode.Forbidden && computer.Commands == 1, "per-device audio permission enforced in REST");
        await host.SetNetwork(null, CancellationToken.None);
        Check(host.LanUrl is null && apiTrust.Devices().Length == 1, "LAN off preserves records and stops listener");
    }
    else Console.WriteLine("SKIP LAN integration: no private IPv4 interface; run on Windows LAN before release.");
    Console.WriteLine($"{passed} checks passed.");
}
finally { Directory.Delete(directory, true); }

sealed class Clock : TimeProvider
{
    private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan time) => now += time;
}
sealed class FakeComputer : IComputer
{
    public int Commands;
    public Task<StateSnapshot> ReadState(CancellationToken token) => Task.FromResult(new StateSnapshot(1, DateTimeOffset.UtcNow, new("Test", 1, 1024, 512, 10, []), [], []));
    public Task ChangeAudio(string id, VolumeChange change, CancellationToken token) { Commands++; return Task.CompletedTask; }
    public Task<AppItem[]> GetApps(CancellationToken token) => Task.FromResult(Array.Empty<AppItem>());
    public Task Launch(string id, CancellationToken token) => Task.FromException(new ControlException("app_unknown", "Unknown app", 404));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
