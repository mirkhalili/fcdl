using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>Bounded PDFium rasterization. No scripts are executed; native library operations are serialized.</summary>
public static class PdfImageLoader
{
    private const string Dll = "pdfium";
    private static readonly object Gate = new();
    private static bool initialized;
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_InitLibrary();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, IntPtr password);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern uint FPDF_GetLastError();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
    public static ImageResult Load(string path, int pageIndex)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Render(bytes, pageIndex, null, null, Path.GetExtension(path).Equals(".ai", StringComparison.OrdinalIgnoreCase) ? "AI (PDF compatible)" : "PDF");
    }
    public static ImageResult Render(byte[] bytes, int pageIndex, int? targetWidth = null, int? targetHeight = null, string format = "PDF")
    {
        if (bytes.Length > ImageLoader.MaxFileBytes) throw new InvalidDataException("PDF exceeds the 128 MiB limit.");
        if (bytes.Length < 5 || !System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 1024)).Contains("%PDF-"))
            throw new NotSupportedException("AI must be saved with Create PDF Compatible File enabled; legacy AI is not supported.");
        lock (Gate)
        {
            if (!initialized) { FPDF_InitLibrary(); initialized = true; }
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned); IntPtr document = IntPtr.Zero, page = IntPtr.Zero, bitmap = IntPtr.Zero;
            try
            {
                document = FPDF_LoadMemDocument(pin.AddrOfPinnedObject(), bytes.Length, IntPtr.Zero);
                if (document == IntPtr.Zero) throw new InvalidDataException($"PDF could not be opened (error {FPDF_GetLastError()}); encrypted or damaged documents are unsupported.");
                int count = FPDF_GetPageCount(document);
                if (count is < 1 or > ImageLoader.MaxFrames) throw new InvalidDataException("Invalid or excessive PDF page count.");
                if (pageIndex < 0 || pageIndex >= count) throw new ArgumentOutOfRangeException(nameof(pageIndex));
                page = FPDF_LoadPage(document, pageIndex); if (page == IntPtr.Zero) throw new InvalidDataException("PDF page could not be loaded.");
                if ((targetWidth is null) != (targetHeight is null)) throw new ArgumentException("Both target dimensions are required.");
                double w = targetWidth ?? Math.Ceiling(FPDF_GetPageWidthF(page) * 2), h = targetHeight ?? Math.Ceiling(FPDF_GetPageHeightF(page) * 2); // 144 DPI on initial import
                if (!double.IsFinite(w) || !double.IsFinite(h) || w < 1 || h < 1 || w > 40_000 || h > 40_000 || w * h > ImageLoader.MaxPixels) throw new InvalidDataException("PDF page exceeds the 40 megapixel raster limit at 144 DPI.");
                int width = (int)w, height = (int)h; bitmap = FPDFBitmap_Create(width, height, 1); if (bitmap == IntPtr.Zero) throw new InvalidDataException("Could not allocate PDF image.");
                FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF); // PDFium flags disable smoothing for text, images and paths, including Bezier curves.
                const int noAntialias = 0x1000 | 0x2000 | 0x4000;
                FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, 1 | noAntialias);
                int stride = FPDFBitmap_GetStride(bitmap); if (stride != checked(width * 4)) throw new InvalidDataException("Unexpected PDF image stride.");
                var pixels = new byte[checked(stride * height)]; Marshal.Copy(FPDFBitmap_GetBuffer(bitmap), pixels, 0, pixels.Length);
                var image = BitmapSource.Create(width, height, 144, 144, PixelFormats.Bgra32, null, pixels, stride); image.Freeze();
                return new ImageResult(image, count, pageIndex, format, new VectorDocument(bytes, pageIndex));
            }
            finally { if (bitmap != IntPtr.Zero) FPDFBitmap_Destroy(bitmap); if (page != IntPtr.Zero) FPDF_ClosePage(page); if (document != IntPtr.Zero) FPDF_CloseDocument(document); pin.Free(); }
        }
    }
}
