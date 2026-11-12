using System.Management;
using JacquardLicensing;

namespace JacquardStudio;

/// <summary>Reads the processor identifier and the physical disk hosting Windows. Raw values never leave this method.</summary>
public static class HardwareIdentity
{
    public static string ReadDeviceId()
    {
        var cpuIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(var cpus=new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
        using(var rows=cpus.Get()) foreach(ManagementObject cpu in rows) using(cpu) {var id=cpu["ProcessorId"] as string;if(!string.IsNullOrWhiteSpace(id))cpuIds.Add(id.Trim());}
        if(cpuIds.Count!=1)throw new InvalidOperationException("شناسه یکتای پردازنده قابل تشخیص نیست؛ شناسه دستگاه صادر نشد.");
        string root=Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? throw new InvalidOperationException("درایو ویندوز قابل تشخیص نیست.");
        string drive=root.TrimEnd('\\').ToUpperInvariant();if(drive.Length!=2 || drive[1]!=':' || drive[0] is < 'A' or > 'Z')throw new InvalidOperationException("درایو ویندوز معتبر نیست.");
        var diskIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(var logicals=new ManagementObjectSearcher($"SELECT * FROM Win32_LogicalDisk WHERE DeviceID='{drive}'"))
        using(var logicalRows=logicals.Get()) foreach(ManagementObject logical in logicalRows) using(logical)
        using(var parts=logical.GetRelated("Win32_DiskPartition")) foreach(ManagementObject part in parts) using(part)
        using(var disks=part.GetRelated("Win32_DiskDrive")) foreach(ManagementObject disk in disks) using(disk)
        {var serial=disk["SerialNumber"] as string;if(!string.IsNullOrWhiteSpace(serial))diskIds.Add(serial.Trim());}
        if(diskIds.Count!=1)throw new InvalidOperationException("شماره سریال دیسک فیزیکی ویندوز قابل تشخیص نیست یا بیش از یک دیسک وجود دارد؛ شناسه دستگاه صادر نشد.");
        return LicenseToken.DeviceDigest(cpuIds.Single(),diskIds.Single());
    }
}
