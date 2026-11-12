using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace JacquardStudio;
public sealed class SettingsWindow : Window
{
    public UserPreferences Result { get; }
    public SettingsWindow(UserPreferences initial,IEnumerable<(ModuleKind Kind,string Name)> types)
    {
        Result=new UserPreferences {Theme=initial.Theme,HiddenModules=initial.HiddenModules.ToList(),TelemetryEnabled=initial.TelemetryEnabled,PasswordSalt=initial.PasswordSalt,PasswordHash=initial.PasswordHash};
        Title="تنظیمات برنامه";Width=760;Height=800;MinWidth=540;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;FlowDirection=FlowDirection.RightToLeft;SetResourceReference(BackgroundProperty,"Surface");
        var root=new DockPanel {Margin=new Thickness(22)}; var top=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        var apply=new Button {Content="اعمال تنظیمات و بستن",Margin=new Thickness(4)}; var cancel=new Button {Content="انصراف",Margin=new Thickness(4),IsCancel=true};top.Children.Add(apply);top.Children.Add(cancel);
        var body=new StackPanel(); root.Children.Add(new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=root;
        void Heading(string text)=>body.Children.Add(new TextBlock {Text=text,FontSize=16,FontWeight=FontWeights.Bold,Margin=new Thickness(0,15,0,8)});
        Heading("ظاهر برنامه"); var names=new[]{"تیره","روشن","آرام — سبز مریم‌گلی","اقیانوس"}; var keys=new[]{"dark","light","calm","ocean"}; var theme=new ComboBox {ItemsSource=names,SelectedIndex=Math.Max(0,Array.IndexOf(keys,initial.Theme)),Margin=new Thickness(0,0,0,8)};body.Children.Add(theme);
        body.Children.Add(new TextBlock {Text="زبان محیط ماژولی: فارسی. تغییر زبان کامل محیط ماژولی در این نسخه آماده نیست.",TextWrapping=TextWrapping.Wrap});
        Heading("رمز ورود برنامه");body.Children.Add(new TextBlock {Text="در صورت تنظیم، هنگام اجرای برنامه رمز خواسته می‌شود. این قفل محلی جایگزین رمزگذاری فایل‌ها نیست.",TextWrapping=TextWrapping.Wrap});
        var current=new PasswordBox {Margin=new Thickness(0,8,0,0),Padding=new Thickness(7),MaxLength=1024}; var pass=new PasswordBox {Margin=new Thickness(0,8,0,0),Padding=new Thickness(7),MaxLength=1024}; var repeat=new PasswordBox {Margin=new Thickness(0,8,0,0),Padding=new Thickness(7),MaxLength=1024};
        if(initial.HasPassword){body.Children.Add(new TextBlock {Text="رمز فعلی",Margin=new Thickness(0,8,0,0)});body.Children.Add(current);}
        body.Children.Add(new TextBlock {Text="رمز جدید (برای حفظ رمز قبلی خالی بگذارید)",Margin=new Thickness(0,8,0,0)});body.Children.Add(pass);body.Children.Add(new TextBlock {Text="تکرار رمز جدید",Margin=new Thickness(0,8,0,0)});body.Children.Add(repeat);
        var remove=new CheckBox {Content="حذف رمز ورود فعلی",Margin=new Thickness(0,12,0,0),IsEnabled=initial.HasPassword};body.Children.Add(remove);
        Heading("ماژول‌های کتابخانه"); body.Children.Add(new TextBlock {Text="ماژول غیرفعال از فهرست افزودن پنهان می‌شود؛ ماژول‌های موجود پروژه حذف یا متوقف نمی‌شوند.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        var checks=new Dictionary<ModuleKind,CheckBox>(); foreach(var item in types) {var check=new CheckBox {Content=item.Name,IsChecked=!initial.HiddenModules.Contains(item.Kind.ToString()),Margin=new Thickness(0,4,0,4)};checks[item.Kind]=check;body.Children.Add(check);}
        Heading("شناسه دستگاه و ثبت برنامه");body.Children.Add(new TextBlock {Text="فعال‌سازی هنوز آماده استفاده نیست. این دکمه صرفاً شناسه هش‌شده پردازنده و دیسک ویندوز را برای بررسی نمایش می‌دهد؛ هیچ اطلاعاتی به سایت ارسال نمی‌شود. با تعویض قطعات شناسه تغییر می‌کند.",TextWrapping=TextWrapping.Wrap});
        var deviceId=new TextBox {IsReadOnly=true,FlowDirection=FlowDirection.LeftToRight,Margin=new Thickness(0,8,0,0),TextWrapping=TextWrapping.Wrap};body.Children.Add(deviceId);
        var getId=new Button {Content="خواندن شناسه دستگاه",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)};body.Children.Add(getId);
        getId.Click+=async (_,_)=>{getId.IsEnabled=false;try {deviceId.Text=await Task.Run(HardwareIdentity.ReadDeviceId);}catch(Exception ex) when(ex is System.Management.ManagementException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or IOException or System.Runtime.InteropServices.COMException){deviceId.Text="";MessageBox.Show(this,"دریافت شناسه سخت‌افزار ممکن نیست.\n"+ex.Message,"شناسه دستگاه",MessageBoxButton.OK,MessageBoxImage.Warning);}finally {getId.IsEnabled=true;}};
        Heading("اتصال به سایت و گزارش اجرا");body.Children.Add(new TextBlock {Text="در صورت فعال‌کردن این گزینه، هنگام هر اجرا شناسه نرم‌افزار و هش دستگاه، نسخه برنامه و ویندوز، مدل پردازنده، ظرفیت RAM و نام دستگاه به mirkhalili.com/app فرستاده می‌شود. سایت IP و زمان دریافت را ثبت می‌کند. سریال خام سخت‌افزار ارسال نمی‌شود. این قابلیت هنوز فعال‌سازی یا غیرفعال‌سازی لایسنس نیست.",TextWrapping=TextWrapping.Wrap});
        var reporting=new CheckBox {Content="با ارسال اطلاعات بالا در هر اجرا موافقم",IsChecked=initial.TelemetryEnabled,Margin=new Thickness(0,10,0,0)};body.Children.Add(reporting);
        var testReport=new Button {Content="ارسال آزمایشی گزارش",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)};body.Children.Add(testReport);
        testReport.Click+=async (_,_)=>{if(reporting.IsChecked!=true){MessageBox.Show(this,"ابتدا رضایت ارسال اطلاعات را انتخاب کنید.");return;}testReport.IsEnabled=false;try {MessageBox.Show(this,await SiteReporting.SendAsync(),"پاسخ سایت");}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or TaskCanceledException or System.Management.ManagementException or InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or System.Runtime.InteropServices.COMException){MessageBox.Show(this,"ارسال به سایت ممکن نیست. سرور باید نصب و پیکربندی شده باشد.\n"+ex.Message,"ارتباط با سایت");}finally {testReport.IsEnabled=true;}};
        apply.Click+=(_,_)=>{
            try
            {
                bool changing=remove.IsChecked==true || pass.Password.Length>0 || repeat.Password.Length>0;
                if(changing && initial.HasPassword && !initial.Verify(current.Password))throw new ArgumentException("رمز فعلی درست نیست.");
                if(remove.IsChecked==true && (pass.Password.Length>0 || repeat.Password.Length>0))throw new ArgumentException("یا رمز را حذف کنید یا رمز جدید تعیین کنید.");
                if(remove.IsChecked==true){Result.PasswordSalt=null;Result.PasswordHash=null;}
                else if(pass.Password.Length>0 || repeat.Password.Length>0){if(pass.Password!=repeat.Password)throw new ArgumentException("تکرار رمز جدید یکسان نیست.");Result.SetPassword(pass.Password);}
                Result.TelemetryEnabled=reporting.IsChecked==true; Result.Theme=keys[theme.SelectedIndex]; Result.HiddenModules=checks.Where(x=>x.Value.IsChecked!=true).Select(x=>x.Key.ToString()).ToList();Result.Save();DialogResult=true;
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException) {MessageBox.Show(this,ex.Message,"خطای تنظیمات",MessageBoxButton.OK,MessageBoxImage.Warning);}
        };
    }
}
