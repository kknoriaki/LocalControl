using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalControl.Core;
using Microsoft.Win32;

namespace LocalControl.Windows;

internal sealed class StartupManager
{
    private sealed record Backup(string Id,string Name,string Source,string Location,string Command,int ValueKind=1);
    private readonly string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalControl","startup-backups");
    private readonly Dictionary<string,Backup> active=[];
    private static string Id(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32];
    private Backup[] Saved()=>Directory.Exists(directory)?Directory.EnumerateFiles(directory,"*.json").Select(x=>JsonSerializer.Deserialize<Backup>(File.ReadAllText(x))!).ToArray():[];
    public StartupItem[] List()
    {
        Directory.CreateDirectory(directory);active.Clear();var list=new List<StartupItem>();
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
            using var registry=RegistryKey.OpenBaseKey(hive,view);using var key=registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(key is null)continue;
            foreach(var name in key.GetValueNames())if(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command){
                var source=hive==RegistryHive.CurrentUser?"HKCU Run":"HKLM Run";var location=view.ToString();var id=Id(source+location+name);var backup=new Backup(id,name,source,location,command,(int)key.GetValueKind(name));active[id]=backup;
                list.Add(new(id,name,source,command,true,hive==RegistryHive.CurrentUser,hive==RegistryHive.LocalMachine?"Изменение требует администратора; доступ только для чтения.":null));
            }
        }
        foreach(var folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Startup),Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)}){
            if(!Directory.Exists(folder))continue;var own=folder==Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            foreach(var file in Directory.EnumerateFiles(folder).Take(500)){var name=Path.GetFileName(file);var id=Id(file);active[id]=new(id,name,own?"User Startup":"Common Startup",folder,file);list.Add(new(id,name,own?"User Startup":"Common Startup",file,true,own,own?null:"Общая папка: только чтение."));}
        }
        foreach(var backup in Saved())if(!active.ContainsKey(backup.Id))list.Add(new(backup.Id,backup.Name,backup.Source,backup.Command,false,true,"Сохранена исходная конфигурация."));
        object? service=null;
        try{
            service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);((dynamic)service!).Connect();
            Walk(((dynamic)service).GetFolder("\\"),list,0);
        }catch(COMException){list.Add(new("tasks-unavailable","Task Scheduler","Scheduled Tasks","",false,false,"Недоступно для этого пользователя."));}
        finally{if(service is not null)Marshal.FinalReleaseComObject(service);}
        return list.OrderBy(x=>x.Name).ToArray();
    }
    private static void Walk(dynamic folder,List<StartupItem> list,int depth)
    {
        if(depth>6||list.Count>3000)return;object? tasks=null,folders=null;
        try{
            tasks=folder.GetTasks(1);
            foreach(dynamic task in (dynamic)tasks){object? definition=null,triggers=null;
                try{definition=task.Definition;triggers=((dynamic)definition).Triggers;var boot=false;foreach(dynamic trigger in (dynamic)triggers){try{if((int)trigger.Type is 8 or 9)boot=true;}finally{Marshal.FinalReleaseComObject(trigger);}}
                    if(boot)list.Add(new(Id((string)task.Path),(string)task.Name,"Scheduled Tasks",(string)task.Path,(bool)task.Enabled,false,"Задачи показаны только для чтения; изменяйте их через Task Scheduler."));
                }finally{if(triggers is not null)Marshal.FinalReleaseComObject(triggers);if(definition is not null)Marshal.FinalReleaseComObject(definition);Marshal.FinalReleaseComObject(task);}
            }
            folders=folder.GetFolders(0);foreach(dynamic child in (dynamic)folders)try{Walk(child,list,depth+1);}finally{Marshal.FinalReleaseComObject(child);}
        }catch(COMException){}finally{if(tasks is not null)Marshal.FinalReleaseComObject(tasks);if(folders is not null)Marshal.FinalReleaseComObject(folders);}
    }
    public void Set(string id,bool enabled)
    {
        List();var manifest=Path.Combine(directory,id+".json");
        if(enabled){
            var backup=Saved().FirstOrDefault(x=>x.Id==id)??throw new ControlException("startup_backup_missing","Резервная конфигурация не найдена.",404);
            if(backup.Source=="HKCU Run"){
                using var registry=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,Enum.Parse<RegistryView>(backup.Location));using var key=registry.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if(key.GetValue(backup.Name) is not null)throw new ControlException("startup_changed","Значение уже создано другой программой. Восстановление не перезапишет его.",409);
                key.SetValue(backup.Name,backup.Command,(RegistryValueKind)backup.ValueKind);
            }else if(backup.Source=="User Startup"){
                if(File.Exists(backup.Command))throw new ControlException("startup_changed","Файл уже существует. Восстановление не перезапишет его.",409);
                File.Move(Path.Combine(directory,id+".disabled"),backup.Command);
            }else throw new ControlException("startup_readonly","Эта запись доступна только для чтения.",403);
            File.Delete(manifest);return;
        }
        if(!active.TryGetValue(id,out var value)||value.Source is not ("HKCU Run" or "User Startup"))throw new ControlException("startup_readonly","Эта запись доступна только для чтения.",403);
        File.WriteAllText(manifest+".tmp",JsonSerializer.Serialize(value));File.Move(manifest+".tmp",manifest,true);
        if(value.Source=="HKCU Run"){
            using var registry=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,Enum.Parse<RegistryView>(value.Location));using var key=registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)!;
            if(key.GetValue(value.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string!=value.Command){File.Delete(manifest);throw new ControlException("startup_changed","Запись изменилась. Обновите список.",409);}key.DeleteValue(value.Name);
        }else File.Move(value.Command,Path.Combine(directory,id+".disabled"),false);
    }
}
