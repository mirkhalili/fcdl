using System.IO;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace JacquardStudio;

public sealed class ActivationWindow : Window
{
    private readonly UserPreferences preferences;private ECDsa? key;private string? device;
    private readonly TextBox customerName=new() {MaxLength=30,Margin=new Thickness(0,3,0,8)};
    private readonly TextBox phone=new() {MaxLength=30,Margin=new Thickness(0,3,0,8),FlowDirection=FlowDirection.LeftToRight};
    private readonly TextBox city=new() {MaxLength=25,Margin=new Thickness(0,3,0,8)};
    private readonly TextBox identity=new() {IsReadOnly=true,FlowDirection=FlowDirection.LeftToRight,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,10)};
    private readonly TextBox token=new() {AcceptsReturn=true,FlowDirection=FlowDirection.LeftToRight,TextWrapping=TextWrapping.Wrap,Height=110,MaxLength=4096,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,5,0,10)};
    private readonly TextBlock message=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    private readonly Button manual=new() {Content="ثبت کد فعال‌سازی",Margin=new Thickness(4),IsEnabled=false};
    private readonly Button online=new() {Content="فعال‌سازی آنلاین",Margin=new Thickness(4),IsEnabled=false};
    public ActivationWindow(UserPreferences prefs)
    {
        preferences=prefs;Title="فعال‌سازی سام";Width=700;Height=610;MinWidth=520;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterScreen;FlowDirection=FlowDirection.RightToLeft;SetResourceReference(BackgroundProperty,"Surface");SetResourceReference(ForegroundProperty,"PrimaryText");
        var root=new DockPanel {Margin=new Thickness(22)};var footer=new StackPanel {Orientation=Orientation.Horizontal};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);footer.Children.Add(manual);footer.Children.Add(online);var cancel=new Button {Content="خروج",Margin=new Thickness(4),IsCancel=true};footer.Children.Add(cancel);
        var content=new StackPanel();root.Children.Add(new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=root;
        content.Children.Add(new TextBlock {Text="فعال‌سازی نرم‌افزار",FontSize=22,FontWeight=FontWeights.Bold});
        content.Children.Add(new TextBlock {Text="شناسه دستگاه را برای مدیر بفرستید یا درخواست آنلاین ثبت کنید. کد پس از تأیید مدیر و با کلید معتبر همین نسخه صادر می‌شود.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,8)});
        content.Children.Add(new TextBlock {Text="شناسه دستگاه"});content.Children.Add(identity);var copy=new Button {Content="کپی شناسه",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,10)};copy.Click+=(_,_)=>{if(device is not null)Clipboard.SetText(device);};content.Children.Add(copy);
        content.Children.Add(new TextBlock {Text="مشخصات اختیاری برای درخواست آنلاین (برای درخواست در انتظار، در صورت خالی بودن ثبت می‌شود)"});
        content.Children.Add(new TextBlock {Text="نام کاربر"});content.Children.Add(customerName);content.Children.Add(new TextBlock {Text="شماره تلفن"});content.Children.Add(phone);content.Children.Add(new TextBlock {Text="شهر"});content.Children.Add(city);
        content.Children.Add(new TextBlock {Text="کد فعال‌سازی آفلاین"});content.Children.Add(token);
        var file=new Button {Content="انتخاب فایل کد فعال‌سازی",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,8)};
        file.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFileDialog {Filter="فایل کد فعال‌سازی|*.txt;*.lic|همه فایل‌ها|*.*",CheckFileExists=true};if(picker.ShowDialog(this)!=true)return;try{token.Text=ActivationStore.ReadTokenFile(picker.FileName);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){message.Text="خواندن فایل فعال‌سازی ممکن نیست: "+ex.Message;}};content.Children.Add(file);
        content.Children.Add(new TextBlock {Text="درخواست آنلاین پس از رضایت شما شناسه نصب و هش دستگاه، نام، تلفن و شهر واردشده، نسخه برنامه و ویندوز، مدل پردازنده، RAM و نام دستگاه را به سایت می‌فرستد. سایت زمان و IP را ثبت می‌کند. پس از تأیید، مجوز تا هفت روز بدون ارتباط معتبر است؛ لغو از راه دور در اتصال بعدی اعمال می‌شود.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});content.Children.Add(message);
        Loaded+=async (_,_)=>await InitializeAsync();Closed+=(_,_)=>key?.Dispose();
        manual.Click+=(_,_)=>{try{if(key is null || device is null || !ActivationStore.Accept(key,device,token.Text,DateTimeOffset.UtcNow)){message.Text=ActivationStore.IsRevoked()?"این شناسه غیرفعال است؛ ابتدا تأیید مجدد آنلاین لازم است.":"کد نامعتبر، منقضی یا متعلق به دستگاه دیگری است.";return;}DialogResult=true;}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException){message.Text="ذخیره کد ممکن نیست: "+ex.Message;}};
        online.Click+=async (_,_)=>{if(MessageBox.Show(this,"اطلاعات گفته‌شده برای ثبت درخواست و بررسی‌های بعدی به سایت ارسال شود؟","ارسال اطلاعات",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;preferences.TelemetryEnabled=true;try{preferences.Save();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){message.Text="ذخیره تنظیمات ممکن نیست: "+ex.Message;return;}await CheckOnlineAsync(true);};
    }
    private async Task InitializeAsync()
    {
        try
        {
            message.Text="در حال شناسایی سخت‌افزار…";key=ActivationStore.PublicKey();device=await Task.Run(HardwareIdentity.ReadDeviceId);if(!IsVisible)return;identity.Text=device;
            if(key is null){message.Text="کلید عمومی صدور در این نسخه قرار نگرفته است. مدیر باید ابتدا نسخه مخصوص انتشار را با کلید خود بسازد.";return;}
            manual.IsEnabled=online.IsEnabled=true;message.Text="کد را وارد کنید یا درخواست آنلاین بفرستید.";
            if(preferences.TelemetryEnabled)await CheckOnlineAsync(false);
            if(IsVisible && ActivationStore.Valid(key,device,DateTimeOffset.UtcNow))DialogResult=true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or CryptographicException or ManagementException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException){message.Text="آماده‌سازی فعال‌سازی ممکن نیست: "+ex.Message;}
    }
    private async Task CheckOnlineAsync(bool interactive)
    {
        if(key is null || device is null)return;manual.IsEnabled=online.IsEnabled=false;
        try
        {
            message.Text="در حال ارتباط با سایت…";var contact=interactive ? new SiteReporting.Contact(customerName.Text,phone.Text,city.Text) : null;var response=await SiteReporting.RequestAsync(contact);if(!IsVisible)return;
            if(response.Status is "blocked" or "expired"){ActivationStore.Revoke();message.Text=response.Status=="expired" ? "اعتبار مجوز به پایان رسیده است." : "این شناسه توسط مدیر غیرفعال شده است.";return;}
            if(response.Status=="pending" && ActivationStore.Valid(key,device,DateTimeOffset.UtcNow))ActivationStore.Clear();
            if(response.Status=="approved" && response.Token is { } issued && ActivationStore.Accept(key,device,issued,DateTimeOffset.UtcNow,allowRevoked:true)) {message.Text="فعال‌سازی موفق بود.";if(interactive)DialogResult=true;return;}
            message.Text=response.Status=="pending" ? "درخواست ثبت شد؛ پس از تأیید مدیر دوباره بررسی کنید." : "سایت کد معتبر برای این نسخه صادر نکرده است.";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or ManagementException or InvalidOperationException or ArgumentException or CryptographicException or JsonException or System.Runtime.InteropServices.COMException){message.Text="سایت در دسترس نیست؛ در صورت داشتن کد معتبر می‌توانید آفلاین ادامه دهید.";}
        finally {manual.IsEnabled=online.IsEnabled=true;}
    }
}
