namespace LocalControl.Core;

public static class Permissions
{
    public const string Status = "status.read", Audio = "audio.control", Launch = "apps.launch", Close = "apps.close", Power = "power.control", RemoteView = "remote.view", RemoteInput = "remote.input", ClipboardRead = "clipboard.read", ClipboardWrite = "clipboard.write", TransferSend = "transfer.send", TransferReceive = "transfer.receive", Media = "media.control", Hotkey = "hotkey.invoke";
    public static readonly string[] Default = [Status, Audio, Launch];
    public static readonly string[] All = [..Default, Close, Power, RemoteView, RemoteInput, ClipboardRead, ClipboardWrite, TransferSend, TransferReceive, Media, Hotkey];
    public static bool Valid(IEnumerable<string> values)
    { var set = values.ToHashSet(); return set.All(All.Contains) && (!set.Contains(RemoteInput) || set.Contains(RemoteView)); }
}
public sealed record Device(string Id, string Label, string[] Permissions, DateTimeOffset ApprovedAt);
public sealed record Lease(string DeviceId, string[] Permissions, string Csrf, DateTimeOffset ExpiresAt, bool Desktop = false)
{
    public bool Allows(string permission) => Desktop || Permissions.Contains(permission);
}
public sealed record PairTicket(string Id, string Secret, string Code, DateTimeOffset ExpiresAt);
public sealed record PendingPair(string Id, string Label, string Code, string Peer, DateTimeOffset ExpiresAt);
public sealed record AudioItem(string Id, string Label, string Kind, float Volume, bool Muted, bool IsDefault);
public sealed record AppItem(string Id, string Label, string Source, bool? Running = null, bool Favorite = false, string Category = "Apps", int[]? Pids = null, string? Publisher = null);
public sealed record DiskItem(string Label, long TotalBytes, long FreeBytes);
public sealed record MachineState(string Name, double? CpuPercent, ulong MemoryTotalBytes, ulong MemoryUsedBytes, long UptimeSeconds, DiskItem[] Disks);
public sealed record StateSnapshot(long Revision, DateTimeOffset At, MachineState Machine, AudioItem[] Audio, string[] Warnings);
public sealed record VolumeChange(float? Volume, bool? Muted)
{
    public bool IsValid => (Volume.HasValue || Muted.HasValue) && (!Volume.HasValue || float.IsFinite(Volume.Value) && Volume.Value is >= 0 and <= 1);
}
public interface IComputer : IAsyncDisposable
{
    Task<StateSnapshot> ReadState(CancellationToken token);
    Task ChangeAudio(string id, VolumeChange change, CancellationToken token);
    Task<AppItem[]> GetApps(CancellationToken token);
    Task Launch(string id, CancellationToken token);
}
public sealed class ControlException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
