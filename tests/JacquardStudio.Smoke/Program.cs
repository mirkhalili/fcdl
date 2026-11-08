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
            Check(ImageLoader.Load(pngPath, 0).Format == "PNG", "PNG detected by image signature");
            var jpeg = new JpegBitmapEncoder(); jpeg.Frames.Add(BitmapFrame.Create(image)); string jpegPath = Path.Combine(dir, "sample.jpg"); using (var f = File.Create(jpegPath)) jpeg.Save(f);
            Check(ImageLoader.Load(jpegPath, 0).Format == "JPEG", "JPEG input");
            string webpPath = Path.Combine(dir, "sample.webp"); using (var bitmap = new SkiaSharp.SKBitmap(3, 2)) using (var skImage = SkiaSharp.SKImage.FromBitmap(bitmap)) using (var data = skImage.Encode(SkiaSharp.SKEncodedImageFormat.Webp, 90)) using (var f = File.Create(webpPath)) data.SaveTo(f);
            Check(ImageLoader.Load(webpPath, 0).Format == "WebP", "WebP input");
            string legacyAi = Path.Combine(dir, "legacy.ai"); File.WriteAllText(legacyAi, "%!PS-Adobe-3.0"); Expect<NotSupportedException>(() => ImageLoader.Load(legacyAi, 0));
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
            string legacySam = Path.Combine(dir, "legacy-v1.sam"); File.Copy(projectPath, legacySam);
            using (var zip = ZipFile.Open(legacySam, ZipArchiveMode.Update))
            {
                var entryManifest = zip.GetEntry("manifest.json")!; string json; using (var reader = new StreamReader(entryManifest.Open())) json = reader.ReadToEnd();
                entryManifest.Delete(); var replacement = zip.CreateEntry("manifest.json"); using var writer = new StreamWriter(replacement.Open()); writer.Write(json.Replace("\"Version\":8", "\"Version\":1"));
            }
            Check(ProjectFile.Load(legacySam).Session.CanSave, "legacy SAM version one loads");
            string legacyV2 = Path.Combine(dir, "legacy-v2.sam"); File.Copy(projectPath, legacyV2);
            using (var zip = ZipFile.Open(legacyV2, ZipArchiveMode.Update))
            {
                var old = zip.GetEntry("manifest.json")!; string json; using (var reader = new StreamReader(old.Open())) json = reader.ReadToEnd();
                old.Delete(); var replacement = zip.CreateEntry("manifest.json"); using var writer = new StreamWriter(replacement.Open()); writer.Write(json.Replace("\"Version\":8", "\"Version\":2"));
            }
            Check(ProjectFile.Load(legacyV2).Session.CanSave, "legacy SAM version two loads");
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
            // Per-user bank writes self-contained immutable copies and survives a new bank instance.
            string bankDir = Path.Combine(dir, "weave-bank"); var bank = new WeaveBank(bankDir);
            Check(bank.Load(out int skipped).Count == PatternLibrary.Presets.Count && skipped == 0, "built-in bank entries");
            var bankPattern = new WeavePattern(2, 2, new byte[] {0, 255, 255, 0});
            var savedWeave = bank.SaveNew("بافت آزمایشی", bankPattern); bankPattern.Values[0] = 255;
            var reopenedBank = new WeaveBank(bankDir); var loadedBank = reopenedBank.Load(out skipped);
            var loadedWeave = loadedBank.Single(x => x.Id == savedWeave.Id);
            Check(loadedWeave.Name == "بافت آزمایشی" && loadedWeave.Pattern.Values.SequenceEqual(new byte[] {0,255,255,0}), "bank persistence and copy isolation");
            Expect<ArgumentException>(() => bank.SaveNew("", loadedWeave.Pattern));
            Expect<InvalidDataException>(() => bank.SaveNew("bad", new WeavePattern(2, 1, new byte[] {0, 1})));
            File.WriteAllText(Path.Combine(bankDir, Guid.NewGuid().ToString("N") + ".weave.json"), "invalid");
            Check(reopenedBank.Load(out skipped).Any(x => x.Id == savedWeave.Id) && skipped == 1, "corrupt bank item is skipped without deleting good data");
            Expect<InvalidOperationException>(() => reopenedBank.Delete(loadedBank[0]));
            var bankSession = new WorkflowSession(a.Image); bankSession.ApplySize(3, 2); bankSession.ApplyColors(50);
            for (int i = 0; i < bankSession.Palette!.Colors.Length; i++) bankSession.AssignPattern(i, loadedWeave.Pattern);
            string bankProject = Path.Combine(dir, "bank-project.sam");
            ProjectFile.Save(bankProject, new ProjectState(bankSession, "sample.bmp", "3", "2", "50", "Patterns", false, 1, bankSession.Patterns.Select(_ => (string?)loadedWeave.Name).ToArray()), new[] { bmpPath });
            reopenedBank.Delete(loadedWeave);
            Check(ProjectFile.Load(bankProject).Session.Patterns[0]!.Values.SequenceEqual(new byte[] {0,255,255,0}), "project retains bank weave after deletion");
            Check(reopenedBank.Load(out skipped).All(x => x.Id != savedWeave.Id), "delete only selected user bank item");
            // Every weft contributes one row at a time, with each pattern repeated to its common period.
            var weaveA = new WeavePattern(2, 2, new byte[] { 0, 255, 255, 0 });
            var weaveB = new WeavePattern(3, 1, new byte[] { 255, 0, 0 });
            var weaveC = new WeavePattern(1, 2, new byte[] { 0, 255 });
            var composite2 = CompositeWeave.Combine(new[] { weaveA, weaveB });
            Check(composite2.Width == 6 && composite2.Height == 4 && composite2.Values.SequenceEqual(new byte[] { 0,255,0,255,0,255, 255,0,0,255,0,0, 255,0,255,0,255,0, 255,0,0,255,0,0 }), "two-weft LCM interleave");
            var composite3 = CompositeWeave.Combine(new[] { weaveA, weaveB, weaveC });
            Check(composite3.Width == 6 && composite3.Height == 6 && composite3.Values.Take(18).SequenceEqual(new byte[] { 0,255,0,255,0,255, 255,0,0,255,0,0, 0,0,0,0,0,0 }), "three-weft row ordering");
            Check(CompositeWeave.Combine(CompositeWeave.Split(composite3, 3)).Values.SequenceEqual(composite3.Values), "split and recombine exactly");
            var editedComposite = new WeavePattern(composite3.Width, composite3.Height, (byte[])composite3.Values.Clone()); editedComposite.Values[1] = 0;
            Check(CompositeWeave.Combine(CompositeWeave.Split(editedComposite, 3)).Values.SequenceEqual(editedComposite.Values), "edited result survives splitting and recombination");
            var storedComposite = bank.SaveNew("ترکیبی", editedComposite, new[] { "#FF0000", "#00FF00", "#0000FF" }); var restoredComposite = reopenedBank.Load(out _).Single(x => x.Id == storedComposite.Id).Pattern;
            Check(restoredComposite.Width == editedComposite.Width && restoredComposite.Height == editedComposite.Height && restoredComposite.Values.SequenceEqual(editedComposite.Values), "composite bank persistence");
            Check(restoredComposite.WeftColors!.SequenceEqual(new[] { "#FF0000", "#00FF00", "#0000FF" }), "composite colors survive bank reload");
            var colorProjectSession = new WorkflowSession(a.Image); colorProjectSession.ApplySize(3, 2); colorProjectSession.ApplyColors(50);
            for (int i = 0; i < colorProjectSession.Patterns.Length; i++) colorProjectSession.AssignPattern(i, restoredComposite);
            string colorProject = Path.Combine(dir, "colored.sam");
            ProjectFile.Save(colorProject, new ProjectState(colorProjectSession, "sample.bmp", "3", "2", "50", "Patterns", false, 1, colorProjectSession.Patterns.Select(_ => (string?)"composite").ToArray()), Array.Empty<string>());
            Check(ProjectFile.Load(colorProject).Session.Patterns[0]!.WeftColors!.SequenceEqual(restoredComposite.WeftColors), "composite colors survive SAM v8");
            string colorV7 = Path.Combine(dir, "colored-v7.sam"); File.Copy(colorProject, colorV7);
            using (var zip = ZipFile.Open(colorV7, ZipArchiveMode.Update)) { var coloredManifestEntry = zip.GetEntry("manifest.json")!; string json; using (var r = new StreamReader(coloredManifestEntry.Open())) json = r.ReadToEnd(); coloredManifestEntry.Delete(); using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()); writer.Write(json.Replace("\"Version\":8", "\"Version\":7")); }
            Check(ProjectFile.Load(colorV7).Session.Patterns[0]!.WeftColors is null, "SAM v7 still loads without new visual metadata");
            Check(Math.Abs(YarnGeometry.ToTex(90, 2) - 10) < .0001 && Math.Abs(YarnGeometry.ToTex(100, 1) - 10) < .0001 && Math.Abs(YarnGeometry.ToTex(100, 4) - 10) < .0001 && Math.Abs(YarnGeometry.ToTex(59.0541, 3) - 10) < .0001, "yarn count conversions");
            Check(YarnGeometry.DiameterMm(40, 1) > YarnGeometry.DiameterMm(10, 1) && YarnGeometry.SpacingMm(40) < YarnGeometry.SpacingMm(20), "count and density affect thickness and spacing");
            Expect<ArgumentException>(() => YarnGeometry.ToTex(0, 3)); Expect<ArgumentException>(() => YarnGeometry.SpacingMm(double.NaN));
            Expect<ArgumentException>(() => CompositeWeave.Split(composite3, 4));
            Expect<ArgumentException>(() => CompositeWeave.Combine(Array.Empty<WeavePattern>()));
            Expect<ArgumentException>(() => CompositeWeave.Combine(new[] { new WeavePattern(2, 1, new byte[] { 0, 1 }) }));
            Expect<ArgumentException>(() => CompositeWeave.Combine(new[] { new WeavePattern(4093, 1, new byte[4093]), new WeavePattern(4091, 1, new byte[4091]) }));
            Expect<ArgumentException>(() => CompositeWeave.Combine(new[] { new WeavePattern(int.MaxValue, int.MaxValue, Array.Empty<byte>()) }));
            var compositeWindow = new CompositeWeaveWindow(bank, reopenedBank.Load(out _), false); compositeWindow.Close();
            var weaveEditor = new WeaveEditor(); weaveEditor.Reset(3, 2);
            Check(weaveEditor.IsDirty && weaveEditor.Snapshot().Values.SequenceEqual(Enumerable.Repeat((byte)255, 6)), "blank editor grid and dirty state");
            weaveEditor.SetPattern(new WeavePattern(2, 2, new byte[] {0,255,255,0})); var editorCopy = weaveEditor.Snapshot(); editorCopy.Values[0] = 255;
            Check(weaveEditor.Snapshot().Values[0] == 0 && !weaveEditor.IsDirty, "editor copies selected patterns");
            weaveEditor.Reset(65, 1); Check(weaveEditor.Snapshot().Values.Length == 65, "editor accepts composite wider than 64");
            Expect<ArgumentOutOfRangeException>(() => weaveEditor.Reset(4097, 1));
            Expect<ArgumentOutOfRangeException>(() => weaveEditor.Reset(1024, 1024));
            weaveEditor.Reset(8, 8); weaveEditor.ApplyHelper("NE");
            Check(weaveEditor.Snapshot().Values.Count(v => v == 0) == 8 && weaveEditor.CanUndo, "diagonal helper and undo history");
            var diagonal = weaveEditor.Snapshot().Values; weaveEditor.Undo(); Check(weaveEditor.Snapshot().Values.All(v => v == 255) && weaveEditor.CanRedo, "undo helper");
            weaveEditor.Redo(); Check(weaveEditor.Snapshot().Values.SequenceEqual(diagonal), "redo helper");
            weaveEditor.ApplyHelper("invert"); Check(!weaveEditor.CanRedo && weaveEditor.Snapshot().Values.Count(v => v == 0) == 56, "new edit clears redo");
            weaveEditor.ApplyHelper("plain"); Check(weaveEditor.Snapshot().Values.Count(v => v == 0) == 32, "plain weave helper");
            weaveEditor.MarkSaved(); var savedEditor = weaveEditor.Snapshot(); weaveEditor.ApplyHelper("invert"); weaveEditor.DiscardChanges();
            Check(!weaveEditor.IsDirty && !weaveEditor.CanUndo && weaveEditor.Snapshot().Values.SequenceEqual(savedEditor.Values), "discard restores saved editor state");
            var hit = PreviewHitTest.Find(pipeline.Palette!, 1.5, 1.0, 6, 4);
            Check(hit is { X: 1, Y: 1 } && hit.Value.ColorIndex == pipeline.Palette!.Indices[7], "hover maps scaled image coordinates to palette");
            Check(PreviewHitTest.Find(pipeline.Palette!, 6, 1, 6, 4) is null, "hover excludes outside edge");
            var savedSnapshot = pipeline.Snapshot(); var originalPattern = savedSnapshot.Patterns[0]; pipeline.AssignPattern(0, new WeavePattern(1, 1, new byte[] {255}));
            Check(ReferenceEquals(savedSnapshot.Patterns[0], originalPattern) && !ReferenceEquals(savedSnapshot.Patterns, pipeline.Patterns), "workflow snapshot survives reassignment");
            var rowEditor = new WeaveEditor(); rowEditor.SetPattern(new WeavePattern(4, 4, new byte[] {0,255,255,255, 0,255,255,255, 255,255,255,255, 255,255,255,255}));
            rowEditor.ApplyHelper("twillRight");
            Check(rowEditor.Snapshot().Values.SequenceEqual(new byte[] {0,0,255,255, 0,255,255,0, 255,255,0,0, 255,0,0,255}), "copy first warp upward");
            rowEditor.Undo(); rowEditor.ApplyHelper("twillLeft");
            Check(rowEditor.Snapshot().Values.SequenceEqual(new byte[] {0,255,255,0, 0,0,255,255, 255,0,0,255, 255,255,0,0}), "copy first warp downward");
            rowEditor.ApplyHelper("panama"); Check(rowEditor.Snapshot().Values.SequenceEqual(new byte[] {255,255,0,0, 255,255,0,0, 0,0,255,255, 0,0,255,255}), "four-quadrant Panama");
            rowEditor.Reset(6,2); rowEditor.ApplyHelper("panama"); Check(rowEditor.Snapshot().Values.SequenceEqual(new byte[] {255,255,255,0,0,0, 0,0,0,255,255,255}), "panama fills opposite quadrants on 6x2");
            rowEditor.Reset(2,6); rowEditor.ApplyHelper("panama"); Check(rowEditor.Snapshot().Values.SequenceEqual(new byte[] {255,0,255,0,255,0,0,255,0,255,0,255}), "panama fills opposite quadrants on 2x6");
            rowEditor.Reset(3,2); Expect<ArgumentException>(() => rowEditor.ApplyHelper("panama"));
            Expect<ArgumentException>(() => rowEditor.ApplyHelper("satin"));
            rowEditor.Reset(5,5); rowEditor.ApplyHelper("satin");
            Check(rowEditor.Snapshot().Values.Count(v => v == 0) == 5 && Enumerable.Range(0,5).All(y => rowEditor.Snapshot().Values[y*5 + (y*2)%5] == 0), "five end satin with step two");
            rowEditor.Reset(8,8); rowEditor.ApplyHelper("satin", 1, 1, 8, 3); Check(rowEditor.Snapshot().Values.Count(v => v == 0) == 8, "eight-end satin step three");
            Expect<ArgumentException>(() => rowEditor.ApplyHelper("satin", 1, 1, 8, 2));
            Check(NumberInput.TryInt("۱۲۳", out var persianValue) && persianValue == 123 && NumberInput.TryInt("١٢٣", out var arabicValue) && arabicValue == 123 && NumberInput.TryInt("1۲٣", out var mixedValue) && mixedValue == 123, "all digit forms normalize for numeric controls");
            Check(!NumberInput.TryInt("۱۲x", out _) && !NumberInput.TryInt("۹۹۹۹۹۹۹۹۹۹۹۹۹۹۹۹۹۹۹", out _), "invalid or overflow numbers rejected");
            var ranges = WarpRanges.Summarize(new WarpAnalysis(new[] {1,2,3,4,5,6,7,8}, 1, new double[] {-20,-30,0,10,40,-5,0,90}), 12, 2);
            Check(ranges.LooseCount == 2 && ranges.TightCount == 2 && ranges.Loose[0] == new WarpRange(12,13,20,30) && ranges.Tight[0] == new WarpRange(19,19,90,90) && ranges.Tight[1] == new WarpRange(15,16,10,40), "report exact non-neutral ranges, offsets and percentages");
            Check(WarpRanges.Summarize(new WarpAnalysis(new[] {0,0}, 0, new double[] {0,0}), 0).LooseCount == 0, "balanced warp ranges are empty");
            var analysisPixels = new byte[] {0,255,0, 0,255,0, 0,0,255, 255,0,255};
            var warp = DesignAnalysis.Analyze(analysisPixels, 3, 4);
            Check(warp.Counts.SequenceEqual(new[] {3,2,2}) && Math.Abs(warp.Mean - 7d/3) < 0.00001 && Math.Abs(warp.DeviationPercent[0] - 200d/7) < 0.00001, "warp count, mean and percent deviation");
            Check(DesignAnalysis.Analyze(Enumerable.Repeat((byte)255, 6).ToArray(), 3, 2).DeviationPercent.All(v => v == 0), "zero mean does not divide by zero");
            var verticalFloats = DesignAnalysis.FindFloats(analysisPixels, 3, 4, 2, FloatDirection.Warp);
            var horizontalFloats = DesignAnalysis.FindFloats(analysisPixels, 3, 4, 2, FloatDirection.Weft);
            Check(verticalFloats.Count == 5 && verticalFloats[0] == new FloatRun(0,0,3,true,FloatDirection.Warp), "maximal warp floats including edge");
            Check(horizontalFloats.Count == 1 && horizontalFloats[0] == new FloatRun(0,2,2,true,FloatDirection.Weft), "maximal weft floats");
            Check(DesignAnalysis.FindFloats(analysisPixels, 3, 4, 5, FloatDirection.Warp).Count == 0, "float threshold longer than line");
            var alternating = new byte[] {0,255,0,255};
            Check(DesignAnalysis.FindFloats(alternating, 4, 1, 1, FloatDirection.Weft).Count == 4 && DesignAnalysis.FindFloats(alternating, 4, 1, 2, FloatDirection.Weft).Count == 0, "single-cell runs and no edge wrapping");
            var floatPlan = FloatAutoCorrection.Plan(new[] { new FloatRun(11, 0, 3, true, FloatDirection.Warp), new FloatRun(10, 1, 2, false, FloatDirection.Weft), new FloatRun(7, 0, 3, true, FloatDirection.Weft), new FloatRun(12, 0, 2, true, FloatDirection.Warp) }, 10, 3, 3, new[] {12});
            Check(floatPlan.Points.SequenceEqual(new[] {new PixelPoint(1,1), new PixelPoint(0,1)}) && floatPlan.Skipped == 2, "float centers, even tie and reserved/edge exclusions");
            var autoSession = new WorkflowSession(image); autoSession.ApplySize(3,2); autoSession.ApplyColors(50);
            for (int i = 0; i < autoSession.Patterns.Length; i++) autoSession.AssignPattern(i, new WeavePattern(1,1,new byte[]{0}));
            var autoBefore = autoSession.Snapshot(); autoSession.TogglePixels(new[] {new PixelPoint(1,1), new PixelPoint(0,1), new PixelPoint(1,1)});
            Check(DesignAnalysis.ReadBinary(autoSession.PatternPreview!).SequenceEqual(new byte[]{0,0,0,255,255,0}) && DesignAnalysis.ReadBinary(autoBefore.PatternPreview!).All(v => v == 0), "batch correction toggles unique centers and keeps undo snapshot");
            var autoBytes = DesignAnalysis.ReadBinary(autoSession.PatternPreview!); Expect<ArgumentOutOfRangeException>(() => autoSession.TogglePixels(new[] {new PixelPoint(0,0),new PixelPoint(8,8)}));
            Check(DesignAnalysis.ReadBinary(autoSession.PatternPreview!).SequenceEqual(autoBytes), "invalid batch is atomic");
            string autoFile = Path.Combine(dir,"auto-floats.sam"); ProjectFile.Save(autoFile, new ProjectState(autoSession,"sample.bmp","3","2","50","Floats",false,1,autoSession.Patterns.Select(_ => (string?)"pattern").ToArray()), new[] {bmpPath});
            Check(DesignAnalysis.ReadBinary(ProjectFile.Load(autoFile).Session.PatternPreview!).SequenceEqual(autoBytes), "automatic float corrections survive SAM");
            Check(DesignAnalysis.ReadBinary(autoSession.BasePatternPreview!).All(v => v == 0), "float corrections do not alter previous pattern stage");
            var autoRestored = ProjectFile.Load(autoFile).Session;
            Check(DesignAnalysis.ReadBinary(autoRestored.BasePatternPreview!).All(v => v == 0) && DesignAnalysis.ReadBinary(autoRestored.PatternPreview!).SequenceEqual(autoBytes), "SAM reconstructs distinct base and corrected stage previews");
            Check(WarpDisplay.Deviation(0, false) == "متعادل" && !WarpDisplay.Deviation(-4, true).Contains("tightness") && WarpDisplay.Percentage(.001) == "<0.01%", "balanced and nonzero warp labels");
            Check(PositionNumbers.ToStored("۱") == "0" && PositionNumbers.ToStored("۲۰") == "19" && PositionNumbers.ToDisplay("0") == "1" && PositionNumbers.ToStored("۰") == "-1", "one-based UI maps to zero-based storage without an empty first hook");
            Check(PositionNumbers.ListToStored("۱،۳") == "0,2" && PositionNumbers.ListToDisplay("0,2") == "1,3", "legacy reserved hooks use one-based UI");
            var beforeWeft = MachineComposer.BeforeWeft(new MachineSetup(LeftWidth:"1", RightWidth:"1", LeftPattern:new WeavePattern(1,1,new byte[]{0}), RightPattern:new WeavePattern(1,1,new byte[]{255}), BoxEnabled:true, AbsoluteLayout:true, TotalHooks:"30", DesignStart:"20", WeftStart:"1", WeftCount:"3"));
            var earlyMap = MachineComposer.Compose(image, beforeWeft);
            Check(earlyMap.Image.PixelWidth == 5 && earlyMap.DesignStart == 1 && earlyMap.SignalHooks.Length == 0, "pre-weft stages exclude later absolute layout and signal columns");
            var simSettings = new SimulationSettings(1, 0, "#۱۲3ABC", "#AABBCC", 2, 75);
            var sim = FabricSimulation.Render(image, simSettings);
            Check(sim.PixelWidth == 48 && sim.PixelHeight == 32 && sim.IsFrozen && DesignAnalysis.ReadBinary(image).SequenceEqual(pixels), "simulation is bounded, immutable and leaves machine bitmap unchanged");
            Expect<ArgumentException>(() => FabricSimulation.Render(image, simSettings with { WarpColor = "bad" }));
            string simFile = Path.Combine(dir, "simulation.sam");
            ProjectFile.Save(simFile, new ProjectState(autoSession,"sample.bmp","3","2","50","Simulation",false,1,autoSession.Patterns.Select(_ => (string?)"pattern").ToArray(), Simulation:simSettings), Array.Empty<string>());
            var simLoaded = ProjectFile.Load(simFile); Check(simLoaded.Stage == "Simulation" && simLoaded.Simulation == simSettings, "SAM v7 simulation settings round trip");
            var weftCount = new WeftSettings(BoxEnabled:true, WeftCount:"3", Sequence:"3,1,2");
            Check(FabricSimulation.WeftIds(weftCount).SequenceEqual(new[]{"1","2","3"}), "simulation defines one yarn per applied weft");
            var multiSettings = simSettings with { Wefts = new[] { new SimulatedYarn("1", 1, "#FF0000"), new SimulatedYarn("2", 2, "#00FF00"), new SimulatedYarn("3", 0, "#0000FF") } };
            var whiteMap = BitmapSource.Create(1, 2, 96, 96, PixelFormats.Gray8, null, new byte[]{255,255}, 1);
            var multiImage = FabricSimulation.Render(whiteMap, multiSettings with { Weave = 0 }, new[]{"1","2"});
            var simColors = new byte[multiImage.PixelWidth * multiImage.PixelHeight * 4]; multiImage.CopyPixels(simColors, multiImage.PixelWidth * 4, 0);
            int redIndex = (8 * multiImage.PixelWidth + 8) * 4, greenIndex = (24 * multiImage.PixelWidth + 8) * 4;
            Check(simColors[redIndex+2] > simColors[redIndex+1] && simColors[greenIndex+1] > simColors[greenIndex+2], "applied row sequence chooses the corresponding weft color");
            string multiFile = Path.Combine(dir, "multi-weft.sam");
            ProjectFile.Save(multiFile, new ProjectState(autoSession,"sample.bmp","3","2","50","Simulation",false,1,autoSession.Patterns.Select(_ => (string?)"pattern").ToArray(), Simulation:multiSettings), Array.Empty<string>());
            var multiRestored = ProjectFile.Load(multiFile); Check(multiRestored.Simulation!.Wefts!.SequenceEqual(multiSettings.Wefts!), "multi-weft yarns survive SAM v7");
            string simV6 = Path.Combine(dir, "simulation-v6.sam"); File.Copy(multiFile, simV6);
            using (var zip = ZipFile.Open(simV6, ZipArchiveMode.Update))
            {
                var manifest = zip.GetEntry("manifest.json")!; string json; using (var reader = new StreamReader(manifest.Open())) json = reader.ReadToEnd();
                manifest.Delete(); var replacement = zip.CreateEntry("manifest.json"); using var writer = new StreamWriter(replacement.Open()); writer.Write(json.Replace("\"Version\":8", "\"Version\":6"));
            }
            Check(ProjectFile.Load(simV6).Simulation!.Wefts is null, "SAM v6 uses its original single-weft fallback");
            var invalidPosition = new MachineSetup(BoxEnabled:true, AbsoluteLayout:true, DesignStart:"3", WeftStart:PositionNumbers.ToStored("0"), WeftCount:"1"); Expect<ArgumentException>(() => MachineComposer.Compose(image, invalidPosition));
            var firstHook = MachineComposer.Compose(image, invalidPosition with { WeftStart = PositionNumbers.ToStored("1") });
            Check(firstHook.SignalHooks.SequenceEqual(new[]{0}) && DesignAnalysis.ReadBinary(firstHook.Image)[0] == 0, "weft one occupies the first physical hook");
            Check(PreviewViewport.ZoomOffset(100, 50, 2) == 150 && PreviewViewport.PanOffset(120, 40, 70) == 90 && PreviewViewport.PanOffset(1, 0, 20) == 0, "anchored zoom and pan geometry");
            byte beforeToggle = DesignAnalysis.ReadBinary(bankSession.PatternPreview!)[0];
            var historySnapshot = bankSession.Snapshot(); bankSession.TogglePixel(0,0);
            Check(DesignAnalysis.ReadBinary(bankSession.PatternPreview!)[0] != beforeToggle && DesignAnalysis.ReadBinary(historySnapshot.PatternPreview!)[0] == beforeToggle, "edited pixel and history snapshot are independent");
            var changedPixel = DesignAnalysis.ReadBinary(bankSession.PatternPreview!)[0];
            var info = new DesignMetadata("طراح نمونه", "پارچه", "شرح طرح");
            string editedPath = Path.Combine(dir,"edited.sam");
            ProjectFile.Save(editedPath, new ProjectState(bankSession, "sample.bmp", "3", "2", "50", "Floats", false, 1, bankSession.Patterns.Select(_ => (string?)"pattern").ToArray(), null, info), new[] {bmpPath});
            var edited = ProjectFile.Load(editedPath);
            Check(edited.Metadata == info && edited.Stage == "Floats" && DesignAnalysis.ReadBinary(edited.Session.PatternPreview!)[0] == changedPixel, "SAM metadata and edited pixel round-trip");
            bankSession.AssignPattern(0, new WeavePattern(1,1,new byte[]{255}));
            Check(DesignAnalysis.ReadBinary(bankSession.PatternPreview!)[0] == changedPixel, "individual edits survive weave reassignment");
            string editedImage = Path.Combine(dir,"edited.bmp"); ImageExporter.Save(edited.Session.PatternPreview!, editedImage, false, new[] {bmpPath});
            Check(DesignAnalysis.ReadBinary(ImageLoader.Load(editedImage, 0).Image)[0] == changedPixel, "edited pixel exported to bitmap");
            string editedMachine = Path.Combine(dir, "edited.jc5"); Jc5Codec.Save(edited.Session.PatternPreview!, editedMachine, new[] {bmpPath});
            Check(DesignAnalysis.ReadBinary(Jc5Codec.Load(editedMachine)).SequenceEqual(DesignAnalysis.ReadBinary(edited.Session.PatternPreview!)), "edited pixels exported to JC5");
            var edgeLeft = new WeavePattern(2, 2, new byte[] {0,255,255,0}); var edgeRight = new WeavePattern(1,1,new byte[] {0});
            var setup = new MachineSetup("2", "1", edgeLeft, edgeRight, "left", "right", true, 1, "2", "۱=۱۰\n۲=۰۱", "۱،۲");
            var machineMap = MachineComposer.Compose(image, setup);
            Check(machineMap.Image.PixelWidth == 8 && machineMap.SignalHooks.SequenceEqual(new[] {6,7}) && machineMap.RowWefts.SequenceEqual(new[] {"1","2"}), "selvedge and configurable box motion dimensions and sequence");
            Check(DesignAnalysis.ReadBinary(machineMap.Image).SequenceEqual(new byte[] {0,255,0,255,255,0,0,255, 255,0,255,0,0,0,255,0}), "independent expected logical hook pixels");
            var encodedMachine = Jc5Codec.Encode(machineMap.Image, "test.jc5");
            Check(DesignAnalysis.ReadBinary(Jc5Codec.Decode(encodedMachine)).SequenceEqual(DesignAnalysis.ReadBinary(machineMap.Image)), "selvedge and signals survive JC5 encoding");
            var bottomMap = MachineComposer.Compose(image, setup with { StartFromBottom = true });
            Check(bottomMap.RowWefts.SequenceEqual(new[] {"2","1"}) && DesignAnalysis.ReadBinary(bottomMap.Image)[6] == 255, "weft sequence origin selectable");
            var reservedMap = MachineComposer.Compose(image, setup with { BoxPlacement = 2, BoxHooks = "0,5" });
            Check(reservedMap.SignalHooks.SequenceEqual(new[] {0,5}) && reservedMap.Image.PixelWidth == 6, "reserved existing hooks do not expand width");
            Expect<ArgumentException>(() => MachineComposer.Compose(image, setup with { LeftPattern = null }));
            Expect<ArgumentException>(() => MachineComposer.Compose(image, setup with { BoxPlacement = 2, BoxHooks = "0,0" }));
            Expect<ArgumentException>(() => MachineComposer.Compose(image, setup with { BoxCodes = "1=1" }));
            Expect<ArgumentException>(() => MachineComposer.Compose(image, setup with { Sequence = "99" }));
            Expect<ArgumentException>(() => MachineComposer.Compose(image, setup with { LeftWidth = "4000000" }));
            var absolute = new MachineSetup("8", "2", edgeLeft, edgeRight, "left", "right", true, AbsoluteLayout: true, TotalHooks: "30", DesignStart: "20", WeftStart: "1", WeftCount: "3");
            var positioned = MachineComposer.Compose(image, absolute); var positionedPixels = DesignAnalysis.ReadBinary(positioned.Image);
            Check(positioned.DesignStart == 20 && positioned.FabricStart == 12 && positioned.FabricWidth == 13, "absolute composite fabric geometry");
            var fabricMap = CompositionView.Fabric(positioned); Check(fabricMap.PixelWidth == 13 && DesignAnalysis.ReadBinary(fabricMap)[0] == 0, "fabric crop includes selvedges but excludes signals and padding");
            var redSource = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels.SelectMany(v => v == 0 ? new byte[] {0,0,255,255} : new byte[] {255,255,255,255}).ToArray(), 12);
            Check(DesignAnalysis.ReadBinary(MachineComposer.Compose(redSource, absolute, true).Image).SequenceEqual(positionedPixels), "red-white input composes same signal and selvedge bits");
            Expect<InvalidDataException>(() => MachineComposer.Compose(redSource, absolute, false));
            var redView = CompositionView.RedPreview(positioned.Image); var redBytes = new byte[30 * 2 * 4]; redView.CopyPixels(redBytes, 30 * 4, 0);
            Check(redBytes[4] == 0 && redBytes[5] == 0 && redBytes[6] == 255 && redBytes[7] == 255 && redBytes[0] == 255, "red preview marks active weft while preserving white");
            Check(DesignAnalysis.ReadBinary(Jc5Codec.Decode(Jc5Codec.Encode(positioned.Image, "test.jc5"))).SequenceEqual(positionedPixels), "red preview leaves machine bits unchanged");
            var fabricStats = DesignAnalysis.Analyze(DesignAnalysis.ReadBinary(fabricMap), fabricMap.PixelWidth, fabricMap.PixelHeight); var extremes = CompositionView.Extremes(fabricStats, 0, fabricMap.PixelWidth);
            Check(extremes.Looseness >= 0 && extremes.Tightness >= 0, "bounded relative percentage extremes");
            Check(positioned.Image.PixelWidth == 30 && positioned.SignalHooks.SequenceEqual(new[] {1,2,3}), "absolute total and weft columns");
            Check(positionedPixels[0] == 255 && positionedPixels[1] == 0 && positionedPixels[2] == 255 && positionedPixels[3] == 255 && positionedPixels.Skip(4).Take(8).All(v => v == 255), "first one-hot weft and white gap");
            Check(positionedPixels[12] == 0 && positionedPixels[19] == 255 && positionedPixels[20] == 0 && positionedPixels[21] == 255 && positionedPixels[23] == 0 && positionedPixels.Skip(25).Take(5).All(v => v == 255), "left edge at 12–19, design at 20 and white trailing hooks");
            Check(positionedPixels[30+2] == 0 && positionedPixels[30+1] == 255, "second row selects second weft");
            Expect<ArgumentException>(() => MachineComposer.Compose(image, absolute with { WeftStart = "10" }));
            Expect<ArgumentException>(() => MachineComposer.Compose(image, absolute with { TotalHooks = "24" }));
            var draft = WeftSettings.FromMachine(absolute) with { WeftStart = "۱۰", WeftCount = "۳", Sequence = "۱،۲،۳" };
            Check(absolute.WeftStart == "1" && draft.ApplyTo(absolute).WeftStart == "۱۰", "weft draft does not mutate applied settings");
            Expect<ArgumentException>(() => MachineComposer.Compose(image, draft.ApplyTo(absolute)));
            Check(DesignAnalysis.ReadBinary(MachineComposer.Compose(image, absolute).Image).SequenceEqual(positionedPixels), "failed draft cannot alter applied output");
            var draftFile = Path.Combine(dir, "draft.sam");
            ProjectFile.Save(draftFile, new ProjectState(bankSession,"sample.bmp","۳","۲","۵۰","Weft",false,1,bankSession.Patterns.Select(_ => (string?)"pattern").ToArray(),null,null,absolute,draft), new[] {bmpPath});
            var draftLoaded = ProjectFile.Load(draftFile); Check(draftLoaded.Machine!.WeftStart == "1" && draftLoaded.DraftWeft == draft && draftLoaded.WidthInput == "۳", "SAM persists applied settings and Persian draft independently");
            var stripMap = MachineComposer.Compose(image, new MachineSetup(AbsoluteLayout:true, TotalHooks:"7", DesignStart:"2"));
            var strip = CompositionView.HeatStrip(new WarpAnalysis(new[] {0,1,2},1,new double[] {-100,0,100}), stripMap); var stripBytes = new byte[28]; strip.CopyPixels(stripBytes, 28, 0);
            Check(strip.PixelWidth == 7 && strip.PixelHeight == 1 && stripBytes[0] == 120 && stripBytes[8] == 240 && stripBytes[10] == 70 && stripBytes[12] == 240 && stripBytes[16] == 70 && stripBytes[18] == 240 && stripBytes[24] == 120, "heat strip colors, fabric alignment and excluded padding");
            var absoluteFile = Path.Combine(dir,"positioned.sam"); ProjectFile.Save(absoluteFile, new ProjectState(bankSession,"sample.bmp","3","2","50","Weft",false,1,bankSession.Patterns.Select(_ => (string?)"pattern").ToArray(),null,null,absolute), new[] {bmpPath});
            var absoluteLoaded = ProjectFile.Load(absoluteFile).Machine!; Check(absoluteLoaded.AbsoluteLayout && absoluteLoaded.TotalHooks == "30" && absoluteLoaded.WeftStart == "1" && DesignAnalysis.ReadBinary(MachineComposer.Compose(image, absoluteLoaded).Image).SequenceEqual(positionedPixels), "absolute layout persists and reproduces map");
            var v4File = Path.Combine(dir, "legacy-v4.sam"); File.Copy(absoluteFile, v4File);
            using (var zip = ZipFile.Open(v4File, ZipArchiveMode.Update))
            {
                var item = zip.GetEntry("manifest.json")!; string json; using (var reader = new StreamReader(item.Open())) json = reader.ReadToEnd();
                item.Delete(); var replacement = zip.CreateEntry("manifest.json"); using var writer = new StreamWriter(replacement.Open()); writer.Write(json.Replace("\"Version\":8", "\"Version\":4"));
            }
            var oldV4 = ProjectFile.Load(v4File); Check(oldV4.DraftWeft is null && oldV4.Machine!.AbsoluteLayout && DesignAnalysis.ReadBinary(MachineComposer.Compose(image, oldV4.Machine).Image).SequenceEqual(positionedPixels), "SAM v4 absolute settings remain applied");
            var composedState = new ProjectState(bankSession,"sample.bmp","3","2","50","Machine",false,1,bankSession.Patterns.Select(_ => (string?)"pattern").ToArray(),null,null,setup);
            string machineProject = Path.Combine(dir,"machine-setup.sam"); ProjectFile.Save(machineProject, composedState, new[] {bmpPath});
            var restoredSetup = ProjectFile.Load(machineProject).Machine!;
            Check(restoredSetup.LeftPattern!.Values.SequenceEqual(edgeLeft.Values) && restoredSetup.RightPattern!.Values.SequenceEqual(edgeRight.Values) && restoredSetup.BoxCodes == setup.BoxCodes, "SAM embeds both selvedges and signal setup");
            Check(DesignAnalysis.ReadBinary(MachineComposer.Compose(image, restoredSetup).Image).SequenceEqual(DesignAnalysis.ReadBinary(machineMap.Image)), "restored machine setup produces identical map");
            Check(ProjectSavePolicy.RequiresDialog(null, false) && !ProjectSavePolicy.RequiresDialog("project.sam", false) && ProjectSavePolicy.RequiresDialog("project.sam", true), "save existing vs save as policy");
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
            var fiftyImage = BitmapSource.Create(50, 1, 96, 96, PixelFormats.Bgra32, null, many.Take(200).ToArray(), 200);
            var fullProject = new WorkflowSession(fiftyImage); fullProject.ApplySize(50,1); fullProject.ApplyColors(50);
            Check(fullProject.Palette!.Colors.Length == 50, "maximum palette fixture");
            for (int i = 0; i < 50; i++) fullProject.AssignPattern(i, new WeavePattern(1,1,new byte[] {0}));
            fullProject.TogglePixel(0,0); string fullProjectPath = Path.Combine(dir,"full-50.sam");
            ProjectFile.Save(fullProjectPath, new ProjectState(fullProject,"many.bmp","50","1","50","Floats",false,1,Enumerable.Repeat<string?>("pattern",50).ToArray(), null, null, setup), Array.Empty<string>());
            Check(ProjectFile.Load(fullProjectPath).Session.PixelOverrides![0] == 2 && ProjectFile.Load(fullProjectPath).Machine?.LeftPattern is not null, "50 weaves, overrides and two selvedges fit SAM entry limit");
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
            Check(AppVersion.Display == "020.6.0.0-a1", "QAVNS version from version.txt");
            var reportEntries = bankSession.Palette!.Colors.Select((color, index) => { var e = new PaletteEntry { Index = index, ColorCode = $"#{color:X6}", PixelCount = bankSession.Palette.Counts[index], Percent = 100d * bankSession.Palette.Counts[index] / bankSession.Palette.Indices.Length, Brush = new SolidColorBrush(Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color)) }; if (bankSession.Patterns[index] is { } p) e.Restore("sample", p); return e; }).ToArray();
            var fullEntries = fullProject.Palette!.Colors.Select((color, index) => { var e = new PaletteEntry { Index = index, ColorCode = $"#{color:X6}", PixelCount = fullProject.Palette.Counts[index], Percent = 100d * fullProject.Palette.Counts[index] / fullProject.Palette.Indices.Length, Brush = new SolidColorBrush(Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color)) }; e.Restore("sample", fullProject.Patterns[index]!); return e; }).ToArray();
            var fullMap = MachineComposer.Compose(fullProject.PatternPreview!, new MachineSetup("2", "1", edgeLeft, edgeRight, "left", "right", AbsoluteLayout: true, TotalHooks: "54", DesignStart: "3"));
            var fullFabric = CompositionView.Fabric(fullMap); var fullAnalysis = DesignAnalysis.Analyze(DesignAnalysis.ReadBinary(fullFabric), fullFabric.PixelWidth, fullFabric.PixelHeight);
            var fullPage = (System.Windows.Controls.Grid)DesignReport.CreatePage(fullProject, fullEntries, "پنجاه رنگ", null, false, machine: new MachineSetup("2", "1", edgeLeft, edgeRight), analysis: fullAnalysis, composition: fullMap);
            DesignReport.Render(fullPage);
            var reportBody = fullPage.Children.OfType<System.Windows.Controls.Grid>().Single(x => System.Windows.Controls.Grid.GetRow(x) == 3);
            var reportColumns = reportBody.Children.OfType<System.Windows.Controls.Grid>().Single();
            var reportRows = reportColumns.Children.OfType<System.Windows.Controls.StackPanel>().SelectMany(x => x.Children.OfType<System.Windows.Controls.Grid>()).ToArray();
            Check(reportRows.Length == 50 && reportRows.All(r => r.TransformToAncestor(fullPage).TransformBounds(new System.Windows.Rect(r.RenderSize)).Bottom <= fullPage.ActualHeight - 35), "all fifty report rows fit above footer with range section");
            Check(DesignReport.PersianDate(new DateTimeOffset(2024, 3, 20, 12, 0, 0, TimeSpan.Zero)) == "1403/01/01", "solar Hijri report date");
            Check(DesignReport.PersianDate(null).Contains("نامشخص"), "legacy creation date not invented");
            var reportImage = DesignReport.Render(DesignReport.CreatePage(bankSession, reportEntries, "نمونه", new DateTimeOffset(2024, 3, 20, 12, 0, 0, TimeSpan.Zero), false, machine: absolute, analysis: fabricStats, composition: positioned));
            Check(reportImage.PixelWidth == 1240 && reportImage.PixelHeight == 1754, "single A4 report renders");
            string reportPath = Path.Combine(dir, "report.pdf"); DesignReport.SavePdf(reportImage, reportPath, new[] {bmpPath});
            byte[] pdfBytes = File.ReadAllBytes(reportPath); string pdfText = System.Text.Encoding.Latin1.GetString(pdfBytes);
            Check(pdfText.StartsWith("%PDF-1.4") && pdfText.Contains("/Count 1") && pdfText.Contains("/DCTDecode") && pdfText.TrimEnd().EndsWith("%%EOF"), "single-page PDF created");
            Expect<IOException>(() => DesignReport.SavePdf(reportImage, reportPath, new[] {reportPath}));
            var stamped = new ProjectState(bankSession, "sample.bmp", "3", "2", "50", "Patterns", false, 1, bankSession.Patterns.Select(_ => (string?)"sample").ToArray(), new DateTimeOffset(2024, 3, 20, 12, 0, 0, TimeSpan.Zero));
            string datedPath = Path.Combine(dir, "dated.sam"); ProjectFile.Save(datedPath, stamped, new[] {bmpPath});
            Check(ProjectFile.Load(datedPath).CreatedAt == stamped.CreatedAt, "project creation timestamp retained");
            var reportIds = Enumerable.Range(1, 50).Select(x => x.ToString()).ToArray();
            var simulationPages = DesignReport.CreateSimulationPages(multiImage, multiSettings, reportIds, false);
            Check(simulationPages.Count == 2, "all weft yarn metadata is paginated");
            var secondPage = DesignReport.Render(simulationPages[0]); string multiPdf = Path.Combine(dir, "multi-report.pdf");
            DesignReport.SavePdf(new[]{reportImage, secondPage}, multiPdf, Array.Empty<string>());
            Check(System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(multiPdf)).Contains("/Count 2"), "multi-page simulation PDF");
            var pdfImport = ImageLoader.Load(multiPdf, 1); Check(pdfImport.Format == "PDF" && pdfImport.PageCount == 2 && pdfImport.PageIndex == 1, "PDF page rasterization");
            string compatibleAi = Path.Combine(dir, "compatible.ai"); File.Copy(multiPdf, compatibleAi); Check(ImageLoader.Load(compatibleAi, 0).Format == "AI (PDF compatible)", "PDF-compatible AI input");
            Expect<ArgumentOutOfRangeException>(() => ImageLoader.Load(multiPdf, 2));
            var widePattern = new WeavePattern(33, 2, Enumerable.Repeat((byte)255, 66).ToArray());
            var wideWindow = new Fabric3DWindow(Fabric3DWindow.PatternBitmap(widePattern), Array.Empty<string>(), new SimulationSettings(), true, new[] { "#FF0000", "#0000FF" });
            var wideRoot = (System.Windows.Controls.DockPanel)wideWindow.Content; var paging = (System.Windows.Controls.StackPanel)wideRoot.Children[1];
            ((System.Windows.Controls.Button)paging.Children[1]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(((System.Windows.Controls.TextBlock)wideRoot.Children[0]).Text.Contains("33–33 of 33"), "3D page navigation reaches last column");
            var viewport3d = (HelixToolkit.Wpf.HelixViewport3D)wideRoot.Children[3]; double beforeFlip = ((System.Windows.Media.Media3D.PerspectiveCamera)viewport3d.Camera!).Position.Z;
            ((System.Windows.Controls.Button)paging.Children[4]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(beforeFlip > 0 && ((System.Windows.Media.Media3D.PerspectiveCamera)viewport3d.Camera!).Position.Z < 0, "3D front/back flips camera");
            var colorModel = (System.Windows.Media.Media3D.Model3DGroup)((System.Windows.Media.Media3D.ModelVisual3D)viewport3d.Children[1]).Content!;
            var row1 = (System.Windows.Media.Media3D.GeometryModel3D)colorModel.Children[1]; var row2 = (System.Windows.Media.Media3D.GeometryModel3D)colorModel.Children[2];
            var redMaterial = (System.Windows.Media.Media3D.DiffuseMaterial)((System.Windows.Media.Media3D.MaterialGroup)row1.Material!).Children[0];
            var blueMaterial = (System.Windows.Media.Media3D.DiffuseMaterial)((System.Windows.Media.Media3D.MaterialGroup)row2.Material!).Children[0];
            Check(((System.Windows.Media.SolidColorBrush)redMaterial.Brush).Color == Colors.Red && ((System.Windows.Media.SolidColorBrush)blueMaterial.Brush).Color == Colors.Blue, "3D preserves composite weft colors");
            var oldMesh = (System.Windows.Media.Media3D.MeshGeometry3D)((System.Windows.Media.Media3D.GeometryModel3D)colorModel.Children[0]).Geometry!; double diameterBefore = Math.Abs(oldMesh.Positions[0].X - oldMesh.Positions[4].X);
            var controls3d = (System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)wideRoot.Children[2]).Content; var countInput = controls3d.Children.OfType<System.Windows.Controls.TextBox>().First(); countInput.Text = "120";
            var apply3d = controls3d.Children.OfType<System.Windows.Controls.Button>().Single(x => (string)x.Content == "Apply settings"); apply3d.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var newModel = (System.Windows.Media.Media3D.Model3DGroup)((System.Windows.Media.Media3D.ModelVisual3D)viewport3d.Children[1]).Content!; var newMesh = (System.Windows.Media.Media3D.MeshGeometry3D)((System.Windows.Media.Media3D.GeometryModel3D)newModel.Children[0]).Geometry!;
            Check(Math.Abs(newMesh.Positions[0].X - newMesh.Positions[4].X) > diameterBefore * 1.9, "3D geometry responds to yarn count"); wideWindow.Close();
            var detailWindow = new Fabric3DWindow(whiteMap, new[]{"1","2"}, multiSettings with { Weave = 0 }, false);
            Check(detailWindow.Content is System.Windows.Controls.DockPanel, "Helix 3D detail window builds bounded geometry");
            var guide = new GuideWindow(false); Check(guide.Content is System.Windows.Controls.Grid && guide.Title.Contains("راهنمای"), "illustrated in-app guide loads with navigation");
            var window = new MainWindow();
            Check(window.FontFamily.Source.Contains("Vazirmatn"), "embedded font selected in main window");
            var pendingTotal = (System.Windows.Controls.TextBox)window.FindName("TotalHooksInput"); pendingTotal.Text = "۳۰";
            var readSetup = typeof(MainWindow).GetMethod("ReadMachineSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Check(((MachineSetup)readSetup.Invoke(window, null)!).TotalHooks == "" && window.FindName("ApplyWeftButton") is System.Windows.Controls.Button, "typing weft draft does not apply it");
            var loadMachineSettings = typeof(MainWindow).GetMethod("LoadMachineSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            loadMachineSettings.Invoke(window, new object?[] { absolute, null });
            Check(((System.Windows.Controls.TextBox)window.FindName("DesignStartInput")).Text == "21" && ((System.Windows.Controls.TextBox)window.FindName("WeftStartInput")).Text == "2", "old zero-based applied locations display without changing the stored map");
            var loadSimSettings = typeof(MainWindow).GetMethod("LoadSimulationSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            loadSimSettings.Invoke(window, new object?[] { multiSettings });
            Check(((System.Windows.Controls.StackPanel)window.FindName("SimulationWeftRows")).Children.Count == 3, "simulation editor has exactly the applied weft count");
            ((System.Windows.Controls.TextBox)window.FindName("WeftStartInput")).Text = "۱";
            var readDraft = typeof(MainWindow).GetMethod("ReadWeftDraft", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Check(((WeftSettings)readDraft.Invoke(window, null)!).WeftStart == "0", "first displayed weft maps to actual first physical hook");
            var cardEntry = new PaletteEntry(); cardEntry.Restore("card", new WeavePattern(2,2,new byte[]{0,255,255,0}));
            cardEntry.ShowGrid = false; Check(ReferenceEquals(cardEntry.DisplayPatternPreview, cardEntry.PatternPreview), "pattern card hides grid");
            cardEntry.ShowGrid = true; Check(ReferenceEquals(cardEntry.DisplayPatternPreview, cardEntry.PatternGridPreview), "pattern card displays grid");
            cardEntry.Restore("wide", new WeavePattern(200, 2, Enumerable.Repeat((byte)0, 400).ToArray()));
            Check(cardEntry.PatternGridPreview is System.Windows.Media.DrawingImage && cardEntry.PatternPreview!.PixelWidth == 200, "large weave card uses bounded major grid lines without altering bitmap");
            var nav = (System.Windows.Controls.Grid)window.FindName("Navigation");
            var settings = (System.Windows.FrameworkElement)window.FindName("SettingsBorder");
            Check(window.FindName("CompositeButton") is System.Windows.Controls.Button && window.FindName("Bank3DButton") is System.Windows.Controls.Button && window.FindName("Editor3DButton") is System.Windows.Controls.Button && window.FindName("Simulation3DButton") is null, "3D and composite controls belong to design, not simulation");
            Check(System.Windows.Controls.Grid.GetColumn((System.Windows.FrameworkElement)window.FindName("WeftStepButton")) == 7 && System.Windows.Controls.Grid.GetColumn((System.Windows.FrameworkElement)window.FindName("SimulationStepButton")) == 8 && System.Windows.Controls.Grid.GetColumn((System.Windows.FrameworkElement)window.FindName("MachineStepButton")) == 9 && window.FindName("HelpButton") is System.Windows.Controls.Button, "weft, simulation, machine and in-app help are ordered");
            Check(nav.FlowDirection == System.Windows.FlowDirection.RightToLeft && System.Windows.Controls.Grid.GetColumn(settings) == 2, "right-to-left workflow and right settings panel");
            var list = (System.Windows.Controls.ListView)window.FindName("ColorPaletteList");
            Check(list.View is System.Windows.Controls.GridView grid && grid.Columns.Count == 4, "four-column color statistics table");
            Check(list.SelectionMode == System.Windows.Controls.SelectionMode.Multiple, "colors can be selected directly without modifier keys");
            list.ItemsSource = new[] { new PaletteEntry { ColorCode = "#000000" }, new PaletteEntry { ColorCode = "#FFFFFF" } };
            list.SelectedItems.Add(list.Items[0]); list.SelectedItems.Add(list.Items[1]);
            Check(list.SelectedItems.Count == 2, "two colors can be selected at once");
            Check(window.FindName("BankWorkspace") is System.Windows.FrameworkElement && window.FindName("WeaveCanvas") is WeaveEditor && window.FindName("BankButton") is System.Windows.Controls.Button, "embedded weave bank and drawing section");
            Check(window.FindName("SaveProjectAsButton") is System.Windows.Controls.Button && window.FindName("ThemeButton") is System.Windows.Controls.Button, "save as and theme controls");
            Check(window.FindName("UndoButton") is System.Windows.Controls.Button && window.FindName("RedoButton") is System.Windows.Controls.Button && window.FindName("ReportButton") is System.Windows.Controls.Button && window.FindName("HoverCard") is System.Windows.Controls.Border, "history, report and hover controls");
            Check(window.FindName("FloatAutoButton") is System.Windows.Controls.Button && window.FindName("AnalysisStripImage") is System.Windows.Controls.Image && window.FindName("HoverSwatch") is System.Windows.Controls.Border, "automatic float, heat strip and color swatch controls");
            var overlay = new PixelGridOverlay { PixelsWide = 10, PixelsHigh = 10, Zoom = 1, VisibleImage = new System.Windows.Rect(0,0,10,10) };
            overlay.Measure(new System.Windows.Size(10,10)); overlay.Arrange(new System.Windows.Rect(0,0,10,10)); Check(overlay.GridStep() == 4, "low-zoom grid uses labelled major intervals");
            overlay.Zoom = 8; Check(overlay.GridStep() == 1, "zoomed grid uses individual pixels");
            Check(window.FindName("GridToggle") is System.Windows.Controls.CheckBox && window.FindName("GridOverlay") is PixelGridOverlay && ReferenceEquals(System.Windows.Media.VisualTreeHelper.GetParent((System.Windows.DependencyObject)window.FindName("AnalysisStripImage")), window.FindName("PreviewContent")), "view-only grid and heat strip in shared zoom and scroll content");
            Check(window.FindName("InfoButton") is System.Windows.Controls.Button && window.FindName("AnalysisPanel") is System.Windows.Controls.ScrollViewer && window.FindName("FloatPanel") is System.Windows.Controls.ScrollViewer, "design info, analysis and float controls");
            Check(window.FindName("SelvedgePanel") is System.Windows.Controls.ScrollViewer && window.FindName("WeftPanel") is System.Windows.Controls.ScrollViewer && window.FindName("MachinePreviewButton") is System.Windows.Controls.Button, "integrated machine selvedge and box settings");
            var title = (System.Windows.Controls.TextBlock)window.FindName("WindowTitleText");
            Check(System.Windows.Controls.Grid.GetColumn(title) == 0 && System.Windows.Controls.Grid.GetColumn((System.Windows.FrameworkElement)System.Windows.Media.VisualTreeHelper.GetParent((System.Windows.DependencyObject)window.FindName("CloseWindowButton"))) == 1, "title and caption button positions swapped");
            var captionButtons = (System.Windows.Controls.StackPanel)System.Windows.Media.VisualTreeHelper.GetParent((System.Windows.DependencyObject)window.FindName("CloseWindowButton"));
            Check(ReferenceEquals(captionButtons.Children[0], window.FindName("MinimizeWindowButton")) && ReferenceEquals(captionButtons.Children[2], window.FindName("CloseWindowButton")), "minimize left and close right in left-to-right caption");
            ThemeManager.Apply(true, false);
            Check(((System.Windows.Media.SolidColorBrush)app.Resources["AppBackground"]).Color == (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F2F5F8"), "light theme resources");
            ThemeManager.Apply(false, false);
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
