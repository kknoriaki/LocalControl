using System.Collections.Concurrent;
using LocalControl.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace LocalControl.Server;

public sealed record EnabledChange(bool Enabled);
public sealed record CloseRequest(bool Force=false);
public sealed record TextRequest(string Text);
public sealed record PowerRequest(string Action);
public sealed record IntentRequest(string Id);
public sealed record TransferRequest(string Name,long Length);
public sealed record OfferRequest(string DeviceId,string Path);
public sealed record RemoteRequest(string Monitor);
public sealed record UpdateRequest(string Manifest);
public sealed record RemoteTicket(string Id,string Monitor,DateTimeOffset ExpiresAt);
public sealed partial class ControlHost
{
    public Func<string,Task>? ApplyUpdate { get; set; }
    private sealed class RemoteLease(string owner,RemoteTicket ticket){public string Owner=owner;public RemoteTicket Ticket=ticket;public long LastFrame;}
    private readonly ConcurrentDictionary<string,RemoteLease> remotes=[];
    private IExtendedComputer Extended()=>computer as IExtendedComputer??throw new ControlException("capability_unavailable","Эта возможность недоступна в текущей среде.",501);
    private static Lease Session(HttpContext context)=>(Lease)context.Items["lease"]!;
    private RemoteLease Remote(string id,HttpContext context)
    {
        if(!remotes.TryGetValue(id,out var remote)||remote.Owner!=Session(context).DeviceId||remote.Ticket.ExpiresAt<DateTimeOffset.UtcNow)throw new ControlException("remote_expired","Сеанс просмотра истёк. Подключитесь снова.",403);return remote;
    }
    private void MapExtended(WebApplication app,bool admin)
    {
        transfers??=new(configuration,dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalControl"));
        app.MapGet("/api/v1/capabilities",()=>Results.Ok(new{extended=computer is IExtendedComputer,remote="snapshot",transport="http",persistentCredential=false}));
        app.MapGet("/api/v1/apps/{id}/icon",async(string id,HttpContext context)=>{Require(context,Permissions.Launch);var icon=await Extended().AppIcon(id,context.RequestAborted);return icon is null?Results.NotFound():Results.Bytes(icon,"image/png");});
        app.MapPost("/api/v1/apps/{id}/close",async(string id,CloseRequest value,HttpContext context)=>{Require(context,Permissions.Close);await Extended().CloseApp(id,value.Force,context.RequestAborted);return Results.NoContent();});
        app.MapPost("/api/v1/apps/{id}/restart",async(string id,HttpContext context)=>{Require(context,Permissions.Close);Require(context,Permissions.Launch);await Extended().RestartApp(id,context.RequestAborted);return Results.NoContent();});
        app.MapGet("/api/v1/hardware",async(HttpContext context)=>{Require(context,Permissions.Status);return Results.Ok(await Extended().HardwareInfo(context.RequestAborted));});
        app.MapGet("/api/v1/power/capabilities",async(HttpContext context)=>{Require(context,Permissions.Power);return Results.Ok(await Extended().PowerCapabilities(context.RequestAborted));});
        app.MapPost("/api/v1/power/intents",(PowerRequest value,HttpContext context)=>{Require(context,Permissions.Power);return Results.Ok(new{id=powerIntents.Create(Session(context).DeviceId,value.Action),expiresInSeconds=10});});
        app.MapPost("/api/v1/power/confirm",async(IntentRequest value,HttpContext context)=>{Require(context,Permissions.Power);var action=powerIntents.Consume(Session(context).DeviceId,value.Id);await Extended().PowerAction(action,context.RequestAborted);return Results.NoContent();});
        app.MapPost("/api/v1/clipboard/read",async(HttpContext context)=>{Require(context,Permissions.ClipboardRead);var text=await Extended().ReadClipboard(context.RequestAborted);if(text.Length>65536)throw new ControlException("clipboard_large","Текст в буфере превышает 64 КБ.",413);return Results.Ok(new{text});});
        app.MapPost("/api/v1/clipboard/write",async(TextRequest value,HttpContext context)=>{Require(context,Permissions.ClipboardWrite);if(value.Text is null)throw new ControlException("invalid_text","Укажите текст.");await Extended().WriteClipboard(value.Text,context.RequestAborted);return Results.NoContent();});
        app.MapGet("/api/v1/media",async(HttpContext context)=>{Require(context,Permissions.Media);return Results.Ok(await Extended().Media(context.RequestAborted));});
        app.MapPost("/api/v1/media/{id}/{action}",async(string id,string action,HttpContext context)=>{Require(context,Permissions.Media);await Extended().MediaAction(id,action,context.RequestAborted);return Results.NoContent();});
        app.MapGet("/api/v1/hotkeys",async(HttpContext context)=>{Require(context,Permissions.Hotkey);return Results.Ok(await Extended().Hotkeys(context.RequestAborted));});
        app.MapPost("/api/v1/hotkeys/{id}",async(string id,HttpContext context)=>{Require(context,Permissions.Hotkey);await Extended().SendHotkey(id,context.RequestAborted);return Results.Ok(new{sent=true,discordState="unknown"});});
        app.MapGet("/api/v1/remote/monitors",async(HttpContext context)=>{Require(context,Permissions.RemoteView);return Results.Ok(await Extended().Monitors(context.RequestAborted));});
        app.MapPost("/api/v1/remote/sessions",async(RemoteRequest value,HttpContext context)=>{
            Require(context,Permissions.RemoteView);var monitor=(await Extended().Monitors(context.RequestAborted)).FirstOrDefault(x=>x.Id==value.Monitor)??throw new ControlException("monitor_gone","Монитор недоступен.",404);
            foreach(var key in remotes.Where(x=>x.Value.Ticket.ExpiresAt<DateTimeOffset.UtcNow||x.Value.Owner==Session(context).DeviceId).Select(x=>x.Key).ToArray())remotes.TryRemove(key,out _);
            var ticket=new RemoteTicket(TrustStore.Token(),monitor.Id,DateTimeOffset.UtcNow.AddMinutes(1));remotes[ticket.Id]=new(Session(context).DeviceId,ticket);return Results.Ok(ticket);
        });
        app.MapGet("/api/v1/remote/sessions/{id}/frame",async(string id,HttpContext context)=>{
            Require(context,Permissions.RemoteView);var session=Remote(id,context);var now=Environment.TickCount64;
            if(now-Interlocked.Exchange(ref session.LastFrame,now)<900)throw new ControlException("frame_rate","Обновляйте снимок не чаще раза в секунду.",429);
            return Results.Bytes(await Extended().Capture(session.Ticket.Monitor,context.RequestAborted),"image/jpeg");
        });
        app.MapPost("/api/v1/remote/sessions/{id}/input",async(string id,RemoteInput input,HttpContext context)=>{Require(context,Permissions.RemoteView);Require(context,Permissions.RemoteInput);var remote=Remote(id,context);await Extended().Input(remote.Ticket.Monitor,input,context.RequestAborted);return Results.NoContent();});
        app.MapDelete("/api/v1/remote/sessions/{id}",(string id,HttpContext context)=>{Require(context,Permissions.RemoteView);Remote(id,context);remotes.TryRemove(id,out _);return Results.NoContent();});
        app.MapGet("/api/v1/transfers",(HttpContext context)=>{if(!Session(context).Desktop&&!Session(context).Allows(Permissions.TransferSend)&&!Session(context).Allows(Permissions.TransferReceive))throw new ControlException("permission_denied","Передача файлов не разрешена.",403);return Results.Ok(transfers.List(Session(context).DeviceId,Session(context).Desktop));});
        app.MapPost("/api/v1/transfers",(TransferRequest value,HttpContext context)=>{Require(context,Permissions.TransferSend);return Results.Ok(transfers.Begin(Session(context).DeviceId,value.Name,value.Length));});
        app.MapPut("/api/v1/transfers/{id}/content",async(string id,HttpContext context)=>{
            Require(context,Permissions.TransferSend);var limits=context.Features.Get<IHttpMaxRequestBodySizeFeature>();if(limits is not null&&!limits.IsReadOnly)limits.MaxRequestBodySize=Transfers.MaximumFileBytes;
            await transfers.Upload(id,Session(context).DeviceId,context.Request.Body,context.RequestAborted);return Results.NoContent();
        });
        app.MapDelete("/api/v1/transfers/{id}",(string id,HttpContext context)=>{if(!Session(context).Desktop)Require(context,Permissions.TransferSend);transfers.Cancel(id,Session(context).DeviceId,Session(context).Desktop);return Results.NoContent();});
        app.MapGet("/api/v1/transfers/{id}/download",(string id,HttpContext context)=>{Require(context,Permissions.TransferReceive);var file=transfers.Download(id,Session(context).DeviceId);return Results.File(file.Path,"application/octet-stream",file.Name,enableRangeProcessing:true);});
        if(!admin)return;
        app.MapPost("/api/v1/desktop/updates",async(UpdateRequest value)=>{
            if(ApplyUpdate is null)throw new ControlException("update_unavailable","Обновление доступно только в native Windows приложении.",409);
            await ApplyUpdate(value.Manifest);return Results.Ok(new{scheduled=true});
        });
        app.MapPost("/api/v1/desktop/apps/rescan",async(HttpContext context)=>Results.Ok(await Extended().Rescan(context.RequestAborted)));
        app.MapPost("/api/v1/desktop/apps/custom",async(CustomApp app,HttpContext context)=>{await Extended().AddApp(app,context.RequestAborted);return Results.NoContent();});
        app.MapPut("/api/v1/desktop/apps/{id}",async(string id,AppEdit edit,HttpContext context)=>{await Extended().EditApp(id,edit,context.RequestAborted);return Results.NoContent();});
        app.MapGet("/api/v1/desktop/startup",async(HttpContext context)=>Results.Ok(await Extended().Startup(context.RequestAborted)));
        app.MapPut("/api/v1/desktop/startup/{id}",async(string id,EnabledChange value,HttpContext context)=>{await Extended().SetStartup(id,value.Enabled,context.RequestAborted);return Results.NoContent();});
        app.MapGet("/api/v1/desktop/settings",()=>Results.Ok(configuration.Get()));
        app.MapPut("/api/v1/desktop/settings",async(ControlSettings value,HttpContext context)=>{
            Transfers.NoReparse(value.TransferDirectory);configuration.Set(value);await Extended().SetOwnStartup(value.AutoStart,context.RequestAborted);return Results.Ok(configuration.Get());
        });
        app.MapPut("/api/v1/desktop/hotkeys",async(HotkeyDefinition value,HttpContext context)=>{await Extended().ConfigureHotkey(value,context.RequestAborted);return Results.NoContent();});
        app.MapPost("/api/v1/desktop/transfers/offer",(OfferRequest value)=>{
            if(!trust.Devices().Any(x=>x.Id==value.DeviceId&&x.Permissions.Contains(Permissions.TransferReceive)))throw new ControlException("device_unavailable","Разрешите получение файлов для выбранного телефона.",403);
            return Results.Ok(transfers.Offer(value.DeviceId,value.Path));
        });
    }
}
