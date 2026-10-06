namespace LocalControl.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--health-check")) {
            try { StartupCheck.Run().WaitAsync(TimeSpan.FromSeconds(25)).GetAwaiter().GetResult(); Environment.ExitCode = 0; }
            catch { Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--shutdown")) { SingleInstance.Request(true); return; }
        using var mutex = new Mutex(true, "Local\\LocalControl-" + SingleInstance.Identity, out var first);
        if (!first) {
            if (!SingleInstance.Request(false)) { Environment.ExitCode = 1; MessageBox.Show("LocalControl запускается. Повторите открытие через несколько секунд.", "LocalControl"); }
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, _) => MessageBox.Show("Не удалось выполнить действие. Перезапустите LocalControl через меню tray.", "LocalControl");
        var smoke = args.Contains("--ui-smoke");
        if (smoke) Environment.ExitCode = 1;
        var data = smoke ? Path.Combine(Path.GetTempPath(), "LocalControl-ui-" + Guid.NewGuid().ToString("N")) : null;
        var reportIndex = Array.IndexOf(args, "--smoke-report");
        var report = reportIndex >= 0 && reportIndex + 1 < args.Length ? args[reportIndex + 1] : null;
        Application.Run(new MainWindow(args.Contains("--tray"), data, smoke, report));
        if (data is not null) { try { Directory.Delete(data, true); } catch (IOException) { } }
        mutex.ReleaseMutex();
    }
}
