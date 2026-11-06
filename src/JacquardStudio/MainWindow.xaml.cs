using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace JacquardStudio;

public partial class MainWindow : Window
{
    private bool english;
    private string? path;
    private ImageResult? current;
    private double zoom = 1;
    private int requestId;

    public MainWindow() { InitializeComponent(); UpdateLabels(); }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "BMP and TIFF|*.bmp;*.tif;*.tiff|All files|*.*",
            CheckFileExists = true, Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) _ = LoadAsync(dialog.FileName, 0);
    }

    private async Task LoadAsync(string filename, int page)
    {
        int request = ++requestId;
        StatusText.Text = english ? "Loading image…" : "در حال باز کردن تصویر…";
        try
        {
            var result = await Task.Run(() => ImageLoader.Load(filename, page));
            if (request != requestId) return;
            path = filename;
            current = result;
            zoom = 1;
            PreviewImage.Source = result.Image;
            PreviewImage.LayoutTransform = new ScaleTransform(zoom, zoom);
            ImageScroll.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
            FileValue.Text = Path.GetFileName(filename);
            DimensionsValue.Text = $"{result.Image.PixelWidth:N0} × {result.Image.PixelHeight:N0} px";
            FormatValue.Text = $"{result.Format} · {result.Image.Format.BitsPerPixel} bpp";
            UpdatePage();
            PrepareButton.IsEnabled = true;
            StatusText.Text = english ? "Image opened. Preview only." : "تصویر باز شد؛ فقط پیش‌نمایش.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (request != requestId) return;
            StatusText.Text = english ? "Could not open image." : "باز کردن تصویر ناموفق بود.";
            MessageBox.Show(this, (english ? "Could not open the image. Check that it is a supported BMP or TIFF.\n\n" : "باز کردن تصویر ممکن نشد. از سالم بودن فایل BMP یا TIFF و محدودیت اندازه اطمینان حاصل کنید.\n\n") + ex.Message,
                english ? "Image error" : "خطای تصویر", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdatePage()
    {
        PageValue.Text = current is null ? "—" : $"{current.PageIndex + 1} / {current.PageCount}";
        PreviousPage.IsEnabled = current is not null && current.PageIndex > 0;
        NextPage.IsEnabled = current is not null && current.PageIndex + 1 < current.PageCount;
        ZoomText.Text = $"{zoom * 100:0}%";
    }
    private void PreviousPage_Click(object sender, RoutedEventArgs e) { if (path is not null && current is not null && current.PageIndex > 0) _ = LoadAsync(path, current.PageIndex - 1); }
    private void NextPage_Click(object sender, RoutedEventArgs e) { if (path is not null && current is not null && current.PageIndex + 1 < current.PageCount) _ = LoadAsync(path, current.PageIndex + 1); }
    private void SetZoom(double value) { zoom = Math.Clamp(value, 0.125, 8); PreviewImage.LayoutTransform = new ScaleTransform(zoom, zoom); UpdatePage(); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(zoom * 2);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(zoom / 2);
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(1);
    private void Window_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Window_Drop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files) _ = LoadAsync(files[0], 0); }
    private void Prepare_Click(object sender, RoutedEventArgs e)
    {
        if (current is null || path is null) return;
        new EditorWindow(current.Image, path, english) { Owner = this }.ShowDialog();
    }
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow(english) { Owner = this }.ShowDialog();
    private void Language_Click(object sender, RoutedEventArgs e) { english = !english; UpdateLabels(); }

    private void UpdateLabels()
    {
        FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        LanguageButton.Content = english ? "فارسی" : "English";
        AboutButton.Content = english ? "About" : "درباره";
        PrepareButton.Content = english ? "Size, colors & patterns →" : "ابعاد، رنگ و پترن  ←";
        SubtitleText.Text = english ? "Jacquard design workspace" : "فضای کار طراحی و بازرسی ژاکارد";
        BadgeText.Text = english ? "EXPERIMENTAL" : "نسخه آزمایشی";
        WorkspaceTitle.Text = english ? "WORKSPACE" : "فضای کاری";
        WorkspaceHeading.Text = english ? "Get started" : "شروع پروژه";
        OpenButton.Content = english ? "+  Open image" : "+  باز کردن تصویر";
        OpenHint.Text = english ? "Or drop a BMP / TIFF file here" : "یا فایل BMP / TIFF را اینجا رها کنید";
        WorkflowTitle.Text = english ? "Workflow" : "مراحل کار";
        StepOne.Text = english ? "01   Open & preview" : "۱   باز کردن و پیش‌نمایش";
        StepTwo.Text = english ? "02   Size, colors & patterns" : "۲   ابعاد، رنگ و پترن";
        StepThree.Text = english ? "03   Inspect & save image" : "۳   بررسی و ذخیره تصویر";
        CanvasTitle.Text = english ? "Design preview" : "پیش‌نمایش طرح";
        CanvasSubtitle.Text = english ? "Inspect the image before preparation" : "بررسی دقیق تصویر پیش از آماده‌سازی";
        EmptyTitle.Text = english ? "Bring in your design" : "طرح خود را وارد کنید";
        EmptyHint.Text = english ? "Choose a BMP or TIFF image to inspect it in detail." : "یک تصویر BMP یا TIFF انتخاب کنید تا آن را با جزئیات بررسی کنید.";
        EmptyButton.Content = english ? "Choose image" : "انتخاب تصویر";
        SafetyText.Text = english ? "Loom conversion is not available yet. Do not use this preview for production." : "تبدیل به فایل ماشین هنوز فعال نیست. از این نسخه برای تولید استفاده نکنید.";
        InfoCaption.Text = english ? "FILE DETAILS" : "جزئیات فایل";
        InfoTitle.Text = english ? "Image information" : "اطلاعات تصویر";
        FileLabel.Text = english ? "File name" : "نام فایل";
        DimensionsLabel.Text = english ? "Dimensions" : "ابعاد";
        FormatLabel.Text = english ? "Image format" : "نوع تصویر";
        PageLabel.Text = english ? "Image page" : "صفحه تصویر";
        NoticeTitle.Text = english ? "Support status" : "وضعیت پشتیبانی";
        NoticeText.Text = english ? "Reading and writing loom files has not been implemented in this stage." : "خواندن و نوشتن فایل دستگاه در این مرحله پیاده‌سازی نشده است.";
        StatusText.Text = current is null ? (english ? "Ready to open an image" : "آماده برای باز کردن تصویر") : (english ? "Image opened. Preview only." : "تصویر باز شد؛ فقط پیش‌نمایش.");
    }
}
