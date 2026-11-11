using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace JacquardStudio;

/// <summary>Transactional raster design editor; changes are applied only on confirmation.</summary>
public sealed class DesignEditorWindow : Window
{
    private DesignCanvas doc;
    private readonly BitmapSource input;
    private readonly Image picture=new() {Stretch=Stretch.Fill,IsHitTestVisible=false};
    private readonly Image tracing=new() {Stretch=Stretch.Fill,IsHitTestVisible=false,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
    private readonly Image selectionImage=new() {Stretch=Stretch.Fill,IsHitTestVisible=false};
    private readonly Polyline path=new() {Stroke=Brushes.Yellow,StrokeThickness=2,IsHitTestVisible=false};
    private readonly Grid view=new() {HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Cursor=Cursors.Cross,Background=Brushes.Transparent};
    private readonly TextBlock status=new() {Margin=new Thickness(4),TextWrapping=TextWrapping.Wrap};
    private readonly ComboBox tool=new() {ItemsSource=new[]{"قلم", "خط", "مستطیل", "مستطیل پر", "پرکردن", "قطره‌چکان", "انتخاب مستطیلی", "انتخاب آزاد", "جابه‌جایی انتخاب", "پاک‌کن", "کانتور رنگ", "بیضی", "بیضی پر", "انتخاب رنگ یکسان"},SelectedIndex=0,Width=175,Margin=new Thickness(4)};
    private readonly ComboBox brushSize=new() {ItemsSource=new[]{1,2,3,4,5,8,12,16,24,32},SelectedIndex=0,Width=65,Margin=new Thickness(4)};
    private readonly CheckBox repeat=new() {Content="کانتور در راپورت",Margin=new Thickness(6),VerticalAlignment=VerticalAlignment.Center};
    private readonly ListBox layers=new() {MinHeight=145,MaxHeight=230,Margin=new Thickness(4)};
    private readonly TextBox layerName=new() {Margin=new Thickness(4),ToolTip="نام لایه انتخاب‌شده"};
    private readonly TextBox layerOpacity=new() {Margin=new Thickness(4),ToolTip="درصد شفاف نبودن لایه، از ۰ تا ۱۰۰"};
    private readonly TextBox traceOpacity=new() {Text="40",Width=48,Margin=new Thickness(3),ToolTip="درصد نمایش تصویر مرجع"};
    private readonly List<DesignCanvas> undo=new(),redo=new();
    private DesignCanvas.Clip? clipboard;
    private string? tracePng; private bool traceVisible=true; private int traceAlpha=40; private int traceX,traceY,traceW,traceH;
    private readonly TextBox traceXText=new() {Width=47,Margin=new Thickness(2)},traceYText=new() {Width=47,Margin=new Thickness(2)},traceWText=new() {Width=47,Margin=new Thickness(2)},traceHText=new() {Width=47,Margin=new Thickness(2)};
    private bool updating; private Color color=Colors.Black;private readonly Button colorButton=new() {Content="انتخاب رنگ",Margin=new Thickness(4)};
    private double zoom;private (int X,int Y)? start,last;private readonly List<(int X,int Y)> pathPoints=new();private DesignCanvas? before;
    private readonly TextBlock info=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(5)};
    public BitmapSource? Result { get; private set; }
    public DesignDocumentData? Document { get; private set; }
    public DesignEditorWindow(BitmapSource image, DesignDocumentData? saved=null)
    {
        input=image;doc=saved is null ? new DesignCanvas(image) : new DesignCanvas(image,saved);
        if(saved is not null) {tracePng=saved.TracePng;traceVisible=saved.TraceVisible;traceAlpha=saved.TraceOpacity;traceX=saved.TraceX;traceY=saved.TraceY;traceW=saved.TraceWidth;traceH=saved.TraceHeight;if(traceW<0 || traceH<0 || traceW>16000 || traceH>16000 || Math.Abs((long)traceX)>16000 || Math.Abs((long)traceY)>16000)throw new InvalidDataException("ابعاد تصویر مرجع معتبر نیست.");if(traceAlpha<0 || traceAlpha>100)throw new InvalidDataException("شفافیت مرجع معتبر نیست.");if(tracePng is not null)tracing.Source=DesignCanvas.Decode(tracePng);}
        Title="استودیو طراحی طرح";Width=1250;Height=850;MinWidth=820;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterOwner;FlowDirection=FlowDirection.RightToLeft;
        SetResourceReference(BackgroundProperty,"AppBackground");SetResourceReference(ForegroundProperty,"PrimaryText");
        var root=new DockPanel {Margin=new Thickness(10)};var bar=new WrapPanel();DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);bar.Children.Add(tool);bar.Children.Add(new TextBlock {Text="اندازه:",VerticalAlignment=VerticalAlignment.Center});bar.Children.Add(brushSize);
        colorButton.Click+=(_,_)=>{var chosen=ColorChooser.Pick(this,$"#{color.R:X2}{color.G:X2}{color.B:X2}");if(chosen is not null)color=(Color)ColorConverter.ConvertFromString(chosen);UpdateColor();};bar.Children.Add(colorButton);UpdateColor();bar.Children.Add(repeat);
        void Add(Panel panel,string label,Action action,string? tip=null) {var b=new Button {Content=label,Margin=new Thickness(3),Padding=new Thickness(7,5,7,5),ToolTip=tip};b.Click+=(_,_)=>action();panel.Children.Add(b);}
        Add(bar,"−",()=>Zoom(zoom/1.5));Add(bar,"+",()=>Zoom(zoom*1.5));Add(bar,"۱۰۰٪",()=>Zoom(1));
        Add(bar,"بازگشت",()=>History(undo,redo));Add(bar,"بازانجام",()=>History(redo,undo));
        Add(bar,"اعمال و بستن",Apply);Add(bar,"انصراف",()=>DialogResult=false);
        var side=new ScrollViewer {Width=255,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};DockPanel.SetDock(side,Dock.Right);root.Children.Add(side);var panel=new StackPanel {Margin=new Thickness(4)};side.Content=panel;
        panel.Children.Add(new TextBlock {Text="لایه‌ها (بالاتر، روی تصویر)",FontWeight=FontWeights.Bold,Margin=new Thickness(4)});panel.Children.Add(layers);
        layers.SelectionChanged+=(_,_)=>{if(updating || layers.SelectedIndex<0)return;doc.Active=doc.Layers.Count-1-layers.SelectedIndex;RefreshLayers();};
        var layerButtons=new WrapPanel();panel.Children.Add(layerButtons);
        Add(layerButtons,"+ لایه",()=>Mutate(()=>doc.AddLayer("لایه " + (doc.Layers.Count+1))));
        Add(layerButtons,"حذف",()=>{if(MessageBox.Show(this,"لایه انتخاب‌شده حذف شود؟","حذف لایه",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)Mutate(doc.DeleteLayer);});
        Add(layerButtons,"↑",()=>Mutate(()=>doc.Reorder(1)),"یک لایه به بالا");Add(layerButtons,"↓",()=>Mutate(()=>doc.Reorder(-1)),"یک لایه به پایین");
        Add(layerButtons,"نمایش/پنهان",()=>Mutate(()=>doc.Current.Visible=!doc.Current.Visible));Add(layerButtons,"قفل/باز",()=>Mutate(()=>doc.Current.Locked=!doc.Current.Locked));
        panel.Children.Add(new TextBlock {Text="نام و درصد نمایانی لایه:",Margin=new Thickness(4)});panel.Children.Add(layerName);var opacityRow=new WrapPanel();opacityRow.Children.Add(layerOpacity);Add(opacityRow,"ثبت",()=>Mutate(()=>{string n=DesignCanvas.Name(layerName.Text);if(!NumberInput.TryInt(layerOpacity.Text,out int v)||v<0||v>100)throw new ArgumentException("درصد لایه باید از ۰ تا ۱۰۰ باشد.");doc.Current.Name=n;doc.Current.Opacity=v;}));panel.Children.Add(opacityRow);
        panel.Children.Add(new Separator {Margin=new Thickness(4)});panel.Children.Add(new TextBlock {Text="انتخاب و کلیپ‌بورد داخلی",FontWeight=FontWeights.Bold,Margin=new Thickness(4)});var selectButtons=new WrapPanel();panel.Children.Add(selectButtons);
        Add(selectButtons,"همه",()=>{doc.SelectAll();Refresh();});Add(selectButtons,"معکوس",()=>{doc.InvertSelection();Refresh();});
        Add(selectButtons,"کپی",()=>Copy());Add(selectButtons,"برش",()=>{if(Copy())Mutate(doc.ClearSelected);});Add(selectButtons,"چسباندن",Paste);Add(selectButtons,"پاک کردن",()=>Mutate(doc.ClearSelected));
        panel.Children.Add(new Separator {Margin=new Thickness(4)});panel.Children.Add(new TextBlock {Text="تصویر مرجع برای Tracing",FontWeight=FontWeights.Bold,Margin=new Thickness(4)});
        var traceButtons=new WrapPanel();panel.Children.Add(traceButtons);Add(traceButtons,"ورودی",()=>SetTrace(input));Add(traceButtons,"بازکردن تصویر",LoadTrace);Add(traceButtons,"نمایش/پنهان",()=>{traceVisible=!traceVisible;Refresh();});Add(traceButtons,"حذف",()=>{tracePng=null;tracing.Source=null;Refresh();});
        var traceGeometry=new WrapPanel();panel.Children.Add(new TextBlock {Text="مرجع: X، Y، عرض، ارتفاع (پیکسل)",Margin=new Thickness(4)});traceXText.Text=traceX.ToString();traceYText.Text=traceY.ToString();traceWText.Text=(traceW==0?doc.Width:traceW).ToString();traceHText.Text=(traceH==0?doc.Height:traceH).ToString();traceGeometry.Children.Add(traceXText);traceGeometry.Children.Add(traceYText);traceGeometry.Children.Add(traceWText);traceGeometry.Children.Add(traceHText);Add(traceGeometry,"اعمال ابعاد",()=>{if(!NumberInput.TryInt(traceXText.Text,out int x)||!NumberInput.TryInt(traceYText.Text,out int y)||!NumberInput.TryInt(traceWText.Text,out int w)||!NumberInput.TryInt(traceHText.Text,out int h)||Math.Abs((long)x)>16000||Math.Abs((long)y)>16000||w<1||h<1||w>16000||h>16000){MessageBox.Show(this,"مختصات باید بین ۱۶۰۰۰- و ۱۶۰۰۰ و ابعاد بین ۱ و ۱۶۰۰۰ باشد.");return;}traceX=x;traceY=y;traceW=w;traceH=h;Refresh();});panel.Children.Add(traceGeometry);
        var traceRow=new WrapPanel();traceRow.Children.Add(new TextBlock {Text="نمایانی مرجع ٪",VerticalAlignment=VerticalAlignment.Center});traceOpacity.Text=traceAlpha.ToString();traceRow.Children.Add(traceOpacity);Add(traceRow,"ثبت",()=>{if(!NumberInput.TryInt(traceOpacity.Text,out int n)||n<0||n>100){MessageBox.Show(this,"درصد مرجع باید بین ۰ تا ۱۰۰ باشد.");return;}traceAlpha=n;Refresh();});panel.Children.Add(traceRow);
        panel.Children.Add(info);DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);
        view.ClipToBounds=true;view.Children.Add(picture);view.Children.Add(tracing);view.Children.Add(selectionImage);view.Children.Add(path);foreach(var img in new[]{picture,tracing,selectionImage})RenderOptions.SetBitmapScalingMode(img,BitmapScalingMode.NearestNeighbor);
        var scroll=new ScrollViewer {Content=view,FlowDirection=FlowDirection.LeftToRight,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};scroll.SetResourceReference(BackgroundProperty,"CanvasBackground");root.Children.Add(scroll);Content=root;
        view.MouseLeftButtonDown+=Down;view.MouseMove+=Move;view.MouseLeftButtonUp+=Up;view.LostMouseCapture+=(_,_)=>{if(start is not null){if(before is not null)doc=before;before=null;start=last=null;pathPoints.Clear();path.Points.Clear();Refresh();}};
        PreviewKeyDown+=Keys;zoom=Math.Min(1,Math.Min(850.0/doc.Width,570.0/doc.Height));Refresh();
    }
    private int Size => (int)brushSize.SelectedItem;
    private void UpdateColor(){colorButton.Background=new SolidColorBrush(color);colorButton.Foreground=(color.R*299+color.G*587+color.B*114)/1000<128?Brushes.White:Brushes.Black;}
    private void Zoom(double n){zoom=Math.Clamp(n,.05,24);Refresh();}
    private void RefreshLayers()
    {
        updating=true;layers.Items.Clear();for(int i=doc.Layers.Count-1;i>=0;i--){var l=doc.Layers[i];layers.Items.Add($"{(l.Visible?"◉":"○")} {(l.Locked?"🔒":"")} {l.Name} · {l.Opacity}٪");}layers.SelectedIndex=doc.Layers.Count-1-doc.Active;layerName.Text=doc.Current.Name;layerOpacity.Text=doc.Current.Opacity.ToString();updating=false;
    }
    private void Refresh()
    {
        picture.Source=doc.Composite();tracing.Visibility=traceVisible && tracePng is not null?Visibility.Visible:Visibility.Collapsed;tracing.Opacity=traceAlpha/100.0;tracing.Width=(traceW==0?doc.Width:traceW)*zoom;tracing.Height=(traceH==0?doc.Height:traceH)*zoom;tracing.RenderTransform=new TranslateTransform(traceX*zoom,traceY*zoom);
        view.Width=Math.Max(1,doc.Width*zoom);view.Height=Math.Max(1,doc.Height*zoom);RefreshSelection();RefreshLayers();
        status.Text=$"{doc.Width} × {doc.Height} پیکسل | زوم {zoom*100:0}% | انتخاب: {doc.SelectedCount} پیکسل | بازگشت: {undo.Count} | لایه فعال: {doc.Current.Name}";
        info.Text="Tracing: تصویر مرجع فقط روی صفحه دیده می‌شود و در خروجی نیست. برای رسم مستقل لایه خالی بسازید. محل و اندازه مرجع برحسب پیکسل قابل تنظیم است. رنگ‌برداری و کانتور از لایه فعال است.";
    }
    private void RefreshSelection()
    {
        if(doc.Selection is null){selectionImage.Source=null;return;}
        int w=doc.Width,h=doc.Height;byte[] pixels=new byte[checked(w*h*4)];var mask=doc.Selection;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){int p=y*w+x;if(!mask[p])continue;if(x>0 && x<w-1 && y>0 && y<h-1 && mask[p-1]&&mask[p+1]&&mask[p-w]&&mask[p+w])continue;int i=p*4;pixels[i]=0;pixels[i+1]=230;pixels[i+2]=255;pixels[i+3]=240;}
        var bmp=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,w*4);bmp.Freeze();selectionImage.Source=bmp;
    }
    private (int X,int Y)? Pixel(MouseEventArgs e){var p=e.GetPosition(view);return GraphImageOperations.FindPixel(doc.Width,doc.Height,p.X,p.Y,view.ActualWidth,view.ActualHeight);}
    private void Push(List<DesignCanvas> list,DesignCanvas snapshot){list.Add(snapshot);long sum=list.Sum(d=>d.MemoryBytes);while(list.Count>1 && sum>96L*1024*1024){sum-=list[0].MemoryBytes;list.RemoveAt(0);}while(list.Count>20)list.RemoveAt(0);}
    private void Mutate(Action action)
    {
        if(start is not null)return;var old=doc.Clone();try{action();Push(undo,old);redo.Clear();Refresh();}catch(Exception ex) when(ex is ArgumentException or InvalidOperationException){doc=old;Refresh();MessageBox.Show(this,ex.Message,"طراحی");}
    }
    private void History(List<DesignCanvas> from,List<DesignCanvas> to){if(start is not null || from.Count==0)return;Push(to,doc.Clone());doc=from[^1];from.RemoveAt(from.Count-1);Refresh();}
    private bool Copy(){try{clipboard=doc.Copy();status.Text="ناحیه لایه فعال در کلیپ‌بورد داخلی کپی شد.";return true;}catch(InvalidOperationException ex){MessageBox.Show(this,ex.Message);return false;}}
    private void Paste(){if(clipboard is null){MessageBox.Show(this,"ابتدا یک ناحیه را کپی کنید.");return;}Mutate(()=>{doc.AddLayer("ناحیه چسبانده‌شده");doc.Paste(clipboard,clipboard.X,clipboard.Y);});}
    private void Apply(){try{var result=doc.Composite();var saved=doc.Save();saved.TracePng=tracePng;saved.TraceOpacity=traceAlpha;saved.TraceVisible=traceVisible;saved.TraceX=traceX;saved.TraceY=traceY;saved.TraceWidth=traceW;saved.TraceHeight=traceH;Result=result;Document=saved;DialogResult=true;}catch(Exception ex) when(ex is IOException or ArgumentException or InvalidOperationException){MessageBox.Show(this,ex.Message,"خطای ذخیره طراحی");}}
    private void SetTrace(BitmapSource bmp){try{if((long)bmp.PixelWidth*bmp.PixelHeight>DesignProcessor.MaxPixels)throw new ArgumentException("تصویر مرجع باید حداکثر چهار میلیون پیکسل باشد.");string png=ModuleExecutor.EmbedImage(bmp);tracePng=png;tracing.Source=bmp;traceVisible=true;Refresh();}catch(Exception ex) when(ex is IOException or ArgumentException or InvalidOperationException){MessageBox.Show(this,ex.Message);}}
    private void LoadTrace(){var picker=new OpenFileDialog {Filter="تصویر|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif",CheckFileExists=true};if(picker.ShowDialog(this)!=true)return;try{SetTrace(ImageLoader.Load(picker.FileName,0).Image);}catch(Exception ex) when(ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException){MessageBox.Show(this,ex.Message);}}
    private void Keys(object sender,KeyEventArgs e)
    {
        if(Keyboard.FocusedElement is TextBox)return;
        if(Keyboard.Modifiers==ModifierKeys.Control){if(e.Key==Key.Z){History(undo,redo);e.Handled=true;}else if(e.Key==Key.Y){History(redo,undo);e.Handled=true;}else if(e.Key==Key.C){Copy();e.Handled=true;}else if(e.Key==Key.X){if(Copy())Mutate(doc.ClearSelected);e.Handled=true;}else if(e.Key==Key.V){Paste();e.Handled=true;}else if(e.Key==Key.A){doc.SelectAll();Refresh();e.Handled=true;}}
        else if(e.Key==Key.Delete){Mutate(doc.ClearSelected);e.Handled=true;}else if(e.Key==Key.Escape && start is not null){if(before is not null)doc=before;before=null;start=last=null;pathPoints.Clear();path.Points.Clear();view.ReleaseMouseCapture();Refresh();e.Handled=true;}
    }
    private void Down(object sender,MouseButtonEventArgs e)
    {
        var p=Pixel(e);if(p is null)return;int t=tool.SelectedIndex;
        if(t==5){color=doc.At(p.Value.X,p.Value.Y);UpdateColor();return;}
        if(t==13){doc.SelectColor(p.Value.X,p.Value.Y);Refresh();return;}
        if(t==4){Mutate(()=>doc.Fill(p.Value.X,p.Value.Y,color));return;}
        if(t==10){var target=doc.At(p.Value.X,p.Value.Y);Mutate(()=>doc.Contour(target,color,Size,repeat.IsChecked==true));return;}
        if(t is 0 or 1 or 2 or 3 or 9 or 11 or 12){if(doc.Current.Locked){MessageBox.Show(this,"لایه قفل است.");return;}before=doc.Clone();}
        if(t==8 && doc.Current.Locked){MessageBox.Show(this,"لایه قفل است.");return;}
        start=last=p;pathPoints.Clear();path.Points.Clear();if(t==7)AddPath(p.Value);
        if(t is 0 or 1 or 2 or 3 or 9 or 11 or 12)doc.Line(p.Value.X,p.Value.Y,p.Value.X,p.Value.Y,t==9?Colors.Transparent:color,Size);
        view.CaptureMouse();Refresh();e.Handled=true;
    }
    private void AddPath((int X,int Y) p){if(pathPoints.Count>=2048 || pathPoints.Count>0 && pathPoints[^1]==p)return;pathPoints.Add(p);path.Points.Add(new Point((p.X+.5)*zoom,(p.Y+.5)*zoom));}
    private void Move(object sender,MouseEventArgs e)
    {
        if(start is null || e.LeftButton!=MouseButtonState.Pressed)return;var p=Pixel(e);if(p is null)return;int t=tool.SelectedIndex;
        if(t==7){AddPath(p.Value);last=p;return;}
        if(t is 0 or 9 && last is { } prev)doc.Line(prev.X,prev.Y,p.Value.X,p.Value.Y,t==9?Colors.Transparent:color,Size);
        else if(t is 1 or 2 or 3 or 11 or 12 && before is not null){doc=before.Clone();if(t==1)doc.Line(start.Value.X,start.Value.Y,p.Value.X,p.Value.Y,color,Size);else if(t is 11 or 12)doc.Ellipse(start.Value.X,start.Value.Y,p.Value.X,p.Value.Y,color,Size,t==12);else doc.Rectangle(start.Value.X,start.Value.Y,p.Value.X,p.Value.Y,color,Size,t==3);}
        last=p;if(t is 0 or 1 or 2 or 3 or 9 or 11 or 12)Refresh();
    }
    private void Up(object sender,MouseButtonEventArgs e)
    {
        if(start is null)return;var begin=start.Value;var p=Pixel(e) ?? last ?? begin;int t=tool.SelectedIndex;
        try {if(t==6)doc.SelectRectangle(begin.X,begin.Y,p.X,p.Y);else if(t==7){AddPath(p);doc.SelectPolygon(pathPoints);}else if(t==8){var old=doc.Clone();try{doc.MoveSelection(p.X-begin.X,p.Y-begin.Y);Push(undo,old);redo.Clear();}catch{doc=old;throw;}}else if(before is not null){Push(undo,before);redo.Clear();}}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException){if(before is not null)doc=before;MessageBox.Show(this,ex.Message,"طراحی");}
        before=null;start=last=null;pathPoints.Clear();path.Points.Clear();view.ReleaseMouseCapture();Refresh();e.Handled=true;
    }
}
