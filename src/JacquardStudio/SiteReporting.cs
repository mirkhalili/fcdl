using System.IO;
using System.Management;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace JacquardStudio;

/// <summary>User-authorized license lookups, reporting, and retrieval of signed activation leases.</summary>
public static class SiteReporting
{
    private static readonly Uri Endpoint=new("https://www.mirkhalili.com/app/api.php");
    private static readonly HttpClient Client=new(new HttpClientHandler { AllowAutoRedirect=false }) {Timeout=TimeSpan.FromSeconds(12),MaxResponseContentBufferSize=16384};
    private sealed record Credentials(string InstallationId,string Secret,string? Device=null);
    private sealed record Report(string InstallationId,string Secret,string Device,string AppVersion,string OsVersion,string CpuModel,long RamBytes,string MachineName,string CustomerName,string Phone,string City,string? ManualToken,int LicenseProtocol,bool CheckOnly);
    public sealed record Contact(string CustomerName,string Phone,string City);
    public sealed record SiteReply(string? Status,string? Token,string? ActivationCode);
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
    private static readonly object Gate=new();
    private static Credentials Identity(string device)
    {
        lock(Gate)
        {
            string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JacquardStudio","reporting-identity.dat");
            if(File.Exists(path))
            {
                if(new FileInfo(path).Length>8192)throw new InvalidDataException("شناسه گزارش نامعتبر است.");
                var plain=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);
                try {var old=JsonSerializer.Deserialize<Credentials>(plain) ?? throw new InvalidDataException("شناسه گزارش نامعتبر است.");if(old.Device is null || old.Device==device)return old;} finally {CryptographicOperations.ZeroMemory(plain);}
            }
            var c=new Credentials(Guid.NewGuid().ToString("D"),Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),device);
            var bytes=JsonSerializer.SerializeToUtf8Bytes(c);var encrypted=ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);CryptographicOperations.ZeroMemory(bytes);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {File.WriteAllBytes(temp,encrypted);File.Move(temp,path,true);} finally {if(File.Exists(temp))File.Delete(temp);}
            return c;
        }
    }
    private static string Clean(string? s,int max) {s=(s ?? "").Trim();s=new string(s.Where(c=>!char.IsControl(c)).ToArray());while(System.Text.Encoding.UTF8.GetByteCount(s)>max)s=s[..^1];return s;}
    private static string ContactPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JacquardStudio","contact.dat");
    public static void SaveContact(Contact contact,string? path=null)
    {
        var plain=JsonSerializer.SerializeToUtf8Bytes(contact);byte[] encrypted;try{encrypted=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);}finally{CryptographicOperations.ZeroMemory(plain);}
        path ??= ContactPath;Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,encrypted);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static Contact? LoadContact(string? path=null)
    {
        path ??= ContactPath;if(!File.Exists(path))return null;if(new FileInfo(path).Length>8192)throw new InvalidDataException("اطلاعات تماس نامعتبر است.");
        var plain=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);try{return JsonSerializer.Deserialize<Contact>(plain);}finally{CryptographicOperations.ZeroMemory(plain);}
    }
    private static Report Collect(Contact? contact,string? manualToken,bool checkOnly)
    {
        var device=HardwareIdentity.ReadDeviceId();var identity=Identity(device);string cpu="",os="",machine="";long memory=0;
        if(!checkOnly)
        {
            os=Clean(Environment.OSVersion.VersionString,150);machine=Clean(Environment.MachineName,100);
            using(var search=new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
            using(var results=search.Get()) foreach(ManagementObject item in results) using(item) {cpu=Clean(item["Name"] as string,160);break;}
            using(var search=new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            using(var results=search.Get()) foreach(ManagementObject item in results) using(item) {if(item["TotalPhysicalMemory"] is not null)memory=Convert.ToInt64(item["TotalPhysicalMemory"]);break;}
        }
        return new(identity.InstallationId,identity.Secret,device,Clean(AppVersion.Display,50),os,cpu,memory,machine,checkOnly?"":Clean(contact?.CustomerName,120),checkOnly?"":Clean(contact?.Phone,60),checkOnly?"":Clean(contact?.City,100),checkOnly?null:manualToken,2,checkOnly);
    }
    public static async Task<SiteReply> RequestAsync(Contact? contact=null,string? manualToken=null,bool checkOnly=false)
    {
        var data=await Task.Run(()=>Collect(contact,manualToken,checkOnly));
        using var response=await Client.PostAsJsonAsync(Endpoint,data,JsonOptions);response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SiteReply>(JsonOptions) ?? throw new InvalidDataException("پاسخ سایت نامعتبر است.");
    }
    public static async Task<string> SendAsync()
    {
        var reply=await RequestAsync();return reply.Status switch {"pending"=>"درخواست در انتظار بررسی است.","approved"=>reply.Token is null ? "درخواست تأیید شده اما کلید صدور روی سایت تنظیم نشده است." : "درخواست تأیید شده است. برای فعال‌سازی از پنجره ورود به برنامه استفاده کنید.","blocked"=>"درخواست در پنل مسدود شده است.","expired"=>"اعتبار مجوز به پایان رسیده است.",_=>"پاسخ سایت نامعتبر است."};
    }
    public static async Task SendOnStartupAsync()
    {
        try {await SendAsync();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or ManagementException or InvalidOperationException or ArgumentException or CryptographicException or JsonException or System.Runtime.InteropServices.COMException) { /* Reporting is best-effort, never blocks startup. */ }
    }
}
