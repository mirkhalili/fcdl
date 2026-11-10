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
}
