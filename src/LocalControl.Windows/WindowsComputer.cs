using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LocalControl.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace LocalControl.Windows;

// All COM calls are serialized on one MTA thread. No COM wrapper crosses to an
// HTTP worker or the UI thread. Shutdown drains accepted work before returning.
public sealed partial class WindowsComputer : IExtendedComputer
{
    private readonly BlockingCollection<Action> queue = new(64);
    private readonly Thread thread;
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AppCatalog catalog = new();
    private readonly Hardware hardware = new();
    private long revision;
    private int closing;
    public WindowsComputer()
    {
        thread = new Thread(() =>
        {
            try { foreach (var work in queue.GetConsumingEnumerable()) work(); }
            finally { stopped.TrySetResult(); }
        }) { Name = "LocalControl.Windows", IsBackground = true };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }
    private Task<T> Invoke<T>(Func<T> work, CancellationToken token)
    {
        if (Volatile.Read(ref closing) != 0) return Task.FromException<T>(new ObjectDisposedException(nameof(WindowsComputer)));
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (!queue.TryAdd(() =>
            {
                if (token.IsCancellationRequested) { done.TrySetCanceled(token); return; }
                try { done.TrySetResult(work()); } catch (Exception ex) { done.TrySetException(ex); }
            })) done.TrySetException(new ControlException("pc_busy", "ПК занят. Повторите через секунду.", 429));
        }
        catch (InvalidOperationException) { done.TrySetException(new ObjectDisposedException(nameof(WindowsComputer))); }
        return done.Task;
    }
    private static string Id(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32];
    public Task<StateSnapshot> ReadState(CancellationToken token) => Invoke(() =>
    {
        var warnings = new List<string>();
        var audio = new List<AudioItem>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? output = Default(enumerator, DataFlow.Render), input = Default(enumerator, DataFlow.Capture);
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
            {
                using (endpoint)
                {
                    try
                    {
                        var kind = endpoint.DataFlow == DataFlow.Capture ? "microphone" : "output";
                        audio.Add(new(Id(endpoint.ID), endpoint.FriendlyName, kind, endpoint.AudioEndpointVolume.MasterVolumeLevelScalar, endpoint.AudioEndpointVolume.Mute, endpoint.ID == (kind == "output" ? output : input)));
                        if (endpoint.DataFlow != DataFlow.Render) continue;
                        var sessions = endpoint.AudioSessionManager.Sessions;
                        for (var i = 0; i < sessions.Count; i++)
                        {
                            using var session = sessions[i];
                            try
                            {
                                if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                                audio.Add(new(Id(endpoint.ID + "|" + session.GetSessionInstanceIdentifier), SessionLabel(session), "session", session.SimpleAudioVolume.Volume, session.SimpleAudioVolume.Mute, false));
                            }
                            catch (COMException) { /* A process can exit during enumeration. */ }
                        }
                    }
                    catch (COMException) { warnings.Add("Аудиоустройство отключилось во время чтения."); }
                }
            }
        }
        catch (COMException) { warnings.Add("Служба аудио Windows недоступна."); }
        return new StateSnapshot(++revision, DateTimeOffset.UtcNow, hardware.Read(), audio.ToArray(), warnings.Distinct().ToArray());
    }, token);
    private static string? Default(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try { using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia); return device.ID; }
        catch (COMException) { return null; }
    }
    private static string SessionLabel(AudioSessionControl session)
    {
        if (session.IsSystemSoundsSession) return "Звуки Windows";
        try { using var process = Process.GetProcessById(checked((int)session.GetProcessID)); return process.ProcessName; }
        catch (ArgumentException) { return string.IsNullOrWhiteSpace(session.DisplayName) ? "Аудиосессия" : session.DisplayName; }
        catch (InvalidOperationException) { return "Аудиосессия"; }
    }
    public Task ChangeAudio(string id, VolumeChange change, CancellationToken token) => Invoke(() =>
    {
        if (!change.IsValid) throw new ControlException("invalid_audio", "Некорректная громкость.");
        using var enumerator = new MMDeviceEnumerator();
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
        {
            using (endpoint)
            {
                if (Id(endpoint.ID) == id)
                {
                    if (change.Volume.HasValue) endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = change.Volume.Value;
                    if (change.Muted.HasValue) endpoint.AudioEndpointVolume.Mute = change.Muted.Value;
                    return true;
                }
                if (endpoint.DataFlow != DataFlow.Render) continue;
                var sessions = endpoint.AudioSessionManager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    if (Id(endpoint.ID + "|" + session.GetSessionInstanceIdentifier) != id) continue;
                    if (change.Volume.HasValue) session.SimpleAudioVolume.Volume = change.Volume.Value;
                    if (change.Muted.HasValue) session.SimpleAudioVolume.Mute = change.Muted.Value;
                    return true;
                }
            }
        }
        throw new ControlException("audio_gone", "Аудиосессия завершилась или устройство отключено.", 404);
    }, token);
    public Task<AppItem[]> GetApps(CancellationToken token) => Invoke(catalog.List, token);
    public Task Launch(string id, CancellationToken token) => Invoke(() => { catalog.Launch(id); return true; }, token);
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref closing, 1) != 0) return;
        queue.CompleteAdding();
        await stopped.Task;
        queue.Dispose();
    }
}
