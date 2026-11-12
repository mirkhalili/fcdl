using System.IO;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public sealed class ActivationWindow : Window
{
    private readonly UserPreferences preferences;
    private ECDsa? key; private string? device; private bool busy;
    private readonly TextBox customerName=new() {MaxLength=30,Margin=new Thickness(0,3,0,8)};
    private readonly TextBox phone=new() {MaxLength=30,Margin=new Thickness(0,3,0,8),FlowDirection=FlowDirection.LeftToRight};
    private readonly TextBox city=new() {MaxLength=25,Margin=new Thickness(0,3,0,8)};
    private readonly TextBox identity=new() {IsReadOnly=true,FlowDirection=FlowDirection.LeftToRight,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,8)};
    private readonly TextBox token=new() {AcceptsReturn=true,FlowDirection=FlowDirection.LeftToRight,TextWrapping=TextWrapping.Wrap,Height=80,MaxLength=4096,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,5,0,8)};
    private readonly TextBlock message=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    private readonly Button manual=new() {Content="ثبت کد فعال‌سازی",Margin=new Thickness(4),IsEnabled=false};
    private readonly Button online=new() {Content="فعال‌سازی آنلاین",Margin=new Thickness(4),IsEnabled=false};
    public ActivationWindow(UserPreferences prefs)
    {
        preferences=prefs;Title="فعال‌سازی سام";Width=700;Height=720;MinWidth=520;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterScreen;FlowDirection=FlowDirection.RightToLeft;
        SetResourceReference(BackgroundProperty,"Surface");SetResourceReference(ForegroundProperty,"PrimaryText");SetResourceReference(FontFamilyProperty,"VazirFont");
        Icon=new BitmapImage(new Uri("pack://application:,,,/JacquardStudio;component/Assets/app.ico",UriKind.Absolute));
        var root=new DockPanel {Margin=new Thickness(22)};var footer=new WrapPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);footer.Children.Add(online);footer.Children.Add(manual);footer.Children.Add(new Button {Content="خروج",Margin=new Thickness(4),IsCancel=true});
        var content=new StackPanel();root.Children.Add(new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=root;
        var header=new StackPanel {Orientation=Orientation.Horizontal};header.Children.Add(new Image {Source=new BitmapImage(new Uri("pack://application:,,,/JacquardStudio;component/Assets/sam-emblem.png",UriKind.Absolute)),Width=70,Height=70,Margin=new Thickness(10)});header.Children.Add(new TextBlock {Text="فعال‌سازی نرم‌افزار سام",FontSize=22,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center});content.Children.Add(header);
        content.Children.Add(new TextBlock {Text="برای فعال‌سازی آنلاین نام، تلفن و شهر را وارد کنید و درخواست بفرستید. پس از ثبت کد توسط مدیر، در اجرای بعدی فعال‌سازی خودکار انجام می‌شود. برای روش دستی، کد یا فایل فعال‌سازی را وارد و ثبت کنید.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,8)});
        content.Children.Add(new TextBlock {Text="شناسه دستگاه"});content.Children.Add(identity);var copy=new Button {Content="کپی شناسه",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,8)};copy.Click+=(_,_)=>{if(device is not null)Clipboard.SetText(device);};content.Children.Add(copy);
        content.Children.Add(new TextBlock {Text="نام و نام خانوادگی"});content.Children.Add(customerName);content.Children.Add(new TextBlock {Text="شماره تلفن"});content.Children.Add(phone);content.Children.Add(new TextBlock {Text="شهر"});content.Children.Add(city);
        content.Children.Add(new TextBlock {Text="کد فعال‌سازی دستی"});content.Children.Add(token);
        var file=new Button {Content="انتخاب فایل کد فعال‌سازی",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,8)};
        file.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFileDialog {Filter="فایل کد فعال‌سازی|*.txt;*.lic|همه فایل‌ها|*.*",CheckFileExists=true};if(picker.ShowDialog(this)!=true)return;try{token.Text=ActivationStore.ReadTokenFile(picker.FileName);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){message.Text="خواندن فایل فعال‌سازی ممکن نیست: "+ex.Message;}};content.Children.Add(file);content.Children.Add(message);
        foreach(var input in new[]{customerName,phone,city,token})input.TextChanged+=(_,_)=>RefreshButtons();
        Loaded+=async (_,_)=>await InitializeAsync();Closed+=(_,_)=>key?.Dispose();
        manual.Click+=async (_,_)=>await ActivateManualAsync();online.Click+=async (_,_)=>await ActivateOnlineAsync();
    }
    private void RefreshButtons()
    {
        bool ready=device is not null && !busy;
        online.IsEnabled=ready && !string.IsNullOrWhiteSpace(customerName.Text) && !string.IsNullOrWhiteSpace(phone.Text) && !string.IsNullOrWhiteSpace(city.Text);
        manual.IsEnabled=ready && !string.IsNullOrWhiteSpace(token.Text);
    }
    private SiteReporting.Contact Contact() => new(customerName.Text.Trim(),phone.Text.Trim(),city.Text.Trim());
    private bool Consent()
    {
        if(preferences.TelemetryEnabled)return true;
        if(MessageBox.Show(this,"برای درخواست و بررسی مجوز در هر اجرا، نام، تلفن، شهر، شناسه نصب و هش دستگاه، نسخه برنامه و ویندوز، نام سیستم، مدل پردازنده و RAM به سایت ارسال می‌شود؛ سایت IP و زمان را ثبت می‌کند. آیا موافقید؟", "رضایت ارسال اطلاعات",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return false;
        preferences.TelemetryEnabled=true;preferences.Save();return true;
    }
    private async Task InitializeAsync()
    {
        try
        {
            busy=true;RefreshButtons();message.Text="در حال شناسایی دستگاه…";key=ActivationStore.PublicKey();device=await Task.Run(HardwareIdentity.ReadDeviceId);if(!IsVisible)return;identity.Text=device;
            var saved=SiteReporting.LoadContact();if(saved is not null){customerName.Text=saved.CustomerName;phone.Text=saved.Phone;city.Text=saved.City;}
            message.Text=key is null ? "این نسخه کلید عمومی صدور ندارد؛ ثبت درخواست ممکن است، ولی فعال‌سازی به نسخه دارای کلید صحیح نیاز دارد." : "برای ارسال درخواست آنلاین مشخصات را کامل کنید یا کد دستی را وارد کنید.";
            if(preferences.TelemetryEnabled)await CheckOnlineAsync(false);
            if(IsVisible && key is not null && ActivationStore.Valid(key,device,DateTimeOffset.UtcNow))DialogResult=true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or ManagementException or InvalidOperationException or ArgumentException or JsonException or System.Runtime.InteropServices.COMException){message.Text="آماده‌سازی فعال‌سازی ممکن نیست: "+ex.Message;}
        finally{busy=false;RefreshButtons();}
    }
    private async Task ActivateOnlineAsync()
    {
        try{if(!Consent())return;SiteReporting.SaveContact(Contact());await CheckOnlineAsync(true);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException){message.Text="ثبت اطلاعات ممکن نیست: "+ex.Message;}
    }
    private async Task ActivateManualAsync()
    {
        try
        {
            if(key is null){message.Text="کلید عمومی صدور در این نسخه وجود ندارد؛ مدیر باید نسخه هماهنگ با کلید خود را بسازد.";return;}
            if(device is null || !ActivationStore.Accept(key,device,token.Text,DateTimeOffset.UtcNow)){message.Text=ActivationStore.IsRevoked()?"این شناسه غیرفعال است؛ ابتدا تأیید مجدد آنلاین لازم است.":"کد نامعتبر، منقضی یا متعلق به دستگاه دیگری است.";return;}
            if(Consent()){SiteReporting.SaveContact(Contact());await CheckOnlineAsync(false);}
            if(IsVisible && ActivationStore.Valid(key,device,DateTimeOffset.UtcNow))DialogResult=true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException){message.Text="ذخیره کد ممکن نیست: "+ex.Message;}
    }
    private async Task CheckOnlineAsync(bool interactive)
    {
        if(device is null)return;busy=true;RefreshButtons();
        try
        {
            message.Text="در حال ارتباط با سایت…";var saved=SiteReporting.LoadContact();var details=key is null?null:ActivationStore.Details(key,device,DateTimeOffset.UtcNow);
            var response=await SiteReporting.RequestAsync(interactive?Contact():saved,details?.ManualToken);if(!IsVisible)return;
            if(response.Status is "blocked" or "expired"){ActivationStore.Revoke();message.Text=response.Status=="expired"?"اعتبار مجوز به پایان رسیده است.":"این شناسه توسط مدیر غیرفعال شده است.";return;}
            if(response.Status=="pending" && key is not null && details is {ManualToken:null})ActivationStore.Clear();
            if(response.Status=="approved" && response.Token is { } issued)
            {
                if(key is null){message.Text="کد آماده است، اما این نسخه کلید عمومی صدور ندارد؛ نسخه هماهنگ را از مدیر دریافت کنید.";return;}
                if(ActivationStore.Accept(key,device,issued,DateTimeOffset.UtcNow,allowRevoked:true,manual:false)){message.Text="فعال‌سازی موفق بود.";if(interactive)DialogResult=true;return;}
                message.Text="کد سایت معتبر نیست یا با کلید این نسخه مطابقت ندارد.";return;
            }
            message.Text=response.Status=="pending"?"درخواست ثبت شد؛ پس از ثبت کد توسط مدیر، برنامه را دوباره اجرا کنید.":"کد معتبر در سایت آماده نیست.";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or ManagementException or InvalidOperationException or ArgumentException or CryptographicException or JsonException or System.Runtime.InteropServices.COMException){message.Text="سایت در دسترس نیست؛ کد معتبر تا پایان مهلت آفلاین قابل استفاده است.";}
        finally {busy=false;RefreshButtons();}
    }
}
