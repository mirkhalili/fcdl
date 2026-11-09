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
    private Point libraryDragStart;
    private bool panning; private Point panOrigin; private double panX, panY;
    private Point dragStart; private double dragX, dragY, zoom = 1;
    private bool dirty, busy, closing;
    private string? projectPath;
    private readonly WeaveBank bank = new();
    private static readonly (ModuleKind kind, string name, string icon, string color, string subtitle)[] Types = {
        (ModuleKind.Load,"بارگذاری فایل","\uE8B7","#47D4B3","IMAGE SOURCE"),
        (ModuleKind.Resize,"تغییر اندازه","\uE8A3","#61A9F7","RESIZE"),
        (ModuleKind.Colors,"مدیریت رنگ","\uE790","#E2A766","PALETTE"),
        (ModuleKind.Weaves,"بانک بافت پروژه","\uE8A5","#BA91F4","WEAVE LIBRARY"),
        (ModuleKind.CreateWeave,"ساخت بافت","\uE70F","#C89570","WEAVE DESIGNER"),
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
    private static Brush ResourceBrush(string key) => (Brush)Application.Current.FindResource(key);
    private static Brush CategoryBrush(string hex) => ThemeManager.IsLight ? Brush(hex switch { "#47D4B3"=>"#176B5E", "#61A9F7"=>"#255F99", "#E2A766"=>"#8A4C0D", "#BA91F4" or "#BB94EE"=>"#69459A", "#C89570"=>"#824D29", "#7CAAE3"=>"#345D8A", "#E89B73"=>"#914728", "#72C8C5"=>"#286F6D", "#E2AD69"=>"#80500E", "#8DBAF0"=>"#355F94", _=>"#8B442E" }) : Brush(hex);
    public ModuleWindow()
    {
        InitializeComponent(); ThemeManager.Apply(ThemeManager.ReadLight(),false);
        BuildLibrary(); CreateDefaultGraph(); dirty = false; RenderGraph(); Inspect();
        ThemeManager.Changed += OnThemeChanged; Closed += (_,_) => ThemeManager.Changed -= OnThemeChanged;
    }
    private void Theme_Click(object sender, RoutedEventArgs e) { if (!busy) ThemeManager.Apply(!ThemeManager.IsLight,true); }
    private void OnThemeChanged(object? sender, EventArgs e) { BuildLibrary(); RenderGraph(); }
    private void BuildLibrary()
    {
        ModuleLibrary.Children.Clear();
        foreach (var type in Types)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = type.icon, FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = CategoryBrush(type.color), FontSize = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
            var labels = new StackPanel(); labels.Children.Add(new TextBlock { Text = type.name, FontSize = 12, Foreground = ResourceBrush("PrimaryText") }); labels.Children.Add(new TextBlock { Text = type.subtitle, FontSize = 9, Foreground = ResourceBrush("Muted"), FlowDirection = FlowDirection.LeftToRight }); Grid.SetColumn(labels,1); row.Children.Add(labels);
            var kind = type.kind; var group = new DockPanel { FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0,2,0,2) };
            var help = new Button { Content = "?", ToolTip = "راهنمای " + type.name, Width = 28, Padding = new Thickness(0), Margin = new Thickness(3,0,0,0), Background = ResourceBrush("ButtonBackground"), Foreground = ResourceBrush("PrimaryText") }; DockPanel.SetDock(help,Dock.Right); group.Children.Add(help); help.Click += (_,_) => ShowModuleHelp(kind);
            var button = new Button { Content = row, Background = ResourceBrush("GraphNode"), BorderBrush = ResourceBrush("Border"), Padding = new Thickness(6), HorizontalContentAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.LeftToRight, ToolTip = "کلیک یا کشیدن: افزودن " + type.name };
            button.Click += (_,_) => { if (!busy) AddModule(kind, 60 + (graph.Nodes.Count % 6) * 250, 80 + (graph.Nodes.Count / 6) * 160); };
            button.PreviewMouseLeftButtonDown += (_,e) => libraryDragStart = e.GetPosition(button);
            button.PreviewMouseMove += (_,e) => { if (busy || e.LeftButton != MouseButtonState.Pressed) return; var p=e.GetPosition(button); if (Math.Abs(p.X-libraryDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y-libraryDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return; button.ReleaseMouseCapture(); DragDrop.DoDragDrop(button,new DataObject("JacquardModuleKind",kind.ToString()),DragDropEffects.Copy); e.Handled=true; };
            group.Children.Add(button); ModuleLibrary.Children.Add(group);
        }
    }
    private void AddModule(ModuleKind kind, double x, double y)
    {
        try { var node=graph.Add(kind,Math.Clamp(x,0,10000),Math.Clamp(y,0,10000)); selected=node.Id; Changed(); RenderGraph(); Status.Text="ماژول افزوده شد؛ برای تنظیمات دوبار کلیک کنید."; }
        catch(ArgumentException ex) { MessageBox.Show(this,ex.Message); }
    }
    private void GraphCanvas_DragOver(object sender, DragEventArgs e) { e.Effects = !busy && e.Data.GetDataPresent("JacquardModuleKind") ? DragDropEffects.Copy : DragDropEffects.None; e.Handled=true; }
    private void GraphCanvas_Drop(object sender, DragEventArgs e)
    {
        if (!busy && e.Data.GetData("JacquardModuleKind") is string text && Enum.TryParse<ModuleKind>(text,out var kind) && Enum.IsDefined(kind)) { var p=e.GetPosition(NodeLayer); AddModule(kind,p.X-105,p.Y-66); } e.Handled=true;
    }
    private void ShowModuleHelp(ModuleKind kind)
    {
        string text = kind switch {
            ModuleKind.Load => "تصویر یا صفحه اول PDF را انتخاب می‌کند. خروجی: تصویر. نسخه رستری در پروژه ذخیره می‌شود.",
            ModuleKind.Resize => "تصویر ورودی را با اندازه انتخابی تغییر می‌دهد. خروجی آن را به مدیریت رنگ متصل کنید.",
            ModuleKind.Colors => "پالت رنگ تصویر را می‌سازد و برای بافت‌ریزی آماده می‌کند.",
            ModuleKind.CreateWeave => "در طراح کامل بافت رسم کنید، از ابزارهای قطر، سرژه، پاناما و ساتین، بازگشت و نمای سه‌بعدی استفاده کنید و اعمال را بزنید. خروجی به ورودی بافت‌ریزی متصل می‌شود.",
            ModuleKind.Weaves => "چند بافت از بانک انتخاب کنید یا در شبکه بافت بسازید. این مجموعه همراه پروژه است.",
            ModuleKind.ApplyWeaves => "ورودی پالت و مجموعه بافت را متصل و برای هر رنگ یک بافت انتخاب کنید. خروجی نقشه سیاه‌وسفید است.",
            ModuleKind.Selvedge => "کناره و بافت چپ و راست را پیش از پودگذاری به نقشه اضافه می‌کند.",
            ModuleKind.Floats => "فلوت‌های تار یا پود را با حداقل طول مشخص بررسی و در صورت انتخاب اصلاح می‌کند.",
            ModuleKind.Tension => "شاخص نسبی اختلاف تارها را تحلیل می‌کند؛ اندازه‌گیری فیزیکی کشش نیست.",
            ModuleKind.Weft => "توالی و ستون‌های فرمان پود را به پارچه اضافه می‌کند؛ محل و قطبیت را روی دستگاه بررسی کنید.",
            ModuleKind.Simulation => "پیش‌نمایش تقریبی پارچه و نخ را می‌سازد؛ فایل ماشین را تغییر نمی‌دهد.",
            ModuleKind.Machine => "نقشه ورودی را بررسی و به JC5 آزمایشی صادر می‌کند. پیش از تولید روی دستگاه کنترل کنید.", _ => "" };
        MessageBox.Show(this,text + "\n\nبرای اتصال، نقطه خروجی و سپس ورودی سازگار را انتخاب کنید. برای تنظیم ماژول دوبار کلیک کنید.","راهنمای " + Spec(kind).name,MessageBoxButton.OK,MessageBoxImage.Information);
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
    private void Changed() { dirty = true; results = new Dictionary<Guid,ModuleValue>(); foreach (var child in Inspector.Children.OfType<FrameworkElement>().Where(e => Equals(e.Tag, "module-result")).ToArray()) Inspector.Children.Remove(child); UpdateTitle(); }
    private void UpdateTitle() => Title = $"Jacquard Studio · {(projectPath is null ? "پروژه جدید" : System.IO.Path.GetFileName(projectPath))}{(dirty ? " *" : "")}";
    private static string PortName(string port) => port switch { "image" => "تصویر", "palette" => "رنگ‌ها", "weaves" => "بافت‌ها", _ => "پارچه" };
    private void RenderGraph()
    {
        NodeLayer.Children.Clear(); cards.Clear();
        GraphCanvas.Width = Math.Max(2600, graph.Nodes.Select(n => n.X + 300).DefaultIfEmpty(0).Max()); GraphCanvas.Height = Math.Max(1700, graph.Nodes.Select(n => n.Y + 220).DefaultIfEmpty(0).Max());
        NodeLayer.Width = WireLayer.Width = GraphCanvas.Width; NodeLayer.Height = WireLayer.Height = GraphCanvas.Height;
        foreach (var node in graph.Nodes)
        {
            var spec = Spec(node.Kind); var card = new Border { Width = 210, Height = 132, Background = ResourceBrush("GraphNode"), BorderBrush = ResourceBrush(node.Id == selected ? "GraphSelection" : "Border"), BorderThickness = new Thickness(node.Id == selected ? 2 : 1), CornerRadius = new CornerRadius(5), Cursor = Cursors.Hand, ToolTip = "انتخاب و جابه‌جایی ماژول" };
            var inner = new Grid(); inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) }); inner.RowDefinitions.Add(new RowDefinition());
            var header = new Grid { Background = ResourceBrush("GraphNodeHeader") }; header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) }); header.ColumnDefinitions.Add(new ColumnDefinition());
            header.Children.Add(new TextBlock { Text=spec.icon, FontFamily=new FontFamily("Segoe MDL2 Assets"), Foreground=CategoryBrush(spec.color), FontSize=20, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center });
            var text = new StackPanel { VerticalAlignment=VerticalAlignment.Center, FlowDirection=FlowDirection.RightToLeft, Margin=new Thickness(3,0,13,0) }; text.Children.Add(new TextBlock { Text=string.IsNullOrWhiteSpace(node.Label) ? spec.name : node.Label, FontWeight=FontWeights.Bold, FontSize=12, TextTrimming=TextTrimming.CharacterEllipsis }); text.Children.Add(new TextBlock { Text=spec.subtitle, FontSize=9, Foreground=ResourceBrush("Muted"), FlowDirection=FlowDirection.LeftToRight }); Grid.SetColumn(text,1); header.Children.Add(text); inner.Children.Add(header);
            var portGrid = new Grid(); Grid.SetRow(portGrid,1); inner.Children.Add(portGrid);
            var thumb = new Border { Width=62, Height=54, Background=ResourceBrush("GraphPreview"), BorderBrush=ResourceBrush("Border"), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(5), HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center, IsHitTestVisible=false };
            if (results.TryGetValue(node.Id,out var nodeResult) && nodeResult.Image is { } outputImage) { var preview=new Image { Source=outputImage,Stretch=Stretch.Uniform,Margin=new Thickness(3) }; RenderOptions.SetBitmapScalingMode(preview,BitmapScalingMode.NearestNeighbor); thumb.Child=preview; } else thumb.Child=new TextBlock { Text="—",Foreground=ResourceBrush("Muted"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center }; portGrid.Children.Add(thumb); var inputs = ModuleGraph.Inputs(node.Kind).Keys.ToArray();
            for (int i=0; i<inputs.Length; i++)
            {
                string port = inputs[i]; int position = i;
                var label = new TextBlock { Text=PortName(port), FontSize=10, Foreground=ResourceBrush("Muted"), Margin=new Thickness(24, 7 + i*27,0,0), VerticalAlignment=VerticalAlignment.Top, FlowDirection=FlowDirection.RightToLeft, HorizontalAlignment=HorizontalAlignment.Left }; portGrid.Children.Add(label);
                var inputButton = PortButton(SocketColor(ModuleGraph.Inputs(node.Kind)[port]), "اتصال ورودی " + PortName(port)); inputButton.HorizontalAlignment=HorizontalAlignment.Left; inputButton.VerticalAlignment=VerticalAlignment.Top; inputButton.Margin=new Thickness(2,6+i*27,0,0);
                inputButton.Click += (_,e) => { e.Handled=true; if (busy) return; if (connecting is not Guid from) { selected=node.Id; Inspect(); Status.Text="ابتدا نقطه خروجی یک ماژول و سپس این ورودی را انتخاب کنید."; return; } try { graph.Connect(from,node.Id,port); connecting=null; selected=node.Id; Changed(); RenderGraph(); Inspect(); Status.Text="اتصال برقرار شد."; } catch (ArgumentException ex) { Status.Text=ex.Message; } };
                inputButton.ContextMenu = new ContextMenu(); var removeWire = new MenuItem { Header="قطع اتصال ورودی" }; removeWire.Click += (_,_) => { graph.Disconnect(node.Id,port); Changed(); RenderGraph(); Inspect(); }; inputButton.ContextMenu.Items.Add(removeWire); portGrid.Children.Add(inputButton);
            }
            var output = PortButton(SocketColor(ModuleGraph.Output(node.Kind)),"انتخاب خروجی برای اتصال"); output.HorizontalAlignment=HorizontalAlignment.Right; output.VerticalAlignment=VerticalAlignment.Top; output.Margin=new Thickness(0,7,2,0); output.Click += (_,e) => { e.Handled=true; if (busy) return; connecting=node.Id; selected=node.Id; RenderGraph(); Inspect(); Status.Text="خروجی انتخاب شد؛ اکنون روی ورودی سازگار باکس مقصد بزنید."; }; portGrid.Children.Add(output);
            var outLabel = new TextBlock { Text="خروجی", FontSize=10, Foreground=ResourceBrush("Muted"), VerticalAlignment=VerticalAlignment.Top, HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,9,24,0) }; portGrid.Children.Add(outLabel);
            card.Child=inner; Canvas.SetLeft(card,node.X); Canvas.SetTop(card,node.Y); NodeLayer.Children.Add(card); cards[node.Id]=card;
            card.MouseLeftButtonDown += (_,e) => { if (busy || e.OriginalSource is DependencyObject source && FindButton(source)) return; selected=node.Id; if (e.ClickCount == 2) { dragging=null; RenderSelection(); _ = EditModuleAsync(node); e.Handled=true; return; } dragging=node.Id; dragStart=e.GetPosition(NodeLayer); dragX=node.X; dragY=node.Y; card.CaptureMouse(); RenderSelection(); Inspect(); e.Handled=true; };
            card.MouseMove += (_,e) => { if (dragging!=node.Id || e.LeftButton!=MouseButtonState.Pressed) return; var now=e.GetPosition(NodeLayer); node.X=Math.Clamp(dragX+now.X-dragStart.X,0,Math.Max(0,GraphCanvas.Width-210)); node.Y=Math.Clamp(dragY+now.Y-dragStart.Y,0,Math.Max(0,GraphCanvas.Height-132)); Canvas.SetLeft(card,node.X); Canvas.SetTop(card,node.Y); DrawWires(); };
            card.MouseLeftButtonUp += (_,e) => { if (dragging != node.Id) return; dragging=null; card.ReleaseMouseCapture(); if (node.X != dragX || node.Y != dragY) { dirty=true; UpdateTitle(); } e.Handled=true; };
            card.LostMouseCapture += (_,_) => { dragging=null; };
            card.ContextMenu=new ContextMenu(); var delete=new MenuItem { Header="حذف ماژول" }; delete.Click+=(_,_)=>RemoveNode(node.Id); card.ContextMenu.Items.Add(delete);
        }
        DrawWires();
    }
    private static bool FindButton(DependencyObject? item) { while (item is not null) { if (item is Button) return true; item = VisualTreeHelper.GetParent(item); } return false; }
    private static string SocketColor(ModuleData type) => (type,ThemeManager.IsLight) switch
    {
        (ModuleData.Image,true)=>"#8A6414", (ModuleData.Palette,true)=>"#8E6132", (ModuleData.Weaves,true)=>"#7651A5", (ModuleData.Fabric,true)=>"#426D8C", (ModuleData.Preview,true)=>"#467A54",
        (ModuleData.Image,false)=>"#D7B25B", (ModuleData.Palette,false)=>"#D4A477", (ModuleData.Weaves,false)=>"#BC9BE4", (ModuleData.Fabric,false)=>"#83B0CD", _=>"#91C9A0"
    };
    private static Button PortButton(string color,string tooltip) => new() { Width=18,Height=18,Padding=new Thickness(0),Margin=new Thickness(0), Background=Brush(color), BorderBrush=ResourceBrush("PrimaryText"), BorderThickness=new Thickness(1), ToolTip=tooltip };
    private void RenderSelection() { foreach (var pair in cards) { pair.Value.BorderBrush=ResourceBrush(pair.Key==selected ? "GraphSelection" : "Border"); pair.Value.BorderThickness=new Thickness(pair.Key==selected ? 2 : 1); } }
    private void DrawWires()
    {
        WireLayer.Children.Clear(); foreach (var edge in graph.Edges)
        {
            var from=graph.Nodes.Single(n=>n.Id==edge.From); var to=graph.Nodes.Single(n=>n.Id==edge.To); int port=ModuleGraph.Inputs(to.Kind).Keys.ToList().IndexOf(edge.Port);
            var start=new Point(from.X+201,from.Y+68); var end=new Point(to.X+11,to.Y+67+port*27); double bend=Math.Max(60,Math.Abs(end.X-start.X)*.45);
            var figure=new PathFigure { StartPoint=start }; figure.Segments.Add(new BezierSegment(new Point(start.X+bend,start.Y),new Point(end.X-bend,end.Y),end,true));
            var geometry=new PathGeometry(); geometry.Figures.Add(figure); WireLayer.Children.Add(new System.Windows.Shapes.Path { Data=geometry, Stroke=Brush(SocketColor(ModuleGraph.Output(from.Kind))), StrokeThickness=3, Opacity=.82 });
        }
    }
    private void RemoveNode(Guid id) { if (busy) return; graph.Remove(id); if (selected==id) selected=null; if (connecting==id) connecting=null; Changed(); RenderGraph(); Inspect(); }
    private void Delete_Click(object sender,RoutedEventArgs e) { if (selected is Guid id) RemoveNode(id); }
    private void ZoomIn_Click(object sender,RoutedEventArgs e) => SetZoom(zoom*1.25);
    private void ZoomOut_Click(object sender,RoutedEventArgs e) => SetZoom(zoom/1.25);
    private void SetZoom(double value) { zoom=Math.Clamp(value,.5,2); GraphCanvas.LayoutTransform=new ScaleTransform(zoom,zoom); ZoomLabel.Text=$"{zoom*100:0}%"; }
    private void GraphScroll_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (busy || e.ChangedButton != MouseButton.Middle) return;
        panning=true; panOrigin=e.GetPosition(GraphScroll); panX=GraphScroll.HorizontalOffset; panY=GraphScroll.VerticalOffset; GraphScroll.CaptureMouse(); GraphScroll.Cursor=Cursors.ScrollAll; e.Handled=true;
    }
    private void GraphScroll_MouseMove(object sender, MouseEventArgs e)
    {
        if (!panning || e.MiddleButton != MouseButtonState.Pressed) return;
        var point=e.GetPosition(GraphScroll); GraphScroll.ScrollToHorizontalOffset(panX + panOrigin.X-point.X); GraphScroll.ScrollToVerticalOffset(panY + panOrigin.Y-point.Y); e.Handled=true;
    }
    private void GraphScroll_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!panning || e.ChangedButton != MouseButton.Middle) return;
        panning=false; GraphScroll.ReleaseMouseCapture(); GraphScroll.Cursor=null; e.Handled=true;
    }
    private void GraphScroll_LostMouseCapture(object sender, MouseEventArgs e) { if (e.OriginalSource==GraphScroll) { panning=false; GraphScroll.Cursor=null; } }
    private void GraphScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (busy || (Keyboard.Modifiers & ModifierKeys.Control)==0) return;
        var anchor=e.GetPosition(GraphCanvas); var pointer=e.GetPosition(GraphScroll); SetZoom(zoom*(e.Delta>0 ? 1.25 : 0.8)); GraphScroll.UpdateLayout();
        GraphScroll.ScrollToHorizontalOffset(anchor.X*zoom-pointer.X); GraphScroll.ScrollToVerticalOffset(anchor.Y*zoom-pointer.Y); e.Handled=true;
    }
    private ModuleNode? Current => selected is Guid id ? graph.Nodes.FirstOrDefault(n=>n.Id==id) : null;
    private void Inspect()
    {
        Inspector.Children.Clear(); var node=Current; if (node is null) { Inspector.Children.Add(Note("یک ماژول را انتخاب کنید.")); return; }
        var spec=Spec(node.Kind); Inspector.Children.Add(new TextBlock { Text=spec.name, FontSize=19, FontWeight=FontWeights.Bold, Margin=new Thickness(0,0,0,4) }); Inspector.Children.Add(Note(spec.subtitle));
        Inspector.Children.Add(Note("ورودی‌ها: " + (ModuleGraph.Inputs(node.Kind).Count==0 ? "بدون ورودی" : string.Join(" · ",ModuleGraph.Inputs(node.Kind).Keys.Select(p => PortName(p)+(graph.Edges.Any(e=>e.To==node.Id && e.Port==p) ? " ✓" : " ○"))))));
        void Field(string label,string value,Action<string> assign)
        {
            Inspector.Children.Add(new TextBlock { Text=label, FontSize=12, Margin=new Thickness(0,10,0,4) }); var input=new TextBox { Text=value, Padding=new Thickness(7), FlowDirection=FlowDirection.LeftToRight };
            input.LostKeyboardFocus += (_,_) => { if (input.Text!=value) { try { assign(input.Text); Changed(); value=input.Text; } catch (ArgumentException ex) { input.Text=value; MessageBox.Show(this,ex.Message); } } }; Inspector.Children.Add(input);
        }
        void Button(string text,Action action) { var b=new Button { Content=text, Margin=new Thickness(0,8,0,0), Padding=new Thickness(8) }; b.Click+=(_,_)=>action(); Inspector.Children.Add(b); }
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
                for(int i=0;i<node.Weaves.Count;i++) { int index=i; var row=new DockPanel { Margin=new Thickness(0,4,0,0) }; var del=new Button { Content="×", Padding=new Thickness(6,1,6,1), Background=Brush("#80454B"), Foreground=Brush("#FFFFFF") }; DockPanel.SetDock(del,Dock.Left); row.Children.Add(del); del.Click+=(_,_)=> { node.Weaves.RemoveAt(index); node.WeaveNames.RemoveAt(index); foreach(var edge in graph.Edges.Where(e=>e.From==node.Id && e.Port=="weaves")) { var target=graph.Nodes.Single(n=>n.Id==edge.To); foreach(int key in target.WeaveAssignments.Keys.ToArray()) { int old=target.WeaveAssignments[key]; target.WeaveAssignments[key] = old == index ? 0 : old > index ? old-1 : old; } } Changed(); Inspect(); }; var image=new Image { Source=Fabric3DWindow.PatternBitmap(node.Weaves[i]), Width=36, Height=36, Margin=new Thickness(5), Stretch=Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor); DockPanel.SetDock(image,Dock.Right); row.Children.Add(image); row.Children.Add(new TextBlock { Text=$"{i+1}. {node.WeaveNames[i]} · {node.Weaves[i].Width}×{node.Weaves[i].Height}", VerticalAlignment=VerticalAlignment.Center, TextTrimming=TextTrimming.CharacterEllipsis }); Inspector.Children.Add(row); }
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
                Field("عرض کناره چپ",node.Machine.LeftWidth,s=>node.Machine=node.Machine with { LeftWidth=s, LeftPattern=node.Machine.LeftPattern ?? PatternLibrary.Presets[0].Pattern });
                Field("عرض کناره راست",node.Machine.RightWidth,s=>node.Machine=node.Machine with { RightWidth=s, RightPattern=node.Machine.RightPattern ?? PatternLibrary.Presets[0].Pattern }); Button("انتخاب بافت کناره چپ",()=>ChooseSelvedge(node,true)); Button("انتخاب بافت کناره راست",()=>ChooseSelvedge(node,false)); break;
            case ModuleKind.Floats:
                Field("حداقل طول فلوت",node.FloatMinimum.ToString(),s=>node.FloatMinimum=Parse(s,0));
                var dir=new ComboBox { ItemsSource=new[]{"تار / Warp","پود / Weft"},SelectedIndex=node.FloatWarp?0:1, Margin=new Thickness(0,8,0,0) }; dir.SelectionChanged+=(_,_)=>{node.FloatWarp=dir.SelectedIndex==0;Changed();}; Inspector.Children.Add(dir);
                var check=new CheckBox { Content="اصلاح خودکار مرکز فلوت‌ها", IsChecked=node.AutoCorrect, Margin=new Thickness(0,10,0,0) }; check.Click+=(_,_)=>{node.AutoCorrect=check.IsChecked==true;Changed();}; Inspector.Children.Add(check); break;
            case ModuleKind.Tension: Inspector.Children.Add(Note("تحلیل نسبی تار؛ مقدار فیزیکی کشش نیست. خروجی پارچه بدون تغییر ادامه می‌یابد.")); break;
            case ModuleKind.Weft:
                Field("تعداد پود (۰ = بدون فرمان)",node.Machine.WeftCount,s=>{ int count=Parse(s,0); if (count is < 0 or > 256) throw new ArgumentException("تعداد پود باید بین ۰ تا ۲۵۶ باشد."); node.Machine=node.Machine with { AbsoluteLayout=true,BoxEnabled=count>0,WeftCount=s,DesignStart=count.ToString(),WeftStart="0" }; });
                Field("توالی پودها",node.Machine.Sequence,s=>node.Machine=node.Machine with { Sequence=s }); Field("تعداد کل تار (خالی = خودکار)",node.Machine.TotalHooks,s=>node.Machine=node.Machine with { TotalHooks=s });
                Field("ستون شروع پود (از ۱)",PositionNumbers.ToDisplay(node.Machine.WeftStart),s=>node.Machine=node.Machine with { WeftStart=PositionNumbers.ToStored(s) }); Field("ستون شروع طرح (از ۱)",PositionNumbers.ToDisplay(node.Machine.DesignStart),s=>node.Machine=node.Machine with { DesignStart=PositionNumbers.ToStored(s) });
                var bottom=new CheckBox { Content="توالی از پایین شروع شود",IsChecked=node.Machine.StartFromBottom,Margin=new Thickness(0,10,0,0) }; bottom.Click+=(_,_)=>{node.Machine=node.Machine with { StartFromBottom=bottom.IsChecked==true };Changed();}; Inspector.Children.Add(bottom); Inspector.Children.Add(Note("قلاب‌ها و قطبیت باید مستقل روی دستگاه بررسی شوند.")); break;
            case ModuleKind.Simulation:
                Field("رنگ تار #RRGGBB",node.Simulation.WarpColor,s=>node.Simulation=node.Simulation with { WarpColor=s }); Field("رنگ پود #RRGGBB",node.Simulation.WeftColor,s=>node.Simulation=node.Simulation with { WeftColor=s });
                Field("نمره نخ تار",node.Simulation.WarpCount,s=>node.Simulation=node.Simulation with { WarpCount=s }); Field("تراکم تار / سانتی‌متر",node.Simulation.WarpDensity,s=>node.Simulation=node.Simulation with { WarpDensity=s }); Field("تراکم پود / سانتی‌متر",node.Simulation.WeftDensity,s=>node.Simulation=node.Simulation with { WeftDensity=s }); Field("چگالی مؤثر نخ",node.Simulation.MaterialDensity,s=>node.Simulation=node.Simulation with { MaterialDensity=s });
                var warpYarn=new ComboBox { ItemsSource=FabricSimulation.YarnNames,SelectedIndex=node.Simulation.WarpYarn,Margin=new Thickness(0,8,0,0) }; warpYarn.SelectionChanged+=(_,_)=>{node.Simulation=node.Simulation with { WarpYarn=warpYarn.SelectedIndex };Changed();}; Inspector.Children.Add(warpYarn);
                var weftYarn=new ComboBox { ItemsSource=FabricSimulation.YarnNames,SelectedIndex=node.Simulation.WeftYarn,Margin=new Thickness(0,8,0,0) }; weftYarn.SelectionChanged+=(_,_)=>{node.Simulation=node.Simulation with { WeftYarn=weftYarn.SelectedIndex };Changed();}; Inspector.Children.Add(weftYarn);
                var mode=new ComboBox { ItemsSource=new[]{"معمولی","پرز نخ"},SelectedIndex=Math.Clamp(node.Simulation.Mode,0,1),Margin=new Thickness(0,10,0,0) }; mode.SelectionChanged+=(_,_)=>{node.Simulation=node.Simulation with { Mode=mode.SelectedIndex };Changed();}; Inspector.Children.Add(mode); break;
            case ModuleKind.Machine:
                Inspector.Children.Add(Note("خروجی آزمایشی. ابتدا ماژول را اجرا و نقشه را بررسی کنید."));
                var format=new ComboBox { ItemsSource=new[]{"JC5","JC6 (نام شامل 6)","Bonas JC5"}, SelectedIndex=node.MachineFormat==4 ? 1 : node.MachineFormat==5 ? 2 : 0, Margin=new Thickness(0,8,0,0) }; format.SelectionChanged+=(_,_)=>{node.MachineFormat=format.SelectedIndex==1 ? 4 : format.SelectedIndex==2 ? 5 : 1;Changed();}; Inspector.Children.Add(format);
                Button("ذخیره فایل ماشین JC5",()=>ExportMachine(node)); break;
        }
        if (results.TryGetValue(node.Id,out var value))
        {
            var resultPanel = new StackPanel { Tag = "module-result" }; Inspector.Children.Add(resultPanel);
            resultPanel.Children.Add(new TextBlock { Text="پیش‌نمایش خروجی",FontSize=13,FontWeight=FontWeights.Bold,Margin=new Thickness(0,18,0,6) });
            if (value.Image is not null) { var image=new Image { Source=value.Image,MaxHeight=220,Stretch=Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor); resultPanel.Children.Add(new Border { Background=ResourceBrush("GraphPreview"),Padding=new Thickness(8),CornerRadius=new CornerRadius(8),Child=image }); }
            if (value.Palette is not null) resultPanel.Children.Add(Note($"{value.Palette.Width} × {value.Palette.Height} · {value.Palette.Colors.Length} رنگ"));
            if (node.Kind==ModuleKind.Floats) resultPanel.Children.Add(Note($"تعداد فلوت‌های یافته‌شده: {value.FloatCount}"));
            if (node.Kind==ModuleKind.Tension && value.Analysis is { } analysis) resultPanel.Children.Add(Note($"میانگین: {analysis.Mean:0.##} · دامنه انحراف: {analysis.DeviationPercent.Min():0.#}% تا {analysis.DeviationPercent.Max():0.#}%"));
        }
    }
    private static TextBlock Note(string text) { var note=new TextBlock { Text=text,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,4,0,4) }; note.SetResourceReference(TextBlock.ForegroundProperty,"Muted"); return note; }
    private static int Parse(string text,int fallback) => NumberInput.TryInt(text,out int n) ? n : throw new ArgumentException("عدد واردشده معتبر نیست.");
    private async Task ChooseImage(ModuleNode node)
    {
        var dialog=new OpenFileDialog { Filter="Images / PDF|*.bmp;*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.ico;*.pdf;*.ai|All files|*.*" }; if (dialog.ShowDialog(this)!=true) return;
        try { busy=true; Status.Text="در حال بارگذاری تصویر…"; var loaded=await Task.Run(()=>ImageLoader.Load(dialog.FileName,0)); node.ImagePng=await Task.Run(()=>ModuleExecutor.EmbedImage(loaded.Image)); node.Label=System.IO.Path.GetFileName(dialog.FileName); Changed(); RenderGraph(); Inspect(); Status.Text="تصویر در ماژول ثبت شد."; } catch(Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException) { MessageBox.Show(this,ex.Message); } finally { busy=false; }
    }
    private static ListBox WeaveGallery(IEnumerable<BankItem> items, bool multiple)
    {
        var list=new ListBox { ItemsSource=items, SelectionMode=multiple ? SelectionMode.Multiple : SelectionMode.Single, ItemsPanel=(ItemsPanelTemplate)Application.Current.FindResource("WeaveGalleryPanel"), ItemTemplate=(DataTemplate)Application.Current.FindResource("WeaveGalleryItem"), ItemContainerStyle=(Style)Application.Current.FindResource("WeaveGalleryListItem") };
        list.SetResourceReference(Control.BackgroundProperty,"ListBackground"); list.SetResourceReference(Control.ForegroundProperty,"PrimaryText"); ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled); return list;
    }
    private void ChooseWeaves(ModuleNode node)
    {
        try
        {
            var items=bank.Load(out int skipped); var dialog=new Window { Title="انتخاب بافت از بانک",Width=450,Height=580,Owner=Window.GetWindow(Inspector) ?? this,WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft,Background=ResourceBrush("Surface"), Foreground=ResourceBrush("PrimaryText") };
            var root=new DockPanel { Margin=new Thickness(15) }; var hint=new TextBlock { Text="برای انتخاب چند بافت از Ctrl یا Shift استفاده کنید.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,10) }; DockPanel.SetDock(hint,Dock.Top); root.Children.Add(hint); var confirm=new Button { Content="افزودن بافت‌های انتخاب‌شده",Margin=new Thickness(0,10,0,0) }; DockPanel.SetDock(confirm,Dock.Bottom); root.Children.Add(confirm);
            var list=WeaveGallery(items, true); root.Children.Add(list); dialog.SetResourceReference(Window.BackgroundProperty,"Surface"); dialog.SetResourceReference(Window.ForegroundProperty,"PrimaryText"); dialog.Content=root;
            confirm.Click+=(_,_)=>{ var chosen=list.SelectedItems.OfType<BankItem>().ToArray(); if (node.Weaves.Count+chosen.Length>256) { MessageBox.Show(dialog,"حداکثر ۲۵۶ بافت مجاز است."); return; } foreach(var item in chosen) { node.Weaves.Add(new WeavePattern(item.Pattern.Width,item.Pattern.Height,(byte[])item.Pattern.Values.Clone(),WeaveBank.CopyColors(item.Pattern.WeftColors))); node.WeaveNames.Add(item.Name); } if(chosen.Length>0) Changed(); dialog.DialogResult=true; };
            dialog.ShowDialog(); Inspect(); if(skipped>0) Status.Text=$"{skipped} بافت نامعتبر بانک نادیده گرفته شد.";
        } catch (Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException) { MessageBox.Show(this,ex.Message); }
    }
    private void ChooseSelvedge(ModuleNode node, bool left)
    {
        try
        {
            var items=bank.Load(out _); var dialog=new Window { Title=left ? "بافت کناره چپ" : "بافت کناره راست",Width=420,Height=540,Owner=Window.GetWindow(Inspector) ?? this,WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft,Background=ResourceBrush("Surface"), Foreground=ResourceBrush("PrimaryText") };
            var root=new DockPanel { Margin=new Thickness(15) }; var confirm=new Button { Content="انتخاب بافت",Margin=new Thickness(0,10,0,0) }; DockPanel.SetDock(confirm,Dock.Bottom); root.Children.Add(confirm);
            var list=WeaveGallery(items, false); root.Children.Add(list); dialog.SetResourceReference(Window.BackgroundProperty,"Surface"); dialog.SetResourceReference(Window.ForegroundProperty,"PrimaryText"); dialog.Content=root;
            confirm.Click+=(_,_)=>{ if(list.SelectedItem is not BankItem item)return; var p=new WeavePattern(item.Pattern.Width,item.Pattern.Height,(byte[])item.Pattern.Values.Clone(),WeaveBank.CopyColors(item.Pattern.WeftColors)); node.Machine=left ? node.Machine with { LeftPattern=p,LeftName=item.Name } : node.Machine with { RightPattern=p,RightName=item.Name }; Changed(); dialog.DialogResult=true; };
            dialog.ShowDialog(); Inspect();
        } catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException) { MessageBox.Show(this,ex.Message); }
    }
    private void CreateWeave(ModuleNode node)
    {
        if(node.Weaves.Count>=256) { MessageBox.Show(this,"حداکثر ۲۵۶ بافت مجاز است."); return; }
        var dialog=new MainWindow { Owner=Window.GetWindow(Inspector) ?? this }; dialog.ConfigureWeaveModuleEditor(null,null);
        if(dialog.ShowDialog()==true && dialog.ModuleWeaveResult is { } pattern) { node.Weaves.Add(pattern); node.WeaveNames.Add(dialog.ModuleWeaveName); Changed(); Inspect(); }
    }
    private async Task EditWeaveDesignerAsync(ModuleNode node)
    {
        var dialog=new MainWindow { Owner=this }; dialog.ConfigureWeaveModuleEditor(node.Weaves.FirstOrDefault(), node.WeaveNames.FirstOrDefault());
        if(dialog.ShowDialog()!=true || dialog.ModuleWeaveResult is not { } pattern) return;
        node.Weaves.Clear(); node.WeaveNames.Clear(); node.Weaves.Add(pattern); node.WeaveNames.Add(dialog.ModuleWeaveName); Changed(); await ExecuteAsync(node); RenderGraph();
    }
    private async Task<bool> ExecuteAsync(ModuleNode node)
    {
        if(busy) return false;
        try { busy=true; ModuleLibrary.IsEnabled=false; GraphScroll.IsEnabled=false; Inspector.IsEnabled=false; Status.Text="در حال اجرای مسیر متصل…"; var evaluated=await Task.Run(()=>ModuleExecutor.Run(graph,node.Id)); results=results.Concat(evaluated).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Last().Value); Status.Text="اجرای مسیر انتخاب‌شده کامل شد."; RenderGraph(); Inspect(); return true; }
        catch(Exception ex) when(ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or UnauthorizedAccessException) { Status.Text="اجرای ماژول ناموفق بود."; MessageBox.Show(this,ex.Message,"خطای ماژول",MessageBoxButton.OK,MessageBoxImage.Warning); return false; }
        finally { busy=false; ModuleLibrary.IsEnabled=true; GraphScroll.IsEnabled=true; Inspector.IsEnabled=true; }
    }
    private async void Run_Click(object sender,RoutedEventArgs e) { if(Current is { } node) await ExecuteAsync(node); }
    private async Task EditModuleAsync(ModuleNode node)
    {
        if(busy) return;
        if (node.Kind == ModuleKind.CreateWeave) { await EditWeaveDesignerAsync(node); return; }
        if (node.Kind is ModuleKind.Resize or ModuleKind.Colors) { await EditLegacyModuleAsync(node); return; }
        int index=graph.Nodes.FindIndex(n=>n.Id==node.Id); var backup=System.Text.Json.JsonSerializer.Deserialize<ModuleNode>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(node))!;
        var assignments=graph.Nodes.ToDictionary(n=>n.Id,n=>new Dictionary<int,int>(n.WeaveAssignments)); var oldResults=results; bool wasDirty=dirty;
        // Prime connected inputs so weave mappings and current output are visible in the dialog.
        try { if (ModuleGraph.Inputs(node.Kind).Count > 0 && ModuleGraph.Inputs(node.Kind).Keys.All(port=>graph.Edges.Any(e=>e.To==node.Id && e.Port==port))) results=await Task.Run(()=>ModuleExecutor.Run(graph,node.Id)); } catch(Exception ex) when(ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { Status.Text=ex.Message; }
        InspectorHost.Content=null; Inspect(); var dialog=new Window { Title=Spec(node.Kind).name, Width=720,Height=760,MinWidth=500,MinHeight=450,Owner=Window.GetWindow(Inspector) ?? this,WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft,Background=ResourceBrush("Surface"),Foreground=ResourceBrush("PrimaryText") };
        var root=new DockPanel { Margin=new Thickness(24) }; var bottom=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0) }; DockPanel.SetDock(bottom,Dock.Bottom); root.Children.Add(bottom);
        var apply=new Button { Content="اعمال تنظیمات و بستن",Background=Brush("#0E806C"),Foreground=Brush("#FFFFFF"),Margin=new Thickness(4),Padding=new Thickness(14,9,14,9) }; var cancel=new Button { Content="انصراف",Margin=new Thickness(4),Padding=new Thickness(14,9,14,9) }; var run=new Button { Content="به‌روزرسانی پیش‌نمایش",Margin=new Thickness(4),Padding=new Thickness(14,9,14,9) }; bottom.Children.Add(apply); bottom.Children.Add(cancel); bottom.Children.Add(run);
        root.Children.Add(new ScrollViewer { Content=Inspector,VerticalScrollBarVisibility=ScrollBarVisibility.Auto }); dialog.SetResourceReference(Window.BackgroundProperty,"Surface"); dialog.SetResourceReference(Window.ForegroundProperty,"PrimaryText"); dialog.Content=root;
        apply.Click+=async (_,_)=>{ if(await ExecuteAsync(node)) dialog.DialogResult=true; }; run.Click+=async (_,_)=>await ExecuteAsync(node); cancel.Click+=(_,_)=>{ if(!busy)dialog.DialogResult=false; }; dialog.Closing+=(_,e)=>{ if(busy)e.Cancel=true; };
        bool accepted=dialog.ShowDialog()==true;
        ((ScrollViewer)root.Children[^1]).Content=null; InspectorHost.Content=Inspector;
        if (!accepted) { graph.Nodes[index]=backup; foreach(var n in graph.Nodes) if(assignments.TryGetValue(n.Id,out var saved)) n.WeaveAssignments=saved; results=oldResults; dirty=wasDirty; UpdateTitle(); }
        else { dirty=true; UpdateTitle(); }
        RenderGraph(); Inspect();
    }
    private async Task EditLegacyModuleAsync(ModuleNode node)
    {
        var edge=graph.Edges.SingleOrDefault(e=>e.To==node.Id && e.Port=="image"); if(edge is null) { MessageBox.Show(this,"ابتدا ورودی تصویر این ماژول را متصل کنید."); return; }
        BitmapSource source; IReadOnlyDictionary<Guid,ModuleValue> inputs; WorkflowSession prepared; string hash;
        try
        {
            busy=true; ModuleLibrary.IsEnabled=false; GraphScroll.IsEnabled=false; Status.Text="در حال آماده‌سازی ورودی ماژول…";
            inputs=await Task.Run(()=>ModuleExecutor.Run(graph,edge.From)); source=inputs[edge.From].Image ?? throw new InvalidOperationException("ورودی تصویر آماده نیست."); hash=await Task.Run(()=>ModuleExecutor.ImageFingerprint(source));
            if (node.Kind==ModuleKind.Colors && node.ColorEdit is { } prior && prior.InputHash!=hash && MessageBox.Show(this,"ورودی تصویر تغییر کرده است. ویرایش رنگ قبلی با پالت جدید جایگزین شود؟","ورودی تغییر کرده",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return;
            prepared=await Task.Run(()=> {
                if(node.Kind==ModuleKind.Resize) { var s=new WorkflowSession(source); s.ApplySize(node.Width==0 ? source.PixelWidth : node.Width,node.Height==0 ? source.PixelHeight : node.Height); return s; }
                var edit=node.ColorEdit; var palette=edit is not null && edit.InputHash==hash ? edit.Palette : DesignProcessor.Prepare(source,source.PixelWidth,source.PixelHeight,node.ColorLimit);
                return WorkflowSession.Restore(source,source,palette,node.ColorLimit,new WeavePattern?[palette.Colors.Length],lockedColors:edit is not null && edit.InputHash==hash ? edit.Locks : null);
            });
        }
        catch(Exception ex) when(ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { MessageBox.Show(this,ex.Message,"خطای ورودی ماژول",MessageBoxButton.OK,MessageBoxImage.Warning); return; }
        finally { busy=false; ModuleLibrary.IsEnabled=true; GraphScroll.IsEnabled=true; }
        var dialog=new MainWindow { Owner=this }; dialog.ConfigureModuleEditor(node.Kind,prepared);
        if(dialog.ShowDialog()!=true || dialog.ModuleEditorResult is not { } output) return;
        if(node.Kind==ModuleKind.Resize) { node.Width=output.Resized!.PixelWidth; node.Height=output.Resized.PixelHeight; }
        else { node.ColorLimit=output.AppliedColorLimit!.Value; node.ColorEdit=new ModuleColorEdit(hash,output.Palette!, (bool[])output.LockedColors.Clone()); }
        Changed(); results=inputs; await ExecuteAsync(node); RenderGraph();
    }
    private void ExportMachine(ModuleNode node)
    {
        if(!results.TryGetValue(node.Id,out var value) || value.Image is null) { MessageBox.Show(this,"ابتدا ماژول ماشین را اجرا کنید."); return; }
        var dialog=new SaveFileDialog { Filter="JC5 (*.jc5)|*.jc5",FileName=MachineFileNaming.SuggestedName(node.MachineFormat),AddExtension=true }; if(dialog.ShowDialog(this)!=true) return;
        if (!MachineFileNaming.ValidDestination(dialog.FileName,node.MachineFormat)) { MessageBox.Show(this,"نام یا پسوند فایل برای قالب انتخاب‌شده معتبر نیست."); return; }
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
