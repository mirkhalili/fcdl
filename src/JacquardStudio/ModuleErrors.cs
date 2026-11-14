using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace JacquardStudio;
public static class ModuleErrors
{
    public static string Explain(Exception ex)
    {
        string m=ex.Message;
        if(Regex.IsMatch(m,"[\\u0600-\\u06FF]"))return m;
        if(m.StartsWith("Connect input '")) {var match=Regex.Match(m,"Connect input '([^']+)'"); string port=match.Groups[1].Value switch {"image"=>"تصویر","fabric"=>"پارچه","palette"=>"پالت رنگ","weaves"=>"بافت‌ها","machine"=>"ماشین",_=>"ورودی موردنیاز"}; return $"ورودی «{port}» متصل نیست. خروجی سازگار ماژول قبلی را به آن وصل کنید و دوباره تلاش کنید.";}
        if(m.Contains("cycle",StringComparison.OrdinalIgnoreCase))return "این اتصال یک چرخه ایجاد می‌کند. اتصال را به شاخه‌ای بدون بازگشت به همین ماژول وصل کنید.";
        if(m.Contains("Incompatible ports"))return "نوع خروجی و ورودی با هم سازگار نیست. برای نمونه تصویر را به ورودی تصویر و پالت را به ورودی پالت وصل کنید.";
        if(m.Contains("pure binary",StringComparison.OrdinalIgnoreCase) || m.Contains("Hook map must",StringComparison.OrdinalIgnoreCase))return "نقشه باید مات و فقط شامل پیکسل‌های سیاه و سفید خالص باشد. تصویر را اصلاح یا ماژول تبدیل سیاه‌وسفید را استفاده کنید.";
        if(m.Contains("missing weave") || m.Contains("at least one weave"))return "بافت انتخاب‌شده موجود نیست. مجموعه بافت را باز کنید و برای رنگ‌های باقی‌مانده بافت معتبر انتخاب کنید.";
        if(m.Contains("Choose an input image"))return "تصویر منبع انتخاب نشده است. ماژول طرح را باز کنید و تصویر و صفحه موردنظر را انتخاب کنید.";
        if(m.Contains("Yarn count must",StringComparison.OrdinalIgnoreCase) || m.Contains("Yarn count is outside",StringComparison.OrdinalIgnoreCase))return "نمره نخ معتبر نیست؛ مقدار مثبت و واحد مناسب وارد کنید. محدوده تبدیل‌شده بین ۰٫۰۵ تا ۲۰۰۰۰ تِکس است.";
        if(m.Contains("Effective yarn density",StringComparison.OrdinalIgnoreCase))return "چگالی مؤثر نخ باید از ۰٫۰۵ تا ۵ گرم بر سانتی‌متر مکعب باشد.";
        if(m.Contains("Thread density",StringComparison.OrdinalIgnoreCase))return "تراکم تار و پود باید بین ۰٫۱ تا ۵۰۰ نخ یا رج در سانتی‌متر باشد.";
        if(m.Contains("Invalid simulation settings",StringComparison.OrdinalIgnoreCase))return "تنظیمات شبیه‌سازی معتبر نیست. نوع نخ، حالت، سایه و تکرارهای ۱ تا ۱۶ را بررسی کنید.";
        if(m.Contains("overlap",StringComparison.OrdinalIgnoreCase))return "ستون‌های پود با طرح یا کناره هم‌پوشانی دارند. موقعیت شروع طرح و پود و عرض کناره را بررسی کنید.";
        if(m.Contains("Total hooks",StringComparison.OrdinalIgnoreCase))return "تعداد کل زه برای طرح و کناره‌ها کافی نیست یا از محدودیت اندازه بیشتر است. تعریف ماشین و عرض طرح را بررسی کنید.";
        if(m.Contains("pixel",StringComparison.OrdinalIgnoreCase) || m.Contains("too large",StringComparison.OrdinalIgnoreCase))return "اندازه یا حجم ورودی بیش از حد مجاز است. اندازه تصویر و محدودیت چهار میلیون پیکسلِ خروجی را بررسی کنید.";
        if(ex is HttpRequestException || ex is TaskCanceledException)return "اتصال به سرویس برقرار نشد یا زمان درخواست تمام شد. اینترنت، نشانی سرویس و دسترسی حساب را بررسی کنید.";
        if(ex is UnauthorizedAccessException)return "اجازه دسترسی به فایل یا پوشه وجود ندارد. مسیر دیگری انتخاب کنید یا دسترسی پوشه را بررسی کنید.";
        if(ex is FileNotFoundException)return "فایل موردنظر پیدا نشد. مسیر فایل را بررسی کنید و دوباره انتخاب کنید.";
        if(ex is IOException)return "خواندن یا نوشتن فایل ممکن نشد. مسیر، فضای خالی و دسترسی فایل را بررسی کنید.";
        if(ex is InvalidDataException or FormatException)return "داده یا قالب فایل معتبر نیست یا پشتیبانی نمی‌شود. فایل و تنظیمات ورودی را بررسی کنید.";
        if(ex is NotSupportedException)return "این قالب یا عملیات در این نسخه پشتیبانی نمی‌شود. نوع ورودی و محدودیت‌های ماژول را بررسی کنید.";
        return "تنظیمات یا ورودی‌های ماژول معتبر نیستند. اتصال‌ها، ابعاد و مقادیر را بررسی کنید و دوباره تلاش کنید.";
    }
    public static void Show(Window owner,Exception ex,string title="خطای ماژول")
    {
        var dialog=new Window {Title=title,Width=520,SizeToContent=SizeToContent.Height,MinHeight=210,MaxHeight=500,Owner=owner,WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft,ResizeMode=ResizeMode.NoResize};dialog.SetResourceReference(Window.BackgroundProperty,"Surface");
        var panel=new StackPanel {Margin=new Thickness(24)};panel.Children.Add(new TextBlock {Text=title,FontSize=19,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,10)});
        panel.Children.Add(new TextBlock {Text=Explain(ex),TextWrapping=TextWrapping.Wrap,LineHeight=24,Margin=new Thickness(0,0,0,16)});
        var close=new Button {Content="متوجه شدم",HorizontalAlignment=HorizontalAlignment.Left,IsDefault=true,IsCancel=true};panel.Children.Add(close);dialog.Content=panel;dialog.ShowDialog();
    }
}
