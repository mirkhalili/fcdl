namespace JacquardStudio;

public sealed record WarpRange(int Start, int End, double MinPercent, double MaxPercent);
public sealed record WarpRangeSummary(IReadOnlyList<WarpRange> Loose, IReadOnlyList<WarpRange> Tight, int LooseCount, int TightCount);

/// <summary>Contiguous non-neutral warp runs; retain the most extreme runs, with exact ranges and percentages.</summary>
public static class WarpRanges
{
    public static WarpRangeSummary Summarize(WarpAnalysis analysis, int offset, int limit = 3, double threshold = 0)
    {
        if (offset < 0 || limit < 1 || !double.IsFinite(threshold) || threshold < 0 || analysis.Counts.Length != analysis.DeviationPercent.Length) throw new ArgumentException("Invalid warp ranges.");
        var loose = new PriorityQueue<WarpRange, (double severity, int negativeStart)>(); var tight = new PriorityQueue<WarpRange, (double severity, int negativeStart)>(); int looseCount = 0, tightCount = 0;
        int i = 0;
        while (i < analysis.DeviationPercent.Length)
        {
            double d = analysis.DeviationPercent[i]; if (!double.IsFinite(d)) throw new ArgumentException("Invalid warp percentage.");
            int sign = Math.Sign(d); if (Math.Abs(d) <= threshold) { i++; continue; }
            int start = i; double min = Math.Abs(d), max = min;
            while (++i < analysis.DeviationPercent.Length)
            {
                d = analysis.DeviationPercent[i]; if (!double.IsFinite(d)) throw new ArgumentException("Invalid warp percentage.");
                if (Math.Sign(d) != sign || Math.Abs(d) <= threshold) break;
                min = Math.Min(min, Math.Abs(d)); max = Math.Max(max, Math.Abs(d));
            }
            var list = sign < 0 ? loose : tight; if (sign < 0) looseCount++; else tightCount++;
            if (list.Count == limit && max <= list.Peek().MaxPercent) continue;
            var range = new WarpRange(checked(offset + start), checked(offset + i - 1), min, max);
            list.Enqueue(range, (range.MaxPercent, -range.Start));
            if (list.Count > limit) list.Dequeue();
        }
        return new WarpRangeSummary(loose.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.MaxPercent).ThenBy(x => x.Start).ToArray(), tight.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.MaxPercent).ThenBy(x => x.Start).ToArray(), looseCount, tightCount);
    }
}

public static class WarpDisplay
{
    public static string Percentage(double magnitude)
    {
        if (!double.IsFinite(magnitude) || magnitude <= 0) throw new ArgumentOutOfRangeException(nameof(magnitude));
        return magnitude < 0.01 ? "<0.01%" : $"{magnitude:0.##}%";
    }
    public static string Range(WarpRange range)
    {
        string min = Percentage(range.MinPercent), max = Percentage(range.MaxPercent);
        return min == max ? min : $"{min}–{max}";
    }
    public static string Deviation(double d, bool english)
        => d == 0 ? (english ? "balanced" : "متعادل") : d < 0
            ? (english ? "looseness " : "شل‌شدگی ") + Percentage(-d)
            : (english ? "tightness " : "کشیدگی ") + Percentage(d);
}
