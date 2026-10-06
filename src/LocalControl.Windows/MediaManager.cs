using LocalControl.Core;
using Windows.Media.Control;

namespace LocalControl.Windows;

internal sealed class MediaManager
{
    private readonly SemaphoreSlim gate=new(1);
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private readonly Dictionary<string,GlobalSystemMediaTransportControlsSession> sessions=[];
    public async Task<MediaInfo[]> List(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try{
            manager??=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();sessions.Clear();var result=new List<MediaInfo>();
            foreach(var session in manager.GetSessions().Take(32)){
                token.ThrowIfCancellationRequested();var id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(session.SourceAppUserModelId)))[..32];
                sessions[id]=session;var properties=await session.TryGetMediaPropertiesAsync();var playback=session.GetPlaybackInfo();result.Add(new(id,session.SourceAppUserModelId,properties.Title,properties.Artist,playback.PlaybackStatus.ToString()));
            }return result.ToArray();
        }finally{gate.Release();}
    }
    public async Task Action(string id,string action,CancellationToken token)
    {
        await List(token);await gate.WaitAsync(token);
        try{
            if(!sessions.TryGetValue(id,out var session))throw new ControlException("media_gone","Медиасессия завершилась.",404);
            var success=action switch{"play"=>await session.TryPlayAsync(),"pause"=>await session.TryPauseAsync(),"toggle"=>await session.TryTogglePlayPauseAsync(),"previous"=>await session.TrySkipPreviousAsync(),"next"=>await session.TrySkipNextAsync(),_=>throw new ControlException("invalid_media","Неизвестная медиакоманда.")};
            if(!success)throw new ControlException("media_unsupported","Приложение не поддерживает эту медиакоманду.",409);
        }finally{gate.Release();}
    }
}
