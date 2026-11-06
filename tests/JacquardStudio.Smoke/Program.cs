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
            var pipeline = new WorkflowSession(b.Image);
            Expect<InvalidOperationException>(() => pipeline.ApplyColors(50));
            pipeline.ApplySize(6, 4);
            Check(pipeline.Resized!.PixelWidth == 6 && pipeline.Resized.PixelHeight == 4, "size stage output");
            pipeline.ApplyColors(50);
            Check(pipeline.Palette!.Width == 6 && pipeline.Palette.Height == 4, "color stage uses resized output");
            pipeline.AssignPattern(0, new WeavePattern(1, 1, new byte[] { 0 }));
            Check(!pipeline.CanSave, "cannot save with an unassigned pattern");
            pipeline.AssignPattern(1, new WeavePattern(1, 1, new byte[] { 255 }));
            Check(pipeline.CanSave && pipeline.PatternPreview!.PixelWidth == 6, "pattern stage output");
            Expect<ArgumentOutOfRangeException>(() => pipeline.ApplySize(0, 4));
            Check(pipeline.CanSave, "failed size stage preserves committed work");
            pipeline.ApplyColors(1);
            Check(!pipeline.CanSave && pipeline.Patterns.Length == 1, "color changes clear downstream patterns");
            pipeline.ApplySize(3, 2);
            Check(pipeline.Palette is null && pipeline.PatternPreview is null, "size changes clear downstream stages");
            var prepared = DesignProcessor.Prepare(b.Image, 6, 4, 50);
            Check(prepared.Width == 6 && prepared.Height == 4 && prepared.SourceColorCount == 2 && prepared.Colors.Length == 2, "resize and exact palette");
            Check(prepared.Indices[0] == prepared.Indices[1] && prepared.Indices[0] != prepared.Indices[2], "nearest neighbor geometry");
            var black = new WeavePattern(2, 1, new byte[] { 0, 255 });
            var white = new WeavePattern(1, 1, new byte[] { 255 });
            var output = DesignProcessor.Render(prepared, new WeavePattern?[] { black, white });
            var actual = new byte[6 * 4 * 4]; output.CopyPixels(actual, 24, 0);
            Check(actual[0] == 0 && actual[4] == 255 && actual[8] == 255, "pattern tiled in global coordinates");
            var many = new byte[100 * 4];
            for (int i = 0; i < 100; i++) { many[i * 4] = (byte)(i * 2); many[i * 4 + 1] = (byte)i; many[i * 4 + 2] = (byte)(i * 2); many[i * 4 + 3] = 255; }
            var manyImage = BitmapSource.Create(10, 10, 96, 96, PixelFormats.Bgra32, null, many, 40);
            var reduced = DesignProcessor.Prepare(manyImage, 10, 10, 50);
            Check(reduced.SourceColorCount == 100 && reduced.Colors.Length <= 50, "automatic reduction to <=50");
            var less = DesignProcessor.Prepare(manyImage, 10, 10, 3);
            Check(less.Colors.Length <= 3 && less.Counts.Sum() == 100, "requested color reduction");
            Expect<ArgumentOutOfRangeException>(() => DesignProcessor.Prepare(manyImage, 10, 10, 51));
            Expect<ArgumentOutOfRangeException>(() => DesignProcessor.Prepare(manyImage, 4001, 1000, 5));
            var transparent = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] {0, 0, 0, 0}, 4);
            Expect<InvalidDataException>(() => DesignProcessor.Prepare(transparent, 1, 1, 1));
            var gray = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] {128, 128, 128, 255}, 4);
            Expect<InvalidDataException>(() => DesignProcessor.ReadPattern(gray));
            var pure = DesignProcessor.ReadPattern(image);
            Check(pure.Width == 3 && pure.Height == 2 && pure.Values.SequenceEqual(pixels), "binary pattern");
            File.Delete(bmpPath); // decoder must not retain a file handle
            var app = new App(); app.InitializeComponent();
            Check(AppVersion.Display == "003.2.0.0-a1", "QAVNS version from version.txt");
            var window = new MainWindow();
            Check(window.FontFamily.Source.Contains("Vazirmatn"), "embedded font selected in main window");
            Check(window.FontFamily.GetTypefaces().Any(t => t.TryGetGlyphTypeface(out var glyph) && glyph.FontUri.ToString().Contains("Vazirmatn", StringComparison.OrdinalIgnoreCase)), "embedded font resolves");
            window.Close();
            var about = new AboutWindow(false); Check(about.FontFamily.Source.Contains("Vazirmatn"), "about font"); about.Close();
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
