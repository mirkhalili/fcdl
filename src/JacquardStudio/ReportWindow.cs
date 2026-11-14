using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace JacquardStudio;
public sealed class ReportWindow : Window
{
    private readonly IReadOnlyList<BitmapSource> reports;
    private readonly string[] protectedPaths;
    private readonly bool english;
    private readonly string suggestedName;
    public ReportWindow(BitmapSource report, string name, IEnumerable<string> protectedPaths, bool english) : this(new[] { report }, name, protectedPaths, english) { }
    public ReportWindow(IReadOnlyList<BitmapSource> reports, string name, IEnumerable<string> protectedPaths, bool english)
    {
        if (reports.Count == 0) throw new ArgumentException("Report has no pages.");
        this.reports = reports; this.english = english; this.protectedPaths = protectedPaths.ToArray();
        suggestedName = string.IsNullOrWhiteSpace(name) ? "report.pdf" : name + "-report.pdf";
        Title = english ? "Report preview" : "پیش‌نمایش گزارش"; Width = 760; Height = 850; MinWidth = 500; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft; SetResourceReference(BackgroundProperty, "AppBackground");
        var root = new DockPanel { Margin = new Thickness(16) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(actions, Dock.Top);
        var save = new Button { Content = english ? "Save PDF" : "ذخیره PDF", Margin = new Thickness(4) }; save.Click += Save_Click;
        var print = new Button { Content = english ? "Print" : "چاپ", Margin = new Thickness(4) }; print.Click += Print_Click;
        var close = new Button { Content = english ? "Close" : "بستن", Margin = new Thickness(4), IsCancel = true };
        actions.Children.Add(save); actions.Children.Add(print); actions.Children.Add(close); root.Children.Add(actions);
        var pages = new StackPanel();
        foreach (var report in reports) pages.Children.Add(new Image { Source = report, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = pages }); Content = root;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true, OverwritePrompt = true, FileName = suggestedName };
        if (dialog.ShowDialog(this) != true) return;
        try { DesignReport.SavePdf(reports, dialog.FileName, protectedPaths); MessageBox.Show(this, english ? "PDF report saved." : "گزارش PDF ذخیره شد.", Title); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        { MessageBox.Show(this, (english ? "Could not save report: " : "ذخیره گزارش ممکن نشد: ") + ex.Message, Title); }
    }
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new PrintDialog(); if (dialog.ShowDialog() != true) return;
            var document = new FixedDocument(); document.DocumentPaginator.PageSize = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            foreach (var report in reports)
            {
                double scale = Math.Min(dialog.PrintableAreaWidth / report.PixelWidth, dialog.PrintableAreaHeight / report.PixelHeight);
                var page = new FixedPage { Width = dialog.PrintableAreaWidth, Height = dialog.PrintableAreaHeight };
                page.Children.Add(new Image { Source = report, Width = report.PixelWidth * scale, Height = report.PixelHeight * scale });
                var content = new PageContent(); ((IAddChild)content).AddChild(page); document.Pages.Add(content);
            }
            dialog.PrintDocument(document.DocumentPaginator, english ? "Jacquard design report" : "گزارش طرح ژاکارد");
        }
        catch (Exception ex) when (ex is System.Printing.PrintSystemException or IOException or InvalidOperationException or ArgumentException)
        { MessageBox.Show(this, (english ? "Could not print: " : "چاپ ممکن نشد: ") + ex.Message, Title); }
    }
}
