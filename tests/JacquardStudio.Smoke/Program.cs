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
            Check(encoded[^2] == 0x06 && encoded[^1] == 0x01, "JC5 bottom-up LSB-first black bits and zero padding");
            byte[] legacy = (byte[])encoded.Clone(); legacy[^2] = 0x60; legacy[^1] = 0x80;
            var legacyImage = Jc5Codec.Decode(legacy, true); var legacyPixels = new byte[6]; legacyImage.CopyPixels(legacyPixels, 3, 0);
            Check(legacyPixels.SequenceEqual(pixels), "explicit import of version 006 bit order");
            var projectWithJc6 = new ProjectState(pipeline, "sample.bmp", "6", "4", "50", "Machine", false, 4, new string?[] { "black.bmp", "white.bmp" });
            string jc6ProjectPath = Path.Combine(dir, "jc6.sam"); ProjectFile.Save(jc6ProjectPath, projectWithJc6, Array.Empty<string>());
            Check(ProjectFile.Load(jc6ProjectPath).MachineFormatIndex == 4, "SAM remembers JC6 selection");
            Expect<InvalidDataException>(() => Jc5Codec.Decode(encoded[..^1]));
            Expect<ArgumentException>(() => Jc5Codec.Encode(image, "نام"));
            string jcPath = Path.Combine(dir, "test.jc5"); Jc5Codec.Save(image, jcPath, new[] { bmpPath });
            Check(Jc5Codec.Load(jcPath).PixelWidth == 3, "JC5 file load and save");
            Expect<IOException>(() => Jc5Codec.Save(image, jcPath, new[] { jcPath }));
            Expect<ArgumentException>(() => Jc5Codec.Save(image, Path.Combine(dir, "test.zc5"), Array.Empty<string>()));
            string jc6Path = Path.Combine(dir, "test.jc6"); Jc5Codec.Save(image, jc6Path, Array.Empty<string>());
            Check(Jc5Codec.Load(jc6Path).PixelWidth == 3, "sample-profile JC6 file load and save");
            // User-provided DesignScope exports, retained independently of the mutable uploaded archive.
            string repository = Directory.GetCurrentDirectory();
            while (!Directory.Exists(Path.Combine(repository, "tests", "fixtures"))) repository = Path.GetDirectoryName(repository) ?? throw new Exception("Reference fixtures missing");
            foreach (var sample in new[] { ("sample-1344.jc5", 1344), ("sample-1204.jc5", 1204) })
            {
                byte[] reference = File.ReadAllBytes(Path.Combine(repository, "tests", "fixtures", sample.Item1));
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
            // Independent DesignScope oracle: full hook raster AND pixel map for both exported controller samples.
            string zipped = Path.Combine(repository, "tests", "fixtures", "designscope-reference.zip");
            using (var archive = ZipFile.OpenRead(zipped))
            {
                foreach (string file in new[] { "baft.bmp", "orgin1.bmp", "machin.jc5", "machin.jc6", "legacy-design.jc5", "designscope-view-of-legacy.bmp" })
                {
                    var item = archive.GetEntry(file) ?? throw new Exception("Missing DesignScope reference: " + file);
                    using var input = item.Open(); using var outputFile = File.Create(Path.Combine(dir, file)); input.CopyTo(outputFile);
                }
            }
            var referenceMap = ImageLoader.Load(Path.Combine(dir, "baft.bmp"), 0).Image;
            Expect<InvalidDataException>(() => Jc5Codec.Encode(referenceMap, "test.jc5"));
            byte[] generated = Jc5Codec.Encode(referenceMap, "test.jc5", true);
            var refBytes = new byte[1200 * 1200 * 4];
            new FormatConvertedBitmap(referenceMap, PixelFormats.Bgra32, null, 0).CopyPixels(refBytes, 1200 * 4, 0);
            foreach (string file in new[] { "machin.jc5", "machin.jc6" })
            {
                byte[] reference = File.ReadAllBytes(Path.Combine(dir, file));
                Check(reference.AsSpan(reference.Length - 180000).SequenceEqual(generated.AsSpan(generated.Length - 180000)), "DesignScope exact machine raster for " + file);
                var hookMap = Jc5Codec.Decode(reference); var grayMap = new byte[1200 * 1200]; hookMap.CopyPixels(grayMap, 1200, 0);
                for (int i = 0; i < grayMap.Length; i++)
                    Check(grayMap[i] == (refBytes[i * 4 + 1] == 0 ? 0 : 255), "DesignScope exact decoded hook pixel");
            }
            // Actual old application output: normal interpretation shows DesignScope's distorted view;
            // explicit legacy interpretation restores the original full hook map.
            byte[] oldJc = File.ReadAllBytes(Path.Combine(dir, "legacy-design.jc5"));
            var oldView = Jc5Codec.Decode(oldJc); var correctedOldView = Jc5Codec.Decode(oldJc, true);
            byte[] oldGray = new byte[1200 * 1200], correctedGray = new byte[1200 * 1200];
            oldView.CopyPixels(oldGray, 1200, 0); correctedOldView.CopyPixels(correctedGray, 1200, 0);
            var seen = ImageLoader.Load(Path.Combine(dir, "designscope-view-of-legacy.bmp"), 0).Image;
            byte[] seenPixels = new byte[1200 * 1200 * 4];
            new FormatConvertedBitmap(seen, PixelFormats.Bgra32, null, 0).CopyPixels(seenPixels, 1200 * 4, 0);
            for (int i = 0; i < oldGray.Length; i++)
            {
                Check(oldGray[i] == (seenPixels[i * 4 + 1] == 0 ? 0 : 255), "legacy JC5 reproduces DesignScope view");
                Check(correctedGray[i] == (refBytes[i * 4 + 1] == 0 ? 0 : 255), "legacy JC5 restores original reference map");
            }
            // Five supplied pattern grids and their phase reproduce the independently supplied woven image.
            var colorful = ImageLoader.Load(Path.Combine(dir, "orgin1.bmp"), 0).Image;
            var wovenSession = new WorkflowSession(colorful); wovenSession.ApplySize(1200, 1200); wovenSession.ApplyColors(50);
            var referencePatterns = new Dictionary<int, int> { [0x656067] = 0, [0xE44944] = 1, [0xB9A771] = 2, [0xF7951D] = 3, [0xFFFFFF] = 4 };
            Check(wovenSession.Palette!.Colors.Length == 5, "reference palette count");
            for (int i = 0; i < wovenSession.Palette.Colors.Length; i++)
                wovenSession.AssignPattern(i, PatternLibrary.Presets[referencePatterns[wovenSession.Palette.Colors[i]]].Pattern);
            byte[] woven = new byte[1200 * 1200 * 4]; wovenSession.PatternPreview!.CopyPixels(woven, 1200 * 4, 0);
            for (int i = 0; i < 1200 * 1200; i++)
                Check(woven[i * 4] == (refBytes[i * 4 + 1] == 0 ? 0 : 255), "reference pattern placement and phase");
            // Manual edits preserve every pixel, target color, remaining patterns, and SAM state.
            var manual = new WorkflowSession(b.Image); manual.ApplySize(3, 2); manual.ApplyColors(50);
            var firstPattern = new WeavePattern(1, 1, new byte[] {0}); var targetPattern = new WeavePattern(1, 1, new byte[] {255});
            manual.AssignPattern(0, firstPattern); manual.AssignPattern(1, targetPattern);
            Check(DesignProcessor.NearestRemainingColor(manual.Palette!, 0) == 1, "nearest removal target");
            manual.MergeColors(0, 1);
            Check(manual.Palette!.Colors.Length == 1 && manual.Palette.Colors[0] == 0xFFFFFF && manual.Palette.Counts[0] == 6 && manual.Palette.Indices.All(i => i == 0), "merge pixels and target counts");
            Check(ReferenceEquals(manual.Patterns[0], targetPattern) && manual.CanSave, "merge preserves target weave");
            Expect<ArgumentException>(() => manual.MergeColors(0, 0));
            Check(manual.Palette.Colors.Length == 1 && manual.Palette.Counts[0] == 6, "invalid merge preserves state");
            string mergedProject = Path.Combine(dir, "merged.sam");
            ProjectFile.Save(mergedProject, new ProjectState(manual, "sample.bmp", "3", "2", "50", "Colors", false, 1, new string?[] { "target.bmp" }), Array.Empty<string>());
            var mergedRestored = ProjectFile.Load(mergedProject);
            Check(mergedRestored.Session.Palette!.Counts.SequenceEqual(new[] {6}) && mergedRestored.Session.Patterns[0]!.Values.SequenceEqual(new byte[] {255}), "SAM persists merged palette and weave");
            var multi = new PaletteResult(5, 1, 3, new[] {0x000000, 0x121212, 0xFFFFFF}, new byte[] {0, 1, 2, 0, 2}, new[] {2, 1, 2});
            Check(DesignProcessor.NearestRemainingColor(multi, 0) == 1, "nearest uses RGB distance");
            var mergedUp = DesignProcessor.MergeColors(multi, 0, 2);
            Check(mergedUp.Colors.SequenceEqual(new[] {0x121212, 0xFFFFFF}) && mergedUp.Indices.SequenceEqual(new byte[] {1, 0, 1, 1, 1}) && mergedUp.Counts.SequenceEqual(new[] {1, 4}), "merge handles source before target");
            var mergedDown = DesignProcessor.MergeColors(multi, 2, 0);
            Check(mergedDown.Indices.SequenceEqual(new byte[] {0, 1, 0, 0, 0}) && mergedDown.Counts.SequenceEqual(new[] {4, 1}), "merge handles source after target");
            Check(MachineFileNaming.SuggestedName(4) == "design6.jc5" && MachineFileNaming.ValidDestination("design6.jc5", 4) && MachineFileNaming.ValidDestination("طرح۶.jc5", 4), "JC6 uses jc5 extension with 6");
            Check(!MachineFileNaming.ValidDestination("design.jc5", 4) && !MachineFileNaming.ValidDestination("design6.jc6", 4) && MachineFileNaming.ValidDestination("design.jc5", 1), "JC6 name and extension validation");
            var colorStats = new PaletteEntry { ColorCode = "#ABCDEF", PixelCount = 25, Percent = 25.0 };
            Check(colorStats.ColorCode == "#ABCDEF" && colorStats.PixelCount == 25 && colorStats.Percent == 25.0, "color stats fields");
            var entry = new PaletteEntry(); entry.Assign("weave.bmp", new WeavePattern(2, 1, new byte[] { 0, 255 }));
            var thumbnail = new byte[2]; entry.PatternPreview!.CopyPixels(thumbnail, 2, 0);
            Check(entry.PatternLabel == "weave.bmp" && thumbnail.SequenceEqual(new byte[] {0, 255}) && entry.PatternGridPreview is not null && entry.PatternSize == "2 × 1", "pattern thumbnail, grid and dimensions");
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
            Check(AppVersion.Display == "008.2.5.1-a1", "QAVNS version from version.txt");
            var window = new MainWindow();
            Check(window.FontFamily.Source.Contains("Vazirmatn"), "embedded font selected in main window");
            var nav = (System.Windows.Controls.Grid)window.FindName("Navigation");
            var settings = (System.Windows.FrameworkElement)window.FindName("SettingsBorder");
            Check(nav.FlowDirection == System.Windows.FlowDirection.RightToLeft && System.Windows.Controls.Grid.GetColumn(settings) == 2, "right-to-left workflow and right settings panel");
            var list = (System.Windows.Controls.ListView)window.FindName("ColorPaletteList");
            Check(list.View is System.Windows.Controls.GridView grid && grid.Columns.Count == 4, "four-column color statistics table");
            var cards = (System.Windows.Controls.ItemsControl)window.FindName("PatternCards");
            Check(cards.ItemsPanel.LoadContent() is System.Windows.Controls.WrapPanel, "pattern gallery wraps rows");
            var gallery = (System.Windows.DependencyObject)window.FindName("PatternGallery");
            bool insideSettings = false;
            for (var parent = System.Windows.Media.VisualTreeHelper.GetParent(gallery); parent is not null; parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
                if (ReferenceEquals(parent, settings)) insideSettings = true;
            Check(insideSettings, "pattern gallery physically in right settings panel");
            Check(window.FindName("PaletteList") is null, "old pattern table removed");
            Check(((System.Windows.Controls.TextBlock)window.FindName("DeveloperText")).Text.Contains("میرخلیلی"), "developer credit in footer");
            Check(System.Windows.Shell.WindowChrome.GetWindowChrome(window) is not null, "resizable custom title bar");
            Check(window.FindName("CloseWindowButton") is System.Windows.Controls.Button && window.FindName("MaximizeWindowButton") is System.Windows.Controls.Button && window.FindName("MinimizeWindowButton") is System.Windows.Controls.Button, "caption buttons");
            Check(window.FindName("PatternCards") is System.Windows.Controls.ItemsControl, "reference-style pattern gallery exists");
            Check(window.FindName("FormatJC6") is System.Windows.Controls.RadioButton, "sample JC6 selector exists");
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
