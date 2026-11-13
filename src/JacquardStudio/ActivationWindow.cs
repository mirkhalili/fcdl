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
    private readonly Button online=new() {Content="ثبت درخواست آنلاین",Margin=new Thickness(4),IsEnabled=false};
    private readonly Button siteCheck=new() {Content="استعلام مجوز از سایت",Margin=new Thickness(4),IsEnabled=false};
    public ActivationWindow(UserPreferences prefs)
    {
        preferences=prefs;Title="فعال‌سازی سام";Width=700;Height=720;MinWidth=520;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterScreen;FlowDirection=FlowDirection.RightToLeft;
        SetResourceReference(BackgroundProperty,"Surface");SetResourceReference(ForegroundProperty,"PrimaryText");SetResourceReference(FontFamilyProperty,"VazirFont");
        Icon=new BitmapImage(new Uri("pack://application:,,,/JacquardStudio;component/Assets/app.ico",UriKind.Absolute));
        var root=new DockPanel {Margin=new Thickness(22)};var footer=new WrapPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);footer.Children.Add(siteCheck);footer.Children.Add(online);footer.Children.Add(manual);footer.Children.Add(new Button {Content="خروج",Margin=new Thickness(4),IsCancel=true});
        var content=new StackPanel();root.Children.Add(new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=root;
        var header=new StackPanel {Orientation=Orientation.Horizontal};header.Children.Add(new Image {Source=new BitmapImage(new Uri("pack://application:,,,/JacquardStudio;component/Assets/sam-emblem.png",UriKind.Absolute)),Width=70,Height=70,Margin=new Thickness(10)});header.Children.Add(new TextBlock {Text="فعال‌سازی نرم‌افزار سام",FontSize=22,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center});content.Children.Add(header);
        content.Children.Add(new TextBlock {Text="برای بررسی مجوز موجود، «استعلام مجوز از سایت» را بزنید؛ اگر شناسه این دستگاه در سایت مجوز فعال داشته باشد، برنامه بدون واردکردن کد فعال می‌شود. برای ثبت درخواست جدید، نام، تلفن و شهر را وارد کنید. کد یا فایل فعال‌سازی را نیز می‌توان جداگانه ثبت کرد.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,8)});
        content.Children.Add(new TextBlock {Text="شناسه دستگاه"});content.Children.Add(identity);var copy=new Button {Content="کپی شناسه",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,8)};copy.Click+=(_,_)=>{if(device is not null)Clipboard.SetText(device);};content.Children.Add(copy);
        content.Children.Add(new TextBlock {Text="نام و نام خانوادگی"});content.Children.Add(customerName);content.Children.Add(new TextBlock {Text="شماره تلفن"});content.Children.Add(phone);content.Children.Add(new TextBlock {Text="شهر"});content.Children.Add(city);
        content.Children.Add(new TextBlock {Text="کد فعال‌سازی دستی"});content.Children.Add(token);
        var file=new Button {Content="انتخاب فایل کد فعال‌سازی",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,8)};
        file.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFileDialog {Filter="فایل کد فعال‌سازی|*.txt;*.lic|همه فایل‌ها|*.*",CheckFileExists=true};if(picker.ShowDialog(this)!=true)return;try{token.Text=ActivationStore.ReadTokenFile(picker.FileName);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){message.Text="خواندن فایل فعال‌سازی ممکن نیست: "+ex.Message;}};content.Children.Add(file);content.Children.Add(message);
        foreach(var input in new[]{customerName,phone,city,token})input.TextChanged+=(_,_)=>RefreshButtons();
        Loaded+=async (_,_)=>await InitializeAsync();Closed+=(_,_)=>key?.Dispose();
        siteCheck.Click+=async (_,_)=>await CheckSiteLicenseAsync();manual.Click+=async (_,_)=>await ActivateManualAsync();online.Click+=async (_,_)=>await ActivateOnlineAsync();
    }
    private void RefreshButtons()
    {
        bool ready=device is not null && !busy;
        siteCheck.IsEnabled=ready;
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
    private bool LicenseCheckConsent()
    {
        if(preferences.SiteLicenseCheckEnabled || preferences.TelemetryEnabled)return true;
        if(MessageBox.Show(this,"برای استعلام مجوز، شناسه نصب و کلید تصادفی آن، هش شناسه دستگاه و نسخه برنامه از راه HTTPS به سایت فرستاده می‌شود؛ سایت IP و زمان درخواست را نیز ثبت می‌کند. نام، تلفن، شهر و جزئیات سخت‌افزار ارسال نمی‌شوند. آیا موافقید؟","رضایت استعلام مجوز",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes){preferences.SiteLicenseCheckPrompted=true;preferences.Save();return false;}
        preferences.SiteLicenseCheckEnabled=true;preferences.SiteLicenseCheckPrompted=true;preferences.Save();return true;
    }
    private async Task InitializeAsync()
    {
        try
        {
            busy=true;RefreshButtons();message.Text="در حال شناسایی دستگاه…";try{key=ActivationStore.PublicKey();}catch(Exception ex) when(ex is CryptographicException or InvalidDataException or ArgumentException){key=null;}device=await Task.Run(HardwareIdentity.ReadDeviceId);if(!IsVisible)return;identity.Text=device;
            var saved=SiteReporting.LoadContact();if(saved is not null){customerName.Text=saved.CustomerName;phone.Text=saved.Phone;city.Text=saved.City;}
            message.Text=key is null ? "این نسخه کلید عمومی صدور ندارد؛ ثبت درخواست ممکن است، ولی فعال‌سازی به نسخه دارای کلید صحیح نیاز دارد." : "برای استعلام مجوز موجود دکمه «استعلام مجوز از سایت» را بزنید؛ برای درخواست جدید مشخصات را کامل کنید یا کد دستی را وارد کنید.";
            if(preferences.TelemetryEnabled)await CheckOnlineAsync(false);
            else if(preferences.SiteLicenseCheckEnabled || (!preferences.SiteLicenseCheckPrompted && LicenseCheckConsent()))await CheckOnlineAsync(false,true);
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
    private async Task CheckSiteLicenseAsync()
    {
        try {if(!LicenseCheckConsent())return;await CheckOnlineAsync(true,true);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException){message.Text="ثبت رضایت استعلام ممکن نیست: "+ex.Message;}
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
    private async Task CheckOnlineAsync(bool interactive,bool lookupOnly=false)
    {
        if(device is null)return;busy=true;RefreshButtons();
        try
        {
            message.Text="در حال استعلام مجوز از سایت…";var saved=lookupOnly?null:SiteReporting.LoadContact();var details=key is null?null:ActivationStore.Details(key,device,DateTimeOffset.UtcNow);
            SiteReporting.Contact? contact=lookupOnly?null:(interactive?Contact():saved);string? manualToken=lookupOnly?null:details?.ManualToken;
            var response=await SiteReporting.RequestAsync(contact,manualToken,lookupOnly);if(!IsVisible)return;
            if(response.Status is "blocked" or "expired"){ActivationStore.Revoke();message.Text=response.Status=="expired"?"اعتبار مجوز به پایان رسیده است.":"این شناسه توسط مدیر غیرفعال شده است.";return;}
            if(!lookupOnly && response.Status=="pending" && key is not null && details is {ManualToken:null})ActivationStore.Clear();
            if(response.Status=="approved")
            {
                if(key is null){message.Text="مجوز در سایت ثبت است، اما این نسخه کلید عمومی لازم برای بررسی امضا را ندارد.";return;}
                string? issued=response.Token;if(issued is null){message.Text="شناسه در سایت تأیید شده، اما سایت مجوز امضاشده صادر نکرده است؛ تنظیم کلید خصوصی سایت را بررسی کنید.";return;}
                if(response.ActivationCode is { } code && ActivationStore.AcceptOnline(key,device,code,issued,DateTimeOffset.UtcNow)){message.Text="فعال‌سازی موفق بود.";if(interactive)DialogResult=true;return;}
                ActivationStore.Revoke();message.Text="کد دریافتی در برنامه تأیید نشد؛ امضا، شناسه دستگاه و کلید صادرکننده را بررسی کنید.";return;
            }
            message.Text=response.Status switch
            {
                "pending" when lookupOnly=>"برای این شناسه مجوز فعال در سایت پیدا نشد. اگر انتظار مجوز دارید شناسه دستگاه را بررسی کنید.",
                "not_licensed"=>"برای این شناسه مجوز فعال در سایت پیدا نشد. اگر مجوزی ندارید، مشخصات را وارد و درخواست جدید ثبت کنید.",
                "pending"=>"درخواست ثبت شد؛ پس از تأیید و ثبت مجوز توسط مدیر، دوباره استعلام کنید.",
                "upgrade_required"=>"برای فعال‌سازی آنلاین، نسخه جدید برنامه را نصب کنید.",
                _=>"پاسخ سایت برای فعال‌سازی معتبر نیست."
            };
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or ManagementException or InvalidOperationException or ArgumentException or CryptographicException or JsonException or System.Runtime.InteropServices.COMException)
        {
            bool validOffline=key is not null && device is not null && ActivationStore.Valid(key,device,DateTimeOffset.UtcNow);
            message.Text=validOffline?"سایت در دسترس نیست؛ مجوز ذخیره‌شده تا پایان مهلت آفلاین قابل استفاده است.":"اتصال به سایت برقرار نشد؛ فعال‌سازی آنلاین نیاز به دسترسی اینترنت دارد. می‌توانید کد معتبر را دستی وارد کنید.";
        }
        finally {busy=false;RefreshButtons();}
    }
}
