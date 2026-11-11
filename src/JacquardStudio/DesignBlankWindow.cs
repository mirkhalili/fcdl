using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace JacquardStudio;
public sealed record DesignBlankSettings(int Width,int Height,double WarpDensity,double WeftDensity)
{
    public void Validate()
    {
        if(Width<1 || Height<1 || (long)Width*Height>DesignProcessor.MaxPixels)throw new ArgumentException("تعداد تار و پود باید مثبت و حاصل‌ضرب حداکثر چهار میلیون باشد.");
        if(!double.IsFinite(WarpDensity) || !double.IsFinite(WeftDensity) || WarpDensity<.1 || WarpDensity>500 || WeftDensity<.1 || WeftDensity>500)throw new ArgumentException("تراکم تار و پود باید بین ۰٫۱ تا ۵۰۰ در سانتی‌متر باشد.");
    }
}
public sealed class DesignBlankWindow : Window
{
    public DesignBlankSettings? Result {get;private set;}
    public DesignBlankWindow(DesignBlankSettings initial)
    {
        Title="ایجاد صفحه سفید طراحی";Width=440;Height=510;MinWidth=350;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;FlowDirection=FlowDirection.RightToLeft;SetResourceReference(BackgroundProperty,"Surface");SetResourceReference(ForegroundProperty,"PrimaryText");
        var root=new DockPanel {Margin=new Thickness(18)};var buttons=new WrapPanel();DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);var apply=new Button {Content="ایجاد صفحه",Margin=new Thickness(4),Padding=new Thickness(10,6,10,6)};var cancel=new Button {Content="انصراف",Margin=new Thickness(4),IsCancel=true};buttons.Children.Add(apply);buttons.Children.Add(cancel);
        var content=new StackPanel();root.Children.Add(new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        content.Children.Add(new TextBlock {Text="تصویری به ورودی وصل نیست. تعداد تار و پود، ابعاد بوم پیکسلی هستند؛ تراکم‌ها برای ثبت مشخصات طراحی‌اند.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});
        TextBox Field(string title,string text){content.Children.Add(new TextBlock {Text=title,Margin=new Thickness(0,5,0,3)});var box=new TextBox {Text=text,FlowDirection=FlowDirection.LeftToRight,Padding=new Thickness(6)};content.Children.Add(box);return box;}
        var width=Field("تعداد تار / عرض (پیکسل)",initial.Width.ToString());var height=Field("تعداد پود / ارتفاع (پیکسل)",initial.Height.ToString());var warp=Field("تراکم تار (تار در سانتی‌متر)",initial.WarpDensity.ToString(CultureInfo.InvariantCulture));var weft=Field("تراکم پود (پود در سانتی‌متر)",initial.WeftDensity.ToString(CultureInfo.InvariantCulture));
        static bool Decimal(string text,out double value)=>double.TryParse(NumberInput.Normalize(text).Replace('٫','.').Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out value);
        apply.Click+=(_,_)=>{if(!NumberInput.TryInt(width.Text,out int w) || !NumberInput.TryInt(height.Text,out int h) || !Decimal(warp.Text,out double wd) || !Decimal(weft.Text,out double pd)){MessageBox.Show(this,"اعداد واردشده معتبر نیستند.");return;}try{var settings=new DesignBlankSettings(w,h,wd,pd);settings.Validate();Result=settings;DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(this,ex.Message);}};Content=root;
    }
}
