using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace JacquardStudio;
public sealed class UserPreferences
{
    public int Version { get; set; }=1;
    public string Theme { get; set; }="dark";
    public List<string> HiddenModules { get; set; }=new();
    public bool TelemetryEnabled { get; set; }
    public bool SiteLicenseCheckEnabled { get; set; }
    public bool SiteLicenseCheckPrompted { get; set; }
    public string? PasswordSalt { get; set; }
    public string? PasswordHash { get; set; }
    public bool HasPassword => PasswordSalt is not null && PasswordHash is not null;
    private static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JacquardStudio","preferences.json");
    public static UserPreferences Load()
    {
        if(!File.Exists(PathName))return new() {Theme=ThemeManager.ReadLight() ? "light" : "dark"}; var f=new FileInfo(PathName); if(f.Length>256*1024) throw new InvalidDataException("فایل تنظیمات بیش از حد بزرگ است.");
        var prefs=JsonSerializer.Deserialize<UserPreferences>(File.ReadAllBytes(PathName)) ?? throw new InvalidDataException("فایل تنظیمات نامعتبر است.");
        if(prefs.Version!=1 || prefs.HiddenModules is null || prefs.HiddenModules.Count>128 || (prefs.PasswordSalt is null)!=(prefs.PasswordHash is null)) throw new InvalidDataException("فایل تنظیمات نامعتبر است.");
        if(prefs.HasPassword) { try {if(Convert.FromBase64String(prefs.PasswordSalt!).Length!=16 || Convert.FromBase64String(prefs.PasswordHash!).Length!=32) throw new InvalidDataException("اطلاعات رمز ورود نامعتبر است.");} catch(FormatException) {throw new InvalidDataException("اطلاعات رمز ورود نامعتبر است.");} }
        return prefs;
    }
    public void SetPassword(string password)
    {
        if(password.Length is < 8 or > 1024)throw new ArgumentException("رمز ورود باید حداقل ۸ و حداکثر ۱۰۲۴ نویسه باشد.");
        var salt=RandomNumberGenerator.GetBytes(16); var hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,310_000,HashAlgorithmName.SHA256,32); PasswordSalt=Convert.ToBase64String(salt); PasswordHash=Convert.ToBase64String(hash); CryptographicOperations.ZeroMemory(hash);
    }
    public bool Verify(string password)
    {
        if(!HasPassword || password.Length>1024)return false;
        byte[] expected=Convert.FromBase64String(PasswordHash!), hash=Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromBase64String(PasswordSalt!),310_000,HashAlgorithmName.SHA256,32);
        try {return CryptographicOperations.FixedTimeEquals(expected,hash);} finally {CryptographicOperations.ZeroMemory(hash);}
    }
    public void Save()
    {
        string path=PathName; Directory.CreateDirectory(Path.GetDirectoryName(path)!); string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllBytes(tmp,JsonSerializer.SerializeToUtf8Bytes(this));File.Move(tmp,path,true);} finally {if(File.Exists(tmp))File.Delete(tmp);}
    }
}
