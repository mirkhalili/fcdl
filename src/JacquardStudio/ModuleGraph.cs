using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public enum ModuleKind { Load, Resize, Colors, Weaves, ApplyWeaves, Selvedge, Floats, Tension, Weft, Simulation, Machine }
public enum ModuleData { Image, Palette, Weaves, Fabric, Preview }
public sealed record ModuleColorEdit(string InputHash, PaletteResult Palette, bool[] Locks);
public sealed class ModuleNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ModuleKind Kind { get; set; }
    public double X { get; set; } public double Y { get; set; }
    public string Label { get; set; } = "";
    public int Width { get; set; } public int Height { get; set; }
    public int ColorLimit { get; set; } = 256;
    public ModuleColorEdit? ColorEdit { get; set; }
    public string? ImagePng { get; set; }
    public List<WeavePattern> Weaves { get; set; } = new();
    public List<string> WeaveNames { get; set; } = new();
    // RGB -> index in connected weave collection. Missing entries explicitly use the first weave.
    public Dictionary<int, int> WeaveAssignments { get; set; } = new();
    public int FloatMinimum { get; set; } = 5;
    public bool FloatWarp { get; set; } = true;
    public bool AutoCorrect { get; set; }
    public MachineSetup Machine { get; set; } = new();
    public SimulationSettings Simulation { get; set; } = new(Mode: 0);
    public int MachineFormat { get; set; } = 1;
}
public sealed record ModuleEdge(Guid From, Guid To, string Port);
public sealed class ModuleGraph
{
    public List<ModuleNode> Nodes { get; set; } = new();
    public List<ModuleEdge> Edges { get; set; } = new();
    public static ModuleData Output(ModuleKind kind) => kind switch
    {
        ModuleKind.Load or ModuleKind.Resize => ModuleData.Image,
        ModuleKind.Colors => ModuleData.Palette,
        ModuleKind.Weaves => ModuleData.Weaves,
        ModuleKind.Simulation => ModuleData.Preview,
        _ => ModuleData.Fabric
    };
    public static IReadOnlyDictionary<string, ModuleData> Inputs(ModuleKind kind) => kind switch
    {
        ModuleKind.Resize or ModuleKind.Colors => new Dictionary<string, ModuleData> { ["image"] = ModuleData.Image },
        ModuleKind.ApplyWeaves => new Dictionary<string, ModuleData> { ["palette"] = ModuleData.Palette, ["weaves"] = ModuleData.Weaves },
        ModuleKind.Selvedge or ModuleKind.Floats or ModuleKind.Tension or ModuleKind.Weft or ModuleKind.Simulation or ModuleKind.Machine => new Dictionary<string, ModuleData> { ["fabric"] = ModuleData.Fabric },
        _ => new Dictionary<string, ModuleData>()
    };
    public ModuleNode Add(ModuleKind kind, double x, double y)
    {
        if (Nodes.Count >= 128 || !Enum.IsDefined(kind) || !double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or > 10000 || y is < 0 or > 10000) throw new ArgumentException("Invalid module or position.");
        var node = new ModuleNode { Kind = kind, X = x, Y = y }; Nodes.Add(node); return node;
    }
    public void Remove(Guid id) { Nodes.RemoveAll(n => n.Id == id); Edges.RemoveAll(e => e.From == id || e.To == id); }
    public void Disconnect(Guid to, string port) => Edges.RemoveAll(e => e.To == to && e.Port == port);
    public void Connect(Guid from, Guid to, string port)
    {
        var source = Nodes.SingleOrDefault(n => n.Id == from) ?? throw new ArgumentException("Source module not found.");
        var target = Nodes.SingleOrDefault(n => n.Id == to) ?? throw new ArgumentException("Target module not found.");
        if (!Inputs(target.Kind).TryGetValue(port, out var type) || Output(source.Kind) != type || from == to) throw new ArgumentException("Incompatible ports.");
        // Replacing one incoming wire must be atomic, including on a cycle error.
        var edges = Edges.Where(e => !(e.To == to && e.Port == port)).ToList(); edges.Add(new ModuleEdge(from,to,port));
        bool Reach(Guid node, HashSet<Guid> visited) { if (node == from) return true; return visited.Add(node) && edges.Where(e => e.From == node).Any(e => Reach(e.To,visited)); }
        if (Reach(to, new HashSet<Guid>())) throw new ArgumentException("A connection cannot create a cycle.");
        Edges = edges;
    }
    public void Validate()
    {
        if (Nodes is null || Edges is null || Nodes.Count > 128 || Edges.Count > 256 || Nodes.Any(n => n is null || n.Id == Guid.Empty || !Enum.IsDefined(n.Kind) || !double.IsFinite(n.X) || !double.IsFinite(n.Y) || n.X is < 0 or > 10000 || n.Y is < 0 or > 10000 || n.Label is null || n.Label.Length > 255 || n.Weaves is null || n.Weaves.Count > 256 || n.WeaveNames is null || n.WeaveNames.Count != n.Weaves.Count || n.WeaveNames.Any(x => x is null || x.Length > 100) || n.WeaveAssignments is null || n.WeaveAssignments.Count > 256 || n.Machine is null || n.Simulation is null) || Nodes.Select(n => n.Id).Distinct().Count() != Nodes.Count) throw new InvalidDataException("Invalid graph nodes.");
        foreach (var n in Nodes) if (n.ColorEdit is { } edit)
        {
            var p=edit.Palette;
            if (n.Kind != ModuleKind.Colors || edit.InputHash is null || edit.InputHash.Length != 64 || p is null || p.Width < 1 || p.Height < 1 || (long)p.Width * p.Height > DesignProcessor.MaxPixels || p.Colors is null || p.Colors.Length is < 1 or > 256 || p.Colors.Distinct().Count() != p.Colors.Length || p.Colors.Any(c=>c is < 0 or > 0xffffff) || p.Indices is null || p.Indices.Length != (long)p.Width*p.Height || p.Counts is null || p.Counts.Length != p.Colors.Length || p.SourceColorCount < p.Colors.Length || p.SourceColorCount > p.Indices.Length || edit.Locks is null || edit.Locks.Length != p.Colors.Length || n.ColorLimit < p.Colors.Length || n.ColorLimit > 256) throw new InvalidDataException("Invalid edited palette.");
            var counts=new int[p.Colors.Length]; foreach(byte c in p.Indices) { if(c>=counts.Length) throw new InvalidDataException("Invalid edited palette index."); counts[c]++; } if(!counts.SequenceEqual(p.Counts)) throw new InvalidDataException("Invalid edited palette counts.");
        }
        foreach (var n in Nodes) foreach (var p in n.Weaves) if (p is null || p.Width < 1 || p.Height < 1 || (long)p.Width * p.Height > DesignProcessor.MaxPatternPixels || p.Values is null || p.Values.Length != (long)p.Width * p.Height || p.Values.Any(v => v != 0 && v != 255)) throw new InvalidDataException("Invalid weave.");
        if (Edges.Any(e => e is null || e.Port is null) || Edges.Select(e => (e.To,e.Port)).Distinct().Count() != Edges.Count) throw new InvalidDataException("Invalid graph edges.");
        var check = new ModuleGraph { Nodes = Nodes, Edges = new() }; foreach (var edge in Edges) check.Connect(edge.From, edge.To, edge.Port);
    }
}
public sealed record ModuleValue(ModuleData Type, BitmapSource? Image = null, PaletteResult? Palette = null, IReadOnlyList<WeavePattern>? Weaves = null, MachineComposition? Composition = null, WarpAnalysis? Analysis = null, int FloatCount = 0);

/// <summary>Demand-driven DAG execution. Each run has its own cache; independent branches never share mutable results.</summary>
public static class ModuleExecutor
{
    public static IReadOnlyDictionary<Guid, ModuleValue> Run(ModuleGraph graph, Guid target)
    {
        graph.Validate(); var values = new Dictionary<Guid,ModuleValue>(); var nodes = graph.Nodes.ToDictionary(n => n.Id);
        ModuleValue Evaluate(Guid id)
        {
            if (values.TryGetValue(id, out var result)) return result;
            var node = nodes[id]; ModuleValue Input(string port)
            {
                var edge = graph.Edges.SingleOrDefault(e => e.To == id && e.Port == port) ?? throw new InvalidOperationException($"Connect input '{port}' of {node.Kind}.");
                return Evaluate(edge.From);
            }
            switch (node.Kind)
            {
                case ModuleKind.Load:
                    if (string.IsNullOrEmpty(node.ImagePng)) throw new InvalidOperationException("Choose an input image.");
                    byte[] bytes = Convert.FromBase64String(node.ImagePng); if (bytes.Length > ImageLoader.MaxFileBytes) throw new InvalidDataException("Embedded image is too large.");
                    using (var stream = new MemoryStream(bytes)) { var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); var bitmap = decoder.Frames[0]; bitmap.Freeze(); result = new(ModuleData.Image, bitmap); } break;
                case ModuleKind.Resize:
                    var source = Input("image").Image!; int w = node.Width == 0 ? source.PixelWidth : node.Width, h = node.Height == 0 ? source.PixelHeight : node.Height;
                    result = new(ModuleData.Image, DesignProcessor.Resize(source,w,h)); break;
                case ModuleKind.Colors:
                    var image = Input("image").Image!; PaletteResult palette;
                    if (node.ColorEdit is { } edit) { if (edit.Palette.Width != image.PixelWidth || edit.Palette.Height != image.PixelHeight || edit.InputHash != ImageFingerprint(image)) throw new InvalidOperationException("ورودی مدیریت رنگ تغییر کرده است؛ ماژول رنگ را باز و دوباره اعمال کنید."); palette=edit.Palette; }
                    else palette = DesignProcessor.Prepare(image,image.PixelWidth,image.PixelHeight,node.ColorLimit);
                    result = new(ModuleData.Palette,DesignProcessor.RenderColors(palette),palette); break;
                case ModuleKind.Weaves:
                    if (node.Weaves.Count == 0) throw new InvalidOperationException("Add at least one weave to the module.");
                    result = new(ModuleData.Weaves, Fabric3DWindow.PatternBitmap(node.Weaves[0]), Weaves: node.Weaves); break;
                case ModuleKind.ApplyWeaves:
                    var p = Input("palette").Palette!; var patterns = Input("weaves").Weaves!;
                    var assigned = p.Colors.Select(c => { int k = node.WeaveAssignments.GetValueOrDefault(c,0); if ((uint)k >= (uint)patterns.Count) throw new ArgumentException("A color refers to a missing weave."); return (WeavePattern?)patterns[k]; }).ToArray();
                    result = new(ModuleData.Fabric, DesignProcessor.Render(p,assigned)); break;
                case ModuleKind.Selvedge:
                    var edgeInput = Input("fabric"); if (edgeInput.Composition is { SignalHooks.Length: > 0 }) throw new InvalidOperationException("Add selvedges before weft signal placement.");
                    var body = edgeInput.Image!; var edgeMap = MachineComposer.Compose(body,MachineComposer.BeforeWeft(node.Machine));
                    result = new(ModuleData.Fabric,edgeMap.Image,Composition:edgeMap); break;
                case ModuleKind.Floats:
                    var input = Input("fabric"); var view = input.Composition is null ? input.Image! : CompositionView.Fabric(input.Composition); byte[] map = DesignAnalysis.ReadBinary(view);
                    var runs = DesignAnalysis.FindFloats(map,view.PixelWidth,view.PixelHeight,node.FloatMinimum,node.FloatWarp ? FloatDirection.Warp : FloatDirection.Weft);
                    if (!node.AutoCorrect) result = input with { FloatCount = runs.Count };
                    else
                    {
                        var plan = FloatAutoCorrection.Plan(runs,0,view.PixelWidth,view.PixelHeight,Array.Empty<int>()); var all = DesignAnalysis.ReadBinary(input.Image!); int dx = input.Composition?.FabricStart ?? 0;
                        foreach (var point in plan.Points) { int k = point.Y * input.Image!.PixelWidth + dx + point.X; all[k] = all[k] == 0 ? (byte)255 : (byte)0; }
                        var corrected = BitmapSource.Create(input.Image!.PixelWidth,input.Image.PixelHeight,96,96,PixelFormats.Gray8,null,all,input.Image.PixelWidth); corrected.Freeze();
                        result = input with { Image = corrected, Composition = input.Composition is null ? null : input.Composition with { Image = corrected }, FloatCount = runs.Count };
                    } break;
                case ModuleKind.Tension:
                    var original = Input("fabric"); var woven = original.Composition is null ? original.Image! : CompositionView.Fabric(original.Composition);
                    result = original with { Analysis = DesignAnalysis.Analyze(DesignAnalysis.ReadBinary(woven),woven.PixelWidth,woven.PixelHeight) }; break;
                case ModuleKind.Weft:
                    var beforeWeft = Input("fabric"); if (beforeWeft.Composition is { SignalHooks.Length: > 0 }) throw new InvalidOperationException("This branch already has weft signal columns.");
                    var upstream = beforeWeft.Image!; var composed = MachineComposer.Compose(upstream,node.Machine);
                    result = new(ModuleData.Fabric,composed.Image,Composition:composed); break;
                case ModuleKind.Simulation:
                    var cloth = Input("fabric"); var area = cloth.Composition is null ? cloth.Image! : CompositionView.Fabric(cloth.Composition);
                    result = new(ModuleData.Preview,FabricSimulation.Render(area,node.Simulation,cloth.Composition?.RowWefts)); break;
                case ModuleKind.Machine:
                    result = Input("fabric"); break;
                default: throw new NotSupportedException("Unsupported module.");
            }
            values[id] = result; return result;
        }
        if (!nodes.ContainsKey(target)) throw new ArgumentException("Module not found."); Evaluate(target); return values;
    }
    public static string ImageFingerprint(BitmapSource image)
    {
        var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0); int stride=checked(image.PixelWidth*4); var pixels=new byte[checked(stride*image.PixelHeight)]; converted.CopyPixels(pixels,stride,0);
        using var hash=System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256); hash.AppendData(BitConverter.GetBytes(image.PixelWidth)); hash.AppendData(BitConverter.GetBytes(image.PixelHeight)); hash.AppendData(pixels); return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static string EmbedImage(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = new MemoryStream(); encoder.Save(stream); if (stream.Length > ImageLoader.MaxFileBytes) throw new InvalidDataException("Image is too large."); return Convert.ToBase64String(stream.ToArray());
    }
}

public static class ModuleProjectFile
{
    private const int MaxBytes = 128 * 1024 * 1024;
    private sealed class Envelope { public int Version { get; set; } = 1; public ModuleGraph Graph { get; set; } = new(); }
    public static void Save(string path, ModuleGraph graph)
    {
        if (!path.EndsWith(".jgw",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Graph project must use .jgw."); graph.Validate();
        var json = JsonSerializer.SerializeToUtf8Bytes(new Envelope { Graph = graph }); if (json.Length > MaxBytes) throw new InvalidDataException("Graph project is too large.");
        string target = Path.GetFullPath(path), tmp = Path.Combine(Path.GetDirectoryName(target)!, ".jgw-"+Guid.NewGuid().ToString("N")+".tmp");
        try { using (var file = File.Create(tmp)) using (var zip = new ZipArchive(file,ZipArchiveMode.Create)) { var entry = zip.CreateEntry("graph.json",CompressionLevel.Optimal); using var stream = entry.Open(); stream.Write(json); } File.Move(tmp,target,true); } finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static ModuleGraph Load(string path)
    {
        using var file = File.OpenRead(path); if (file.Length > MaxBytes) throw new InvalidDataException("Graph project is too large."); using var zip = new ZipArchive(file,ZipArchiveMode.Read);
        if (zip.Entries.Count != 1 || zip.Entries[0].FullName != "graph.json" || zip.Entries[0].Length > MaxBytes) throw new InvalidDataException("Invalid graph project archive.");
        using var stream = zip.Entries[0].Open(); using var bounded = new MemoryStream(); byte[] buffer = new byte[8192]; int read; while ((read=stream.Read(buffer))>0) { if (bounded.Length + read > MaxBytes) throw new InvalidDataException("Graph is too large."); bounded.Write(buffer,0,read); }
        var envelope = JsonSerializer.Deserialize<Envelope>(bounded.ToArray()) ?? throw new InvalidDataException("Invalid graph project."); if (envelope.Version != 1 || envelope.Graph is null) throw new InvalidDataException("Unsupported graph project."); envelope.Graph.Validate(); return envelope.Graph;
    }
}
