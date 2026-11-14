namespace JacquardStudio;

/// <summary>Pair selected colors and weaves in their visual order, without changing unselected colors.</summary>
public static class AutoWeaveAssignments
{
    public static IReadOnlyDictionary<int,int> Pair(IReadOnlyList<int> colors, int weaveCount, IEnumerable<int> selectedColors, IEnumerable<int> selectedWeaves, IReadOnlyDictionary<int,int> existing)
    {
        var cs=selectedColors.Distinct().Order().ToArray(); var ws=selectedWeaves.Distinct().Order().ToArray();
        if(cs.Any(i=>i<0 || i>=colors.Count) || ws.Any(i=>i<0 || i>=weaveCount)) throw new ArgumentOutOfRangeException(nameof(selectedColors));
        var result=new Dictionary<int,int>(existing);
        for(int i=0;i<Math.Min(cs.Length,ws.Length);i++) result[colors[cs[i]]]=ws[i];
        return result;
    }
}
