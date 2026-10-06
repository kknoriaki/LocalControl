using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalControl.Core;

public sealed record UpdateManifest(string Version,string Archive,string Sha256,long Length);
public sealed record SignedUpdate(UpdateManifest Manifest,string Signature);
public static class UpdatePackage
{
    public static byte[] SigningBytes(UpdateManifest manifest)=>Encoding.UTF8.GetBytes($"LocalControl-Update-v1\n{manifest.Version}\n{manifest.Archive}\n{manifest.Sha256.ToLowerInvariant()}\n{manifest.Length}\n");
    public static (UpdateManifest Manifest,string Archive) Verify(string manifestPath,string publicKeyPath)
    {
        if(!File.Exists(publicKeyPath))throw new ControlException("update_key_missing","Ключ издателя не настроен. Используйте проверенный Setup вместо автоматического обновления.",409);
        if(!File.Exists(manifestPath)||new FileInfo(manifestPath).Length>8192)throw new ControlException("update_manifest","Некорректный файл обновления.");
        var envelope=JsonSerializer.Deserialize<SignedUpdate>(File.ReadAllText(manifestPath))??throw new ControlException("update_manifest","Пустой manifest.");var manifest=envelope.Manifest;
        if(!System.Version.TryParse(manifest.Version,out _)||manifest.Version.Any(char.IsControl)||manifest.Archive.Length is <1 or >200||manifest.Archive!=Path.GetFileName(manifest.Archive)||manifest.Archive.Contains('\\')||manifest.Archive.Contains('/')||manifest.Archive.Contains(':')||manifest.Sha256.Length!=64||!manifest.Sha256.All(Uri.IsHexDigit)||manifest.Length is <1 or >1073741824)throw new ControlException("update_manifest","Недопустимые поля обновления.");
        using var key=ECDsa.Create();key.ImportFromPem(File.ReadAllText(publicKeyPath));
        byte[] signature;try{signature=Convert.FromBase64String(envelope.Signature);}catch(FormatException){throw new ControlException("update_signature","Подпись не распознана.",403);}
        if(!key.VerifyData(SigningBytes(manifest),signature,HashAlgorithmName.SHA256))throw new ControlException("update_signature","Подпись издателя не совпадает.",403);
        var archive=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!,manifest.Archive);
        if(!File.Exists(archive)||new FileInfo(archive).Length!=manifest.Length)throw new ControlException("update_length","Пакет отсутствует или повреждён.");
        using var stream=File.OpenRead(archive);var actual=Convert.ToHexString(SHA256.HashData(stream));
        if(!actual.Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase))throw new ControlException("update_hash","Контрольная сумма пакета не совпадает.",403);
        return(manifest,archive);
    }
}
