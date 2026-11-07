namespace JacquardStudio;

public readonly record struct PixelPoint(int X, int Y);
public sealed record FloatCorrectionPlan(IReadOnlyList<PixelPoint> Points, int Skipped);

/// <summary>One midpoint per original run. Coordinates are converted to the editable design.</summary>
public static class FloatAutoCorrection
{
    public static FloatCorrectionPlan Plan(IEnumerable<FloatRun> runs, int designStart, int designWidth, int height, IEnumerable<int> signalHooks)
    {
        if (designStart < 0 || designWidth < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(designWidth));
        var reserved = signalHooks.ToHashSet(); var points = new List<PixelPoint>(); var seen = new HashSet<PixelPoint>(); int skipped = 0;
        foreach (var run in runs)
        {
            if (run.Length < 1 || run.X < 0 || run.Y < 0) throw new ArgumentException("Invalid float run.");
            int middle = (run.Length - 1) / 2; // Earlier of the two middle cells for even lengths.
            int x = checked(run.X + (run.Direction == FloatDirection.Weft ? middle : 0));
            int y = checked(run.Y + (run.Direction == FloatDirection.Warp ? middle : 0));
            long localX = (long)x - designStart;
            if (localX < 0 || localX >= designWidth || y >= height || reserved.Contains(x)) { skipped++; continue; }
            var p = new PixelPoint((int)localX, y); if (seen.Add(p)) points.Add(p);
        }
        return new FloatCorrectionPlan(points, skipped);
    }
}
