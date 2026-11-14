using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace JacquardStudio;

/// <summary>Rectangular selection mask and explicitly destructive-on-output masking. Source bitmaps remain unchanged.</summary>
public static class GraphMasks
{
    public static BitmapSource Rectangle(BitmapSource source,int x,int y,int width,int height)
    {
        int w=source.PixelWidth,h=source.PixelHeight;
        if(w<1 || h<1 || (long)w*h>DesignProcessor.MaxPixels)throw new ArgumentException("تصویر ماسک باید حداکثر چهار میلیون پیکسل باشد.");
        if(x<0 || y<0 || x>=w || y>=h || width<0 || height<0)throw new ArgumentException("مختصات و اندازه ناحیه معتبر نیست.");
        width=width==0?w-x:width;height=height==0?h-y:height;
        if((long)x+width>w || (long)y+height>h)throw new ArgumentException("ناحیه از مرز تصویر بیرون است؛ مختصات و ابعاد را اصلاح کنید.");
        var data=new byte[w*h];for(int row=y;row<y+height;row++)Array.Fill(data,(byte)255,row*w+x,width);
        var image=BitmapSource.Create(w,h,96,96,PixelFormats.Gray8,null,data,w);image.Freeze();return image;
    }
    public static BitmapSource Apply(BitmapSource source,BitmapSource mask,bool invert,string background)
    {
        if(source.PixelWidth!=mask.PixelWidth || source.PixelHeight!=mask.PixelHeight)throw new ArgumentException("ابعاد ماسک و تصویر باید یکسان باشند.");
        int w=source.PixelWidth,h=source.PixelHeight;if(w<1 || h<1 || (long)w*h>DesignProcessor.MaxPixels)throw new ArgumentException("تصویر ماسک باید حداکثر چهار میلیون پیکسل باشد.");
        var selected=DesignAnalysis.ReadBinary(mask);var color=FabricSimulation.ParseColor(background);var data=new byte[w*h*4];new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0).CopyPixels(data,w*4,0);
        for(int i=0;i<selected.Length;i++)if((selected[i]==255)==invert){int p=i*4;data[p]=color.B;data[p+1]=color.G;data[p+2]=color.R;data[p+3]=255;}
        var result=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,data,w*4);result.Freeze();return result;
    }
}
