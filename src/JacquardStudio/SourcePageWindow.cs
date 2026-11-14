using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public sealed class SourcePageWindow : Window
{
    private readonly string path;
    private readonly bool limitWidth;
    private readonly Image image=new() { Stretch=Stretch.Uniform, Margin=new Thickness(10) };
    private readonly TextBlock status=new() { Margin=new Thickness(8), VerticalAlignment=VerticalAlignment.Center };
    private readonly Button previous=new() { Content="صفحه قبل", Margin=new Thickness(4) }, next=new() { Content="صفحه بعد", Margin=new Thickness(4) }, apply=new() { Content="اعمال تنظیمات و بستن", Margin=new Thickness(4) };
    private bool loading;
    public ImageResult? Result { get; private set; }
    private ImageResult current;
    public SourcePageWindow(string path, ImageResult first, bool limitWidth=false)
    {
        this.path=path; this.limitWidth=limitWidth; current=first; Title="انتخاب صفحه طرح"; Width=900; Height=750; MinWidth=560; MinHeight=420; FlowDirection=FlowDirection.RightToLeft; WindowStartupLocation=WindowStartupLocation.CenterOwner; SetResourceReference(BackgroundProperty,"AppBackground");
        var root=new DockPanel { Margin=new Thickness(16) }; var controls=new WrapPanel(); DockPanel.SetDock(controls,Dock.Top); root.Children.Add(controls); controls.Children.Add(apply);
        var cancel=new Button { Content="انصراف", Margin=new Thickness(4), IsCancel=true }; controls.Children.Add(cancel); controls.Children.Add(previous); controls.Children.Add(next); controls.Children.Add(status);
        root.Children.Add(new Border { Child=image, Background=Brushes.White, Margin=new Thickness(0,12,0,0) }); Content=root;
        apply.SetResourceReference(Control.BackgroundProperty,"ActionBackground"); apply.SetResourceReference(Control.ForegroundProperty,"AccentForeground");
        apply.Click+=(_,_)=>{if(!loading) {Result=current; DialogResult=true;}}; previous.Click+=async (_,_)=>await ChangeAsync(current.PageIndex-1); next.Click+=async (_,_)=>await ChangeAsync(current.PageIndex+1);
        Closing+=(_,e)=>{if(loading)e.Cancel=true;}; Refresh();
    }
    private void Refresh() { image.Source=current.Image; status.Text=$"صفحه {current.PageIndex+1} از {current.PageCount} · {current.Format}" + (current.OriginalWidth>0 ? $" · عرض {current.OriginalWidth} ← {current.Image.PixelWidth} پیکسل" : ""); previous.IsEnabled=!loading && current.PageIndex>0; next.IsEnabled=!loading && current.PageIndex+1<current.PageCount; apply.IsEnabled=!loading; }
    private async Task ChangeAsync(int page)
    {
        if(loading || page<0 || page>=current.PageCount) return; loading=true; Refresh();
        try { current=await Task.Run(()=>ImageLoader.Load(path,page,limitWidth)); }
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or NotSupportedException or InvalidOperationException) { MessageBox.Show(this,ex.Message); }
        finally { loading=false; Refresh(); }
    }
}
