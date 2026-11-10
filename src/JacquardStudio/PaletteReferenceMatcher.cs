namespace JacquardStudio;

/// <summary>Deterministic nearest RGB palette matching; it is not a calibrated colorimetric conversion.</summary>
public static class PaletteReferenceMatcher
{
    public static PaletteResult Match(PaletteResult source,PaletteResult reference)
    {
        static void Validate(PaletteResult p)
        {
            if(p is null || p.Width<1 || p.Height<1 || (long)p.Width*p.Height>DesignProcessor.MaxPixels || p.Colors is null || p.Colors.Length is <1 or >256 || p.Colors.Distinct().Count()!=p.Colors.Length || p.Colors.Any(c=>c<0 || c>0xffffff) || p.Indices is null || p.Indices.Length!=(long)p.Width*p.Height || p.Counts is null || p.Counts.Length!=p.Colors.Length || p.SourceColorCount<p.Colors.Length || p.SourceColorCount>p.Indices.Length)throw new ArgumentException("ساختار پالت معتبر نیست.");
            var counts=new int[p.Colors.Length];foreach(byte i in p.Indices){if(i>=counts.Length)throw new ArgumentException("شماره رنگ پالت معتبر نیست.");counts[i]++;}if(!counts.SequenceEqual(p.Counts))throw new ArgumentException("تعداد پیکسل‌های پالت معتبر نیست.");
        }
        Validate(source); Validate(reference); var nearest=new int[source.Colors.Length];
        for(int i=0;i<source.Colors.Length;i++)
        {
            int c=source.Colors[i], best=0, distance=int.MaxValue;
            for(int j=0;j<reference.Colors.Length;j++)
            {
                int r=reference.Colors[j], dr=((c>>16)&255)-((r>>16)&255), dg=((c>>8)&255)-((r>>8)&255), db=(c&255)-(r&255);int d=dr*dr+dg*dg+db*db;
                if(d<distance){distance=d;best=j;}
            }
            nearest[i]=best;
        }
        var used=Enumerable.Range(0,nearest.Length).Where(i=>source.Counts[i]>0).Select(i=>nearest[i]).Distinct().OrderBy(i=>i).ToArray();var colors=used.Select(i=>reference.Colors[i]).ToArray();var remap=used.Select((value,index)=>(value,index)).ToDictionary(x=>x.value,x=>x.index);
        var indices=new byte[source.Indices.Length];var counts=new int[colors.Length];for(int i=0;i<indices.Length;i++){byte n=(byte)remap[nearest[source.Indices[i]]];indices[i]=n;counts[n]++;}
        return new(source.Width,source.Height,Math.Max(source.SourceColorCount,colors.Length),colors,indices,counts);
    }
}
