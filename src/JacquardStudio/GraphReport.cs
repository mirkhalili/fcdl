using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public static class GraphReport
{
    public static IReadOnlyList<BitmapSource> Render(IReadOnlyList<GraphReportItem> items)
    {
        if(items.Count is < 1 or > 16) throw new ArgumentException("A report requires 1 to 16 modules.");
        var pages=new List<BitmapSource>();
        for(int i=0;i<items.Count;i++)
        {
            var item=items[i]; var page=new Grid { Width=793.7,Height=1122.5,Background=Brushes.White,FlowDirection=FlowDirection.RightToLeft,Margin=new Thickness(0) };
            System.Windows.Documents.TextElement.SetFontFamily(page,(FontFamily)Application.Current.FindResource("VazirFont"));
            page.RowDefinitions.Add(new RowDefinition { Height=new GridLength(76) }); page.RowDefinitions.Add(new RowDefinition { Height=new GridLength(440) }); page.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) }); page.RowDefinitions.Add(new RowDefinition { Height=new GridLength(35) });
            page.Children.Add(new TextBlock { Text=$"گزارش ماژول · {item.Kind}   ({i+1}/{items.Count})",Foreground=Brushes.Black,FontSize=20,FontWeight=FontWeights.Bold,Margin=new Thickness(30,18,30,0),TextTrimming=TextTrimming.CharacterEllipsis });
            var frame=new Border { Margin=new Thickness(30,8,30,8),Background=new SolidColorBrush(Color.FromRgb(243,245,246)),BorderBrush=Brushes.LightGray,BorderThickness=new Thickness(1) };
            if(item.Value.Image is { } image) { var view=new Image { Source=image,Stretch=Stretch.Uniform,Margin=new Thickness(10) }; RenderOptions.SetBitmapScalingMode(view,BitmapScalingMode.NearestNeighbor); frame.Child=view; }
            else frame.Child=new TextBlock { Text="این خروجی تصویر ندارد",Foreground=Brushes.DimGray,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
            Grid.SetRow(frame,1); page.Children.Add(frame);
            var details=new TextBlock { Text=item.Details,Foreground=Brushes.Black,FontSize=11,LineHeight=18,Margin=new Thickness(35,10,35,5),TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis }; Grid.SetRow(details,2); page.Children.Add(details);
            var footer=new TextBlock { Text="Jacquard Studio · گزارش گراف",Foreground=Brushes.DimGray,FontSize=10,Margin=new Thickness(30,5,30,5) }; Grid.SetRow(footer,3); page.Children.Add(footer);
            pages.Add(DesignReport.Render(page));
        }
        return pages;
    }
}
