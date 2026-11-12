using System.IO;
using System.Management;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace JacquardStudio;

/// <summary>Optional, user-authorized execution reporting. It does not grant or enforce licenses.</summary>
public static class SiteReporting
{
    private static readonly Uri Endpoint=new("https://mirkhalili.com/app/api.php");
    private static readonly HttpClient Client=new(new HttpClientHandler { AllowAutoRedirect=false }) {Timeout=TimeSpan.FromSeconds(12),MaxResponseContentBufferSize=4096};
    private sealed record Credentials(string InstallationId,string Secret);
    private sealed record Report(string InstallationId,string Secret,string Device,string AppVersion,string OsVersion,string CpuModel,long RamBytes,string MachineName);
    private sealed record Reply(string? Status);
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
    private static readonly object Gate=new();
    private static Credentials Identity()
    {
        lock(Gate)
        {
            string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JacquardStudio","reporting-identity.dat");
            if(File.Exists(path))
            {
                if(new FileInfo(path).Length>8192)throw new InvalidDataException("شناسه گزارش نامعتبر است.");
                var plain=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);
                try {return JsonSerializer.Deserialize<Credentials>(plain) ?? throw new InvalidDataException("شناسه گزارش نامعتبر است.");} finally {CryptographicOperations.ZeroMemory(plain);}
            }
            var c=new Credentials(Guid.NewGuid().ToString("D"),Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
            var bytes=JsonSerializer.SerializeToUtf8Bytes(c);var encrypted=ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);CryptographicOperations.ZeroMemory(bytes);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {File.WriteAllBytes(temp,encrypted);File.Move(temp,path,false);} finally {if(File.Exists(temp))File.Delete(temp);}
            return c;
        }
    }
    private static string Clean(string? s,int max) {s=(s ?? "").Trim();s=new string(s.Where(c=>!char.IsControl(c)).ToArray());while(System.Text.Encoding.UTF8.GetByteCount(s)>max)s=s[..^1];return s;}
    private static Report Collect()
    {
        var device=HardwareIdentity.ReadDeviceId();var identity=Identity();string cpu="";long memory=0;
        using(var search=new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
        using(var results=search.Get()) foreach(ManagementObject item in results) using(item) {cpu=Clean(item["Name"] as string,160);break;}
        using(var search=new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
        using(var results=search.Get()) foreach(ManagementObject item in results) using(item) {if(item["TotalPhysicalMemory"] is not null)memory=Convert.ToInt64(item["TotalPhysicalMemory"]);break;}
        return new(identity.InstallationId,identity.Secret,device,Clean(AppVersion.Display,50),Clean(Environment.OSVersion.VersionString,150),cpu,memory,Clean(Environment.MachineName,100));
    }
    public static async Task<string> SendAsync()
    {
        var data=await Task.Run(Collect);
        using var response=await Client.PostAsJsonAsync(Endpoint,data,JsonOptions);response.EnsureSuccessStatusCode();
        var reply=await response.Content.ReadFromJsonAsync<Reply>(JsonOptions);return reply?.Status switch {"pending"=>"درخواست در انتظار بررسی است.","approved"=>"درخواست در پنل تأیید شده است؛ فعال‌سازی هنوز آماده نیست.","blocked"=>"درخواست در پنل مسدود شده است؛ کنترل لایسنس هنوز فعال نیست.",_=>"پاسخ سایت نامعتبر است."};
    }
    public static async Task SendOnStartupAsync()
    {
        try {await SendAsync();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or ManagementException or InvalidOperationException or ArgumentException or CryptographicException or JsonException or System.Runtime.InteropServices.COMException) { /* Reporting is best-effort, never blocks startup. */ }
    }
}
