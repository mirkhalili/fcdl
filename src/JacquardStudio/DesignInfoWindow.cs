using System.Windows;
using System.Windows.Controls;

namespace JacquardStudio;
public sealed class DesignInfoWindow : Window
{
    public DesignMetadata Result { get; private set; }
    public DesignInfoWindow(DesignMetadata data, bool english)
    {
        Result = data; Title = english ? "Design information" : "اطلاعات طرح"; Width = 500; Height = 430; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft; SetResourceReference(BackgroundProperty, "HeaderBackground");
        var root = new StackPanel { Margin = new Thickness(24) };
        TextBox Add(string label, string value, int max, bool multiline = false)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
            var box = new TextBox { Text = value, MaxLength = max, Padding = new Thickness(8), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, Height = multiline ? 92 : 36, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
            root.Children.Add(box); return box;
        }
        var designer = Add(english ? "Designer" : "طراح", data.Designer, 100);
        var usage = Add(english ? "Use / application" : "کاربرد", data.Usage, 100);
        var description = Add(english ? "Description" : "توضیحات", data.Description, 300, true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        var save = new Button { Content = english ? "Apply" : "ثبت اطلاعات", Margin = new Thickness(4) }; save.Click += (_, _) => { Result = new(designer.Text.Trim(), usage.Text.Trim(), description.Text.Trim()); DialogResult = true; };
        var cancel = new Button { Content = english ? "Cancel" : "انصراف", Margin = new Thickness(4), IsCancel = true }; buttons.Children.Add(save); buttons.Children.Add(cancel); root.Children.Add(buttons); Content = root;
    }
}
