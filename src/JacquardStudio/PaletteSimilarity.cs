namespace JacquardStudio;

/// <summary>Merge RGB-near colors to a stable representative, never deleting a locked color.</summary>
public static class PaletteSimilarity
{
    public static (PaletteResult Palette,bool[] Locks) Reduce(PaletteResult palette,bool[] locked,int maximum,int threshold)
    {
        if(maximum is <1 or >256 || locked.Length!=palette.Colors.Length || locked.Count(x=>x)>maximum)throw new ArgumentException("تعداد رنگ نمی‌تواند از تعداد رنگ‌های قفل‌شده کمتر باشد.");
        (palette,locked)=Merge(palette,locked,threshold);
        if(palette.Colors.Length<=maximum)return(palette,locked);
        var keep=Enumerable.Range(0,palette.Colors.Length).Where(i=>locked[i]).Concat(Enumerable.Range(0,palette.Colors.Length).Where(i=>!locked[i]).OrderByDescending(i=>palette.Counts[i]).ThenBy(i=>palette.Colors[i]).Take(maximum-locked.Count(x=>x))).Order().ToArray();
        return(DesignProcessor.KeepColors(palette,keep),keep.Select(i=>locked[i]).ToArray());
    }
    public static (PaletteResult Palette,bool[] Locks) Merge(PaletteResult palette,bool[] locked,int threshold)
    {
        if(threshold is <0 or >441 || locked.Length!=palette.Colors.Length || palette.Colors.Length is <1 or >256)throw new ArgumentException("آستانه RGB باید بین ۰ تا ۴۴۱ باشد.");
        if(threshold==0)return(palette,(bool[])locked.Clone());
        var reps=new List<int>();int[] assignment=new int[palette.Colors.Length];
        foreach(int i in Enumerable.Range(0,palette.Colors.Length).OrderByDescending(i=>locked[i]).ThenByDescending(i=>palette.Counts[i]).ThenBy(i=>palette.Colors[i]))
        {
            int best=-1,bestDistance=threshold*threshold+1,c=palette.Colors[i];
            if(!locked[i])foreach(int r in reps) {int color=palette.Colors[r],dr=((c>>16)&255)-((color>>16)&255),dg=((c>>8)&255)-((color>>8)&255),db=(c&255)-(color&255);int d=dr*dr+dg*dg+db*db;if(d<bestDistance || d==bestDistance && best>=0 && r<best){best=r;bestDistance=d;}}
            if(best<0){best=i;reps.Add(i);}assignment[i]=best;
        }
        reps.Sort();var repToIndex=reps.Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i);var indices=new byte[palette.Indices.Length];var counts=new int[reps.Count];
        for(int i=0;i<indices.Length;i++){int source=palette.Indices[i];if(source>=assignment.Length)throw new ArgumentException("اندیس پالت معتبر نیست.");int mapped=repToIndex[assignment[source]];indices[i]=(byte)mapped;counts[mapped]++;}
        return(palette with {Colors=reps.Select(i=>palette.Colors[i]).ToArray(),Indices=indices,Counts=counts},reps.Select(i=>locked[i]).ToArray());
    }
}
