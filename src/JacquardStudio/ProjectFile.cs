using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public sealed record ProjectState(WorkflowSession Session, string SourceName, string WidthInput, string HeightInput, string ColorInput, string Stage, bool English, int MachineFormatIndex, string?[] PatternNames, DateTimeOffset? CreatedAt = null, DesignMetadata? Metadata = null);

/// <summary>Self-contained, versioned SAM archive. Never extracts paths from the archive.</summary>
public static class ProjectFile
{
    private const long MaxArchiveBytes = 128L * 1024 * 1024;
    private const int MaxManifestBytes = 65536;
    private sealed class PatternInfo { public PatternInfo() { } public int Width { get; set; } public int Height { get; set; } public string? Name { get; set; } }
    private sealed class Manifest
    {
        public Manifest() { }
        public int Version { get; set; } = 2;
        public bool HasOverrides { get; set; }
        public string? Designer { get; set; } public string? Usage { get; set; } public string? Description { get; set; }
        public int SourceWidth { get; set; } public int SourceHeight { get; set; }
        public int ResizeWidth { get; set; } public int ResizeHeight { get; set; }
        public string SourceName { get; set; } = "";
        public DateTimeOffset? CreatedAt { get; set; }
        public string WidthInput { get; set; } = ""; public string HeightInput { get; set; } = ""; public string ColorInput { get; set; } = "50";
        public string Stage { get; set; } = "Size"; public bool English { get; set; } public int MachineFormatIndex { get; set; }
        public int? ColorLimit { get; set; } public int SourceColors { get; set; }
        public int[]? Colors { get; set; } public PatternInfo?[]? Patterns { get; set; }
    }
    private static void CheckDimensions(int width, int height, int limit)
    { if (width < 1 || height < 1 || (long)width * height > limit) throw new InvalidDataException("Invalid or excessive image dimensions."); }
    private static byte[] GetBytes(BitmapSource image)
    {
        CheckDimensions(image.PixelWidth, image.PixelHeight, DesignProcessor.MaxPixels);
        var bitmap = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[checked(image.PixelWidth * image.PixelHeight * 4)];
        bitmap.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels;
    }
    private static BitmapSource MakeBitmap(int width, int height, byte[] bytes)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, checked(width * 4));
        image.Freeze(); return image;
    }
    private static void WriteEntry(ZipArchive archive, string name, byte[] data)
    { var entry = archive.CreateEntry(name, CompressionLevel.Optimal); using var stream = entry.Open(); stream.Write(data); }
    private static byte[] ReadEntry(ZipArchive archive, string name, int max, int? expected = null)
    {
        var matches = archive.Entries.Where(e => e.FullName == name).ToList();
        if (matches.Count != 1) throw new InvalidDataException("Missing or duplicate project entry: " + name);
        var entry = matches[0];
        if (entry.Length > max || (expected is int n && entry.Length != n)) throw new InvalidDataException("Invalid project entry size: " + name);
        using var src = entry.Open(); using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int read;
        while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
        { if (output.Length + read > max) throw new InvalidDataException("Project entry exceeds limit."); output.Write(buffer, 0, read); }
        var bytes = output.ToArray();
        if (expected is int count && bytes.Length != count) throw new InvalidDataException("Truncated project entry.");
        return bytes;
    }
    private static string Bounded(string? text, int max, string fallback = "")
    {
        if (text is null) return fallback;
        if (text.Length > max) throw new InvalidDataException("Project field is too long.");
        return text;
    }
    public static void Save(string path, ProjectState state, IEnumerable<string> protectedPaths)
    {
        string target = Path.GetFullPath(path);
        if (!target.EndsWith(".sam", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Project extension must be .sam.");
        if (protectedPaths.Any(p => string.Equals(Path.GetFullPath(p), target, StringComparison.OrdinalIgnoreCase))) throw new IOException("Cannot overwrite an image or pattern.");
        var session = state.Session; var palette = session.Palette;
        if (state.PatternNames.Length != session.Patterns.Length) throw new InvalidDataException("Invalid pattern names.");
        var m = new Manifest
        {
            SourceWidth = session.Source.PixelWidth, SourceHeight = session.Source.PixelHeight,
            ResizeWidth = session.Resized?.PixelWidth ?? 0, ResizeHeight = session.Resized?.PixelHeight ?? 0,
            HasOverrides = session.PixelOverrides is not null,
            Designer = Bounded(state.Metadata?.Designer, 100), Usage = Bounded(state.Metadata?.Usage, 100), Description = Bounded(state.Metadata?.Description, 300),
            CreatedAt = state.CreatedAt, SourceName = Bounded(state.SourceName, 255), WidthInput = Bounded(state.WidthInput, 32), HeightInput = Bounded(state.HeightInput, 32), ColorInput = Bounded(state.ColorInput, 32),
            Stage = Bounded(state.Stage, 16), English = state.English, MachineFormatIndex = state.MachineFormatIndex, ColorLimit = session.AppliedColorLimit,
            SourceColors = palette?.SourceColorCount ?? 0, Colors = palette?.Colors,
            Patterns = palette is null ? null : session.Patterns.Select((p, i) => p is null ? null : new PatternInfo { Width = p.Width, Height = p.Height, Name = Bounded(state.PatternNames[i], 255) }).ToArray()
        };
        string temp = Path.Combine(Path.GetDirectoryName(target)!, ".sam-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(m));
                WriteEntry(zip, "source.bgra", GetBytes(session.Source));
                if (session.Resized is not null) WriteEntry(zip, "resized.bgra", GetBytes(session.Resized));
                if (palette is not null)
                {
                    WriteEntry(zip, "indices.bin", palette.Indices);
                    if (session.PixelOverrides is { } overrides) { if (overrides.Length != palette.Indices.Length || overrides.Any(v => v > 2)) throw new InvalidDataException("Invalid overrides."); WriteEntry(zip, "overrides.bin", overrides); }
                    for (int i = 0; i < session.Patterns.Length; i++) if (session.Patterns[i] is { } p) WriteEntry(zip, $"patterns/{i}.bin", p.Values);
                }
            }
            if (new FileInfo(temp).Length > MaxArchiveBytes) throw new InvalidDataException("Project exceeds the 128 MiB limit.");
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static ProjectState Load(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Project not found.", path);
        if (file.Length > MaxArchiveBytes) throw new InvalidDataException("Project exceeds 128 MiB.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 55) throw new InvalidDataException("Too many project entries.");
        var m = JsonSerializer.Deserialize<Manifest>(ReadEntry(zip, "manifest.json", MaxManifestBytes)) ?? throw new InvalidDataException("Missing project manifest.");
        if (m.Version is not (1 or 2)) throw new NotSupportedException("Unsupported SAM project version.");
        if (m.Version == 1 && m.HasOverrides) throw new InvalidDataException("Overrides require SAM version 2.");
        CheckDimensions(m.SourceWidth, m.SourceHeight, DesignProcessor.MaxPixels);
        var source = MakeBitmap(m.SourceWidth, m.SourceHeight, ReadEntry(zip, "source.bgra", checked(m.SourceWidth * m.SourceHeight * 4), checked(m.SourceWidth * m.SourceHeight * 4)));
        BitmapSource? resized = null; PaletteResult? palette = null; WeavePattern?[] patterns = Array.Empty<WeavePattern?>(); string?[] names = Array.Empty<string?>(); byte[]? overrides = null;
        if (m.ResizeWidth != 0 || m.ResizeHeight != 0)
        {
            CheckDimensions(m.ResizeWidth, m.ResizeHeight, DesignProcessor.MaxPixels);
            int bytes = checked(m.ResizeWidth * m.ResizeHeight * 4);
            resized = MakeBitmap(m.ResizeWidth, m.ResizeHeight, ReadEntry(zip, "resized.bgra", bytes, bytes));
        }
        if (m.Colors is not null)
        {
            if (resized is null || m.Colors.Length is < 1 or > 50 || m.Colors.Any(c => c < 0 || c > 0xffffff) || m.Colors.Distinct().Count() != m.Colors.Length || m.ColorLimit is not (>= 1 and <= 50) || m.Colors.Length > m.ColorLimit || m.SourceColors < m.Colors.Length || m.SourceColors > (long)m.ResizeWidth * m.ResizeHeight || m.Patterns is null || m.Patterns.Length != m.Colors.Length)
                throw new InvalidDataException("Invalid project palette.");
            int pixels = checked(m.ResizeWidth * m.ResizeHeight);
            byte[] indices = ReadEntry(zip, "indices.bin", pixels, pixels); int[] counts = new int[m.Colors.Length];
            foreach (byte index in indices) { if (index >= counts.Length) throw new InvalidDataException("Invalid color index."); counts[index]++; }
            palette = new PaletteResult(m.ResizeWidth, m.ResizeHeight, m.SourceColors, m.Colors, indices, counts);
            if (m.HasOverrides) { overrides = ReadEntry(zip, "overrides.bin", pixels, pixels); if (overrides.Any(v => v > 2)) throw new InvalidDataException("Invalid pixel overrides."); }
            patterns = new WeavePattern?[m.Colors.Length]; names = new string?[m.Colors.Length];
            for (int i = 0; i < patterns.Length; i++)
            {
                if (m.Patterns[i] is not { } p) continue;
                CheckDimensions(p.Width, p.Height, DesignProcessor.MaxPatternPixels);
                int length = checked(p.Width * p.Height); byte[] values = ReadEntry(zip, $"patterns/{i}.bin", length, length);
                if (values.Any(v => v != 0 && v != 255)) throw new InvalidDataException("Invalid pattern pixels.");
                patterns[i] = new WeavePattern(p.Width, p.Height, values); names[i] = Bounded(p.Name, 255);
            }
        }
        else if (m.ColorLimit is not null || m.Patterns is not null || m.HasOverrides) throw new InvalidDataException("Invalid project state.");
        if (m.MachineFormatIndex is < 0 or > 4) throw new InvalidDataException("Invalid machine format selection.");
        string stage = Bounded(m.Stage, 16, "Size");
        if (stage is not ("Size" or "Colors" or "Patterns" or "Machine" or "Analysis" or "Floats")) throw new InvalidDataException("Invalid workflow stage.");
        if ((stage == "Colors" && resized is null) || ((stage is "Patterns" or "Analysis" or "Floats") && palette is null)) throw new InvalidDataException("Stage lacks required data.");
        if ((stage is "Analysis" or "Floats") && patterns.Any(p => p is null)) throw new InvalidDataException("Analysis requires a complete weave.");
        var session = WorkflowSession.Restore(source, resized, palette, m.ColorLimit, patterns, overrides);
        return new ProjectState(session, Bounded(m.SourceName, 255), Bounded(m.WidthInput, 32), Bounded(m.HeightInput, 32), Bounded(m.ColorInput, 32, "50"), stage, m.English, m.MachineFormatIndex, names, m.CreatedAt, new DesignMetadata(Bounded(m.Designer, 100), Bounded(m.Usage, 100), Bounded(m.Description, 300)));
    }
}
