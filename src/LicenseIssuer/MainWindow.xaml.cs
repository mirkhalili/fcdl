using System.IO;
using System.Security.Cryptography;
using System.Windows;
using JacquardLicensing;
using Microsoft.Win32;

namespace JacquardLicenseIssuer;
public partial class MainWindow : Window
{
    public MainWindow(){InitializeComponent();LicenseId.Text=Guid.NewGuid().ToString("D");}
    private void NewId(object sender,RoutedEventArgs e)=>LicenseId.Text=Guid.NewGuid().ToString("D");
    private void BrowseKey(object sender,RoutedEventArgs e){var d=new OpenFileDialog {Filter="PEM private key|*.pem",CheckFileExists=true};if(d.ShowDialog(this)==true)KeyPath.Text=d.FileName;}
    private void GenerateKey(object sender,RoutedEventArgs e)
    {
        var d=new SaveFileDialog {Filter="PEM private key|*.pem",FileName="private-key.pem",AddExtension=true,OverwritePrompt=true};if(d.ShowDialog(this)!=true)return;
        try
        {
            string pub=Path.Combine(Path.GetDirectoryName(d.FileName)!,Path.GetFileNameWithoutExtension(d.FileName)+".public.pem");
            if(File.Exists(d.FileName) || File.Exists(pub)) throw new IOException("برای جلوگیری از بازنویسی کلید، نام یا پوشه دیگری انتخاب کنید.");
            using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);File.WriteAllText(d.FileName,key.ExportPkcs8PrivateKeyPem());
            try {File.WriteAllText(pub,key.ExportSubjectPublicKeyInfoPem());} catch {File.Delete(d.FileName);throw;}
            KeyPath.Text=d.FileName;Status.Text="کلیدها ساخته شدند. کلید خصوصی را خارج از وب‌سایت و در محلی امن و پشتیبان‌گیری‌شده نگه دارید. کلید عمومی باید در نسخه مشتری جاسازی شود.";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException) {MessageBox.Show(this,ex.Message,"خطا");}
    }
    private void SaveToken(object sender,RoutedEventArgs e)
    {
        if(string.IsNullOrWhiteSpace(Token.Text)){MessageBox.Show(this,"ابتدا کد فعال‌سازی بسازید.");return;}
        var dialog=new SaveFileDialog {Filter="Activation code|*.lic;*.txt",FileName="activation.lic",AddExtension=true,DefaultExt=".lic"};if(dialog.ShowDialog(this)!=true)return;
        try{File.WriteAllText(dialog.FileName,Token.Text.Trim(),new System.Text.UTF8Encoding(false));Status.Text="فایل کد ذخیره شد؛ آن را از مسیر امن به کاربر بدهید.";}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){MessageBox.Show(this,ex.Message,"خطا");}
    }
    private void Issue(object sender,RoutedEventArgs e)
    {
        try {if(!Guid.TryParseExact(LicenseId.Text.Trim(),"D",out var id))throw new ArgumentException("شناسه لایسنس معتبر وارد کنید.");using var key=ECDsa.Create();key.ImportFromPem(File.ReadAllText(KeyPath.Text));Token.Text=LicenseToken.IssueAuthorization(key,Device.Text.Trim().ToUpperInvariant(),id,DateTimeOffset.UtcNow,CustomerName.Text);Status.Text="رشته ایجاد شد؛ آن را از مسیر امن به کاربر بدهید.";}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException) {Token.Clear();MessageBox.Show(this,ex.Message,"خطا");}
    }
}
