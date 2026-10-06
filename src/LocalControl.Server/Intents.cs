using LocalControl.Core;

namespace LocalControl.Server;

public sealed class Intents
{
    private sealed record Intent(string Device,string Action,DateTimeOffset Expires);
    private readonly Dictionary<string,Intent> pending=[];
    private readonly object gate=new();
    public string Create(string device,string action)
    {
        if(action is not ("lock" or "sleep" or "hibernate" or "restart" or "shutdown"))throw new ControlException("invalid_power","Неизвестное действие питания.");
        lock(gate){foreach(var key in pending.Where(x=>x.Value.Expires<DateTimeOffset.UtcNow||x.Value.Device==device).Select(x=>x.Key).ToArray())pending.Remove(key);var id=TrustStore.Token();pending[id]=new(device,action,DateTimeOffset.UtcNow.AddSeconds(10));return id;}
    }
    public string Consume(string device,string id)
    { lock(gate){if(!pending.TryGetValue(id,out var intent)||intent.Device!=device||intent.Expires<DateTimeOffset.UtcNow)throw new ControlException("power_expired","Подтверждение истекло. Повторите действие.",403);pending.Remove(id);return intent.Action;} }
}
