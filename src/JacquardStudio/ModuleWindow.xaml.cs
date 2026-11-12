using System.IO;
using System.Net.Http;
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
    private ModuleEdge? selectedWire;
    private Point libraryDragStart;
    private bool panning; private Point panOrigin; private double panX, panY;
    private Point dragStart; private double dragX, dragY, zoom = 1;
    private bool dirty, busy, closing;
    private string? projectPath, projectPassword;
    private bool openedWithRecovery;
    private UserPreferences preferences = new();
    private string aiEndpoint="https://api.openai.com/v1/images/edits", aiModel="gpt-image-1";
    private string? aiKey;
    private string? acceptedDrag;
    private readonly WeaveBank bank = new();
    private static readonly (ModuleKind kind, string name, string icon, string color, string subtitle)[] Types = {
        (ModuleKind.ExportData,"خروجی تصویر و جدول","\uE74E","#8DBAF0","IMAGE & TABLE EXPORT"),
        (ModuleKind.BinaryLoad,"نقشه بافت آماده","\uE8B7","#47D4B3","BINARY MAP"),
        (ModuleKind.BinaryConvert,"تبدیل سیاه‌وسفید","\uE790","#E2A766","BINARY CONVERSION"),
        (ModuleKind.SeamCheck,"بررسی درز راپورت","\uE9D9","#72C8C5","SEAM CHECK"),
        (ModuleKind.Compare,"مقایسه دو طرح","\uE8A5","#61A9F7","COMPARISON"),
        (ModuleKind.QualityCheck,"کنترل کیفیت نقشه","\uE73E","#AFA5D1","QUALITY CHECK"),
        (ModuleKind.Load,"طرح نقشه‌بیتی","\uE8B7","#47D4B3","IMAGE SOURCE"),
        (ModuleKind.VectorLoad,"طرح برداری","\uE8B7","#80B8DC","VECTOR SOURCE"),
        (ModuleKind.MachineDefinition,"تعریف ماشین","\uE90F","#AFA5D1","MACHINE PROFILE"),
        (ModuleKind.DesignIdentity,"شناسنامه طرح","\uE8A5","#A3C6A6","DESIGN IDENTITY"),
        (ModuleKind.AiEdit,"هوش مصنوعی","\uE945","#D1A4BC","AI IMAGE EDIT"),
        (ModuleKind.DesignEditor,"طراحی و ویرایش طرح","\uE70F","#C89570","DESIGN EDITOR"),
        (ModuleKind.Crop,"برش طرح","\uE7A8","#E89B73","CROP"),
        (ModuleKind.Repeat,"راپورت‌بندی","\uE8A5","#61A9F7","REPEAT"),
        (ModuleKind.Report,"گزارش","\uE9D2","#72C8C5","REPORT"),
        (ModuleKind.Resize,"تغییر اندازه","\uE8A3","#61A9F7","RESIZE"),
        (ModuleKind.Colors,"مدیریت رنگ","\uE790","#E2A766","PALETTE"),
        (ModuleKind.Weaves,"بانک بافت پروژه","\uE8A5","#BA91F4","WEAVE LIBRARY"),
        (ModuleKind.CreateWeave,"ساخت بافت","\uE70F","#C89570","WEAVE DESIGNER"),
        (ModuleKind.ApplyWeaves,"بافت‌ریزی رنگ‌ها","\uE9D2","#BB94EE","WEAVE MAPPING"),
        (ModuleKind.AutoWeaves,"بافت‌ریزی خودکار","\uE9D2","#BB94EE","AUTO WEAVE"),
        (ModuleKind.Selvedge,"کناره","\uE8A1","#7CAAE3","SELVEDGES"),
        (ModuleKind.Floats,"فلوت‌گیری","\uE7C3","#E89B73","FLOAT CHECK"),
        (ModuleKind.Tension,"تحلیل کشش","\uE9D9","#72C8C5","TENSION"),
        (ModuleKind.Weft,"پودگذاری","\uE8FD","#E2AD69","BOX MOTION"),
        (ModuleKind.Simulation,"شبیه‌سازی پارچه","\uE7F4","#8DBAF0","SIMULATION"),
        (ModuleKind.Machine,"خروجی ماشین","\uE950","#F19F7A","MACHINE OUTPUT"),
        (ModuleKind.PeriodicFloats,"فلوت در راپورت مستقیم","\uE7C3","#E89B73","PERIODIC FLOAT CHECK"),
        (ModuleKind.MatchPalette,"تطبیق پالت مرجع","\uE790","#E2A766","PALETTE MATCH"),
        (ModuleKind.RectangleMask,"ماسک مستطیلی","\uE7A8","#7CAAE3","RECTANGLE MASK"),
        (ModuleKind.ApplyMask,"اعمال ماسک","\uE74E","#72C8C5","APPLY MASK"),
        (ModuleKind.BlankDesign,"بوم طرح خالی","\uE70F","#C89570","BLANK CANVAS")
    };
    private static (ModuleKind kind, string name, string icon, string color, string subtitle) Spec(ModuleKind kind) => Types.Single(t => t.kind == kind);
    private static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    private static Brush ResourceBrush(string key) => (Brush)Application.Current.FindResource(key);
    private static Brush NodeTint(string hex) { var tint=(Color)ColorConverter.ConvertFromString(hex); var background=((SolidColorBrush)ResourceBrush("GraphNode")).Color; double t=ThemeManager.IsLight ? .13 : .12; return new SolidColorBrush(Color.FromRgb((byte)(background.R*(1-t)+tint.R*t),(byte)(background.G*(1-t)+tint.G*t),(byte)(background.B*(1-t)+tint.B*t))); }
    private static Brush CategoryBrush(string hex) => ThemeManager.IsLight ? Brush(hex switch { "#47D4B3"=>"#176B5E", "#61A9F7"=>"#255F99", "#E2A766"=>"#8A4C0D", "#BA91F4" or "#BB94EE"=>"#69459A", "#C89570"=>"#824D29", "#7CAAE3"=>"#345D8A", "#E89B73"=>"#914728", "#72C8C5"=>"#286F6D", "#E2AD69"=>"#80500E", "#8DBAF0"=>"#355F94", _=>"#8B442E" }) : Brush(hex);
    public ModuleWindow()
    {
        InitializeComponent(); preferences=UserPreferences.Load(); ThemeManager.Apply(preferences.Theme,false);
        BuildLibrary(); CreateDefaultGraph(); dirty = false; RenderGraph(); Inspect();
        ThemeManager.Changed += OnThemeChanged; Closed += (_,_) => ThemeManager.Changed -= OnThemeChanged;
    }
    private void Theme_Click(object sender, RoutedEventArgs e) { if (!busy) { preferences.Theme=ThemeManager.IsLight ? "dark" : "light"; ThemeManager.Apply(preferences.Theme,true); try { preferences.Save(); } catch(IOException) { Status.Text="ذخیره تم ممکن نشد."; } } }
    private void Settings_Click(object sender,RoutedEventArgs e) { if(busy)return; var window=new SettingsWindow(preferences,Types.Select(t=>(t.kind,t.name))) {Owner=this}; if(window.ShowDialog()==true) {preferences=window.Result;ThemeManager.Apply(preferences.Theme,true);BuildLibrary();} }
    private void Guide_Click(object sender,RoutedEventArgs e) { if(!busy) new GraphGuideWindow {Owner=this}.ShowDialog(); }
    private void About_Click(object sender,RoutedEventArgs e) => new AboutWindow(false) {Owner=this}.ShowDialog();
    private void LibraryToggle_Click(object sender,RoutedEventArgs e) { bool open=LibraryPanel.Visibility==Visibility.Visible; LibraryPanel.Visibility=open ? Visibility.Collapsed : Visibility.Visible; LibraryColumn.Width=new GridLength(open ? 0 : 270); }
    private void FitAll_Click(object sender,RoutedEventArgs e)
    {
        if(graph.Nodes.Count==0)return; double left=graph.Nodes.Min(n=>n.X), top=graph.Nodes.Min(n=>n.Y), right=graph.Nodes.Max(n=>n.X+210), bottom=graph.Nodes.Max(n=>n.Y+(n.Kind==ModuleKind.AiEdit ? 278 : 132));
        double vw=GraphScroll.ViewportWidth, vh=GraphScroll.ViewportHeight; if(vw<=0 || vh<=0)return;
        SetZoom(Math.Min((vw-50)/Math.Max(1,right-left),(vh-50)/Math.Max(1,bottom-top))); GraphScroll.UpdateLayout(); GraphScroll.ScrollToHorizontalOffset(Math.Max(0,(left+right)*zoom/2-vw/2)); GraphScroll.ScrollToVerticalOffset(Math.Max(0,(top+bottom)*zoom/2-vh/2));
    }
    private void ConfigureAi()
    {
        var dialog=new Window { Title="تنظیم اتصال هوش مصنوعی",Width=620,Height=440,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft };dialog.SetResourceReference(BackgroundProperty,"Surface");
        var panel=new StackPanel {Margin=new Thickness(20)};panel.Children.Add(new TextBlock {Text="نشانی سازگار با OpenAI Images Edits (فقط HTTPS):",TextWrapping=TextWrapping.Wrap});
        var endpoint=new TextBox {Text=aiEndpoint,FlowDirection=FlowDirection.LeftToRight,Margin=new Thickness(0,5,0,10),Padding=new Thickness(6)};panel.Children.Add(endpoint);panel.Children.Add(new TextBlock {Text="نام مدل:"});var model=new TextBox {Text=aiModel,FlowDirection=FlowDirection.LeftToRight,Margin=new Thickness(0,5,0,10),Padding=new Thickness(6)};panel.Children.Add(model);
        panel.Children.Add(new TextBlock {Text="کلید اتصال (فقط در حافظهٔ این اجرا؛ خالی = متغیر محیطی):",TextWrapping=TextWrapping.Wrap});var key=new PasswordBox {Margin=new Thickness(0,5,0,10),Padding=new Thickness(6),MaxLength=4096};panel.Children.Add(key);
        panel.Children.Add(new TextBlock {Text="کلید و تصویر به نشانی انتخاب‌شده ارسال می‌شوند. تنها از سرویس مورد اعتماد استفاده کنید. کلید در فایل پروژه ذخیره نمی‌شود.",TextWrapping=TextWrapping.Wrap});
        var ok=new Button {Content="اعمال تنظیمات و بستن",Margin=new Thickness(0,12,0,0)};panel.Children.Add(ok);ok.Click+=(_,_)=>{if(!Uri.TryCreate(endpoint.Text,UriKind.Absolute,out var uri) || uri.Scheme!=Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || string.IsNullOrWhiteSpace(model.Text) || model.Text.Length>100) {MessageBox.Show(dialog,"نشانی HTTPS یا نام مدل معتبر نیست.");return;} aiEndpoint=uri.ToString();aiModel=model.Text.Trim();aiKey=string.IsNullOrWhiteSpace(key.Password)?null:key.Password;dialog.DialogResult=true;};dialog.Content=panel;dialog.ShowDialog();
    }
    private void OnThemeChanged(object? sender, EventArgs e) { BuildLibrary(); RenderGraph(); }
    private void BuildLibrary()
    {
        ModuleLibrary.Children.Clear();
        foreach (var type in Types)
        {
            if(preferences.HiddenModules.Contains(type.kind.ToString())) continue;
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            var icon = new TextBlock { Text = type.icon, FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = CategoryBrush(type.color), FontSize = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }; Grid.SetColumn(icon,1); row.Children.Add(icon);
            var labels = new StackPanel(); labels.Children.Add(new TextBlock { Text = type.name, FontSize = 12, Foreground = ResourceBrush("PrimaryText") }); labels.Children.Add(new TextBlock { Text = type.subtitle, FontSize = 9, Foreground = ResourceBrush("Muted"), FlowDirection = FlowDirection.LeftToRight }); labels.FlowDirection=FlowDirection.RightToLeft; labels.HorizontalAlignment=HorizontalAlignment.Stretch; foreach(var label in labels.Children.OfType<TextBlock>()) label.TextAlignment=TextAlignment.Right; row.Children.Add(labels);
            var kind = type.kind; bool wasDragged=false; var group = new DockPanel { FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0,2,0,2) };
            var help = new Button { Content = "?", ToolTip = "راهنمای " + type.name, Width = 28, Padding = new Thickness(0), Margin = new Thickness(3,0,0,0), Background = ResourceBrush("ButtonBackground"), Foreground = ResourceBrush("PrimaryText") }; DockPanel.SetDock(help,Dock.Left); group.Children.Add(help); help.Click += (_,_) => ShowModuleHelp(kind);
            if(kind==ModuleKind.AiEdit) { var config=new Button { Content="⚙",ToolTip="تنظیم اتصال هوش مصنوعی",Width=30,Padding=new Thickness(0),Margin=new Thickness(3,0,0,0) }; DockPanel.SetDock(config,Dock.Left); group.Children.Add(config); config.Click+=(_,_)=>ConfigureAi(); }
            else { var spacer=new Border {Width=30,Margin=new Thickness(3,0,0,0)};DockPanel.SetDock(spacer,Dock.Left);group.Children.Add(spacer); }
            var button = new Button { Content = row, Background = ResourceBrush("GraphNode"), BorderBrush = ResourceBrush("Border"), Padding = new Thickness(6), HorizontalContentAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.LeftToRight, ToolTip = "کلیک یا کشیدن: افزودن " + type.name };
            button.Click += (_,_) => { if (wasDragged) return; if (!busy) AddModule(kind, 60 + (graph.Nodes.Count % 6) * 250, 80 + (graph.Nodes.Count / 6) * 160); };
            button.PreviewMouseLeftButtonDown += (_,e) => { wasDragged=false; libraryDragStart = e.GetPosition(button); };
            button.PreviewMouseMove += (_,e) => { if (busy || wasDragged || e.LeftButton != MouseButtonState.Pressed) return; var p=e.GetPosition(button); if (Math.Abs(p.X-libraryDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y-libraryDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return; wasDragged=true; button.ReleaseMouseCapture(); var data=new DataObject("JacquardModuleKind",kind.ToString()); data.SetData("JacquardDragToken",Guid.NewGuid().ToString()); DragDrop.DoDragDrop(button,data,DragDropEffects.Copy); e.Handled=true; };
            group.Children.Add(button); ModuleLibrary.Children.Add(group);
        }
    }
    private void AddModule(ModuleKind kind, double x, double y)
    {
        try { var node=graph.Add(kind,Math.Clamp(x,0,10000),Math.Clamp(y,0,10000)); selected=node.Id; Changed(); RenderGraph(); Status.Text="ماژول افزوده شد؛ برای تنظیمات دوبار کلیک کنید."; }
        catch(ArgumentException ex) { ModuleErrors.Show(Window.GetWindow(Inspector) ?? this,ex); }
    }
    private void GraphCanvas_DragOver(object sender, DragEventArgs e) { e.Effects = !busy && e.Data.GetDataPresent("JacquardModuleKind") ? DragDropEffects.Copy : DragDropEffects.None; e.Handled=true; }
    private void GraphCanvas_Drop(object sender, DragEventArgs e)
    {
        if (!busy && e.Data.GetData("JacquardModuleKind") is string text && Enum.TryParse<ModuleKind>(text,out var kind) && Enum.IsDefined(kind)) { string? token=e.Data.GetData("JacquardDragToken") as string; if(token is not null && token!=acceptedDrag) {acceptedDrag=token; var p=e.GetPosition(NodeLayer); AddModule(kind,p.X-105,p.Y-66);} } e.Handled=true;
    }
    private void ShowModuleHelp(ModuleKind kind)
    {
        string text = kind switch {
            ModuleKind.ExportData => "تصویر یا نقشه را متصل و به PNG، BMP یا TIFF صادر کنید؛ برای جدول CSV پالت را نیز وصل کنید.",
            ModuleKind.BinaryLoad => "تصویر مات سیاه‌وسفید یا JC5 پشتیبانی‌شده را بارگذاری و خروجی نقشه را به فلوت یا پودگذاری وصل کنید.",
            ModuleKind.BinaryConvert => "آستانه و وارونگی را تعیین و تصویر مات را به نقشه سیاه‌وسفید تبدیل کنید.",
            ModuleKind.SeamCheck => "لبه‌های مقابل تصویر را مقایسه می‌کند؛ اختلاف صفر تضمین زیبایی راپورت نیست.",
            ModuleKind.Compare => "دو تصویر یا دو نقشه هم‌اندازه را مقایسه و نقاط متفاوت را قرمز نمایش می‌دهد.",
            ModuleKind.QualityCheck => "دودویی بودن نقشه و در صورت اتصال، عرض ماشین را بررسی می‌کند؛ تأیید صنعتی نیست.",
            ModuleKind.Load => "تصویر نقشه‌بیتی از جمله TIFF و GIF را انتخاب کنید و در فایل چندصفحه‌ای صفحه مورد نظر را برگزینید.",
            ModuleKind.VectorLoad => "PDF یا AI سازگار با PDF و صفحه دلخواه را انتخاب کنید. منبع برای تغییر اندازه برداری در پروژه ذخیره می‌شود.",
            ModuleKind.MachineDefinition => "تعداد زه و شروع طرح و پود را تعیین و خروجی را به ورودی ماشین پودگذاری وصل کنید. برند به تنهایی قالب و قطبیت دستگاه را مشخص نمی‌کند.",
            ModuleKind.DesignIdentity => "فیلدهای عنوان و متن دلخواه را وارد کرده و به گزارش متصل کنید.",
            ModuleKind.AiEdit => "تصویر و پرامپت فقط پس از تأیید به OpenAI ارسال می‌شود و خروجی در پروژه ذخیره می‌شود. کلید از متغیر محیطی خوانده می‌شود.",
            ModuleKind.AutoWeaves => "چند رنگ و چند بافت را انتخاب کنید؛ به ترتیب فهرست، جفت‌های یک‌به‌یک اختصاص می‌یابند و رنگ‌های باقی‌مانده نمایش داده می‌شوند.",
            ModuleKind.BlankDesign => "یک بوم رنگی با عرض و ارتفاع انتخابی بسازید و به ویرایشگر طرح وصل کنید.",
            ModuleKind.DesignEditor => "ویرایش پیکسلی با لایه، قلم، اشکال، انتخاب آزاد و مستطیلی، کپی، جابه‌جایی، کانتور و تصویر مرجع برای Tracing. با تغییر ورودی ویرایش باید دوباره تأیید شود.",
            ModuleKind.Crop => "ابتدا جهت حذف را انتخاب کنید؛ با کلیک روی پیکسل مرزی بخش آن سمت حذف می‌شود و پیکسل انتخاب‌شده باقی می‌ماند. ورودی تصویر است.",
            ModuleKind.Repeat => "طرح با تعداد ستون و ردیف انتخابی تکرار می‌شود. نیم‌عرض ردیف‌ها را و نیم‌طول ستون‌ها را به اندازه نصف طرح جابه‌جا می‌کند.",
            ModuleKind.Report => "خروجی چند ماژول را به ورودی گزارش وصل کنید. پیش‌نمایش، چاپ و ذخیره PDF هر ماژول را نمایش می‌دهد. حداکثر ۱۶ ورودی.",
            ModuleKind.Resize => "تصویر ورودی را با اندازه انتخابی تغییر می‌دهد. خروجی آن را به مدیریت رنگ متصل کنید.",
            ModuleKind.Colors => "پالت رنگ تصویر را می‌سازد و برای بافت‌ریزی آماده می‌کند.",
            ModuleKind.CreateWeave => "در طراح کامل بافت رسم کنید، از ابزارهای قطر، سرژه، پاناما و ساتین، بازگشت و نمای سه‌بعدی استفاده کنید و اعمال را بزنید. خروجی به ورودی بافت‌ریزی متصل می‌شود.",
            ModuleKind.Weaves => "چند بافت از بانک انتخاب کنید یا در شبکه بافت بسازید. این مجموعه همراه پروژه است.",
            ModuleKind.ApplyWeaves => "ورودی پالت و مجموعه بافت را متصل و برای هر رنگ یک بافت انتخاب کنید. خروجی نقشه سیاه‌وسفید است.",
            ModuleKind.Selvedge => "کناره و بافت چپ و راست را پیش از پودگذاری به نقشه اضافه می‌کند.",
            ModuleKind.RectangleMask => "مستطیل با مختصات صفرمبنا تعریف و ماسک سفید/سیاه به خروجی می‌دهد. عرض یا ارتفاع صفر یعنی تا انتهای تصویر.",
            ModuleKind.ApplyMask => "تصویر و ماسک هم‌اندازه را وصل کنید؛ خارج انتخاب با رنگ تعیین‌شده جایگزین می‌شود. منبع تغییر نمی‌کند.",
            ModuleKind.MatchPalette => "پالت طرح و پالت مرجع را وصل کنید. نزدیک‌ترین رنگ با فاصله RGB انتخاب و رنگ‌های هم‌مقصد ادغام می‌شوند؛ تطبیق آزمایشگاهی نیست.",
            ModuleKind.PeriodicFloats => "در تکرار مستقیم، فلوت‌های دو سوی مرز یک فلوت شمرده می‌شوند. خطوط یک‌رنگ نامحدود گزارش می‌شوند. نتیجه نقشه را تغییر نمی‌دهد.",
            ModuleKind.Floats => "فلوت‌های تار یا پود را با حداقل طول مشخص بررسی و در صورت انتخاب اصلاح می‌کند.",
            ModuleKind.Tension => "شاخص نسبی اختلاف تارها را تحلیل می‌کند؛ اندازه‌گیری فیزیکی کشش نیست.",
            ModuleKind.Weft => "توالی و ستون‌های فرمان پود را به پارچه اضافه می‌کند؛ محل و قطبیت را روی دستگاه بررسی کنید.",
            ModuleKind.Simulation => "پیش‌نمایش تقریبی پارچه و نخ را می‌سازد؛ فایل ماشین را تغییر نمی‌دهد.",
            ModuleKind.Machine => "نقشه ورودی را بررسی و به JC5 آزمایشی صادر می‌کند. پیش از تولید روی دستگاه کنترل کنید.", _ => "" };
        MessageBox.Show(this,text + "\n\nبرای اتصال، نقطه خروجی و سپس ورودی سازگار را انتخاب کنید. برای تنظیم ماژول دوبار کلیک کنید.","راهنمای " + Spec(kind).name,MessageBoxButton.OK,MessageBoxImage.Information);
    }
    private void CreateDefaultGraph()
    {
        selectedWire=null; graph = new ModuleGraph(); var load=graph.Add(ModuleKind.Load,80,100); var resize=graph.Add(ModuleKind.Resize,330,100); var colors=graph.Add(ModuleKind.Colors,580,100);
        var weaves=graph.Add(ModuleKind.Weaves,580,330); var apply=graph.Add(ModuleKind.ApplyWeaves,830,100); var floats=graph.Add(ModuleKind.Floats,1080,100); var tension=graph.Add(ModuleKind.Tension,1330,100);
        var weft=graph.Add(ModuleKind.Weft,1580,100); var machine=graph.Add(ModuleKind.Machine,1830,100); var simulation=graph.Add(ModuleKind.Simulation,1830,330);
        weft.Machine = new MachineSetup(AbsoluteLayout:true,BoxEnabled:true,DesignStart:"1",WeftStart:"0",WeftCount:"1");
        foreach (var (a,b,port) in new[] { (load,resize,"image"),(resize,colors,"image"),(colors,apply,"palette"),(weaves,apply,"weaves"),(apply,floats,"fabric"),(floats,tension,"fabric"),(tension,weft,"fabric"),(weft,machine,"fabric"),(weft,simulation,"fabric") }) graph.Connect(a.Id,b.Id,port);
        selected = load.Id; connecting = null; projectPath = null; results = new Dictionary<Guid,ModuleValue>();
    }
    private void Changed(Guid? changed=null) { dirty = true; Invalidate(changed ?? selected); foreach (var child in Inspector.Children.OfType<FrameworkElement>().Where(e => Equals(e.Tag, "module-result")).ToArray()) Inspector.Children.Remove(child); UpdateTitle(); }
    private void Invalidate(Guid? changed)
    {
        if(changed is not Guid id) return; var affected=new HashSet<Guid>(); var todo=new Stack<Guid>(); todo.Push(id);
        while(todo.Count>0) { var current=todo.Pop(); if(!affected.Add(current)) continue; foreach(var edge in graph.Edges.Where(e=>e.From==current)) todo.Push(edge.To); }
        results=results.Where(p=>!affected.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value);
        foreach(var n in graph.Nodes.Where(n=>affected.Contains(n.Id))) n.Configured=false;
    }
    private void MergeResults(IReadOnlyDictionary<Guid,ModuleValue> incoming) => results=results.Concat(incoming).GroupBy(x=>x.Key).ToDictionary(g=>g.Key,g=>g.Last().Value);
    private void UpdateTitle() => Title = $"Jacquard Studio · {(projectPath is null ? "پروژه جدید" : System.IO.Path.GetFileName(projectPath))}{(dirty ? " *" : "")}";
    private static string PortName(string port) => port switch { "image" => "تصویر", "palette" => "رنگ‌ها", "weaves" => "بافت‌ها", "report" => "گزارش", "machine" => "ماشین", "first" => "اول", "second" => "دوم", "reference" => "مرجع", "mask" => "ماسک", _ => "پارچه" };
    private void RenderGraph()
    {
        NodeLayer.Children.Clear(); cards.Clear();
        GraphCanvas.Width = Math.Max(2600, graph.Nodes.Select(n => n.X + 300).DefaultIfEmpty(0).Max()); GraphCanvas.Height = Math.Max(1700, graph.Nodes.Select(n => n.Y + (n.Kind==ModuleKind.AiEdit ? 330 : 220)).DefaultIfEmpty(0).Max());
        NodeLayer.Width = WireLayer.Width = GraphCanvas.Width; NodeLayer.Height = WireLayer.Height = GraphCanvas.Height;
        foreach (var node in graph.Nodes)
        {
            var spec = Spec(node.Kind); var card = new Border { Width = 210, Height = node.Kind==ModuleKind.AiEdit ? 278 : 132, Background = NodeTint(spec.color), BorderBrush = ResourceBrush(node.Id == selected ? "GraphSelection" : "Border"), BorderThickness = new Thickness(node.Id == selected ? 2 : 1), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, ToolTip = "انتخاب و جابه‌جایی ماژول" };
            var inner = new Grid(); inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) }); inner.RowDefinitions.Add(new RowDefinition { Height = node.Kind==ModuleKind.AiEdit ? new GridLength(80) : new GridLength(1,GridUnitType.Star) }); if(node.Kind==ModuleKind.AiEdit) inner.RowDefinitions.Add(new RowDefinition());
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) }); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            header.Children.Add(new TextBlock { Text=spec.icon, FontFamily=new FontFamily("Segoe MDL2 Assets"), Foreground=CategoryBrush(spec.color), FontSize=20, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center });
            var text = new StackPanel { VerticalAlignment=VerticalAlignment.Center, FlowDirection=FlowDirection.RightToLeft, Margin=new Thickness(3,0,13,0) }; text.Children.Add(new TextBlock { Text=string.IsNullOrWhiteSpace(node.Label) ? spec.name : node.Label, FontWeight=FontWeights.Bold, FontSize=12, TextTrimming=TextTrimming.CharacterEllipsis }); text.Children.Add(new TextBlock { Text=spec.subtitle, FontSize=9, Foreground=ResourceBrush("Muted"), FlowDirection=FlowDirection.LeftToRight }); Grid.SetColumn(text,1); header.Children.Add(text);
            var state=new Border { Width=10,Height=10,CornerRadius=new CornerRadius(5),Background=Brush(node.PendingInsertionPort is not null ? "#E3A43C" : node.Configured ? "#48BC7B" : "#DC6260"),BorderBrush=ResourceBrush("GraphNodeHeader"),BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,4,4),ToolTip=node.PendingInsertionPort is not null ? "در انتظار تأیید؛ عبور مستقیم" : node.Configured ? "تنظیم و اجرا شده" : "تنظیم یا اجرا نشده" }; header.Children.Add(state);
            var edit=new Button { Content="⋯",Padding=new Thickness(0),Width=28,Height=28,ToolTip="باز کردن تنظیمات ماژول" }; Grid.SetColumn(edit,2); edit.Click+=(_,e)=>{e.Handled=true; if(!busy) {selected=node.Id; selectedWire=null; RenderSelection(); Inspect(); _=EditModuleAsync(node);} }; header.Children.Add(edit);
            inner.Children.Add(new Border { CornerRadius=new CornerRadius(10,10,0,0), Background=ResourceBrush("GraphNodeHeader"), Child=header });
            var portGrid = new Grid(); Grid.SetRow(portGrid,1); inner.Children.Add(portGrid);
            var thumb = new Border { Width=62, Height=54, Background=ResourceBrush("GraphPreview"), BorderBrush=ResourceBrush("Border"), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(5), HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center, IsHitTestVisible=false };
            if (results.TryGetValue(node.Id,out var nodeResult) && nodeResult.Image is { } outputImage) { var preview=new Image { Source=outputImage,Stretch=Stretch.Uniform,Margin=new Thickness(3) }; RenderOptions.SetBitmapScalingMode(preview,BitmapScalingMode.NearestNeighbor); thumb.Child=preview; } else thumb.Child=new TextBlock { Text="—",Foreground=ResourceBrush("Muted"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center }; portGrid.Children.Add(thumb); var inputs = ModuleGraph.Inputs(node.Kind).Keys.ToArray();
            for (int i=0; i<inputs.Length; i++)
            {
                string port = inputs[i]; int position = i;
                var label = new TextBlock { Text=PortName(port), FontSize=10, Foreground=ResourceBrush("Muted"), Margin=new Thickness(24, 7 + i*27,0,0), VerticalAlignment=VerticalAlignment.Top, FlowDirection=FlowDirection.RightToLeft, HorizontalAlignment=HorizontalAlignment.Left }; portGrid.Children.Add(label);
                var inputButton = PortButton(SocketColor(ModuleGraph.Inputs(node.Kind)[port]), "اتصال ورودی " + PortName(port)); inputButton.HorizontalAlignment=HorizontalAlignment.Left; inputButton.VerticalAlignment=VerticalAlignment.Top; inputButton.Margin=new Thickness(2,6+i*27,0,0);
                inputButton.AllowDrop=true; inputButton.DragOver+=(_,e)=>{e.Effects=!busy && e.Data.GetDataPresent("JacquardModuleOutput") ? DragDropEffects.Link : DragDropEffects.None; e.Handled=true;};
                inputButton.Drop+=(_,e)=>{ if(!busy && e.Data.GetData("JacquardModuleOutput") is string text && Guid.TryParse(text,out var source)) { try { graph.Connect(source,node.Id,port); connecting=null; selected=node.Id; selectedWire=null; Changed(node.Id); RenderGraph(); Inspect(); Status.Text="اتصال برقرار شد."; } catch(ArgumentException ex) {Status.Text=ModuleErrors.Explain(ex);} } e.Handled=true; };
                inputButton.Click += (_,e) => { e.Handled=true; if (busy) return; selectedWire=null; if (connecting is not Guid from) { selected=node.Id; DrawWires(); RenderSelection(); Inspect(); Status.Text="ابتدا نقطه خروجی یک ماژول و سپس این ورودی را انتخاب کنید."; return; } try { graph.Connect(from,node.Id,port); connecting=null; selected=node.Id; selectedWire=null; Changed(); RenderGraph(); Inspect(); Status.Text="اتصال برقرار شد."; } catch (ArgumentException ex) { Status.Text=ModuleErrors.Explain(ex); } };
                inputButton.ContextMenu = new ContextMenu();
                if (port=="report") foreach(var edge in graph.Edges.Where(e=>e.To==node.Id && e.Port==port).ToArray())
                {
                    var sourceNode=graph.Nodes.Single(n=>n.Id==edge.From); var disconnect=new MenuItem { Header="قطع: " + (string.IsNullOrWhiteSpace(sourceNode.Label) ? Spec(sourceNode.Kind).name : sourceNode.Label) };
                    disconnect.Click+=(_,_)=>{ if(busy)return; graph.Disconnect(edge.From,node.Id,port); Changed(node.Id); RenderGraph(); Inspect(); }; inputButton.ContextMenu.Items.Add(disconnect);
                }
                var removeWire = new MenuItem { Header=port=="report" ? "قطع همه ورودی‌های گزارش" : "قطع اتصال ورودی" }; removeWire.Click += (_,_) => { if(busy)return; graph.Disconnect(node.Id,port); Changed(node.Id); RenderGraph(); Inspect(); }; inputButton.ContextMenu.Items.Add(removeWire); portGrid.Children.Add(inputButton);
            }
            var output = PortButton(SocketColor(ModuleGraph.Output(node.Kind)),"انتخاب خروجی یا کشیدن برای اتصال"); output.HorizontalAlignment=HorizontalAlignment.Right; output.VerticalAlignment=VerticalAlignment.Top; output.Margin=new Thickness(0,7,2,0); bool wireDragged=false; Point wireStart=default; output.PreviewMouseLeftButtonDown+=(_,e)=>{wireDragged=false; wireStart=e.GetPosition(output);}; output.PreviewMouseMove+=(_,e)=>{if(busy || wireDragged || e.LeftButton!=MouseButtonState.Pressed)return; var p=e.GetPosition(output); if(Math.Abs(p.X-wireStart.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y-wireStart.Y)<SystemParameters.MinimumVerticalDragDistance)return; wireDragged=true; output.ReleaseMouseCapture(); DragDrop.DoDragDrop(output,new DataObject("JacquardModuleOutput",node.Id.ToString()),DragDropEffects.Link); e.Handled=true;}; output.Click += (_,e) => { e.Handled=true; if (wireDragged) return; if (busy) return; connecting=node.Id; selected=node.Id; selectedWire=null; RenderGraph(); Inspect(); Status.Text="خروجی انتخاب شد؛ اکنون روی ورودی سازگار باکس مقصد بزنید."; }; portGrid.Children.Add(output);
            if(node.Kind==ModuleKind.AiEdit)
            {
                var controls=new StackPanel { Margin=new Thickness(8,0,8,6),FlowDirection=FlowDirection.RightToLeft }; Grid.SetRow(controls,2); inner.Children.Add(controls);
                controls.Children.Add(new TextBlock { Text="پرامپت ویرایش تصویر",FontSize=10,Margin=new Thickness(0,0,0,3) });
                var prompt=new TextBox { Text=node.AiPrompt,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=67,MaxLength=4000,Padding=new Thickness(5) };
                prompt.TextChanged+=(_,_)=>{ if(node.AiPrompt!=prompt.Text) {node.AiPrompt=prompt.Text; Changed(node.Id);} }; controls.Children.Add(prompt);
                var generate=new Button { Content="ارسال به OpenAI",Padding=new Thickness(4),Margin=new Thickness(0,5,0,0),ToolTip="ارسال فقط پس از تأیید" };
                generate.Click+=async (_,e)=>{e.Handled=true; if(busy)return; selected=node.Id; await GenerateAiAsync(node); if(node.AiResultPng is not null) await ExecuteAsync(node);}; controls.Children.Add(generate);
            }
            var outLabel = new TextBlock { Text="خروجی", FontSize=10, Foreground=ResourceBrush("Muted"), VerticalAlignment=VerticalAlignment.Top, HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,9,24,0) }; portGrid.Children.Add(outLabel);
            card.Child=inner; Canvas.SetLeft(card,node.X); Canvas.SetTop(card,node.Y); NodeLayer.Children.Add(card); cards[node.Id]=card;
            card.MouseLeftButtonDown += (_,e) => { if (busy || e.OriginalSource is DependencyObject source && FindButton(source)) return; selected=node.Id; if(selectedWire is not null) {selectedWire=null; DrawWires();} if (e.ClickCount == 2) { dragging=null; RenderSelection(); _ = EditModuleAsync(node); e.Handled=true; return; } dragging=node.Id; dragStart=e.GetPosition(NodeLayer); dragX=node.X; dragY=node.Y; card.CaptureMouse(); RenderSelection(); Inspect(); e.Handled=true; };
            card.MouseMove += (_,e) => { if (dragging!=node.Id || e.LeftButton!=MouseButtonState.Pressed) return; var now=e.GetPosition(NodeLayer); node.X=Math.Clamp(dragX+now.X-dragStart.X,0,Math.Max(0,GraphCanvas.Width-210)); node.Y=Math.Clamp(dragY+now.Y-dragStart.Y,0,Math.Max(0,GraphCanvas.Height-132)); Canvas.SetLeft(card,node.X); Canvas.SetTop(card,node.Y); DrawWires(); };
            card.MouseLeftButtonUp += (_,e) => { if (dragging != node.Id) return; dragging=null; card.ReleaseMouseCapture(); if (node.X != dragX || node.Y != dragY) { dirty=true; UpdateTitle(); } e.Handled=true; };
            card.LostMouseCapture += (_,_) => { dragging=null; };
            card.ContextMenu=new ContextMenu(); var delete=new MenuItem { Header="حذف ماژول" }; delete.Click+=(_,_)=>RemoveNode(node.Id); card.ContextMenu.Items.Add(delete);
        }
        DrawWires();
    }
    private static bool FindButton(DependencyObject? item) { while (item is not null) { if (item is Button || item is TextBox) return true; item = VisualTreeHelper.GetParent(item); } return false; }
    private static string SocketColor(ModuleData type) => (type,ThemeManager.IsLight) switch
    {
        (ModuleData.Mask,true)=>"#6A5078", (ModuleData.Mask,false)=>"#D7A9E6", (ModuleData.Image,true)=>"#8A6414", (ModuleData.Palette,true)=>"#8E6132", (ModuleData.Weaves,true)=>"#7651A5", (ModuleData.Fabric,true)=>"#426D8C", (ModuleData.Preview,true)=>"#467A54",
        (ModuleData.Image,false)=>"#D7B25B", (ModuleData.Palette,false)=>"#D4A477", (ModuleData.Weaves,false)=>"#BC9BE4", (ModuleData.Fabric,false)=>"#83B0CD", (ModuleData.Any,true)=>"#555555", (ModuleData.Report,true)=>"#555555", (ModuleData.Any,false)=>"#DDDDDD", (ModuleData.Report,false)=>"#DDDDDD", _=>"#91C9A0"
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
            var geometry=new PathGeometry(); geometry.Figures.Add(figure); var wire=new System.Windows.Shapes.Path { Data=geometry, Stroke=Brush(edge==selectedWire ? "#F39A4A" : SocketColor(ModuleGraph.Output(from.Kind))), StrokeThickness=edge==selectedWire ? 4 : 2.5, StrokeStartLineCap=PenLineCap.Round, StrokeEndLineCap=PenLineCap.Round, Opacity=.9, IsHitTestVisible=false }; if(edge.Port=="report") wire.StrokeDashArray=new DoubleCollection { 4,3 }; WireLayer.Children.Add(wire);
            var hit=new System.Windows.Shapes.Path { Data=geometry,Stroke=Brushes.Transparent,StrokeThickness=16,Cursor=Cursors.Hand,ToolTip="انتخاب اتصال؛ Delete برای حذف" }; hit.MouseLeftButtonDown+=(_,e)=>{if(busy)return; selectedWire=edge; selected=null; connecting=null; Focus(); DrawWires(); RenderSelection(); Inspect(); Status.Text="اتصال انتخاب شد؛ کلید Delete را برای حذف بزنید."; e.Handled=true;}; WireLayer.Children.Add(hit);
            var add=new Button {Content="+",Width=26,Height=26,Padding=new Thickness(0),FontSize=18,FontWeight=FontWeights.Bold,ToolTip="درج ماژول سازگار در این اتصال",Background=ResourceBrush("Surface"),Foreground=ResourceBrush("PrimaryText"),BorderBrush=ResourceBrush("Border")};
            Canvas.SetLeft(add,(start.X+end.X)/2-13); Canvas.SetTop(add,(start.Y+end.Y)/2-13); add.Click+=(_,e)=>{e.Handled=true;if(!busy)InsertOnWire(edge);}; WireLayer.Children.Add(add);
        }
    }
    private void InsertOnWire(ModuleEdge edge)
    {
        var dialog=new Window {Title="درج ماژول در اتصال",Width=580,Height=500,MinWidth=400,MinHeight=300,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft};
        dialog.SetResourceReference(Window.BackgroundProperty,"Surface");dialog.SetResourceReference(Window.ForegroundProperty,"PrimaryText");
        var root=new DockPanel {Margin=new Thickness(16)};var note=new TextBlock {Text="ماژول سازگار را انتخاب کنید. تا تأیید تنظیمات، داده قبلی بدون تغییر عبور می‌کند. ماژول‌های چندورودی پس از اتصال ورودی‌های دیگر قابل تأییدند.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(note,Dock.Top);root.Children.Add(note);
        var grid=new WrapPanel();root.Children.Add(new ScrollViewer {Content=grid,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});dialog.Content=root;
        foreach(var spec in Types) foreach(var port in graph.InsertionPorts(edge,spec.kind))
        {
            var icon=new TextBlock {Text=spec.icon,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=27,HorizontalAlignment=HorizontalAlignment.Center,Foreground=CategoryBrush(spec.color)};
            var button=new Button {Content=icon,Width=68,Height=60,Margin=new Thickness(5),ToolTip=spec.name+" · ورودی "+PortName(port)};System.Windows.Automation.AutomationProperties.SetName(button,spec.name+" · "+PortName(port));
            button.Click+=(_,_)=>{try {var from=graph.Nodes.Single(n=>n.Id==edge.From);var to=graph.Nodes.Single(n=>n.Id==edge.To);var node=graph.InsertBetween(edge,spec.kind,port,Math.Clamp((from.X+to.X)/2,0,10000),Math.Clamp((from.Y+to.Y)/2+155,0,10000));selected=node.Id;selectedWire=null;Changed(node.Id);RenderGraph();Inspect();Status.Text="ماژول درج شد؛ تا تأیید تنظیمات در حالت عبور مستقیم است.";dialog.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(dialog,ex.Message);}};grid.Children.Add(button);
        }
        if(grid.Children.Count==0)grid.Children.Add(new TextBlock {Text="ماژول سازگاری برای این اتصال وجود ندارد.",TextWrapping=TextWrapping.Wrap});dialog.ShowDialog();
    }
    private void RemoveNode(Guid id) { if (busy) return; Invalidate(id); if(!graph.RemovePendingSplice(id)) graph.Remove(id); if (selected==id) selected=null; if (selectedWire is { } wire && (wire.From==id || wire.To==id)) selectedWire=null; if (connecting==id) connecting=null; Changed(); RenderGraph(); Inspect(); }
    private void Arrange_Click(object sender,RoutedEventArgs e)
    {
        if(busy || graph.Nodes.Count==0) return;
        var dialog=new Window { Title="چیدمان ماژول‌ها", Width=360,Height=240,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft }; dialog.SetResourceReference(Window.BackgroundProperty,"Surface");
        var root=new StackPanel { Margin=new Thickness(20) }; root.Children.Add(new TextBlock { Text=$"تعداد ماژول‌ها: {graph.Nodes.Count}" });
        var row=new TextBox { Text=Math.Ceiling(graph.Nodes.Count/4d).ToString(),Margin=new Thickness(0,5,0,5) }; var col=new TextBox { Text="4",Margin=new Thickness(0,5,0,5) }; root.Children.Add(new TextBlock { Text="ردیف" }); root.Children.Add(row); root.Children.Add(new TextBlock { Text="ستون" }); root.Children.Add(col);
        var apply=new Button { Content="چیدمان",Margin=new Thickness(0,8,0,0) }; root.Children.Add(apply); apply.Click+=(_,_)=>{ try { graph.Arrange(Parse(row.Text,0),Parse(col.Text,0)); dirty=true; UpdateTitle(); RenderGraph(); dialog.DialogResult=true; } catch(ArgumentException ex) { ModuleErrors.Show(dialog,ex); } }; dialog.Content=root; dialog.ShowDialog();
    }
    private void ClearAll_Click(object sender,RoutedEventArgs e)
    {
        if(busy || graph.Nodes.Count==0 || MessageBox.Show(this,"همه ماژول‌ها و اتصال‌ها از پروژه حذف شوند؟","حذف همه",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return;
        graph=new ModuleGraph(); selected=connecting=null; selectedWire=null; Changed(); RenderGraph(); Inspect(); Status.Text="همه ماژول‌ها حذف شدند.";
    }
    private void Preset_Click(object sender,RoutedEventArgs e)
    {
        if(busy || graph.Nodes.Count>0 && MessageBox.Show(this,"زنجیره عمومی جایگزین تمام ماژول‌های فعلی شود؟","زنجیره عمومی",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return;
        var savedPath=projectPath; CreateDefaultGraph(); projectPath=savedPath; Changed(); RenderGraph(); Inspect(); Status.Text="زنجیره عمومی ساخته شد.";
    }
    private void Delete_Click(object sender,RoutedEventArgs e) => DeleteSelection();
    private void DeleteSelection() { if(busy)return; if(selectedWire is { } edge) { graph.Disconnect(edge.From,edge.To,edge.Port); selectedWire=null; Changed(edge.To); RenderGraph(); Inspect(); Status.Text="اتصال حذف شد."; } else if(selected is Guid id) RemoveNode(id); }
    private void Workspace_KeyDown(object sender,KeyEventArgs e) { if(e.Key==Key.F1 && !busy) { Guide_Click(sender,e); e.Handled=true; return; } if(e.Key==Key.Delete && e.OriginalSource is not TextBox && e.OriginalSource is not PasswordBox) { DeleteSelection(); e.Handled=true; } }
    private void ZoomIn_Click(object sender,RoutedEventArgs e) => SetZoom(zoom*1.25);
    private void ZoomOut_Click(object sender,RoutedEventArgs e) => SetZoom(zoom/1.25);
    private void SetZoom(double value) { zoom=Math.Clamp(value,.01,2); GraphCanvas.LayoutTransform=new ScaleTransform(zoom,zoom); ZoomLabel.Text=$"{zoom*100:0}%"; }
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
            input.LostKeyboardFocus += (_,_) => { if (input.Text!=value) { try { assign(input.Text); Changed(); value=input.Text; } catch (ArgumentException ex) { input.Text=value; ModuleErrors.Show(Window.GetWindow(Inspector) ?? this,ex); } } }; Inspector.Children.Add(input);
        }
        void Button(string text,Action action) { var b=new Button { Content=text, Margin=new Thickness(0,8,0,0), Padding=new Thickness(8) }; b.Click+=(_,_)=>action(); Inspector.Children.Add(b); }
        switch (node.Kind)
        {
            case ModuleKind.Load:
            case ModuleKind.VectorLoad:
            case ModuleKind.BinaryLoad:
                Inspector.Children.Add(Note(node.ImagePng is null ? "تصویری انتخاب نشده است." : "تصویر در پروژه ذخیره می‌شود."));
                Button("انتخاب طرح و صفحه", async () => await ChooseImage(node)); break;
            case ModuleKind.BlankDesign:
                Inspector.Children.Add(Note("برای شروع طراحی از صفر، بوم را با ابعاد و رنگ دلخواه بسازید. سقف چهار میلیون پیکسل است."));
                Field("عرض بوم (پیکسل)",node.Width.ToString(),s=>{int n=Parse(s,0);if(n<1 || (long)n*node.Height>DesignProcessor.MaxPixels)throw new ArgumentException("ابعاد بوم معتبر نیست.");node.Width=n;});
                Field("ارتفاع بوم (پیکسل)",node.Height.ToString(),s=>{int n=Parse(s,0);if(n<1 || (long)n*node.Width>DesignProcessor.MaxPixels)throw new ArgumentException("ابعاد بوم معتبر نیست.");node.Height=n;});
                Field("رنگ پس‌زمینه (#RRGGBB)",node.MaskFill,s=>{var c=FabricSimulation.ParseColor(s);if(c.A!=255)throw new ArgumentException("رنگ پس‌زمینه باید مات باشد.");node.MaskFill=s;});break;
            case ModuleKind.ExportData:
                Inspector.Children.Add(Note("تصویر یا نقشه را وصل و اجرا کنید. برای جدول، خروجی مدیریت رنگ را نیز به پالت وصل کنید."));
                Button("ذخیره تصویر",()=>ExportGraphImage(node)); Button("ذخیره جدول پالت CSV",()=>ExportGraphPalette(node)); break;
            case ModuleKind.BinaryConvert:
                Field("آستانه روشنایی (۰ تا ۲۵۵)",node.Threshold.ToString(),s=>{int n=Parse(s,0);if(n is < 0 or > 255)throw new ArgumentException("آستانه باید بین صفر و ۲۵۵ باشد.");node.Threshold=n;}); var invert=new CheckBox {Content="معکوس کردن سیاه و سفید",IsChecked=node.InvertBinary,Margin=new Thickness(0,8,0,0)};invert.Click+=(_,_)=>{node.InvertBinary=invert.IsChecked==true;Changed(node.Id);};Inspector.Children.Add(invert);break;
            case ModuleKind.SeamCheck: Inspector.Children.Add(Note("لبه‌های مقابل مقایسه می‌شوند؛ اختلاف صفر تضمین کیفیت بصری نیست."));break;
            case ModuleKind.Compare: Inspector.Children.Add(Note("دو تصویر یا دو نقشهٔ پارچه با ابعاد یکسان وصل کنید؛ تفاوت‌ها قرمز می‌شوند."));break;
            case ModuleKind.QualityCheck: Inspector.Children.Add(Note("دودویی بودن و عرض بررسی می‌شوند. ورودی ماشین اختیاری است؛ این کنترل تأیید دستگاه واقعی نیست."));break;
            case ModuleKind.MachineDefinition:
                Inspector.Children.Add(Note("شماره زه‌ها در این پنجره از ۱ آغاز می‌شود. نوع سازنده صرفاً مشخصات پروژه است و قالب یا قطبیت خروجی را تأیید نمی‌کند."));
                Field("تعداد کل زه",node.Machine.TotalHooks,s=>node.Machine=node.Machine with { AbsoluteLayout=true, TotalHooks=s });
                Field("زه شروع طرح (از ۱)",PositionNumbers.ToDisplay(node.Machine.DesignStart),s=>node.Machine=node.Machine with { AbsoluteLayout=true, DesignStart=PositionNumbers.ToStored(s) });
                Field("زه شروع پودگذاری (از ۱)",PositionNumbers.ToDisplay(node.Machine.WeftStart),s=>node.Machine=node.Machine with { AbsoluteLayout=true, WeftStart=PositionNumbers.ToStored(s) });
                var maker=new ComboBox { ItemsSource=new[]{"Bonas","Stäubli","Vamatex","Other"}, SelectedItem=node.Manufacturer, Margin=new Thickness(0,10,0,0) };
                var brand=new Image { Height=55,Stretch=Stretch.Uniform,Margin=new Thickness(10) }; var brandFrame=new Border { Child=brand,Background=Brushes.White,Margin=new Thickness(0,8,0,0),CornerRadius=new CornerRadius(4) };
                void ShowBrand(string name) { string? file=name switch { "Bonas"=>"bonas.gif", "Stäubli"=>"staubli.png", _=>null }; brand.Source=file is null ? null : new BitmapImage(new Uri("pack://application:,,,/JacquardStudio;component/Assets/Brands/"+file)); brandFrame.Visibility=file is null ? Visibility.Collapsed : Visibility.Visible; }
                maker.SelectionChanged+=(_,_)=>{if(maker.SelectedItem is string m) { node.Manufacturer=m; ShowBrand(m); Changed(node.Id); }}; Inspector.Children.Add(maker); Inspector.Children.Add(brandFrame); ShowBrand(node.Manufacturer);
                Button("ذخیره تعریف ماشین (.samm)",()=>SaveMachineProfile(node)); Button("بارگذاری تعریف ماشین (.samm)",()=>LoadMachineProfile(node)); break;
            case ModuleKind.DesignIdentity:
                Inspector.Children.Add(Note("برای هر مشخصه عنوان و متن وارد کنید. خروجی شناسنامه را می‌توان به گزارش متصل کرد."));
                for(int i=0;i<node.DesignFields.Count;i++) { int index=i; var f=node.DesignFields[i]; Field("عنوان",f.Title,s=>node.DesignFields[index]=node.DesignFields[index] with { Title=s }); Field("متن",f.Text,s=>node.DesignFields[index]=node.DesignFields[index] with { Text=s }); Button("حذف این فیلد",()=>{node.DesignFields.RemoveAt(index); Changed(); Inspect();}); }
                Button("افزودن فیلد",()=>{if(node.DesignFields.Count>=50) {MessageBox.Show(this,"حداکثر ۵۰ فیلد مجاز است.");return;} node.DesignFields.Add(new DesignField("عنوان جدید", "")); Changed(); Inspect();}); break;
            case ModuleKind.AiEdit:
                Inspector.Children.Add(Note("ارسال تصویر به OpenAI ممکن است هزینه داشته باشد. کلید از متغیر محیطی OPENAI_API_KEY خوانده می‌شود و در پروژه ذخیره نمی‌شود؛ تصویر و نتیجه در پروژه ذخیره می‌شوند."));
                var promptField=new TextBox { Text=node.AiPrompt,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=100,MaxLength=4000,Margin=new Thickness(0,6,0,6) }; promptField.TextChanged+=(_,_)=>{node.AiPrompt=promptField.Text;Changed(node.Id);}; Inspector.Children.Add(promptField); Button("ارسال تصویر و تولید نتیجه",async ()=>await GenerateAiAsync(node)); break;
            case ModuleKind.Repeat:
                Inspector.Children.Add(Note("خروجی مستطیلی و پیوسته است؛ بخش‌های بیرون قاب از سمت مقابل تکرار می‌شوند. ابعاد فرد با نصفِ رو به پایین جابه‌جا می‌شوند."));
                Field("تعداد ستون",node.RepeatColumns.ToString(),s=>{ int n=Parse(s,1); if(n<1) throw new ArgumentException("تعداد ستون باید مثبت باشد."); node.RepeatColumns=n; });
                Field("تعداد ردیف",node.RepeatRows.ToString(),s=>{ int n=Parse(s,1); if(n<1) throw new ArgumentException("تعداد ردیف باید مثبت باشد."); node.RepeatRows=n; });
                var repeat=new ComboBox { ItemsSource=new[]{"مستقیم", "۱/۲ عرضی (ردیف‌های متناوب)", "۱/۲ طولی (ستون‌های متناوب)"}, SelectedIndex=(int)node.RepeatMode, Margin=new Thickness(0,8,0,0) }; repeat.SelectionChanged+=(_,_)=>{if(repeat.SelectedIndex>=0) { node.RepeatMode=(RepeatMode)repeat.SelectedIndex; Changed(); }}; Inspector.Children.Add(repeat); break;
            case ModuleKind.Report:
                Inspector.Children.Add(Note($"{graph.Edges.Count(e=>e.To==node.Id && e.Port=="report")} ماژول به گزارش متصل است. هر خروجی را جداگانه به همین ورودی متصل کنید."));
                Button("نمایش و ذخیره گزارش PDF",async ()=> { if(await ExecuteAsync(node) && results.TryGetValue(node.Id,out var report) && report.ReportItems is { } items) { try { var pages=GraphReport.Render(items); new ReportWindow(pages,"graph",new[]{projectPath}.OfType<string>(),false) { Owner=Window.GetWindow(Inspector) ?? this }.ShowDialog(); } catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or IOException) { ModuleErrors.Show(Window.GetWindow(Inspector) ?? this,ex); } } }); break;
            case ModuleKind.Resize:
                Field("عرض (۰ = عرض ورودی)",node.Width.ToString(),s => node.Width=Parse(s,0)); Field("ارتفاع (۰ = ارتفاع ورودی)",node.Height.ToString(),s => node.Height=Parse(s,0)); break;
            case ModuleKind.Colors:
                Field("حداکثر رنگ ۱ تا ۲۵۶",node.ColorLimit.ToString(),s=>node.ColorLimit=Parse(s,0)); break;
            case ModuleKind.Weaves:
                Inspector.Children.Add(Note($"{node.Weaves.Count} بافت در این ماژول پروژه ذخیره شده است."));
                for(int i=0;i<node.Weaves.Count;i++) { int index=i; var row=new DockPanel { Margin=new Thickness(0,4,0,0) }; var del=new Button { Content="×", Padding=new Thickness(6,1,6,1), Background=Brush("#80454B"), Foreground=Brush("#FFFFFF") }; DockPanel.SetDock(del,Dock.Left); row.Children.Add(del); del.Click+=(_,_)=> { node.Weaves.RemoveAt(index); node.WeaveNames.RemoveAt(index); foreach(var edge in graph.Edges.Where(e=>e.From==node.Id && e.Port=="weaves")) { var target=graph.Nodes.Single(n=>n.Id==edge.To); foreach(int key in target.WeaveAssignments.Keys.ToArray()) { int old=target.WeaveAssignments[key]; target.WeaveAssignments[key] = old == index ? -1 : old > index ? old-1 : old; } int d=target.DefaultWeaveIndex; target.DefaultWeaveIndex=d==index ? -1 : d>index ? d-1 : d; } Changed(); Inspect(); }; var image=new Image { Source=Fabric3DWindow.PatternBitmap(node.Weaves[i]), Width=36, Height=36, Margin=new Thickness(5), Stretch=Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor); DockPanel.SetDock(image,Dock.Right); row.Children.Add(image); row.Children.Add(new TextBlock { Text=$"{i+1}. {node.WeaveNames[i]} · {node.Weaves[i].Width}×{node.Weaves[i].Height}", VerticalAlignment=VerticalAlignment.Center, TextTrimming=TextTrimming.CharacterEllipsis }); Inspector.Children.Add(row); }
                Button("افزودن از بانک بافت",()=>ChooseWeaves(node)); Button("ایجاد بافت جدید",()=>CreateWeave(node)); break;
            case ModuleKind.ApplyWeaves:
                Inspector.Children.Add(Note("هر رنگ به بافت انتخابی متصل می‌شود؛ پیش‌فرض، بافت نخست است. پس از اتصال ورودی‌ها اجرا کنید."));
                if (graph.Edges.FirstOrDefault(e=>e.To==node.Id && e.Port=="palette") is { } pe && graph.Edges.FirstOrDefault(e=>e.To==node.Id && e.Port=="weaves") is { } we && results.TryGetValue(pe.From,out var pv) && results.TryGetValue(we.From,out var wv))
                {
                    var weaveNode=graph.Nodes.Single(n=>n.Id==we.From);
                    foreach (int color in pv.Palette!.Colors)
                    {
                        int c=color; var row=new DockPanel { Margin=new Thickness(0,4,0,0) }; var swatch=new Border { Width=25,Height=25,Background=Brush($"#{c:X6}"),Margin=new Thickness(5) }; DockPanel.SetDock(swatch,Dock.Right); row.Children.Add(swatch);
                        var combo=new ComboBox { ItemsSource=weaveNode.WeaveNames, SelectedIndex=Math.Clamp(node.WeaveAssignments.GetValueOrDefault(c,node.DefaultWeaveIndex),-1,wv.Weaves!.Count-1), MinWidth=120 }; combo.SelectionChanged+=(_,_)=>{ if (combo.SelectedIndex>=0) { node.WeaveAssignments[c]=combo.SelectedIndex; Changed(); } }; row.Children.Add(combo); Inspector.Children.Add(row);
                    }
                } break;
            case ModuleKind.Selvedge:
                Field("عرض کناره چپ",node.Machine.LeftWidth,s=>node.Machine=node.Machine with { LeftWidth=s, LeftPattern=node.Machine.LeftPattern ?? PatternLibrary.Presets[0].Pattern });
                Field("عرض کناره راست",node.Machine.RightWidth,s=>node.Machine=node.Machine with { RightWidth=s, RightPattern=node.Machine.RightPattern ?? PatternLibrary.Presets[0].Pattern }); Button("انتخاب بافت کناره چپ",()=>ChooseSelvedge(node,true)); Button("انتخاب بافت کناره راست",()=>ChooseSelvedge(node,false)); break;
            case ModuleKind.RectangleMask:
                Inspector.Children.Add(Note("مختصات از گوشه بالا-چپ و صفرمبنا هستند. صفر برای عرض یا ارتفاع یعنی تا انتهای تصویر. سفید انتخاب است."));
                Field("X شروع",node.MaskX.ToString(),s=>node.MaskX=Parse(s,0));Field("Y شروع",node.MaskY.ToString(),s=>node.MaskY=Parse(s,0));Field("عرض ناحیه",node.MaskWidth.ToString(),s=>node.MaskWidth=Parse(s,0));Field("ارتفاع ناحیه",node.MaskHeight.ToString(),s=>node.MaskHeight=Parse(s,0));break;
            case ModuleKind.ApplyMask:
                Inspector.Children.Add(Note("تصویر و ماسک باید هم‌اندازه باشند. فقط خروجی تغییر می‌کند. بیرون انتخاب با رنگ مات جایگزین می‌شود."));
                Field("رنگ بیرون انتخاب #RRGGBB",node.MaskFill,s=>{_=FabricSimulation.ParseColor(s);node.MaskFill=s;});var inverse=new CheckBox {Content="انتخاب معکوس (سیاه حفظ شود)",IsChecked=node.MaskInvert,Margin=new Thickness(0,8,0,0)};inverse.Click+=(_,_)=>{node.MaskInvert=inverse.IsChecked==true;Changed(node.Id);};Inspector.Children.Add(inverse);break;
            case ModuleKind.MatchPalette:
                Inspector.Children.Add(Note("پالت طرح و پالت مرجع را وصل کنید. هر رنگ با نزدیک‌ترین رنگ مرجع جایگزین و رنگ‌های هم‌مقصد ادغام می‌شوند. فاصله RGB است و تطبیق آزمایشگاهی نیست. در فاصله برابر، رنگ زودتر در پالت مرجع انتخاب می‌شود."));break;
            case ModuleKind.PeriodicFloats:
                Inspector.Children.Add(Note("تشخیص در راپورت مستقیم؛ بدون اصلاح نقشه. برای بررسی تار و پود از دو ماژول استفاده کنید."));
                Field("حداقل طول فلوت (شامل خود مقدار)",node.FloatMinimum.ToString(),s=>{int n=Parse(s,0);if(n<1)throw new ArgumentException("حداقل طول باید مثبت باشد.");node.FloatMinimum=n;});
                var periodicDir=new ComboBox {ItemsSource=new[]{"تار / عمودی","پود / افقی"},SelectedIndex=node.FloatWarp?0:1,Margin=new Thickness(0,8,0,0)};periodicDir.SelectionChanged+=(_,_)=>{node.FloatWarp=periodicDir.SelectedIndex==0;Changed(node.Id);};Inspector.Children.Add(periodicDir);break;
            case ModuleKind.Floats:
                Field("حداقل طول فلوت",node.FloatMinimum.ToString(),s=>node.FloatMinimum=Parse(s,0));
                var dir=new ComboBox { ItemsSource=new[]{"تار / Warp","پود / Weft"},SelectedIndex=node.FloatWarp?0:1, Margin=new Thickness(0,8,0,0) }; dir.SelectionChanged+=(_,_)=>{node.FloatWarp=dir.SelectedIndex==0;Changed();}; Inspector.Children.Add(dir);
                var check=new CheckBox { Content="اصلاح خودکار مرکز فلوت‌ها", IsChecked=node.AutoCorrect, Margin=new Thickness(0,10,0,0) }; check.Click+=(_,_)=>{node.AutoCorrect=check.IsChecked==true;Changed();}; Inspector.Children.Add(check); break;
            case ModuleKind.Tension: Inspector.Children.Add(Note("تحلیل نسبی تار؛ مقدار فیزیکی کشش نیست. خروجی پارچه بدون تغییر ادامه می‌یابد.")); break;
            case ModuleKind.Weft:
                Field("تعداد پود (۰ = بدون فرمان)",node.Machine.WeftCount,s=>{ int count=Parse(s,0); if (count is < 0 or > 256) throw new ArgumentException("تعداد پود باید بین ۰ تا ۲۵۶ باشد."); node.Machine=node.Machine with { AbsoluteLayout=true,BoxEnabled=count>0,WeftCount=s }; });
                Field("توالی پودها",node.Machine.Sequence,s=>node.Machine=node.Machine with { Sequence=s }); Inspector.Children.Add(Note("برای مشخصات زه‌ها، خروجی ماژول تعریف ماشین را به ورودی ماشین این ماژول متصل کنید."));
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
            if(value.QualityNotes is { } notes) foreach(var note in notes) resultPanel.Children.Add(Note(note));
            if (value.Palette is not null) resultPanel.Children.Add(Note($"{value.Palette.Width} × {value.Palette.Height} · {value.Palette.Colors.Length} رنگ"));
            if (node.Kind==ModuleKind.Floats) resultPanel.Children.Add(Note($"تعداد فلوت‌های یافته‌شده: {value.FloatCount}"));
            if (node.Kind==ModuleKind.Tension && value.Analysis is { } analysis) resultPanel.Children.Add(Note($"میانگین: {analysis.Mean:0.##} · دامنه انحراف: {analysis.DeviationPercent.Min():0.#}% تا {analysis.DeviationPercent.Max():0.#}%"));
        }
    }
    private static TextBlock Note(string text) { var note=new TextBlock { Text=text,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,4,0,4) }; note.SetResourceReference(TextBlock.ForegroundProperty,"Muted"); return note; }
    private static int Parse(string text,int fallback) => NumberInput.TryInt(text,out int n) ? n : throw new ArgumentException("عدد واردشده معتبر نیست.");
    private async Task ChooseImage(ModuleNode node)
    {
        bool vector=node.Kind==ModuleKind.VectorLoad, binary=node.Kind==ModuleKind.BinaryLoad;
        var picker=new OpenFileDialog { Filter=vector ? "PDF / AI سازگار با PDF|*.pdf;*.ai" : binary ? "نقشه سیاه‌وسفید یا JC5|*.bmp;*.tif;*.tiff;*.png;*.jc5" : "طرح نقشه‌بیتی|*.bmp;*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.ico;*.wdp;*.jxr", CheckFileExists=true };
        var owner=Window.GetWindow(Inspector) ?? this; if(picker.ShowDialog(owner)!=true) return;
        string ext=System.IO.Path.GetExtension(picker.FileName).ToLowerInvariant(); if(vector ? ext is not (".pdf" or ".ai") : binary ? ext is not (".bmp" or ".tif" or ".tiff" or ".png" or ".jc5") : ext is not (".bmp" or ".tif" or ".tiff" or ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".ico" or ".wdp" or ".jxr")) { MessageBox.Show(owner,"پسوند فایل با نوع ماژول سازگار نیست."); return; }
        try
        {
            busy=true; Status.Text="در حال بارگذاری طرح…"; var first=await Task.Run(()=>ext==".jc5" ? new ImageResult(Jc5Codec.Load(picker.FileName),1,0,"JC5") : ImageLoader.Load(picker.FileName,0,!binary)); busy=false;
            ImageResult chosen=first; if(ext!=".jc5") { var pages=new SourcePageWindow(picker.FileName,first,!binary) { Owner=owner }; if(pages.ShowDialog()!=true || pages.Result is not { } selectedPage) return; chosen=selectedPage; } if(binary) await Task.Run(()=> {if((long)chosen.Image.PixelWidth*chosen.Image.PixelHeight>DesignProcessor.MaxPixels)throw new InvalidDataException("نقشه باید حداکثر چهار میلیون پیکسل باشد."); _=DesignAnalysis.ReadBinary(chosen.Image);});
            busy=true; var png=await Task.Run(()=>ModuleExecutor.EmbedImage(chosen.Image)); node.ImagePng=png; node.VectorPdfBase64=chosen.Vector is null ? null : Convert.ToBase64String(chosen.Vector.PdfBytes); node.SourcePage=chosen.PageIndex; node.Label=System.IO.Path.GetFileName(picker.FileName); Changed(); RenderGraph(); Inspect(); Status.Text=$"صفحه {chosen.PageIndex+1} از {chosen.PageCount} انتخاب شد." + (chosen.OriginalWidth>0 ? $" عرض از {chosen.OriginalWidth} به {chosen.Image.PixelWidth} پیکسل کاهش یافت." : "");
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException) { ModuleErrors.Show(owner,ex); }
        finally { busy=false; }
    }
    private void SaveMachineProfile(ModuleNode node)
    {
        var owner=Window.GetWindow(Inspector) ?? this; var file=new SaveFileDialog { Filter="Machine profile (*.samm)|*.samm",DefaultExt=".samm",AddExtension=true,FileName="machine.samm" }; if(file.ShowDialog(owner)!=true)return;
        try { MachineProfile.Save(file.FileName,node.Machine,node.Manufacturer); } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException) { ModuleErrors.Show(owner,ex); }
    }
    private void LoadMachineProfile(ModuleNode node)
    {
        var owner=Window.GetWindow(Inspector) ?? this; var file=new OpenFileDialog { Filter="Machine profile (*.samm)|*.samm" }; if(file.ShowDialog(owner)!=true)return;
        try { var data=MachineProfile.Load(file.FileName); node.Machine=data.Setup; node.Manufacturer=data.Manufacturer; Changed(); Inspect(); } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or System.Text.Json.JsonException) { ModuleErrors.Show(owner,ex); }
    }
    private async Task GenerateAiAsync(ModuleNode node)
    {
        var owner=Window.GetWindow(Inspector) ?? this; string? key=aiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"); if(string.IsNullOrWhiteSpace(key)) { MessageBox.Show(owner,"ابتدا متغیر محیطی OPENAI_API_KEY را در ویندوز تنظیم و برنامه را دوباره اجرا کنید."); return; }
        var edge=graph.Edges.SingleOrDefault(e=>e.To==node.Id && e.Port=="image"); if(edge is null) { MessageBox.Show(owner,"ابتدا ورودی تصویر را وصل کنید."); return; }
        if(string.IsNullOrWhiteSpace(node.AiPrompt) || node.AiPrompt.Length>4000) { MessageBox.Show(owner,"پرامپت باید ۱ تا ۴۰۰۰ نویسه باشد."); return; }
        if(MessageBox.Show(owner,$"تصویر و پرامپت همراه کلید به این سرویس ارسال می‌شوند: {aiEndpoint}\nممکن است هزینه داشته باشد. ادامه می‌دهید؟","ارسال داده",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return;
        try
        {
            busy=true; Inspector.IsEnabled=false; GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=false; Status.Text="در حال ارسال به OpenAI…";
            var inputs=await Task.Run(()=>ModuleExecutor.Run(graph,edge.From)); var image=inputs[edge.From].Image ?? throw new InvalidOperationException("تصویر آماده نیست.");
            string hash=await Task.Run(()=>ModuleExecutor.ImageFingerprint(image)); string png=await AiImageEdit.GenerateAsync(image,node.AiPrompt,key,endpoint:new Uri(aiEndpoint),model:aiModel);
            node.AiResultPng=png; node.AiInputHash=hash; node.AiPromptHash=AiImageEdit.PromptHash(node.AiPrompt); Changed(); MergeResults(inputs); Status.Text="نتیجه دریافت شد؛ برای ثبت اعمال را بزنید.";
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { ModuleErrors.Show(owner,ex); }
        finally { busy=false; Inspector.IsEnabled=true; GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=true; }
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
        } catch (Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException) { ModuleErrors.Show(Window.GetWindow(Inspector) ?? this,ex); }
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
        } catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException) { ModuleErrors.Show(Window.GetWindow(Inspector) ?? this,ex); }
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
    private async Task<bool> ExecuteAsync(ModuleNode node, bool applyPending=false, bool previewPending=false)
    {
        if(busy) return false;
        try { busy=true; ModuleLibrary.IsEnabled=false; GraphScroll.IsEnabled=false; Inspector.IsEnabled=false; Status.Text="در حال اجرای مسیر متصل…"; var evaluated=await Task.Run(()=>ModuleExecutor.Run(graph,node.Id,(applyPending || previewPending) ? node.Id : null)); if(applyPending && node.PendingInsertionPort is not null) { node.PendingInsertionPort=null; Invalidate(node.Id); } if(!previewPending) MergeResults(evaluated); foreach(var n in graph.Nodes.Where(n=>evaluated.ContainsKey(n.Id) && n.PendingInsertionPort is null)) n.Configured=true; dirty=true; UpdateTitle(); Status.Text="اجرای مسیر انتخاب‌شده کامل شد."; RenderGraph(); Inspect(); return true; }
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or UnauthorizedAccessException) { Status.Text="اجرای ماژول ناموفق بود."; ModuleErrors.Show(Window.GetWindow(Inspector) ?? this,ex); return false; }
        finally { busy=false; ModuleLibrary.IsEnabled=true; GraphScroll.IsEnabled=true; Inspector.IsEnabled=true; }
    }
    private async void Run_Click(object sender,RoutedEventArgs e) { if(Current is { } node) await ExecuteAsync(node); }
    private async Task EditModuleAsync(ModuleNode node)
    {
        if(busy) return;
        if (node.Kind == ModuleKind.Simulation) { await EditSimulationAsync(node); return; }
        if (node.Kind is ModuleKind.Floats or ModuleKind.Tension) { await EditFabricAsync(node); return; }
        if (node.Kind == ModuleKind.ApplyWeaves) { await EditWeaveMappingAsync(node); return; }
        if (node.Kind == ModuleKind.AutoWeaves) { await EditAutoWeavesAsync(node); return; }
        if (node.Kind == ModuleKind.Crop) { await EditCropAsync(node); return; }
        if (node.Kind == ModuleKind.DesignEditor) { await EditDesignAsync(node); return; }
        if (node.Kind == ModuleKind.CreateWeave) { await EditWeaveDesignerAsync(node); return; }
        if (node.Kind is ModuleKind.Resize or ModuleKind.Colors) { await EditLegacyModuleAsync(node); return; }
        int index=graph.Nodes.FindIndex(n=>n.Id==node.Id); var backup=System.Text.Json.JsonSerializer.Deserialize<ModuleNode>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(node))!;
        var assignments=graph.Nodes.ToDictionary(n=>n.Id,n=>(new Dictionary<int,int>(n.WeaveAssignments), n.DefaultWeaveIndex, n.Configured)); var oldResults=results; bool wasDirty=dirty;
        // Prime connected inputs so weave mappings and current output are visible in the dialog.
        try { busy=true; GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=false; if (ModuleGraph.Inputs(node.Kind).Count > 0 && ModuleGraph.Inputs(node.Kind).Keys.Where(port=>port!="machine" && !(node.Kind==ModuleKind.ExportData && port=="palette")).All(port=>graph.Edges.Any(e=>e.To==node.Id && e.Port==port))) MergeResults(await Task.Run(()=>ModuleExecutor.Run(graph,node.Id))); } catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { Status.Text=ModuleErrors.Explain(ex); } finally { busy=false; GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=true; }
        InspectorHost.Content=null; Inspect(); var dialog=new Window { Title=Spec(node.Kind).name, Width=720,Height=760,MinWidth=500,MinHeight=450,Owner=Window.GetWindow(Inspector) ?? this,WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft,Background=ResourceBrush("Surface"),Foreground=ResourceBrush("PrimaryText") };
        var root=new DockPanel { Margin=new Thickness(24) }; var bottom=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0) }; DockPanel.SetDock(bottom,Dock.Top); root.Children.Add(bottom);
        var apply=new Button { Content="اعمال تنظیمات و بستن",Margin=new Thickness(4),Padding=new Thickness(14,9,14,9) }; var cancel=new Button { Content="انصراف",Margin=new Thickness(4),Padding=new Thickness(14,9,14,9) }; var run=new Button { Content="به‌روزرسانی پیش‌نمایش",Margin=new Thickness(4),Padding=new Thickness(14,9,14,9) }; apply.SetResourceReference(Control.BackgroundProperty,"ActionBackground"); bottom.Children.Add(apply); bottom.Children.Add(cancel); bottom.Children.Add(run);
        root.Children.Add(new ScrollViewer { Content=Inspector,VerticalScrollBarVisibility=ScrollBarVisibility.Auto }); dialog.SetResourceReference(Window.BackgroundProperty,"Surface"); dialog.SetResourceReference(Window.ForegroundProperty,"PrimaryText"); dialog.Content=root;
        apply.Click+=async (_,_)=>{ if(await ExecuteAsync(node,applyPending:true)) dialog.DialogResult=true; }; run.Click+=async (_,_)=>await ExecuteAsync(node,previewPending:true); cancel.Click+=(_,_)=>{ if(!busy)dialog.DialogResult=false; }; dialog.Closing+=(_,e)=>{ if(busy)e.Cancel=true; };
        bool accepted=dialog.ShowDialog()==true;
        ((ScrollViewer)root.Children[^1]).Content=null; InspectorHost.Content=Inspector;
        if (!accepted) { graph.Nodes[index]=backup; foreach(var n in graph.Nodes) if(assignments.TryGetValue(n.Id,out var saved)) { n.WeaveAssignments=saved.Item1; n.DefaultWeaveIndex=saved.Item2; n.Configured=saved.Item3; } results=oldResults; dirty=wasDirty; UpdateTitle(); }
        else { dirty=true; UpdateTitle(); }
        RenderGraph(); Inspect();
    }
    private async Task EditSimulationAsync(ModuleNode node)
    {
        var edge=graph.Edges.SingleOrDefault(e=>e.To==node.Id && e.Port=="fabric");if(edge is null){MessageBox.Show(this,"ابتدا ورودی پارچه را وصل کنید.");return;}
        IReadOnlyDictionary<Guid,ModuleValue> inputs;BitmapSource fabric; string[] rows;
        try {busy=true;GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=false;inputs=await Task.Run(()=>ModuleExecutor.Run(graph,edge.From));var input=inputs[edge.From];fabric=input.Composition is null ? input.Image! : CompositionView.Fabric(input.Composition);rows=input.Composition?.RowWefts ?? Array.Empty<string>();}
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException){ModuleErrors.Show(this,ex,"خطای ورودی شبیه‌سازی");return;}
        finally {busy=false;GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=true;}
        try {var editor=new SimulationEditorWindow(fabric,rows,node.Simulation,projectPath) {Owner=this};if(editor.ShowDialog()!=true || editor.Result is not { } settings)return;node.Simulation=settings;Changed(node.Id);MergeResults(inputs);await ExecuteAsync(node,applyPending:true);}
        catch(Exception ex) when(ex is ArgumentException or InvalidDataException or InvalidOperationException){ModuleErrors.Show(this,ex,"خطای شبیه‌سازی");}
    }
    private async Task EditFabricAsync(ModuleNode node)
    {
        var edge=graph.Edges.SingleOrDefault(e=>e.To==node.Id && e.Port=="fabric"); if(edge is null) {MessageBox.Show(this,"ابتدا ورودی پارچه را متصل کنید.");return;}
        IReadOnlyDictionary<Guid,ModuleValue> inputs; BitmapSource fabric; WorkflowSession prepared; string hash; int left,right;
        try
        {
            busy=true; GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=false; inputs=await Task.Run(()=>ModuleExecutor.Run(graph,edge.From)); var input=inputs[edge.From];
            fabric=input.Composition is null ? input.Image! : CompositionView.Fabric(input.Composition); left=input.Composition?.LeftWidth ?? 0; right=input.Composition?.RightWidth ?? 0;
            hash=await Task.Run(()=>ModuleExecutor.ImageFingerprint(fabric));
            if(node.Kind==ModuleKind.Floats && node.FloatImagePng is not null && node.FloatInputHash!=hash && MessageBox.Show(this,"ورودی تغییر کرده است؛ اصلاح قبلی فلوت بازنشانی شود؟","فلوت",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return;
            if(node.Kind==ModuleKind.Floats && node.FloatImagePng is not null && node.FloatInputHash==hash) { var edited=await Task.Run(()=>ModuleExecutor.ApplyFloatEdit(input,node.FloatImagePng)); fabric=edited.Composition is null ? edited.Image! : CompositionView.Fabric(edited.Composition); }
            var source=fabric; prepared=await Task.Run(()=> { _=DesignAnalysis.ReadBinary(source); var p=DesignProcessor.Prepare(source,source.PixelWidth,source.PixelHeight,2); var patterns=p.Colors.Select(c=>(WeavePattern?)new WeavePattern(1,1,new[]{c==0 ? (byte)0 : (byte)255})).ToArray(); return WorkflowSession.Restore(source,source,p,2,patterns); });
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException) {ModuleErrors.Show(Window.GetWindow(Inspector) ?? this,ex);return;}
        finally {busy=false; GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=true;}
        var dialog=new MainWindow { Owner=this }; dialog.ConfigureBinaryModuleEditor(node.Kind,prepared,node.FloatMinimum,node.FloatWarp,left,right); if(dialog.ShowDialog()!=true || dialog.ModuleEditorResult is not { } output)return;
        if(node.Kind==ModuleKind.Floats) { node.FloatImagePng=await Task.Run(()=>ModuleExecutor.EmbedImage(output.PatternPreview!)); node.FloatInputHash=hash; node.FloatMinimum=dialog.EditedFloatMinimum; node.FloatWarp=dialog.EditedFloatWarp; node.AutoCorrect=false; }
        Changed(); MergeResults(inputs); await ExecuteAsync(node,applyPending:true); RenderGraph();
    }
    private static bool SameWeave(WeavePattern a,WeavePattern b) => a.Width==b.Width && a.Height==b.Height && a.Values.SequenceEqual(b.Values) && ((a.WeftColors is null && b.WeftColors is null) || (a.WeftColors is not null && b.WeftColors is not null && a.WeftColors.SequenceEqual(b.WeftColors)));
    private async Task EditWeaveMappingAsync(ModuleNode node)
    {
        var paletteEdge=graph.Edges.SingleOrDefault(e=>e.To==node.Id && e.Port=="palette"); var weaveEdge=graph.Edges.SingleOrDefault(e=>e.To==node.Id && e.Port=="weaves");
        if(paletteEdge is null || weaveEdge is null) {MessageBox.Show(this,"ابتدا ورودی رنگ و بافت را متصل کنید.");return;}
        IReadOnlyDictionary<Guid,ModuleValue> colors, weaves; WorkflowSession prepared; BankItem[] choices;
        try
        {
            busy=true; GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=false;
            colors=await Task.Run(()=>ModuleExecutor.Run(graph,paletteEdge.From)); weaves=await Task.Run(()=>ModuleExecutor.Run(graph,weaveEdge.From));
            var palette=colors[paletteEdge.From].Palette ?? throw new InvalidOperationException("پالت آماده نیست."); var source=colors[paletteEdge.From].Image!; var available=weaves[weaveEdge.From].Weaves ?? throw new InvalidOperationException("بافت‌ها آماده نیستند."); var weaveNode=graph.Nodes.Single(n=>n.Id==weaveEdge.From);
            choices=available.Select((p,i)=>new BankItem { Name=weaveNode.WeaveNames[i],Pattern=p }).ToArray();
            var patterns=palette.Colors.Select(c=>{if(node.CustomWeaves.TryGetValue(c,out var custom))return custom; int k=node.WeaveAssignments.GetValueOrDefault(c,node.DefaultWeaveIndex); return k>=0 && k<available.Count ? available[k] : null;}).ToArray();
            prepared=await Task.Run(()=>WorkflowSession.Restore(source,source,palette,palette.Colors.Length,patterns));
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { ModuleErrors.Show(Window.GetWindow(Inspector) ?? this,ex);return; }
        finally { busy=false; GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=true; }
        var dialog=new MainWindow { Owner=this }; dialog.ConfigureModuleEditor(ModuleKind.ApplyWeaves,prepared,choices); if(dialog.ShowDialog()!=true || dialog.ModuleEditorResult is not { } result)return;
        var assignments=new Dictionary<int,int>(); var custom=new Dictionary<int,WeavePattern>();
        for(int i=0;i<result.Palette!.Colors.Length;i++) { var pattern=result.Patterns[i]!; int index=Array.FindIndex(choices,item=>SameWeave(item.Pattern,pattern)); int color=result.Palette.Colors[i]; if(index>=0) assignments[color]=index; else custom[color]=pattern; }
        node.WeaveAssignments=assignments; node.DefaultWeaveIndex=-1; node.CustomWeaves=custom; Changed(); MergeResults(colors); MergeResults(weaves); await ExecuteAsync(node,applyPending:true); RenderGraph();
    }
    private async Task EditAutoWeavesAsync(ModuleNode node)
    {
        var pe=graph.Edges.SingleOrDefault(e=>e.To==node.Id && e.Port=="palette"); var we=graph.Edges.SingleOrDefault(e=>e.To==node.Id && e.Port=="weaves");
        if(pe is null || we is null) {MessageBox.Show(this,"ابتدا پالت رنگ و بانک بافت را متصل کنید.");return;}
        IReadOnlyDictionary<Guid,ModuleValue> colors, patterns;
        try { busy=true; colors=await Task.Run(()=>ModuleExecutor.Run(graph,pe.From)); patterns=await Task.Run(()=>ModuleExecutor.Run(graph,we.From)); }
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException) {ModuleErrors.Show(Window.GetWindow(Inspector) ?? this,ex);return;}
        finally {busy=false;}
        var palette=colors[pe.From].Palette!; var available=patterns[we.From].Weaves!; var source=graph.Nodes.Single(n=>n.Id==we.From);
        var draft=new Dictionary<int,int>(node.WeaveAssignments); var custom=new Dictionary<int,WeavePattern>(node.CustomWeaves);
        var dialog=new Window { Title="بافت‌ریزی خودکار", Width=900,Height=650,MinWidth=600,MinHeight=450,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft }; dialog.SetResourceReference(Window.BackgroundProperty,"Surface");
        var root=new DockPanel { Margin=new Thickness(16) }; var actions=new WrapPanel(); DockPanel.SetDock(actions,Dock.Top); root.Children.Add(actions);
        var apply=new Button { Content="اعمال تنظیمات و بستن",Margin=new Thickness(4) }; apply.SetResourceReference(Control.BackgroundProperty,"ActionBackground"); actions.Children.Add(apply);
        var cancel=new Button { Content="انصراف",Margin=new Thickness(4),IsCancel=true }; actions.Children.Add(cancel);
        var assign=new Button { Content="اختصاص جفت‌های انتخاب‌شده",Margin=new Thickness(4) }; actions.Children.Add(assign);
        var note=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,5,4,10) }; DockPanel.SetDock(note,Dock.Bottom); root.Children.Add(note);
        var columns=new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(columns);
        var colorList=new ListBox { SelectionMode=SelectionMode.Extended,Margin=new Thickness(5) }; var weaveList=new ListBox { SelectionMode=SelectionMode.Extended,Margin=new Thickness(5) }; Grid.SetColumn(weaveList,1); columns.Children.Add(colorList); columns.Children.Add(weaveList);
        for(int i=0;i<palette.Colors.Length;i++) { int c=palette.Colors[i]; colorList.Items.Add(new ListBoxItem { Tag=i,Content=$"رنگ {i+1} · #{c:X6}" }); }
        for(int i=0;i<available.Count;i++) weaveList.Items.Add(new ListBoxItem { Tag=i,Content=$"{i+1} · {source.WeaveNames[i]}" });
        void Refresh() { var remaining=Enumerable.Range(0,palette.Colors.Length).Where(i=>!custom.ContainsKey(palette.Colors[i]) && (!draft.TryGetValue(palette.Colors[i],out var j) || j<0 || j>=available.Count)).ToArray(); note.Text="رنگ‌های بدون بافت: " + (remaining.Length==0 ? "هیچ‌کدام" : string.Join("، ",remaining.Select(i=>$"{i+1} (#{palette.Colors[i]:X6})"))); }
        assign.Click+=(_,_)=>{ var cs=colorList.SelectedItems.Cast<ListBoxItem>().Select(x=>(int)x.Tag).Order().ToArray(); var ws=weaveList.SelectedItems.Cast<ListBoxItem>().Select(x=>(int)x.Tag).Order().ToArray(); if(cs.Length==0 || ws.Length==0) { MessageBox.Show(dialog,"رنگ و بافت را انتخاب کنید.");return; } draft=AutoWeaveAssignments.Pair(palette.Colors,available.Count,cs,ws,draft).ToDictionary(x=>x.Key,x=>x.Value); for(int i=0;i<Math.Min(cs.Length,ws.Length);i++) custom.Remove(palette.Colors[cs[i]]); Refresh(); };
        apply.Click+=(_,_)=>dialog.DialogResult=true; dialog.Content=root; Refresh();
        if(dialog.ShowDialog()!=true)return;
        node.WeaveAssignments=draft; node.CustomWeaves=custom; node.DefaultWeaveIndex=-1; Changed(node.Id); MergeResults(colors); MergeResults(patterns);
        if(palette.Colors.All(c=>custom.ContainsKey(c) || draft.TryGetValue(c,out int index) && index>=0 && index<available.Count)) await ExecuteAsync(node,applyPending:true); else { Status.Text="رنگ‌های بدون بافت را در ماژول مشخص کنید؛ خروجی تا تکمیل آماده نیست."; RenderGraph(); }
    }
    private async Task EditDesignAsync(ModuleNode node)
    {
        var edge=graph.Edges.SingleOrDefault(e=>e.To==node.Id && e.Port=="image"); bool standalone=edge is null;
        var settings=new DesignBlankSettings(node.DesignWidth,node.DesignHeight,node.DesignWarpDensity,node.DesignWeftDensity);
        if(standalone) { var setup=new DesignBlankWindow(settings){Owner=this};if(setup.ShowDialog()!=true || setup.Result is null)return;settings=setup.Result; }
        IReadOnlyDictionary<Guid,ModuleValue> inputs=new Dictionary<Guid,ModuleValue>(); BitmapSource source; string hash;
        try { busy=true; GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=false; if(standalone)source=await Task.Run(()=>DesignCanvas.Blank(settings.Width,settings.Height,Colors.White));else {inputs=await Task.Run(()=>ModuleExecutor.Run(graph,edge!.From));source=inputs[edge!.From].Image ?? throw new InvalidOperationException("تصویر ورودی آماده نیست.");} hash=await Task.Run(()=>ModuleExecutor.ImageFingerprint(source)); }
        catch(Exception ex) when(ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException) {ModuleErrors.Show(this,ex);return;}
        finally {busy=false;GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=true;}
        bool compatible=node.DesignEditInputHash==hash && node.DesignStandalone==standalone;
        if((node.DesignEditPng is not null || node.DesignEditDocument is not null) && !compatible && MessageBox.Show(this,"ورودی یا ابعاد تغییر کرده است. ویرایش قبلی کنار گذاشته شود؟","طراحی",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return;
        try
        {
            BitmapSource initial=source;
            if(node.DesignEditDocument is null && node.DesignEditPng is not null && compatible)
            {
                byte[] bytes=Convert.FromBase64String(node.DesignEditPng); if(bytes.Length>ImageLoader.MaxFileBytes) throw new InvalidDataException("تصویر بیش از حد بزرگ است.");
                using var stream=new MemoryStream(bytes); var frame=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
                if(frame.PixelWidth!=source.PixelWidth || frame.PixelHeight!=source.PixelHeight) throw new InvalidDataException("ابعاد تصویر طراحی معتبر نیست."); frame.Freeze(); initial=frame;
            }
            var editor=new DesignEditorWindow(initial,compatible ? node.DesignEditDocument : null,standalone ? settings : null) {Owner=this}; if(editor.ShowDialog()!=true || editor.Result is null || editor.Document is null)return;
            string encoded=await Task.Run(()=>ModuleExecutor.EmbedImage(editor.Result)); node.DesignEditPng=encoded;node.DesignEditDocument=editor.Document;node.DesignEditInputHash=hash;node.DesignStandalone=standalone;
            if(standalone){node.DesignWidth=settings.Width;node.DesignHeight=settings.Height;node.DesignWarpDensity=settings.WarpDensity;node.DesignWeftDensity=settings.WeftDensity;}
            Changed(node.Id);MergeResults(inputs);await ExecuteAsync(node,applyPending:true);RenderGraph();
        }
        catch(Exception ex) when(ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException) {ModuleErrors.Show(this,ex);}
    }
    private async Task EditCropAsync(ModuleNode node)
    {
        IReadOnlyDictionary<Guid,ModuleValue>? input=null;
        var dialog=new GraphCropWindow(async ()=>{
            var edge=graph.Edges.SingleOrDefault(e=>e.To==node.Id && e.Port=="image") ?? throw new InvalidOperationException("ابتدا خروجی تصویر را به ورودی این ماژول متصل کنید.");
            IReadOnlyDictionary<Guid,ModuleValue> evaluated; BitmapSource image; string hash;
            try { busy=true; GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=false; evaluated=await Task.Run(()=>ModuleExecutor.Run(graph,edge.From)); image=evaluated[edge.From].Image ?? throw new InvalidOperationException("تصویر ورودی آماده نیست."); hash=await Task.Run(()=>ModuleExecutor.ImageFingerprint(image)); }
            finally { busy=false; GraphScroll.IsEnabled=ModuleLibrary.IsEnabled=true; }
            if(node.Crop is { } prior && prior.InputHash!=hash && MessageBox.Show(this,"تصویر ورودی عوض شده است؛ برش قبلی بازنشانی شود؟","برش",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) throw new InvalidOperationException("برش قبلی حفظ شد؛ ورودی قبلی را بازگردانید.");
            input=evaluated; return (image,node.Crop?.InputHash==hash ? node.Crop : null,hash);
        }) { Owner=this };
        if(dialog.ShowDialog()!=true || dialog.Result is not { } crop || input is null) return;
        node.Crop=crop; Changed(node.Id); MergeResults(input); await ExecuteAsync(node,applyPending:true); RenderGraph();
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
                if(node.Kind==ModuleKind.Resize) { var s=new WorkflowSession(source,inputs[edge.From].Vector); s.ApplySize(node.Width==0 ? source.PixelWidth : node.Width,node.Height==0 ? source.PixelHeight : node.Height); return s; }
                var edit=node.ColorEdit; var raw=DesignProcessor.Prepare(source,source.PixelWidth,source.PixelHeight,node.ColorDistance>0 ? 256 : node.ColorLimit); var palette=edit is not null && edit.InputHash==hash ? edit.Palette : PaletteSimilarity.Reduce(raw,new bool[raw.Colors.Length],node.ColorLimit,node.ColorDistance).Palette;
                return WorkflowSession.Restore(source,source,palette,node.ColorLimit,new WeavePattern?[palette.Colors.Length],lockedColors:edit is not null && edit.InputHash==hash ? edit.Locks : null,colorDistance:node.ColorDistance);
            });
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { ModuleErrors.Show(Window.GetWindow(Inspector) ?? this,ex,"خطای ورودی ماژول"); return; }
        finally { busy=false; ModuleLibrary.IsEnabled=true; GraphScroll.IsEnabled=true; }
        var dialog=new MainWindow { Owner=this }; dialog.ConfigureModuleEditor(node.Kind,prepared);
        if(dialog.ShowDialog()!=true || dialog.ModuleEditorResult is not { } output) return;
        if(node.Kind==ModuleKind.Resize) { node.Width=output.Resized!.PixelWidth; node.Height=output.Resized.PixelHeight; }
        else { node.ColorLimit=output.AppliedColorLimit!.Value; node.ColorDistance=output.AppliedColorDistance; node.ColorEdit=new ModuleColorEdit(hash,output.Palette!, (bool[])output.LockedColors.Clone()); }
        Changed(); MergeResults(inputs); await ExecuteAsync(node,applyPending:true); RenderGraph();
    }
    private void ExportGraphImage(ModuleNode node)
    {
        var owner=Window.GetWindow(Inspector) ?? this; if(!results.TryGetValue(node.Id,out var result) || result.Image is null) {MessageBox.Show(owner,"ابتدا ماژول خروجی را اجرا کنید.");return;}
        var dialog=new SaveFileDialog {Filter="PNG (*.png)|*.png|BMP (*.bmp)|*.bmp|TIFF (*.tif)|*.tif",AddExtension=true,FileName="export.png"}; if(dialog.ShowDialog(owner)!=true)return;
        try {GraphExports.SaveImage(result.Image,dialog.FileName,new[]{projectPath}.OfType<string>());Status.Text="تصویر ذخیره شد.";} catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) {ModuleErrors.Show(owner,ex,"خطای خروجی تصویر");}
    }
    private void ExportGraphPalette(ModuleNode node)
    {
        var owner=Window.GetWindow(Inspector) ?? this; if(!results.TryGetValue(node.Id,out var result) || result.Palette is null) {MessageBox.Show(owner,"پالت را وصل و ماژول خروجی را اجرا کنید.");return;}
        var dialog=new SaveFileDialog {Filter="CSV (*.csv)|*.csv",AddExtension=true,FileName="palette.csv"};if(dialog.ShowDialog(owner)!=true)return;
        try {GraphExports.SavePalette(result.Palette,dialog.FileName,new[]{projectPath}.OfType<string>());Status.Text="جدول پالت ذخیره شد.";} catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or InvalidDataException) {ModuleErrors.Show(owner,ex,"خطای خروجی جدول");}
    }
    private void ExportMachine(ModuleNode node)
    {
        if(!results.TryGetValue(node.Id,out var value) || value.Image is null) { MessageBox.Show(this,"ابتدا ماژول ماشین را اجرا کنید."); return; }
        var dialog=new SaveFileDialog { Filter="JC5 (*.jc5)|*.jc5",FileName=MachineFileNaming.SuggestedName(node.MachineFormat),AddExtension=true }; if(dialog.ShowDialog(this)!=true) return;
        if (!MachineFileNaming.ValidDestination(dialog.FileName,node.MachineFormat)) { MessageBox.Show(this,"نام یا پسوند فایل برای قالب انتخاب‌شده معتبر نیست."); return; }
        try { Jc5Codec.Save(value.Image,dialog.FileName,new[]{projectPath}.OfType<string>()); Status.Text="فایل آزمایشی ماشین ذخیره شد؛ پیش از تولید آن را بررسی کنید."; } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { ModuleErrors.Show(Window.GetWindow(Inspector) ?? this,ex); }
    }
    private bool ConfirmDiscard() { if(!dirty)return true; var result=MessageBox.Show(this,"تغییرات پروژه ذخیره نشده‌اند. ذخیره شوند؟","پروژه",MessageBoxButton.YesNoCancel,MessageBoxImage.Warning); return result==MessageBoxResult.No || result==MessageBoxResult.Yes && SaveGraph(false); }
    private void Legacy_Click(object sender,RoutedEventArgs e) { if(!busy) new MainWindow().Show(); }
    private void New_Click(object sender,RoutedEventArgs e) { if(busy || !ConfirmDiscard())return; CreateDefaultGraph(); projectPassword=null; openedWithRecovery=false; dirty=false; UpdateTitle(); RenderGraph(); Inspect(); }
    private void Open_Click(object sender,RoutedEventArgs e)
    {
        if(busy)return; var dialog=new OpenFileDialog { Filter="SAM projects (*.sam)|*.sam|Graph project (*.jgw)|*.jgw" }; if(dialog.ShowDialog(this)!=true || !ConfirmDiscard())return;
        try
        {
            if(!ModuleProjectFile.IsGraph(dialog.FileName)) { if(!dialog.FileName.EndsWith(".sam",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("پروژه گراف معتبر نیست."); var legacy=new MainWindow(); legacy.Show(); legacy.OpenLegacyProject(dialog.FileName); Status.Text="پروژه SAM قدیمی در محیط سازگار باز شد."; return; }
            bool encrypted=ModuleProjectFile.IsEncrypted(dialog.FileName); string? password=null; ModuleGraph loaded;
            if(encrypted) {var prompt=new PasswordPrompt("بازکردن پروژه","رمز پروژه را وارد کنید.") {Owner=this}; if(prompt.ShowDialog()!=true)return; password=prompt.Password; loaded=ModuleProjectFile.LoadEncrypted(dialog.FileName,password);}
            else loaded=ModuleProjectFile.Load(dialog.FileName);
            graph=loaded; projectPath=dialog.FileName; projectPassword=password; openedWithRecovery=password is not null && EncryptedProject.IsRecoveryPassword(password); selected=graph.Nodes.FirstOrDefault()?.Id; selectedWire=null; connecting=null; results=new Dictionary<Guid,ModuleValue>(); dirty=false; UpdateTitle(); RenderGraph(); Inspect();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Text.Json.JsonException or NotSupportedException) { ModuleErrors.Show(this,ex,"خطای بازکردن پروژه"); }
    }
    private bool SaveGraph(bool saveAs)
    {
        string? target=projectPath, password=projectPassword; bool recovery=openedWithRecovery;
        if(saveAs || target is null || !target.EndsWith(".sam",StringComparison.OrdinalIgnoreCase))
        {
            var dialog=new SaveFileDialog { Filter="SAM graph project (*.sam)|*.sam",FileName=target is null ? "project.sam" : System.IO.Path.GetFileNameWithoutExtension(target)+".sam",AddExtension=true,DefaultExt=".sam" }; if(dialog.ShowDialog(this)!=true)return false; target=dialog.FileName;
        }
        if(password is null)
        {
            var prompt=new PasswordPrompt("رمز پروژه","برای ذخیره، رمز پروژه (حداقل ۸ نویسه) را وارد و تکرار کنید. رمز بازیابی مشترک در همه نسخه‌ها قابل استخراج است و محرمانگی را محدود می‌کند.",true) {Owner=this}; if(prompt.ShowDialog()!=true)return false; password=prompt.Password;
        }
        else if(saveAs)
        {
            var prompt=new PasswordPrompt("رمز پروژه","برای نسخهٔ جدید رمز را وارد و تکرار کنید. رمز بازیابی مشترک قابل استخراج است و محرمانگی را محدود می‌کند.",true) {Owner=this}; if(prompt.ShowDialog()!=true)return false; password=prompt.Password; recovery=false;
        }
        if(recovery) { var prompt=new PasswordPrompt("تعیین رمز جدید","پروژه با رمز بازیابی باز شده است. برای ذخیره، یک رمز جدید تعیین کنید؛ رمز قبلی دیگر معتبر نخواهد بود.",true) {Owner=this}; if(prompt.ShowDialog()!=true)return false; password=prompt.Password; recovery=false; }
        try {if(password is null) ModuleProjectFile.Save(target!,graph); else ModuleProjectFile.SaveEncrypted(target!,graph,password); projectPassword=password;openedWithRecovery=recovery;projectPath=target; dirty=false; UpdateTitle(); Status.Text="پروژه ماژولی ذخیره شد."; return true; } catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { ModuleErrors.Show(this,ex,"خطای ذخیره پروژه"); return false; }
    }
    private void Save_Click(object sender,RoutedEventArgs e) { if(!busy) SaveGraph(false); }
    private void SaveAs_Click(object sender,RoutedEventArgs e) { if(!busy) SaveGraph(true); }
    private void Window_Closing(object sender,System.ComponentModel.CancelEventArgs e) { if(closing)return; if(busy || !ConfirmDiscard()) { e.Cancel=true; return; } closing=true; }
}
