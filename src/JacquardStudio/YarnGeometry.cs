namespace JacquardStudio;

/// <summary>Linear mass conversion and an explicitly approximate cylindrical yarn model.</summary>
public static class YarnGeometry
{
    public static readonly string[] Units = { "tex", "dtex", "denier", "Ne (cotton)", "Nm" };
    public static double ToTex(double count, int unit)
    {
        if (!double.IsFinite(count) || count <= 0 || count > 1_000_000) throw new ArgumentException("Yarn count must be positive and finite.");
        double tex = unit switch { 0 => count, 1 => count / 10, 2 => count / 9, 3 => 590.541 / count, 4 => 1000 / count, _ => throw new ArgumentException("Unknown yarn count unit.") };
        if (!double.IsFinite(tex) || tex < .05 || tex > 20_000) throw new ArgumentException("Yarn count is outside the supported range (0.05–20000 tex).");
        return tex;
    }
    public static double DiameterMm(double tex, double density)
    {
        if (!double.IsFinite(tex) || tex < .05 || tex > 20_000 || !double.IsFinite(density) || density < .05 || density > 5) throw new ArgumentException("Effective yarn density must be between 0.05 and 5 g/cm³.");
        return Math.Sqrt(4 * tex / (1000 * Math.PI * density));
    }
    public static double SpacingMm(double threadsPerCm)
    {
        if (!double.IsFinite(threadsPerCm) || threadsPerCm < .1 || threadsPerCm > 500) throw new ArgumentException("Thread density must be between 0.1 and 500 per cm.");
        return 10 / threadsPerCm;
    }
}
