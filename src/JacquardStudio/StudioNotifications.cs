using System.Runtime.CompilerServices;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace JacquardStudio;

/// <summary>Routes brief, non-blocking confirmations to the active workspace window.</summary>
internal static class StudioNotifications
{
    private static readonly ConditionalWeakTable<SnackbarPresenter, SnackbarService> Services = new();

    public static void Show(
        Window? owner,
        string title,
        string message,
        ControlAppearance appearance = ControlAppearance.Success,
        TimeSpan timeout = default)
    {
        var application = Application.Current;
        if (application is null)
            return;

        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.BeginInvoke(new Action(() => ShowCore(application, owner, title, message, appearance, timeout)));
            return;
        }

        ShowCore(application, owner, title, message, appearance, timeout);
    }

    private static void ShowCore(Application application, Window? owner, string title, string message, ControlAppearance appearance, TimeSpan timeout)
    {
        SnackbarPresenter? presenter = owner is { IsVisible: true } ? FindPresenter(owner) : null;
        if (presenter is null || !presenter.IsLoaded)
        {
            SnackbarPresenter? visiblePresenter = null;
            foreach (Window window in application.Windows.OfType<Window>())
            {
                SnackbarPresenter? candidate = FindPresenter(window);
                if (!window.IsVisible || candidate is null || !candidate.IsLoaded)
                    continue;

                if (window.IsActive)
                {
                    presenter = candidate;
                    break;
                }

                visiblePresenter ??= candidate;
            }

            presenter ??= visiblePresenter;
        }

        if (presenter is null || !presenter.IsLoaded)
            return;

        var service = Services.GetValue(presenter, target =>
        {
            var snackbarService = new SnackbarService();
            snackbarService.SetSnackbarPresenter(target);
            return snackbarService;
        });
        service.Show(title, message, appearance, null, timeout == default ? TimeSpan.FromSeconds(4) : timeout);
        if (presenter.Content is { } snackbar)
            snackbar.FlowDirection = ContainsPersian(title) || ContainsPersian(message) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private static SnackbarPresenter? FindPresenter(Window? window) =>
        window?.FindName("SnackbarPresenter") as SnackbarPresenter;

    private static bool ContainsPersian(string value) => value.Any(character => character is >= '\u0600' and <= '\u06FF');
}
