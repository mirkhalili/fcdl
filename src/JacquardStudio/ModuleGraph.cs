using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public enum ModuleKind { Load, Resize, Colors, Weaves, ApplyWeaves, Selvedge, Floats, Tension, Weft, Simulation, Machine, CreateWeave, Crop, Repeat, Report, VectorLoad, MachineDefinition, DesignIdentity, AiEdit }
public enum ModuleData { Image, Palette, Weaves, Fabric, Preview, Report, Any, MachineConfig, Metadata }
public sealed record DesignField(string Title, string Text);
public sealed record ModuleColorEdit(string InputHash, PaletteResult Palette, bool[] Locks);
public sealed class ModuleNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ModuleKind Kind { get; set; }
    public double X { get; set; } public double Y { get; set; }
    public string Label { get; set; } = "";
    public bool Configured { get; set; }
    public string? VectorPdfBase64 { get; set; }
    public int SourcePage { get; set; }
    public string Manufacturer { get; set; } = "Other";
    public List<DesignField> DesignFields { get; set; } = new();
    public string AiPrompt { get; set; } = "";
    public string? AiResultPng { get; set; }
    public string? AiInputHash { get; set; }
    public string? AiPromptHash { get; set; }
    public int Width { get; set; } public int Height { get; set; }
    public int ColorLimit { get; set; } = 256;
    public ModuleColorEdit? ColorEdit { get; set; }
    public GraphCrop? Crop { get; set; }
    public int RepeatColumns { get; set; } = 1;
    public int RepeatRows { get; set; } = 1;
    public RepeatMode RepeatMode { get; set; }
    public string? ImagePng { get; set; }
    public List<WeavePattern> Weaves { get; set; } = new();
    public List<string> WeaveNames { get; set; } = new();
    // RGB -> index in connected weave collection. Missing entries explicitly use the first weave.
    public Dictionary<int, int> WeaveAssignments { get; set; } = new();
    public int DefaultWeaveIndex { get; set; } = 0;
    public Dictionary<int,WeavePattern> CustomWeaves { get; set; } = new();
    public int FloatMinimum { get; set; } = 5;
    public bool FloatWarp { get; set; } = true;
    public bool AutoCorrect { get; set; }
    public string? FloatImagePng { get; set; }
    public string? FloatInputHash { get; set; }
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
        ModuleKind.Load or ModuleKind.VectorLoad or ModuleKind.Resize or ModuleKind.Crop or ModuleKind.Repeat or ModuleKind.AiEdit => ModuleData.Image,
        ModuleKind.Colors => ModuleData.Palette,
        ModuleKind.Weaves or ModuleKind.CreateWeave => ModuleData.Weaves,
        ModuleKind.Simulation => ModuleData.Preview,
        ModuleKind.Report => ModuleData.Report,
        ModuleKind.MachineDefinition => ModuleData.MachineConfig,
        ModuleKind.DesignIdentity => ModuleData.Metadata,
        _ => ModuleData.Fabric
    };
    public static IReadOnlyDictionary<string, ModuleData> Inputs(ModuleKind kind) => kind switch
    {
        ModuleKind.Resize or ModuleKind.Colors or ModuleKind.Crop or ModuleKind.Repeat or ModuleKind.AiEdit => new Dictionary<string, ModuleData> { ["image"] = ModuleData.Image },
        ModuleKind.Weft => new Dictionary<string, ModuleData> { ["fabric"] = ModuleData.Fabric, ["machine"] = ModuleData.MachineConfig },
        ModuleKind.Report => new Dictionary<string, ModuleData> { ["report"] = ModuleData.Any },
        ModuleKind.ApplyWeaves => new Dictionary<string, ModuleData> { ["palette"] = ModuleData.Palette, ["weaves"] = ModuleData.Weaves },
        ModuleKind.Selvedge or ModuleKind.Floats or ModuleKind.Tension or ModuleKind.Simulation or ModuleKind.Machine => new Dictionary<string, ModuleData> { ["fabric"] = ModuleData.Fabric },
        _ => new Dictionary<string, ModuleData>()
    };
    public ModuleNode Add(ModuleKind kind, double x, double y)
    {
        if (Nodes.Count >= 128 || !Enum.IsDefined(kind) || !double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or > 10000 || y is < 0 or > 10000) throw new ArgumentException("Invalid module or position.");
        var node = new ModuleNode { Kind = kind, X = x, Y = y }; Nodes.Add(node); return node;
    }
    /// <summary>Place existing nodes in insertion order without altering connections.</summary>
    public void Arrange(int rows, int columns)
    {
        if(rows < 1 || columns < 1 || columns > 40 || rows > 55) throw new ArgumentException("تعداد ردیف و ستون معتبر نیست.");
        columns=Math.Max(columns,(Nodes.Count+rows-1)/rows); if(columns>40) throw new ArgumentException("تعداد ستون لازم بیش از حد مجاز است.");
        for(int i=0;i<Nodes.Count;i++) { Nodes[i].X=80+(i%columns)*250; Nodes[i].Y=80+(i/columns)*180; }
    }
    public void Remove(Guid id) { Nodes.RemoveAll(n => n.Id == id); Edges.RemoveAll(e => e.From == id || e.To == id); }
    public void Disconnect(Guid to, string port) => Edges.RemoveAll(e => e.To == to && e.Port == port);
    public void Disconnect(Guid from, Guid to, string port) => Edges.RemoveAll(e => e.From == from && e.To == to && e.Port == port);
    public void Connect(Guid from, Guid to, string port)
    {
        var source = Nodes.SingleOrDefault(n => n.Id == from) ?? throw new ArgumentException("Source module not found.");
        var target = Nodes.SingleOrDefault(n => n.Id == to) ?? throw new ArgumentException("Target module not found.");
        if (!Inputs(target.Kind).TryGetValue(port, out var type) || type != ModuleData.Any && Output(source.Kind) != type || from == to) throw new ArgumentException("Incompatible ports.");
        // Replacing one incoming wire must be atomic, including on a cycle error.
        var edges = type == ModuleData.Any ? Edges.ToList() : Edges.Where(e => !(e.To == to && e.Port == port)).ToList();
        if (type == ModuleData.Any && edges.Any(e => e.To == to && e.Port == port && e.From == from)) return;
        if (type == ModuleData.Any && edges.Count(e => e.To == to && e.Port == port) >= 16) throw new ArgumentException("A report can include at most 16 modules.");
        edges.Add(new ModuleEdge(from,to,port));
        bool Reach(Guid node, HashSet<Guid> visited) { if (node == from) return true; return visited.Add(node) && edges.Where(e => e.From == node).Any(e => Reach(e.To,visited)); }
        if (Reach(to, new HashSet<Guid>())) throw new ArgumentException("A connection cannot create a cycle.");
        Edges = edges;
    }
    public void Validate()
    {
        if (Nodes is null || Edges is null || Nodes.Count > 128 || Edges.Count > 256 || Nodes.Any(n => n is null || n.Id == Guid.Empty || !Enum.IsDefined(n.Kind) || !double.IsFinite(n.X) || !double.IsFinite(n.Y) || n.X is < 0 or > 10000 || n.Y is < 0 or > 10000 || n.Label is null || n.Label.Length > 255 || n.Weaves is null || n.Weaves.Count > 256 || n.WeaveNames is null || n.WeaveNames.Count != n.Weaves.Count || n.WeaveNames.Any(x => x is null || x.Length > 100) || n.WeaveAssignments is null || n.WeaveAssignments.Count > 256 || n.CustomWeaves is null || n.CustomWeaves.Count > 256 || n.Machine is null || n.Simulation is null) || Nodes.Select(n => n.Id).Distinct().Count() != Nodes.Count) throw new InvalidDataException("Invalid graph nodes.");
        if (Nodes.Any(n => n.Kind == ModuleKind.CreateWeave && (n.Weaves.Count > 1 || n.Weaves.Any(p => p is null || p.Width > WeaveEditor.MaxSide || p.Height > WeaveEditor.MaxSide)))) throw new InvalidDataException("A weave designer node holds at most one weave.");
        foreach (var n in Nodes) {
            if(n.DesignFields is null || n.DesignFields.Count>50 || n.DesignFields.Any(f=>f is null || string.IsNullOrWhiteSpace(f.Title) || f.Title.Length>100 || f.Text is null || f.Text.Length>2000) || n.Manufacturer is null || n.Manufacturer.Length>100 || n.AiPrompt is null || n.AiPrompt.Length>4000 || n.SourcePage<0 || n.SourcePage>=ImageLoader.MaxFrames) throw new InvalidDataException("Invalid module metadata.");
            if (n.Kind == ModuleKind.Repeat && (n.RepeatColumns < 1 || n.RepeatRows < 1 || !Enum.IsDefined(n.RepeatMode))) throw new InvalidDataException("Invalid repeat settings.");
            if (n.Crop is { } crop && (n.Kind != ModuleKind.Crop || crop.X < 0 || crop.Y < 0 || crop.Width < 1 || crop.Height < 1 || (long)crop.Width*crop.Height > DesignProcessor.MaxPixels || crop.InputHash is null || crop.InputHash.Length != 64)) throw new InvalidDataException("Invalid crop settings.");
        }
        foreach (var n in Nodes) if (n.ColorEdit is { } edit)
        {
            var p=edit.Palette;
            if (n.Kind != ModuleKind.Colors || edit.InputHash is null || edit.InputHash.Length != 64 || p is null || p.Width < 1 || p.Height < 1 || (long)p.Width * p.Height > DesignProcessor.MaxPixels || p.Colors is null || p.Colors.Length is < 1 or > 256 || p.Colors.Distinct().Count() != p.Colors.Length || p.Colors.Any(c=>c is < 0 or > 0xffffff) || p.Indices is null || p.Indices.Length != (long)p.Width*p.Height || p.Counts is null || p.Counts.Length != p.Colors.Length || p.SourceColorCount < p.Colors.Length || p.SourceColorCount > p.Indices.Length || edit.Locks is null || edit.Locks.Length != p.Colors.Length || n.ColorLimit < p.Colors.Length || n.ColorLimit > 256) throw new InvalidDataException("Invalid edited palette.");
            var counts=new int[p.Colors.Length]; foreach(byte c in p.Indices) { if(c>=counts.Length) throw new InvalidDataException("Invalid edited palette index."); counts[c]++; } if(!counts.SequenceEqual(p.Counts)) throw new InvalidDataException("Invalid edited palette counts.");
        }
        foreach (var n in Nodes) foreach (var p in n.CustomWeaves.Values) if (p is null || p.Width < 1 || p.Height < 1 || (long)p.Width*p.Height > DesignProcessor.MaxPatternPixels || p.Values is null || p.Values.Length != (long)p.Width*p.Height || p.Values.Any(v=>v!=0 && v!=255)) throw new InvalidDataException("Invalid custom weave.");
        foreach (var n in Nodes) foreach (var p in n.Weaves) if (p is null || p.Width < 1 || p.Height < 1 || (long)p.Width * p.Height > DesignProcessor.MaxPatternPixels || p.Values is null || p.Values.Length != (long)p.Width * p.Height || p.Values.Any(v => v != 0 && v != 255)) throw new InvalidDataException("Invalid weave.");
        if (Edges.Any(e => e is null || e.Port is null) || Edges.Where(e => e.Port != "report").Select(e => (e.To,e.Port)).Distinct().Count() != Edges.Count(e => e.Port != "report") || Edges.Distinct().Count() != Edges.Count) throw new InvalidDataException("Invalid graph edges.");
        var check = new ModuleGraph { Nodes = Nodes, Edges = new() }; foreach (var edge in Edges) check.Connect(edge.From, edge.To, edge.Port);
    }
}
public sealed record GraphReportItem(Guid NodeId, ModuleKind Kind, string Label, ModuleValue Value, string Details);
public sealed record ModuleValue(ModuleData Type, BitmapSource? Image = null, PaletteResult? Palette = null, IReadOnlyList<WeavePattern>? Weaves = null, MachineComposition? Composition = null, WarpAnalysis? Analysis = null, int FloatCount = 0, IReadOnlyList<GraphReportItem>? ReportItems = null, VectorDocument? Vector = null, MachineSetup? Machine = null, IReadOnlyList<DesignField>? DesignFields = null);

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
                case ModuleKind.VectorLoad:
                    if (string.IsNullOrEmpty(node.ImagePng)) throw new InvalidOperationException("Choose an input image.");
                    byte[] bytes = Convert.FromBase64String(node.ImagePng); if (bytes.Length > ImageLoader.MaxFileBytes) throw new InvalidDataException("Embedded image is too large.");
                    using (var stream = new MemoryStream(bytes)) { var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); var bitmap = decoder.Frames[0]; bitmap.Freeze(); VectorDocument? vector=null; if(node.Kind==ModuleKind.VectorLoad && !string.IsNullOrEmpty(node.VectorPdfBase64)) { var pdf=Convert.FromBase64String(node.VectorPdfBase64); if(pdf.Length>ImageLoader.MaxFileBytes) throw new InvalidDataException("Vector source is too large."); vector=new VectorDocument(pdf,node.SourcePage); } result = new(ModuleData.Image, bitmap, Vector:vector); } break;
                case ModuleKind.Resize:
                    var sourceValue = Input("image"); var source = sourceValue.Image!; int w = node.Width == 0 ? source.PixelWidth : node.Width, h = node.Height == 0 ? source.PixelHeight : node.Height;
                    if(w<1 || h<1 || (long)w*h>DesignProcessor.MaxPixels) throw new ArgumentOutOfRangeException(nameof(node.Width),"Resize exceeds the 4 million pixel limit.");
                    result = new(ModuleData.Image, sourceValue.Vector is { } vectorSource ? PdfImageLoader.Render(vectorSource.PdfBytes,vectorSource.PageIndex,w,h).Image : DesignProcessor.Resize(source,w,h), Vector:sourceValue.Vector); break;
                case ModuleKind.MachineDefinition:
                    MachineProfile.Validate(node.Machine,node.Manufacturer); result=new(ModuleData.MachineConfig,Machine:node.Machine); break;
                case ModuleKind.DesignIdentity:
                    result=new(ModuleData.Metadata,DesignFields:node.DesignFields); break;
                case ModuleKind.AiEdit:
                    var aiInput=Input("image").Image!;
                    if(string.IsNullOrEmpty(node.AiResultPng) || node.AiInputHash!=ImageFingerprint(aiInput) || node.AiPromptHash!=AiImageEdit.PromptHash(node.AiPrompt)) throw new InvalidOperationException("ابتدا تصویر و پرامپت را در ماژول هوش مصنوعی ارسال و نتیجه را تأیید کنید.");
                    var aiBytes=Convert.FromBase64String(node.AiResultPng); if(aiBytes.Length>ImageLoader.MaxFileBytes) throw new InvalidDataException("AI image is too large.");
                    using(var aiStream=new MemoryStream(aiBytes)) { var frame=BitmapDecoder.Create(aiStream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0]; if((long)frame.PixelWidth*frame.PixelHeight>DesignProcessor.MaxPixels) throw new InvalidDataException("AI image is too large."); frame.Freeze(); result=new(ModuleData.Image,frame); } break;
                case ModuleKind.Crop:
                    var cropInput=Input("image").Image!;
                    if (node.Crop is { } crop) { if (crop.InputHash != ImageFingerprint(cropInput)) throw new InvalidOperationException("ورودی برش تغییر کرده است؛ ماژول برش را مجدداً اعمال کنید."); result=new(ModuleData.Image,GraphImageOperations.Crop(cropInput,crop)); }
                    else result=new(ModuleData.Image,cropInput);
                    break;
                case ModuleKind.Repeat:
                    result=new(ModuleData.Image,GraphImageOperations.Repeat(Input("image").Image!,node.RepeatColumns,node.RepeatRows,node.RepeatMode)); break;
                case ModuleKind.Report:
                    var reportEdges=graph.Edges.Where(e=>e.To==id && e.Port=="report").ToArray();
                    if(reportEdges.Length==0) throw new InvalidOperationException("حداقل یک ماژول را به ورودی گزارش وصل کنید.");
                    var reports=new List<GraphReportItem>();
                    foreach(var edge in reportEdges) { var sourceNode=nodes[edge.From]; var value=Evaluate(edge.From); reports.Add(new GraphReportItem(edge.From,sourceNode.Kind,sourceNode.Label,value,ReportDetails(sourceNode,value))); }
                    result=new(ModuleData.Report,reports.Select(r=>r.Value.Image).FirstOrDefault(v=>v is not null),ReportItems:reports); break;
                case ModuleKind.Colors:
                    var image = Input("image").Image!; PaletteResult palette;
                    if (node.ColorEdit is { } edit) { if (edit.Palette.Width != image.PixelWidth || edit.Palette.Height != image.PixelHeight || edit.InputHash != ImageFingerprint(image)) throw new InvalidOperationException("ورودی مدیریت رنگ تغییر کرده است؛ ماژول رنگ را باز و دوباره اعمال کنید."); palette=edit.Palette; }
                    else palette = DesignProcessor.Prepare(image,image.PixelWidth,image.PixelHeight,node.ColorLimit);
                    result = new(ModuleData.Palette,DesignProcessor.RenderColors(palette),palette); break;
                case ModuleKind.Weaves:
                case ModuleKind.CreateWeave:
                    if (node.Weaves.Count == 0) throw new InvalidOperationException("Add at least one weave to the module.");
                    result = new(ModuleData.Weaves, Fabric3DWindow.PatternBitmap(node.Weaves[0]), Weaves: node.Weaves); break;
                case ModuleKind.ApplyWeaves:
                    var p = Input("palette").Palette!; var patterns = Input("weaves").Weaves!;
                    var assigned = p.Colors.Select(c => { if(node.CustomWeaves.TryGetValue(c,out var custom)) return (WeavePattern?)custom; int k = node.WeaveAssignments.GetValueOrDefault(c,node.DefaultWeaveIndex); if ((uint)k >= (uint)patterns.Count) throw new ArgumentException("A color refers to a missing weave."); return (WeavePattern?)patterns[k]; }).ToArray();
                    result = new(ModuleData.Fabric, DesignProcessor.Render(p,assigned)); break;
                case ModuleKind.Selvedge:
                    var edgeInput = Input("fabric"); if (edgeInput.Composition is { SignalHooks.Length: > 0 }) throw new InvalidOperationException("Add selvedges before weft signal placement.");
                    var body = edgeInput.Image!; var edgeMap = MachineComposer.Compose(body,MachineComposer.BeforeWeft(node.Machine));
                    result = new(ModuleData.Fabric,edgeMap.Image,Composition:edgeMap); break;
                case ModuleKind.Floats:
                    var input = Input("fabric"); var view = input.Composition is null ? input.Image! : CompositionView.Fabric(input.Composition);
                    if(node.FloatImagePng is { } editedPng) { if(node.FloatInputHash!=ImageFingerprint(view)) throw new InvalidOperationException("ورودی فلوت تغییر کرده است؛ ماژول را دوباره اعمال کنید."); input=ApplyFloatEdit(input,editedPng); view=input.Composition is null ? input.Image! : CompositionView.Fabric(input.Composition); }
                    byte[] map = DesignAnalysis.ReadBinary(view);
                    var runs = DesignAnalysis.FindFloats(map,view.PixelWidth,view.PixelHeight,node.FloatMinimum,node.FloatWarp ? FloatDirection.Warp : FloatDirection.Weft);
                    if (!node.AutoCorrect) result = input with { FloatCount = runs.Count };
                    else
                    {
                        int left=input.Composition?.LeftWidth ?? 0, right=input.Composition?.RightWidth ?? 0;
                        var plan = FloatAutoCorrection.Plan(runs,left,view.PixelWidth-left-right,view.PixelHeight,Array.Empty<int>()); var all = DesignAnalysis.ReadBinary(input.Image!); int dx = (input.Composition?.FabricStart ?? 0)+left;
                        foreach (var point in plan.Points) { int k = point.Y * input.Image!.PixelWidth + dx + point.X; all[k] = all[k] == 0 ? (byte)255 : (byte)0; }
                        var corrected = BitmapSource.Create(input.Image!.PixelWidth,input.Image.PixelHeight,96,96,PixelFormats.Gray8,null,all,input.Image.PixelWidth); corrected.Freeze();
                        result = input with { Image = corrected, Composition = input.Composition is null ? null : input.Composition with { Image = corrected }, FloatCount = runs.Count };
                    } break;
                case ModuleKind.Tension:
                    var original = Input("fabric"); var woven = original.Composition is null ? original.Image! : CompositionView.Fabric(original.Composition);
                    result = original with { Analysis = DesignAnalysis.Analyze(DesignAnalysis.ReadBinary(woven),woven.PixelWidth,woven.PixelHeight) }; break;
                case ModuleKind.Weft:
                    var beforeWeft = Input("fabric"); if (beforeWeft.Composition is { SignalHooks.Length: > 0 }) throw new InvalidOperationException("This branch already has weft signal columns.");
                    var upstream = beforeWeft.Image!; var setup=node.Machine; var machineEdge=graph.Edges.SingleOrDefault(e=>e.To==id && e.Port=="machine");
                    if(machineEdge is not null) { var profile=Evaluate(machineEdge.From).Machine ?? throw new InvalidOperationException("تنظیم ماشین موجود نیست."); setup=profile with { AbsoluteLayout=true, BoxEnabled=node.Machine.BoxEnabled, WeftCount=node.Machine.WeftCount, Sequence=node.Machine.Sequence, StartFromBottom=node.Machine.StartFromBottom }; }
                    var composed = MachineComposer.Compose(upstream,setup);
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
    public static ModuleValue ApplyFloatEdit(ModuleValue input,string png)
    {
        var original=input.Composition is null ? input.Image! : CompositionView.Fabric(input.Composition);
        byte[] bytes=Convert.FromBase64String(png); if(bytes.Length>ImageLoader.MaxFileBytes) throw new InvalidDataException("Float edit is too large.");
        using var stream=new MemoryStream(bytes); var image=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0]; image.Freeze();
        if(image.PixelWidth!=original.PixelWidth || image.PixelHeight!=original.PixelHeight) throw new InvalidDataException("Float edit dimensions differ.");
        var changed=DesignAnalysis.ReadBinary(image); var before=DesignAnalysis.ReadBinary(original); int left=input.Composition?.LeftWidth ?? 0, right=input.Composition?.RightWidth ?? 0;
        for(int y=0;y<original.PixelHeight;y++) for(int x=0;x<original.PixelWidth;x++) if((x<left || x>=original.PixelWidth-right) && before[y*original.PixelWidth+x]!=changed[y*original.PixelWidth+x]) throw new InvalidDataException("Cannot edit selvedge or signal columns in float mode.");
        if(input.Composition is null) return input with { Image=image };
        var full=DesignAnalysis.ReadBinary(input.Image!); int width=input.Image!.PixelWidth, start=input.Composition.FabricStart;
        for(int y=0;y<original.PixelHeight;y++) Buffer.BlockCopy(changed,y*original.PixelWidth,full,y*width+start,original.PixelWidth);
        var combined=BitmapSource.Create(width,original.PixelHeight,96,96,PixelFormats.Gray8,null,full,width); combined.Freeze(); return input with { Image=combined,Composition=input.Composition with { Image=combined } };
    }
    private static string ReportDetails(ModuleNode node, ModuleValue value)
    {
        var lines=new List<string> { "نوع: " + node.Kind };
        if(!string.IsNullOrWhiteSpace(node.Label)) lines.Add("نام: " + node.Label);
        if(value.Image is { } image) lines.Add($"ابعاد خروجی: {image.PixelWidth} × {image.PixelHeight} پیکسل");
        if(value.Palette is { } p) { lines.Add($"رنگ‌ها: {p.Colors.Length} / رنگ‌های منبع: {p.SourceColorCount}"); for(int i=0;i<Math.Min(20,p.Colors.Length);i++) lines.Add($"#{p.Colors[i]:X6} — {p.Counts[i]} پیکسل"); if(p.Colors.Length>20) lines.Add($"و {p.Colors.Length-20} رنگ دیگر"); }
        if(value.Weaves is { } weaves) { lines.Add($"تعداد بافت: {weaves.Count}"); foreach(var name in node.WeaveNames.Take(20)) lines.Add("بافت: " + name); }
        if(node.Kind==ModuleKind.Floats) lines.Add($"فلوت: {value.FloatCount} · حداقل طول {node.FloatMinimum} · {(node.FloatWarp ? "تار" : "پود")}");
        if(value.Analysis is { } a) lines.Add($"میانگین شاخص تار: {a.Mean:0.##} (تحلیل نسبی، نه اندازه‌گیری فیزیکی)");
        if(node.Kind==ModuleKind.Repeat) lines.Add($"تکرار: {node.RepeatColumns} × {node.RepeatRows} · {node.RepeatMode}");
        if(node.Kind==ModuleKind.Crop && node.Crop is { } c) lines.Add($"برش: ({c.X}, {c.Y}) · {c.Width} × {c.Height}");
        if(node.Kind==ModuleKind.Resize) lines.Add($"اندازه درخواستی: {node.Width} × {node.Height} (صفر یعنی اندازه ورودی)");
        if(node.Kind==ModuleKind.Colors) lines.Add($"حداکثر رنگ: {node.ColorLimit} · رنگ‌های قفل‌شده: {node.ColorEdit?.Locks.Count(x=>x) ?? 0}");
        if(node.Kind==ModuleKind.ApplyWeaves) lines.Add($"تخصیص‌های انتخابی رنگ به بافت: {node.WeaveAssignments.Count}؛ رنگ‌های دیگر از بافت اول استفاده می‌کنند.");
        if(node.Kind==ModuleKind.Selvedge) lines.Add($"کناره چپ: {node.Machine.LeftWidth} ({node.Machine.LeftName}) · راست: {node.Machine.RightWidth} ({node.Machine.RightName})");
        if(node.Kind==ModuleKind.Floats) lines.Add("اصلاح خودکار: " + (node.AutoCorrect ? "فعال" : "غیرفعال"));
        if(node.Kind==ModuleKind.Weft) lines.Add($"تعداد پود: {node.Machine.WeftCount} · توالی: {node.Machine.Sequence} · شروع طرح: {PositionNumbers.ToDisplay(node.Machine.DesignStart)} · شروع پود: {PositionNumbers.ToDisplay(node.Machine.WeftStart)}");
        if(node.Kind==ModuleKind.Simulation) lines.Add($"حالت: {node.Simulation.Mode} · نمره تار: {node.Simulation.WarpCount} · تراکم تار/پود: {node.Simulation.WarpDensity} / {node.Simulation.WeftDensity} · رنگ تار/پود: {node.Simulation.WarpColor} / {node.Simulation.WeftColor} (نمایش تقریبی)");
        if(node.Kind==ModuleKind.Machine) lines.Add($"قالب: {node.MachineFormat} · خروجی ماشین آزمایشی؛ نگاشت و قطبیت باید مستقل بررسی شود.");
        if(value.DesignFields is { } fields) foreach(var field in fields) lines.Add(field.Title + ": " + field.Text);
        if(node.Kind==ModuleKind.MachineDefinition) lines.Add($"سازنده: {node.Manufacturer} · کل زه: {node.Machine.TotalHooks} · شروع طرح: {PositionNumbers.ToDisplay(node.Machine.DesignStart)} · شروع پود: {PositionNumbers.ToDisplay(node.Machine.WeftStart)}");
        if(value.ReportItems is { } nested) lines.Add($"تعداد ورودی گزارش: {nested.Count}");
        return string.Join("\n",lines);
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
        if (!path.EndsWith(".sam",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".jgw",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Graph project must use .sam or .jgw.");
        if (File.Exists(path) && !IsGraph(path)) throw new IOException("Cannot overwrite a legacy or unrecognized project with a graph."); graph.Validate();
        var json = JsonSerializer.SerializeToUtf8Bytes(new Envelope { Graph = graph }); if (json.Length > MaxBytes) throw new InvalidDataException("Graph project is too large.");
        string target = Path.GetFullPath(path), tmp = Path.Combine(Path.GetDirectoryName(target)!, ".jgw-"+Guid.NewGuid().ToString("N")+".tmp");
        try { using (var file = File.Create(tmp)) using (var zip = new ZipArchive(file,ZipArchiveMode.Create)) { var entry = zip.CreateEntry("graph.json",CompressionLevel.Optimal); using var stream = entry.Open(); stream.Write(json); } File.Move(tmp,target,true); } finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static bool IsGraph(string path)
    {
        using var file=File.OpenRead(path); if(file.Length>MaxBytes) throw new InvalidDataException("Project is too large.");
        using var zip=new ZipArchive(file,ZipArchiveMode.Read); return zip.Entries.Count==1 && zip.Entries[0].FullName=="graph.json";
    }
    public static ModuleGraph Load(string path)
    {
        using var file = File.OpenRead(path); if (file.Length > MaxBytes) throw new InvalidDataException("Graph project is too large."); using var zip = new ZipArchive(file,ZipArchiveMode.Read);
        if (zip.Entries.Count != 1 || zip.Entries[0].FullName != "graph.json" || zip.Entries[0].Length > MaxBytes) throw new InvalidDataException("Invalid graph project archive.");
        using var stream = zip.Entries[0].Open(); using var bounded = new MemoryStream(); byte[] buffer = new byte[8192]; int read; while ((read=stream.Read(buffer))>0) { if (bounded.Length + read > MaxBytes) throw new InvalidDataException("Graph is too large."); bounded.Write(buffer,0,read); }
        var envelope = JsonSerializer.Deserialize<Envelope>(bounded.ToArray()) ?? throw new InvalidDataException("Invalid graph project."); if (envelope.Version != 1 || envelope.Graph is null) throw new InvalidDataException("Unsupported graph project."); envelope.Graph.Validate(); return envelope.Graph;
    }
}
