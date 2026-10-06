using System.Text.Json;

namespace LocalControl.Core;

public sealed record ControlSettings(string ComputerName, bool AutoStart, int Port, string TransferDirectory, bool Onboarded, string? Address = null);
public sealed class SettingsStore
{
    private readonly string file;
    private readonly object gate=new();
    private ControlSettings current;
    public SettingsStore(string directory)
    {
        Directory.CreateDirectory(directory);file=Path.Combine(directory,"settings.json");
        current=File.Exists(file)?JsonSerializer.Deserialize<ControlSettings>(File.ReadAllText(file))??throw new InvalidDataException("Invalid settings"):new(Environment.MachineName,false,41017,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","LocalControl"),false);
    }
    public ControlSettings Get(){lock(gate)return current;}
    public void Set(ControlSettings value)
    {
        if(string.IsNullOrWhiteSpace(value.ComputerName)||value.ComputerName.Length>80||value.Port is <1024 or >65535||!Path.IsPathFullyQualified(value.TransferDirectory))throw new ControlException("invalid_settings","Проверьте имя, порт (1024–65535) и полную папку передачи.");
        lock(gate){File.WriteAllText(file+".tmp",JsonSerializer.Serialize(value));File.Move(file+".tmp",file,true);current=value;}
    }
}
