using System.Net;
using System.Net.NetworkInformation;
using System.Threading.RateLimiting;
using System.Collections.Concurrent;
using LocalControl.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalControl.Server;

public sealed record PairRequest(string Invite, string Nonce, string Label);
public sealed record ClaimRequest(string Id, string Secret);
public sealed record Decision(bool Approve, string[] Permissions);
public sealed record NetworkChange(string? Address,int Port=41017);
public sealed partial class ControlHost(IComputer computer, TrustStore trust, string webRoot, string? dataDirectory = null) : IAsyncDisposable
{
    private WebApplication? desktop, lan;
    private CancellationTokenSource? pumpStop;
    private Task? pump;
    private readonly SemaphoreSlim networkGate = new(1);
    private readonly Connections desktopConnections = new(), lanConnections = new();
    public string DesktopUrl { get; private set; } = "";
    public string? LanUrl { get; private set; }
    public event Action<string, Exception>? Diagnostic;
    public const int LanPort = 41017;
    private int listeningPort=LanPort;
    private readonly ConcurrentDictionary<string,CancellationTokenSource> deviceRequests=[];
    private readonly SettingsStore configuration=new(dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalControl"));
    private Transfers? transfers;
    private readonly Intents powerIntents=new();
    public async Task Start(CancellationToken token = default)
    {
        if (!File.Exists(Path.Combine(webRoot, "index.html"))) throw new FileNotFoundException("Web bundle missing. Run scripts/build.ps1.");
        trust.Revoked += Revoke;
        desktop = Build(true, IPAddress.Loopback, 0, desktopConnections);
        await desktop.StartAsync(token);
        DesktopUrl = desktop.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        pumpStop = new();
        pump = Pump(pumpStop.Token);
    }
    public static string[] LanAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(x => x.GetIPProperties().UnicastAddresses)
        .Where(x => x.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && Private(x.Address))
        .Select(x => x.Address.ToString()).Distinct().Order().ToArray();
    private static bool Private(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31);
    }
    public async Task SetNetwork(string? address, CancellationToken token,int port=LanPort)
    {
        await networkGate.WaitAsync(token);
        try
        {
            if (address is not null && !LanAddresses().Contains(address)) throw new ControlException("invalid_interface", "Выберите действующий локальный IPv4 адрес.");
            if(port is <1024 or >65535)throw new ControlException("invalid_port","Порт должен быть от 1024 до 65535.");
            trust.DropPhoneSessions();
            foreach(var device in deviceRequests.Keys.ToArray())Revoke(device);
            lanConnections.Valid(trust);
            if (lan is not null) { await lan.StopAsync(token); await lan.DisposeAsync(); lan = null; }
            LanUrl = null;
            if (address is null) return;
            listeningPort=port;
            var next = Build(false, IPAddress.Parse(address), port, lanConnections);
            try { await next.StartAsync(token); lan = next; LanUrl = $"http://{address}:{port}"; }
            catch { await next.DisposeAsync(); throw new ControlException("listen_failed", "Не удалось открыть LAN порт. Проверьте адрес и занятость порта.", 409); }
        }
        finally { networkGate.Release(); }
    }
    private void Revoke(string id) { desktopConnections.Revoke(id); lanConnections.Revoke(id); if(deviceRequests.TryRemove(id,out var cancellation))cancellation.Cancel();transfers?.Revoke(id); }
    private WebApplication Build(bool admin, IPAddress address, int port, Connections connections)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory, WebRootPath = webRoot });
        builder.Logging.ClearProviders(); // Never log pairing/session credentials or request bodies.
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.AddServerHeader = false;
            o.Limits.MaxRequestBodySize = 8192;
            o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            o.Listen(address, port);
        });
        builder.Services.AddSignalR(o => { o.MaximumReceiveMessageSize = 8192; o.EnableDetailedErrors = false; });
        builder.Services.AddSingleton(connections);
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
                    new FixedWindowRateLimiterOptions { PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
            o.AddPolicy("pairing", ctx => RateLimitPartition.GetFixedWindowLimiter("pairing", _ =>
                new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        });
        var app = builder.Build();
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            var authority = $"{address}:{ctx.Connection.LocalPort}";
            ctx.Response.Headers["Content-Security-Policy"] = $"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self' ws://{authority}; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            if (!string.Equals(ctx.Request.Host.Value, authority, StringComparison.OrdinalIgnoreCase)) { ctx.Response.StatusCode = 400; return; }
            if (!admin && (ctx.Connection.RemoteIpAddress is null || !Private(ctx.Connection.RemoteIpAddress))) { ctx.Response.StatusCode = 403; return; }
            var expectedOrigin = $"http://{authority}";
            var mutating = ctx.Request.Method is not ("GET" or "HEAD" or "OPTIONS");
            var socket = ctx.Request.Headers.Upgrade.ToString().Equals("websocket", StringComparison.OrdinalIgnoreCase);
            if ((mutating || socket) && !string.Equals(ctx.Request.Headers.Origin.ToString(), expectedOrigin, StringComparison.Ordinal)) { ctx.Response.StatusCode = 403; return; }
            var path = ctx.Request.Path.Value ?? "";
            var api = path.StartsWith("/api/", StringComparison.Ordinal) || path.StartsWith("/hubs/", StringComparison.Ordinal);
            var anonymous = !admin && path is "/api/v1/pairing/requests" or "/api/v1/pairing/claim";
            // Admin credentials are never accepted on the LAN listener and vice versa.
            var lease = trust.Authenticate(ctx.Request.Cookies["lc_session"], admin);
            using var cancelled = lease is not null && !lease.Desktop ? CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted,deviceRequests.GetOrAdd(lease.DeviceId,_=>new()).Token) : null;
            if(cancelled is not null){var remaining=lease!.ExpiresAt-DateTimeOffset.UtcNow;cancelled.CancelAfter(remaining>TimeSpan.Zero?remaining:TimeSpan.Zero);ctx.RequestAborted=cancelled.Token;}
            if (api && !anonymous)
            {
                if (lease is null) { ctx.Response.StatusCode = 401; return; }
                ctx.Items["lease"] = lease;
                // SignalR negotiate carries no command and uses the same authenticated,
                // exact-origin cookie; commands live only in CSRF-protected REST routes.
                if (mutating && !path.StartsWith("/hubs/", StringComparison.Ordinal) && !trust.ValidateCsrf(lease, ctx.Request.Headers["X-LC-CSRF"].ToString())) { ctx.Response.StatusCode = 403; return; }
            }
            if(path=="/api/v1/clipboard/write"&&lease?.Allows(Permissions.ClipboardWrite)==true) {
                var limit=ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if(limit is not null&&!limit.IsReadOnly)limit.MaxRequestBodySize=400000; // 64K UTF-16 chars serialized with JSON escaping.
            }
            try { await next(ctx); }
            catch (ControlException ex) { if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = ex.Status; await ctx.Response.WriteAsJsonAsync(new { code = ex.Code, message = ex.Message }); } }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { }
            catch (Exception exception)
            {
                var reference = Guid.NewGuid().ToString("N")[..8];
                Diagnostic?.Invoke(reference, exception);
                if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 500; await ctx.Response.WriteAsJsonAsync(new { code = "operation_failed", reference, message = $"Операция не выполнена. Диагностика: {reference}." }); }
            }
        });
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { ready = true }));
        app.MapGet("/api/v1/session", (HttpContext ctx) =>
        {
            var l = (Lease)ctx.Items["lease"]!;
            return Results.Ok(new { l.Csrf, l.Permissions, l.Desktop, l.ExpiresAt });
        });
        app.MapPost("/api/v1/session/logout", (HttpContext ctx) => { var session=(Lease)ctx.Items["lease"]!; trust.Logout(ctx.Request.Cookies["lc_session"]); if(!session.Desktop)Revoke(session.DeviceId); ctx.Response.Cookies.Delete("lc_session"); connections.Valid(trust); return Results.NoContent(); });
        app.MapGet("/api/v1/state", async (HttpContext ctx) => { Require(ctx, Permissions.Status); var state=await computer.ReadState(ctx.RequestAborted);return Results.Ok(state with{Machine=state.Machine with{Name=configuration.Get().ComputerName}}); });
        app.MapGet("/api/v1/apps", async (HttpContext ctx) => { Require(ctx, Permissions.Launch); return Results.Ok(await computer.GetApps(ctx.RequestAborted)); });
        app.MapPost("/api/v1/apps/{id}/launch", async (string id, HttpContext ctx) => { Require(ctx, Permissions.Launch); await computer.Launch(id, ctx.RequestAborted); return Results.NoContent(); });
        app.MapPut("/api/v1/audio/{id}", async (string id, VolumeChange change, HttpContext ctx) =>
        {
            Require(ctx, Permissions.Audio);
            if (!change.IsValid) throw new ControlException("invalid_audio", "Громкость должна быть от 0 до 100%.");
            await computer.ChangeAudio(id, change, ctx.RequestAborted);
            return Results.NoContent();
        });
        app.MapHub<StateHub>("/hubs/state");
        MapExtended(app,admin);
        if (admin)
        {
            app.MapGet("/api/v1/desktop/network", () => Results.Ok(new { url = LanUrl, addresses = LanAddresses(), port = listeningPort }));
            app.MapPut("/api/v1/desktop/network", async (NetworkChange value, HttpContext ctx) => { await SetNetwork(value.Address, ctx.RequestAborted,value.Port); return Results.Ok(new { url = LanUrl }); });
            app.MapPost("/api/v1/desktop/pairing/open", () =>
            {
                if (LanUrl is null) throw new ControlException("lan_off", "Сначала включите LAN.", 409);
                var invitation = trust.OpenPairing();
                return Results.Ok(new { url = $"{LanUrl}/#invite={invitation.Token}", invitation.ExpiresAt });
            });
            app.MapGet("/api/v1/desktop/pairing/pending", () => Results.Ok(trust.Pending()));
            app.MapPost("/api/v1/desktop/pairing/{id}/decide", (string id, Decision decision) => { trust.Decide(id, decision.Approve, decision.Permissions); return Results.NoContent(); });
            app.MapGet("/api/v1/desktop/devices", () => Results.Ok(trust.Devices()));
            app.MapDelete("/api/v1/desktop/devices/{id}", (string id) => { trust.Revoke(id); return Results.NoContent(); });
        }
        else
        {
            app.MapPost("/api/v1/pairing/requests", (PairRequest request, HttpContext ctx) =>
            {
                if (request.Invite is null || request.Nonce is null || request.Label is null) throw new ControlException("invalid_pairing", "Заполните данные подключения.");
                return Results.Ok(trust.RequestPair(request.Invite, request.Nonce, request.Label, ctx.Connection.RemoteIpAddress!.ToString()));
            }).RequireRateLimiting("pairing");
            app.MapPost("/api/v1/pairing/claim", (ClaimRequest request, HttpContext ctx) =>
            {
                if (request.Id is null || request.Secret is null) throw new ControlException("claim_invalid", "Недействительный запрос.", 403);
                var claim = trust.Claim(request.Id, request.Secret);
                if (claim.Credential is not null) ctx.Response.Cookies.Append("lc_session", claim.Credential, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = false, Path = "/", IsEssential = true });
                return Results.Ok(new { status = claim.Status });
            });
        }
        app.UseDefaultFiles();
        app.UseStaticFiles();
        // Leave unmatched requests to the framework's 404 handler. A fallback
        // endpoint would match '/' before UseDefaultFiles and prevent static
        // middleware from serving index.html. Unknown API routes remain 404.
        return app;
    }
    private static void Require(HttpContext ctx, string permission)
    { if (ctx.Items["lease"] is not Lease lease || !lease.Allows(permission)) throw new ControlException("permission_denied", "Доступ к этой функции не разрешён на ПК.", 403); }
    private async Task Pump(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                var a = desktopConnections.Valid(trust); var b = lanConnections.Valid(trust);
                if (a.Length + b.Length == 0) continue;
                try
                {
                    var state = await computer.ReadState(token);state=state with{Machine=state.Machine with{Name=configuration.Get().ComputerName}};
                    if (desktop is not null && a.Length > 0) await desktop.Services.GetRequiredService<IHubContext<StateHub>>().Clients.Clients(a).SendAsync("state", state, token);
                    var current = lan;
                    if (current is not null && b.Length > 0) await current.Services.GetRequiredService<IHubContext<StateHub>>().Clients.Clients(b).SendAsync("state", state, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch { /* Polling clients can retry; never terminate the tray on transient device loss. */ }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        trust.Revoked -= Revoke;
        if (pumpStop is not null) { await pumpStop.CancelAsync(); if (pump is not null) await pump; pumpStop.Dispose(); }
        if (lan is not null) { await lan.StopAsync(); await lan.DisposeAsync(); }
        if (desktop is not null) { await desktop.StopAsync(); await desktop.DisposeAsync(); }
        networkGate.Dispose();
    }
}
