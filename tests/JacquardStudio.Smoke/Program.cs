using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JacquardStudio;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "jacquard-smoke-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var pixels = new byte[] { 0, 255, 255, 255, 0, 0 };
            var image = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Gray8, null, pixels, 3);
            var bmp = new BmpBitmapEncoder(); bmp.Frames.Add(BitmapFrame.Create(image));
            string bmpPath = Path.Combine(dir, "sample.bmp");
            using (var f = File.Create(bmpPath)) bmp.Save(f);
            var tiff = new TiffBitmapEncoder(); tiff.Frames.Add(BitmapFrame.Create(image)); tiff.Frames.Add(BitmapFrame.Create(image));
            string tiffPath = Path.Combine(dir, "pages.tif");
            using (var f = File.Create(tiffPath)) tiff.Save(f);
            var a = ImageLoader.Load(bmpPath, 0);
            Check(a.Format == "BMP" && a.Image.PixelWidth == 3 && a.Image.PixelHeight == 2 && a.PageCount == 1, "BMP dimensions and format");
            var b = ImageLoader.Load(tiffPath, 1);
            Check(b.Format == "TIFF" && b.PageIndex == 1 && b.PageCount == 2, "TIFF page selection");
            var extracted = new byte[6]; b.Image.CopyPixels(extracted, 3, 0);
            Check(extracted.SequenceEqual(pixels), "pixel values retained");
            Expect<ArgumentOutOfRangeException>(() => ImageLoader.Load(tiffPath, 2));
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            string pngPath = Path.Combine(dir, "disguised.bmp"); using (var f = File.Create(pngPath)) png.Save(f);
            Expect<NotSupportedException>(() => ImageLoader.Load(pngPath, 0));
            Expect<FileNotFoundException>(() => ImageLoader.Load(Path.Combine(dir, "missing.bmp"), 0));
            File.Delete(bmpPath); // decoder must not retain a file handle
            var app = new App(); app.InitializeComponent();
            var window = new MainWindow(); window.Close();
            Console.WriteLine("Smoke tests passed."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { Directory.Delete(dir, true); }
    }
    private static void Check(bool condition, string description) { if (!condition) throw new Exception("Failed: " + description); }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
