using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JacquardStudio;

public sealed class SettingsWindow : Window
{
    public UserPreferences Result { get; }

    public SettingsWindow(UserPreferences initial, IEnumerable<(ModuleKind Kind, string Name)> types)
    {
        Result = new UserPreferences
        {
            Theme = initial.Theme,
            HiddenModules = initial.HiddenModules.ToList(),
            TelemetryEnabled = initial.TelemetryEnabled,
            SiteLicenseCheckEnabled = initial.SiteLicenseCheckEnabled,
            SiteLicenseCheckPrompted = initial.SiteLicenseCheckPrompted,
            PasswordSalt = initial.PasswordSalt,
            PasswordHash = initial.PasswordHash,
        };

        Title = "تنظیمات برنامه";
        Width = 860;
        Height = 720;
        MinWidth = 600;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "PrimaryText");

        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new Border { Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 12), BorderThickness = new Thickness(1) };
        header.SetResourceReference(Border.BackgroundProperty, "GraphHeader");
        header.SetResourceReference(Border.BorderBrushProperty, "Border");
        header.Child = new StackPanel();
        ((StackPanel)header.Child).Children.Add(new TextBlock { Text = "تنظیمات Jacquard Studio", FontSize = 20, FontWeight = FontWeights.Bold });
        ((StackPanel)header.Child).Children.Add(new TextBlock { Text = "ظاهر، کتابخانهٔ ماژول‌ها، امنیت ورود، گزارش اجرا و استعلام مجوز را جداگانه مدیریت کنید.", Foreground = BrushResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        var apply = new Button { Content = "ذخیره و بستن", Margin = new Thickness(4), Padding = new Thickness(16, 9, 16, 9), IsDefault = true };
        apply.SetResourceReference(Control.BackgroundProperty, "ActionBackground");
        apply.SetResourceReference(Control.ForegroundProperty, "AccentForeground");
        var cancel = new Button { Content = "انصراف", Margin = new Thickness(4), Padding = new Thickness(16, 9, 16, 9), IsCancel = true };
        footer.Children.Add(apply);
        footer.Children.Add(cancel);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var tabs = new TabControl { Margin = new Thickness(0, 0, 0, 0), FlowDirection = FlowDirection.RightToLeft };
        root.Children.Add(tabs);
        Content = root;

        static SolidColorBrush BrushResource(string key) => (SolidColorBrush)Application.Current.FindResource(key);
        Border Section(string title, string description, UIElement content)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
            if (!string.IsNullOrWhiteSpace(description))
                body.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = BrushResource("Muted"), Margin = new Thickness(0, 0, 0, 10) });
            body.Children.Add(content);
            var card = new Border { Padding = new Thickness(16), Margin = new Thickness(2, 4, 2, 10), BorderThickness = new Thickness(1), Child = body };
            card.SetResourceReference(Border.BackgroundProperty, "GraphSurface");
            card.SetResourceReference(Border.BorderBrushProperty, "Border");
            return card;
        }
        StackPanel Page(string title, string description)
        {
            var page = new StackPanel { Margin = new Thickness(8) };
            page.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 4, 4, 2) });
            page.Children.Add(new TextBlock { Text = description, Foreground = BrushResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 12) });
            return page;
        }
        void AddTab(string title, StackPanel page)
        {
            var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
            tabs.Items.Add(new TabItem { Header = title, Content = scroll, Padding = new Thickness(12, 6, 12, 6) });
        }

        var appearancePage = Page("ظاهر برنامه", "تم دلخواه را انتخاب کنید؛ انتخاب شما در اجراهای بعدی نیز حفظ می‌شود.");
        var names = new[] { "تیره", "روشن", "آرام — سبز مریم‌گلی", "اقیانوس" };
        var keys = new[] { "dark", "light", "calm", "ocean" };
        var theme = new ComboBox
        {
            ItemsSource = names,
            SelectedIndex = Math.Max(0, Array.IndexOf(keys, initial.Theme)),
            MinWidth = 250,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 4, 0, 2),
        };
        appearancePage.Children.Add(Section("تم و رنگ‌بندی", "تغییر تم روی پنجره‌ها و فضای گراف اعمال می‌شود.", theme));
        appearancePage.Children.Add(Section("زبان", "زبان محیط ماژولی در این نسخه فارسی است؛ ترجمهٔ کامل انگلیسی هنوز آماده نیست.", new TextBlock { Text = "فارسی", FontWeight = FontWeights.SemiBold }));
        AddTab("ظاهر", appearancePage);

        var modulesPage = Page("مدیریت ماژول‌ها", "ماژول غیرفعال از فهرست افزودن پنهان می‌شود؛ ماژول‌های موجود در پروژه حذف یا متوقف نمی‌شوند.");
        var moduleList = new StackPanel();
        var checks = new Dictionary<ModuleKind, CheckBox>();
        foreach (var item in types)
        {
            var check = new CheckBox
            {
                Content = item.Name,
                IsChecked = !initial.HiddenModules.Contains(item.Kind.ToString()),
                Margin = new Thickness(4, 5, 4, 5),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            System.Windows.Automation.AutomationProperties.SetName(check, "فعال‌سازی ماژول " + item.Name);
            checks[item.Kind] = check;
            var row = new Border { Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(2, 2, 2, 2), BorderThickness = new Thickness(1), Child = check };
            row.SetResourceReference(Border.BackgroundProperty, "ListBackground");
            row.SetResourceReference(Border.BorderBrushProperty, "Border");
            moduleList.Children.Add(row);
        }
        modulesPage.Children.Add(Section("فهرست افزودن", "برای کاهش شلوغی می‌توانید فقط ماژول‌های موردنیاز را در کتابخانه نگه دارید.", moduleList));
        AddTab("ماژول‌ها", modulesPage);

        var securityPage = Page("ورود و مجوز", "مدیریت قفل محلی، مشاهدهٔ مشخصات فعال‌سازی و انتخاب استعلام کمینهٔ مجوز.");
        var passwordContent = new StackPanel();
        passwordContent.Children.Add(new TextBlock { Text = "در صورت تنظیم، هنگام اجرای برنامه رمز خواسته می‌شود. این قفل محلی جایگزین رمزگذاری فایل‌ها نیست.", TextWrapping = TextWrapping.Wrap, Foreground = BrushResource("Muted") });
        var current = new PasswordBox { Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(7), MaxLength = 1024 };
        var pass = new PasswordBox { Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(7), MaxLength = 1024 };
        var repeat = new PasswordBox { Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(7), MaxLength = 1024 };
        if (initial.HasPassword)
        {
            passwordContent.Children.Add(new TextBlock { Text = "رمز فعلی", Margin = new Thickness(0, 8, 0, 0) });
            passwordContent.Children.Add(current);
        }
        passwordContent.Children.Add(new TextBlock { Text = "رمز جدید (برای حفظ رمز قبلی خالی بگذارید)", Margin = new Thickness(0, 8, 0, 0) });
        passwordContent.Children.Add(pass);
        passwordContent.Children.Add(new TextBlock { Text = "تکرار رمز جدید", Margin = new Thickness(0, 8, 0, 0) });
        passwordContent.Children.Add(repeat);
        var remove = new CheckBox { Content = "حذف رمز ورود فعلی", Margin = new Thickness(0, 12, 0, 0), IsEnabled = initial.HasPassword };
        passwordContent.Children.Add(remove);
        securityPage.Children.Add(Section("رمز ورود برنامه", "رمز باید دست‌کم ۸ نویسه باشد.", passwordContent));

        var deviceContent = new StackPanel();
        deviceContent.Children.Add(new TextBlock { Text = "شناسه دستگاه به پردازنده و دیسک ویندوز وابسته است؛ با تعویض قطعات ممکن است تغییر کند.", TextWrapping = TextWrapping.Wrap, Foreground = BrushResource("Muted") });
        var deviceId = new TextBox { IsReadOnly = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        deviceContent.Children.Add(deviceId);
        var getId = new Button { Content = "خواندن شناسه دستگاه", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 6, 10, 6) };
        deviceContent.Children.Add(getId);
        getId.Click += async (_, _) =>
        {
            getId.IsEnabled = false;
            try { deviceId.Text = await Task.Run(HardwareIdentity.ReadDeviceId); }
            catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or IOException or System.Runtime.InteropServices.COMException)
            {
                deviceId.Text = "";
                MessageBox.Show(this, "دریافت شناسه سخت‌افزار ممکن نیست.\n" + ex.Message, "شناسه دستگاه", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { getId.IsEnabled = true; }
        };
        securityPage.Children.Add(Section("شناسه دستگاه", "این شناسه فقط برای نمایش و پشتیبانی محلی است.", deviceContent));

        var licenseContent = new StackPanel();
        var licenseSummary = new TextBlock { Text = "در حال خواندن مشخصات مجوز…", TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft };
        licenseContent.Children.Add(licenseSummary);
        var licenseInfo = new Button { Content = "به‌روزرسانی مشخصات فعال‌سازی", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 6, 10, 6) };
        licenseContent.Children.Add(licenseInfo);
        async Task RefreshLicenseInfoAsync()
        {
            licenseInfo.IsEnabled = false;
            try
            {
                using var key = ActivationStore.PublicKey();
                if (key is null) throw new InvalidOperationException("کلید عمومی در این نسخه موجود نیست.");
                string device = await Task.Run(HardwareIdentity.ReadDeviceId);
                var details = ActivationStore.Details(key, device, DateTimeOffset.UtcNow);
                if (details is null)
                {
                    licenseSummary.Text = "مجوز معتبر ذخیره نشده است.\nشناسه دستگاه: " + device;
                    return;
                }
                var profile = SiteReporting.LoadContact();
                string name = string.IsNullOrWhiteSpace(details.Value.Claims.CustomerName) ? profile?.CustomerName ?? "—" : details.Value.Claims.CustomerName;
                string offline = DateTimeOffset.FromUnixTimeSeconds(details.Value.OfflineUntil).ToLocalTime().ToString("yyyy/MM/dd HH:mm");
                string expiry = details.Value.LicenseUntil is long end ? DateTimeOffset.FromUnixTimeSeconds(end).ToLocalTime().ToString("yyyy/MM/dd HH:mm") : "تعیین نشده / در انتظار استعلام سایت";
                licenseSummary.Text = $"نام صاحب مجوز: {name}\nشناسه دستگاه: {device}\nپایان اعتبار اشتراک: {expiry}\nمهلت استفاده آفلاین: {offline} (زمان محلی)";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Management.ManagementException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or System.Runtime.InteropServices.COMException)
            {
                licenseSummary.Text = "مشخصات مجوز در دسترس نیست: " + ex.Message;
            }
            finally { licenseInfo.IsEnabled = true; }
        }
        Loaded += async (_, _) => await RefreshLicenseInfoAsync();
        licenseInfo.Click += async (_, _) => await RefreshLicenseInfoAsync();

        var licenseCheck = new CheckBox
        {
            Content = "استعلام کمینهٔ مجوز در شروع برنامه",
            IsChecked = initial.SiteLicenseCheckEnabled,
            Margin = new Thickness(0, 12, 0, 0),
        };
        licenseContent.Children.Add(new TextBlock
        {
            Text = "در استعلام کمینه، شناسه و کلید تصادفی نصب، هش شناسه دستگاه و نسخه برنامه از راه HTTPS فرستاده می‌شوند؛ سایت IP و زمان درخواست را ثبت می‌کند. نام، تلفن، شهر و مشخصات سیستم ارسال نمی‌شوند. در صورت غیرفعال‌بودن این گزینه، استعلام خودکار انجام نمی‌شود.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = BrushResource("Muted"),
            Margin = new Thickness(0, 8, 0, 0),
        });
        licenseContent.Children.Add(licenseCheck);
        securityPage.Children.Add(Section("استعلام مجوز آنلاین", "استعلام فقط برای بررسی مجوز دستگاه است و با ارسال درخواست ثبت‌نام تفاوت دارد.", licenseContent));

        var reportContent = new StackPanel();
        reportContent.Children.Add(new TextBlock
        {
            Text = "با رضایت جداگانه، پس از بازشدن موفق برنامه یک گزارش شامل شناسه نصب، هش دستگاه، نسخه برنامه و ویندوز، نام رایانه، پردازنده، RAM و اطلاعات تماس ذخیره‌شده ارسال می‌شود؛ سایت IP و زمان را نیز ثبت می‌کند. گزارش خودکار فقط برای نصب ثبت‌شده در سایت ذخیره می‌شود و دستگاه تازه‌ای ثبت‌نام یا تأیید نمی‌کند.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = BrushResource("Muted"),
        });
        var telemetryCheck = new CheckBox
        {
            Content = "گزارش کامل اجرا پس از شروع برنامه فعال باشد",
            IsChecked = initial.TelemetryEnabled,
            Margin = new Thickness(0, 12, 0, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(telemetryCheck, "گزارش کامل اجرا پس از شروع برنامه");
        reportContent.Children.Add(telemetryCheck);
        securityPage.Children.Add(Section("گزارش کامل اجرا", "این گزینه مستقل از استعلام کمینه و ثبت درخواست آنلاین است و هر زمان می‌توان آن را خاموش کرد.", reportContent));
        AddTab("امنیت و مجوز", securityPage);

        apply.Click += (_, _) =>
        {
            try
            {
                bool changing = remove.IsChecked == true || pass.Password.Length > 0 || repeat.Password.Length > 0;
                if (changing && initial.HasPassword && !initial.Verify(current.Password)) throw new ArgumentException("رمز فعلی درست نیست.");
                if (remove.IsChecked == true && (pass.Password.Length > 0 || repeat.Password.Length > 0)) throw new ArgumentException("یا رمز را حذف کنید یا رمز جدید تعیین کنید.");
                if (remove.IsChecked == true)
                {
                    Result.PasswordSalt = null;
                    Result.PasswordHash = null;
                }
                else if (pass.Password.Length > 0 || repeat.Password.Length > 0)
                {
                    if (pass.Password != repeat.Password) throw new ArgumentException("تکرار رمز جدید یکسان نیست.");
                    Result.SetPassword(pass.Password);
                }
                Result.SiteLicenseCheckEnabled = licenseCheck.IsChecked == true;
                Result.SiteLicenseCheckPrompted = true;
                Result.TelemetryEnabled = telemetryCheck.IsChecked == true;
                Result.Theme = keys[theme.SelectedIndex];
                Result.HiddenModules = checks.Where(x => x.Value.IsChecked != true).Select(x => x.Key.ToString()).ToList();
                Result.Save();
                DialogResult = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                MessageBox.Show(this, ex.Message, "خطای تنظیمات", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }
}
