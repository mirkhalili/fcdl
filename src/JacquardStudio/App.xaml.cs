using System.IO;
using System.Windows;

namespace JacquardStudio;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        try
        {
            var prefs=UserPreferences.Load(); ThemeManager.Apply(prefs.Theme,false);
            if(prefs.HasPassword)
            {
                while(true) {var prompt=new PasswordPrompt("ورود به برنامه","رمز ورود برنامه را وارد کنید.") {WindowStartupLocation=WindowStartupLocation.CenterScreen}; if(prompt.ShowDialog()!=true) {Shutdown();return;} if(prefs.Verify(prompt.Password))break; MessageBox.Show("رمز ورود اشتباه است.","ورود",MessageBoxButton.OK,MessageBoxImage.Warning);}
            }
            var window=new ModuleWindow(); MainWindow=window; window.Show(); ShutdownMode=ShutdownMode.OnLastWindowClose;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or FormatException) {MessageBox.Show("خواندن تنظیمات برنامه ممکن نیست. فایل تنظیمات را بررسی کنید.\n"+ex.Message,"خطای تنظیمات",MessageBoxButton.OK,MessageBoxImage.Error); Shutdown();}
    }
}
