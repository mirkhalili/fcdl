namespace JacquardStudio;

public sealed record WarpRange(int Start, int End, double MinPercent, double MaxPercent);
public sealed record WarpRangeSummary(IReadOnlyList<WarpRange> Loose, IReadOnlyList<WarpRange> Tight, int LooseCount, int TightCount);

/// <summary>Contiguous non-neutral warp runs; retain the most extreme runs, with exact ranges and percentages.</summary>
public static class WarpRanges
{
    public static WarpRangeSummary Summarize(WarpAnalysis analysis, int offset, int limit = 3)
    {
        if (offset < 0 || limit < 1 || analysis.Counts.Length != analysis.DeviationPercent.Length) throw new ArgumentException("Invalid warp ranges.");
        var loose = new List<WarpRange>(); var tight = new List<WarpRange>(); int looseCount = 0, tightCount = 0;
        int i = 0;
        while (i < analysis.DeviationPercent.Length)
        {
            double d = analysis.DeviationPercent[i]; if (!double.IsFinite(d)) throw new ArgumentException("Invalid warp percentage.");
            int sign = Math.Sign(d); if (sign == 0) { i++; continue; }
            int start = i; double min = Math.Abs(d), max = min;
            while (++i < analysis.DeviationPercent.Length)
            {
                d = analysis.DeviationPercent[i]; if (!double.IsFinite(d)) throw new ArgumentException("Invalid warp percentage.");
                if (Math.Sign(d) != sign) break;
                min = Math.Min(min, Math.Abs(d)); max = Math.Max(max, Math.Abs(d));
            }
            var list = sign < 0 ? loose : tight; if (sign < 0) looseCount++; else tightCount++;
            if (list.Count == limit && max <= list[^1].MaxPercent) continue;
            var range = new WarpRange(checked(offset + start), checked(offset + i - 1), min, max);
            list.Add(range); list.Sort((a, b) => { int rank = b.MaxPercent.CompareTo(a.MaxPercent); return rank != 0 ? rank : a.Start.CompareTo(b.Start); });
            if (list.Count > limit) list.RemoveAt(list.Count - 1);
        }
        return new WarpRangeSummary(loose, tight, looseCount, tightCount);
    }
}
