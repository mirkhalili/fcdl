using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JacquardStudio;
/// <summary>Explicit destination choice; closing or Cancel never merges colors.</summary>
public sealed class MergeChoiceWindow : Window
{
    public int? Destination { get; private set; }
    public MergeChoiceWindow(PaletteEntry first, PaletteEntry second, bool english)
    {
        Title = english ? "Choose the color to keep" : "انتخاب رنگ باقی‌مانده";
        Width = 600; Height = 205; MinWidth = 520; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        SetResourceReference(BackgroundProperty, "HeaderBackground");
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = english ? "Choose the destination color. The other color will be merged into it." : "رنگ مقصد را انتخاب کنید؛ رنگ دیگر در آن ادغام می‌شود.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var entry in new[] { first, second })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = english ? "Merge into " : "ادغام به ", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new Border { Width = 22, Height = 22, Background = entry.Brush, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(5, 0, 5, 0) });
            row.Children.Add(new TextBlock { Text = (english ? "Color " : "رنگ ") + entry.ColorNumber, FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = row, Margin = new Thickness(4), Padding = new Thickness(10, 8, 10, 8) };
            button.Click += (_, _) => { Destination = entry.Index; DialogResult = true; };
            actions.Children.Add(button);
        }
        var cancel = new Button { Content = english ? "Cancel" : "انصراف", Margin = new Thickness(4), IsCancel = true };
        actions.Children.Add(cancel); root.Children.Add(actions); Content = root;
    }
}
