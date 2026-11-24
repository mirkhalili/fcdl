using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace JacquardStudio;

/// <summary>Compatibility facade for the SVG glyphs in the Jacquard Studio Vazir design system.</summary>
public static class ModuleIcons
{
    public static string PathFor(ModuleKind kind) => StudioIconCatalog.PathDataFor(kind);
    internal static string PathForFallback(ModuleKind kind) => "M4,4 H20 V20 H4 Z";
    public static string? ResourceName(ModuleKind kind) => StudioIconCatalog.HasGlyph(kind) ? StudioIconCatalog.ModuleSlug(kind) : null;
    public static Color? BackgroundColor(ModuleKind kind) => null;

    public static Image? CreateResourceImage(string name, double size)
    {
        if (!double.IsFinite(size) || size <= 0 || size > 256) return null;
        var bitmap = StudioIconCatalog.CreateRaster(name, Math.Clamp((int)Math.Ceiling(size), 8, 256));
        return bitmap is null ? null : new Image
        {
            Source = bitmap,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible = false
        };
    }

    public static FrameworkElement? CreateResourceVisual(string name, double size)
    {
        if (!double.IsFinite(size) || size <= 0) return null;
        string glyph = StudioIconCatalog.ToolSlug(name);
        var visual = StudioIconCatalog.CreateTool(glyph, size);
        visual.SetResourceReference(Shape.StrokeProperty, "IconGlyph");
        return visual;
    }

    public static FrameworkElement Create(ModuleKind kind, double size) => StudioIconCatalog.Create(kind, size);

    // Kept for source compatibility with component callers; it now falls back to the Vazir SVG, never legacy artwork.
    public static FrameworkElement Create(ModuleKind kind, double size, Func<string, double, Image?> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        return StudioIconCatalog.Create(kind, size);
    }
}
