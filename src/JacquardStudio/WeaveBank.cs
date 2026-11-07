using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public sealed class BankItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public WeavePattern Pattern { get; init; } = new(1, 1, new byte[] { 255 });
    public bool IsPreset { get; init; }
    public string Dimensions => $"{Pattern.Width} × {Pattern.Height}";
    public ImageSource Preview
    {
        get { var image = BitmapSource.Create(Pattern.Width, Pattern.Height, 96, 96, PixelFormats.Gray8, null, Pattern.Values, Pattern.Width); image.Freeze(); return image; }
    }
}

/// <summary>Per-user local library. Each self-contained item is written atomically and is never executable.</summary>
public sealed class WeaveBank
{
    private sealed class Record
    {
        public Record() { }
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] Values { get; set; } = Array.Empty<byte>();
    }
    private readonly string folder;
    public WeaveBank(string? folder = null) => this.folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JacquardStudio", "Weaves");
    private static void Validate(string name, WeavePattern p)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || name.Any(char.IsControl)) throw new ArgumentException("Pattern name must contain 1 to 100 printable characters.");
        if (p.Width <= 0 || p.Height <= 0 || (long)p.Width * p.Height > DesignProcessor.MaxPatternPixels || p.Values.Length != (long)p.Width * p.Height || p.Values.Any(v => v != 0 && v != 255))
            throw new InvalidDataException("Invalid black-and-white pattern dimensions or values.");
    }
    public BankItem SaveNew(string name, WeavePattern pattern)
    {
        Validate(name, pattern); Directory.CreateDirectory(folder);
        if (Directory.EnumerateFiles(folder, "*.weave.json").Take(1001).Count() >= 1000) throw new IOException("Pattern bank is full (1000 items).");
        string id = Guid.NewGuid().ToString("N"), file = Path.Combine(folder, id + ".weave.json"), temp = Path.Combine(folder, ".weave-" + Guid.NewGuid().ToString("N") + ".tmp");
        var copy = new WeavePattern(pattern.Width, pattern.Height, (byte[])pattern.Values.Clone());
        try
        {
            var record = new Record { Name = name.Trim(), Width = copy.Width, Height = copy.Height, Values = copy.Values };
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(record)); File.Move(temp, file);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return new BankItem { Id = id, Name = name.Trim(), Pattern = copy };
    }
    public IReadOnlyList<BankItem> Load(out int skipped)
    {
        var result = PatternLibrary.Presets.Select((p, i) => new BankItem { Id = "preset-" + i, Name = p.Name, Pattern = p.Pattern, IsPreset = true }).ToList();
        skipped = 0;
        if (!Directory.Exists(folder)) return result;
        foreach (string file in Directory.EnumerateFiles(folder, "*.weave.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(1000))
        {
            try
            {
                string id = Path.GetFileName(file)[..^".weave.json".Length];
                if (!Guid.TryParseExact(id, "N", out _) || new FileInfo(file).Length > 1024 * 1024) throw new InvalidDataException("Invalid bank item.");
                var record = JsonSerializer.Deserialize<Record>(File.ReadAllBytes(file)) ?? throw new InvalidDataException("Missing bank record.");
                if (record.Version != 1 || record.Values is null) throw new InvalidDataException("Unsupported bank record.");
                var pattern = new WeavePattern(record.Width, record.Height, record.Values); Validate(record.Name, pattern);
                result.Add(new BankItem { Id = id, Name = record.Name, Pattern = pattern });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { skipped++; }
        }
        return result;
    }
    public void Delete(BankItem item)
    {
        if (item.IsPreset || !Guid.TryParseExact(item.Id, "N", out _)) throw new InvalidOperationException("Built-in or invalid bank items cannot be removed.");
        File.Delete(Path.Combine(folder, item.Id + ".weave.json"));
    }
}
