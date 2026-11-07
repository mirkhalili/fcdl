using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>Editable machine output settings. Bit 1 means black in the logical map, not a verified electrical state.</summary>
public sealed record MachineSetup(
    string LeftWidth = "0", string RightWidth = "0", WeavePattern? LeftPattern = null, WeavePattern? RightPattern = null,
    string LeftName = "", string RightName = "", bool BoxEnabled = false, int BoxPlacement = 0,
    string BoxHooks = "", string BoxCodes = "", string Sequence = "", bool StartFromBottom = false, bool UseSource = false, bool RedAsBlack = false, bool AbsoluteLayout = false, string TotalHooks = "", string DesignStart = "", string WeftStart = "0", string WeftCount = "0");
public sealed record MachineComposition(BitmapSource Image, int[] SignalHooks, string[] RowWefts, int LeftWidth, int RightWidth, int DesignStart, int FabricStart, int FabricWidth);

public static class MachineComposer
{
    /// <summary>Projection for stages before weft placement: preserve only the already-defined selvedges.</summary>
    public static MachineSetup BeforeWeft(MachineSetup setup) => new(setup.LeftWidth, setup.RightWidth, setup.LeftPattern, setup.RightPattern, setup.LeftName, setup.RightName);
    private static int Nonnegative(string s, string name)
    {
        if (!int.TryParse(NumberInput.Normalize(s.Trim()), NumberStyles.None, CultureInfo.InvariantCulture, out int result) || result < 0) throw new ArgumentException(name + " must be a non-negative integer.");
        return result;
    }
    private static void CheckPattern(WeavePattern? p, int width, string side)
    {
        if (width == 0) return;
        if (p is null || p.Width < 1 || p.Height < 1 || (long)p.Width * p.Height > DesignProcessor.MaxPatternPixels || p.Values.Length != (long)p.Width * p.Height || p.Values.Any(v => v != 0 && v != 255)) throw new ArgumentException("Select a valid black/white weave for the " + side + " selvedge.");
    }
    private static MachineComposition ComposeAbsolute(BitmapSource frame, MachineSetup setup, bool redAsBlack)
    {
        int left = Nonnegative(setup.LeftWidth, "Left selvedge width"), right = Nonnegative(setup.RightWidth, "Right selvedge width");
        CheckPattern(setup.LeftPattern, left, "left"); CheckPattern(setup.RightPattern, right, "right");
        int start = string.IsNullOrWhiteSpace(setup.DesignStart) ? left : Nonnegative(setup.DesignStart, "Design start");
        long needed = (long)start + frame.PixelWidth + right;
        int total = string.IsNullOrWhiteSpace(setup.TotalHooks) ? (needed <= int.MaxValue ? (int)needed : throw new ArgumentException("Invalid width.")) : Nonnegative(setup.TotalHooks, "Total hooks");
        if (start < left) throw new ArgumentException("Left selvedge would extend before the first column.");
        if (total < needed || total < 1 || (long)total * frame.PixelHeight > DesignProcessor.MaxPixels) throw new ArgumentException("Total hooks must fit the design and both selvedges, within the pixel limit.");
        int count = setup.BoxEnabled ? Nonnegative(setup.WeftCount, "Weft count") : 0;
        int weftStart = count == 0 ? 0 : Nonnegative(setup.WeftStart, "Weft start");
        if (count > 256) throw new ArgumentException("At most 256 wefts are supported.");
        if ((long)weftStart + count > start - left) throw new ArgumentException("Weft signal columns overlap the left selvedge or design.");
        int[] hooks = count == 0 ? Array.Empty<int>() : Enumerable.Range(weftStart, count).ToArray();
        int[] sequence = Array.Empty<int>();
        if (count > 0)
        {
            sequence = string.IsNullOrWhiteSpace(setup.Sequence) ? Enumerable.Range(1, count).ToArray() : NumberInput.Normalize(setup.Sequence).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(s => Nonnegative(s, "Weft number")).ToArray();
            if (sequence.Length is < 1 or > 4096 || sequence.Any(n => n < 1 || n > count)) throw new ArgumentException("The sequence must contain weft numbers between 1 and the weft count.");
        }
        int h = frame.PixelHeight, inputStride = frame.PixelWidth * 4; var bgra = new byte[checked(inputStride * h)];
        new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(bgra, inputStride, 0);
        var output = new byte[checked(total * h)]; Array.Fill(output, (byte)255); var rows = count == 0 ? Array.Empty<string>() : new string[h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < left; x++) output[y * total + start - left + x] = setup.LeftPattern!.Values[(y % setup.LeftPattern.Height) * setup.LeftPattern.Width + x % setup.LeftPattern.Width];
            for (int x = 0; x < frame.PixelWidth; x++)
            {
                int i = y * inputStride + x * 4; bool black = bgra[i] == 0 && bgra[i+1] == 0 && bgra[i+2] == 0, white = bgra[i] == 255 && bgra[i+1] == 255 && bgra[i+2] == 255, red = bgra[i] == 0 && bgra[i+1] == 0 && bgra[i+2] == 255;
                if (bgra[i+3] != 255 || !(black || white || redAsBlack && red)) throw new InvalidDataException("Input must be opaque black/white, or red/white with red enabled.");
                output[y * total + start + x] = black || red ? (byte)0 : (byte)255;
            }
            for (int x = 0; x < right; x++) output[y * total + start + frame.PixelWidth + x] = setup.RightPattern!.Values[(y % setup.RightPattern.Height) * setup.RightPattern.Width + x % setup.RightPattern.Width];
            if (count > 0) { int selected = sequence[(setup.StartFromBottom ? h - 1 - y : y) % sequence.Length]; rows[y] = selected.ToString(CultureInfo.InvariantCulture); output[y * total + weftStart + selected - 1] = 0; }
        }
        var result = BitmapSource.Create(total, h, 96, 96, PixelFormats.Gray8, null, output, total); result.Freeze();
        return new MachineComposition(result, hooks, rows, left, right, start, start - left, left + frame.PixelWidth + right);
    }
    public static MachineComposition Compose(BitmapSource frame, MachineSetup setup, bool redAsBlack = false)
    {
        if (setup.AbsoluteLayout) return ComposeAbsolute(frame, setup, redAsBlack);
        int left = Nonnegative(setup.LeftWidth, "Left width"), right = Nonnegative(setup.RightWidth, "Right width");
        CheckPattern(setup.LeftPattern, left, "left"); CheckPattern(setup.RightPattern, right, "right");
        if (setup.BoxPlacement is < 0 or > 2) throw new ArgumentException("Invalid box motion placement.");
        int[] signalHooks = Array.Empty<int>(); int extra = 0;
        if (setup.BoxEnabled)
        {
            if (setup.BoxPlacement is 0 or 1)
            {
                extra = Nonnegative(setup.BoxHooks, "Number of added signal hooks");
                if (extra is < 1 or > 32) throw new ArgumentException("Add between 1 and 32 signal hooks.");
            }
            else
            {
                var parts = NumberInput.Normalize(setup.BoxHooks).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length is < 1 or > 32) throw new ArgumentException("Enter between 1 and 32 comma-separated hook indices.");
                signalHooks = parts.Select(x => Nonnegative(x, "Hook index")).ToArray();
                if (signalHooks.Distinct().Count() != signalHooks.Length) throw new ArgumentException("Hook indices must be distinct.");
            }
        }
        long finalWidthLong = (long)frame.PixelWidth + left + right + extra;
        if (finalWidthLong > int.MaxValue || finalWidthLong * frame.PixelHeight > DesignProcessor.MaxPixels) throw new ArgumentException("Output, including added hooks, exceeds 4 million pixels.");
        int w = (int)finalWidthLong, h = frame.PixelHeight;
        if (setup.BoxEnabled && setup.BoxPlacement == 0) signalHooks = Enumerable.Range(0, extra).ToArray();
        if (setup.BoxEnabled && setup.BoxPlacement == 1) signalHooks = Enumerable.Range(w - extra, extra).ToArray();
        if (signalHooks.Any(x => x < 0 || x >= w)) throw new ArgumentException("A signal hook index is outside the final output width.");
        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] sequence = Array.Empty<string>();
        if (setup.BoxEnabled)
        {
            foreach (string line in NumberInput.Normalize(setup.BoxCodes).Split(new[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] pair = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (pair.Length != 2 || pair[0].Length is < 1 or > 32 || pair[0].Any(c => char.IsControl(c) || c == ',') || pair[1].Length != signalHooks.Length || pair[1].Any(c => c is not ('0' or '1')) || !codes.TryAdd(pair[0], pair[1]))
                    throw new ArgumentException("Each weft must have a unique name and exactly one 0/1 bit per signal hook, e.g. 1=10.");
            }
            if (codes.Count is < 1 or > 256) throw new ArgumentException("Define 1 to 256 wefts.");
            sequence = NumberInput.Normalize(setup.Sequence).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (sequence.Length is < 1 or > 4096 || sequence.Any(id => !codes.ContainsKey(id))) throw new ArgumentException("Every weft in the sequence must be defined.");
        }
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int stride = frame.PixelWidth * 4; byte[] input = new byte[checked(stride * h)]; bitmap.CopyPixels(input, stride, 0);
        var output = new byte[checked(w * h)]; var rowWefts = setup.BoxEnabled ? new string[h] : Array.Empty<string>();
        int prefix = setup.BoxEnabled && setup.BoxPlacement == 0 ? extra : 0, bodyStart = prefix + left;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < left; x++) output[y * w + prefix + x] = setup.LeftPattern!.Values[(y % setup.LeftPattern.Height) * setup.LeftPattern.Width + (x % setup.LeftPattern.Width)];
            for (int x = 0; x < frame.PixelWidth; x++)
            {
                int k = y * stride + x * 4; bool black = input[k] == 0 && input[k+1] == 0 && input[k+2] == 0;
                bool white = input[k] == 255 && input[k+1] == 255 && input[k+2] == 255;
                bool red = input[k] == 0 && input[k+1] == 0 && input[k+2] == 255;
                if (input[k+3] != 255 || !(black || white || (redAsBlack && red))) throw new InvalidDataException("Input must be opaque black/white, or red/white with red enabled.");
                output[y * w + bodyStart + x] = black || red ? (byte)0 : (byte)255;
            }
            for (int x = 0; x < right; x++) output[y * w + bodyStart + frame.PixelWidth + x] = setup.RightPattern!.Values[(y % setup.RightPattern.Height) * setup.RightPattern.Width + (x % setup.RightPattern.Width)];
            if (setup.BoxEnabled)
            {
                string id = sequence[(setup.StartFromBottom ? h - 1 - y : y) % sequence.Length]; rowWefts[y] = id;
                string bits = codes[id]; for (int i = 0; i < signalHooks.Length; i++) output[y * w + signalHooks[i]] = bits[i] == '1' ? (byte)0 : (byte)255;
            }
        }
        var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, output, w); image.Freeze();
        return new MachineComposition(image, signalHooks, rowWefts, left, right, bodyStart, prefix, left + frame.PixelWidth + right);
    }
}
