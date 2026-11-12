using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

// Data persisted in graph projects. Selection, clipboard, and undo are intentionally session-only.
public sealed class DesignLayerData
{
    public string Name { get; set; } = "لایه";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public int Opacity { get; set; } = 100;
    public string Png { get; set; } = "";
}
public sealed class DesignDocumentData
{
    public List<DesignLayerData> Layers { get; set; } = new(); // bottom to top
    public int Active { get; set; }
    public string? TracePng { get; set; }
    public int TraceOpacity { get; set; } = 40;
    public bool TraceVisible { get; set; } = true;
    public int TraceX { get; set; }
    public int TraceY { get; set; }
    public int TraceWidth { get; set; }
    public int TraceHeight { get; set; }
}

/// <summary>Raster editor state, RGBA compositing, selection and bounded independent layers.</summary>
public sealed class DesignCanvas
{
    public const int MaxLayers=8;
    // Total uncompressed layer storage is capped independently of the per-image pixel limit.
    private const long MaxLayerPixels=16000000;
    private static readonly (int X,int Y)[] Directions = {(-1,0),(1,0),(0,-1),(0,1)};
    public sealed class Layer
    {
        public string Name="لایه"; public bool Visible=true, Locked; public int Opacity=100; public byte[] Pixels=Array.Empty<byte>();
        public Layer Clone() => new() {Name=Name,Visible=Visible,Locked=Locked,Opacity=Opacity,Pixels=(byte[])Pixels.Clone()};
    }
    public sealed class Clip
    {
        public int X,Y,Width,Height; public byte[] Pixels=Array.Empty<byte>(); public bool[] Mask=Array.Empty<bool>();
    }
    public int Width { get; } public int Height { get; }
    public List<Layer> Layers { get; } = new();
    public int Active { get; set; }
    // null = all pixels, otherwise an explicit mask (which can be empty).
    public bool[]? Selection { get; private set; }
    public Layer Current => Layers[Active];
    public static BitmapSource Blank(int width,int height,Color color)
    {
        if(width<1 || height<1 || (long)width*height>DesignProcessor.MaxPixels)throw new ArgumentException("ابعاد بوم باید مثبت و حداکثر چهار میلیون پیکسل باشد.");
        var pixels=new byte[checked(width*height*4)];for(int i=0;i<pixels.Length;i+=4){pixels[i]=color.B;pixels[i+1]=color.G;pixels[i+2]=color.R;pixels[i+3]=color.A;}var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);image.Freeze();return image;
    }
    public DesignCanvas(BitmapSource input)
    {
        Width=input.PixelWidth;Height=input.PixelHeight;ValidateSize();
        var bytes=new byte[checked(Width*Height*4)];new FormatConvertedBitmap(input,PixelFormats.Bgra32,null,0).CopyPixels(bytes,Width*4,0);
        Layers.Add(new Layer {Name="طرح اصلی",Pixels=bytes});
    }
    public DesignCanvas(BitmapSource input,DesignDocumentData data) : this(input)
    {
        if(data.Layers is null || data.Layers.Count<1 || data.Layers.Count>MaxLayers || (long)data.Layers.Count*Width*Height>MaxLayerPixels || data.Active<0 || data.Active>=data.Layers.Count) throw new InvalidDataException("تعداد یا لایه فعال معتبر نیست.");
        Layers.Clear();
        foreach(var layer in data.Layers)
        {
            if(layer is null || string.IsNullOrWhiteSpace(layer.Name) || layer.Name.Length>80 || layer.Opacity is <0 or >100 || string.IsNullOrEmpty(layer.Png)) throw new InvalidDataException("مشخصات لایه معتبر نیست.");
            var bitmap=Decode(layer.Png); if(bitmap.PixelWidth!=Width || bitmap.PixelHeight!=Height)throw new InvalidDataException("ابعاد لایه با طرح یکسان نیست.");
            var bytes=new byte[checked(Width*Height*4)];new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0).CopyPixels(bytes,Width*4,0);
            Layers.Add(new Layer {Name=layer.Name,Visible=layer.Visible,Locked=layer.Locked,Opacity=layer.Opacity,Pixels=bytes});
        }
        Active=data.Active;
    }
    private void ValidateSize() { if(Width<1 || Height<1 || (long)Width*Height>DesignProcessor.MaxPixels) throw new ArgumentException("طرح باید حداکثر چهار میلیون پیکسل باشد."); }
    public static BitmapSource Decode(string encoded)
    {
        if(encoded.Length>ImageLoader.MaxFileBytes*2) throw new InvalidDataException("تصویر بیش از حد بزرگ است."); byte[] data=Convert.FromBase64String(encoded); if(data.Length>ImageLoader.MaxFileBytes)throw new InvalidDataException("تصویر بیش از حد بزرگ است.");
        using var stream=new MemoryStream(data); var frame=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
        if(frame.PixelWidth<1 || frame.PixelHeight<1 || (long)frame.PixelWidth*frame.PixelHeight>DesignProcessor.MaxPixels)throw new InvalidDataException("ابعاد تصویر مجاز نیست.");frame.Freeze();return frame;
    }
    private BitmapSource Bitmap(byte[] pixels) { var bmp=BitmapSource.Create(Width,Height,96,96,PixelFormats.Bgra32,null,pixels,Width*4);bmp.Freeze();return bmp; }
    public DesignDocumentData Save() => new() { Active=Active,Layers=Layers.Select(l=>new DesignLayerData {Name=l.Name,Visible=l.Visible,Locked=l.Locked,Opacity=l.Opacity,Png=ModuleExecutor.EmbedImage(Bitmap(l.Pixels))}).ToList() };
    public DesignCanvas Clone()
    {
        var copy=new DesignCanvas(Bitmap(Layers[0].Pixels)); copy.Layers.Clear();copy.Layers.AddRange(Layers.Select(l=>l.Clone()));copy.Active=Active;copy.Selection=Selection is null ? null : (bool[])Selection.Clone();return copy;
    }
    public long MemoryBytes => (long)Width*Height*(4L*Layers.Count+1);
    public void AddLayer(string name)
    {
        if(Layers.Count>=MaxLayers || (long)(Layers.Count+1)*Width*Height>MaxLayerPixels)throw new InvalidOperationException("حداکثر ۸ لایه و ۱۶ میلیون پیکسل مجموع لایه‌ها مجاز است.");
        Layers.Insert(Active+1,new Layer { Name=Name(name),Pixels=new byte[checked(Width*Height*4)] });Active++;
    }
    public static string Name(string text) {text=text.Trim();if(text.Length<1 || text.Length>80)throw new ArgumentException("نام لایه باید بین ۱ تا ۸۰ نویسه باشد.");return text;}
    public void DeleteLayer() {if(Layers.Count==1)throw new InvalidOperationException("آخرین لایه قابل حذف نیست.");Layers.RemoveAt(Active);Active=Math.Min(Active,Layers.Count-1);}
    public void Reorder(int delta) {int target=Active+delta;if(target<0 || target>=Layers.Count)return;(Layers[Active],Layers[target])=(Layers[target],Layers[Active]);Active=target;}
    public BitmapSource Composite()
    {
        byte[] output=new byte[checked(Width*Height*4)];
        foreach(var l in Layers) { if(!l.Visible || l.Opacity==0)continue;
            for(int i=0;i<output.Length;i+=4) {
                int a=(l.Pixels[i+3]*l.Opacity+50)/100, da=output[i+3]; if(a==0)continue;
                int oa=a+(da*(255-a)+127)/255;
                for(int c=0;c<3;c++) output[i+c]=(byte)Math.Clamp((l.Pixels[i+c]*a*255 + output[i+c]*da*(255-a) + oa*127)/(oa*255),0,255);
                output[i+3]=(byte)oa;
            }
        }
        return Bitmap(output);
    }
    private void Check(int x,int y) {if((uint)x>=(uint)Width || (uint)y>=(uint)Height)throw new ArgumentOutOfRangeException(nameof(x));}
    private void Editable() {if(Current.Locked)throw new InvalidOperationException("لایه قفل است؛ ابتدا قفل را باز کنید.");}
    private bool Selected(int p)=>Selection is null || Selection[p];
    public int SelectedCount => Selection is null ? Width*Height : Selection.Count(v=>v);
    private Color ColorAt(byte[] data,int p) {int i=p*4;return Color.FromArgb(data[i+3],data[i+2],data[i+1],data[i]);}
    public Color At(int x,int y) {Check(x,y);return ColorAt(Current.Pixels,y*Width+x);}
    private void Put(int x,int y,Color c) {int p=y*Width+x;if(!Selected(p))return;int i=p*4;var a=Current.Pixels;a[i]=c.B;a[i+1]=c.G;a[i+2]=c.R;a[i+3]=c.A;}
    public void Line(int x0,int y0,int x1,int y1,Color color,int size=1)
    {
        Editable();Check(x0,y0);Check(x1,y1);if(size<1 || size>32)throw new ArgumentOutOfRangeException(nameof(size));
        int dx=Math.Abs(x1-x0),sx=x0<x1?1:-1,dy=-Math.Abs(y1-y0),sy=y0<y1?1:-1,err=dx+dy;
        while(true) {for(int by=-(size/2);by<size-size/2;by++)for(int bx=-(size/2);bx<size-size/2;bx++){int x=x0+bx,y=y0+by;if((uint)x<(uint)Width && (uint)y<(uint)Height)Put(x,y,color);} if(x0==x1 && y0==y1)break;int e=2*err;if(e>=dy){err+=dy;x0+=sx;}if(e<=dx){err+=dx;y0+=sy;}}
    }
    public void Rectangle(int x0,int y0,int x1,int y1,Color c,int size=1,bool filled=false)
    {
        Editable();Check(x0,y0);Check(x1,y1);
        if(filled) {for(int y=Math.Min(y0,y1);y<=Math.Max(y0,y1);y++)for(int x=Math.Min(x0,x1);x<=Math.Max(x0,x1);x++)Put(x,y,c);}
        else {Line(x0,y0,x1,y0,c,size);Line(x1,y0,x1,y1,c,size);Line(x1,y1,x0,y1,c,size);Line(x0,y1,x0,y0,c,size);}
    }
    public void Ellipse(int x0,int y0,int x1,int y1,Color color,int thickness=1,bool filled=false)
    {
        Editable();Check(x0,y0);Check(x1,y1);if(thickness<1 || thickness>32)throw new ArgumentOutOfRangeException(nameof(thickness));
        int left=Math.Min(x0,x1),right=Math.Max(x0,x1),top=Math.Min(y0,y1),bottom=Math.Max(y0,y1);double rx=(right-left+1)/2.0,ry=(bottom-top+1)/2.0,cx=(left+right)/2.0,cy=(top+bottom)/2.0;
        double innerX=rx-thickness,innerY=ry-thickness;
        for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++) {double dx=x-cx,dy=y-cy;double outer=dx*dx/(rx*rx)+dy*dy/(ry*ry);if(outer>1)continue;bool inner=innerX>0 && innerY>0 && dx*dx/(innerX*innerX)+dy*dy/(innerY*innerY)<1;if(filled || !inner)Put(x,y,color);}
    }
    /// <summary>Ellipse arc inside the drag rectangle. Angles are clockwise on the screen.</summary>
    public void Arc(int x0,int y0,int x1,int y1,Color color,int thickness=1,bool bottom=false)
        => ArcSection(x0,y0,x1,y1,color,thickness,bottom ? 0 : 180,180);
    public void ArcSection(int x0,int y0,int x1,int y1,Color color,int thickness,double startDegrees,double sweepDegrees)
    {
        Editable();Check(x0,y0);Check(x1,y1);if(thickness<1 || thickness>32)throw new ArgumentOutOfRangeException(nameof(thickness));
        if(!double.IsFinite(startDegrees) || !double.IsFinite(sweepDegrees) || Math.Abs(sweepDegrees)>360 || sweepDegrees==0)throw new ArgumentException("زاویه کمان معتبر نیست.");
        int left=Math.Min(x0,x1),right=Math.Max(x0,x1),top=Math.Min(y0,y1),end=Math.Max(y0,y1);double cx=(left+right)/2.0,cy=(top+end)/2.0,rx=(right-left)/2.0,ry=(end-top)/2.0;
        int steps=(int)Math.Clamp(2L*(right-left+end-top),12,16000);int? px=null,py=null;
        for(int k=0;k<=steps;k++){double a=(startDegrees+sweepDegrees*k/steps)*Math.PI/180;int x=Math.Clamp((int)Math.Round(cx+rx*Math.Cos(a)),left,right),y=Math.Clamp((int)Math.Round(cy+ry*Math.Sin(a)),top,end);if(px is int ox && py is int oy)Line(ox,oy,x,y,color,thickness);else Line(x,y,x,y,color,thickness);px=x;py=y;}
    }
    public void SelectColor(int x,int y)
    {
        Check(x,y);Color target=At(x,y);var m=new bool[Width*Height];for(int p=0;p<m.Length;p++)m[p]=ColorAt(Current.Pixels,p)==target;Selection=m;
    }
    public void Fill(int x,int y,Color color)
    {
        Editable();Check(x,y);int first=y*Width+x;if(!Selected(first))return;Color target=ColorAt(Current.Pixels,first);if(target==color)return;
        var queue=new Queue<int>();Put(x,y,color);queue.Enqueue(first);
        while(queue.Count>0) {int p=queue.Dequeue(),px=p%Width,py=p/Width;void Add(int nx,int ny){if((uint)nx<(uint)Width && (uint)ny<(uint)Height){int n=ny*Width+nx;if(Selected(n) && ColorAt(Current.Pixels,n)==target){Put(nx,ny,color);queue.Enqueue(n);}}} Add(px-1,py);Add(px+1,py);Add(px,py-1);Add(px,py+1);}
    }
    public void ClearSelected() {Editable();for(int p=0;p<Width*Height;p++)if(Selected(p))Array.Clear(Current.Pixels,p*4,4);}
    public void SelectAll() => Selection=null;
    public void InvertSelection() {var m=new bool[Width*Height];for(int i=0;i<m.Length;i++)m[i]=Selection is not null && !Selection[i];Selection=m;}
    public void SelectRectangle(int x0,int y0,int x1,int y1) {Check(x0,y0);Check(x1,y1);var m=new bool[Width*Height];for(int y=Math.Min(y0,y1);y<=Math.Max(y0,y1);y++)for(int x=Math.Min(x0,x1);x<=Math.Max(x0,x1);x++)m[y*Width+x]=true;Selection=m;}
    public void SelectPolygon(IReadOnlyList<(int X,int Y)> points)
    {
        if(points.Count<3 || points.Count>2048)throw new ArgumentException("انتخاب آزاد حداقل سه و حداکثر ۲۰۴۸ نقطه لازم دارد.");foreach(var p in points)Check(p.X,p.Y);
        int minY=points.Min(p=>p.Y),maxY=points.Max(p=>p.Y);if((long)(maxY-minY+1)*points.Count>8000000)throw new ArgumentException("مسیر انتخاب بیش از حد پیچیده است؛ ساده‌تر انتخاب کنید.");
        var m=new bool[Width*Height];var intersections=new List<double>();
        for(int y=minY;y<=maxY;y++) {intersections.Clear();double scan=y+0.5;
            for(int i=0,j=points.Count-1;i<points.Count;j=i++) {var a=points[j];var b=points[i];if((a.Y<=scan && b.Y>scan)||(b.Y<=scan && a.Y>scan))intersections.Add(a.X+(scan-a.Y)*(b.X-a.X)/(b.Y-a.Y));}
            intersections.Sort();for(int i=0;i+1<intersections.Count;i+=2) {int from=Math.Max(0,(int)Math.Ceiling(intersections[i]-.5)),to=Math.Min(Width-1,(int)Math.Floor(intersections[i+1]-.5));for(int x=from;x<=to;x++)m[y*Width+x]=true;}
        }
        // Include boundary pixels so thin and small selections remain usable.
        for(int i=0;i<points.Count;i++){var a=points[i];var b=points[(i+1)%points.Count];int x=a.X,y=a.Y,dx=Math.Abs(b.X-x),sx=x<b.X?1:-1,dy=-Math.Abs(b.Y-y),sy=y<b.Y?1:-1,err=dx+dy;while(true){m[y*Width+x]=true;if(x==b.X&&y==b.Y)break;int e=2*err;if(e>=dy){err+=dy;x+=sx;}if(e<=dx){err+=dx;y+=sy;}}}
        Selection=m;
    }
    public Clip Copy()
    {
        int minX=Width,minY=Height,maxX=-1,maxY=-1;for(int p=0;p<Width*Height;p++)if(Selected(p)){int x=p%Width,y=p/Width;minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
        if(maxX<0)throw new InvalidOperationException("ناحیه‌ای انتخاب نشده است.");int w=maxX-minX+1,h=maxY-minY+1;var clip=new Clip {X=minX,Y=minY,Width=w,Height=h,Pixels=new byte[w*h*4],Mask=new bool[w*h]};
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){int p=(minY+y)*Width+minX+x,q=y*w+x;if(Selected(p)){clip.Mask[q]=true;Buffer.BlockCopy(Current.Pixels,p*4,clip.Pixels,q*4,4);}} return clip;
    }
    public void Paste(Clip clip,int x,int y)
    {
        Editable();if(clip.Width<1 || clip.Height<1 || (long)clip.Width*clip.Height!=clip.Mask.Length || clip.Pixels.Length!=(long)clip.Mask.Length*4 || x<0 || y<0 || (long)x+clip.Width>Width || (long)y+clip.Height>Height)throw new ArgumentException("ناحیه خارج از تصویر است؛ عملیات انجام نشد.");
        var m=new bool[Width*Height];for(int dy=0;dy<clip.Height;dy++)for(int dx=0;dx<clip.Width;dx++){int q=dy*clip.Width+dx;if(!clip.Mask[q])continue;int p=(y+dy)*Width+x+dx;Buffer.BlockCopy(clip.Pixels,q*4,Current.Pixels,p*4,4);m[p]=true;}Selection=m;
    }
    public void MoveSelection(int dx,int dy)
    {
        Editable();var clip=Copy();int x=clip.X+dx,y=clip.Y+dy;if(x<0 || y<0 || (long)x+clip.Width>Width || (long)y+clip.Height>Height)throw new ArgumentException("انتقال به بیرون تصویر مجاز نیست؛ هیچ پیکسلی تغییر نکرد.");
        ClearSelected();Paste(clip,x,y);
    }
    public void Contour(Color target,Color ink,int thickness,bool repeat)
    {
        Editable();if(thickness<1 || thickness>32)throw new ArgumentOutOfRangeException(nameof(thickness));int count=Width*Height;var src=(byte[])Current.Pixels.Clone();var distance=new byte[count];var queue=new Queue<int>();
        bool IsTarget(int p)=>Selected(p) && ColorAt(src,p)==target;
        for(int y=0;y<Height;y++)for(int x=0;x<Width;x++){int p=y*Width+x;if(!IsTarget(p))continue;bool edge=false;
            foreach(var (dx,dy) in Directions) {int xx=x+dx,yy=y+dy;if(repeat){xx=(xx+Width)%Width;yy=(yy+Height)%Height;}else if(xx<0||yy<0||xx>=Width||yy>=Height){edge=true;break;}if(!IsTarget(yy*Width+xx)){edge=true;break;}}
            if(edge){distance[p]=1;queue.Enqueue(p);}}
        while(queue.Count>0){int p=queue.Dequeue(),x=p%Width,y=p/Width;byte d=distance[p];Put(x,y,ink);if(d>=thickness)continue;
            foreach(var (dx,dy) in Directions){int xx=x+dx,yy=y+dy;if(repeat){xx=(xx+Width)%Width;yy=(yy+Height)%Height;}else if(xx<0||yy<0||xx>=Width||yy>=Height)continue;int q=yy*Width+xx;if(distance[q]==0 && IsTarget(q)){distance[q]=(byte)(d+1);queue.Enqueue(q);}}}
    }
}
