using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace JacquardStudio;

internal sealed record UpdateProgressState(string Stage, string Detail, double? Fraction);

/// <summary>Visible, cancellable status window for downloading and verifying a release.</summary>
internal sealed class UpdateProgressWindow : Window
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly TextBlock stageText;
    private readonly TextBlock detailText;
    private readonly TextBlock percentageText;
    private readonly ProgressBar progressBar;
    private readonly Button cancelButton;
    private bool finished;

    public CancellationToken CancellationToken => cancellation.Token;
    public IProgress<UpdateProgressState> Reporter { get; }

    public UpdateProgressWindow(Window owner)
    {
        Owner = owner;
        Title = "به‌روزرسانی Jacquard Studio";
        Width = 490;
        Height = 230;
        MinWidth = 430;
        MaxWidth = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FlowDirection = FlowDirection.RightToLeft;
        Background = FindBrush("Surface", Brushes.White);
        Foreground = FindBrush("PrimaryText", Brushes.Black);

        stageText = new TextBlock { Text = "نسخهٔ تازه شناسایی شد", FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        detailText = new TextBlock { Text = "برای دریافت، اندازه و چک‌سام فایل بررسی می‌شوند.", Margin = new Thickness(0, 7, 0, 12), Foreground = FindBrush("Muted", Brushes.Gray), TextWrapping = TextWrapping.Wrap };
        percentageText = new TextBlock { Text = "", MinWidth = 45, TextAlignment = TextAlignment.Left, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
        progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 16, IsIndeterminate = true, Margin = new Thickness(0, 3, 8, 3), Foreground = FindBrush("Accent", Brushes.SteelBlue) };
        AutomationProperties.SetName(progressBar, "پیشرفت دریافت و راستی‌آزمایی به‌روزرسانی");
        var progressRow = new Grid { FlowDirection = FlowDirection.LeftToRight };
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progressRow.Children.Add(progressBar);
        Grid.SetColumn(percentageText, 1); progressRow.Children.Add(percentageText);

        cancelButton = new Button { Content = "لغو دریافت", MinWidth = 110, MinHeight = 34, Padding = new Thickness(14, 5, 14, 5), HorizontalAlignment = HorizontalAlignment.Left };
        cancelButton.Click += (_, _) =>
        {
            if (finished) { Close(); return; }
            if (!cancellation.IsCancellationRequested) cancellation.Cancel();
            cancelButton.IsEnabled = false;
            stageText.Text = "در حال لغو درخواست…";
            detailText.Text = "درخواست دریافت لغو شد؛ فایل موقت پاک‌سازی می‌شود.";
            progressBar.IsIndeterminate = true;
            percentageText.Text = "";
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            cancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
        };

        var body = new StackPanel { Margin = new Thickness(22) };
        body.Children.Add(stageText);
        body.Children.Add(detailText);
        body.Children.Add(progressRow);
        body.Children.Add(cancelButton);
        Content = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = FindBrush("Border", Brushes.LightGray),
            Child = body
        };

        Reporter = new Progress<UpdateProgressState>(ApplyProgress);
        Closing += (_, _) =>
        {
            if (!finished && !cancellation.IsCancellationRequested) cancellation.Cancel();
        };
    }

    public void ShowCompleted()
    {
        finished = true;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ShowCompleted)); return; }
        ApplyProgress(new UpdateProgressState("نصب‌کننده راه‌اندازی شد", "برای تکمیل به‌روزرسانی، مراحل نصب را دنبال کنید.", 1));
        cancelButton.IsEnabled = false;
        if (IsVisible) Close();
    }

    public void ShowCancelled()
    {
        finished = true;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ShowCancelled)); return; }
        ApplyProgress(new UpdateProgressState("دریافت لغو شد", "فایل موقت پاک شد و نصب‌کننده اجرا نشد.", null));
        cancelButton.Content = "بستن";
        cancelButton.IsEnabled = true;
    }

    public void ShowFailure(string message)
    {
        finished = true;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<string>(ShowFailure), message); return; }
        ApplyProgress(new UpdateProgressState("به‌روزرسانی انجام نشد", message, null));
        cancelButton.Content = "بستن";
        cancelButton.IsEnabled = true;
    }

    private void ApplyProgress(UpdateProgressState state)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<UpdateProgressState>(ApplyProgress), state); return; }
        stageText.Text = state.Stage;
        detailText.Text = state.Detail;
        if (state.Fraction is double fraction)
        {
            fraction = Math.Clamp(fraction, 0, 1);
            progressBar.IsIndeterminate = false;
            progressBar.Value = fraction * 100;
            percentageText.Text = $"{fraction * 100:0}%";
        }
        else
        {
            progressBar.IsIndeterminate = true;
            percentageText.Text = "";
        }
    }

    private static Brush FindBrush(string resource, Brush fallback) => Application.Current?.TryFindResource(resource) as Brush ?? fallback;
}
