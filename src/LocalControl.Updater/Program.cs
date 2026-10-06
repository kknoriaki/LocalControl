using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using LocalControl.Core;

namespace LocalControl.Updater;

internal static class Program
{
    private static string Safe(string root,string relative)
    {
        if(Path.IsPathRooted(relative)||relative.Split('/','\\').Any(x=>x is ".." or ".")||relative.Contains(':'))throw new InvalidDataException("Unsafe update path");
        var path=Path.GetFullPath(Path.Combine(root,relative));if(!path.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unsafe update path");return path;
    }
    [STAThread]
    private static void Main(string[] args)
    {
        string? target=null,backup=null;string[] oldFiles=[];string[] newFiles=[];bool changed=false;
        try{
            if(args.Length!=4)throw new InvalidDataException("Invalid update invocation");
            target=Path.GetFullPath(args[1]);var pid=int.Parse(args[2]);var startTicks=long.Parse(args[3]);
            if(!File.Exists(Path.Combine(target,"LocalControl.exe"))||!File.Exists(Path.Combine(target,"owned-files.json")))throw new InvalidDataException("Unknown installation");
            var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalControl");Directory.CreateDirectory(Path.Combine(data,"update-state"));
            var package=UpdatePackage.Verify(args[0],Path.Combine(target,"publisher-public.pem"));
            using var mutex=new Mutex(true,"Local\\LocalControl-Update-"+Environment.UserName,out var acquired);if(!acquired)throw new InvalidOperationException("An update is already running");
            using(var parent=Process.GetProcessById(pid)){
                if(!parent.MainModule!.FileName.Equals(Path.Combine(target,"LocalControl.exe"),StringComparison.OrdinalIgnoreCase)||parent.StartTime.ToUniversalTime().Ticks!=startTicks)throw new InvalidOperationException("Parent process identity mismatch");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ready"),"ready");
                if(!parent.WaitForExit(20000))throw new InvalidOperationException("Exit LocalControl before updating");
            }
            var stage=Path.Combine(data,"update-state","stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            using(var archive=ZipFile.OpenRead(package.Archive)){
                if(archive.Entries.Count>10000||archive.Entries.Sum(x=>x.Length)>2147483648L)throw new InvalidDataException("Update is too large");
                foreach(var entry in archive.Entries){if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Symbolic link in update");var file=Safe(stage,entry.FullName);if(entry.FullName.EndsWith('/')||entry.FullName.EndsWith('\\')){Directory.CreateDirectory(file);continue;}Directory.CreateDirectory(Path.GetDirectoryName(file)!);entry.ExtractToFile(file,false);}
            }
            if(!File.Exists(Path.Combine(stage,"LocalControl.exe"))||!File.Exists(Path.Combine(stage,"wwwroot","index.html")))throw new InvalidDataException("Application missing from update");
            oldFiles=JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(target,"owned-files.json")))??throw new InvalidDataException("Invalid installed manifest");
            newFiles=JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(stage,"owned-files.json")))??throw new InvalidDataException("Invalid update file list");
            foreach(var file in newFiles){if(!File.Exists(Safe(stage,file)))throw new InvalidDataException("Missing listed file");Safe(target,file);}
            backup=Path.Combine(data,"update-state","backup-"+DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));Directory.CreateDirectory(backup);
            foreach(var relative in oldFiles){var file=Safe(target,relative);if(File.Exists(file)){var copy=Safe(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.Copy(file,copy,false);}}
            // User data never lives under the install folder. The updater performs
            // no schema migration and never overwrites user settings/devices/apps.
            changed=true;
            foreach(var relative in oldFiles.Except(newFiles,StringComparer.OrdinalIgnoreCase))File.Delete(Safe(target,relative));
            foreach(var relative in newFiles){var file=Safe(target,relative);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.Copy(Safe(stage,relative),file,true);}
            var health=new ProcessStartInfo(Path.Combine(target,"LocalControl.exe")){UseShellExecute=false,CreateNoWindow=true};health.ArgumentList.Add("--health-check");
            using(var check=Process.Start(health)){if(check is null||!check.WaitForExit(20000)||check.ExitCode!=0){if(check is not null&&!check.HasExited)check.Kill(false);throw new InvalidOperationException("New version failed health check");}}
            Directory.Delete(stage,true);Process.Start(new ProcessStartInfo(Path.Combine(target,"LocalControl.exe")){UseShellExecute=true});
        }catch(Exception exception){
            if(changed&&target is not null&&backup is not null){try{
                foreach(var relative in newFiles.Except(oldFiles,StringComparer.OrdinalIgnoreCase))File.Delete(Safe(target,relative));
                foreach(var relative in oldFiles){var file=Safe(backup,relative);if(File.Exists(file)){var destination=Safe(target,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination,true);}}
            }catch{MessageBox.Show("Обновление и автоматическое восстановление не завершены. Резервная копия сохранена в LocalControl/update-state.","LocalControl",MessageBoxButtons.OK,MessageBoxIcon.Error);return;}}
            MessageBox.Show("Обновление не применено. Предыдущая версия и пользовательские настройки сохранены. Диагностика: "+exception.GetType().Name,"LocalControl",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            if(changed&&target is not null)Process.Start(new ProcessStartInfo(Path.Combine(target,"LocalControl.exe")){UseShellExecute=true});
        }
    }
}
