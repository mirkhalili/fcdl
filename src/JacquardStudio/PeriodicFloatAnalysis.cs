namespace JacquardStudio;

/// <summary>A direct-repeat diagnostic. Uniform lines are unbounded, not merely one repeat long.</summary>
public sealed record PeriodicFloatSummary(int BlackRuns, int WhiteRuns, int CrossingRuns, int UnboundedBlack, int UnboundedWhite, int LongestFinite);
public static class PeriodicFloatAnalysis
{
    public static PeriodicFloatSummary Analyze(byte[] pixels,int width,int height,int minimum,FloatDirection direction)
    {
        if(width<1 || height<1 || (long)width*height>DesignProcessor.MaxPixels || pixels.Length!=(long)width*height || pixels.Any(p=>p!=0 && p!=255)) throw new ArgumentException("نقشه باید مات، دودویی و حداکثر چهار میلیون پیکسل باشد.");
        if(minimum<1)throw new ArgumentException("حداقل طول فلوت باید عدد مثبت باشد.");
        if(!Enum.IsDefined(direction))throw new ArgumentException("جهت بررسی معتبر نیست.");
        int lines=direction==FloatDirection.Warp ? width : height, length=direction==FloatDirection.Warp ? height : width;
        int black=0,white=0,crossing=0,unboundedBlack=0,unboundedWhite=0,longest=0;
        for(int line=0;line<lines;line++)
        {
            byte At(int position) => direction==FloatDirection.Warp ? pixels[position*width+line] : pixels[line*width+position];
            // Find a run boundary on the circle; never split a crossing run into two.
            int first=0; while(first<length && At(first)==At((first+length-1)%length)) first++;
            if(first==length) {if(At(0)==0)unboundedBlack++;else unboundedWhite++;continue;}
            int offset=0;
            while(offset<length)
            {
                int start=(first+offset)%length,run=1; byte color=At(start);
                while(offset+run<length && At((first+offset+run)%length)==color)run++;
                longest=Math.Max(longest,run);
                if(run>=minimum){if(color==0)black++;else white++;if(start+run>length)crossing++;}
                offset+=run;
            }
        }
        return new(black,white,crossing,unboundedBlack,unboundedWhite,longest);
    }
}
