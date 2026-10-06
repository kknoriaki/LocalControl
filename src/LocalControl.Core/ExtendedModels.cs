namespace LocalControl.Core;

public sealed record CustomApp(string? Id, string Name, string Executable, string Arguments = "", string WorkingDirectory = "", string? Icon = null);
public sealed record AppEdit(bool Favorite, string? Name);
public sealed record HardwareInfo(string Windows, string User, string Cpu, string[] Gpus, string Motherboard, string PowerPlan, NetworkInfo[] Networks, MonitorInfo[] Monitors, string[] Warnings);
public sealed record NetworkInfo(string Name, string Kind, string[] Addresses, long ReceivedBytes, long SentBytes);
public sealed record MonitorInfo(string Id, string Label, int Width, int Height, bool Primary);
public sealed record StartupItem(string Id, string Name, string Source, string Command, bool Enabled, bool CanEdit, string? Note);
public sealed record RemoteInput(string Kind, double X = 0, double Y = 0, int Delta = 0, string? Key = null, string? Text = null);
public sealed record MediaInfo(string Id, string Source, string Title, string Artist, string Status);
public sealed record HotkeyDefinition(string Id, string Label, int[] Keys);
public sealed record PowerCapabilities(bool Sleep, bool Hibernate);
public interface IExtendedComputer : IComputer
{
    Task<AppItem[]> Rescan(CancellationToken token);
    Task AddApp(CustomApp app, CancellationToken token);
    Task EditApp(string id, AppEdit edit, CancellationToken token);
    Task<byte[]?> AppIcon(string id, CancellationToken token);
    Task CloseApp(string id, bool force, CancellationToken token);
    Task RestartApp(string id, CancellationToken token);
    Task<HardwareInfo> HardwareInfo(CancellationToken token);
    Task<StartupItem[]> Startup(CancellationToken token);
    Task SetStartup(string id, bool enabled, CancellationToken token);
    Task SetOwnStartup(bool enabled, CancellationToken token);
    Task<PowerCapabilities> PowerCapabilities(CancellationToken token);
    Task PowerAction(string action, CancellationToken token);
    Task<string> ReadClipboard(CancellationToken token);
    Task WriteClipboard(string text, CancellationToken token);
    Task<MonitorInfo[]> Monitors(CancellationToken token);
    Task<byte[]> Capture(string monitor, CancellationToken token);
    Task Input(string monitor, RemoteInput input, CancellationToken token);
    Task<MediaInfo[]> Media(CancellationToken token);
    Task MediaAction(string id, string action, CancellationToken token);
    Task ConfigureHotkey(HotkeyDefinition hotkey, CancellationToken token);
    Task<HotkeyDefinition[]> Hotkeys(CancellationToken token);
    Task SendHotkey(string id, CancellationToken token);
}
