using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
namespace JacquardStudio;
public static class GraphExports
{
    private static void AtomicWrite(string path,IEnumerable<string> protectedPaths,Action<Stream> write)
    {
        string target=Path.GetFullPath(path); if(protectedPaths.Any(p=>string.Equals(Path.GetFullPath(p),target,StringComparison.OrdinalIgnoreCase)))throw new IOException("فایل پروژه نباید با خروجی جایگزین شود.");
        string temp=Path.Combine(Path.GetDirectoryName(target)!,".export-"+Guid.NewGuid().ToString("N")+".tmp");
        try {using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))write(file);File.Move(temp,target,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static void SaveImage(BitmapSource image,string path,IEnumerable<string> protectedPaths)
    {
        string ext=Path.GetExtension(path).ToLowerInvariant(); BitmapEncoder encoder=ext switch {".png"=>new PngBitmapEncoder(),".bmp"=>new BmpBitmapEncoder(),".tif" or ".tiff"=>new TiffBitmapEncoder(),_=>throw new ArgumentException("پسوند تصویر باید PNG، BMP یا TIFF باشد.")};
        encoder.Frames.Add(BitmapFrame.Create(image));AtomicWrite(path,protectedPaths,encoder.Save);
    }
    public static void SavePalette(PaletteResult palette,string path,IEnumerable<string> protectedPaths)
    {
        if(!path.EndsWith(".csv",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("پسوند جدول باید CSV باشد.");
        if(palette.Colors.Length!=palette.Counts.Length)throw new InvalidDataException("پالت معتبر نیست.");
        AtomicWrite(path,protectedPaths,stream=>{using var writer=new StreamWriter(stream,new UTF8Encoding(true),1024,true);writer.WriteLine("index,hex,r,g,b,pixels");for(int i=0;i<palette.Colors.Length;i++){int c=palette.Colors[i];writer.WriteLine($"{i+1},#{c:X6},{(c>>16)&255},{(c>>8)&255},{c&255},{palette.Counts[i]}");}});
    }
}
