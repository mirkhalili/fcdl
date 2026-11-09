using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>Portfolio built only from explicitly connected results; unrelated branches are not inferred to belong together.</summary>
public static class GraphReport
{
    public static IReadOnlyList<BitmapSource> Render(IReadOnlyList<GraphReportItem> items)
    {
        if(items.Count is < 1 or > 16) throw new ArgumentException("A report requires 1 to 16 modules.");
        var pages=new List<BitmapSource>();
        // A complete palette/weave table in the legacy report style. Each distinct palette remains separate;
        // weave assignments are used only when supplied by the same connected result.
        var seen=new HashSet<PaletteResult>();
        foreach(var item in items.OrderByDescending(i=>i.Value.Type==ModuleData.Fabric))
        {
            var p=item.Value.Palette; if(p is null || !seen.Add(p))continue;
            var colors=DesignProcessor.RenderColors(p);
            var assigned=item.Value.Type==ModuleData.Fabric && item.Value.Weaves?.Count==p.Colors.Length ? item.Value.Weaves.Select(w=>(WeavePattern?)w).ToArray() : new WeavePattern?[p.Colors.Length];
            var session=WorkflowSession.Restore(colors,colors,p,p.Colors.Length,assigned);
            var entries=p.Colors.Select((c,i)=>{ var entry=new PaletteEntry { Index=i,ColorCode=$"#{c:X6}",PixelCount=p.Counts[i],Percent=100d*p.Counts[i]/p.Indices.Length,Brush=new SolidColorBrush(Color.FromRgb((byte)(c>>16),(byte)(c>>8),(byte)c)) }; if(assigned[i] is { } weave) entry.Restore("بافت اختصاص‌یافته",weave); return entry; }).ToArray();
            for(int n=0;n<(entries.Length+49)/50;n++) pages.Add(DesignReport.Render(DesignReport.CreatePage(session,entries,string.IsNullOrWhiteSpace(item.Label)?"پورتفولیو":item.Label,null,false,machine:item.Value.Machine,analysis:item.Value.Analysis,composition:item.Value.Composition,palettePage:n)));
        }
        var pictures=items.Where(i=>i.Value.Image is not null).ToArray();
        for(int start=0;start<pictures.Length;start+=6)
        {
            var page=Page("پورتفولیو · تصاویر خروجی‌های متصل"); var grid=new Grid { Margin=new Thickness(32,80,32,45) }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
            for(int row=0;row<3;row++)grid.RowDefinitions.Add(new RowDefinition());
            foreach(var (item,index) in pictures.Skip(start).Take(6).Select((item,index)=>(item,index)))
            {
                var box=new StackPanel { Margin=new Thickness(10) }; Grid.SetRow(box,index/2); Grid.SetColumn(box,index%2); grid.Children.Add(box);
                var image=new Image { Source=item.Value.Image,Height=235,Stretch=Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor); box.Children.Add(image);
                box.Children.Add(new TextBlock { Text=string.IsNullOrWhiteSpace(item.Label)?item.Kind.ToString():item.Label,FontSize=11,Foreground=Brushes.Black,TextAlignment=TextAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis });
            }
            page.Children.Add(grid); pages.Add(DesignReport.Render(page));
        }
        // Paginate bounded chunks instead of silently trimming connected metadata or long fields.
        var lines=new List<string>();
        foreach(var item in items)
        {
            lines.Add("● " + (string.IsNullOrWhiteSpace(item.Label)?item.Kind.ToString():item.Label));
            foreach(string raw in item.Details.Replace("\r","").Split('\n'))
            {
                if(raw.Length==0) {lines.Add("");continue;}
                for(int pos=0;pos<raw.Length;pos+=65) lines.Add(raw.Substring(pos,Math.Min(65,raw.Length-pos)));
            }
            lines.Add("");
        }
        for(int start=0;start<lines.Count;start+=42)
        {
            var page=Page("پورتفولیو · اطلاعات طرح و خروجی‌ها"); var text=new TextBlock { Text=string.Join("\n",lines.Skip(start).Take(42)),Foreground=Brushes.Black,FontSize=12,LineHeight=21,Margin=new Thickness(40,80,40,40),TextWrapping=TextWrapping.NoWrap }; page.Children.Add(text); pages.Add(DesignReport.Render(page));
        }
        return pages;
    }
    private static Grid Page(string title)
    {
        var page=new Grid { Width=793.7,Height=1122.5,Background=Brushes.White,FlowDirection=FlowDirection.RightToLeft };
        System.Windows.Documents.TextElement.SetFontFamily(page,(FontFamily)Application.Current.FindResource("VazirFont"));
        page.Children.Add(new TextBlock { Text=title,Foreground=new SolidColorBrush(Color.FromRgb(29,117,98)),FontSize=20,FontWeight=FontWeights.Bold,Margin=new Thickness(35,22,35,0),VerticalAlignment=VerticalAlignment.Top });
        page.Children.Add(new TextBlock { Text="Jacquard Studio · گزارش طرح",Foreground=Brushes.DimGray,FontSize=10,Margin=new Thickness(35,0,35,14),VerticalAlignment=VerticalAlignment.Bottom }); return page;
    }
}
