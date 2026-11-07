using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace JacquardStudio;

/// <summary>Offline Persian guide. Illustrations are schematic, not screenshots.</summary>
public sealed class GuideWindow : Window
{
    private static readonly Brush Ink = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#17343E"));
    private static readonly Brush Accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#087E70"));
    private static readonly Brush Pale = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F6F2"));
    private static readonly Brush Edge = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#84B9B0"));
    private readonly StackPanel content = new();
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private static readonly string[] Titles = { "شروع سریع", "بانک و طراحی بافت", "کناره", "فلوت‌گیری", "تحلیل تار", "پودگذاری (Box Motion)", "خروجی ماشین", "ذخیره پروژه و گزارش", "کلیدهای میان‌بر", "نصب و SmartScreen" };

    public GuideWindow(bool english)
    {
        Title = english ? "User guide (Persian)" : "راهنمای تصویری برنامه"; Width = 1050; Height = 760; MinWidth = 680; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FlowDirection = FlowDirection.RightToLeft;
        Background = (Brush)Application.Current.FindResource("AppBackground");
        var root = new Grid { Margin = new Thickness(16) }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) }); root.ColumnDefinitions.Add(new ColumnDefinition()); Content = root;
        var navigation = new StackPanel(); navigation.Children.Add(new TextBlock { Text = "فهرست راهنما", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 4, 4, 10) });
        var navScroll = new ScrollViewer { Content = navigation, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(navScroll);
        scroll.Content = content; Grid.SetColumn(scroll, 1); root.Children.Add(scroll);
        content.Margin = new Thickness(18, 0, 0, 12);
        content.Children.Add(new TextBlock { Text = "راهنمای تصویری ژاکارد استودیو", FontSize = 23, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        content.Children.Add(new TextBlock { Text = "تصاویر زیر شماتیک‌اند؛ فلش‌ها ترتیب کار را نشان می‌دهند.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 15), Foreground = (Brush)Application.Current.FindResource("Muted") });
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/JacquardStudio;component/Help.fa.txt", UriKind.Absolute)) ?? throw new InvalidOperationException("Guide resource is missing.");
        using var reader = new StreamReader(resource.Stream); string text = reader.ReadToEnd().Replace("\r\n", "\n");
        for (int i = 0; i < Titles.Length; i++)
        {
            string title = Titles[i]; int start = text.IndexOf("\n" + title + "\n", StringComparison.Ordinal); if (start < 0) continue;
            start += title.Length + 2; int end = i + 1 < Titles.Length ? text.IndexOf("\n" + Titles[i + 1] + "\n", start, StringComparison.Ordinal) : text.Length; if (end < 0) end = text.Length;
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
            section.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.Bold, Foreground = (Brush)Application.Current.FindResource("Accent"), Margin = new Thickness(0, 0, 0, 8) });
            section.Children.Add(Diagram(i));
            section.Children.Add(new TextBlock { Text = text[start..end].Trim(), TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 26, Margin = new Thickness(0, 10, 0, 0) });
            content.Children.Add(section);
            var button = new Button { Content = title, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 2, 4, 2), Padding = new Thickness(8, 6, 8, 6), FontSize = 12 };
            button.Click += (_, _) => section.BringIntoView(); navigation.Children.Add(button);
        }
    }
    private static FrameworkElement Diagram(int section)
    {
        var canvas = new Canvas { Width = 680, Height = 160, Background = Brushes.White, ClipToBounds = true, FlowDirection = FlowDirection.LeftToRight };
        void Box(double x, double y, double w, double h, string text, Brush? fill = null)
        {
            var b = new Border { Width = w, Height = h, Background = fill ?? Pale, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Child = new TextBlock { Text = text, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Ink, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.RightToLeft } };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y); canvas.Children.Add(b);
        }
        void Arrow(double x1, double y1, double x2, double y2)
        {
            var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Accent, StrokeThickness = 2 }; canvas.Children.Add(line);
            double angle = Math.Atan2(y2 - y1, x2 - x1), n = 8;
            var p = new Polygon { Fill = Accent, Points = new PointCollection { new(x2, y2), new(x2 - n * Math.Cos(angle - .45), y2 - n * Math.Sin(angle - .45)), new(x2 - n * Math.Cos(angle + .45), y2 - n * Math.Sin(angle + .45)) } }; canvas.Children.Add(p);
        }
        void Cells(double x, double y, int columns, int rows, Func<int,int,bool> black, double size = 20)
        {
            for (int r = 0; r < rows; r++) for (int c = 0; c < columns; c++)
            { var rect = new Rectangle { Width = size, Height = size, Fill = black(c,r) ? Brushes.Black : Brushes.White, Stroke = Edge, StrokeThickness = .6 }; Canvas.SetLeft(rect, x+c*size); Canvas.SetTop(rect, y+r*size); canvas.Children.Add(rect); }
        }
        switch (section)
        {
            case 0: Box(25, 50, 125, 55, "۱ · باز کردن\nتصویر یا پروژه"); Box(205, 50, 125, 55, "۲ · اندازه\nو رنگ"); Box(385, 50, 125, 55, "۳ · انتخاب\nبافت هر رنگ"); Box(555, 50, 100, 55, "۴ · ادامه\nمراحل"); Arrow(150,77,205,77); Arrow(330,77,385,77); Arrow(510,77,555,77); break;
            case 1: Cells(35,35,4,4,(x,y)=>(x<2)!=(y<2)); Box(160,45,130,55,"۱ · طراحی\nیا پاناما"); Box(350,45,130,55,"۲ · ذخیره\nدر بانک"); Box(540,45,110,55,"۳ · اختصاص\nبه رنگ"); Arrow(115,75,160,75); Arrow(290,75,350,75); Arrow(480,75,540,75); break;
            case 2: Box(25,45,115,60,"کناره چپ",Brushes.LightSteelBlue); Box(140,45,400,60,"طرح اصلی"); Box(540,45,115,60,"کناره راست",Brushes.LightSteelBlue); Arrow(82,130,82,108); Arrow(597,130,597,108); break;
            case 3: Cells(40,30,6,5,(x,y)=>(x+y)%2==0,18); Box(205,45,130,55,"۱ · حداقل طول\nو جهت"); Box(390,45,120,55,"۲ · بررسی"); Box(555,45,95,55,"۳ · اصلاح\nنقطه طرح"); Arrow(335,73,390,73); Arrow(510,73,555,73); break;
            case 4: Box(30,42,150,65,"۱ · نقشه طرح\nو کناره"); Box(265,42,150,65,"۲ · محاسبه\nدرصدها"); Box(500,42,150,65,"۳ · اشاره به تار\nبرای دیدن درصد"); Arrow(180,75,265,75); Arrow(415,75,500,75); break;
            case 5:
                Box(20,32,115,55,"۱ · ورود اعداد\nپیش‌نویس"); Box(185,32,115,55,"۲ · اعمال"); Box(350,32,140,55,"۳ · نقشه نهایی\nو خروجی"); Arrow(135,60,185,60); Arrow(300,60,350,60);
                Box(35,108,90,30,"پود ۱ تا ۳"); Box(160,108,115,30,"سفید ۴ تا ۱۱"); Box(310,108,120,30,"کناره ۱۲ تا ۱۹"); Box(465,108,165,30,"شروع طرح: ۲۰"); break;
            case 6: Box(30,45,150,55,"۱ · انتخاب JC5\nیا JC6"); Box(265,45,150,55,"۲ · نمایش قرمز\nو بررسی نقشه"); Box(500,45,150,55,"۳ · تبدیل\nو ذخیره"); Arrow(180,72,265,72); Arrow(415,72,500,72); break;
            case 7: Box(35,45,145,55,"Ctrl+S\nذخیره پروژه"); Box(265,45,145,55,"SAM\nتصویر و تنظیمات"); Box(495,45,145,55,"گزارش\nPDF و چاپ"); Arrow(180,72,265,72); Arrow(410,72,495,72); break;
            case 8: Box(30,45,130,55,"F1\nراهنما"); Box(195,45,130,55,"Ctrl+S\nذخیره"); Box(360,45,130,55,"Ctrl+Z\nبازگشت"); Box(525,45,130,55,"Alt+1…9\nمراحل"); break;
            default: Box(70,45,155,55,"دریافت از\nمنبع رسمی"); Box(260,45,155,55,"بررسی هش\nو ناشر"); Box(450,45,155,55,"نصب و\nارتقای نسخه"); Arrow(225,72,260,72); Arrow(415,72,450,72); break;
        }
        var box = new Border { Background = Brushes.White, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6), Child = new Viewbox { Stretch = Stretch.Uniform, Child = canvas } };
        return box;
    }
}
