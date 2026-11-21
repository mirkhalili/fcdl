using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using UiMessageBox = Wpf.Ui.Controls.MessageBox;
using UiMessageBoxButton = Wpf.Ui.Controls.MessageBoxButton;
using UiMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using WindowsMessageBoxButton = System.Windows.MessageBoxButton;
using WindowsMessageBoxImage = System.Windows.MessageBoxImage;
using WindowsMessageBoxResult = System.Windows.MessageBoxResult;
using WindowsTextBlock = System.Windows.Controls.TextBlock;

namespace JacquardStudio;

/// <summary>
/// Keeps the application's alerts and confirmations on the same Fluent, theme-aware surface.
/// The overloads intentionally mirror the MessageBox calls used throughout the WPF screens.
/// </summary>
internal static class StudioDialogs
{
    public static WindowsMessageBoxResult Show(string text) => Show(null, text, string.Empty, WindowsMessageBoxButton.OK, WindowsMessageBoxImage.None);

    public static WindowsMessageBoxResult Show(string text, string caption) => Show(null, text, caption, WindowsMessageBoxButton.OK, WindowsMessageBoxImage.None);

    public static WindowsMessageBoxResult Show(string text, string caption, WindowsMessageBoxButton buttons) => Show(null, text, caption, buttons, WindowsMessageBoxImage.None);

    public static WindowsMessageBoxResult Show(string text, string caption, WindowsMessageBoxButton buttons, WindowsMessageBoxImage image) => Show(null, text, caption, buttons, image);

    public static WindowsMessageBoxResult Show(Window? owner, string text) => Show(owner, text, string.Empty, WindowsMessageBoxButton.OK, WindowsMessageBoxImage.None);

    public static WindowsMessageBoxResult Show(Window? owner, string text, string caption) => Show(owner, text, caption, WindowsMessageBoxButton.OK, WindowsMessageBoxImage.None);

    public static WindowsMessageBoxResult Show(Window? owner, string text, string caption, WindowsMessageBoxButton buttons) => Show(owner, text, caption, buttons, WindowsMessageBoxImage.None);

    public static WindowsMessageBoxResult Show(Window? owner, string text, string caption, WindowsMessageBoxButton buttons, WindowsMessageBoxImage image)
    {
        var application = Application.Current;
        if (application is not null && !application.Dispatcher.CheckAccess())
            return application.Dispatcher.Invoke(() => ShowCore(owner, text, caption, buttons, image));

        return ShowCore(owner, text, caption, buttons, image);
    }

    private static WindowsMessageBoxResult ShowCore(Window? requestedOwner, string text, string caption, WindowsMessageBoxButton buttons, WindowsMessageBoxImage image)
    {
        bool rightToLeft = ContainsPersian(text) || ContainsPersian(caption);
        bool persian = rightToLeft;
        var owner = ResolveOwner(requestedOwner);

        var dialog = new UiMessageBox
        {
            Title = string.IsNullOrWhiteSpace(caption) ? (persian ? "ژاکارد استودیو" : "Jacquard Studio") : caption,
            ShowTitle = true,
            FlowDirection = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 360,
            MaxWidth = 560,
            MaxHeight = 520,
            FontSize = 14,
            Padding = new Thickness(14),
            ShowInTaskbar = false
        };
        if (owner is not null)
            dialog.Owner = owner;

        dialog.SetResourceReference(Control.FontFamilyProperty, "VazirFont");
        dialog.SetResourceReference(Window.BackgroundProperty, "Surface");
        dialog.SetResourceReference(Window.ForegroundProperty, "PrimaryText");

        var message = new WindowsTextBlock
        {
            Text = text ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = rightToLeft ? TextAlignment.Right : TextAlignment.Left,
            FlowDirection = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            FontSize = 14,
            LineHeight = 22,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 470
        };
        message.SetResourceReference(WindowsTextBlock.ForegroundProperty, "PrimaryText");

        var symbol = CreateSymbol(image);
        if (symbol is null)
        {
            dialog.Content = message;
        }
        else
        {
            string accentKey = image switch
            {
                WindowsMessageBoxImage.Error => "SystemFillColorCriticalBrush",
                WindowsMessageBoxImage.Warning => "SystemFillColorCautionBrush",
                WindowsMessageBoxImage.Information => "SystemFillColorAttentionBrush",
                _ => "Accent"
            };
            symbol.SetResourceReference(Control.ForegroundProperty, accentKey);
            symbol.VerticalAlignment = VerticalAlignment.Top;
            symbol.Margin = rightToLeft ? new Thickness(14, 2, 0, 0) : new Thickness(0, 2, 14, 0);

            var content = new Grid { FlowDirection = FlowDirection.LeftToRight };
            if (rightToLeft)
            {
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(message, 0);
                Grid.SetColumn(symbol, 1);
            }
            else
            {
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(symbol, 0);
                Grid.SetColumn(message, 1);
            }
            content.Children.Add(symbol);
            content.Children.Add(message);
            dialog.Content = content;
        }

        string ok = persian ? "باشه" : "OK";
        string cancel = persian ? "انصراف" : "Cancel";
        string yes = persian ? "بله" : "Yes";
        string no = persian ? "خیر" : "No";

        switch (buttons)
        {
            case WindowsMessageBoxButton.OK:
                dialog.PrimaryButtonText = ok;
                dialog.IsCloseButtonEnabled = false;
                break;
            case WindowsMessageBoxButton.OKCancel:
                dialog.PrimaryButtonText = ok;
                dialog.CloseButtonText = cancel;
                break;
            case WindowsMessageBoxButton.YesNo:
                dialog.PrimaryButtonText = yes;
                dialog.CloseButtonText = no;
                break;
            case WindowsMessageBoxButton.YesNoCancel:
                dialog.PrimaryButtonText = yes;
                dialog.SecondaryButtonText = no;
                dialog.CloseButtonText = cancel;
                break;
        }

        dialog.PrimaryButtonAppearance = image switch
        {
            WindowsMessageBoxImage.Error => ControlAppearance.Danger,
            WindowsMessageBoxImage.Warning => ControlAppearance.Caution,
            WindowsMessageBoxImage.Information => ControlAppearance.Info,
            _ => ControlAppearance.Primary
        };

        UiMessageBoxResult result = dialog.ShowDialogAsync().GetAwaiter().GetResult();
        return buttons switch
        {
            WindowsMessageBoxButton.OK => result == UiMessageBoxResult.Primary ? WindowsMessageBoxResult.OK : WindowsMessageBoxResult.None,
            WindowsMessageBoxButton.OKCancel => result == UiMessageBoxResult.Primary ? WindowsMessageBoxResult.OK : WindowsMessageBoxResult.Cancel,
            WindowsMessageBoxButton.YesNo => result == UiMessageBoxResult.Primary ? WindowsMessageBoxResult.Yes : WindowsMessageBoxResult.No,
            WindowsMessageBoxButton.YesNoCancel => result switch
            {
                UiMessageBoxResult.Primary => WindowsMessageBoxResult.Yes,
                UiMessageBoxResult.Secondary => WindowsMessageBoxResult.No,
                _ => WindowsMessageBoxResult.Cancel
            },
            _ => WindowsMessageBoxResult.None
        };
    }

    private static SymbolIcon? CreateSymbol(WindowsMessageBoxImage image) => image switch
    {
        WindowsMessageBoxImage.Error => new SymbolIcon(SymbolRegular.ErrorCircle24, 28),
        WindowsMessageBoxImage.Warning => new SymbolIcon(SymbolRegular.Warning24, 28),
        WindowsMessageBoxImage.Information => new SymbolIcon(SymbolRegular.Info24, 28),
        WindowsMessageBoxImage.Question => new SymbolIcon(SymbolRegular.QuestionCircle24, 28),
        _ => null
    };

    private static Window? ResolveOwner(Window? requestedOwner)
    {
        if (requestedOwner is { IsVisible: true })
            return requestedOwner;

        var windows = Application.Current?.Windows.OfType<Window>();
        return windows?.FirstOrDefault(window => window.IsVisible && window.IsActive)
            ?? windows?.FirstOrDefault(window => window.IsVisible);
    }

    private static bool ContainsPersian(string? value) => value?.Any(character => character is >= '\u0600' and <= '\u06FF') == true;
}
