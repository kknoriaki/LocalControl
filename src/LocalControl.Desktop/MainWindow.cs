using LocalControl.Core;
using LocalControl.Server;
using LocalControl.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Text.Json;

namespace LocalControl.Desktop;

internal sealed class MainWindow : Form
{
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private readonly NotifyIcon tray;
    private readonly WindowsComputer computer = new();
    private readonly string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalControl");
    private ControlHost? host;
    private bool exiting, stopping;
    private Task<bool>? startup;
    public MainWindow(bool startInTray=false)
    {
        Text = "LocalControl";
        Width = 1280; Height = 850; MinimumSize = new Size(820, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(14, 15, 17);
        Controls.Add(web);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть LocalControl", null, (_, _) => ShowWindow());
        menu.Items.Add("Выключить LAN", null, async (_, _) =>
        {
            if (host is null) return;
            try { await host.SetNetwork(null, CancellationToken.None); tray.ShowBalloonTip(2000, "LocalControl", "LAN выключен, телефонные сессии завершены.", ToolTipIcon.Info); }
            catch { MessageBox.Show("Не удалось выключить LAN. Завершите LocalControl через меню tray.", "LocalControl"); }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, async (_, _) => await Exit());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "LocalControl", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();
        Shown += async (_, _) => { startup = Start(); if (!await startup) await Exit(); else if(startInTray && new SettingsStore(data).Get().Onboarded)Hide(); };
        FormClosing += (_, e) =>
        {
            if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing) { exiting = true; return; }
            if (!exiting) { e.Cancel = true; Hide(); }
        };
    }
    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void Diagnostic(string reference, Exception exception)
    {
        try
        {
            var directory = Path.Combine(data, "logs"); Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "diagnostics.log");
            if (File.Exists(file) && new FileInfo(file).Length > 1048576) File.Move(file, file + ".old", true);
            // Exception messages and stacks can contain paths or credentials.
            // Persist only a reference, exception type and native error number.
            File.AppendAllText(file, $"{DateTimeOffset.UtcNow:O} {reference} {exception.GetType().Name} {exception.HResult:X8}{Environment.NewLine}");
        }
        catch { }
    }
    private async Task<bool> Start()
    {
        try
        {
            Directory.CreateDirectory(data);
            var trust = new TrustStore(data);
            host = new ControlHost(computer, trust, Path.Combine(AppContext.BaseDirectory, "wwwroot"));
            host.Diagnostic += Diagnostic;
            host.ApplyUpdate = ApplyUpdate;
            await host.Start();
            if (stopping) return true;
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(data, "WebView2"));
            await web.EnsureCoreWebView2Async(environment);
            if (stopping) return true;
            var settings = web.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsWebMessageEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsStatusBarEnabled = false;
            web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != host.DesktopUrl) e.Cancel = true;
            };
            web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            web.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            web.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
            var credentials = trust.CreateDesktop();
            web.CoreWebView2.CookieManager.DeleteAllCookies();
            var cookie = web.CoreWebView2.CookieManager.CreateCookie("lc_session", credentials.Credential, "127.0.0.1", "/");
            cookie.IsHttpOnly = true; cookie.IsSecure = false; cookie.SameSite = CoreWebView2CookieSameSiteKind.Strict;
            web.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
            web.Source = new Uri(host.DesktopUrl);
            return true;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("Установите Microsoft Edge WebView2 Evergreen Runtime и запустите LocalControl снова. Ссылка и инструкция находятся в README.", "LocalControl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        catch (Exception exception)
        {
            Diagnostic("startup", exception);
            MessageBox.Show("Не удалось запустить LocalControl. Проверьте целостность файлов программы и наличие WebView2. LocalControl не должен запускаться от администратора.", "LocalControl", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
    private async Task Exit()
    {
        if (stopping) return;
        stopping = true;
        tray.Visible = false;
        try
        {
            if (startup is not null) await startup;
            if (host is not null) await host.DisposeAsync();
        }
        finally { await computer.DisposeAsync(); tray.Dispose(); web.Dispose(); exiting = true; Close(); }
    }
    private async Task ApplyUpdate(string manifest)
    {
        var package=await Task.Run(()=>UpdatePackage.Verify(manifest,Path.Combine(AppContext.BaseDirectory,"publisher-public.pem")));
        var current=typeof(MainWindow).Assembly.GetName().Version!;
        if(Version.Parse(package.Manifest.Version)<=current)throw new ControlException("update_old","Пакет не новее установленной версии.",409);
        var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(new Action(()=>ready.SetResult(MessageBox.Show($"Установить LocalControl {package.Manifest.Version}? Программа завершится, файлы будут проверены; при неудаче версия восстановится.","LocalControl",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)));
        if(!await ready.Task)throw new ControlException("update_cancelled","Обновление отменено.",409);
        var runner=Path.Combine(data,"update-state","runner-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(runner);
        var owned=JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"owned-files.json")))??throw new InvalidDataException();
        foreach(var file in owned){var source=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,file));var target=Path.GetFullPath(Path.Combine(runner,file));
            if(!source.StartsWith(AppContext.BaseDirectory,StringComparison.OrdinalIgnoreCase)||!target.StartsWith(runner+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(source,target,false);
        }
        var info=new ProcessStartInfo(Path.Combine(runner,"LocalControl.Updater.exe")){UseShellExecute=false};info.ArgumentList.Add(Path.GetFullPath(manifest));info.ArgumentList.Add(AppContext.BaseDirectory);info.ArgumentList.Add(Environment.ProcessId.ToString());using(var process=Process.GetCurrentProcess())info.ArgumentList.Add(process.StartTime.ToUniversalTime().Ticks.ToString());
        using var helper=Process.Start(info)??throw new ControlException("update_start","Не удалось запустить updater.",409);
        var deadline=DateTimeOffset.UtcNow.AddSeconds(20);
        while(!File.Exists(Path.Combine(runner,"ready"))&&DateTimeOffset.UtcNow<deadline&&!helper.HasExited)await Task.Delay(100);
        if(!File.Exists(Path.Combine(runner,"ready")))throw new ControlException("update_not_ready","Updater не подтвердил готовность. Программа продолжает работать.",409);
        // Exit only after this API response can finish; no HTTP request awaits
        // stopping the very Kestrel host that is serving it.
        BeginInvoke(new Action(async()=>{await Task.Delay(500);await Exit();}));
    }
}
