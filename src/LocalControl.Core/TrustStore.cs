using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalControl.Core;

// All credentials and pending requests are memory-only. Device records survive
// restart, but HTTP authentication always requires a fresh desktop approval.
public sealed class TrustStore
{
    private sealed record Request(PendingPair Public, string SecretHash, string Nonce, string InviteHash, string? DeviceId = null, bool Rejected = false);
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly string file;
    private readonly Dictionary<string, Lease> leases = [];
    private readonly Dictionary<string, Request> pending = [];
    private readonly Dictionary<string, Device> devices = [];
    private string? inviteHash;
    private DateTimeOffset inviteExpiry;
    public event Action<string>? Revoked;
    public TrustStore(string directory, TimeProvider? time = null)
    {
        clock = time ?? TimeProvider.System;
        Directory.CreateDirectory(directory);
        file = Path.Combine(directory, "devices.json");
        if (File.Exists(file))
        {
            var list = JsonSerializer.Deserialize<Device[]>(File.ReadAllText(file)) ?? [];
            foreach (var d in list)
            {
                if (!Permissions.Valid(d.Permissions)) throw new InvalidDataException("Unsupported permissions in device store.");
                devices.Add(d.Id, d);
            }
        }
    }
    private DateTimeOffset Now => clock.GetUtcNow();
    public static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Equal(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    private void Prune()
    {
        foreach (var key in pending.Where(x => x.Value.Public.ExpiresAt <= Now).Select(x => x.Key).ToArray()) pending.Remove(key);
        foreach (var key in leases.Where(x => x.Value.ExpiresAt <= Now).Select(x => x.Key).ToArray()) leases.Remove(key);
    }
    public (string Token, DateTimeOffset ExpiresAt) OpenPairing()
    {
        lock (gate)
        {
            pending.Clear();
            var value = Token();
            inviteHash = Hash(value);
            inviteExpiry = Now.AddMinutes(2);
            return (value, inviteExpiry);
        }
    }
    public void ClosePairing() { lock (gate) { inviteHash = null; pending.Clear(); } }
    public PairTicket RequestPair(string invite, string nonce, string label, string peer)
    {
        if (invite.Length != 64 || nonce.Length is < 32 or > 128 || label.Length is < 1 or > 80 || label.Any(char.IsControl))
            throw new ControlException("invalid_pairing", "Некорректные данные подключения.");
        lock (gate)
        {
            Prune();
            if (inviteHash is null || Now >= inviteExpiry || !Equal(inviteHash, Hash(invite)))
                throw new ControlException("invite_expired", "Откройте подключение телефона на ПК.", 403);
            if (pending.Count >= 8) throw new ControlException("pairing_busy", "Слишком много запросов подключения.", 429);
            if (pending.Values.Any(x => x.Nonce == nonce)) throw new ControlException("nonce_used", "Запрос уже создан.", 409);
            var id = Guid.NewGuid().ToString("N");
            var secret = Token();
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            pending[id] = new(new(id, label, code, peer, inviteExpiry), Hash(secret), nonce, inviteHash);
            return new(id, secret, code, inviteExpiry);
        }
    }
    public PendingPair[] Pending() { lock (gate) { Prune(); return pending.Values.Where(x => x.DeviceId is null && !x.Rejected).Select(x => x.Public).ToArray(); } }
    public Device[] Devices() { lock (gate) return devices.Values.ToArray(); }
    public void Decide(string id, bool approve, string[] permissions)
    {
        lock (gate)
        {
            Prune();
            if (!pending.TryGetValue(id, out var request) || request.DeviceId is not null || request.Rejected)
                throw new ControlException("pairing_gone", "Запрос уже обработан или истёк.", 404);
            if (!approve) { pending[id] = request with { Rejected = true }; return; }
            if (!Permissions.Valid(permissions) || !permissions.Contains(Permissions.Status)) throw new ControlException("invalid_permissions", "Нужен доступ к состоянию ПК.");
            if (devices.Count >= 32) throw new ControlException("device_limit", "Удалите неиспользуемое устройство.", 409);
            var device = new Device(Guid.NewGuid().ToString("N"), request.Public.Label, permissions.Distinct().ToArray(), Now);
            devices.Add(device.Id, device);
            try { Save(); } catch { devices.Remove(device.Id); throw; }
            pending[id] = request with { DeviceId = device.Id };
        }
    }
    public (string Status, string? Credential, Lease? Lease) Claim(string id, string secret)
    {
        if (secret.Length != 64) throw new ControlException("claim_invalid", "Недействительный запрос.", 403);
        lock (gate)
        {
            Prune();
            if (!pending.TryGetValue(id, out var request) || !Equal(request.SecretHash, Hash(secret)))
                throw new ControlException("claim_invalid", "Запрос истёк или уже использован.", 403);
            if (request.Rejected) { pending.Remove(id); return ("rejected", null, null); }
            if (request.DeviceId is null) return ("pending", null, null);
            pending.Remove(id); // Single use under the same lock as lease creation.
            var device = devices[request.DeviceId];
            var credential = Token();
            var lease = new Lease(device.Id, device.Permissions, Token(), Now.AddMinutes(30));
            leases[Hash(credential)] = lease;
            return ("approved", credential, lease);
        }
    }
    public (string Credential, Lease Lease) CreateDesktop()
    {
        lock (gate)
        {
            var value = Token();
            var lease = new Lease("desktop", Permissions.Default, Token(), DateTimeOffset.MaxValue, true);
            leases[Hash(value)] = lease;
            return (value, lease);
        }
    }
    public Lease? Authenticate(string? credential, bool desktopListener)
    {
        if (credential?.Length != 64) return null;
        lock (gate)
        {
            Prune();
            if (!leases.TryGetValue(Hash(credential), out var lease) || lease.Desktop != desktopListener) return null;
            if (!lease.Desktop && !devices.ContainsKey(lease.DeviceId)) return null;
            return lease;
        }
    }
    public bool StillValid(Lease lease) { lock (gate) return lease.ExpiresAt > Now && leases.Values.Any(x => ReferenceEquals(x, lease)) && (lease.Desktop || devices.ContainsKey(lease.DeviceId)); }
    public bool ValidateCsrf(Lease lease, string token) => token.Length == 64 && Equal(lease.Csrf, token);
    public void Logout(string? credential) { if (credential is not null) lock (gate) leases.Remove(Hash(credential)); }
    public void Revoke(string id)
    {
        lock (gate)
        {
            if (!devices.Remove(id, out var device)) return;
            try { Save(); } catch { devices[id] = device; throw; }
            foreach (var key in leases.Where(x => x.Value.DeviceId == id).Select(x => x.Key).ToArray()) leases.Remove(key);
            foreach (var key in pending.Where(x => x.Value.DeviceId == id).Select(x => x.Key).ToArray()) pending.Remove(key);
        }
        Revoked?.Invoke(id);
    }
    public void DropPhoneSessions() { lock (gate) { foreach (var k in leases.Where(x => !x.Value.Desktop).Select(x => x.Key).ToArray()) leases.Remove(k); pending.Clear(); inviteHash = null; } }
    private void Save()
    {
        var temp = file + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(stream, devices.Values.ToArray()); stream.Flush(true); }
        File.Move(temp, file, true);
    }
}
