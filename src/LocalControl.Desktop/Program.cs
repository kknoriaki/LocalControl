using System.Security.Cryptography;
using System.Text;

namespace LocalControl.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Contains("--health-check")){
            try{
                if(!File.Exists(Path.Combine(AppContext.BaseDirectory,"wwwroot","index.html")))throw new FileNotFoundException();
                var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalControl");
                _=new LocalControl.Core.TrustStore(data);_=new LocalControl.Core.SettingsStore(data);
                var computer=new LocalControl.Windows.WindowsComputer();computer.ReadState(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();computer.DisposeAsync().AsTask().GetAwaiter().GetResult();Environment.ExitCode=0;
            }catch{Environment.ExitCode=1;}return;
        }
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..16];
        using var mutex = new Mutex(true, "Local\\LocalControl-" + identity, out var first);
        if (!first) { MessageBox.Show("LocalControl уже работает. Откройте его через значок в tray.", "LocalControl"); return; }
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, _) => MessageBox.Show("Не удалось выполнить действие. Перезапустите LocalControl через меню tray.", "LocalControl");
        Application.Run(new MainWindow(args.Contains("--tray")));
        mutex.ReleaseMutex();
    }
}
