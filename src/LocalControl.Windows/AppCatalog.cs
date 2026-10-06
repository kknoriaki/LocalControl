using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using LocalControl.Core;
using Microsoft.Win32;

namespace LocalControl.Windows;

internal sealed class AppCatalog
{
    private sealed record Registered(AppItem Public, string Target, bool Steam = false, string? Executable = null, string? Directory = null);
    private sealed record Preferences(CustomApp[] Custom, Dictionary<string, AppEdit> Edits);
    private readonly string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalControl", "apps.json");
    private Preferences preferences = new([], []);
    private readonly Dictionary<string, Registered> apps = [];
    private bool scanned;
    public AppItem[] List()
    {
        if (!scanned) { Scan(); scanned = true; }
        var snapshot = ProcessSnapshot();
        return apps.Values.Select(x => {
            var target = x.Executable ?? (x.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x.Target : null);
            var matches = snapshot.Where(p => target is not null && p.Path.Equals(target,StringComparison.OrdinalIgnoreCase) || x.Directory is not null && p.Path.StartsWith(Path.TrimEndingDirectorySeparator(x.Directory) + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).ToArray();
            return x.Public with { Running = target is null && x.Directory is null ? null : matches.Length > 0, Pids = matches.Select(p => p.Id).ToArray() };
        }).OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    private void Add(string label, string source, string target, bool steam = false)
    {
        if (apps.Count >= 4000 || label.Length == 0) return;
        var id = Identifier(source, target);
        var edit = preferences.Edits.GetValueOrDefault(id);
        apps.TryAdd(id, new(new(id, string.IsNullOrWhiteSpace(edit?.Name) ? label : edit.Name, source, Favorite: edit?.Favorite ?? false, Category: steam ? "Games" : source == "Custom" ? "Custom" : "Apps"), target, steam));
    }
    private void Scan()
    {
        if (File.Exists(settingsFile)) preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(settingsFile)) ?? new([], []);
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
        {
            if (!Directory.Exists(root)) continue;
            // Reparse points are skipped; discovery never follows a shortcut as a directory.
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 8 };
                foreach (var path in Directory.EnumerateFiles(root, "*.lnk", options).Take(4000))
                    {
                    Add(Path.GetFileNameWithoutExtension(path), "Start Menu", path);
                    var id = Identifier("Start Menu", path);
                    if (apps.TryGetValue(id, out var app)) apps[id] = app with { Executable = ShortcutTarget(path) };
                }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var registry = RegistryKey.OpenBaseKey(hive, view);
            using var paths = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
            if (paths is null) continue;
            foreach (var name in paths.GetSubKeyNames().Take(2000))
            {
                using var entry = paths.OpenSubKey(name);
                if (entry?.GetValue(null) is not string raw) continue;
                var target = Environment.ExpandEnvironmentVariables(raw).Trim().Trim('"');
                if (Path.IsPathFullyQualified(target) && Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                    Add(Path.GetFileNameWithoutExtension(name), "App Paths", target);
            }
        }
        ScanInstalled();
        ScanSteam();
        foreach (var app in preferences.Custom) Add(app.Name, "Custom", app.Executable);
        foreach (var custom in preferences.Custom) {
            var id = Identifier("Custom", custom.Executable);
            if(apps.TryGetValue(id, out var app)) apps[id] = app with { Executable = custom.Executable };
        }
    }
    private static string? ReadBounded(string path)
    { try { return File.Exists(path) && new FileInfo(path).Length <= 262144 ? File.ReadAllText(path) : null; } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; } }
    private static string? VdfValue(string text, string key)
    {
        var m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return m.Success ? m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"") : null;
    }
    private void ScanSteam()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (key?.GetValue("SteamPath") is not string steam || !Directory.Exists(steam)) return;
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam };
        var libraries = ReadBounded(Path.Combine(steam, "steamapps", "libraryfolders.vdf"));
        if (libraries is not null)
            foreach (Match match in Regex.Matches(libraries, "\"path\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                roots.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
        foreach (var root in roots.Take(32))
        {
            var folder = Path.Combine(root, "steamapps");
            if (!Directory.Exists(folder)) continue;
            try
            {
                foreach (var manifest in Directory.EnumerateFiles(folder, "appmanifest_*.acf").Take(4000))
                {
                    var text = ReadBounded(manifest); if (text is null) continue;
                    var id = VdfValue(text, "appid"); var name = VdfValue(text, "name");
                    if (id is not null && name is not null && uint.TryParse(id, out var appId) && appId > 0)
                        {
                        var uri = $"steam://run/{appId}"; Add(name, "Steam", uri, true);
                        var catalogId = Identifier("Steam", uri); var install = VdfValue(text, "installdir");
                        if (install is not null && Path.GetFileName(install) == install && apps.TryGetValue(catalogId, out var app))
                            apps[catalogId] = app with { Directory = Path.Combine(folder, "common", install) };
                    }
                }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    public void Launch(string id)
    {
        List();
        if (!apps.TryGetValue(id, out var app)) throw new ControlException("app_unknown", "Приложение не зарегистрировано на ПК.", 404);
        if (!app.Steam && !File.Exists(app.Target)) throw new ControlException("app_removed", "Приложение удалено или перемещено.", 404);
        try {
            var custom = preferences.Custom.FirstOrDefault(x => x.Executable.Equals(app.Target, StringComparison.OrdinalIgnoreCase));
            var info = new ProcessStartInfo(app.Target) { UseShellExecute = true };
            if(custom is not null) { info.Arguments = custom.Arguments; info.WorkingDirectory = custom.WorkingDirectory; }
            Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception) { throw new ControlException("launch_failed", "Windows не удалось запустить приложение.", 409); }
    }
    private static string Identifier(string source, string target) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source + "|" + target.ToUpperInvariant())))[..32];
    private static string? ShortcutTarget(string path)
    {
        object? shell = null, shortcut = null;
        try {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(path);
            string target = ((dynamic)shortcut).TargetPath;
            return File.Exists(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target : null;
        } catch (COMException) { return null; }
        finally { if(shortcut is not null) Marshal.FinalReleaseComObject(shortcut); if(shell is not null) Marshal.FinalReleaseComObject(shell); }
    }
    private void ScanInstalled()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) {
            using var registry = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if(uninstall is null) continue;
            foreach(var key in uninstall.GetSubKeyNames().Take(2000)) {
                using var entry = uninstall.OpenSubKey(key);
                if(entry?.GetValue("DisplayName") is not string label || entry.GetValue("DisplayIcon") is not string icon) continue;
                var candidate = Environment.ExpandEnvironmentVariables(icon.Split(',')[0].Trim().Trim('"'));
                if(Path.IsPathFullyQualified(candidate) && candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate)) {
                    Add(label, "Installed", candidate); var id=Identifier("Installed",candidate);
                    if(apps.TryGetValue(id,out var app)) apps[id] = app with { Public = app.Public with { Publisher = entry.GetValue("Publisher") as string } };
                }
            }
        }
    }
    private static (int Id,string Path)[] ProcessSnapshot()
    {
        var list = new List<(int,string)>();
        foreach(var process in Process.GetProcesses()) using(process) try { if(process.MainModule?.FileName is string path) list.Add((process.Id,path)); } catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
        return list.ToArray();
    }
    private static IEnumerable<Process> MatchingProcesses(Registered app)
    {
        var executable = app.Executable ?? (app.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? app.Target : null);
        foreach(var process in Process.GetProcesses()) {
            string? path = null;
            try { path = process.MainModule?.FileName; } catch (Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            if(path is not null && (executable is not null && path.Equals(executable,StringComparison.OrdinalIgnoreCase) || app.Directory is not null && path.StartsWith(Path.TrimEndingDirectorySeparator(app.Directory) + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))) yield return process;
            else process.Dispose();
        }
    }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        var temp = settingsFile + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(preferences)); File.Move(temp,settingsFile,true);
    }
    public AppItem[] Rescan() { apps.Clear(); scanned=false; return List(); }
    public void AddCustom(CustomApp app)
    {
        if(string.IsNullOrWhiteSpace(app.Name) || app.Name.Length > 100 || !Path.IsPathFullyQualified(app.Executable) || !app.Executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase) || !File.Exists(app.Executable) || app.Arguments.Length > 2048 || app.WorkingDirectory.Length > 1024 || app.WorkingDirectory.Length > 0 && !Directory.Exists(app.WorkingDirectory)) throw new ControlException("invalid_app","Выберите существующий exe и рабочую папку.");
        var list=preferences.Custom.Where(x=>!x.Executable.Equals(app.Executable,StringComparison.OrdinalIgnoreCase)).Append(app).ToArray();
        if(list.Length>100) throw new ControlException("custom_limit","Слишком много пользовательских приложений.");
        preferences=preferences with {Custom=list};Save();Rescan();
    }
    public void Edit(string id,AppEdit edit)
    {
        List();if(!apps.ContainsKey(id))throw new ControlException("app_unknown","Приложение не найдено.",404);
        if(edit.Name?.Length>100)throw new ControlException("invalid_name","Название слишком длинное.");
        preferences.Edits[id]=edit;Save();Rescan();
    }
    public void Close(string id,bool force)
    {
        List();if(!apps.TryGetValue(id,out var app))throw new ControlException("app_unknown","Приложение не найдено.",404);
        var processes=MatchingProcesses(app).ToArray();
        try {
            if(processes.Any(p=>p.Id==Environment.ProcessId))throw new ControlException("self_close","LocalControl завершается только из tray.",403);
            var targets=force?processes:processes.Where(p=>!p.HasExited&&p.MainWindowHandle!=IntPtr.Zero).ToArray();
            if(targets.Length==0)throw new ControlException("no_main_window","Нет доступного окна. Принудительное закрытие — отдельное действие.",409);
            foreach(var process in targets)if(!process.HasExited){if(force)process.Kill(false);else if(!process.CloseMainWindow())throw new ControlException("close_failed","Windows отклонила закрытие окна.",409);}
        } finally {foreach(var process in processes)process.Dispose();}
    }
    public void Restart(string id)
    {
        List();if(!apps.TryGetValue(id,out var app))throw new ControlException("app_unknown","Приложение не найдено.",404);
        var processes=MatchingProcesses(app).ToArray();
        try {
            if(processes.Any(p=>p.Id==Environment.ProcessId))throw new ControlException("self_close","LocalControl завершается только из tray.",403);
            var windows=processes.Where(p=>!p.HasExited&&p.MainWindowHandle!=IntPtr.Zero).ToArray();
            if(processes.Length>0&&windows.Length==0)throw new ControlException("restart_wait","Сначала завершите приложение с фоновой службой.",409);
            foreach(var process in windows)if(!process.CloseMainWindow())throw new ControlException("restart_wait","Windows отклонила закрытие окна.",409);
            foreach(var process in windows)if(!process.WaitForExit(3000))throw new ControlException("restart_wait","Закройте диалоги сохранения; приложение не завершилось.",409);
        } finally {foreach(var process in processes)process.Dispose();}
        Launch(id);
    }
    public byte[]? Icon(string id)
    {
        List();if(!apps.TryGetValue(id,out var app))return null;
        var custom=preferences.Custom.FirstOrDefault(x=>x.Executable.Equals(app.Target,StringComparison.OrdinalIgnoreCase));
        try {
            using var icon=System.Drawing.Icon.ExtractAssociatedIcon(custom?.Icon ?? app.Executable ?? app.Target);
            if(icon is null)return null;using var bitmap=icon.ToBitmap();using var stream=new MemoryStream();bitmap.Save(stream,ImageFormat.Png);return stream.ToArray();
        } catch (Exception ex) when(ex is ArgumentException or IOException or System.ComponentModel.Win32Exception) { return null; }
    }

}
