using System.Collections.Concurrent;
using LocalControl.Core;

namespace LocalControl.Server;

public sealed record TransferInfo(string Id,string Name,long Length,string Direction,string Status,DateTimeOffset ExpiresAt);
public sealed class Transfers(SettingsStore settings,string dataDirectory)
{
    private sealed class Entry(TransferInfo info,string owner,string path)
    { public TransferInfo Info=info; public string Owner=owner,Path=path;public int Busy; }
    private readonly ConcurrentDictionary<string,Entry> items=[];
    private readonly object gate=new();
    public const long MaximumFileBytes=1024L*1024*1024;
    public static string SafeName(string name)
    {
        var safe=string.Concat(name.Where(c=>!char.IsControl(c)&&!"<>:\"/\\|?*".Contains(c))).Trim(' ','.');
        if(safe.Length is <1 or >180||new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(safe.Split('.')[0].ToUpperInvariant()))throw new ControlException("invalid_filename","Недопустимое имя файла.");return safe;
    }
    public static void NoReparse(string directory)
    { for(var current=new DirectoryInfo(directory);current is not null;current=current.Parent)if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)throw new ControlException("transfer_reparse","Выберите обычную папку без junction/symlink.",409); }
    public TransferInfo Begin(string owner,string name,long length)
    {
        if(length is <0 or >MaximumFileBytes)throw new ControlException("file_too_large","Максимальный размер файла — 1 ГБ.",413);
        name=SafeName(name);lock(gate){Cleanup();if(items.Count>=100||items.Values.Count(x=>x.Owner==owner&&x.Info.Status=="waiting")>=4||items.Values.Where(x=>x.Info.Status=="waiting").Sum(x=>x.Info.Length)+length>2*MaximumFileBytes)throw new ControlException("transfer_limit","Очередь заполнена. Завершите текущие передачи.",429);
            var id=Guid.NewGuid().ToString("N");var info=new TransferInfo(id,name,length,"toPc","waiting",DateTimeOffset.UtcNow.AddMinutes(20));var directory=Path.Combine(dataDirectory,"transfer-temp");Directory.CreateDirectory(directory);NoReparse(directory);items[id]=new(info,owner,Path.Combine(directory,id+".part"));return info;
        }
    }
    private Entry Owned(string id,string owner,bool desktop=false)
    { if(!items.TryGetValue(id,out var item)||item.Info.ExpiresAt<=DateTimeOffset.UtcNow||(!desktop&&item.Owner!=owner))throw new ControlException("transfer_gone","Передача недоступна или истекла.",404);return item; }
    public async Task Upload(string id,string owner,Stream input,CancellationToken token)
    {
        var item=Owned(id,owner);if(item.Info.Direction!="toPc"||item.Info.Status!="waiting"||Interlocked.CompareExchange(ref item.Busy,1,0)!=0)throw new ControlException("transfer_busy","Передача уже обрабатывается.",409);
        try{
            using(var output=new FileStream(item.Path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous)){
                var buffer=new byte[65536];long written=0;int read;
                while((read=await input.ReadAsync(buffer,token))!=0){written+=read;if(written>item.Info.Length)throw new ControlException("transfer_length","Получено больше данных, чем было заявлено.",413);await output.WriteAsync(buffer.AsMemory(0,read),token);}
                if(written!=item.Info.Length)throw new ControlException("transfer_incomplete","Файл передан не полностью.",400);
            }
            token.ThrowIfCancellationRequested();var destination=settings.Get().TransferDirectory;NoReparse(destination);Directory.CreateDirectory(destination);NoReparse(destination);
            var disk=new DriveInfo(Path.GetPathRoot(destination)!);if(disk.AvailableFreeSpace<item.Info.Length+32*1024*1024)throw new ControlException("disk_full","Недостаточно места в папке передачи.",409);
            var target=Path.Combine(destination,DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+id[..8]+"-"+item.Info.Name);File.Move(item.Path,target,false);item.Path=target;item.Info=item.Info with{Status="complete"};
        }catch{File.Delete(item.Path);item.Info=item.Info with{Status="failed"};throw;}finally{Volatile.Write(ref item.Busy,0);}
    }
    public TransferInfo Offer(string device,string path)
    {
        if(!Path.IsPathFullyQualified(path)||!File.Exists(path))throw new ControlException("file_missing","Выберите существующий файл.");
        var file=new FileInfo(path);if(file.Length>MaximumFileBytes)throw new ControlException("file_too_large","Максимальный размер файла — 1 ГБ.",413);
        lock(gate){Cleanup();if(items.Count>=100)throw new ControlException("transfer_limit","Очередь заполнена.",429);var id=Guid.NewGuid().ToString("N");var info=new TransferInfo(id,SafeName(file.Name),file.Length,"toPhone","ready",DateTimeOffset.UtcNow.AddMinutes(20));items[id]=new(info,device,file.FullName);return info;}
    }
    public TransferInfo[] List(string owner,bool desktop=false){Cleanup();return items.Values.Where(x=>desktop||x.Owner==owner).Select(x=>x.Info).OrderByDescending(x=>x.ExpiresAt).ToArray();}
    public (string Path,string Name) Download(string id,string owner)
    { var entry=Owned(id,owner);if(entry.Info.Direction!="toPhone"||entry.Info.Status!="ready")throw new ControlException("transfer_unavailable","Этот файл нельзя скачать.",404);return(entry.Path,entry.Info.Name); }
    public void Cancel(string id,string owner,bool desktop=false)
    { var item=Owned(id,owner,desktop);if(Volatile.Read(ref item.Busy)!=0)throw new ControlException("transfer_busy","Сначала отмените активную загрузку.",409);if(items.TryRemove(id,out _)&&item.Info.Direction=="toPc"&&item.Info.Status!="complete")File.Delete(item.Path); }
    public void Revoke(string owner){foreach(var item in items.Values.Where(x=>x.Owner==owner).ToArray())if(Volatile.Read(ref item.Busy)==0){items.TryRemove(item.Info.Id,out _);if(item.Info.Direction=="toPc"&&item.Info.Status!="complete")File.Delete(item.Path);}}
    private void Cleanup(){foreach(var entry in items.Values.Where(x=>x.Info.ExpiresAt<=DateTimeOffset.UtcNow&&Volatile.Read(ref x.Busy)==0).ToArray()){items.TryRemove(entry.Info.Id,out _);if(entry.Info.Direction=="toPc"&&entry.Info.Status!="complete")File.Delete(entry.Path);}}
}
