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
                entryManifest.Delete(); var replacement = zip.CreateEntry("manifest.json"); using var writer = new StreamWriter(replacement.Open()); writer.Write(json.Replace("\"Version\":13", "\"Version\":1"));
            }
            Check(ProjectFile.Load(legacySam).Session.CanSave, "legacy SAM version one loads");
            string legacyV2 = Path.Combine(dir, "legacy-v2.sam"); File.Copy(projectPath, legacyV2);
            using (var zip = ZipFile.Open(legacyV2, ZipArchiveMode.Update))
            {
                var old = zip.GetEntry("manifest.json")!; string json; using (var reader = new StreamReader(old.Open())) json = reader.ReadToEnd();
                old.Delete(); var replacement = zip.CreateEntry("manifest.json"); using var writer = new StreamWriter(replacement.Open()); writer.Write(json.Replace("\"Version\":13", "\"Version\":2"));
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
            var vectorInput = ImageLoader.Load(Path.Combine(repository, "tests", "fixtures", "vector-curves.pdf"), 0);
            var vectorSession = new WorkflowSession(vectorInput.Image, vectorInput.Vector); vectorSession.ApplySize(211, 177); var vectorPixels = new byte[211 * 177 * 4]; vectorSession.Resized!.CopyPixels(vectorPixels, 211 * 4, 0);
            var vectorColors = Enumerable.Range(0, 211 * 177).Select(i => (vectorPixels[4*i+2] << 16) | (vectorPixels[4*i+1] << 8) | vectorPixels[4*i]).Distinct().OrderBy(i => i).ToArray();
            Check(vectorColors.SequenceEqual(new[] { 0x0000FF, 0xFF0000, 0xFFFFFF }), "direct PDF curve rasterization without anti-aliased edge colors");
            string vectorProject = Path.Combine(dir, "vector-project.sam"); ProjectFile.Save(vectorProject, new ProjectState(vectorSession, "curves.pdf", "211", "177", "50", "Size", false, 0, Array.Empty<string?>()), Array.Empty<string>());
            var reopenedVector = ProjectFile.Load(vectorProject).Session; reopenedVector.ApplySize(211, 177); var restoredVectorPixels = new byte[vectorPixels.Length]; reopenedVector.Resized!.CopyPixels(restoredVectorPixels, 211 * 4, 0);
            Check(reopenedVector.Vector is not null && vectorPixels.SequenceEqual(restoredVectorPixels), "SAM embeds vector source and rerenders identical curves after reopen");
            var vectorAi = Path.Combine(dir, "curves.ai"); File.Copy(Path.Combine(repository, "tests", "fixtures", "vector-curves.pdf"), vectorAi); var aiInput = ImageLoader.Load(vectorAi, 0);
            var aiSession = new WorkflowSession(aiInput.Image, aiInput.Vector); aiSession.ApplySize(211, 177); var aiPixels = new byte[vectorPixels.Length]; aiSession.Resized!.CopyPixels(aiPixels, 211 * 4, 0); Check(aiPixels.SequenceEqual(vectorPixels), "PDF-compatible AI uses same curve engine");
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
            Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(dir, "machin.jc5")))).Equals("A378D4FA18D1641BBB7453AC3BE8B35D4D1B1CCB0FE7F5BFA97E5392693E04C7"), "Bonas user sample is the same JC5 fixture, independently identified");
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
            var profilePath=System.IO.Path.Combine(dir,"test-machine.samm");
            var profileSetup=new MachineSetup(AbsoluteLayout:true,TotalHooks:"30",DesignStart:"4",WeftStart:"1");
            MachineProfile.Save(profilePath,profileSetup,"Bonas"); var loadedProfile=MachineProfile.Load(profilePath);
            Check(loadedProfile.Manufacturer=="Bonas" && loadedProfile.Setup.TotalHooks=="30" && loadedProfile.Setup.DesignStart=="4", "independent machine profile roundtrip");
            Expect<ArgumentException>(()=>MachineProfile.Validate(profileSetup with { DesignStart="30" },"Bonas"));
            using (var fakeClient=new System.Net.Http.HttpClient(new FakeImageHandler(Convert.ToBase64String(File.ReadAllBytes(pngPath)))))
            {
                var aiPng=AiImageEdit.GenerateAsync(b.Image,"make it blue","test-key",fakeClient).GetAwaiter().GetResult();
                Check(!string.IsNullOrEmpty(aiPng), "AI edit uses injected HTTP handler and returns a decoded image without a real service");
            }
            var safeMap=MachineComposer.Compose(b.Image,new MachineSetup(LeftWidth:"1",RightWidth:"1",LeftPattern:new WeavePattern(1,1,new byte[]{0}),RightPattern:new WeavePattern(1,1,new byte[]{255}),AbsoluteLayout:true,TotalHooks:"9",DesignStart:"3"));
            var safeInput=new ModuleValue(ModuleData.Fabric,safeMap.Image,Composition:safeMap); var safeFabric=CompositionView.Fabric(safeMap);
            var sameEdited=ModuleExecutor.ApplyFloatEdit(safeInput,ModuleExecutor.EmbedImage(safeFabric));
            Check(DesignAnalysis.ReadBinary(sameEdited.Image!).SequenceEqual(DesignAnalysis.ReadBinary(safeMap.Image)), "float edits preserve signal and outside-fabric columns");
            var forbidden=DesignAnalysis.ReadBinary(safeFabric); forbidden[0]=forbidden[0]==0?(byte)255:(byte)0;
            var forbiddenImage=BitmapSource.Create(safeFabric.PixelWidth,safeFabric.PixelHeight,96,96,PixelFormats.Gray8,null,forbidden,safeFabric.PixelWidth); forbiddenImage.Freeze();
            Expect<InvalidDataException>(()=>ModuleExecutor.ApplyFloatEdit(safeInput,ModuleExecutor.EmbedImage(forbiddenImage)));
            var autoPair=AutoWeaveAssignments.Pair(new[]{11,22,33},3,new[]{2,0,1},new[]{2,0},new Dictionary<int,int>());
            Check(autoPair.Count==2 && autoPair[11]==0 && autoPair[22]==2 && !autoPair.ContainsKey(33), "automatic weaves pair selected items in visual order and leave remaining colors unassigned");
            Expect<ArgumentOutOfRangeException>(()=>AutoWeaveAssignments.Pair(new[]{1},1,new[]{2},new[]{0},new Dictionary<int,int>()));
            var encryptedData=EncryptedProject.Encrypt(System.Text.Encoding.UTF8.GetBytes("private sample"),"user pass 123");
            Check(System.Text.Encoding.UTF8.GetString(EncryptedProject.Decrypt(encryptedData,"user pass 123"))=="private sample", "encrypted project password roundtrip");
            Check(System.Text.Encoding.UTF8.GetString(EncryptedProject.Decrypt(encryptedData,"Mir@1354@sam"))=="private sample", "explicitly requested shared recovery password roundtrip");
            Expect<InvalidDataException>(()=>EncryptedProject.Decrypt(encryptedData,"incorrect")); var tampered=(byte[])encryptedData.Clone(); tampered[^1]^=1; Expect<InvalidDataException>(()=>EncryptedProject.Decrypt(tampered,"user pass 123")); Expect<InvalidDataException>(()=>EncryptedProject.Decrypt(tampered,"Mir@1354@sam")); var headerTampered=(byte[])encryptedData.Clone();headerTampered[25]^=1;Expect<InvalidDataException>(()=>EncryptedProject.Decrypt(headerTampered,"user pass 123"));
            var localPrefs=new UserPreferences(); localPrefs.SetPassword("local pass 123"); Check(localPrefs.Verify("local pass 123") && !localPrefs.Verify("wrong"), "local startup password uses a salted verifier");
            var checkedBinary=GraphChecks.Threshold(b.Image,128,false); Check(DesignAnalysis.ReadBinary(checkedBinary).SequenceEqual(pixels), "threshold conversion preserves binary input");
            var flippedBinary=GraphChecks.Threshold(b.Image,128,true); Check(DesignAnalysis.ReadBinary(flippedBinary).SequenceEqual(pixels.Select(x=>(byte)(255-x))), "threshold conversion supports inversion");
            var imageDiff=GraphChecks.Compare(checkedBinary,flippedBinary); Check(imageDiff.changed==pixels.Length, "comparison counts changed pixels");
            Expect<ArgumentException>(()=>GraphChecks.Compare(checkedBinary,BitmapSource.Create(1,1,96,96,PixelFormats.Gray8,null,new byte[]{0},1)));
            var seam=GraphChecks.Seams(b.Image); Check(seam.horizontal==2 && seam.vertical==3, "repeat boundary comparison counts mismatched edge pixels");
            var cyclic=PeriodicFloatAnalysis.Analyze(new byte[]{0,0,255,255,0},5,1,2,FloatDirection.Weft);
            Check(cyclic.BlackRuns==1 && cyclic.WhiteRuns==1 && cyclic.CrossingRuns==1 && cyclic.LongestFinite==3 && cyclic.UnboundedBlack==0,"direct repeat joins boundary float once");
            var verticalCyclic=PeriodicFloatAnalysis.Analyze(new byte[]{0,0,255,255,0},1,5,4,FloatDirection.Warp);
            Check(verticalCyclic.BlackRuns==0 && verticalCyclic.WhiteRuns==0 && verticalCyclic.LongestFinite==3,"periodic threshold is inclusive and independent of direction");
            var infinite=PeriodicFloatAnalysis.Analyze(new byte[]{0,0,0,255,255,255},3,2,100,FloatDirection.Weft);
            Check(infinite.UnboundedBlack==1 && infinite.UnboundedWhite==1 && infinite.LongestFinite==0,"solid repeated lines are unbounded even above one period");
            Expect<ArgumentException>(()=>PeriodicFloatAnalysis.Analyze(new byte[]{0,1},2,1,1,FloatDirection.Weft));
            // Compare the cyclic analysis with an independently merged linear-run oracle for every short binary line.
            for(int len=1;len<=8;len++) for(int bits=0;bits<(1<<len);bits++) for(int minimum=1;minimum<=len+1;minimum++)
            {
                var line=Enumerable.Range(0,len).Select(j=>(bits & (1<<j))==0?(byte)0:(byte)255).ToArray();
                var referenceRuns=DesignAnalysis.FindFloats(line,len,1,1,FloatDirection.Weft); bool uniform=referenceRuns.Count==1;
                int ub=uniform && line[0]==0?1:0, uw=uniform && line[0]==255?1:0; var segments=new List<(int size,bool black,bool crossing)>();
                if(!uniform)
                {
                    bool joins=line[0]==line[^1]; if(joins)segments.Add((referenceRuns[0].Length+referenceRuns[^1].Length,line[0]==0,true));
                    for(int k=joins?1:0;k<referenceRuns.Count-(joins?1:0);k++)segments.Add((referenceRuns[k].Length,referenceRuns[k].Black,false));
                }
                var periodicActual=PeriodicFloatAnalysis.Analyze(line,len,1,minimum,FloatDirection.Weft);
                Check(periodicActual.BlackRuns==segments.Count(t=>t.black && t.size>=minimum) && periodicActual.WhiteRuns==segments.Count(t=>!t.black && t.size>=minimum) && periodicActual.CrossingRuns==segments.Count(t=>t.crossing && t.size>=minimum) && periodicActual.UnboundedBlack==ub && periodicActual.UnboundedWhite==uw && periodicActual.LongestFinite==segments.Select(t=>t.size).DefaultIfEmpty(0).Max(),"periodic float exhaustive oracle");
            }
            var matchSource=new PaletteResult(4,1,4,new[]{0x000000,0x100000,0xff0000,0x800000},new byte[]{0,1,2,3},new[]{1,1,1,1});
            var matchReference=new PaletteResult(2,1,2,new[]{0x000000,0xff0000},new byte[]{0,1},new[]{1,1});
            var matched=PaletteReferenceMatcher.Match(matchSource,matchReference);
            Check(matched.Colors.SequenceEqual(new[]{0x000000,0xff0000}) && matched.Indices.SequenceEqual(new byte[]{0,0,1,1}) && matched.Counts.SequenceEqual(new[]{2,2}) && matchSource.Colors.Length==4,"reference palette matching merges nearest targets and keeps source immutable");
            var tiedSource=new PaletteResult(1,1,1,new[]{0x010000},new byte[]{0},new[]{1});var tiedReference=new PaletteResult(2,1,2,new[]{0x020000,0x000000},new byte[]{0,1},new[]{1,1});
            Check(PaletteReferenceMatcher.Match(tiedSource,tiedReference).Colors.Single()==0x020000,"palette match ties use first reference entry");
            Expect<ArgumentException>(()=>PaletteReferenceMatcher.Match(matchSource,matchReference with {Counts=new[]{0,0}}));
            var rectMask=GraphMasks.Rectangle(b.Image,1,0,1,2);Check(DesignAnalysis.ReadBinary(rectMask).SequenceEqual(new byte[]{0,255,0,0,255,0}),"rectangular mask selects exact zero-based pixels");
            var masked=GraphMasks.Apply(b.Image,rectMask,false,"#112233");var maskedBytes=new byte[24];masked.CopyPixels(maskedBytes,12,0);Check(maskedBytes[0]==0x33 && maskedBytes[1]==0x22 && maskedBytes[2]==0x11 && maskedBytes[4]==255 && maskedBytes[16]==0,"mask preserves selected pixels and fills outside only");
            var inverseMask=GraphMasks.Apply(b.Image,rectMask,true,"#112233");inverseMask.CopyPixels(maskedBytes,12,0);Check(maskedBytes[0]==0 && maskedBytes[4]==0x33 && maskedBytes[16]==0x33,"mask can preserve the complement");
            Expect<ArgumentException>(()=>GraphMasks.Rectangle(b.Image,2,0,2,1));Expect<ArgumentException>(()=>GraphMasks.Apply(b.Image,BitmapSource.Create(1,1,96,96,PixelFormats.Gray8,null,new byte[]{255},1),false,"#FFFFFF"));
            Check(DesignAnalysis.ReadBinary(b.Image).SequenceEqual(pixels),"mask operations never mutate source image");
            var qualityGraph=new ModuleGraph(); var binarySource=qualityGraph.Add(ModuleKind.BinaryLoad,0,0);binarySource.ImagePng=ModuleExecutor.EmbedImage(b.Image);var qc=qualityGraph.Add(ModuleKind.QualityCheck,250,0);qualityGraph.Connect(binarySource.Id,qc.Id,"fabric");
            Check(ModuleExecutor.Run(qualityGraph,qc.Id)[qc.Id].QualityNotes!.Any(t=>t.Contains("تعریف ماشین وصل نیست")), "quality module checks binary map without implying machine validation");
            var comparisonNode=qualityGraph.Add(ModuleKind.Compare,500,0); qualityGraph.Connect(binarySource.Id,comparisonNode.Id,"first");qualityGraph.Connect(qc.Id,comparisonNode.Id,"second");Check(ModuleExecutor.Run(qualityGraph,comparisonNode.Id)[comparisonNode.Id].QualityNotes![0].Contains("0"), "visual ports compare two fabric branches");
            var seamNode=qualityGraph.Add(ModuleKind.SeamCheck,750,0);qualityGraph.Connect(binarySource.Id,seamNode.Id,"image");Check(ModuleExecutor.Run(qualityGraph,seamNode.Id)[seamNode.Id].QualityNotes!.Count==3,"seam check accepts a binary map");
            var periodicNode=qualityGraph.Add(ModuleKind.PeriodicFloats,850,0);periodicNode.FloatWarp=false;periodicNode.FloatMinimum=2;qualityGraph.Connect(binarySource.Id,periodicNode.Id,"fabric");var periodicOutput=ModuleExecutor.Run(qualityGraph,periodicNode.Id)[periodicNode.Id];Check(DesignAnalysis.ReadBinary(periodicOutput.Image!).SequenceEqual(pixels) && periodicOutput.QualityNotes!.Count==6,"periodic float module reports without mutating map");
            var periodicReport=qualityGraph.Add(ModuleKind.Report,900,100);qualityGraph.Connect(periodicNode.Id,periodicReport.Id,"report");Check(ModuleExecutor.Run(qualityGraph,periodicReport.Id)[periodicReport.Id].ReportItems!.Single().Details.Contains("نامحدود"),"periodic findings appear in connected report");
            string periodicPath=Path.Combine(dir,"periodic.sam");ModuleProjectFile.SaveEncrypted(periodicPath,qualityGraph,"user pass 123");var periodicReload=ModuleProjectFile.LoadEncrypted(periodicPath,"user pass 123");Check(ModuleExecutor.Run(periodicReload,periodicNode.Id)[periodicNode.Id].QualityNotes!.SequenceEqual(periodicOutput.QualityNotes!),"periodic module configuration survives encrypted project roundtrip");
            var exportNode=qualityGraph.Add(ModuleKind.ExportData,1000,0);qualityGraph.Connect(binarySource.Id,exportNode.Id,"image");var exportValue=ModuleExecutor.Run(qualityGraph,exportNode.Id)[exportNode.Id];
            string exportPng=Path.Combine(dir,"graph-export.png");GraphExports.SaveImage(exportValue.Image!,exportPng,Array.Empty<string>());Check(ImageLoader.Load(exportPng,0).Image.PixelWidth==3,"graph image export writes PNG");
            string csv=Path.Combine(dir,"graph-palette.csv");GraphExports.SavePalette(pipeline.Palette!,csv,Array.Empty<string>());Check(File.ReadAllLines(csv).Length==3 && File.ReadAllText(csv).Contains("#000000"),"palette table CSV has header and color rows");Expect<IOException>(()=>GraphExports.SavePalette(pipeline.Palette!,csv,new[]{csv}));
            var graph = new ModuleGraph(); var graphLoad = graph.Add(ModuleKind.Load, 0, 0); graphLoad.ImagePng = ModuleExecutor.EmbedImage(b.Image);
            string secretGraph=Path.Combine(dir,"encrypted.sam"); ModuleProjectFile.SaveEncrypted(secretGraph,graph,"user pass 123"); Check(ModuleProjectFile.IsGraph(secretGraph) && ModuleProjectFile.IsEncrypted(secretGraph) && ModuleProjectFile.LoadEncrypted(secretGraph,"user pass 123").Nodes.Count==1, "encrypted SAM graph roundtrip"); Expect<IOException>(()=>ModuleProjectFile.Save(secretGraph,graph));
            // Drawing operations, isolation, persisted graph output and changed-input guard.
            var draw = new DesignDrawing(image); var originalDrawing=draw.Snapshot();
            draw.Line(0,0,2,1,Colors.Red); Check(draw.At(0,0)==Colors.Red && draw.At(2,1)==Colors.Red, "drawing line endpoints");
            draw.Restore(originalDrawing); draw.Rectangle(0,0,2,1,Colors.Blue); Check(draw.At(0,0)==Colors.Blue && draw.At(2,1)==Colors.Blue, "drawing rectangle border");
            draw.Restore(originalDrawing); draw.Fill(1,0,Colors.Red); Check(draw.At(1,0)==Colors.Red && draw.At(2,0)==Colors.Red && draw.At(0,0)!=Colors.Red, "drawing fill connected component");
            Check(new DesignDrawing(image).At(1,0)!=Colors.Red, "drawing never modifies input");
            var designGraph=new ModuleGraph(); var designSource=designGraph.Add(ModuleKind.Load,0,0); designSource.ImagePng=ModuleExecutor.EmbedImage(image); var designNode=designGraph.Add(ModuleKind.DesignEditor,250,0); designGraph.Connect(designSource.Id,designNode.Id,"image");
            designNode.DesignEditPng=ModuleExecutor.EmbedImage(draw.Image()); designNode.DesignEditInputHash=ModuleExecutor.ImageFingerprint(image);
            Check(new DesignDrawing(ModuleExecutor.Run(designGraph,designNode.Id)[designNode.Id].Image!).At(1,0)==Colors.Red, "design graph output");
            string designPath=Path.Combine(dir,"design.jgw");ModuleProjectFile.Save(designPath,designGraph);var designRestored=ModuleProjectFile.Load(designPath);
            Check(new DesignDrawing(ModuleExecutor.Run(designRestored,designNode.Id)[designNode.Id].Image!).At(1,0)==Colors.Red, "design graph project roundtrip");
            designRestored.Nodes.Single(n=>n.Id==designSource.Id).ImagePng=ModuleExecutor.EmbedImage(draw.Image()); Expect<InvalidOperationException>(()=>ModuleExecutor.Run(designRestored,designNode.Id));
            var masksGraph=new ModuleGraph();var masksSource=masksGraph.Add(ModuleKind.Load,0,0);masksSource.ImagePng=ModuleExecutor.EmbedImage(b.Image);var maskNode=masksGraph.Add(ModuleKind.RectangleMask,250,0);maskNode.MaskX=1;maskNode.MaskWidth=1;var applyMaskNode=masksGraph.Add(ModuleKind.ApplyMask,500,0);applyMaskNode.MaskFill="#112233";masksGraph.Connect(masksSource.Id,maskNode.Id,"image");masksGraph.Connect(masksSource.Id,applyMaskNode.Id,"image");masksGraph.Connect(maskNode.Id,applyMaskNode.Id,"mask");
            var graphMaskResult=ModuleExecutor.Run(masksGraph,applyMaskNode.Id);Check(graphMaskResult[maskNode.Id].Type==ModuleData.Mask && graphMaskResult[applyMaskNode.Id].Image!.PixelWidth==3,"graph mask ports execute connected branches");Expect<ArgumentException>(()=>masksGraph.Connect(masksSource.Id,applyMaskNode.Id,"mask"));
            string maskPath=Path.Combine(dir,"mask-graph.sam");ModuleProjectFile.SaveEncrypted(maskPath,masksGraph,"user pass 123");var restoredMasks=ModuleProjectFile.LoadEncrypted(maskPath,"user pass 123");Check(DesignAnalysis.ReadBinary(ModuleExecutor.Run(restoredMasks,maskNode.Id)[maskNode.Id].Image!).SequenceEqual(DesignAnalysis.ReadBinary(rectMask)),"mask settings and connections survive encrypted project roundtrip");
            var paletteGraph=new ModuleGraph();var paletteA=paletteGraph.Add(ModuleKind.Load,0,0);paletteA.ImagePng=ModuleExecutor.EmbedImage(b.Image);var paletteB=paletteGraph.Add(ModuleKind.Load,0,200);paletteB.ImagePng=paletteA.ImagePng;var colorA=paletteGraph.Add(ModuleKind.Colors,250,0);var colorB=paletteGraph.Add(ModuleKind.Colors,250,200);var paletteMatch=paletteGraph.Add(ModuleKind.MatchPalette,500,0);paletteGraph.Connect(paletteA.Id,colorA.Id,"image");paletteGraph.Connect(paletteB.Id,colorB.Id,"image");paletteGraph.Connect(colorA.Id,paletteMatch.Id,"palette");paletteGraph.Connect(colorB.Id,paletteMatch.Id,"reference");var paletteOutput=ModuleExecutor.Run(paletteGraph,paletteMatch.Id)[paletteMatch.Id];Check(paletteOutput.Type==ModuleData.Palette && paletteOutput.Palette!.Counts.Sum()==pixels.Length,"reference palette graph module produces reusable palette");
            var graphResize = graph.Add(ModuleKind.Resize, 250, 0); graphResize.Width=3; graphResize.Height=2; graph.Connect(graphLoad.Id, graphResize.Id, "image");
            var graphOtherResize = graph.Add(ModuleKind.Resize, 250, 180); graphOtherResize.Width=6; graphOtherResize.Height=4; graph.Connect(graphLoad.Id,graphOtherResize.Id,"image");
            var graphColors = graph.Add(ModuleKind.Colors, 500, 0); graphColors.ColorLimit=2; graph.Connect(graphResize.Id,graphColors.Id,"image");
            var graphWeaves = graph.Add(ModuleKind.Weaves, 500, 180); graphWeaves.Weaves.Add(PatternLibrary.Presets[0].Pattern); graphWeaves.WeaveNames.Add("plain");
            var graphApply = graph.Add(ModuleKind.ApplyWeaves, 750, 0); graph.Connect(graphColors.Id,graphApply.Id,"palette"); graph.Connect(graphWeaves.Id,graphApply.Id,"weaves");
            var graphFloat = graph.Add(ModuleKind.Floats, 1000, 0); graphFloat.FloatMinimum=2; graph.Connect(graphApply.Id,graphFloat.Id,"fabric");
            var graphTension=graph.Add(ModuleKind.Tension,1250,0); graph.Connect(graphFloat.Id,graphTension.Id,"fabric");
            var graphWeft=graph.Add(ModuleKind.Weft,1500,0); graphWeft.Machine=new MachineSetup(AbsoluteLayout:true,BoxEnabled:true,DesignStart:"1",WeftStart:"0",WeftCount:"1"); graph.Connect(graphTension.Id,graphWeft.Id,"fabric");
            var graphMachine=graph.Add(ModuleKind.Machine,1750,0); graph.Connect(graphWeft.Id,graphMachine.Id,"fabric");
            var graphSimulation=graph.Add(ModuleKind.Simulation,1750,200); graphSimulation.Simulation=new SimulationSettings(Mode:0); graph.Connect(graphWeft.Id,graphSimulation.Id,"fabric");
            var executed=ModuleExecutor.Run(graph,graphMachine.Id); Check(executed[graphMachine.Id].Image!.PixelWidth==4 && executed[graphTension.Id].Analysis is not null && executed[graphWeft.Id].Composition!.RowWefts.Length==2, "connected modules execute real image, weave, float, analysis, weft and machine data");
            Check(ModuleExecutor.Run(graph,graphSimulation.Id)[graphSimulation.Id].Image is not null && ModuleExecutor.Run(graph,graphOtherResize.Id)[graphOtherResize.Id].Image!.PixelWidth==6, "independent image and simulation branches execute");
            Expect<ArgumentException>(()=>graph.Connect(graphColors.Id,graphFloat.Id,"fabric")); Expect<ArgumentException>(()=>graph.Connect(graphTension.Id,graphFloat.Id,"fabric"));
            Check(graph.Edges.Any(e=>e.From==graphApply.Id && e.To==graphFloat.Id), "failed connection is atomic and leaves original edge");
            var secondWeft = graph.Add(ModuleKind.Weft,1900,0); graph.Connect(graphWeft.Id,secondWeft.Id,"fabric"); Expect<InvalidOperationException>(()=>ModuleExecutor.Run(graph,secondWeft.Id)); graph.Remove(secondWeft.Id);
            var lateSelvedge = graph.Add(ModuleKind.Selvedge,1900,0); graph.Connect(graphWeft.Id,lateSelvedge.Id,"fabric"); Expect<InvalidOperationException>(()=>ModuleExecutor.Run(graph,lateSelvedge.Id)); graph.Remove(lateSelvedge.Id);
            string graphPath=Path.Combine(dir,"workflow.jgw"); ModuleProjectFile.Save(graphPath,graph); var graphRestored=ModuleProjectFile.Load(graphPath); Check(graphRestored.Nodes.Count==graph.Nodes.Count && ModuleExecutor.Run(graphRestored,graphMachine.Id)[graphMachine.Id].Image!.PixelWidth==4, "graph, embedded source, patterns, settings and connections survive round trip");
            var editedInput=ModuleExecutor.Run(graph,graphResize.Id)[graphResize.Id].Image!;
            var editing=new WorkflowSession(editedInput); editing.ApplySize(3,2); editing.ApplyColors(2); editing.MergeColors(0,1); editing.SetColorLock(0,true);
            graphColors.ColorEdit=new ModuleColorEdit(ModuleExecutor.ImageFingerprint(editedInput),editing.Palette!,editing.LockedColors);
            Check(ModuleExecutor.Run(graph,graphApply.Id)[graphColors.Id].Palette!.Colors.Length==1, "graph propagates edited palette into downstream weave mapping");
            ModuleProjectFile.Save(graphPath,graph); var editRestored=ModuleProjectFile.Load(graphPath); var savedEdit=editRestored.Nodes.Single(n=>n.Id==graphColors.Id).ColorEdit!;
            Check(savedEdit.Locks.Single() && savedEdit.Palette.Counts.Single()==6 && ModuleExecutor.Run(editRestored,graphApply.Id)[graphColors.Id].Palette!.Indices.SequenceEqual(editing.Palette!.Indices), "graph palette edits and locks survive project roundtrip");
            editRestored.Nodes.Single(n=>n.Id==graphResize.Id).Width=2; Expect<InvalidOperationException>(()=>ModuleExecutor.Run(editRestored,graphApply.Id));
            graphColors.ColorEdit=new ModuleColorEdit(ModuleExecutor.ImageFingerprint(editedInput), editing.Palette! with { Counts=new[]{99} }, editing.LockedColors); Expect<InvalidDataException>(()=>graph.Validate()); graphColors.ColorEdit=null;
            var designer=graph.Add(ModuleKind.CreateWeave, 500, 350); designer.Weaves.Add(PatternLibrary.Presets[0].Pattern); designer.WeaveNames.Add("designed");
            graph.Connect(designer.Id,graphApply.Id,"weaves"); Check(ModuleExecutor.Run(graph,graphApply.Id)[designer.Id].Weaves!.Count==1, "independent weave designer node feeds a downstream mapping");
            ModuleProjectFile.Save(graphPath,graph); var designerRestored=ModuleProjectFile.Load(graphPath); Check(designerRestored.Nodes.Single(n=>n.Id==designer.Id).Weaves.Single().Values.SequenceEqual(designer.Weaves[0].Values) && ModuleExecutor.Run(designerRestored,graphApply.Id)[graphApply.Id].Image is not null, "designed weave node survives project roundtrip and runs");
            designer.Weaves.Add(PatternLibrary.Presets[0].Pattern); designer.WeaveNames.Add("too many"); Expect<InvalidDataException>(()=>graph.Validate()); designer.Weaves.RemoveAt(1); designer.WeaveNames.RemoveAt(1);
            var cropNode=graph.Add(ModuleKind.Crop,0,400); graph.Connect(graphResize.Id,cropNode.Id,"image");
            var inputImage=ModuleExecutor.Run(graph,graphResize.Id)[graphResize.Id].Image!; string inputHash=ModuleExecutor.ImageFingerprint(inputImage);
            var fullCrop=new GraphCrop(0,0,3,2,inputHash);
            Check(GraphImageOperations.FindPixel(3,2,59,39,60,40)==(2,1) && GraphImageOperations.FindPixel(3,2,60,0,60,40) is null && GraphImageOperations.FindPixel(3,2,double.NaN,0,60,40) is null, "crop pointer maps correctly with scaling and rejects invalid coordinates");
            Check(GraphImageOperations.Cut(fullCrop,CropDirection.Left,1,0) == new GraphCrop(1,0,2,2,inputHash) && GraphImageOperations.Cut(fullCrop,CropDirection.Right,1,0).Width==2 && GraphImageOperations.Cut(fullCrop,CropDirection.Top,0,1).Height==1 && GraphImageOperations.Cut(fullCrop,CropDirection.Bottom,0,0).Height==1, "crop discards selected side and retains clicked boundary pixel");
            cropNode.Crop=GraphImageOperations.Cut(fullCrop,CropDirection.Left,1,0);
            var cropped=ModuleExecutor.Run(graph,cropNode.Id)[cropNode.Id].Image!; Check(cropped.PixelWidth==2 && cropped.PixelHeight==2, "crop node executes selected rectangle");
            var repeatNode=graph.Add(ModuleKind.Repeat,250,400); repeatNode.RepeatColumns=2; repeatNode.RepeatRows=2; graph.Connect(cropNode.Id,repeatNode.Id,"image");
            var repeatSource=BitmapSource.Create(2,2,96,96,PixelFormats.Gray8,null,new byte[]{0,64,128,255},2); repeatSource.Freeze();
            byte[] Gray(BitmapSource im) { var g=new FormatConvertedBitmap(im,PixelFormats.Gray8,null,0); var data=new byte[im.PixelWidth*im.PixelHeight]; g.CopyPixels(data,im.PixelWidth,0); return data; }
            Check(Gray(GraphImageOperations.Repeat(repeatSource,2,2,RepeatMode.Straight)).SequenceEqual(new byte[]{0,64,0,64,128,255,128,255,0,64,0,64,128,255,128,255}), "straight repeat preserves pixels");
            Check(Gray(GraphImageOperations.Repeat(repeatSource,2,2,RepeatMode.HalfWidth)).SequenceEqual(new byte[]{0,64,0,64,128,255,128,255,64,0,64,0,255,128,255,128}), "half-width repeat offsets alternating rows and wraps edges");
            Check(Gray(GraphImageOperations.Repeat(repeatSource,2,2,RepeatMode.HalfHeight)).SequenceEqual(new byte[]{0,64,128,255,128,255,0,64,0,64,128,255,128,255,0,64}), "half-height repeat offsets alternating columns and wraps edges");
            Expect<ArgumentException>(()=>GraphImageOperations.Repeat(repeatSource,int.MaxValue,int.MaxValue,RepeatMode.Straight));
            Check(ModuleExecutor.Run(graph,repeatNode.Id)[repeatNode.Id].Image!.PixelWidth==4, "repeat node processes crop output");
            var reportNode=graph.Add(ModuleKind.Report,500,400); graph.Connect(graphResize.Id,reportNode.Id,"report"); graph.Connect(graphColors.Id,reportNode.Id,"report"); graph.Connect(designer.Id,reportNode.Id,"report"); graph.Connect(repeatNode.Id,reportNode.Id,"report");
            graph.Connect(graphResize.Id,reportNode.Id,"report"); graph.Disconnect(designer.Id,reportNode.Id,"report"); Check(graph.Edges.Count(e=>e.To==reportNode.Id)==3, "report disconnect removes only the chosen input"); graph.Connect(designer.Id,reportNode.Id,"report"); var reportValues=ModuleExecutor.Run(graph,reportNode.Id)[reportNode.Id].ReportItems!;
            Check(reportValues.Count==4 && reportValues.Any(x=>x.Value.Palette is not null) && reportValues.Any(x=>x.Value.Weaves is not null), "report aggregates independent connected module types without duplicate links");
            var secondReport=graph.Add(ModuleKind.Report,750,400); graph.Connect(reportNode.Id,secondReport.Id,"report"); Expect<ArgumentException>(()=>graph.Connect(secondReport.Id,reportNode.Id,"report")); graph.Remove(secondReport.Id);
            var capacity=new ModuleGraph(); var capacityReport=capacity.Add(ModuleKind.Report,0,0); for(int i=0;i<16;i++) capacity.Connect(capacity.Add(ModuleKind.Load,0,0).Id,capacityReport.Id,"report"); Expect<ArgumentException>(()=>capacity.Connect(capacity.Add(ModuleKind.Load,0,0).Id,capacityReport.Id,"report")); Check(capacity.Edges.Count==16, "report input is capped without partial connection");
            var previousWires=graph.Edges.ToArray(); var oldCoords=graph.Nodes.Select(n=>(n.X,n.Y)).ToArray(); Expect<ArgumentException>(()=>graph.Arrange(0,1)); Check(graph.Nodes.Select(n=>(n.X,n.Y)).SequenceEqual(oldCoords), "invalid layout is atomic"); graph.Arrange(1,1); Check(graph.Nodes.Select(n=>(n.X,n.Y)).Distinct().Count()==graph.Nodes.Count, "layout expands columns for insufficient capacity");
            graph.Arrange(5,4); Check(graph.Edges.SequenceEqual(previousWires) && graph.Nodes[1].X==330 && graph.Nodes[4].Y==260, "grid layout positions nodes without changing connections");
            string graphSam=Path.Combine(dir,"graph.sam"); ModuleProjectFile.Save(graphSam,graph); var loadedSam=ModuleProjectFile.Load(graphSam); Check(ModuleProjectFile.IsGraph(graphSam) && loadedSam.Nodes.Single(n=>n.Id==cropNode.Id).Crop==cropNode.Crop && ModuleExecutor.Run(loadedSam,reportNode.Id)[reportNode.Id].ReportItems!.Count==4, "SAM graph persists crop, repeat and multiple report links");
            loadedSam.Nodes.Single(n=>n.Id==graphResize.Id).Width=2; Expect<InvalidOperationException>(()=>ModuleExecutor.Run(loadedSam,reportNode.Id));
            graphRestored.Disconnect(graphApply.Id,"weaves"); Expect<InvalidOperationException>(()=>ModuleExecutor.Run(graphRestored,graphMachine.Id));
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
            Check(!ModuleProjectFile.IsGraph(mergedProject), "legacy SAM is distinguished from graph SAM"); Expect<IOException>(()=>ModuleProjectFile.Save(mergedProject,graph));
            var mergedRestored = ProjectFile.Load(mergedProject);
            Check(mergedRestored.Session.Palette!.Counts.SequenceEqual(new[] {6}) && mergedRestored.Session.Patterns[0]!.Values.SequenceEqual(new byte[] {255}), "SAM persists merged palette and weave");
            var multi = new PaletteResult(5, 1, 3, new[] {0x000000, 0x121212, 0xFFFFFF}, new byte[] {0, 1, 2, 0, 2}, new[] {2, 1, 2});
            Check(DesignProcessor.NearestRemainingColor(multi, 0) == 1, "nearest uses RGB distance");
            var mergedUp = DesignProcessor.MergeColors(multi, 0, 2);
            Check(mergedUp.Colors.SequenceEqual(new[] {0x121212, 0xFFFFFF}) && mergedUp.Indices.SequenceEqual(new byte[] {1, 0, 1, 1, 1}) && mergedUp.Counts.SequenceEqual(new[] {1, 4}), "merge handles source before target");
            var mergedDown = DesignProcessor.MergeColors(multi, 2, 0);
            Check(mergedDown.Indices.SequenceEqual(new byte[] {0, 1, 0, 0, 0}) && mergedDown.Counts.SequenceEqual(new[] {4, 1}), "merge handles source after target");
            var kept = DesignProcessor.KeepColors(multi, new[]{0,2}); Check(kept.Indices.SequenceEqual(new byte[]{0,0,1,0,1}) && kept.Counts.SequenceEqual(new[]{3,2}), "bulk deletion maps each color to nearest remaining color");
            var bulkMerge = DesignProcessor.KeepColors(multi, new[]{2}, 2); Check(bulkMerge.Counts.Single() == 5 && bulkMerge.Colors.Single() == 0xFFFFFF, "multiple colors merge into selected target");
            Check(PreviewHitTest.ColorsInRectangle(multi, 21,0, 0,9, 50,10).SequenceEqual(new[]{0,1,2}), "reverse rectangle selects all intersecting colors");
            Check(PreviewHitTest.ColorsInRectangle(multi, -3,0, 9,9, 50,10).SequenceEqual(new[]{0}), "rectangle clips to image bounds");
            Check(PreviewHitTest.ColorsInRectangle(multi, -30,-2,-1,2,50,10).Length == 0, "rectangle outside image selects nothing");
            Check(MachineFileNaming.SuggestedName(4) == "design6.jc5" && MachineFileNaming.ValidDestination("design6.jc5", 4) && MachineFileNaming.ValidDestination("طرح۶.jc5", 4), "JC6 uses jc5 extension with 6");
            Check(!MachineFileNaming.ValidDestination("design.jc5", 4) && !MachineFileNaming.ValidDestination("design6.jc6", 4) && MachineFileNaming.ValidDestination("design.jc5", 1) && MachineFileNaming.ValidDestination("bonas.jc5", 5), "JC6 and Bonas naming validation");
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
            using (var zip = ZipFile.Open(colorV7, ZipArchiveMode.Update)) { var coloredManifestEntry = zip.GetEntry("manifest.json")!; string json; using (var r = new StreamReader(coloredManifestEntry.Open())) json = r.ReadToEnd(); coloredManifestEntry.Delete(); using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()); writer.Write(json.Replace("\"Version\":13", "\"Version\":7")); }
            Check(ProjectFile.Load(colorV7).Session.Patterns[0]!.WeftColors is null, "SAM v7 still loads without new visual metadata");
            string colorV8 = Path.Combine(dir, "colored-v8.sam"); File.Copy(colorProject, colorV8);
            using (var zip = ZipFile.Open(colorV8, ZipArchiveMode.Update)) { var coloredManifestV8 = zip.GetEntry("manifest.json")!; string json; using (var r = new StreamReader(coloredManifestV8.Open())) json = r.ReadToEnd(); coloredManifestV8.Delete(); using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()); writer.Write(json.Replace("\"Version\":13", "\"Version\":8")); }
            Check(ProjectFile.Load(colorV8).Session.Patterns[0]!.WeftColors!.SequenceEqual(restoredComposite.WeftColors), "SAM v8 composite colors remain compatible");

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
            var thresholdRanges = WarpRanges.Summarize(new WarpAnalysis(new[] {0,0,0,0,0,0}, 1, new double[] {-21,-20,20,20.1,90,-90}), 1, 2, 20);
            Check(thresholdRanges.LooseCount == 2 && thresholdRanges.TightCount == 1 && thresholdRanges.Loose[0].Start == 6 && thresholdRanges.Tight[0].Start == 4 && thresholdRanges.Tight[0].End == 5, "strict 20 percent threshold and ranking");
            var thresholdImage = DesignAnalysis.Heatmap(new WarpAnalysis(new[] {0,0,0,0}, 1, new double[] {-21,-20,20,21}), 1); var thresholdBytes = new byte[16]; thresholdImage.CopyPixels(thresholdBytes, 16, 0);
            Check(thresholdBytes[0] > thresholdBytes[2] && thresholdBytes[4] == thresholdBytes[6] && thresholdBytes[8] == thresholdBytes[10] && thresholdBytes[14] > thresholdBytes[12], "heat strip colors only deviations above 20 percent");
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
            var flat = FabricSimulation.Render(image, simSettings with { Mode = 0 }); var relief = FabricSimulation.Render(image, simSettings with { Mode = 1 });
            var flatBytes = new byte[flat.PixelWidth * flat.PixelHeight * 4]; var reliefBytes = new byte[flatBytes.Length]; flat.CopyPixels(flatBytes, flat.PixelWidth * 4, 0); relief.CopyPixels(reliefBytes, relief.PixelWidth * 4, 0);
            Check(!flatBytes.SequenceEqual(reliefBytes), "flat and relief rendering are distinct");
            Check(FabricSimulation.WarpOnTop(new byte[]{0,255}, 2, 0, 0, 0) == false && FabricSimulation.WarpOnTop(new byte[]{0,255}, 2, 1, 0, 0), "black places weft on top and white places warp on top");
            var structure = new SimulationSettings(WarpColor:"#CC0000", WeftColor:"#00AA00", Mode:0, WarpCount:"30", WarpDensity:"40", WeftDensity:"40");
            var oneCross = BitmapSource.Create(1,1,96,96,PixelFormats.Gray8,null,new byte[]{0},1); oneCross.Freeze();
            byte[] View(SimulationSettings value) { var im = FabricSimulation.Render(oneCross, value); var bytes = new byte[im.PixelWidth * im.PixelHeight * 4]; im.CopyPixels(bytes, im.PixelWidth * 4, 0); return bytes; }
            var thick = View(structure); var thin = View(structure with { WarpCount="5" });
            Check(!thick.SequenceEqual(thin), "warp yarn count changes rendered width");
            Check(!thick.SequenceEqual(View(structure with { Wefts = new[]{new SimulatedYarn("1",0,"#00AA00","5")} })), "weft yarn count changes rendered width");
            Check(!thick.SequenceEqual(View(structure with { MaterialDensity="2" })), "effective density changes rendered diameter");
            var normalOne=FabricSimulation.Render(oneCross,structure);var repeatedOne=FabricSimulation.Render(oneCross,structure with {RepeatX=2,RepeatY=3});
            Check(repeatedOne.PixelWidth==normalOne.PixelWidth*2 && repeatedOne.PixelHeight==normalOne.PixelHeight*3,"2D simulation uses bounded direct repeat without modifying source");
            var rearPixels=View(structure with {ReverseSide=true});var frontPixels=View(structure);Check(!frontPixels.SequenceEqual(rearPixels),"front and reverse preview use different over-under crossings");
            Expect<ArgumentException>(()=>FabricSimulation.Validate(structure with {RepeatX=17}));
            var defaultSim=System.Text.Json.JsonSerializer.Deserialize<SimulationSettings>("{}");Check(defaultSim!.RepeatX==1 && defaultSim.RepeatY==1 && !defaultSim.ReverseSide,"older simulation settings get compatible defaults");
            var differentSpacing = FabricSimulation.Render(image, structure with { WarpDensity="20", WeftDensity="40" });
            Check(differentSpacing.PixelWidth == 96 && differentSpacing.PixelHeight == 32, "fabric aspect ratio follows physical warp and weft spacing");
            var noGap = View(structure with { WarpDensity="50", WeftDensity="50" }); var moreGap = View(structure with { WarpDensity="20", WeftDensity="20" });
            int White(byte[] b) => Enumerable.Range(0,b.Length/4).Count(i => b[i*4] >= 230 && b[i*4+1] >= 230 && b[i*4+2] >= 230);
            Check(White(moreGap) > White(noGap), "lower thread density creates visible openings");
            var crossingMap = BitmapSource.Create(2,1,96,96,PixelFormats.Gray8,null,new byte[]{0,255},2); var crossing = FabricSimulation.Render(crossingMap, structure); var crossingBytes = new byte[crossing.PixelWidth * crossing.PixelHeight * 4]; crossing.CopyPixels(crossingBytes, crossing.PixelWidth * 4, 0);
            int weftPixel = (8 * crossing.PixelWidth + 8) * 4, warpPixel = (8 * crossing.PixelWidth + 24) * 4;
            Check(crossingBytes[weftPixel+1] > crossingBytes[weftPixel+2] && crossingBytes[warpPixel+2] > crossingBytes[warpPixel+1], "normal simulation draws black map pixels as weft and white as warp");
            int nearJoin = (8 * crossing.PixelWidth + 15) * 4; Check(crossingBytes[nearJoin+1] < crossingBytes[weftPixel+1], "weft softly darkens approaching an overlying warp");
            Check(Math.Abs(FabricSimulation.Number("۱٫۵") - 1.5) < .001, "Persian decimal input");
            Expect<ArgumentException>(() => FabricSimulation.Validate(structure with { WarpDensity="0" }));
            Expect<ArgumentException>(() => FabricSimulation.Validate(structure with { Wefts = new[]{new SimulatedYarn("1",0,"#00AA00","0")} }));
            Expect<ArgumentException>(() => FabricSimulation.Validate(simSettings with { Mode = 3 }));
            var samplePixels = new byte[128 * 64]; for (int y = 0; y < 64; y++) for (int x = 0; x < 128; x++) samplePixels[y * 128 + x] = x < 64 ? (byte)0 : (byte)255;
            var sampleImage = BitmapSource.Create(128, 64, 96, 96, PixelFormats.Gray8, null, samplePixels, 128); sampleImage.Freeze();
            var sampleRows = Enumerable.Range(0,64).Select(y => y < 32 ? "1" : "2").ToArray(); var overview = FabricOverview.Create(sampleImage, sampleRows, 0); var overviewPixels = DesignAnalysis.ReadBinary(overview.Image);
            Check(overview.Image.PixelWidth == 32 && overview.Image.PixelHeight == 32 && overview.ScaleX == 4 && overview.ScaleY == 2 && overviewPixels[0] == 0 && overviewPixels[31] == 255 && overview.RowWefts[0] == "1" && overview.RowWefts[^1] == "2", "3D samples full extent with representative applied weft rows");
            Expect<ArgumentException>(() => FabricOverview.Create(sampleImage, new[] { "1" }, 0));
            var v10Settings = simSettings with { Mode = 2 }; string v10File = Path.Combine(dir, "sim-v10.sam");
            ProjectFile.Save(v10File, new ProjectState(autoSession,"sample.bmp","3","2","50","Simulation",false,1,autoSession.Patterns.Select(_ => (string?)"pattern").ToArray(), Simulation:v10Settings), Array.Empty<string>());
            Check(ProjectFile.Load(v10File).Simulation!.Mode == 2, "SAM v10 preserves simulation mode");
            using (var zip = ZipFile.Open(v10File, ZipArchiveMode.Update)) { var old = zip.GetEntry("manifest.json")!; string json; using (var r = new StreamReader(old.Open())) json = r.ReadToEnd(); old.Delete(); using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()); writer.Write(json.Replace("\"Version\":13", "\"Version\":9")); }
            Check(ProjectFile.Load(v10File).Simulation!.Mode == 1, "SAM v9 defaults to the legacy relief appearance");
            var multiSettings = simSettings with { WarpCount = "۲۰", WarpUnit = 3, WarpDensity = "۳۵", WeftDensity = "۲۵", MaterialDensity = "۰٫۸", Wefts = new[] { new SimulatedYarn("1", 1, "#FF0000", "40", 0), new SimulatedYarn("2", 2, "#00FF00", "30", 3), new SimulatedYarn("3", 0, "#0000FF", "150", 1) } };
            var whiteMap = BitmapSource.Create(1, 2, 96, 96, PixelFormats.Gray8, null, new byte[]{255,255}, 1);
            var blackMap = BitmapSource.Create(1, 2, 96, 96, PixelFormats.Gray8, null, new byte[]{0,0}, 1);
            var multiImage = FabricSimulation.Render(blackMap, multiSettings with { Weave = 0 }, new[]{"1","2"});
            var simColors = new byte[multiImage.PixelWidth * multiImage.PixelHeight * 4]; multiImage.CopyPixels(simColors, multiImage.PixelWidth * 4, 0);
            int redIndex = ((multiImage.PixelHeight / 4) * multiImage.PixelWidth + multiImage.PixelWidth / 2) * 4, greenIndex = ((multiImage.PixelHeight * 3 / 4) * multiImage.PixelWidth + multiImage.PixelWidth / 2) * 4;
            Check(simColors[redIndex+2] > simColors[redIndex+1] && simColors[greenIndex+1] > simColors[greenIndex+2], "applied row sequence chooses the corresponding weft color");
            string multiFile = Path.Combine(dir, "multi-weft.sam");
            ProjectFile.Save(multiFile, new ProjectState(autoSession,"sample.bmp","3","2","50","Simulation",false,1,autoSession.Patterns.Select(_ => (string?)"pattern").ToArray(), Simulation:multiSettings), Array.Empty<string>());
            var multiRestored = ProjectFile.Load(multiFile); Check(multiRestored.Simulation!.Wefts!.SequenceEqual(multiSettings.Wefts!) && multiRestored.Simulation.WarpCount == "۲۰" && multiRestored.Simulation.WarpDensity == "۳۵" && multiRestored.Simulation.MaterialDensity == "۰٫۸", "per-weft counts and fabric parameters survive SAM v11");
            string simV10 = Path.Combine(dir, "legacy-no-geometry.sam"); File.Copy(multiFile, simV10);
            using (var zip = ZipFile.Open(simV10, ZipArchiveMode.Update))
            {
                var old = zip.GetEntry("manifest.json")!; string json; using (var r = new StreamReader(old.Open())) json = r.ReadToEnd(); old.Delete();
                var root = System.Text.Json.Nodes.JsonNode.Parse(json)!; root["Version"] = 10; var simulation = root["Simulation"]!;
                foreach (string field in new[]{"WarpCount", "WarpUnit", "WarpDensity", "WeftDensity", "MaterialDensity"}) ((System.Text.Json.Nodes.JsonObject)simulation).Remove(field);
                foreach (var item in simulation["Wefts"]!.AsArray()) { ((System.Text.Json.Nodes.JsonObject)item!).Remove("Count"); ((System.Text.Json.Nodes.JsonObject)item!).Remove("Unit"); }
                using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()); writer.Write(root.ToJsonString());
            }
            var legacySimulation = ProjectFile.Load(simV10).Simulation!; Check(legacySimulation.WarpCount == "30" && legacySimulation.WarpDensity == "40" && legacySimulation.Wefts![0].Count == "30" && legacySimulation.Mode == multiSettings.Mode, "SAM v10 without geometry loads safe defaults");
            string simV6 = Path.Combine(dir, "simulation-v6.sam"); File.Copy(multiFile, simV6);
            using (var zip = ZipFile.Open(simV6, ZipArchiveMode.Update))
            {
                var manifest = zip.GetEntry("manifest.json")!; string json; using (var reader = new StreamReader(manifest.Open())) json = reader.ReadToEnd();
                manifest.Delete(); var replacement = zip.CreateEntry("manifest.json"); using var writer = new StreamWriter(replacement.Open()); writer.Write(json.Replace("\"Version\":13", "\"Version\":6"));
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
            Check(MachineComposer.ParseNumericSequence("۱۲ 3.۴،10;۲").SequenceEqual(new[] { "12", "3", "4", "10", "2" }), "numeric weft sequence handles arbitrary separators and multi-digit Persian values");
            var separated = MachineComposer.Compose(image, absolute with { WeftCount = "12", DesignStart = "23", Sequence = "۱۲ 3.۴،10" });
            Check(separated.RowWefts.SequenceEqual(new[] { "12", "3" }), "absolute placement uses separated multi-digit sequence");
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
                item.Delete(); var replacement = zip.CreateEntry("manifest.json"); using var writer = new StreamWriter(replacement.Open()); writer.Write(json.Replace("\"Version\":13", "\"Version\":4"));
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
            var rgb256 = Enumerable.Range(0,256).SelectMany(i => new byte[] { (byte)i, (byte)(255-i), (byte)(i ^ 93), 255 }).ToArray();
            var indexedInput = BitmapSource.Create(256,1,96,96,PixelFormats.Bgra32,null,rgb256,1024); indexedInput.Freeze(); var indexedSession = new WorkflowSession(indexedInput); indexedSession.ApplySize(256,1); indexedSession.ApplyColors(256);
            Check(indexedSession.Palette!.Colors.Length == 256 && indexedSession.ColorPreview!.Format == PixelFormats.Indexed8, "initial indexed 8-bit palette includes 256 colors");
            var indexedPixels = new byte[rgb256.Length]; new FormatConvertedBitmap(indexedSession.ColorPreview, PixelFormats.Bgra32, null, 0).CopyPixels(indexedPixels,1024,0); Check(indexedPixels.SequenceEqual(rgb256), "initial indexed preview preserves every original color");
            var lockedSession = indexedSession.Snapshot(); lockedSession.SetColorLock(255,true); int lockedRgb = lockedSession.Palette!.Colors[255]; int lockedPixel = Array.IndexOf(lockedSession.Palette.Indices, (byte)255); var lockSnapshot = lockedSession.Snapshot();
            lockedSession.SetColorLock(0,true); Expect<ArgumentException>(() => lockedSession.ApplyColors(1)); Check(lockedSession.Palette!.Colors.Length == 256, "limit below locked count leaves palette unchanged");
            lockedSession.SetColorLock(0,false); lockedSession.ApplyColors(2); Check(lockedSession.Palette!.Colors.Contains(lockedRgb) && lockedSession.LockedColors.Count(x=>x) == 1 && lockedSession.Palette.Colors[lockedSession.Palette.Indices[lockedPixel]] == lockedRgb, "locked RGB and its pixels survive reduction");
            int lockedIndex = Array.IndexOf(lockedSession.Palette.Colors, lockedRgb); Expect<ArgumentException>(() => lockedSession.RemoveColors(new[]{lockedIndex}));
            string lockedPath = Path.Combine(dir,"locked.sam"); ProjectFile.Save(lockedPath,new ProjectState(lockedSession,"256.bmp","256","1","2","Colors",false,1,new string?[lockedSession.Patterns.Length]),Array.Empty<string>());
            var lockedRestored = ProjectFile.Load(lockedPath).Session; Check(lockedRestored.LockedColors.SequenceEqual(lockedSession.LockedColors) && lockSnapshot.LockedColors[255] && !lockSnapshot.LockedColors[0], "SAM and undo snapshot preserve locks independently");
            indexedSession.AssignPatterns(Enumerable.Range(0,256).Select(_ => new WeavePattern(1,1,new byte[]{0})).ToArray()); indexedSession.TogglePixel(0,0);
            string indexedPath = Path.Combine(dir,"indexed256.sam"); ProjectFile.Save(indexedPath,new ProjectState(indexedSession,"256.bmp","256","1","256","Patterns",false,5,Enumerable.Repeat<string?>("pattern",256).ToArray(), Machine:setup),Array.Empty<string>());
            var indexedLoaded = ProjectFile.Load(indexedPath); Check(indexedLoaded.MachineFormatIndex == 5 && indexedLoaded.Session.Palette!.Colors.Length == 256 && indexedLoaded.Session.Patterns.All(p => p is not null), "256 assigned patterns and Bonas JC5 profile survive SAM");
            var adaptiveInputBytes = new List<byte>(); for (int i=0;i<300;i++) for (int repeat=0;repeat<2;repeat++) adaptiveInputBytes.AddRange(new byte[]{80,(byte)(80+i/32),(byte)(80+i%32),255}); adaptiveInputBytes.AddRange(new byte[]{255,0,0,255});
            var adaptiveInput = BitmapSource.Create(601,1,96,96,PixelFormats.Bgra32,null,adaptiveInputBytes.ToArray(),601*4); var adaptive = DesignProcessor.Prepare(adaptiveInput,601,1,256);
            Check(adaptive.SourceColorCount == 301 && adaptive.Colors.Length <= 256 && adaptive.Colors.Contains(0x0000FF), "adaptive palette preserves a small visually distinct region");
            var allReduced = DesignProcessor.Prepare(indexedInput,256,1,17);
            for (int i=0;i<256;i++) { int rgb = (rgb256[4*i+2]<<16)|(rgb256[4*i+1]<<8)|rgb256[4*i]; int Distance(int c) { int r=((rgb>>16)&255)-((c>>16)&255), g=((rgb>>8)&255)-((c>>8)&255), bl=(rgb&255)-(c&255); return r*r+g*g+bl*bl; } int expected = Enumerable.Range(0,allReduced.Colors.Length).OrderBy(k => Distance(allReduced.Colors[k])).ThenBy(k => k).First(); Check(allReduced.Indices[i] == expected, "palette acceleration equals brute-force nearest color"); }

            var fiftyImage = BitmapSource.Create(50, 1, 96, 96, PixelFormats.Bgra32, null, many.Take(200).ToArray(), 200);
            var fullProject = new WorkflowSession(fiftyImage); fullProject.ApplySize(50,1); fullProject.ApplyColors(50);
            Check(fullProject.Palette!.Colors.Length == 50, "maximum palette fixture");
            for (int i = 0; i < 50; i++) fullProject.AssignPattern(i, new WeavePattern(1,1,new byte[] {0}));
            fullProject.TogglePixel(0,0); string fullProjectPath = Path.Combine(dir,"full-50.sam");
            ProjectFile.Save(fullProjectPath, new ProjectState(fullProject,"many.bmp","50","1","50","Floats",false,1,Enumerable.Repeat<string?>("pattern",50).ToArray(), null, null, setup), Array.Empty<string>());
            Check(ProjectFile.Load(fullProjectPath).Session.PixelOverrides![0] == 2 && ProjectFile.Load(fullProjectPath).Machine?.LeftPattern is not null, "50 weaves, overrides and two selvedges fit SAM entry limit");
            var less = DesignProcessor.Prepare(manyImage, 10, 10, 3);
            Check(less.Colors.Length <= 3 && less.Counts.Sum() == 100, "requested color reduction");
            Expect<ArgumentOutOfRangeException>(() => DesignProcessor.Prepare(manyImage, 10, 10, 257));
            Expect<ArgumentOutOfRangeException>(() => DesignProcessor.Prepare(manyImage, 4001, 1000, 5));
            var transparent = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] {0, 0, 0, 0}, 4);
            Check(DesignProcessor.Prepare(transparent, 1, 1, 1).Colors.Single() == 0xFFFFFF, "transparent pixels become white without blending");
            var alphaBytes = new byte[] { 0,0,0,0, 0,0,255,128, 255,0,0,127, 0,255,0,255 };
            var alphaImage = BitmapSource.Create(4, 1, 96, 96, PixelFormats.Bgra32, null, alphaBytes, 16);
            var alphaPng = new PngBitmapEncoder(); alphaPng.Frames.Add(BitmapFrame.Create(alphaImage)); string alphaPath = Path.Combine(dir, "transparent.png"); using (var f = File.Create(alphaPath)) alphaPng.Save(f);
            var alphaSession = new WorkflowSession(ImageLoader.Load(alphaPath, 0).Image); alphaSession.ApplySize(4, 1); alphaSession.ApplyColors(50);
            Check(alphaSession.Palette!.Colors.SequenceEqual(new[] { 0x00FF00, 0xFF0000, 0xFFFFFF }), "transparent PNG opens and maps alpha by threshold without extra colors");
            var largeRaw = new byte[2001 * 2000 * 4]; for (int i = 3; i < largeRaw.Length; i += 4) largeRaw[i] = 255;
            var largeInput = BitmapSource.Create(2001, 2000, 96, 96, PixelFormats.Bgra32, null, largeRaw, 2001 * 4); largeInput.Freeze();
            Check(DesignProcessor.Resize(largeInput, 1000, 1000).PixelWidth == 1000, "source over four million pixels can be resized to limit");
            var frequencies = new[] {0xCC0000,0xCC0000,0xCC0000,0xCC0000,0xCC0000,0x00CC00,0x00CC00,0x00CC00,0x0000CC};
            byte[] freqBytes = frequencies.SelectMany(c => new byte[] { (byte)c, (byte)(c >> 8), (byte)(c >> 16), 255 }).ToArray();
            var freqImage = BitmapSource.Create(frequencies.Length, 1, 96, 96, PixelFormats.Bgra32, null, freqBytes, frequencies.Length * 4);
            var frequentPalette = DesignProcessor.Prepare(freqImage, frequencies.Length, 1, 2);
            Check(frequentPalette.Colors.SequenceEqual(new[] { 0x00CC00, 0xCC0000 }) && frequentPalette.Counts.Sum() == frequencies.Length, "automatic reduction retains most frequent exact colors");
            byte[] highlightPixels = new byte[frequencies.Length * 4]; DesignProcessor.RenderHighlight(frequentPalette, 0).CopyPixels(highlightPixels, highlightPixels.Length, 0);
            Check(highlightPixels[5 * 4 + 1] == 0xCC && highlightPixels[0 * 4 + 2] != 0xCC && frequentPalette.Colors[1] == 0xCC0000, "highlight mutes others without modifying palette");
            Check(highlightPixels[4 * 4 + 2] == 255 && highlightPixels[4 * 4 + 1] == 0 && highlightPixels[0 * 4] >= 230 && highlightPixels[0 * 4 + 1] >= 230, "highlight uses red boundary and fades distant colors");
            var batchSession = restored.Session.Snapshot(); var originalPatterns = batchSession.Snapshot(); batchSession.AssignPatterns(Enumerable.Repeat(PatternLibrary.Presets[5].Pattern, batchSession.Patterns.Length).ToArray());
            Check(batchSession.Patterns.All(p => p is not null) && originalPatterns.Patterns[0]!.Values.SequenceEqual(new byte[]{0}), "batch random assignment can be undone using snapshot");
            var originalMap = DesignAnalysis.ReadBinary(batchSession.PatternPreview!); var originalPalette = batchSession.Palette!.Colors[0]; batchSession.Recolor(0, 0x123456);
            Check(batchSession.Palette!.Colors[0] == 0x123456 && originalPatterns.Palette!.Colors[0] == originalPalette && DesignAnalysis.ReadBinary(batchSession.PatternPreview!).SequenceEqual(originalMap), "swatch recoloring retains weave and undo snapshot");
            Expect<ArgumentException>(() => batchSession.Recolor(0, batchSession.Palette!.Colors[1]));
            Expect<ArgumentException>(() => batchSession.AssignPatterns(Array.Empty<WeavePattern>()));
            Check(PatternLibrary.Presets.Select(p => p.Category).Distinct().Count() >= 7 && PatternLibrary.Presets[0].Name == "p4-4_clb", "standard categorized bank preserves legacy preset indices");
            var gray = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] {128, 128, 128, 255}, 4);
            Expect<InvalidDataException>(() => DesignProcessor.ReadPattern(gray));
            var pure = DesignProcessor.ReadPattern(image);
            Check(pure.Width == 3 && pure.Height == 2 && pure.Values.SequenceEqual(pixels), "binary pattern");
            File.Delete(bmpPath); // decoder must not retain a file handle
            var app = new App { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
            Check(GraphGuideWindow.Search("راپورت").Any(t=>t.Title.Contains("فلوت")) && GraphGuideWindow.Search("no-such-topic-01923").Count==0, "offline graph guide searches titles and body");
            Check(GuideDiagrams.Create("راهنمای تصویری شبیه‌سازی") is System.Windows.Controls.StackPanel, "graph guide supplies original illustration");
            var simulationEditor=new SimulationEditorWindow(b.Image,Array.Empty<string>(),new SimulationSettings(Mode:0));var simRoot=(System.Windows.Controls.DockPanel)simulationEditor.Content;var simColumns=(System.Windows.Controls.Grid)simRoot.Children[1];Check(simColumns.ColumnDefinitions.Count==2,"simulation editor has preview and settings columns");
            var simFooter=(System.Windows.Controls.StackPanel)simRoot.Children[0];var simApply=simFooter.Children.OfType<System.Windows.Controls.Button>().Single(button=>Equals(button.Content,"اعمال و بستن"));var simTicks=0;var simTimer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(20)};
            simTimer.Tick+=(_,_)=>{if(++simTicks>500){simTimer.Stop();simulationEditor.Close();}else if(simApply.IsEnabled && simTicks>1){simTimer.Stop();simApply.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}};simTimer.Start();
            Check(simulationEditor.ShowDialog()==true && simulationEditor.Result is {RepeatX:1,RepeatY:1,Mode:0},"simulation editor renders and commits draft through apply");
            var cancelEditor=new SimulationEditorWindow(b.Image,Array.Empty<string>(),new SimulationSettings(Mode:0));var cancelRoot=(System.Windows.Controls.DockPanel)cancelEditor.Content;var cancelColumns=(System.Windows.Controls.Grid)cancelRoot.Children[1];var simSettingsPanel=(System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)cancelColumns.Children[1]).Content;var cancelFooter=(System.Windows.Controls.StackPanel)cancelRoot.Children[0];var simCancel=cancelFooter.Children.OfType<System.Windows.Controls.Button>().Single(button=>Equals(button.Content,"انصراف"));var simSave=cancelFooter.Children.OfType<System.Windows.Controls.Button>().Single(button=>Equals(button.Content,"ذخیره تصویر"));var cancelTicks=0;bool staleDisabled=false;var cancelTimer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(20)};
            cancelTimer.Tick+=(_,_)=>{if(++cancelTicks>500){cancelTimer.Stop();cancelEditor.Close();}else if(simSave.IsEnabled){simSettingsPanel.Children.OfType<System.Windows.Controls.TextBox>().First().Text="2";staleDisabled=!simSave.IsEnabled;cancelTimer.Stop();simCancel.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}};cancelTimer.Start();Check(cancelEditor.ShowDialog()==false && cancelEditor.Result is null && staleDisabled,"simulation edits invalidate preview and cancel discards draft");
            var multiEditor=new SimulationEditorWindow(b.Image,new[]{"1","2"},new SimulationSettings(Mode:0,Wefts:new[]{new SimulatedYarn("1",0,"#FF0000","20",0),new SimulatedYarn("2",1,"#00FF00","25",1),new SimulatedYarn("extra",2,"#0000FF","31",0)}));
            var multiRoot=(System.Windows.Controls.DockPanel)multiEditor.Content;var multiGrid=(System.Windows.Controls.Grid)multiRoot.Children[1];var multiPanel=(System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)multiGrid.Children[1]).Content;multiPanel.Children.OfType<System.Windows.Controls.TextBox>().Last().Text="42";
            var readSimulationDraft=typeof(SimulationEditorWindow).GetMethod("ReadDraft",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;var simulationDraft=(SimulationSettings)readSimulationDraft.Invoke(multiEditor,null)!;Check(simulationDraft.Wefts!.Single(x=>x.Id=="2").Count=="42" && simulationDraft.Wefts.Single(x=>x.Id=="extra").Color=="#0000FF","per-weft editor updates active row and preserves inactive yarn settings");multiEditor.Close();
            var graphGuide=new GraphGuideWindow();Check(graphGuide.Title.Contains("راهنما"),"offline graph guide creates successfully");graphGuide.Close();
            var reportPages=GraphReport.Render(reportValues); Check(reportPages.Count>=2 && reportPages.All(p=>p.IsFrozen), "portfolio combines connected results and paginated palette");
            string graphReportPath=Path.Combine(dir,"graph-report.pdf"); DesignReport.SavePdf(reportPages,graphReportPath,Array.Empty<string>()); Check(File.ReadAllText(graphReportPath,System.Text.Encoding.Latin1).Contains("/Count " + reportPages.Count), "graph report exports multipage PDF");
            var moduleWindow = new ModuleWindow(); var moduleFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var moduleGraph = (ModuleGraph)typeof(ModuleWindow).GetField("graph",moduleFlags)!.GetValue(moduleWindow)!;
            static double Brightness(Brush brush) { var c=((SolidColorBrush)brush).Color; return .2126*c.R+.7152*c.G+.0722*c.B; }
            ThemeManager.Apply(false,false); Check(Brightness((Brush)app.FindResource("PrimaryText")) > Brightness((Brush)app.FindResource("GraphNode")) + 100, "dark theme text contrasts with graph nodes");
            ThemeManager.Apply(true,false); Check(Brightness((Brush)app.FindResource("PrimaryText")) + 100 < Brightness((Brush)app.FindResource("GraphNode")), "light theme text contrasts with graph nodes");
            ThemeManager.Apply("calm",false); Check(Brightness((Brush)app.FindResource("PrimaryText")) + 100 < Brightness((Brush)app.FindResource("GraphNode")), "calm theme text contrasts with nodes");
            ThemeManager.Apply("ocean",false); Check(Brightness((Brush)app.FindResource("PrimaryText")) > Brightness((Brush)app.FindResource("GraphNode")) + 100, "ocean theme text contrasts with nodes"); ThemeManager.Apply(true,false);
            var themedInput=new System.Windows.Controls.TextBox(); ((System.Windows.Controls.StackPanel)moduleWindow.FindName("Inspector")).Children.Add(themedInput); themedInput.ApplyTemplate(); Check(Brightness(themedInput.Foreground)+100 < Brightness(themedInput.Background), "light themed text input is readable");
            var themedCombo=new System.Windows.Controls.ComboBox { ItemsSource=new[]{"one", "two"}, SelectedIndex=0 }; ((System.Windows.Controls.StackPanel)moduleWindow.FindName("Inspector")).Children.Add(themedCombo); themedCombo.ApplyTemplate(); Check(themedCombo.Template.FindName("PART_Popup",themedCombo) is System.Windows.Controls.Primitives.Popup, "themed combo retains dropdown popup");
            ThemeManager.Apply(false,false); Check(Brightness(themedInput.Foreground) > Brightness(themedInput.Background)+100, "existing inputs update on dark theme change");
            var moduleLibrary = (System.Windows.Controls.StackPanel)moduleWindow.FindName("ModuleLibrary");
            Check(moduleLibrary.Children.Count == 31 && moduleGraph.Nodes.Count == 10 && moduleGraph.Edges.Count == 9, "module workspace initializes with a connected starter graph and library");
            ((System.Windows.Controls.DockPanel)moduleLibrary.Children[15]).Children.OfType<System.Windows.Controls.Button>().Last().RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var addedResize = moduleGraph.Nodes.Last(); Check(moduleGraph.Nodes.Count == 11 && addedResize.Kind == ModuleKind.Resize, "adding a second independent module through the library");
            var firstLibraryGroup=(System.Windows.Controls.DockPanel)moduleLibrary.Children[0];var aiLibraryGroup=(System.Windows.Controls.DockPanel)moduleLibrary.Children[10];
            Check(System.Windows.Controls.DockPanel.GetDock(firstLibraryGroup.Children[0])==System.Windows.Controls.Dock.Left && firstLibraryGroup.Children[1] is System.Windows.Controls.Border && System.Windows.Controls.DockPanel.GetDock(aiLibraryGroup.Children[1])==System.Windows.Controls.Dock.Left && aiLibraryGroup.Children[1] is System.Windows.Controls.Button,"library help and AI settings use reserved left slots so right icons align");
            var nodeLayer = (System.Windows.Controls.Canvas)moduleWindow.FindName("NodeLayer");
            System.Windows.Controls.Button Port(int index, string tip) { var card=(System.Windows.Controls.Border)nodeLayer.Children[index]; var inner=(System.Windows.Controls.Grid)card.Child; var ports=(System.Windows.Controls.Grid)inner.Children[1]; return ports.Children.OfType<System.Windows.Controls.Button>().Single(b => ((string?)b.ToolTip)?.Contains(tip)==true); }
            Port(0,"انتخاب خروجی").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Port(10,"اتصال ورودی").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(moduleGraph.Edges.Any(e => e.From == moduleGraph.Nodes[0].Id && e.To == addedResize.Id && e.Port == "image"), "workspace port clicks create a real compatible branch");
            var selectedEdge=moduleGraph.Edges.Single(e=>e.To==addedResize.Id && e.Port=="image");
            typeof(ModuleWindow).GetField("selectedWire",moduleFlags)!.SetValue(moduleWindow,selectedEdge);
            typeof(ModuleWindow).GetMethod("DeleteSelection",moduleFlags)!.Invoke(moduleWindow,null);
            Check(!moduleGraph.Edges.Contains(selectedEdge) && moduleGraph.Nodes.Contains(addedResize), "deleting a selected wire leaves the connected nodes intact");
            var cropDialog=new GraphCropWindow(inputImage,cropNode.Crop,inputHash); var cropRoot=(System.Windows.Controls.DockPanel)cropDialog.Content; var cropToolbar=(System.Windows.Controls.WrapPanel)cropRoot.Children[0];
            var applyCrop=cropToolbar.Children.OfType<System.Windows.Controls.Button>().Single(b=>Equals(b.Content,"اعمال تنظیمات و بستن"));
            cropDialog.Loaded+=(_,_)=>cropDialog.Dispatcher.BeginInvoke(new Action(()=>applyCrop.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent))));
            Check(cropDialog.ShowDialog()==true && cropDialog.Result==cropNode.Crop, "crop window commits the current boundary on Apply");
            var graphEditor=new MainWindow(); graphEditor.ConfigureModuleEditor(ModuleKind.Colors,editing);
            Check(((System.Windows.Controls.Grid)graphEditor.FindName("Navigation")).Visibility==System.Windows.Visibility.Collapsed && ((System.Windows.Controls.Button)graphEditor.FindName("ModuleApplyButton")).Visibility==System.Windows.Visibility.Visible, "graph color editor opens legacy palette UI with apply and without navigation");
            graphEditor.Close();
            var weaveEditorWindow=new MainWindow(); weaveEditorWindow.ConfigureWeaveModuleEditor(PatternLibrary.Presets[0].Pattern,"test weave");
            var weaveGallery=(System.Windows.Controls.ListBox)weaveEditorWindow.FindName("BankList"); Check(weaveGallery.ItemTemplate==app.FindResource("WeaveGalleryItem") && weaveGallery.ItemsPanel==app.FindResource("WeaveGalleryPanel"), "legacy bank uses compact weave previews");
            var weaveGrid=(WeaveEditor)weaveEditorWindow.FindName("WeaveCanvas"); weaveGrid.ApplyHelper("invert"); var expectedWeave=weaveGrid.Snapshot();
            var waitTicks=0; var busyField=typeof(MainWindow).GetField("busy",moduleFlags)!; var applyTimer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(20) };
            applyTimer.Tick+=(_,_)=>{ if (++waitTicks > 1000) { applyTimer.Stop(); weaveEditorWindow.Close(); return; } if (!(bool)busyField.GetValue(weaveEditorWindow)!) { applyTimer.Stop(); ((System.Windows.Controls.Button)weaveEditorWindow.FindName("ModuleApplyButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); } }; applyTimer.Start();
            Check(weaveEditorWindow.ShowDialog()==true && weaveEditorWindow.ModuleWeaveResult!.Values.SequenceEqual(expectedWeave.Values) && weaveEditorWindow.ModuleWeaveName=="test weave", "full legacy weave designer applies edited pattern to the graph");

            // Exercise the actual card action and connected upstream evaluation, not only the dialog constructor.
            moduleGraph.Nodes[0].ImagePng=ModuleExecutor.EmbedImage(b.Image);
            var liveCrop=moduleGraph.Add(ModuleKind.Crop,850,650); moduleGraph.Connect(moduleGraph.Nodes[0].Id,liveCrop.Id,"image");
            typeof(ModuleWindow).GetMethod("RenderGraph",moduleFlags)!.Invoke(moduleWindow,null); moduleWindow.Show();
            bool cropOpened=false,cropLoaded=false; int cropTicks=0; var cropTimer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(20) };
            cropTimer.Tick+=(_,_)=>{
                var opened=app.Windows.OfType<GraphCropWindow>().FirstOrDefault(w=>w.IsVisible); if(opened is null) { if(++cropTicks>300) cropTimer.Stop(); return; }
                cropOpened=true; var toolbar=(System.Windows.Controls.WrapPanel)((System.Windows.Controls.DockPanel)opened.Content).Children[0];
                var apply=toolbar.Children.OfType<System.Windows.Controls.Button>().Single(x=>Equals(x.Content,"اعمال تنظیمات و بستن"));
                if(apply.IsEnabled || ++cropTicks>300) { cropLoaded=apply.IsEnabled; cropTimer.Stop(); opened.DialogResult=false; }
            };
            cropTimer.Start(); var liveCard=(System.Windows.Controls.Border)nodeLayer.Children[nodeLayer.Children.Count-1]; var liveInner=(System.Windows.Controls.Grid)liveCard.Child;
            var headerBorder=(System.Windows.Controls.Border)liveInner.Children[0]; var headerGrid=(System.Windows.Controls.Grid)headerBorder.Child; var editButton=headerGrid.Children.OfType<System.Windows.Controls.Button>().Single();
            editButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); cropTimer.Stop();
            Check(cropOpened && cropLoaded, "crop card button opens dialog and loads its connected input");
            typeof(ModuleWindow).GetField("dirty",moduleFlags)!.SetValue(moduleWindow,false); moduleWindow.Close();
            Check(AppVersion.Display == "035.0.0.0-a1", "QAVNS version from version.txt");
            var paletteRows = indexedSession.Palette!.Colors.Select((c,i) => new PaletteEntry { Index=i, ColorCode=$"#{c:X6}", PixelCount=indexedSession.Palette.Counts[i], Percent=100d*indexedSession.Palette.Counts[i]/256, Brush=new SolidColorBrush(Color.FromRgb((byte)(c>>16),(byte)(c>>8),(byte)c)) }).ToArray();
            var paletteSheet = PaletteImage.Render(paletteRows,false); Check(paletteSheet.PixelWidth > 1000 && paletteSheet.PixelHeight > 2000, "palette sheet fits all 256 entries across columns");
            string paletteFile = Path.Combine(dir,"palette.png"); PaletteImage.Save(paletteSheet,paletteFile,Array.Empty<string>()); Check(ImageLoader.Load(paletteFile,0).Image.PixelWidth == paletteSheet.PixelWidth, "palette PNG opens at correct dimensions"); Expect<IOException>(() => PaletteImage.Save(paletteSheet,paletteFile,new[]{paletteFile}));
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
            var indexedEntries = indexedSession.Palette!.Colors.Select((color,index) => new PaletteEntry { Index=index, Brush=new SolidColorBrush(Color.FromRgb((byte)(color>>16),(byte)(color>>8),(byte)color)) }).ToArray();
            int reportedColors = 0; for (int pageNo=0;pageNo<6;pageNo++) { var colorPage=(System.Windows.Controls.Grid)DesignReport.CreatePage(indexedSession,indexedEntries,"256",null,true,palettePage:pageNo); DesignReport.Render(colorPage); var body=colorPage.Children.OfType<System.Windows.Controls.Grid>().Single(x => System.Windows.Controls.Grid.GetRow(x)==3); var cols=body.Children.OfType<System.Windows.Controls.Grid>().Single(); reportedColors += cols.Children.OfType<System.Windows.Controls.StackPanel>().Sum(x => x.Children.OfType<System.Windows.Controls.Grid>().Count()); }
            Check(reportedColors == 256, "all 256 palette entries appear on paginated report pages");
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
            int totalYarnRows = 0; foreach (System.Windows.Controls.Grid yarnPage in simulationPages) { DesignReport.Render(yarnPage); var table = yarnPage.Children.OfType<System.Windows.Controls.Grid>().Single(x => System.Windows.Controls.Grid.GetRow(x) == 3); totalYarnRows += table.Children.OfType<System.Windows.Controls.StackPanel>().Sum(x => x.Children.OfType<System.Windows.Controls.Grid>().Count() - 1); }
            Check(totalYarnRows == 51, "yarn table includes warp and all fifty wefts");
            var secondPage = DesignReport.Render(simulationPages[0]); string multiPdf = Path.Combine(dir, "multi-report.pdf");
            DesignReport.SavePdf(new[]{reportImage, secondPage}, multiPdf, Array.Empty<string>());
            Check(System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(multiPdf)).Contains("/Count 2"), "multi-page simulation PDF");
            var pdfImport = ImageLoader.Load(multiPdf, 1); Check(pdfImport.Format == "PDF" && pdfImport.PageCount == 2 && pdfImport.PageIndex == 1, "PDF page rasterization");
            string compatibleAi = Path.Combine(dir, "compatible.ai"); File.Copy(multiPdf, compatibleAi); Check(ImageLoader.Load(compatibleAi, 0).Format == "AI (PDF compatible)", "PDF-compatible AI input");
            Expect<ArgumentOutOfRangeException>(() => ImageLoader.Load(multiPdf, 2));
            var polarityWindow = new Fabric3DWindow(crossingMap, new[]{"1"}, structure with { Weave = 0 }, true);
            var polarityRoot = (System.Windows.Controls.DockPanel)polarityWindow.Content; var polarityViewport = (HelixToolkit.Wpf.HelixViewport3D)polarityRoot.Children[3];
            var polarityGroup = (System.Windows.Media.Media3D.Model3DGroup)((System.Windows.Media.Media3D.ModelVisual3D)polarityViewport.Children[1]).Content!;
            System.Windows.Media.Media3D.MeshGeometry3D MeshAt(int i) => (System.Windows.Media.Media3D.MeshGeometry3D)((System.Windows.Media.Media3D.GeometryModel3D)polarityGroup.Children[i]).Geometry!;
            Check(MeshAt(0).Positions[0].Z < 0 && MeshAt(1).Positions[0].Z > 0 && MeshAt(2).Positions[16].Z > 0 && MeshAt(2).Positions[48].Z < 0, "3D geometry puts weft over black cells and warp over white cells"); polarityWindow.Close();
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
            var apply3d = controls3d.Children.OfType<System.Windows.Controls.Button>().Single(x => x.Content as string == "Apply settings"); apply3d.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var newModel = (System.Windows.Media.Media3D.Model3DGroup)((System.Windows.Media.Media3D.ModelVisual3D)viewport3d.Children[1]).Content!; var newMesh = (System.Windows.Media.Media3D.MeshGeometry3D)((System.Windows.Media.Media3D.GeometryModel3D)newModel.Children[0]).Geometry!;
            Check(Math.Abs(newMesh.Positions[0].X - newMesh.Positions[4].X) > diameterBefore * 1.9, "3D geometry responds to yarn count"); wideWindow.Close();
            var overviewWindow = new Fabric3DWindow(overview.Image, overview.RowWefts, multiSettings with { Weave = 0, Mode = 2 }, true, overview: overview);
            var overviewRoot = (System.Windows.Controls.DockPanel)overviewWindow.Content; var overviewBar = (System.Windows.Controls.StackPanel)overviewRoot.Children[1];
            using (var watchdog3d = new System.Threading.Timer(_ => { Console.Error.WriteLine("3D capture timed out"); Environment.Exit(1); }, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan))
            {
                overviewWindow.ContentRendered += (_, _) => ((System.Windows.Controls.Button)overviewBar.Children[5]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(overviewWindow.ShowDialog() == true && overviewWindow.Captured is { PixelWidth: > 1, PixelHeight: > 1, IsFrozen: true } && overviewWindow.CapturedSettings!.Mode == 2, "3D overview captures an actual viewport and settings");
                var captured = overviewWindow.Captured!; int captureStride = captured.PixelWidth * 4; var capturedBytes = new byte[captureStride * captured.PixelHeight]; captured.CopyPixels(capturedBytes, captureStride, 0);
                Check(Enumerable.Range(0, capturedBytes.Length / 4).Select(i => BitConverter.ToInt32(capturedBytes, i * 4)).Distinct().Take(16).Count() >= 16, "3D viewport capture contains rendered scene rather than a blank bitmap");
            }
            var detailWindow = new Fabric3DWindow(whiteMap, new[]{"1","2"}, multiSettings with { Weave = 0 }, false);
            Check(detailWindow.FontFamily.Source.Contains("Vazirmatn"), "3D window uses project font");
            Check(detailWindow.Content is System.Windows.Controls.DockPanel, "Helix 3D detail window builds bounded geometry");
            var guide = new GuideWindow(false); Check(guide.Content is System.Windows.Controls.Grid && guide.Title.Contains("راهنمای"), "illustrated in-app guide loads with navigation");
            var window = new MainWindow();
            Check(window.FontFamily.Source.Contains("Vazirmatn"), "embedded font selected in main window");
            var pngWindow = new MainWindow(); var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            var priorContext = System.Threading.SynchronizationContext.Current;
            System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
            try
            {
                var loadPng = typeof(MainWindow).GetMethod("LoadAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                using var watchdog = new System.Threading.Timer(_ => { Console.Error.WriteLine("PNG UI loading timed out"); Environment.Exit(1); }, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
                var loadingPng = (Task)loadPng.Invoke(pngWindow, new object[] { alphaPath, 0, false })!; var frame = new System.Windows.Threading.DispatcherFrame();
                loadingPng.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
                System.Windows.Threading.Dispatcher.PushFrame(frame); loadingPng.GetAwaiter().GetResult();
                var loadedSession = (WorkflowSession?)typeof(MainWindow).GetField("session", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pngWindow);
                Check(loadedSession?.Resized?.PixelWidth == 4, "transparent PNG opens through the main window without crashing");
                loadedSession!.ApplyColors(256); var mainType = typeof(MainWindow); var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                mainType.GetMethod("RebuildPaletteEntries", flags)!.Invoke(pngWindow, new object?[] { null, null, -1 });
                var stageField = mainType.GetField("stage", flags)!; stageField.SetValue(pngWindow, Enum.Parse(stageField.FieldType, "Colors")); mainType.GetMethod("ShowStage", flags)!.Invoke(pngWindow, null);
                var paletteList = (System.Windows.Controls.ListView)pngWindow.FindName("ColorPaletteList"); var highlightButton = new System.Windows.Controls.Button { DataContext = paletteList.Items[0] };
                var highlightMethod = mainType.GetMethod("HighlightColor_Click", flags)!; highlightMethod.Invoke(pngWindow, new object[] { highlightButton, new System.Windows.RoutedEventArgs() });
                var highlightField = mainType.GetField("colorHighlight", flags)!; var highlightFrame = new System.Windows.Threading.DispatcherFrame();
                var highlightTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) }; highlightTimer.Tick += (_, _) => { if (highlightField.GetValue(pngWindow) is not null) { highlightTimer.Stop(); highlightFrame.Continue = false; } }; highlightTimer.Start(); System.Windows.Threading.Dispatcher.PushFrame(highlightFrame);
                Check(ReferenceEquals(((System.Windows.Controls.Image)pngWindow.FindName("PreviewImage")).Source, highlightField.GetValue(pngWindow)), "color highlight icon updates only the preview");
                highlightMethod.Invoke(pngWindow, new object[] { highlightButton, new System.Windows.RoutedEventArgs() });
                Check(highlightField.GetValue(pngWindow) is null && ReferenceEquals(((System.Windows.Controls.Image)pngWindow.FindName("PreviewImage")).Source, loadedSession.ColorPreview), "second icon click restores unmodified color preview");
                var paletteView = (System.Windows.Data.ListCollectionView)paletteList.ItemsSource; paletteView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(PaletteEntry.ColorNumber), System.ComponentModel.ListSortDirection.Descending));
                 mainType.GetMethod("ApplyColorRegionSelection", flags)!.Invoke(pngWindow, new object[] { loadedSession.Palette!, new System.Windows.Point(10,0), new System.Windows.Point(29,9), 40d, 10d });
                var expectedSelection = loadedSession.Palette!.Indices.Skip(1).Take(2).Select(x => (int)x).Distinct().Order().ToArray();
                Check(paletteList.SelectedItems.OfType<PaletteEntry>().Select(x => x.Index).Order().SequenceEqual(expectedSelection), "image region selects correct palette rows after sorting");
                mainType.GetField("allowClose", flags)!.SetValue(pngWindow, true); pngWindow.Close();
            }
            finally { System.Threading.SynchronizationContext.SetSynchronizationContext(priorContext); }
            var pendingTotal = (System.Windows.Controls.TextBox)window.FindName("TotalHooksInput"); pendingTotal.Text = "۳۰";
            var readSetup = typeof(MainWindow).GetMethod("ReadMachineSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Check(((MachineSetup)readSetup.Invoke(window, null)!).TotalHooks == "" && window.FindName("ApplyWeftButton") is System.Windows.Controls.Button, "typing weft draft does not apply it");
            var loadMachineSettings = typeof(MainWindow).GetMethod("LoadMachineSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            loadMachineSettings.Invoke(window, new object?[] { absolute, null });
            Check(((System.Windows.Controls.TextBox)window.FindName("DesignStartInput")).Text == "21" && ((System.Windows.Controls.TextBox)window.FindName("WeftStartInput")).Text == "2", "old zero-based applied locations display without changing the stored map");
            var loadSimSettings = typeof(MainWindow).GetMethod("LoadSimulationSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            loadSimSettings.Invoke(window, new object?[] { multiSettings });
            Check(((System.Windows.Controls.StackPanel)window.FindName("SimulationWeftRows")).Children.Count == 3, "simulation editor has exactly the applied weft count");
            var rowControls = ((System.Windows.Controls.StackPanel)window.FindName("SimulationWeftRows")).Children.OfType<System.Windows.Controls.StackPanel>().First();
            Check(rowControls.Children.OfType<ColorSwatch>().Any() && rowControls.Children.OfType<System.Windows.Controls.TextBlock>().Last().Text.Contains("mm") && ((System.Windows.Controls.TextBlock)window.FindName("SimulationWarpGeometry")).Text.Contains("mm"), "simulation displays swatches and computed diameters and gaps");
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
            Check(window.FindName("CompositeButton") is System.Windows.Controls.Button && window.FindName("Bank3DButton") is System.Windows.Controls.Button && window.FindName("Editor3DButton") is System.Windows.Controls.Button && window.FindName("SimulationMode") is System.Windows.Controls.ComboBox, "3D is available in design and simulation");
            Check(System.Windows.Controls.Grid.GetColumn((System.Windows.FrameworkElement)window.FindName("WeftStepButton")) == 7 && System.Windows.Controls.Grid.GetColumn((System.Windows.FrameworkElement)window.FindName("SimulationStepButton")) == 8 && System.Windows.Controls.Grid.GetColumn((System.Windows.FrameworkElement)window.FindName("MachineStepButton")) == 9 && window.FindName("HelpButton") is System.Windows.Controls.Button, "weft, simulation, machine and in-app help are ordered");
            Check(nav.FlowDirection == System.Windows.FlowDirection.RightToLeft && System.Windows.Controls.Grid.GetColumn(settings) == 2, "right-to-left workflow and right settings panel");
            var list = (System.Windows.Controls.ListView)window.FindName("ColorPaletteList");
            Check(list.View is System.Windows.Controls.GridView grid && grid.Columns.Count == 6, "color statistics table includes number, lock and highlight columns");
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

internal sealed class FakeImageHandler : System.Net.Http.HttpMessageHandler
{
    private readonly string png;
    public FakeImageHandler(string png) => this.png=png;
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken cancellationToken)
    {
        if(request.Method!=System.Net.Http.HttpMethod.Post || request.RequestUri?.Host!="api.openai.com" || request.Headers.Authorization?.Scheme!="Bearer") throw new InvalidOperationException("Unexpected AI request");
        var payload=System.Text.Json.JsonSerializer.Serialize(new { data=new[]{new { b64_json=png }} });
        return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content=new System.Net.Http.StringContent(payload) });
    }
}
