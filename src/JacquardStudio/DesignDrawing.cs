using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>Pixel-accurate, bounded operations on a private BGRA copy of the input image.</summary>
public sealed class DesignDrawing
{
    public int Width { get; }
    public int Height { get; }
    private byte[] pixels;
    public DesignDrawing(BitmapSource image)
    {
        Width=image.PixelWidth; Height=image.PixelHeight;
        if(Width<1 || Height<1 || (long)Width*Height>DesignProcessor.MaxPixels) throw new ArgumentException("تصویر طراحی باید حداکثر چهار میلیون پیکسل باشد.");
        pixels=new byte[checked(Width*Height*4)]; new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0).CopyPixels(pixels,Width*4,0);
    }
    public byte[] Snapshot() => (byte[])pixels.Clone();
    public void Restore(byte[] data) { if(data.Length!=pixels.Length) throw new ArgumentException("ابعاد تصویر معتبر نیست."); pixels=(byte[])data.Clone(); }
    public BitmapSource Image() { var image=BitmapSource.Create(Width,Height,96,96,PixelFormats.Bgra32,null,pixels,Width*4); image.Freeze(); return image; }
    public Color At(int x,int y) { Check(x,y); int i=(y*Width+x)*4; return Color.FromArgb(pixels[i+3],pixels[i+2],pixels[i+1],pixels[i]); }
    private void Check(int x,int y) { if((uint)x >= (uint)Width || (uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(x)); }
    private void Put(int x,int y,Color c) { int i=(y*Width+x)*4; pixels[i]=c.B; pixels[i+1]=c.G; pixels[i+2]=c.R; pixels[i+3]=c.A; }
    public void Line(int x0,int y0,int x1,int y1,Color color)
    {
        Check(x0,y0); Check(x1,y1); int dx=Math.Abs(x1-x0), sx=x0<x1?1:-1, dy=-Math.Abs(y1-y0), sy=y0<y1?1:-1, err=dx+dy;
        while(true) { Put(x0,y0,color); if(x0==x1 && y0==y1)break; int e=2*err; if(e>=dy){err+=dy;x0+=sx;} if(e<=dx){err+=dx;y0+=sy;} }
    }
    public void Rectangle(int x0,int y0,int x1,int y1,Color color)
    { Check(x0,y0); Check(x1,y1); Line(x0,y0,x1,y0,color); Line(x1,y0,x1,y1,color); Line(x1,y1,x0,y1,color); Line(x0,y1,x0,y0,color); }
    public void Fill(int x,int y,Color color)
    {
        Check(x,y); Color target=At(x,y); if(target==color)return;
        // Mark pixels before adding to queue, so each is visited once even on fragmented regions.
        var queue=new Queue<int>(); int start=y*Width+x; Put(x,y,color); queue.Enqueue(start);
        while(queue.Count>0) { int p=queue.Dequeue(), px=p%Width, py=p/Width;
            void Add(int nx,int ny) { if((uint)nx<(uint)Width && (uint)ny<(uint)Height && At(nx,ny)==target) { Put(nx,ny,color); queue.Enqueue(ny*Width+nx); } }
            Add(px-1,py); Add(px+1,py); Add(px,py-1); Add(px,py+1);
        }
    }
}
