using System.IO;
using System.Windows;

namespace JacquardStudio;
public partial class App : Application
{
    // Component-only WPF tests initialize resources but do not launch the application.
    internal bool SuppressStartupForComponentTests;
    protected override async void OnStartup(StartupEventArgs e)
    {
        if(SuppressStartupForComponentTests)return;
        base.OnStartup(e);
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        StartupSplashWindow? splash=null;
        try
        {
            splash=new StartupSplashWindow(); splash.Show(); await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var prefs=await Task.Run(UserPreferences.Load); ThemeManager.Apply(prefs.Theme,false);
            if(prefs.HasPassword)
            {
                splash.Hide();
                while(true) {var prompt=new PasswordPrompt("ورود به برنامه","رمز ورود برنامه را وارد کنید.") {WindowStartupLocation=WindowStartupLocation.CenterScreen}; if(prompt.ShowDialog()!=true) {Shutdown();return;} if(prefs.Verify(prompt.Password))break; MessageBox.Show("رمز ورود اشتباه است.","ورود",MessageBoxButton.OK,MessageBoxImage.Warning);}
            }
            splash.Hide(); var activation=new ActivationWindow(prefs); if(activation.ShowDialog()!=true) {Shutdown();return;}
            splash.Show(); var window=new ModuleWindow(); MainWindow=window; window.ContentRendered+=(_,_)=>splash.Close(); window.Show(); ShutdownMode=ShutdownMode.OnLastWindowClose;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or FormatException) {splash?.Close();MessageBox.Show("خواندن تنظیمات برنامه ممکن نیست. فایل تنظیمات را بررسی کنید.\n"+ex.Message,"خطای تنظیمات",MessageBoxButton.OK,MessageBoxImage.Error); Shutdown();}
    }
}
