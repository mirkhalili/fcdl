using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace JacquardStudio;
public static class GraphChecks
{
    private static byte[] Pixels(BitmapSource image)
    {
        if((long)image.PixelWidth*image.PixelHeight>DesignProcessor.MaxPixels)throw new InvalidDataException("تصویر باید حداکثر چهار میلیون پیکسل باشد.");
        var data=new byte[checked(image.PixelWidth*image.PixelHeight*4)];new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0).CopyPixels(data,image.PixelWidth*4,0);return data;
    }
    public static BitmapSource Threshold(BitmapSource image,int threshold,bool invert)
    {
        if(threshold is < 0 or > 255)throw new ArgumentException("آستانه باید بین صفر تا ۲۵۵ باشد.");var p=Pixels(image); var values=new byte[image.PixelWidth*image.PixelHeight];
        for(int i=0;i<values.Length;i++) {int k=i*4;if(p[k+3]!=255)throw new InvalidDataException("تصویر شفاف است؛ ابتدا تصویر مات انتخاب کنید."); bool white=(p[k+2]*299+p[k+1]*587+p[k]*114)/1000>=threshold; values[i]=(white^invert)?(byte)255:(byte)0;}
        var output=BitmapSource.Create(image.PixelWidth,image.PixelHeight,96,96,PixelFormats.Gray8,null,values,image.PixelWidth);output.Freeze();return output;
    }
    public static (int horizontal,int vertical) Seams(BitmapSource image)
    {
        byte[] p=Pixels(image);int w=image.PixelWidth,h=image.PixelHeight,horizontal=0,vertical=0;
        bool Equal(int a,int b) {for(int k=0;k<4;k++)if(p[a*4+k]!=p[b*4+k])return false;return true;}
        for(int y=0;y<h;y++)if(!Equal(y*w,y*w+w-1))horizontal++; for(int x=0;x<w;x++)if(!Equal(x,(h-1)*w+x))vertical++;return(horizontal,vertical);
    }
    public static (BitmapSource image,int changed) Compare(BitmapSource first,BitmapSource second)
    {
        if(first.PixelWidth!=second.PixelWidth || first.PixelHeight!=second.PixelHeight)throw new ArgumentException("ابعاد دو ورودی باید یکسان باشد.");
        var a=Pixels(first);var b=Pixels(second);var result=new byte[a.Length];int count=0;
        for(int i=0;i<a.Length;i+=4) {bool different=false;for(int j=0;j<4;j++)different|=a[i+j]!=b[i+j]; if(different)count++; result[i]=result[i+1]=different?(byte)0:(byte)255; result[i+2]=255; result[i+3]=255;}
        var image=BitmapSource.Create(first.PixelWidth,first.PixelHeight,96,96,PixelFormats.Bgra32,null,result,first.PixelWidth*4);image.Freeze();return(image,count);
    }
}
