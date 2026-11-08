using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JacquardStudio;
/// <summary>Explicit destination choice; closing or Cancel never merges colors.</summary>
public sealed class MergeChoiceWindow : Window
{
    public int? Destination { get; private set; }
    public MergeChoiceWindow(IReadOnlyList<PaletteEntry> entries, bool english)
    {
        Title = english ? "Choose the color to keep" : "انتخاب رنگ باقی‌مانده";
        Width = 440; Height = 480; MinWidth = 350; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        SetResourceReference(BackgroundProperty, "HeaderBackground");
        var root = new DockPanel { Margin = new Thickness(20) };
        var intro = new TextBlock { Text = english ? "Choose the destination. All other selected colors will be merged into it." : "رنگ مقصد را انتخاب کنید؛ سایر رنگ‌های انتخابی در آن ادغام می‌شوند.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(intro, Dock.Top); root.Children.Add(intro);
        var cancel = new Button { Content = english ? "Cancel" : "انصراف", Margin = new Thickness(4), IsCancel = true }; DockPanel.SetDock(cancel, Dock.Bottom); root.Children.Add(cancel);
        var actions = new StackPanel();
        foreach (var entry in entries)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border { Width = 22, Height = 22, Background = entry.Brush, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(5, 0, 10, 0) });
            row.Children.Add(new TextBlock { Text = $"{entry.ColorNumber} · {entry.ColorCode}" + (entry.IsLocked ? " 🔒" : ""), FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = row, Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(10, 5, 10, 5) };
            button.Click += (_, _) => { Destination = entry.Index; DialogResult = true; }; actions.Children.Add(button);
        }
        root.Children.Add(new ScrollViewer { Content = actions, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
    }
}
