using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using JacquardLicensing;

namespace JacquardStudio;
/// <summary>Signed short-lived authorization; a missing pinned public key is never treated as a valid license.</summary>
public static class ActivationStore
{
    private sealed record Saved(string Token,long LastSeen,bool Revoked=false,bool Manual=false,string? Authorization=null,long OfflineUntil=0);
    private static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JacquardStudio","activation.dat");
    public static ECDsa? PublicKey()
    {
        using var stream=typeof(ActivationStore).Assembly.GetManifestResourceStream("JacquardStudio.LicensePublicKey.pem");
        if(stream is null)return null;using var reader=new StreamReader(stream);var pem=reader.ReadToEnd();if(pem.Length>4096)throw new InvalidDataException("کلید عمومی نامعتبر است.");
        var key=ECDsa.Create();try {key.ImportFromPem(pem);return key;}catch {key.Dispose();throw;}
    }
    public static bool Accept(ECDsa key,string device,string token,DateTimeOffset now,bool allowRevoked=false,string? path=null,bool manual=true)
    {
        token=token.Trim();if(!allowRevoked && IsRevoked(path))return false;
        if(!manual){if(!LicenseToken.TryVerify(key,token,device,now,out _))return false;Write(new Saved(token,now.ToUnixTimeSeconds()),path);return true;}
        if(!LicenseToken.TryVerifyAuthorization(key,token,device,now,out var claims) || claims is null)return false;
        long timestamp=now.ToUnixTimeSeconds();var previous=Read(path);if(previous is not null && timestamp<previous.LastSeen-300)return false;long end=previous is {Manual:true} && previous.Token==token && previous.OfflineUntil>0 ? previous.OfflineUntil : timestamp+LicenseToken.MaximumLeaseSeconds;
        if(claims.ExpiresAt>0)end=Math.Min(end,claims.ExpiresAt);if(timestamp>=end)return false;
        Write(new Saved(token,timestamp,Manual:true,OfflineUntil:end),path);return true;
    }
    public static bool AcceptOnline(ECDsa key,string device,string authorization,string lease,DateTimeOffset now,string? path=null)
    {
        authorization=authorization.Trim();lease=lease.Trim();
        if(!LicenseToken.TryVerifyAuthorization(key,authorization,device,now,out _) || !LicenseToken.TryVerify(key,lease,device,now,out var claims) || claims is null || claims.ActivationHash!=LicenseToken.AuthorizationDigest(authorization))return false;
        Write(new Saved(lease,now.ToUnixTimeSeconds(),Authorization:authorization),path);return true;
    }
    private static (LicenseClaims Claims,string? ManualToken,long OfflineUntil,long? LicenseUntil)? Inspect(Saved? saved,ECDsa key,string device,DateTimeOffset now)
    {
        long timestamp=now.ToUnixTimeSeconds();if(saved is null || saved.Revoked || timestamp<saved.LastSeen-300)return null;
        if(saved.Manual && saved.Token.StartsWith("JSA1.",StringComparison.Ordinal))
        {
            if(saved.OfflineUntil<=timestamp || !LicenseToken.TryVerifyAuthorization(key,saved.Token,device,now,out var auth) || auth is null)return null;
            return (auth,saved.Token,saved.OfflineUntil,null);
        }
        if(!LicenseToken.TryVerify(key,saved.Token,device,now,out var lease) || lease is null)return null;
        if(saved.Manual && saved.OfflineUntil>0 && timestamp>=saved.OfflineUntil)return null;
        if(saved.Authorization is not null)
        {
            if(!LicenseToken.TryVerifyAuthorization(key,saved.Authorization,device,now,out var auth) || auth is null || lease.ActivationHash!=LicenseToken.AuthorizationDigest(saved.Authorization))return null;
            return (auth,null,lease.ExpiresAt,lease.LicenseExpiresAt);
        }
        return (lease,saved.Manual?saved.Token:null,lease.ExpiresAt,lease.LicenseExpiresAt ?? lease.ExpiresAt);
    }
    public static bool Valid(ECDsa key,string device,DateTimeOffset now,string? path=null)
    {
        try {var saved=Read(path);if(Inspect(saved,key,device,now) is null)return false;Write(saved! with {LastSeen=Math.Max(saved.LastSeen,now.ToUnixTimeSeconds())},path);return true;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException){return false;}
    }
    public static (LicenseClaims Claims,string? ManualToken,long OfflineUntil,long? LicenseUntil)? Details(ECDsa key,string device,DateTimeOffset now,string? path=null)
    {
        try{return Inspect(Read(path),key,device,now);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException){return null;}
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
