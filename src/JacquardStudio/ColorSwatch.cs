using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JacquardStudio;

/// <summary>Keyboard-accessible color picker without exposing implementation color codes.</summary>
public sealed class ColorSwatch : Button
{
    private string value;
    private readonly Border square;
    public event EventHandler? ColorChanged;
    public string Value
    {
        get => value;
        set { var color = FabricSimulation.ParseColor(value); this.value = value; square.Background = new SolidColorBrush(color); }
    }
    public ColorSwatch(string initial, string label)
    {
        value = initial; square = new Border { Width = 28, Height = 28, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
        Content = square; Width = 42; Height = 42; Padding = new Thickness(3); HorizontalAlignment = HorizontalAlignment.Left; ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(this, label); Value = initial;
        Click += (_, _) => { var owner = Window.GetWindow(this); if (owner is null) return; var selected = ColorChooser.Pick(owner, Value); if (selected is not null && selected != Value) { Value = selected; ColorChanged?.Invoke(this, EventArgs.Empty); } };
    }
}
