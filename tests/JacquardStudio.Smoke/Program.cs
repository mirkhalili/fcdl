using System.IO;
using System.IO.Compression;
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
            string resizedPath = Path.Combine(dir, "resized.bmp");
            ImageExporter.Save(pipeline.Resized!, resizedPath, false, new[] { bmpPath });
            Check(ImageLoader.Load(resizedPath, 0).Image.PixelWidth == 6, "export size stage");
            pipeline.ApplyColors(50);
            string colorsPath = Path.Combine(dir, "colors.tif");
            ImageExporter.Save(pipeline.ColorPreview!, colorsPath, true, new[] { bmpPath });
            Check(ImageLoader.Load(colorsPath, 0).Image.PixelHeight == 4, "export color stage");
            Check(pipeline.Palette!.Width == 6 && pipeline.Palette.Height == 4, "color stage uses resized output");
            pipeline.AssignPattern(0, new WeavePattern(1, 1, new byte[] { 0 }));
            Check(!pipeline.CanSave, "cannot save with an unassigned pattern");
            pipeline.AssignPattern(1, new WeavePattern(1, 1, new byte[] { 255 }));
            Check(pipeline.CanSave && pipeline.PatternPreview!.PixelWidth == 6, "pattern stage output");
            string projectPath = Path.Combine(dir, "test.sam");
            var projectState = new ProjectState(pipeline, "sample.bmp", "7", "4", "50", "Patterns", false, 2, new string?[] { "black.bmp", "white.tif" });
            ProjectFile.Save(projectPath, projectState, new[] { bmpPath });
            var restored = ProjectFile.Load(projectPath);
            Check(restored.Session.CanSave && restored.Session.Resized!.PixelWidth == 6 && restored.Session.Palette!.Indices.SequenceEqual(pipeline.Palette!.Indices), "SAM exact resize and palette");
            Check(restored.WidthInput == "7" && restored.HeightInput == "4" && restored.Stage == "Patterns" && restored.MachineFormatIndex == 2, "SAM pending inputs and stage");
            Check(restored.PatternNames.SequenceEqual(new string?[] { "black.bmp", "white.tif" }) && restored.Session.Patterns[1]!.Values.SequenceEqual(new byte[] {255}), "SAM embedded patterns");
            var beforeProject = new byte[6 * 4 * 4]; var afterProject = new byte[6 * 4 * 4];
            pipeline.PatternPreview!.CopyPixels(beforeProject, 24, 0); restored.Session.PatternPreview!.CopyPixels(afterProject, 24, 0);
            Check(beforeProject.SequenceEqual(afterProject), "SAM exact pattern preview");
            Expect<IOException>(() => ProjectFile.Save(projectPath, projectState, new[] { projectPath }));
            string broken = Path.Combine(dir, "broken.sam"); File.WriteAllBytes(broken, new byte[] { 1, 2, 3 });
            Expect<InvalidDataException>(() => ProjectFile.Load(broken));
            var sourceOnly = new WorkflowSession(b.Image);
            string firstProject = Path.Combine(dir, "source-only.sam");
            ProjectFile.Save(firstProject, new ProjectState(sourceOnly, "sample.bmp", "3", "2", "50", "Size", true, 0, Array.Empty<string?>()), Array.Empty<string>());
            Check(ProjectFile.Load(firstProject).Session.Resized is null, "SAM source-only project");
            string patternsPath = Path.Combine(dir, "patterns.bmp");
            ImageExporter.Save(pipeline.PatternPreview!, patternsPath, false, new[] { bmpPath });
            Check(ImageLoader.Load(patternsPath, 0).Image.PixelWidth == 6, "export pattern stage");
            Expect<IOException>(() => ImageExporter.Save(pipeline.PatternPreview!, bmpPath, false, new[] { bmpPath }));
            Expect<ArgumentException>(() => ImageExporter.Save(pipeline.PatternPreview!, Path.Combine(dir, "fake.jc5"), false, new[] { bmpPath }));
            Check(!File.Exists(Path.Combine(dir, "fake.jc5")), "machine extension not created");
            // Small asymmetric raster exercises bit order, vertical direction and non-byte-aligned width.
            byte[] encoded = Jc5Codec.Encode(image, "test.jc5");
            var decoded = Jc5Codec.Decode(encoded); var roundTrip = new byte[6]; decoded.CopyPixels(roundTrip, 3, 0);
            Check(roundTrip.SequenceEqual(pixels), "JC5 lossless hook-map roundtrip");
            Check(encoded[^2] == 0x60 && encoded[^1] == 0x80, "JC5 bottom-up MSB-first black bits and zero padding");
            Expect<InvalidDataException>(() => Jc5Codec.Decode(encoded[..^1]));
            Expect<ArgumentException>(() => Jc5Codec.Encode(image, "نام"));
            string jcPath = Path.Combine(dir, "test.jc5"); Jc5Codec.Save(image, jcPath, new[] { bmpPath });
            Check(Jc5Codec.Load(jcPath).PixelWidth == 3, "JC5 file load and save");
            Expect<IOException>(() => Jc5Codec.Save(image, jcPath, new[] { jcPath }));
            Expect<ArgumentException>(() => Jc5Codec.Save(image, Path.Combine(dir, "test.zc5"), Array.Empty<string>()));
            // User-provided DesignScope exports in the repository: validate raster independently of our header.
            string repository = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(repository, "baft.zip"))) repository = Path.GetDirectoryName(repository) ?? throw new Exception("Reference archive missing");
            using (var archive = ZipFile.OpenRead(Path.Combine(repository, "baft.zip")))
            foreach (var sample in new[] { ("PP1-02.jc5.jc5", 1344), ("PP1-02.jc6.jc5", 1204) })
            {
                var fixture = archive.GetEntry(sample.Item1) ?? throw new Exception("Reference sample missing");
                using var input = fixture.Open(); using var memory = new MemoryStream(); input.CopyTo(memory); byte[] reference = memory.ToArray();
                var map = Jc5Codec.Decode(reference);
                Check(map.PixelWidth == sample.Item2 && map.PixelHeight == 1203, "DesignScope JC5 dimensions");
                byte[] regenerated = Jc5Codec.Encode(map, "test.jc5"); int stride = (sample.Item2 + 7) / 8, rasterLength = stride * map.PixelHeight;
                int oldOffset = reference.Length - rasterLength, newOffset = regenerated.Length - rasterLength;
                for (int y = 0; y < map.PixelHeight; y++)
                for (int x = 0; x < sample.Item2; x++)
                {
                    int index = y * stride + x / 8, mask = 0x80 >> (x % 8);
                    Check((reference[oldOffset + index] & mask) == (regenerated[newOffset + index] & mask), "DesignScope declared hook bits");
                }
            }
            var entry = new PaletteEntry(); entry.Assign("weave.bmp", new WeavePattern(2, 1, new byte[] { 0, 255 }));
            var thumbnail = new byte[2]; entry.PatternPreview!.CopyPixels(thumbnail, 2, 0);
            Check(entry.PatternLabel == "weave.bmp" && thumbnail.SequenceEqual(new byte[] {0, 255}), "pattern thumbnail");
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
            Check(AppVersion.Display == "006.2.3.0-a1", "QAVNS version from version.txt");
            var window = new MainWindow();
            Check(window.FontFamily.Source.Contains("Vazirmatn"), "embedded font selected in main window");
            var nav = (System.Windows.Controls.Grid)window.FindName("Navigation");
            var settings = (System.Windows.FrameworkElement)window.FindName("SettingsBorder");
            Check(nav.FlowDirection == System.Windows.FlowDirection.RightToLeft && System.Windows.Controls.Grid.GetColumn(settings) == 2, "right-to-left workflow and right settings panel");
            var list = (System.Windows.Controls.ListView)window.FindName("PaletteList");
            Check(list.View is System.Windows.Controls.GridView grid && grid.Columns.Count == 2, "two-column color and pattern table");
            var machineExport = (System.Windows.Controls.Button)window.FindName("MachineExportButton");
            Check(!machineExport.IsEnabled, "machine export disabled until a valid input is selected");
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
