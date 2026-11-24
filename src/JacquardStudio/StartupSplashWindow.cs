using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>Vazir reference-design startup splash. Its moving accent is decorative, not a progress indicator.</summary>
public sealed class StartupSplashWindow : Window
{
    public StartupSplashWindow()
    {
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterScreen;Width=420;Height=360;
        Background=new SolidColorBrush(Color.FromRgb(0xF4,0xF6,0xFA));
        Icon=new BitmapImage(new Uri("pack://application:,,,/JacquardStudio;component/Assets/app.ico"));
        var root=new Grid {Margin=new Thickness(20)};
        root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        var card=new Border {Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(0xE1,0xE5,0xEC)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(32)};
        var content=new StackPanel {VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
        content.Children.Add(new Image {Source=new BitmapImage(new Uri("pack://application:,,,/JacquardStudio;component/Assets/Studio/jacquard-mark.png")),Width=88,Height=88,Stretch=Stretch.Uniform,Margin=new Thickness(0,4,0,18)});
        content.Children.Add(new TextBlock {Text="JACQUARD",FlowDirection=FlowDirection.LeftToRight,Foreground=new SolidColorBrush(Color.FromRgb(0x18,0x1F,0x2B)),FontWeight=FontWeights.Bold,FontSize=25,HorizontalAlignment=HorizontalAlignment.Center});
        content.Children.Add(new TextBlock {Text="STUDIO  /  VAZIR DESIGN",FlowDirection=FlowDirection.LeftToRight,Foreground=new SolidColorBrush(Color.FromRgb(0x2C,0x60,0xF6)),FontWeight=FontWeights.Bold,FontSize=9,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,2,0,11)});
        content.Children.Add(new TextBlock {Text="در حال آماده‌سازی فضای کار…",HorizontalAlignment=HorizontalAlignment.Center,Foreground=new SolidColorBrush(Color.FromRgb(0x70,0x78,0x87)),FontSize=12});
        var track=new Border {Height=4,Width=150,Background=new SolidColorBrush(Color.FromRgb(0xED,0xF2,0xFF)),CornerRadius=new CornerRadius(3),Margin=new Thickness(0,20,0,0),ClipToBounds=true};
        var sweep=new Border {Width=46,Height=4,Background=new SolidColorBrush(Color.FromRgb(0x2C,0x60,0xF6)),HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new CornerRadius(3)};
        track.Child=sweep;var animation=new DoubleAnimation(-48,152,TimeSpan.FromSeconds(1.4)){RepeatBehavior=RepeatBehavior.Forever};sweep.BeginAnimation(FrameworkElement.WidthProperty,new DoubleAnimation(46,46,TimeSpan.FromSeconds(1.4)){RepeatBehavior=RepeatBehavior.Forever});sweep.RenderTransform=new TranslateTransform();sweep.RenderTransform.BeginAnimation(TranslateTransform.XProperty,animation);
        content.Children.Add(track);card.Child=content;root.Children.Add(card);Content=root;
    }
}
