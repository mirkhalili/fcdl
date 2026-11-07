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
            string legacySam = Path.Combine(dir, "legacy-v1.sam"); File.Copy(projectPath, legacySam);
            using (var zip = ZipFile.Open(legacySam, ZipArchiveMode.Update))
            {
                var entryManifest = zip.GetEntry("manifest.json")!; string json; using (var reader = new StreamReader(entryManifest.Open())) json = reader.ReadToEnd();
                entryManifest.Delete(); var replacement = zip.CreateEntry("manifest.json"); using var writer = new StreamWriter(replacement.Open()); writer.Write(json.Replace("\"Version\":4", "\"Version\":1"));
            }
            Check(ProjectFile.Load(legacySam).Session.CanSave, "legacy SAM version one loads");
            string legacyV2 = Path.Combine(dir, "legacy-v2.sam"); File.Copy(projectPath, legacyV2);
            using (var zip = ZipFile.Open(legacyV2, ZipArchiveMode.Update))
            {
                var old = zip.GetEntry("manifest.json")!; string json; using (var reader = new StreamReader(old.Open())) json = reader.ReadToEnd();
                old.Delete(); var replacement = zip.CreateEntry("manifest.json"); using var writer = new StreamWriter(replacement.Open()); writer.Write(json.Replace("\"Version\":4", "\"Version\":2"));
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
            var weaveEditor = new WeaveEditor(); weaveEditor.Reset(3, 2);
            Check(weaveEditor.IsDirty && weaveEditor.Snapshot().Values.SequenceEqual(Enumerable.Repeat((byte)255, 6)), "blank editor grid and dirty state");
            weaveEditor.SetPattern(new WeavePattern(2, 2, new byte[] {0,255,255,0})); var editorCopy = weaveEditor.Snapshot(); editorCopy.Values[0] = 255;
            Check(weaveEditor.Snapshot().Values[0] == 0 && !weaveEditor.IsDirty, "editor copies selected patterns");
            Expect<ArgumentOutOfRangeException>(() => weaveEditor.Reset(65, 1));
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
            rowEditor.ApplyHelper("panama"); Check(rowEditor.Snapshot().Values.SequenceEqual(new byte[] {0,0,255,255, 0,0,255,255, 255,255,0,0, 255,255,0,0}), "2x2 Panama repeat");
            rowEditor.Reset(6,2); rowEditor.ApplyHelper("panama"); Check(rowEditor.Snapshot().Values.Length == 12, "panama accepts even non-multiple-of-four dimensions");
            rowEditor.Reset(3,2); Expect<ArgumentException>(() => rowEditor.ApplyHelper("panama"));
            Expect<ArgumentException>(() => rowEditor.ApplyHelper("satin"));
            rowEditor.Reset(5,5); rowEditor.ApplyHelper("satin");
            Check(rowEditor.Snapshot().Values.Count(v => v == 0) == 5 && Enumerable.Range(0,5).All(y => rowEditor.Snapshot().Values[y*5 + (y*2)%5] == 0), "five end satin with step two");
            rowEditor.Reset(8,8); rowEditor.ApplyHelper("satin", 1, 1, 8, 3); Check(rowEditor.Snapshot().Values.Count(v => v == 0) == 8, "eight-end satin step three");
            Expect<ArgumentException>(() => rowEditor.ApplyHelper("satin", 1, 1, 8, 2));
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
            var absoluteFile = Path.Combine(dir,"positioned.sam"); ProjectFile.Save(absoluteFile, new ProjectState(bankSession,"sample.bmp","3","2","50","Weft",false,1,bankSession.Patterns.Select(_ => (string?)"pattern").ToArray(),null,null,absolute), new[] {bmpPath});
            var absoluteLoaded = ProjectFile.Load(absoluteFile).Machine!; Check(absoluteLoaded.AbsoluteLayout && absoluteLoaded.TotalHooks == "30" && absoluteLoaded.WeftStart == "1" && DesignAnalysis.ReadBinary(MachineComposer.Compose(image, absoluteLoaded).Image).SequenceEqual(positionedPixels), "absolute layout persists and reproduces map");
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
            Check(AppVersion.Display == "014.2.11.1-a1", "QAVNS version from version.txt");
            var reportEntries = bankSession.Palette!.Colors.Select((color, index) => { var e = new PaletteEntry { Index = index, ColorCode = $"#{color:X6}", PixelCount = bankSession.Palette.Counts[index], Percent = 100d * bankSession.Palette.Counts[index] / bankSession.Palette.Indices.Length, Brush = new SolidColorBrush(Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color)) }; if (bankSession.Patterns[index] is { } p) e.Restore("sample", p); return e; }).ToArray();
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
            var window = new MainWindow();
            Check(window.FontFamily.Source.Contains("Vazirmatn"), "embedded font selected in main window");
            var nav = (System.Windows.Controls.Grid)window.FindName("Navigation");
            var settings = (System.Windows.FrameworkElement)window.FindName("SettingsBorder");
            Check(System.Windows.Controls.Grid.GetColumn((System.Windows.FrameworkElement)window.FindName("WeftStepButton")) == 7 && System.Windows.Controls.Grid.GetColumn((System.Windows.FrameworkElement)window.FindName("MachineStepButton")) == 8 && window.FindName("HelpButton") is System.Windows.Controls.Button, "weft immediately before machine and in-app help present");
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
