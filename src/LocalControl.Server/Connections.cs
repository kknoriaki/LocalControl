using System.Collections.Concurrent;
using LocalControl.Core;
using Microsoft.AspNetCore.SignalR;

namespace LocalControl.Server;

public sealed class Connections
{
    private sealed record Entry(Lease Lease, Action Abort);
    private readonly ConcurrentDictionary<string, Entry> entries = new();
    public void Add(string id, Lease lease, Action abort) => entries[id] = new(lease, abort);
    public void Remove(string id) => entries.TryRemove(id, out _);
    public string[] Valid(TrustStore trust)
    {
        foreach (var (id, entry) in entries)
            if (!trust.StillValid(entry.Lease)) { entry.Abort(); entries.TryRemove(id, out _); }
        return entries.Keys.ToArray();
    }
    public void Revoke(string deviceId)
    {
        foreach (var (id, entry) in entries)
            if (entry.Lease.DeviceId == deviceId) { entry.Abort(); entries.TryRemove(id, out _); }
    }
}
public sealed class StateHub(Connections connections) : Hub
{
    public override Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext()!;
        var lease = http.Items["lease"] as Lease;
        if (lease is null || !lease.Allows(Permissions.Status)) { Context.Abort(); return Task.CompletedTask; }
        connections.Add(Context.ConnectionId, lease, Context.Abort);
        return base.OnConnectedAsync();
    }
    public override Task OnDisconnectedAsync(Exception? exception)
    { connections.Remove(Context.ConnectionId); return base.OnDisconnectedAsync(exception); }
}
