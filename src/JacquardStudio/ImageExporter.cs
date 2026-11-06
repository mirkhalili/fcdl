using System.IO;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public static class ImageExporter
{
    public static void Save(BitmapSource frame, string target, bool tiff, IEnumerable<string> protectedPaths)
    {
        string destination = Path.GetFullPath(target);
        string ext = Path.GetExtension(destination).ToLowerInvariant();
        if ((!tiff && ext != ".bmp") || (tiff && ext is not (".tif" or ".tiff")))
            throw new ArgumentException("The extension must match the image format.");
        if (protectedPaths.Any(p => string.Equals(Path.GetFullPath(p), destination, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Cannot overwrite the input image or a selected pattern.");
        string tmp = Path.Combine(Path.GetDirectoryName(destination)!, ".jacquard-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            BitmapEncoder encoder = tiff ? new TiffBitmapEncoder() : new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using (var file = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) encoder.Save(file);
            File.Move(tmp, destination, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
