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
    private readonly Action<UserPreferences> savePreferences;
    private ECDsa? key;
    private string? device;
    private bool busy;

    private readonly TextBox customerName = new() { MaxLength = 30, Margin = new Thickness(0, 3, 0, 8) };
    private readonly TextBox phone = new() { MaxLength = 30, Margin = new Thickness(0, 3, 0, 8), FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox city = new() { MaxLength = 25, Margin = new Thickness(0, 3, 0, 8) };
    private readonly TextBox identity = new() { IsReadOnly = true, FlowDirection = FlowDirection.LeftToRight, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) };
    private readonly TextBox token = new() { AcceptsReturn = true, FlowDirection = FlowDirection.LeftToRight, TextWrapping = TextWrapping.Wrap, Height = 80, MaxLength = 4096, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 5, 0, 8) };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
    private readonly CheckBox licenseConsent = new() { Content = "با استعلام کمینهٔ مجوز موافقم", Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox telemetryConsent = new() { Content = "گزارش جزئیات اجرا در شروع برنامه فعال باشد", Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox registrationConsent = new() { Content = "با ارسال اطلاعات بالا برای ثبت درخواست آنلاین موافقم", Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button manual = new() { Content = "ثبت کد فعال‌سازی", Margin = new Thickness(4), IsEnabled = false, Padding = new Thickness(12, 8, 12, 8) };
    private readonly Button online = new() { Content = "ثبت درخواست آنلاین", Margin = new Thickness(4), IsEnabled = false, Padding = new Thickness(12, 8, 12, 8) };
    private readonly Button siteCheck = new() { Content = "استعلام مجوز از سایت", Margin = new Thickness(4), IsEnabled = false, Padding = new Thickness(12, 8, 12, 8) };

    public ActivationWindow(UserPreferences prefs) : this(prefs, null) { }

    internal ActivationWindow(UserPreferences prefs, Action<UserPreferences>? preferenceSaver)
    {
        preferences = prefs;
        savePreferences = preferenceSaver ?? (value => value.Save());
        Title = "فعال‌سازی Jacquard Studio";
        Width = 860;
        Height = 790;
        MinWidth = 620;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FlowDirection = FlowDirection.RightToLeft;
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "PrimaryText");
        SetResourceReference(FontFamilyProperty, "VazirFont");
        Icon = new BitmapImage(new Uri("pack://application:,,,/JacquardStudio;component/Assets/app.ico", UriKind.Absolute));
        licenseConsent.IsChecked = preferences.SiteLicenseCheckEnabled;
        telemetryConsent.IsChecked = preferences.TelemetryEnabled;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border { Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
        header.SetResourceReference(Border.BackgroundProperty, "GraphHeader");
        header.SetResourceReference(Border.BorderBrushProperty, "Border");
        var headerGrid = new Grid { FlowDirection = FlowDirection.RightToLeft };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var logo = new Border
        {
            Width = 68,
            Height = 68,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromRgb(0xED,0xF2,0xFF)),
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = StudioIconCatalog.Create(ModuleKind.Weaves,48),
        };
        headerGrid.Children.Add(logo);
        var title = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "فعال‌سازی Jacquard Studio", FontSize = 22, FontWeight = FontWeights.Bold });
        title.Children.Add(new TextBlock { Text = "برای ادامه، مجوز معتبر را از سایت بررسی کنید یا کد فعال‌سازی را وارد کنید.", FontSize = 12, Foreground = BrushResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        Grid.SetColumn(title, 1);
        headerGrid.Children.Add(title);
        header.Child = headerGrid;
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var content = new StackPanel();
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        Border Card(string heading, string description, UIElement body)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = heading, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            if (!string.IsNullOrWhiteSpace(description))
                panel.Children.Add(new TextBlock { Text = description, FontSize = 11, Foreground = BrushResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(body);
            var card = new Border { Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = panel };
            card.SetResourceReference(Border.BackgroundProperty, "GraphSurface");
            card.SetResourceReference(Border.BorderBrushProperty, "Border");
            return card;
        }
        StackPanel Field(string label, TextBox input)
        {
            var field = new StackPanel();
            field.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold });
            field.Children.Add(input);
            return field;
        }

        var devicePanel = new StackPanel();
        var identityRow = new DockPanel { LastChildFill = true };
        var copy = new Button { Content = "کپی شناسه", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 5, 0, 8) };
        DockPanel.SetDock(copy, Dock.Left);
        identityRow.Children.Add(copy);
        identityRow.Children.Add(identity);
        copy.Click += (_, _) => { if (device is not null) Clipboard.SetText(device); };
        devicePanel.Children.Add(identityRow);
        content.Children.Add(Card("شناسه دستگاه", "این شناسه به سخت‌افزار ویندوز وابسته است و برای استعلام یا بررسی کد فعال‌سازی استفاده می‌شود.", devicePanel));

        var licensePanel = new StackPanel();
        licensePanel.Children.Add(new TextBlock
        {
            Text = "با فعال‌کردن استعلام خودکار، شناسه و کلید تصادفی نصب، هش شناسه دستگاه و نسخه برنامه از راه HTTPS فرستاده می‌شوند؛ سایت IP و زمان را ثبت می‌کند. نام، تلفن، شهر و جزئیات سخت‌افزار ارسال نمی‌شوند. خطای اتصال در همین پنجره نمایش داده می‌شود و پنجرهٔ هشدار جداگانه‌ای باز نخواهد شد.",
            TextWrapping = TextWrapping.Wrap,
        });
        licensePanel.Children.Add(licenseConsent);
        content.Children.Add(Card("استعلام مجوز", "برای بررسی مجوز موجود در سایت نیازی به واردکردن اطلاعات تماس نیست.", licensePanel));

        var telemetryPanel = new StackPanel();
        telemetryPanel.Children.Add(new TextBlock
        {
            Text = "این رضایت اختیاری و مستقل از استعلام مجوز و ثبت درخواست آنلاین است. پس از بازشدن موفق برنامه، نام، تلفن و شهر ذخیره‌شده، شناسه نصب، هش دستگاه، نسخه برنامه و ویندوز، مدل پردازنده، ظرفیت RAM و نام رایانه ارسال می‌شوند؛ سایت IP و زمان را نیز ثبت می‌کند. سریال خام سخت‌افزار ارسال نمی‌شود. گزارش خودکار فقط برای نصبی که از پیش در سایت ثبت شده ذخیره می‌شود و دستگاه تازه‌ای ثبت‌نام یا تأیید نمی‌کند. می‌توانید این گزینه را از تنظیمات هم خاموش کنید.",
            TextWrapping = TextWrapping.Wrap,
        });
        telemetryPanel.Children.Add(telemetryConsent);
        content.Children.Add(Card("گزارش اجرا در شروع برنامه", "می‌توانید این رضایت را همین‌جا روشن یا خاموش کنید؛ ارسال خودکار بدون پنجرهٔ هشدار انجام می‌شود.", telemetryPanel));

        var registrationPanel = new StackPanel();
        var contacts = new Grid();
        contacts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contacts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contacts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contacts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contacts.Children.Add(Field("نام و نام خانوادگی", customerName));
        var phoneField = Field("شماره تلفن", phone);
        Grid.SetColumn(phoneField, 1);
        contacts.Children.Add(phoneField);
        var cityField = Field("شهر", city);
        Grid.SetRow(cityField, 1);
        contacts.Children.Add(cityField);
        Grid.SetRow(registrationConsent, 2);
        Grid.SetColumnSpan(registrationConsent, 2);
        contacts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contacts.Children.Add(registrationConsent);
        registrationPanel.Children.Add(contacts);
        registrationPanel.Children.Add(new TextBlock
        {
            Text = "برای ثبت درخواست، فقط پس از تأیید این گزینه و زدن دکمهٔ ثبت، نام، تلفن، شهر، شناسه نصب و هش دستگاه، نسخه برنامه و ویندوز، مدل پردازنده، RAM و نام رایانه به mirkhalili.com/app فرستاده می‌شوند. سایت IP و زمان را نیز ثبت می‌کند. رضایت ثبت‌نام به‌تنهایی گزارش خودکار اجرا را فعال نمی‌کند.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = BrushResource("Muted"),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
        });
        content.Children.Add(Card("ثبت درخواست آنلاین", "نام، تلفن و شهر فقط برای ثبت درخواست جدید لازم‌اند.", registrationPanel));

        var manualPanel = new StackPanel();
        manualPanel.Children.Add(new TextBlock { Text = "کد یا فایل فعال‌سازی را وارد کنید. ثبت کد دستی روی همین دستگاه انجام می‌شود و به‌تنهایی اطلاعاتی به سایت نمی‌فرستد.", TextWrapping = TextWrapping.Wrap });
        manualPanel.Children.Add(token);
        var file = new Button { Content = "انتخاب فایل کد فعال‌سازی", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10, 6, 10, 6) };
        file.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "فایل کد فعال‌سازی|*.txt;*.lic|همه فایل‌ها|*.*", CheckFileExists = true };
            if (picker.ShowDialog(this) != true) return;
            try { token.Text = ActivationStore.ReadTokenFile(picker.FileName); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { message.Text = "خواندن فایل فعال‌سازی ممکن نیست: " + ex.Message; }
        };
        manualPanel.Children.Add(file);
        content.Children.Add(Card("کد فعال‌سازی دستی", "کد معتبر باید برای شناسه همین دستگاه صادر شده باشد.", manualPanel));

        var statusCard = new Border { Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = message };
        statusCard.SetResourceReference(Border.BackgroundProperty, "ListBackground");
        statusCard.SetResourceReference(Border.BorderBrushProperty, "Border");
        content.Children.Add(statusCard);

        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        var exit = new Button { Content = "خروج", Margin = new Thickness(4), Padding = new Thickness(12, 8, 12, 8), IsCancel = true };
        footer.Children.Add(siteCheck);
        footer.Children.Add(online);
        footer.Children.Add(manual);
        footer.Children.Add(exit);
        manual.SetResourceReference(Control.BackgroundProperty, "ActionBackground");
        manual.SetResourceReference(Control.ForegroundProperty, "AccentForeground");

        foreach (var input in new[] { customerName, phone, city, token }) input.TextChanged += (_, _) => RefreshButtons();
        licenseConsent.Unchecked += (_, _) => DisableStoredLicenseCheck();
        telemetryConsent.Checked += TelemetryConsent_Checked;
        telemetryConsent.Unchecked += TelemetryConsent_Unchecked;
        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) => key?.Dispose();
        siteCheck.Click += async (_, _) => await CheckSiteLicenseAsync();
        manual.Click += async (_, _) => await ActivateManualAsync();
        online.Click += async (_, _) => await ActivateOnlineAsync();
    }

    private static SolidColorBrush BrushResource(string key) => (SolidColorBrush)Application.Current.FindResource(key);

    private void RefreshButtons()
    {
        bool ready = device is not null && !busy;
        siteCheck.IsEnabled = ready;
        online.IsEnabled = ready && !string.IsNullOrWhiteSpace(customerName.Text) && !string.IsNullOrWhiteSpace(phone.Text) && !string.IsNullOrWhiteSpace(city.Text);
        manual.IsEnabled = ready && !string.IsNullOrWhiteSpace(token.Text);
    }

    private SiteReporting.Contact Contact() => new(customerName.Text.Trim(), phone.Text.Trim(), city.Text.Trim());

    private void PersistTelemetryConsent(bool enabled)
    {
        if (preferences.TelemetryEnabled == enabled) return;
        bool previous = preferences.TelemetryEnabled;
        preferences.TelemetryEnabled = enabled;
        try
        {
            savePreferences(preferences);
            message.Text = enabled ? "گزارش اجرا برای شروع‌های بعدی فعال شد؛ ارسال بدون پنجرهٔ هشدار انجام می‌شود." : "ارسال خودکار گزارش اجرا غیرفعال شد.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            preferences.TelemetryEnabled = previous;
            telemetryConsent.Checked -= TelemetryConsent_Checked;
            telemetryConsent.Unchecked -= TelemetryConsent_Unchecked;
            telemetryConsent.IsChecked = previous;
            telemetryConsent.Checked += TelemetryConsent_Checked;
            telemetryConsent.Unchecked += TelemetryConsent_Unchecked;
            message.Text = "ذخیرهٔ رضایت ارسال ممکن نشد: " + ex.Message;
        }
    }

    private void TelemetryConsent_Checked(object? sender, RoutedEventArgs e) => PersistTelemetryConsent(true);
    private void TelemetryConsent_Unchecked(object? sender, RoutedEventArgs e) => PersistTelemetryConsent(false);

    private void DisableStoredLicenseCheck()
    {
        if (!preferences.SiteLicenseCheckEnabled) return;
        bool wasPrompted = preferences.SiteLicenseCheckPrompted;
        preferences.SiteLicenseCheckEnabled = false;
        preferences.SiteLicenseCheckPrompted = true;
        try { savePreferences(preferences); message.Text = "استعلام خودکار مجوز در شروع برنامه غیرفعال شد."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            preferences.SiteLicenseCheckEnabled = true;
            preferences.SiteLicenseCheckPrompted = wasPrompted;
            licenseConsent.IsChecked = true;
            message.Text = "ذخیرهٔ تنظیم استعلام ممکن نشد؛ انتخاب قبلی حفظ شد. " + ex.Message;
        }
    }

    private bool LicenseCheckConsent()
    {
        // The broad full-report consent is deliberately not a substitute for this
        // purpose-specific permission, even though both requests can return a lease.
        if (preferences.SiteLicenseCheckEnabled) return true;
        if (licenseConsent.IsChecked != true)
        {
            message.Text = "برای استعلام مجوز، گزینهٔ رضایت استعلام کمینه را در همین پنجره فعال کنید.";
            return false;
        }
        bool wasEnabled = preferences.SiteLicenseCheckEnabled;
        bool wasPrompted = preferences.SiteLicenseCheckPrompted;
        preferences.SiteLicenseCheckEnabled = true;
        preferences.SiteLicenseCheckPrompted = true;
        try { savePreferences(preferences); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            preferences.SiteLicenseCheckEnabled = wasEnabled;
            preferences.SiteLicenseCheckPrompted = wasPrompted;
            licenseConsent.IsChecked = wasEnabled;
            message.Text = "ذخیرهٔ رضایت استعلام ممکن نشد؛ انتخاب قبلی حفظ شد. " + ex.Message;
            return false;
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            busy = true;
            RefreshButtons();
            message.Text = "در حال شناسایی دستگاه…";
            try { key = ActivationStore.PublicKey(); }
            catch (Exception ex) when (ex is CryptographicException or InvalidDataException or ArgumentException) { key = null; }
            device = await Task.Run(HardwareIdentity.ReadDeviceId);
            if (!IsVisible) return;
            identity.Text = device;
            var saved = SiteReporting.LoadContact();
            if (saved is not null) { customerName.Text = saved.CustomerName; phone.Text = saved.Phone; city.Text = saved.City; }
            message.Text = key is null
                ? "این نسخه کلید عمومی صدور ندارد؛ ثبت درخواست ممکن است، ولی فعال‌سازی به نسخه دارای کلید صحیح نیاز دارد."
                : "برای استعلام مجوز از گزینهٔ استعلام استفاده کنید؛ برای درخواست آنلاین، مشخصات و رضایت ارسال را وارد کنید.";

            // License checks remain minimal and purpose-specific. Full execution reporting runs
            // separately after activation succeeds and never substitutes for this lookup.
            if (preferences.SiteLicenseCheckEnabled) await CheckOnlineAsync(false, true);
            if (IsVisible && key is not null && ActivationStore.Valid(key, device, DateTimeOffset.UtcNow)) DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ManagementException or InvalidOperationException or ArgumentException or JsonException or System.Runtime.InteropServices.COMException)
        {
            message.Text = "آماده‌سازی فعال‌سازی ممکن نیست: " + ex.Message;
        }
        finally
        {
            busy = false;
            RefreshButtons();
        }
    }

    private async Task ActivateOnlineAsync()
    {
        if (registrationConsent.IsChecked != true)
        {
            message.Text = "برای ارسال درخواست، ابتدا رضایت ثبت‌نام آنلاین را در همین پنجره فعال کنید.";
            return;
        }
        try
        {
            SiteReporting.SaveContact(Contact());
            await CheckOnlineAsync(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            message.Text = "ثبت اطلاعات ممکن نیست: " + ex.Message;
        }
    }

    private async Task CheckSiteLicenseAsync()
    {
        if (!LicenseCheckConsent()) return;
        await CheckOnlineAsync(true, true);
    }

    private async Task ActivateManualAsync()
    {
        try
        {
            if (key is null) { message.Text = "کلید عمومی صدور در این نسخه وجود ندارد؛ مدیر باید نسخه هماهنگ با کلید خود را بسازد."; return; }
            if (device is null || !ActivationStore.Accept(key, device, token.Text, DateTimeOffset.UtcNow))
            {
                message.Text = ActivationStore.IsRevoked() ? "این شناسه غیرفعال است؛ ابتدا تأیید مجدد آنلاین لازم است." : "کد نامعتبر، منقضی یا متعلق به دستگاه دیگری است.";
                return;
            }
            if (IsVisible && ActivationStore.Valid(key, device, DateTimeOffset.UtcNow)) DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            message.Text = "ذخیره کد ممکن نیست: " + ex.Message;
        }
    }

    private async Task CheckOnlineAsync(bool interactive, bool lookupOnly = false)
    {
        if (device is null) return;
        busy = true;
        RefreshButtons();
        try
        {
            message.Text = "در حال استعلام مجوز از سایت…";
            var saved = lookupOnly ? null : SiteReporting.LoadContact();
            var details = key is null ? null : ActivationStore.Details(key, device, DateTimeOffset.UtcNow);
            SiteReporting.Contact? contact = lookupOnly ? null : (interactive ? Contact() : saved);
            string? manualToken = lookupOnly ? null : details?.ManualToken;
            var response = await SiteReporting.RequestAsync(contact, manualToken, lookupOnly);
            if (!IsVisible) return;
            if (response.Status is "blocked" or "expired")
            {
                ActivationStore.Revoke();
                message.Text = response.Status == "expired" ? "اعتبار مجوز به پایان رسیده است." : "این شناسه توسط مدیر غیرفعال شده است.";
                return;
            }
            if (!lookupOnly && response.Status == "pending" && key is not null && details is { ManualToken: null }) ActivationStore.Clear();
            if (response.Status == "approved")
            {
                if (key is null) { message.Text = "مجوز در سایت ثبت است، اما این نسخه کلید عمومی لازم برای بررسی امضا را ندارد."; return; }
                string? issued = response.Token;
                if (issued is null) { message.Text = "شناسه در سایت تأیید شده، اما سایت مجوز امضاشده صادر نکرده است؛ تنظیم کلید خصوصی سایت را بررسی کنید."; return; }
                if (response.ActivationCode is { } code && ActivationStore.AcceptOnline(key, device, code, issued, DateTimeOffset.UtcNow))
                {
                    message.Text = "فعال‌سازی موفق بود.";
                    if (interactive) DialogResult = true;
                    return;
                }
                ActivationStore.Revoke();
                message.Text = "کد دریافتی در برنامه تأیید نشد؛ امضا، شناسه دستگاه و کلید صادرکننده را بررسی کنید.";
                return;
            }
            message.Text = response.Status switch
            {
                "pending" when lookupOnly => "برای این شناسه مجوز فعال در سایت پیدا نشد. اگر انتظار مجوز دارید شناسه دستگاه را بررسی کنید.",
                "not_licensed" => "برای این شناسه مجوز فعال در سایت پیدا نشد. اگر مجوزی ندارید، مشخصات را وارد و درخواست جدید ثبت کنید.",
                "pending" => "درخواست ثبت شد؛ پس از تأیید و ثبت مجوز توسط مدیر، دوباره استعلام کنید.",
                "upgrade_required" => "برای فعال‌سازی آنلاین، نسخه جدید برنامه را نصب کنید.",
                _ => "پاسخ سایت برای فعال‌سازی معتبر نیست."
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or ManagementException or InvalidOperationException or ArgumentException or CryptographicException or JsonException or System.Runtime.InteropServices.COMException)
        {
            bool validOffline = key is not null && device is not null && ActivationStore.Valid(key, device, DateTimeOffset.UtcNow);
            message.Text = validOffline
                ? "سایت در دسترس نیست؛ مجوز ذخیره‌شده تا پایان مهلت آفلاین قابل استفاده است."
                : "اتصال به سایت برقرار نشد؛ فعال‌سازی آنلاین نیاز به دسترسی اینترنت دارد. می‌توانید کد معتبر را دستی وارد کنید.";
        }
        finally
        {
            busy = false;
            RefreshButtons();
        }
    }
}
