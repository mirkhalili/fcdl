using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace JacquardStudio;
/// <summary>Original schematic illustrations, not screenshots from the supplied manual.</summary>
public static class GuideDiagrams
{
    public static FrameworkElement Create(string topic)
    {
        if(topic is "شبیه‌سازی و گزارش" or "راهنمای تصویری شبیه‌سازی" or "محدودیت‌های شبیه‌سازی") return SimulationIllustration();
        string[] labels=topic switch
        {
            "شروع و اتصال ماژول‌ها"=>new[]{"۱ · کتابخانه", "۲ · خروجی", "۳ · ورودی", "۴ · اجرا"},
            "تصویر، اندازه و رنگ"=>new[]{"تصویر", "اندازه", "پالت طرح", "پالت مرجع"},
            "نقشه آماده و سیاه‌وسفید"=>new[]{"تصویر مات", "آستانه", "نقشه دودویی", "پارچه"},
            "بافت، کناره و پودگذاری"=>new[]{"پالت و بافت", "نقشه بافت", "کناره", "پودگذاری"},
            "ماسک و انتخاب ناحیه"=>new[]{"تصویر", "ماسک مستطیلی", "اعمال ماسک", "خروجی جدا"},
            "راپورت و مرزها"=>new[]{"طرح پایه", "تکرار مستقیم", "لبه‌ها", "بازبینی"},
            "فلوت و راپورت مستقیم"=>new[]{"مرز چپ", "فلوت پیوسته", "مرز راست", "گزارش"},
            "مقایسه و کنترل کیفیت"=>new[]{"طرح اول", "طرح دوم", "تفاوت", "گزارش"},
            "شبیه‌سازی و گزارش" or "راهنمای تصویری شبیه‌سازی" or "محدودیت‌های شبیه‌سازی"=>new[]{"پیش‌نمایش پارچه", "نمایش / بزرگ‌نمایی", "تنظیمات تار و پود", "اعمال یا انصراف"},
            "پروژه و رمز"=>new[]{"پروژه گراف", "رمز پروژه", "ذخیره SAM", "بازآزمایی"},
            "ماشین و محدودیت‌های صنعتی"=>new[]{"پروفایل", "نقشه زه", "آزمون کنترلر", "تأیید متخصص"},
            _=>new[]{"تنظیمات", "سرویس مورد اعتماد", "تأیید ارسال", "نتیجه"}
        };
        var panel=new StackPanel();panel.Children.Add(new TextBlock {Text="نمودار شماتیک — تصویر واقعی پنجره نیست",FontSize=11,Margin=new Thickness(0,0,0,5)});
        var canvas=new Canvas {Width=680,Height=120,Background=Brushes.White,FlowDirection=FlowDirection.LeftToRight};
        var ink=new SolidColorBrush(Color.FromRgb(35,59,53));var green=new SolidColorBrush(Color.FromRgb(215,231,218));var edge=new SolidColorBrush(Color.FromRgb(102,139,118));
        for(int i=0;i<labels.Length;i++)
        {
            double x=10+i*170;var box=new Border {Width=145,Height=62,CornerRadius=new CornerRadius(9),Background=green,BorderBrush=edge,BorderThickness=new Thickness(1),Child=new TextBlock {Text=labels[i],Foreground=ink,FontSize=15,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,FlowDirection=FlowDirection.RightToLeft}};
            Canvas.SetLeft(box,x);Canvas.SetTop(box,22);canvas.Children.Add(box);
            if(i<3){var line=new Line {X1=x+148,Y1=53,X2=x+167,Y2=53,Stroke=edge,StrokeThickness=3};canvas.Children.Add(line);var arrow=new Polygon {Points=new PointCollection {new(x+167,53),new(x+160,48),new(x+160,58)},Fill=edge};canvas.Children.Add(arrow);}
        }
        panel.Children.Add(new Viewbox {Stretch=Stretch.Uniform,Child=canvas});return panel;
    }
    private static FrameworkElement SimulationIllustration()
    {
        var holder=new StackPanel();holder.Children.Add(new TextBlock {Text="تصویر شماتیک پنجره — نمونه آموزشی، نه تصویر واقعی برنامه",FontSize=11,Margin=new Thickness(0,0,0,5)});
        var canvas=new Canvas {Width=680,Height=260,Background=new SolidColorBrush(Color.FromRgb(245,248,244)),FlowDirection=FlowDirection.LeftToRight};
        var ink=new SolidColorBrush(Color.FromRgb(38,59,51));var edge=new SolidColorBrush(Color.FromRgb(122,155,135));
        void Box(double x,double y,double w,double h,string text,Color fill,double font=14)
        {
            var border=new Border {Width=w,Height=h,Background=new SolidColorBrush(fill),CornerRadius=new CornerRadius(5),BorderBrush=edge,BorderThickness=new Thickness(1),Child=new TextBlock {Text=text,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,FontSize=font,Foreground=ink,FlowDirection=FlowDirection.RightToLeft}};
            Canvas.SetLeft(border,x);Canvas.SetTop(border,y);canvas.Children.Add(border);
        }
        Box(12,10,411,190,"",Colors.White);Box(437,10,230,190,"",Color.FromRgb(236,242,233));
        Box(25,20,385,24,"۱ · نمایش پارچه و بزرگ‌نمایی",Color.FromRgb(224,237,223),13);
        Box(448,20,207,24,"۲ · تنظیمات نخ و نمایش",Color.FromRgb(210,226,208),13);
        // Interlaced schematic rather than copied fabric imagery.
        for(int y=0;y<4;y++)for(int x=0;x<12;x++)
        {
            var cell=new Rectangle {Width=24,Height=23,Fill=(x+y)%2==0?new SolidColorBrush(Color.FromRgb(195,157,124)):new SolidColorBrush(Color.FromRgb(100,143,136)),Stroke=Brushes.White,StrokeThickness=1};Canvas.SetLeft(cell,68+x*24);Canvas.SetTop(cell,55+y*23);canvas.Children.Add(cell);
        }
        Box(63,158,112,25,"اندازه در پنجره",Colors.White,11);Box(186,158,68,25,"+  /  −",Colors.White,12);
        Box(451,52,198,28,"رنگ و نمره تار",Colors.White,12);Box(451,85,198,28,"تراکم و چگالی",Colors.White,12);Box(451,118,198,28,"نخ هر پود",Colors.White,12);Box(451,151,198,28,"رو / پشت و تکرار",Colors.White,12);
        Box(40,214,135,28,"به‌روزرسانی",Color.FromRgb(224,237,223),12);Box(193,214,135,28,"ذخیره تصویر",Color.FromRgb(224,237,223),12);Box(346,214,135,28,"اعمال و بستن",Color.FromRgb(195,224,207),12);Box(499,214,135,28,"انصراف",Colors.White,12);
        holder.Children.Add(new Viewbox {Stretch=Stretch.Uniform,Child=canvas});return holder;
    }
}
