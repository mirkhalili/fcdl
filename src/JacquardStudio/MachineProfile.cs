using System.IO;
using System.Text.Json;

namespace JacquardStudio;

public sealed record MachineProfileData(int Version, string Manufacturer, MachineSetup Setup);
public static class MachineProfile
{
    public static void Validate(MachineSetup setup, string manufacturer)
    {
        if (string.IsNullOrWhiteSpace(manufacturer) || manufacturer.Length>100) throw new ArgumentException("نوع ماشین را مشخص کنید.");
        if(!NumberInput.TryInt(setup.TotalHooks,out int total) || total<1 || total>DesignProcessor.MaxPixels) throw new ArgumentException("تعداد کل زه باید عددی مثبت باشد.");
        if(!NumberInput.TryInt(setup.DesignStart,out int design) || design<0 || design>=total || !NumberInput.TryInt(setup.WeftStart,out int weft) || weft<0 || weft>=total) throw new ArgumentException("زه شروع طرح و پود باید داخل محدوده ماشین باشند.");
    }
    public static void Save(string path, MachineSetup setup, string manufacturer)
    {
        Validate(setup,manufacturer); if(!path.EndsWith(".samm",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("پسوند ماشین باید samm باشد.");
        string full=Path.GetFullPath(path), temp=Path.Combine(Path.GetDirectoryName(full)!,".machine-"+Guid.NewGuid().ToString("N")+".tmp");
        try { File.WriteAllBytes(temp,JsonSerializer.SerializeToUtf8Bytes(new MachineProfileData(1,manufacturer,setup))); File.Move(temp,full,true); } finally { if(File.Exists(temp))File.Delete(temp); }
    }
    public static MachineProfileData Load(string path)
    {
        var file=new FileInfo(path); if(file.Length>1024*1024) throw new InvalidDataException("Machine file is too large.");
        var data=JsonSerializer.Deserialize<MachineProfileData>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Invalid machine file.");
        if(data.Version!=1 || data.Setup is null || data.Manufacturer is null) throw new InvalidDataException("Invalid machine file."); Validate(data.Setup,data.Manufacturer); return data;
    }
}
