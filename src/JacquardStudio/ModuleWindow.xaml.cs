using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace JacquardStudio;

public partial class ModuleWindow : Window
{
    private ModuleGraph graph = new();
    private readonly Dictionary<Guid, Border> cards = new();
    private IReadOnlyDictionary<Guid,ModuleValue> results = new Dictionary<Guid,ModuleValue>();
    private Guid? selected, connecting, dragging;
    private Point dragStart; private double dragX, dragY, zoom = 1;
    private bool dirty, busy, closing;
    private string? projectPath;
    private readonly WeaveBank bank = new();
    private static readonly (ModuleKind kind, string name, string icon, string color, string subtitle)[] Types = {
        (ModuleKind.Load,"بارگذاری فایل","\uE8B7","#47D4B3","IMAGE SOURCE"),
        (ModuleKind.Resize,"تغییر اندازه","\uE8A3","#61A9F7","RESIZE"),
        (ModuleKind.Colors,"مدیریت رنگ","\uE790","#E2A766","PALETTE"),
        (ModuleKind.Weaves,"بانک بافت پروژه","\uE8A5","#BA91F4","WEAVE LIBRARY"),
        (ModuleKind.ApplyWeaves,"بافت‌ریزی رنگ‌ها","\uE9D2","#BB94EE","WEAVE MAPPING"),
        (ModuleKind.Selvedge,"کناره","\uE8A1","#7CAAE3","SELVEDGES"),
        (ModuleKind.Floats,"فلوت‌گیری","\uE7C3","#E89B73","FLOAT CHECK"),
        (ModuleKind.Tension,"تحلیل کشش","\uE9D9","#72C8C5","TENSION"),
        (ModuleKind.Weft,"پودگذاری","\uE8FD","#E2AD69","BOX MOTION"),
        (ModuleKind.Simulation,"شبیه‌سازی پارچه","\uE7F4","#8DBAF0","SIMULATION"),
        (ModuleKind.Machine,"خروجی ماشین","\uE950","#F19F7A","MACHINE OUTPUT")
    };
    private static (ModuleKind kind, string name, string icon, string color, string subtitle) Spec(ModuleKind kind) => Types.Single(t => t.kind == kind);
    private static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    public ModuleWindow()
    {
        InitializeComponent();
        foreach (var type in Types)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = new TextBlock { Text = type.icon, FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = Brush(type.color), FontSize = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }; row.Children.Add(icon);
            var labels = new StackPanel(); labels.Children.Add(new TextBlock { Text = type.name, FontSize = 12, Foreground = Brush("#DFEAF3") }); labels.Children.Add(new TextBlock { Text = type.subtitle, FontSize = 9, Foreground = Brush("#718A9E"), FlowDirection = FlowDirection.LeftToRight }); Grid.SetColumn(labels,1); row.Children.Add(labels);
            var button = new Button { Content = row, Background = Brush("#1B2A3A"), BorderBrush = Brush("#2C4153"), Padding = new Thickness(6), Margin = new Thickness(0,2,0,2), HorizontalContentAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.LeftToRight, ToolTip = "افزودن " + type.name };
            var kind = type.kind; button.Click += (_,_) => { if (busy) return; var node = graph.Add(kind, 60 + (graph.Nodes.Count % 6) * 250, 80 + (graph.Nodes.Count / 6) * 160); selected = node.Id; Changed(); RenderGraph(); Inspect(); };
            ModuleLibrary.Children.Add(button);
        }
        CreateDefaultGraph(); dirty = false; RenderGraph(); Inspect();
    }
    private void CreateDefaultGraph()
    {
        graph = new ModuleGraph(); var load=graph.Add(ModuleKind.Load,80,100); var resize=graph.Add(ModuleKind.Resize,330,100); var colors=graph.Add(ModuleKind.Colors,580,100);
        var weaves=graph.Add(ModuleKind.Weaves,580,330); var apply=graph.Add(ModuleKind.ApplyWeaves,830,100); var floats=graph.Add(ModuleKind.Floats,1080,100); var tension=graph.Add(ModuleKind.Tension,1330,100);
        var weft=graph.Add(ModuleKind.Weft,1580,100); var machine=graph.Add(ModuleKind.Machine,1830,100); var simulation=graph.Add(ModuleKind.Simulation,1830,330);
        weft.Machine = new MachineSetup(AbsoluteLayout:true,BoxEnabled:true,DesignStart:"1",WeftStart:"0",WeftCount:"1");
        foreach (var (a,b,port) in new[] { (load,resize,"image"),(resize,colors,"image"),(colors,apply,"palette"),(weaves,apply,"weaves"),(apply,floats,"fabric"),(floats,tension,"fabric"),(tension,weft,"fabric"),(weft,machine,"fabric"),(weft,simulation,"fabric") }) graph.Connect(a.Id,b.Id,port);
        selected = load.Id; connecting = null; projectPath = null; results = new Dictionary<Guid,ModuleValue>();
    }
    private void Changed() { dirty = true; results = new Dictionary<Guid,ModuleValue>(); UpdateTitle(); }
    private void UpdateTitle() => Title = $"Jacquard Studio · {(projectPath is null ? "پروژه جدید" : System.IO.Path.GetFileName(projectPath))}{(dirty ? " *" : "")}";
    private static string PortName(string port) => port switch { "image" => "تصویر", "palette" => "رنگ‌ها", "weaves" => "بافت‌ها", _ => "پارچه" };
    private void RenderGraph()
    {
        NodeLayer.Children.Clear(); cards.Clear();
        foreach (var node in graph.Nodes)
        {
            var spec = Spec(node.Kind); var card = new Border { Width = 210, Height = 132, Background = Brush("#192838"), BorderBrush = Brush(node.Id == selected ? "#48DAB7" : "#354B5E"), BorderThickness = new Thickness(node.Id == selected ? 2 : 1), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, ToolTip = "انتخاب و جابه‌جایی ماژول" };
            var inner = new Grid(); inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) }); inner.RowDefinitions.Add(new RowDefinition());
            var header = new Grid { Background = Brush("#203447") }; header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) }); header.ColumnDefinitions.Add(new ColumnDefinition());
            header.Children.Add(new TextBlock { Text=spec.icon, FontFamily=new FontFamily("Segoe MDL2 Assets"), Foreground=Brush(spec.color), FontSize=20, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center });
            var text = new StackPanel { VerticalAlignment=VerticalAlignment.Center, FlowDirection=FlowDirection.RightToLeft, Margin=new Thickness(3,0,13,0) }; text.Children.Add(new TextBlock { Text=string.IsNullOrWhiteSpace(node.Label) ? spec.name : node.Label, FontWeight=FontWeights.Bold, FontSize=12, TextTrimming=TextTrimming.CharacterEllipsis }); text.Children.Add(new TextBlock { Text=spec.subtitle, FontSize=9, Foreground=Brush("#9CB1BF"), FlowDirection=FlowDirection.LeftToRight }); Grid.SetColumn(text,1); header.Children.Add(text); inner.Children.Add(header);
            var portGrid = new Grid(); Grid.SetRow(portGrid,1); inner.Children.Add(portGrid); var inputs = ModuleGraph.Inputs(node.Kind).Keys.ToArray();
            for (int i=0; i<inputs.Length; i++)
            {
                string port = inputs[i]; int position = i;
                var label = new TextBlock { Text=PortName(port), FontSize=10, Foreground=Brush("#A5B8C6"), Margin=new Thickness(24, 7 + i*27,0,0), VerticalAlignment=VerticalAlignment.Top, FlowDirection=FlowDirection.RightToLeft, HorizontalAlignment=HorizontalAlignment.Left }; portGrid.Children.Add(label);
                var inputButton = PortButton("#6DA2CF", "اتصال ورودی " + PortName(port)); inputButton.HorizontalAlignment=HorizontalAlignment.Left; inputButton.VerticalAlignment=VerticalAlignment.Top; inputButton.Margin=new Thickness(2,6+i*27,0,0);
                inputButton.Click += (_,e) => { e.Handled=true; if (busy) return; if (connecting is not Guid from) { selected=node.Id; Inspect(); Status.Text="ابتدا نقطه خروجی یک ماژول و سپس این ورودی را انتخاب کنید."; return; } try { graph.Connect(from,node.Id,port); connecting=null; selected=node.Id; Changed(); RenderGraph(); Inspect(); Status.Text="اتصال برقرار شد."; } catch (ArgumentException ex) { Status.Text=ex.Message; } };
                inputButton.ContextMenu = new ContextMenu(); var removeWire = new MenuItem { Header="قطع اتصال ورودی" }; removeWire.Click += (_,_) => { graph.Disconnect(node.Id,port); Changed(); RenderGraph(); Inspect(); }; inputButton.ContextMenu.Items.Add(removeWire); portGrid.Children.Add(inputButton);
            }
            var output = PortButton(spec.color,"انتخاب خروجی برای اتصال"); output.HorizontalAlignment=HorizontalAlignment.Right; output.VerticalAlignment=VerticalAlignment.Top; output.Margin=new Thickness(0,7,2,0); output.Click += (_,e) => { e.Handled=true; if (busy) return; connecting=node.Id; selected=node.Id; RenderGraph(); Inspect(); Status.Text="خروجی انتخاب شد؛ اکنون روی ورودی سازگار باکس مقصد بزنید."; }; portGrid.Children.Add(output);
            var outLabel = new TextBlock { Text="خروجی", FontSize=10, Foreground=Brush("#A5B8C6"), VerticalAlignment=VerticalAlignment.Top, HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,9,24,0) }; portGrid.Children.Add(outLabel);
            card.Child=inner; Canvas.SetLeft(card,node.X); Canvas.SetTop(card,node.Y); NodeLayer.Children.Add(card); cards[node.Id]=card;
            card.MouseLeftButtonDown += (_,e) => { if (busy || e.OriginalSource is DependencyObject source && FindButton(source)) return; selected=node.Id; dragging=node.Id; dragStart=e.GetPosition(NodeLayer); dragX=node.X; dragY=node.Y; card.CaptureMouse(); RenderSelection(); Inspect(); e.Handled=true; };
            card.MouseMove += (_,e) => { if (dragging!=node.Id || e.LeftButton!=MouseButtonState.Pressed) return; var now=e.GetPosition(NodeLayer); node.X=Math.Clamp(dragX+now.X-dragStart.X,0,Math.Max(0,GraphCanvas.Width-210)); node.Y=Math.Clamp(dragY+now.Y-dragStart.Y,0,Math.Max(0,GraphCanvas.Height-132)); Canvas.SetLeft(card,node.X); Canvas.SetTop(card,node.Y); DrawWires(); };
            card.MouseLeftButtonUp += (_,e) => { if (dragging != node.Id) return; dragging=null; card.ReleaseMouseCapture(); if (node.X != dragX || node.Y != dragY) { dirty=true; UpdateTitle(); } e.Handled=true; };
            card.LostMouseCapture += (_,_) => { dragging=null; };
            card.ContextMenu=new ContextMenu(); var delete=new MenuItem { Header="حذف ماژول" }; delete.Click+=(_,_)=>RemoveNode(node.Id); card.ContextMenu.Items.Add(delete);
        }
        DrawWires();
    }
    private static bool FindButton(DependencyObject? item) { while (item is not null) { if (item is Button) return true; item = VisualTreeHelper.GetParent(item); } return false; }
    private static Button PortButton(string color,string tooltip) => new() { Width=18,Height=18,Padding=new Thickness(0),Margin=new Thickness(0), Background=Brush(color), BorderBrush=Brush("#D0E5F0"), BorderThickness=new Thickness(1), ToolTip=tooltip };
    private void RenderSelection() { foreach (var pair in cards) { pair.Value.BorderBrush=Brush(pair.Key==selected ? "#48DAB7" : "#354B5E"); pair.Value.BorderThickness=new Thickness(pair.Key==selected ? 2 : 1); } }
    private void DrawWires()
    {
        WireLayer.Children.Clear(); foreach (var edge in graph.Edges)
        {
            var from=graph.Nodes.Single(n=>n.Id==edge.From); var to=graph.Nodes.Single(n=>n.Id==edge.To); int port=ModuleGraph.Inputs(to.Kind).Keys.ToList().IndexOf(edge.Port);
            var start=new Point(from.X+201,from.Y+68); var end=new Point(to.X+11,to.Y+67+port*27); double bend=Math.Max(60,Math.Abs(end.X-start.X)*.45);
            var figure=new PathFigure { StartPoint=start }; figure.Segments.Add(new BezierSegment(new Point(start.X+bend,start.Y),new Point(end.X-bend,end.Y),end,true));
            var geometry=new PathGeometry(); geometry.Figures.Add(figure); WireLayer.Children.Add(new System.Windows.Shapes.Path { Data=geometry, Stroke=Brush(Spec(from.Kind).color), StrokeThickness=3, Opacity=.82 });
        }
    }
    private void RemoveNode(Guid id) { if (busy) return; graph.Remove(id); if (selected==id) selected=null; if (connecting==id) connecting=null; Changed(); RenderGraph(); Inspect(); }
    private void Delete_Click(object sender,RoutedEventArgs e) { if (selected is Guid id) RemoveNode(id); }
    private void ZoomIn_Click(object sender,RoutedEventArgs e) => SetZoom(zoom*1.25);
    private void ZoomOut_Click(object sender,RoutedEventArgs e) => SetZoom(zoom/1.25);
    private void SetZoom(double value) { zoom=Math.Clamp(value,.5,2); GraphCanvas.LayoutTransform=new ScaleTransform(zoom,zoom); ZoomLabel.Text=$"{zoom*100:0}%"; }
    private ModuleNode? Current => selected is Guid id ? graph.Nodes.FirstOrDefault(n=>n.Id==id) : null;
    private void Inspect()
    {
        Inspector.Children.Clear(); var node=Current; if (node is null) { Inspector.Children.Add(Note("یک ماژول را انتخاب کنید.")); return; }
        var spec=Spec(node.Kind); Inspector.Children.Add(new TextBlock { Text=spec.name, FontSize=19, FontWeight=FontWeights.Bold, Foreground=Brush(spec.color), Margin=new Thickness(0,0,0,4) }); Inspector.Children.Add(Note(spec.subtitle));
        Inspector.Children.Add(Note("ورودی‌ها: " + (ModuleGraph.Inputs(node.Kind).Count==0 ? "بدون ورودی" : string.Join(" · ",ModuleGraph.Inputs(node.Kind).Keys.Select(p => PortName(p)+(graph.Edges.Any(e=>e.To==node.Id && e.Port==p) ? " ✓" : " ○"))))));
        void Field(string label,string value,Action<string> assign)
        {
            Inspector.Children.Add(new TextBlock { Text=label, FontSize=12, Margin=new Thickness(0,10,0,4) }); var input=new TextBox { Text=value, Padding=new Thickness(7), FlowDirection=FlowDirection.LeftToRight };
            input.LostKeyboardFocus += (_,_) => { if (input.Text!=value) { try { assign(input.Text); Changed(); value=input.Text; } catch (ArgumentException ex) { input.Text=value; MessageBox.Show(this,ex.Message); } } }; Inspector.Children.Add(input);
        }
        void Button(string text,Action action) { var b=new Button { Content=text, Margin=new Thickness(0,8,0,0), Padding=new Thickness(8), Background=Brush("#263B4C"), BorderBrush=Brush("#385366") }; b.Click+=(_,_)=>action(); Inspector.Children.Add(b); }
        switch (node.Kind)
        {
            case ModuleKind.Load:
                Inspector.Children.Add(Note(node.ImagePng is null ? "تصویری انتخاب نشده است." : "تصویر در پروژه ذخیره می‌شود."));
                Button("انتخاب تصویر / PDF", async () => await ChooseImage(node)); break;
            case ModuleKind.Resize:
                Field("عرض (۰ = عرض ورودی)",node.Width.ToString(),s => node.Width=Parse(s,0)); Field("ارتفاع (۰ = ارتفاع ورودی)",node.Height.ToString(),s => node.Height=Parse(s,0)); break;
            case ModuleKind.Colors:
                Field("حداکثر رنگ ۱ تا ۲۵۶",node.ColorLimit.ToString(),s=>node.ColorLimit=Parse(s,0)); break;
            case ModuleKind.Weaves:
                Inspector.Children.Add(Note($"{node.Weaves.Count} بافت در این ماژول پروژه ذخیره شده است."));
                for(int i=0;i<node.Weaves.Count;i++) { int index=i; var row=new DockPanel { Margin=new Thickness(0,4,0,0) }; var del=new Button { Content="×", Padding=new Thickness(6,1,6,1), Background=Brush("#593544") }; DockPanel.SetDock(del,Dock.Left); row.Children.Add(del); del.Click+=(_,_)=> { node.Weaves.RemoveAt(index); node.WeaveNames.RemoveAt(index); foreach(var edge in graph.Edges.Where(e=>e.From==node.Id && e.Port=="weaves")) { var target=graph.Nodes.Single(n=>n.Id==edge.To); foreach(int key in target.WeaveAssignments.Keys.ToArray()) { int old=target.WeaveAssignments[key]; target.WeaveAssignments[key] = old == index ? 0 : old > index ? old-1 : old; } } Changed(); Inspect(); }; row.Children.Add(new TextBlock { Text=$"{i+1}. {node.WeaveNames[i]} · {node.Weaves[i].Width}×{node.Weaves[i].Height}", VerticalAlignment=VerticalAlignment.Center, TextTrimming=TextTrimming.CharacterEllipsis }); Inspector.Children.Add(row); }
                Button("افزودن از بانک بافت",()=>ChooseWeaves(node)); Button("ایجاد بافت جدید",()=>CreateWeave(node)); break;
            case ModuleKind.ApplyWeaves:
                Inspector.Children.Add(Note("هر رنگ به بافت انتخابی متصل می‌شود؛ پیش‌فرض، بافت نخست است. پس از اتصال ورودی‌ها اجرا کنید."));
                if (graph.Edges.FirstOrDefault(e=>e.To==node.Id && e.Port=="palette") is { } pe && graph.Edges.FirstOrDefault(e=>e.To==node.Id && e.Port=="weaves") is { } we && results.TryGetValue(pe.From,out var pv) && results.TryGetValue(we.From,out var wv))
                {
                    var weaveNode=graph.Nodes.Single(n=>n.Id==we.From);
                    foreach (int color in pv.Palette!.Colors)
                    {
                        int c=color; var row=new DockPanel { Margin=new Thickness(0,4,0,0) }; var swatch=new Border { Width=25,Height=25,Background=Brush($"#{c:X6}"),Margin=new Thickness(5) }; DockPanel.SetDock(swatch,Dock.Right); row.Children.Add(swatch);
                        var combo=new ComboBox { ItemsSource=weaveNode.WeaveNames, SelectedIndex=Math.Clamp(node.WeaveAssignments.GetValueOrDefault(c),0,wv.Weaves!.Count-1), MinWidth=120 }; combo.SelectionChanged+=(_,_)=>{ if (combo.SelectedIndex>=0) { node.WeaveAssignments[c]=combo.SelectedIndex; Changed(); } }; row.Children.Add(combo); Inspector.Children.Add(row);
                    }
                } break;
            case ModuleKind.Selvedge:
                Field("عرض کناره چپ",node.Machine.LeftWidth,s=>node.Machine=node.Machine with { LeftWidth=s, LeftPattern=PatternLibrary.Presets[0].Pattern });
                Field("عرض کناره راست",node.Machine.RightWidth,s=>node.Machine=node.Machine with { RightWidth=s, RightPattern=PatternLibrary.Presets[0].Pattern }); Inspector.Children.Add(Note("بافت پیش‌فرض کناره: بافت نخست بانک.")); break;
            case ModuleKind.Floats:
                Field("حداقل طول فلوت",node.FloatMinimum.ToString(),s=>node.FloatMinimum=Parse(s,0));
                var dir=new ComboBox { ItemsSource=new[]{"تار / Warp","پود / Weft"},SelectedIndex=node.FloatWarp?0:1, Margin=new Thickness(0,8,0,0) }; dir.SelectionChanged+=(_,_)=>{node.FloatWarp=dir.SelectedIndex==0;Changed();}; Inspector.Children.Add(dir);
                var check=new CheckBox { Content="اصلاح خودکار مرکز فلوت‌ها", IsChecked=node.AutoCorrect, Margin=new Thickness(0,10,0,0) }; check.Click+=(_,_)=>{node.AutoCorrect=check.IsChecked==true;Changed();}; Inspector.Children.Add(check); break;
            case ModuleKind.Tension: Inspector.Children.Add(Note("تحلیل نسبی تار؛ مقدار فیزیکی کشش نیست. خروجی پارچه بدون تغییر ادامه می‌یابد.")); break;
            case ModuleKind.Weft:
                Field("تعداد پود (۰ = بدون فرمان)",node.Machine.WeftCount,s=>{ int count=Parse(s,0); if (count is < 0 or > 256) throw new ArgumentException("تعداد پود باید بین ۰ تا ۲۵۶ باشد."); node.Machine=node.Machine with { AbsoluteLayout=true,BoxEnabled=count>0,WeftCount=s,DesignStart=count.ToString(),WeftStart="0" }; });
                Field("توالی پودها",node.Machine.Sequence,s=>node.Machine=node.Machine with { Sequence=s }); Field("تعداد کل تار (خالی = خودکار)",node.Machine.TotalHooks,s=>node.Machine=node.Machine with { TotalHooks=s }); Inspector.Children.Add(Note("قلاب‌ها و قطبیت باید مستقل روی دستگاه بررسی شوند.")); break;
            case ModuleKind.Simulation:
                Field("رنگ تار #RRGGBB",node.Simulation.WarpColor,s=>node.Simulation=node.Simulation with { WarpColor=s }); Field("رنگ پود #RRGGBB",node.Simulation.WeftColor,s=>node.Simulation=node.Simulation with { WeftColor=s });
                var mode=new ComboBox { ItemsSource=new[]{"معمولی","پرز نخ"},SelectedIndex=Math.Clamp(node.Simulation.Mode,0,1),Margin=new Thickness(0,10,0,0) }; mode.SelectionChanged+=(_,_)=>{node.Simulation=node.Simulation with { Mode=mode.SelectedIndex };Changed();}; Inspector.Children.Add(mode); break;
            case ModuleKind.Machine:
                Inspector.Children.Add(Note("خروجی آزمایشی JC5 / Bonas. ابتدا ماژول را اجرا و نقشه را بررسی کنید.")); Button("ذخیره فایل ماشین JC5",()=>ExportMachine(node)); break;
        }
        if (results.TryGetValue(node.Id,out var value))
        {
            Inspector.Children.Add(new TextBlock { Text="پیش‌نمایش خروجی",FontSize=13,FontWeight=FontWeights.Bold,Margin=new Thickness(0,18,0,6) });
            if (value.Image is not null) { var image=new Image { Source=value.Image,MaxHeight=220,Stretch=Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor); Inspector.Children.Add(new Border { Background=Brush("#0C1620"),Padding=new Thickness(8),CornerRadius=new CornerRadius(8),Child=image }); }
            if (value.Palette is not null) Inspector.Children.Add(Note($"{value.Palette.Width} × {value.Palette.Height} · {value.Palette.Colors.Length} رنگ"));
            if (node.Kind==ModuleKind.Floats) Inspector.Children.Add(Note($"تعداد فلوت‌های یافته‌شده: {value.FloatCount}"));
            if (node.Kind==ModuleKind.Tension && value.Analysis is { } analysis) Inspector.Children.Add(Note($"میانگین: {analysis.Mean:0.##} · دامنه انحراف: {analysis.DeviationPercent.Min():0.#}% تا {analysis.DeviationPercent.Max():0.#}%"));
        }
    }
    private static TextBlock Note(string text)=>new() { Text=text,TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=Brush("#91AABB"),Margin=new Thickness(0,4,0,4) };
    private static int Parse(string text,int fallback) => NumberInput.TryInt(text,out int n) ? n : throw new ArgumentException("عدد واردشده معتبر نیست.");
    private async Task ChooseImage(ModuleNode node)
    {
        var dialog=new OpenFileDialog { Filter="Images / PDF|*.bmp;*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.ico;*.pdf;*.ai|All files|*.*" }; if (dialog.ShowDialog(this)!=true) return;
        try { busy=true; Status.Text="در حال بارگذاری تصویر…"; var loaded=await Task.Run(()=>ImageLoader.Load(dialog.FileName,0)); node.ImagePng=await Task.Run(()=>ModuleExecutor.EmbedImage(loaded.Image)); node.Label=System.IO.Path.GetFileName(dialog.FileName); Changed(); RenderGraph(); Inspect(); Status.Text="تصویر در ماژول ثبت شد."; } catch(Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException) { MessageBox.Show(this,ex.Message); } finally { busy=false; }
    }
    private void ChooseWeaves(ModuleNode node)
    {
        try
        {
            var items=bank.Load(out int skipped); var dialog=new Window { Title="انتخاب بافت از بانک",Width=450,Height=580,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft,Background=Brush("#192838") };
            var root=new DockPanel { Margin=new Thickness(15) }; var confirm=new Button { Content="افزودن بافت‌های انتخاب‌شده",Margin=new Thickness(0,10,0,0) }; DockPanel.SetDock(confirm,Dock.Bottom); root.Children.Add(confirm);
            var list=new ListBox { SelectionMode=SelectionMode.Multiple,ItemsSource=items,DisplayMemberPath=nameof(BankItem.Name) }; root.Children.Add(list); dialog.Content=root;
            confirm.Click+=(_,_)=>{ var chosen=list.SelectedItems.OfType<BankItem>().ToArray(); if (node.Weaves.Count+chosen.Length>256) { MessageBox.Show(dialog,"حداکثر ۲۵۶ بافت مجاز است."); return; } foreach(var item in chosen) { node.Weaves.Add(new WeavePattern(item.Pattern.Width,item.Pattern.Height,(byte[])item.Pattern.Values.Clone(),WeaveBank.CopyColors(item.Pattern.WeftColors))); node.WeaveNames.Add(item.Name); } if(chosen.Length>0) Changed(); dialog.DialogResult=true; };
            dialog.ShowDialog(); Inspect(); if(skipped>0) Status.Text=$"{skipped} بافت نامعتبر بانک نادیده گرفته شد.";
        } catch (Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException) { MessageBox.Show(this,ex.Message); }
    }
    private void CreateWeave(ModuleNode node)
    {
        if(node.Weaves.Count>=256) { MessageBox.Show(this,"حداکثر ۲۵۶ بافت مجاز است."); return; }
        var dialog=new Window { Title="ساخت بافت پروژه",Width=650,Height=620,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft,Background=Brush("#192838") };
        var root=new DockPanel { Margin=new Thickness(16) }; var bar=new StackPanel { Orientation=Orientation.Horizontal }; DockPanel.SetDock(bar,Dock.Top); root.Children.Add(bar);
        var width=new TextBox { Text="8",Width=45,Margin=new Thickness(4) }; var height=new TextBox { Text="8",Width=45,Margin=new Thickness(4) }; var name=new TextBox { Text="بافت پروژه",Width=120,Margin=new Thickness(4) }; bar.Children.Add(width); bar.Children.Add(height); bar.Children.Add(name);
        var editor=new WeaveEditor(); editor.SizeForScrolling(); var resize=new Button { Content="ساخت شبکه",Padding=new Thickness(6),Margin=new Thickness(4) }; bar.Children.Add(resize); resize.Click+=(_,_)=>{try { editor.Reset(Parse(width.Text,0),Parse(height.Text,0)); editor.SizeForScrolling(); } catch(ArgumentException ex) { MessageBox.Show(dialog,ex.Message); }};
        var bottom=new StackPanel { Orientation=Orientation.Horizontal }; DockPanel.SetDock(bottom,Dock.Bottom); root.Children.Add(bottom);
        foreach(string op in new[]{"plain","invert","clear"}) { var button=new Button { Content=op,Padding=new Thickness(7),Margin=new Thickness(4) }; button.Click+=(_,_)=>editor.ApplyHelper(op); bottom.Children.Add(button); }
        var save=new Button { Content="افزودن به پروژه",Padding=new Thickness(7),Margin=new Thickness(4) }; bottom.Children.Add(save); save.Click+=(_,_)=>{ if(string.IsNullOrWhiteSpace(name.Text) || name.Text.Length>100) { MessageBox.Show(dialog,"نام بافت باید ۱ تا ۱۰۰ حرف باشد."); return; } node.Weaves.Add(editor.Snapshot()); node.WeaveNames.Add(name.Text.Trim()); Changed(); dialog.DialogResult=true; };
        root.Children.Add(new ScrollViewer { Content=editor,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto }); dialog.Content=root; dialog.ShowDialog(); Inspect();
    }
    private async void Run_Click(object sender,RoutedEventArgs e)
    {
        if(busy || Current is not { } node) return; try { busy=true; ModuleLibrary.IsEnabled=false; GraphScroll.IsEnabled=false; Inspector.IsEnabled=false; Status.Text="در حال اجرای مسیر متصل…"; var evaluated=await Task.Run(()=>ModuleExecutor.Run(graph,node.Id)); results=evaluated; Status.Text="اجرای مسیر انتخاب‌شده کامل شد."; Inspect(); }
        catch(Exception ex) when(ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or UnauthorizedAccessException) { Status.Text="اجرای ماژول ناموفق بود."; MessageBox.Show(this,ex.Message,"خطای ماژول",MessageBoxButton.OK,MessageBoxImage.Warning); }
        finally { busy=false; ModuleLibrary.IsEnabled=true; GraphScroll.IsEnabled=true; Inspector.IsEnabled=true; }
    }
    private void ExportMachine(ModuleNode node)
    {
        if(!results.TryGetValue(node.Id,out var value) || value.Image is null) { MessageBox.Show(this,"ابتدا ماژول ماشین را اجرا کنید."); return; }
        var dialog=new SaveFileDialog { Filter="JC5 (*.jc5)|*.jc5",FileName="design.jc5",AddExtension=true }; if(dialog.ShowDialog(this)!=true) return;
        try { Jc5Codec.Save(value.Image,dialog.FileName,new[]{projectPath}.OfType<string>()); Status.Text="فایل آزمایشی ماشین ذخیره شد؛ پیش از تولید آن را بررسی کنید."; } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { MessageBox.Show(this,ex.Message); }
    }
    private bool ConfirmDiscard() { if(!dirty)return true; var result=MessageBox.Show(this,"تغییرات پروژه ذخیره نشده‌اند. ذخیره شوند؟","پروژه",MessageBoxButton.YesNoCancel,MessageBoxImage.Warning); return result==MessageBoxResult.No || result==MessageBoxResult.Yes && SaveGraph(false); }
    private void Legacy_Click(object sender,RoutedEventArgs e) { if(!busy) new MainWindow().Show(); }
    private void New_Click(object sender,RoutedEventArgs e) { if(busy || !ConfirmDiscard())return; CreateDefaultGraph(); dirty=false; UpdateTitle(); RenderGraph(); Inspect(); }
    private void Open_Click(object sender,RoutedEventArgs e)
    {
        if(busy)return; var dialog=new OpenFileDialog { Filter="Graph project (*.jgw)|*.jgw|Legacy SAM project (*.sam)|*.sam" }; if(dialog.ShowDialog(this)!=true || !ConfirmDiscard())return;
        try { if(dialog.FileName.EndsWith(".sam",StringComparison.OrdinalIgnoreCase)) { var legacy=new MainWindow(); legacy.Show(); legacy.OpenLegacyProject(dialog.FileName); Status.Text="پروژه SAM در محیط سازگار باز شد."; return; } var loaded=ModuleProjectFile.Load(dialog.FileName); graph=loaded; projectPath=dialog.FileName; selected=graph.Nodes.FirstOrDefault()?.Id; connecting=null; results=new Dictionary<Guid,ModuleValue>(); dirty=false; UpdateTitle(); RenderGraph(); Inspect(); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Text.Json.JsonException or NotSupportedException) { MessageBox.Show(this,ex.Message,"بازکردن پروژه"); }
    }
    private bool SaveGraph(bool saveAs)
    {
        string? target=projectPath; if(saveAs || target is null) { var dialog=new SaveFileDialog { Filter="Graph project (*.jgw)|*.jgw",FileName="project.jgw",AddExtension=true }; if(dialog.ShowDialog(this)!=true)return false; target=dialog.FileName; }
        try { ModuleProjectFile.Save(target,graph); projectPath=target; dirty=false; UpdateTitle(); Status.Text="پروژه ماژولی ذخیره شد."; return true; } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { MessageBox.Show(this,ex.Message); return false; }
    }
    private void Save_Click(object sender,RoutedEventArgs e) { if(!busy) SaveGraph(false); }
    private void SaveAs_Click(object sender,RoutedEventArgs e) { if(!busy) SaveGraph(true); }
    private void Window_Closing(object sender,System.ComponentModel.CancelEventArgs e) { if(closing)return; if(busy || !ConfirmDiscard()) { e.Cancel=true; return; } closing=true; }
}
