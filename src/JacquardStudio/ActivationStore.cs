using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using JacquardLicensing;

namespace JacquardStudio;
/// <summary>Signed short-lived authorization; a missing pinned public key is never treated as a valid license.</summary>
public static class ActivationStore
{
    private sealed record Saved(string Token,long LastSeen,bool Revoked=false,bool Manual=false);
    private static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JacquardStudio","activation.dat");
    public static ECDsa? PublicKey()
    {
        using var stream=typeof(ActivationStore).Assembly.GetManifestResourceStream("JacquardStudio.LicensePublicKey.pem");
        if(stream is null)return null;using var reader=new StreamReader(stream);var pem=reader.ReadToEnd();if(pem.Length>4096)throw new InvalidDataException("کلید عمومی نامعتبر است.");
        var key=ECDsa.Create();try {key.ImportFromPem(pem);return key;}catch {key.Dispose();throw;}
    }
    public static bool Accept(ECDsa key,string device,string token,DateTimeOffset now,bool allowRevoked=false,string? path=null,bool manual=true)
    {
        token=token.Trim();if(!allowRevoked && IsRevoked(path))return false;if(!LicenseToken.TryVerify(key,token,device,now,out _))return false;Write(new Saved(token,now.ToUnixTimeSeconds(),Manual:manual),path);return true;
    }
    public static bool Valid(ECDsa key,string device,DateTimeOffset now,string? path=null)
    {
        try
        {
            var saved=Read(path);if(saved is null || saved.Revoked || now.ToUnixTimeSeconds()<saved.LastSeen-300 || !LicenseToken.TryVerify(key,saved.Token,device,now,out _))return false;
            Write(saved with {LastSeen=Math.Max(saved.LastSeen,now.ToUnixTimeSeconds())},path);return true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException) {return false;}
    }
    public static (LicenseClaims Claims,string? ManualToken)? Details(ECDsa key,string device,DateTimeOffset now,string? path=null)
    {
        try {var saved=Read(path);if(saved is null || saved.Revoked || now.ToUnixTimeSeconds()<saved.LastSeen-300 || !LicenseToken.TryVerify(key,saved.Token,device,now,out var claims) || claims is null)return null;return (claims,saved.Manual?saved.Token:null);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException){return null;}
    }
    private static Saved? Read(string? path)
    {
        path ??= DefaultPath;if(!File.Exists(path))return null;if(new FileInfo(path).Length>16*1024)throw new InvalidDataException("Activation data is too large.");
        var plain=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);try{return JsonSerializer.Deserialize<Saved>(plain) ?? throw new InvalidDataException("Invalid activation data.");}finally{CryptographicOperations.ZeroMemory(plain);}
    }
    private static void Write(Saved value,string? path)
    {
        var plain=JsonSerializer.SerializeToUtf8Bytes(value);byte[] cipher;try {cipher=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);}finally {CryptographicOperations.ZeroMemory(plain);}
        path ??= DefaultPath;Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temp,cipher);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static bool IsRevoked(string? path=null)
    {
        try{return Read(path)?.Revoked ?? false;}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException){return true;}
    }
    public static void Clear(string? path=null) {path ??= DefaultPath;if(File.Exists(path))File.Delete(path);}
    public static string ReadTokenFile(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);if(stream.Length>8192)throw new InvalidDataException("فایل فعال‌سازی بیش از حد بزرگ است.");using var reader=new StreamReader(stream,new System.Text.UTF8Encoding(false,true),true);var value=reader.ReadToEnd().Trim();if(value.Length>4096)throw new InvalidDataException("کد فعال‌سازی بیش از حد طولانی است.");return value;
    }
    public static void Revoke(string? path=null) => Write(new Saved("",DateTimeOffset.UtcNow.ToUnixTimeSeconds(),true),path);
}
