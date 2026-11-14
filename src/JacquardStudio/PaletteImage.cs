using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>A compact, readable multi-column PNG palette sheet; never changes the project palette.</summary>
public static class PaletteImage
{
    public static BitmapSource Render(IReadOnlyList<PaletteEntry> entries, bool english)
    {
        if (entries.Count is < 1 or > 256) throw new ArgumentException("Invalid palette size.");
        int columns = Math.Min(4, Math.Max(1, (entries.Count + 19) / 20)), rows = (entries.Count + columns - 1) / columns;
        const int columnWidth = 355, gap = 18, left = 24, top = 115, rowHeight = 38;
        int width = 2 * left + columns * columnWidth + (columns - 1) * gap, height = top + rows * rowHeight + 35;
        var visual = new DrawingVisual(); var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var titleFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var ink = new SolidColorBrush(Color.FromRgb(30, 46, 59)); var muted = new SolidColorBrush(Color.FromRgb(93, 112, 122)); var accent = new SolidColorBrush(Color.FromRgb(19, 121, 107));
        void Text(DrawingContext dc, string value, double x, double y, double size, Brush brush, bool bold = false)
        {
            var text = new FormattedText(value, CultureInfo.GetCultureInfo(english ? "en-US" : "fa-IR"), FlowDirection.LeftToRight, bold ? titleFace : typeface, size, brush, 1.0); dc.DrawText(text, new Point(x, y));
        }
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0,0,width,height));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(237,247,244)), null, new Rect(0,0,width,81), 0,0);
            Text(dc, english ? "COLOR PALETTE" : "پالت رنگ", left, 12, 25, ink, true);
            Text(dc, english ? $"{entries.Count} colors  ·  Jacquard Studio" : $"{entries.Count} رنگ  ·  ژاکارد استودیو", left, 48, 13, muted);
            for (int col=0; col<columns; col++)
            {
                int x = left + col * (columnWidth + gap);
                dc.DrawRoundedRectangle(accent, null, new Rect(x, 83, columnWidth, 28), 4,4);
                Text(dc, english ? "No.      COLOR           PIXELS         SHARE" : "شماره       رنگ             تعداد          درصد", x+10, 88, 12, Brushes.White, true);
                for (int row=0; row<rows; row++)
                {
                    int i = col*rows+row; if (i >= entries.Count) break; var entry = entries[i]; int y = top+row*rowHeight;
                    if (row%2 == 0) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(246,249,248)), null, new Rect(x,y,columnWidth,rowHeight-2),4,4);
                    Text(dc, entry.ColorNumber.ToString(CultureInfo.InvariantCulture), x+10,y+8,13,ink,true);
                    dc.DrawRoundedRectangle(entry.Brush, new Pen(new SolidColorBrush(Color.FromRgb(175,190,188)),1), new Rect(x+51,y+6,27,25), 3,3);
                    Text(dc, entry.ColorCode, x+85,y+8,12,ink);
                    Text(dc, entry.PixelCount.ToString("N0",CultureInfo.InvariantCulture), x+170,y+8,12,ink);
                    Text(dc, entry.Percent.ToString("0.##", CultureInfo.InvariantCulture)+"%", x+285,y+8,12,ink);
                }
            }
        }
        var image = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
    }
    public static void Save(BitmapSource image, string path, IEnumerable<string> protectedPaths)
    {
        string target = Path.GetFullPath(path); if (!target.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Palette image must be PNG.");
        if (protectedPaths.Any(p => string.Equals(Path.GetFullPath(p), target, StringComparison.OrdinalIgnoreCase))) throw new IOException("Cannot overwrite an input file.");
        string temp = Path.Combine(Path.GetDirectoryName(target)!, ".palette-"+Guid.NewGuid().ToString("N")+".tmp");
        try { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var file = new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) encoder.Save(file); File.Move(temp,target,true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
