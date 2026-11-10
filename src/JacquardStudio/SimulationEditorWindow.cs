using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace JacquardStudio;

/// <summary>Transactional simulation editor: draft settings and preview do not change the graph until Apply succeeds.</summary>
public sealed class SimulationEditorWindow : Window
{
    private readonly BitmapSource fabric;
    private readonly string[] rows;
    private readonly SimulationSettings original;
    private readonly string? projectPath;
    private readonly StackPanel settingsPanel=new();
    private readonly Image preview=new() {Stretch=Stretch.Fill};
    private readonly ScrollViewer previewScroll=new() {HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FlowDirection=FlowDirection.LeftToRight};
    private readonly TextBlock status=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4)};
    private readonly Button render=new() {Content="به‌روزرسانی پیش‌نمایش",Margin=new Thickness(4)};
    private readonly Button apply=new() {Content="اعمال و بستن",Margin=new Thickness(4)};
    private readonly Button cancel=new() {Content="انصراف",Margin=new Thickness(4)};
    private readonly Button detail=new() {Content="نمای جزئیات سه‌بعدی",Margin=new Thickness(4)};
    private readonly Button save=new() {Content="ذخیره تصویر",Margin=new Thickness(4),IsEnabled=false};
    private readonly TextBox warpCount,warpDensity,weftDensity,materialDensity,repeatX,repeatY;
    private readonly ComboBox warpUnit,warpType,mode,weave;
    private readonly ColorSwatch warpColor;
    private readonly CheckBox reverse;
    private readonly Slider shadow;
    private readonly Dictionary<string,(ColorSwatch color,ComboBox type,TextBox count,ComboBox unit)> weftControls=new(StringComparer.Ordinal);
    private BitmapSource? rendered;
    private bool rendering, previewCurrent;
    private double zoom=1;
    public SimulationSettings? Result {get;private set;}
    public SimulationEditorWindow(BitmapSource fabric,IReadOnlyList<string>? rowWefts,SimulationSettings initial,string? projectPath=null)
    {
        FabricSimulation.Validate(initial); this.fabric=fabric;this.original=initial;this.projectPath=projectPath; rows=rowWefts?.ToArray() ?? Array.Empty<string>();
        if(rows.Length>0 && rows.Length!=fabric.PixelHeight)throw new ArgumentException("توالی پود با تعداد رج نقشه مطابقت ندارد.");
        Title="شبیه‌سازی پارچه";Width=1200;Height=800;MinWidth=780;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterOwner;FlowDirection=FlowDirection.LeftToRight;SetResourceReference(BackgroundProperty,"Surface");SetResourceReference(ForegroundProperty,"PrimaryText");
        var root=new DockPanel {Margin=new Thickness(12)}; Content=root;
        var footer=new StackPanel {Orientation=Orientation.Horizontal,FlowDirection=FlowDirection.RightToLeft,Margin=new Thickness(0,8,0,0)}; DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);footer.Children.Add(apply);footer.Children.Add(cancel);footer.Children.Add(render);footer.Children.Add(detail);footer.Children.Add(save);
        var columns=new Grid();columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(360)});root.Children.Add(columns);
        var display=new DockPanel {Margin=new Thickness(0,0,12,0)};Grid.SetColumn(display,0);columns.Children.Add(display);
        var heading=new StackPanel {FlowDirection=FlowDirection.RightToLeft};DockPanel.SetDock(heading,Dock.Top);display.Children.Add(heading);
        heading.Children.Add(new TextBlock {Text="پیش‌نمایش پارچه",FontSize=20,FontWeight=FontWeights.Bold});heading.Children.Add(new TextBlock {Text="نمای گرافیکی تقریبی است و فایل ماشین را تغییر نمی‌دهد.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,4)});heading.Children.Add(status);
        var tools=new StackPanel {Orientation=Orientation.Horizontal,FlowDirection=FlowDirection.RightToLeft};DockPanel.SetDock(tools,Dock.Top);display.Children.Add(tools);
        var fit=new Button {Content="اندازه در پنجره",Margin=new Thickness(3)};var plus=new Button {Content="+",Margin=new Thickness(3)};var minus=new Button {Content="−",Margin=new Thickness(3)};tools.Children.Add(fit);tools.Children.Add(plus);tools.Children.Add(minus);
        var frame=new Border {CornerRadius=new CornerRadius(8),Padding=new Thickness(8)};frame.SetResourceReference(Border.BackgroundProperty,"GraphPreview");previewScroll.Content=preview;frame.Child=previewScroll;display.Children.Add(frame);
        var panelScroll=new ScrollViewer {Content=settingsPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FlowDirection=FlowDirection.RightToLeft};Grid.SetColumn(panelScroll,1);columns.Children.Add(panelScroll);settingsPanel.Margin=new Thickness(8,0,4,0);
        void Heading(string text) => settingsPanel.Children.Add(new TextBlock {Text=text,FontWeight=FontWeights.Bold,FontSize=16,Margin=new Thickness(0,14,0,6)});
        void Label(string text) => settingsPanel.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4)});
        TextBox Input(string text) {var x=new TextBox {Text=text,Padding=new Thickness(7),FlowDirection=FlowDirection.LeftToRight};settingsPanel.Children.Add(x);return x;}
        ComboBox Select(IEnumerable<string> values,int index) {var x=new ComboBox {ItemsSource=values,SelectedIndex=index};settingsPanel.Children.Add(x);return x;}
        Heading("تنظیمات نمایش");Label("ظاهر نخ");mode=Select(new[]{"معمولی", "پرز و سایه"},Math.Clamp(initial.Mode,0,1));Label("ساختار پیش‌نمایش");weave=Select(new[]{"نقشه ورودی", "تافته نمایشی", "طرح نمایشی چهارگام"},initial.Weave);
        Label("شدت سایه");shadow=new Slider {Minimum=0,Maximum=100,Value=initial.Shadow,TickFrequency=10,IsSnapToTickEnabled=true};settingsPanel.Children.Add(shadow);
        reverse=new CheckBox {Content="نمای پشت (آینه افقی و وارونگی رو/زیر)",IsChecked=initial.ReverseSide,Margin=new Thickness(0,10,0,0)};settingsPanel.Children.Add(reverse);
        Label("تکرار مستقیم افقی (۱ تا ۱۶)");repeatX=Input(initial.RepeatX.ToString());Label("تکرار مستقیم عمودی (۱ تا ۱۶)");repeatY=Input(initial.RepeatY.ToString());
        Heading("نخ تار");Label("نوع نخ تار");warpType=Select(FabricSimulation.YarnNames,initial.WarpYarn);Label("رنگ تار");warpColor=new ColorSwatch(initial.WarpColor,"انتخاب رنگ تار");settingsPanel.Children.Add(warpColor);Label("نمره نخ تار");warpCount=Input(initial.WarpCount);Label("واحد نمره تار");warpUnit=Select(YarnGeometry.Units,initial.WarpUnit);
        Heading("تراکم و چگالی");Label("تار در سانتی‌متر");warpDensity=Input(initial.WarpDensity);Label("رج در سانتی‌متر");weftDensity=Input(initial.WeftDensity);Label("چگالی مؤثر نخ (g/cm³)");materialDensity=Input(initial.MaterialDensity);
        Heading("نخ‌های پود موجود در نقشه");
        var ids=rows.Length==0 ? new[]{"1"} : rows.Distinct(StringComparer.Ordinal).ToArray();
        if(ids.Length>256)throw new ArgumentException("تعداد پودهای نقشه بیش از ۲۵۶ است.");
        foreach(string id in ids)
        {
            var yarn=FabricSimulation.YarnFor(initial,id);Label("پود " + id + " — نوع نخ");var type=Select(FabricSimulation.YarnNames,yarn.Yarn);Label("رنگ پود " + id);var color=new ColorSwatch(yarn.Color,"رنگ پود " + id);settingsPanel.Children.Add(color);Label("نمره نخ پود " + id);var count=Input(yarn.Count);Label("واحد نمره پود " + id);var unit=Select(YarnGeometry.Units,yarn.Unit);weftControls[id]=(color,type,count,unit);
        }
        settingsPanel.Children.Add(new TextBlock {Text="هر تغییری ابتدا فقط پیش‌نویس است. به‌روزرسانی تصویر تنظیمات را ذخیره نمی‌کند؛ برای ثبت در پروژه «اعمال و بستن» را بزنید.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,8)});
        void InvalidatePreview() {previewCurrent=false;save.IsEnabled=false;status.Text="تنظیمات تغییر کرده است؛ پیش‌نمایش نمایش‌داده‌شده قدیمی است. برای خروجی جدید آن را به‌روزرسانی کنید.";}
        foreach(var c in settingsPanel.Children.OfType<TextBox>()) c.TextChanged+=(_,_)=>InvalidatePreview();
        foreach(var c in settingsPanel.Children.OfType<ComboBox>()) c.SelectionChanged+=(_,_)=>InvalidatePreview();
        foreach(var c in settingsPanel.Children.OfType<ColorSwatch>()) c.ColorChanged+=(_,_)=>InvalidatePreview();
        reverse.Click+=(_,_)=>InvalidatePreview();shadow.ValueChanged+=(_,_)=>InvalidatePreview();
        render.Click+=async (_,_)=>await RenderAsync(false);apply.Click+=async (_,_)=>await RenderAsync(true);cancel.Click+=(_,_)=>{if(!rendering)DialogResult=false;};
        plus.Click+=(_,_)=>SetZoom(zoom*1.25);minus.Click+=(_,_)=>SetZoom(zoom/1.25);fit.Click+=(_,_)=>Fit();
        save.Click+=(_,_)=>{if(rendered is null)return;var file=new SaveFileDialog {Filter="PNG (*.png)|*.png|BMP (*.bmp)|*.bmp|TIFF (*.tif)|*.tif",FileName="simulation.png",AddExtension=true};if(file.ShowDialog(this)!=true)return;try{GraphExports.SaveImage(rendered,file.FileName,new[]{projectPath}.OfType<string>());}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException){ModuleErrors.Show(this,ex,"خطای ذخیره تصویر");}};
        detail.Click+=(_,_)=>{try{var s=ReadDraft();FabricSimulation.Validate(s);MessageBox.Show(this,"نمای سه‌بعدی یک نمونه محدود و تقریبی است. تنظیمات آن مستقل از این پیش‌نمایش است؛ تکرار و نمایش پشت دوبعدی به آن منتقل نمی‌شوند.","نمای سه‌بعدی",MessageBoxButton.OK,MessageBoxImage.Information);new Fabric3DWindow(fabric,rows,s with {RepeatX=1,RepeatY=1,ReverseSide=false},false) {Owner=this}.ShowDialog();}catch(ArgumentException ex){ModuleErrors.Show(this,ex,"تنظیمات شبیه‌سازی");}};
        Closing+=(_,e)=>{if(rendering)e.Cancel=true;};Loaded+=async (_,_)=>await RenderAsync(false);
    }
    private SimulationSettings ReadDraft()
    {
        int Number(TextBox input) => NumberInput.TryInt(input.Text,out int n) ? n : throw new ArgumentException("تعداد تکرار باید عدد صحیح بین ۱ تا ۱۶ باشد.");
        var stored=(original.Wefts ?? Array.Empty<SimulatedYarn>()).ToDictionary(x=>x.Id,StringComparer.Ordinal);
        foreach(var (id,c) in weftControls) stored[id]=new SimulatedYarn(id,c.type.SelectedIndex,c.color.Value,c.count.Text,c.unit.SelectedIndex);
        var first=stored[weftControls.Keys.First()];
        return original with {Mode=mode.SelectedIndex,Weave=weave.SelectedIndex,Shadow=(int)shadow.Value,ReverseSide=reverse.IsChecked==true,RepeatX=Number(repeatX),RepeatY=Number(repeatY),WarpYarn=warpType.SelectedIndex,WarpColor=warpColor.Value,WarpCount=warpCount.Text,WarpUnit=warpUnit.SelectedIndex,WarpDensity=warpDensity.Text,WeftDensity=weftDensity.Text,MaterialDensity=materialDensity.Text,WeftYarn=first.Yarn,WeftColor=first.Color,Wefts=stored.Values.ToArray()};
    }
    private void SetZoom(double value) {if(rendered is null)return;zoom=Math.Clamp(value,.02,6);preview.Width=Math.Max(1,rendered.PixelWidth*zoom);preview.Height=Math.Max(1,rendered.PixelHeight*zoom);}
    private void Fit(){if(rendered is null)return;double w=previewScroll.ViewportWidth,h=previewScroll.ViewportHeight;if(w>0 && h>0)SetZoom(Math.Min(w/rendered.PixelWidth,h/rendered.PixelHeight));}
    private async Task RenderAsync(bool commit)
    {
        if(rendering)return;
        try
        {
            var draft=ReadDraft();FabricSimulation.Validate(draft);rendering=true;settingsPanel.IsEnabled=false;render.IsEnabled=apply.IsEnabled=cancel.IsEnabled=detail.IsEnabled=save.IsEnabled=false;status.Text="در حال ساخت پیش‌نمایش…";
            var result=await Task.Run(()=>FabricSimulation.Render(fabric,draft,rows));rendered=result;previewCurrent=true;preview.Source=result;previewScroll.UpdateLayout();Fit();save.IsEnabled=true;
            double warp=FabricSimulation.WarpDiameter(draft), dx=YarnGeometry.SpacingMm(FabricSimulation.Number(draft.WarpDensity)),dy=YarnGeometry.SpacingMm(FabricSimulation.Number(draft.WeftDensity));
            bool overlap=warp>dx || weftControls.Keys.Any(id=>FabricSimulation.WeftDiameter(draft,FabricSimulation.YarnFor(draft,id))>dy);
            status.Text=$"نقشه {fabric.PixelWidth} × {fabric.PixelHeight}؛ قطر تقریبی تار {warp:F3} و فاصله تار {dx:F3}، پود {dy:F3} میلی‌متر. " + (overlap ? "هشدار: قطر نخ از فاصله بیشتر است و در تصویر محدود می‌شود. " : "") + "پیش‌نمایش تقریبی است؛ جزئیات طرح بزرگ ممکن است نمونه‌برداری شود.";
            if(commit){Result=draft;rendering=false;DialogResult=true;}
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or IOException or OverflowException){status.Text=ModuleErrors.Explain(ex);}
        finally {rendering=false;settingsPanel.IsEnabled=true;render.IsEnabled=apply.IsEnabled=cancel.IsEnabled=detail.IsEnabled=true;save.IsEnabled=previewCurrent;}
    }
}
