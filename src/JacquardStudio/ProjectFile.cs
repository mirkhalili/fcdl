using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public sealed record ProjectState(WorkflowSession Session, string SourceName, string WidthInput, string HeightInput, string ColorInput, string Stage, bool English, int MachineFormatIndex, string?[] PatternNames, DateTimeOffset? CreatedAt = null, DesignMetadata? Metadata = null, MachineSetup? Machine = null, WeftSettings? DraftWeft = null, SimulationSettings? Simulation = null);

/// <summary>Self-contained, versioned SAM archive. Never extracts paths from the archive.</summary>
public static class ProjectFile
{
    private const long MaxArchiveBytes = 128L * 1024 * 1024;
    private const int MaxManifestBytes = 262144;
    private sealed class PatternInfo { public PatternInfo() { } public int Width { get; set; } public int Height { get; set; } public string? Name { get; set; } public string[]? WeftColors { get; set; } }
    private sealed class MachineInfo
    {
        public MachineInfo() { }
        public string? LeftWidth { get; set; } public string? RightWidth { get; set; }
        public PatternInfo? LeftPattern { get; set; } public PatternInfo? RightPattern { get; set; }
        public bool BoxEnabled { get; set; } public int BoxPlacement { get; set; }
        public string? BoxHooks { get; set; } public string? BoxCodes { get; set; } public string? Sequence { get; set; }
        public bool StartFromBottom { get; set; } public bool UseSource { get; set; } public bool RedAsBlack { get; set; }
        public bool AbsoluteLayout { get; set; } public string? TotalHooks { get; set; } public string? DesignStart { get; set; } public string? WeftStart { get; set; } public string? WeftCount { get; set; }
    }
    private sealed class Manifest
    {
        public Manifest() { }
        public int Version { get; set; } = 10;
        public int? VectorPageIndex { get; set; }
        public MachineInfo? Machine { get; set; }
        public WeftSettings? DraftWeft { get; set; }
        public SimulationSettings? Simulation { get; set; }
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
        CheckDimensions(image.PixelWidth, image.PixelHeight, (int)ImageLoader.MaxPixels);
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
    private static WeftSettings BoundedDraft(WeftSettings d)
    {
        if (d.BoxPlacement is < 0 or > 2) throw new InvalidDataException("Invalid draft signal placement.");
        return d with { BoxHooks = Bounded(d.BoxHooks, 256), BoxCodes = Bounded(d.BoxCodes, 8192), Sequence = Bounded(d.Sequence, 8192), TotalHooks = Bounded(d.TotalHooks, 32), DesignStart = Bounded(d.DesignStart, 32), WeftStart = Bounded(d.WeftStart, 32, "0"), WeftCount = Bounded(d.WeftCount, 32, "0") };
    }
    private static SimulationSettings BoundedSimulation(SimulationSettings s)
    {
        if ((uint)s.WarpYarn >= FabricSimulation.YarnNames.Length || (uint)s.WeftYarn >= FabricSimulation.YarnNames.Length || s.Weave is < 0 or > 2 || s.Shadow is < 0 or > 100 || s.Mode is < 0 or > 2) throw new InvalidDataException("Invalid simulation settings.");
        SimulatedYarn[]? yarns = null;
        if (s.Wefts is { } items)
        {
            if (items.Length > 256 || items.Any(y => y is null || string.IsNullOrWhiteSpace(y.Id) || (uint)y.Yarn >= FabricSimulation.YarnNames.Length) || items.Select(y => y.Id).Distinct(StringComparer.Ordinal).Count() != items.Length) throw new InvalidDataException("Invalid simulation wefts.");
            yarns = items.Select(y => y with { Id = Bounded(y.Id, 32), Color = Bounded(y.Color, 16) }).ToArray();
        }
        return s with { WarpColor = Bounded(s.WarpColor, 16), WeftColor = Bounded(s.WeftColor, 16), Wefts = yarns };
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
            SourceWidth = session.Source.PixelWidth, SourceHeight = session.Source.PixelHeight, VectorPageIndex = session.Vector?.PageIndex,
            ResizeWidth = session.Resized?.PixelWidth ?? 0, ResizeHeight = session.Resized?.PixelHeight ?? 0,
            HasOverrides = session.PixelOverrides is not null,
            Designer = Bounded(state.Metadata?.Designer, 100), Usage = Bounded(state.Metadata?.Usage, 100), Description = Bounded(state.Metadata?.Description, 300),
            CreatedAt = state.CreatedAt, SourceName = Bounded(state.SourceName, 255), WidthInput = Bounded(state.WidthInput, 32), HeightInput = Bounded(state.HeightInput, 32), ColorInput = Bounded(state.ColorInput, 32),
            Stage = Bounded(state.Stage, 16), English = state.English, MachineFormatIndex = state.MachineFormatIndex, ColorLimit = session.AppliedColorLimit,
            SourceColors = palette?.SourceColorCount ?? 0, Colors = palette?.Colors,
            Patterns = palette is null ? null : session.Patterns.Select((p, i) => p is null ? null : new PatternInfo { Width = p.Width, Height = p.Height, Name = Bounded(state.PatternNames[i], 255), WeftColors = WeaveBank.CopyColors(p.WeftColors) }).ToArray()
        };
        if (state.DraftWeft is { } draft) m.DraftWeft = BoundedDraft(draft);
        if (state.Simulation is { } simulation) m.Simulation = BoundedSimulation(simulation);
        if (state.Machine is { } setup)
        {
            PatternInfo? Info(WeavePattern? p, string name)
            {
                if (p is null) return null;
                CheckDimensions(p.Width, p.Height, DesignProcessor.MaxPatternPixels);
                if (p.Values.Length != (long)p.Width * p.Height || p.Values.Any(v => v != 0 && v != 255)) throw new InvalidDataException("Invalid selvedge weave.");
                return new PatternInfo { Width = p.Width, Height = p.Height, Name = Bounded(name, 255), WeftColors = WeaveBank.CopyColors(p.WeftColors) };
            }
            if (setup.BoxPlacement is < 0 or > 2) throw new InvalidDataException("Invalid signal placement.");
            m.Machine = new MachineInfo { LeftWidth = Bounded(setup.LeftWidth, 32), RightWidth = Bounded(setup.RightWidth, 32), LeftPattern = Info(setup.LeftPattern, setup.LeftName), RightPattern = Info(setup.RightPattern, setup.RightName), BoxEnabled = setup.BoxEnabled, BoxPlacement = setup.BoxPlacement, BoxHooks = Bounded(setup.BoxHooks, 256), BoxCodes = Bounded(setup.BoxCodes, 8192), Sequence = Bounded(setup.Sequence, 8192), StartFromBottom = setup.StartFromBottom, UseSource = setup.UseSource, RedAsBlack = setup.RedAsBlack, AbsoluteLayout = setup.AbsoluteLayout, TotalHooks = Bounded(setup.TotalHooks, 32), DesignStart = Bounded(setup.DesignStart, 32), WeftStart = Bounded(setup.WeftStart, 32), WeftCount = Bounded(setup.WeftCount, 32) };
        }
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(m);
        if (manifestBytes.Length > MaxManifestBytes) throw new InvalidDataException("Project settings exceed the manifest size limit.");
        string temp = Path.Combine(Path.GetDirectoryName(target)!, ".sam-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "manifest.json", manifestBytes);
                WriteEntry(zip, "source.bgra", GetBytes(session.Source));
                if (session.Vector is { } vector)
                {
                    if (vector.PdfBytes.Length > ImageLoader.MaxFileBytes || vector.PageIndex < 0 || vector.PageIndex >= ImageLoader.MaxFrames) throw new InvalidDataException("Invalid vector source.");
                    WriteEntry(zip, "source.pdf", vector.PdfBytes);
                }
                if (state.Machine?.LeftPattern is { } left) WriteEntry(zip, "machine/left.bin", left.Values);
                if (state.Machine?.RightPattern is { } right) WriteEntry(zip, "machine/right.bin", right.Values);
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
        if (zip.Entries.Count > 58) throw new InvalidDataException("Too many project entries.");
        var m = JsonSerializer.Deserialize<Manifest>(ReadEntry(zip, "manifest.json", MaxManifestBytes)) ?? throw new InvalidDataException("Missing project manifest.");
        if (m.Version is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10)) throw new NotSupportedException("Unsupported SAM project version.");
        if (m.Version == 1 && m.HasOverrides) throw new InvalidDataException("Overrides require SAM version 2.");
        if (m.Version < 3 && m.Machine is not null) throw new InvalidDataException("Machine setup requires SAM version 3.");
        CheckDimensions(m.SourceWidth, m.SourceHeight, (int)ImageLoader.MaxPixels);
        var source = MakeBitmap(m.SourceWidth, m.SourceHeight, ReadEntry(zip, "source.bgra", checked(m.SourceWidth * m.SourceHeight * 4), checked(m.SourceWidth * m.SourceHeight * 4)));
        VectorDocument? vectorSource = null;
        if (m.Version >= 9 && m.VectorPageIndex is int vectorPage)
        {
            if (vectorPage < 0 || vectorPage >= ImageLoader.MaxFrames) throw new InvalidDataException("Invalid PDF page index.");
            byte[] vectorBytes = ReadEntry(zip, "source.pdf", (int)ImageLoader.MaxFileBytes);
            PdfImageLoader.Render(vectorBytes, vectorPage, 1, 1); // validate without trusting paths or external files
            vectorSource = new VectorDocument(vectorBytes, vectorPage);
        }
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
                patterns[i] = new WeavePattern(p.Width, p.Height, values, m.Version >= 8 ? WeaveBank.CopyColors(p.WeftColors) : null); names[i] = Bounded(p.Name, 255);
            }
        }
        else if (m.ColorLimit is not null || m.Patterns is not null || m.HasOverrides) throw new InvalidDataException("Invalid project state.");
        if (m.MachineFormatIndex is < 0 or > 4) throw new InvalidDataException("Invalid machine format selection.");
        string stage = Bounded(m.Stage, 16, "Size");
        if (stage is not ("Size" or "Colors" or "Patterns" or "Machine" or "Analysis" or "Floats" or "Selvedge" or "Weft" or "Simulation")) throw new InvalidDataException("Invalid workflow stage.");
        if ((stage == "Colors" && resized is null) || ((stage is "Patterns" or "Analysis" or "Floats" or "Selvedge" or "Weft" or "Simulation") && palette is null)) throw new InvalidDataException("Stage lacks required data.");
        if ((stage is "Analysis" or "Floats" or "Selvedge" or "Weft" or "Simulation") && patterns.Any(p => p is null)) throw new InvalidDataException("Analysis requires a complete weave.");
        MachineSetup? setup = null;
        if (m.Machine is { } settings)
        {
            WeavePattern? ReadWeave(PatternInfo? p, string entry)
            {
                if (p is null) return null; CheckDimensions(p.Width, p.Height, DesignProcessor.MaxPatternPixels);
                int length = checked(p.Width * p.Height); byte[] bytes = ReadEntry(zip, entry, length, length);
                if (bytes.Any(v => v != 0 && v != 255)) throw new InvalidDataException("Invalid selvedge pixels.");
                return new WeavePattern(p.Width, p.Height, bytes, m.Version >= 8 ? WeaveBank.CopyColors(p.WeftColors) : null);
            }
            if (settings.BoxPlacement is < 0 or > 2) throw new InvalidDataException("Invalid signal placement.");
            setup = new MachineSetup(Bounded(settings.LeftWidth, 32, "0"), Bounded(settings.RightWidth, 32, "0"), ReadWeave(settings.LeftPattern, "machine/left.bin"), ReadWeave(settings.RightPattern, "machine/right.bin"), Bounded(settings.LeftPattern?.Name, 255), Bounded(settings.RightPattern?.Name, 255), settings.BoxEnabled, settings.BoxPlacement, Bounded(settings.BoxHooks, 256), Bounded(settings.BoxCodes, 8192), Bounded(settings.Sequence, 8192), settings.StartFromBottom, settings.UseSource, settings.RedAsBlack, m.Version >= 4 && settings.AbsoluteLayout, Bounded(settings.TotalHooks, 32), Bounded(settings.DesignStart, 32), Bounded(settings.WeftStart, 32, "0"), Bounded(settings.WeftCount, 32, "0"));
        }
        var session = WorkflowSession.Restore(source, resized, palette, m.ColorLimit, patterns, overrides, vectorSource);
        return new ProjectState(session, Bounded(m.SourceName, 255), Bounded(m.WidthInput, 32), Bounded(m.HeightInput, 32), Bounded(m.ColorInput, 32, "50"), stage, m.English, m.MachineFormatIndex, names, m.CreatedAt, new DesignMetadata(Bounded(m.Designer, 100), Bounded(m.Usage, 100), Bounded(m.Description, 300)), setup, m.Version >= 5 && m.DraftWeft is { } draft ? BoundedDraft(draft) : null, m.Version >= 6 && m.Simulation is { } simulation ? BoundedSimulation(simulation with { Wefts = m.Version < 7 ? null : simulation.Wefts, Mode = m.Version < 10 ? 1 : simulation.Mode }) : null);
    }
}
