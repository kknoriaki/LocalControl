using System.Text.Json;
using LocalControl.Core;
using Microsoft.Win32;

namespace LocalControl.Windows;

public sealed partial class WindowsComputer
{
    private readonly StartupManager startupManager=new();
    private readonly MediaManager mediaManager=new();
    private readonly string hotkeyFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalControl","hotkeys.json");
    private HardwareInfo? hardwareInfo;
    private DateTimeOffset hardwareAt;
    public Task<AppItem[]> Rescan(CancellationToken token)=>Invoke(catalog.Rescan,token);
    public Task AddApp(CustomApp app,CancellationToken token)=>Invoke(()=>{catalog.AddCustom(app);return true;},token);
    public Task EditApp(string id,AppEdit edit,CancellationToken token)=>Invoke(()=>{catalog.Edit(id,edit);return true;},token);
    public Task<byte[]?> AppIcon(string id,CancellationToken token)=>Invoke(()=>catalog.Icon(id),token);
    public Task CloseApp(string id,bool force,CancellationToken token)=>Invoke(()=>{catalog.Close(id,force);return true;},token);
    public Task RestartApp(string id,CancellationToken token)=>Invoke(()=>{catalog.Restart(id);return true;},token);
    public Task<HardwareInfo> HardwareInfo(CancellationToken token)=>Invoke(()=>{
        if(hardwareInfo is null||DateTimeOffset.UtcNow-hardwareAt>TimeSpan.FromMinutes(5)){hardwareInfo=DesktopControl.Hardware();hardwareAt=DateTimeOffset.UtcNow;}return hardwareInfo;
    },token);
    public Task<StartupItem[]> Startup(CancellationToken token)=>Invoke(startupManager.List,token);
    public Task SetStartup(string id,bool enabled,CancellationToken token)=>Invoke(()=>{startupManager.Set(id,enabled);return true;},token);
    public Task SetOwnStartup(bool enabled,CancellationToken token)=>Invoke(()=>{
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled)key.SetValue("LocalControl","\""+Environment.ProcessPath+"\" --tray");else key.DeleteValue("LocalControl",false);return true;
    },token);
    public Task<PowerCapabilities> PowerCapabilities(CancellationToken token)=>Invoke(DesktopControl.Capabilities,token);
    public Task PowerAction(string action,CancellationToken token)=>Invoke(()=>{DesktopControl.Power(action);return true;},token);
    public Task<string> ReadClipboard(CancellationToken token)=>Invoke(()=>DesktopControl.ClipboardSta(()=>Clipboard.ContainsText()?Clipboard.GetText():""),token);
    public Task WriteClipboard(string text,CancellationToken token)=>Invoke(()=>{
        if(text.Length>65536)throw new ControlException("clipboard_large","Текст ограничен 64 КБ.");
        return DesktopControl.ClipboardSta(()=>{if(text.Length==0)Clipboard.Clear();else Clipboard.SetText(text);return true;});
    },token);
    public Task<MonitorInfo[]> Monitors(CancellationToken token)=>Invoke(DesktopControl.Monitors,token);
    public Task<byte[]> Capture(string monitor,CancellationToken token)=>Invoke(()=>DesktopControl.Capture(monitor),token);
    public Task Input(string monitor,RemoteInput input,CancellationToken token)=>Invoke(()=>{DesktopControl.InputAction(monitor,input);return true;},token);
    public Task<MediaInfo[]> Media(CancellationToken token)=>mediaManager.List(token);
    public Task MediaAction(string id,string action,CancellationToken token)=>mediaManager.Action(id,action,token);
    public Task<HotkeyDefinition[]> Hotkeys(CancellationToken token)=>Invoke(()=>File.Exists(hotkeyFile)?JsonSerializer.Deserialize<HotkeyDefinition[]>(File.ReadAllText(hotkeyFile))??[]:[],token);
    public Task ConfigureHotkey(HotkeyDefinition hotkey,CancellationToken token)=>Invoke(()=>{
        if(hotkey.Id is not ("discord-mute" or "discord-deafen")||hotkey.Keys.Length is <2 or >4||hotkey.Keys.Any(x=>x is <8 or >165)||!hotkey.Keys.Any(x=>x is 16 or 17 or 18))throw new ControlException("invalid_hotkey","Настройте Discord global keybind из 2–4 клавиш с Ctrl/Alt/Shift.");
        var previous=File.Exists(hotkeyFile)?JsonSerializer.Deserialize<HotkeyDefinition[]>(File.ReadAllText(hotkeyFile))??[]:[];
        Directory.CreateDirectory(Path.GetDirectoryName(hotkeyFile)!);File.WriteAllText(hotkeyFile+".tmp",JsonSerializer.Serialize(previous.Where(x=>x.Id!=hotkey.Id).Append(hotkey).ToArray()));File.Move(hotkeyFile+".tmp",hotkeyFile,true);return true;
    },token);
    public async Task SendHotkey(string id,CancellationToken token)
    { var definition=(await Hotkeys(token)).FirstOrDefault(x=>x.Id==id)??throw new ControlException("hotkey_missing","Сначала настройте Discord global keybind на ПК.",404);await Invoke(()=>{DesktopControl.Hotkey(definition.Keys);return true;},token); }
}
