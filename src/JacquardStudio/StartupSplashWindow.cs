using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>Transient startup presentation. The foreground sweep is cosmetic, not a progress indicator.</summary>
public sealed class StartupSplashWindow : Window
{
    public StartupSplashWindow()
    {
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterScreen;Width=440;Height=500;
        Background=new SolidColorBrush(Color.FromRgb(25,34,46));Icon=new BitmapImage(new Uri("pack://application:,,,/JacquardStudio;component/Assets/app.ico"));
        var root=new Grid {Margin=new Thickness(20)};root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        root.Children.Add(new Image {Source=new BitmapImage(new Uri("pack://application:,,,/JacquardStudio;component/Assets/sam-emblem.png")),Width=340,Height=340,Stretch=Stretch.Uniform});
        var highlight=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(1,0),MappingMode=BrushMappingMode.RelativeToBoundingBox,SpreadMethod=GradientSpreadMethod.Pad};
        highlight.GradientStops.Add(new GradientStop(Color.FromRgb(177,118,59),0));highlight.GradientStops.Add(new GradientStop(Colors.White,.46));highlight.GradientStops.Add(new GradientStop(Color.FromRgb(255,202,132),.53));highlight.GradientStops.Add(new GradientStop(Color.FromRgb(177,118,59),1));
        var translate=new TranslateTransform(-1,0);highlight.RelativeTransform=translate;translate.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-1,1,TimeSpan.FromSeconds(1.8)){RepeatBehavior=RepeatBehavior.Forever});
        var name=new TextBlock {Text="SAM",FlowDirection=FlowDirection.LeftToRight,Foreground=highlight,FontWeight=FontWeights.Black,FontSize=40,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,4)};Grid.SetRow(name,1);root.Children.Add(name);
        var message=new TextBlock {Text="در حال آماده‌سازی استودیو طراحی…",HorizontalAlignment=HorizontalAlignment.Center,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,0,0,20)};Grid.SetRow(message,2);root.Children.Add(message);Content=root;
    }
}
