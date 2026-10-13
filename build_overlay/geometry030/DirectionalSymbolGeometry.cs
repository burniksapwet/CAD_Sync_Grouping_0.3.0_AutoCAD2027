// Pure geometry used by the AutoCAD adapter and the DWG-derived regression tests.
// No block names, drawing coordinates or hard-coded rotation offsets are used.
namespace CadSyncFindAndReplaceBlock;

internal readonly record struct SymbolPoint(double X, double Y, double Z)
{
    public static SymbolPoint operator +(SymbolPoint a, SymbolPoint b) => new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    public static SymbolPoint operator -(SymbolPoint a, SymbolPoint b) => new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    public static SymbolPoint operator *(SymbolPoint a, double s) => new(a.X*s,a.Y*s,a.Z*s);
    public double Length => Math.Sqrt(Dot(this));
    public double Dot(SymbolPoint b) => X*b.X+Y*b.Y+Z*b.Z;
    public SymbolPoint Cross(SymbolPoint b) => new(Y*b.Z-Z*b.Y,Y*0+Z*b.X-X*b.Z,X*b.Y-Y*b.X);
    public bool Finite => double.IsFinite(X)&&double.IsFinite(Y)&&double.IsFinite(Z);
}
internal readonly record struct SymbolEdge(SymbolPoint A, SymbolPoint B);
internal enum DirectionalSymbolKind { Chevron, Triangle }
internal sealed record DirectionalSymbol(
    DirectionalSymbolKind Kind, SymbolPoint Tip, SymbolPoint Direction,
    double Depth, double Width, double Skew);

internal static class DirectionalSymbolGeometry
{
    // A narrowly qualified connection-symbol path. Anything incomplete, complex,
    // non-comparable or directionally ambiguous returns false to the old engine.
    internal static bool TryDescribe(IReadOnlyList<SymbolEdge> input, out DirectionalSymbol? symbol)
    {
        symbol=null;
        if(input.Count<2 || input.Count>32) return false;
        var raw=input.SelectMany(e=>new[]{e.A,e.B}).ToArray();
        if(raw.Any(p=>!p.Finite)) return false;
        double size=0;
        for(int i=1;i<raw.Length;i++) size=Math.Max(size,(raw[i]-raw[0]).Length);
        if(size<=1e-10) return false;
        var epsilon=Math.Max(1e-10,size*1e-7);
        var points=new List<SymbolPoint>();
        int Index(SymbolPoint p)
        {
            for(int i=0;i<points.Count;i++) if((points[i]-p).Length<=epsilon) return i;
            points.Add(p); return points.Count-1;
        }
        var edges=new HashSet<(int A,int B)>();
        foreach(var edge in input)
        {
            int a=Index(edge.A),b=Index(edge.B);
            if(a!=b) edges.Add(a<b?(a,b):(b,a));
        }
        // Duplicate hatch/outline edges and repeated SOLID corners are harmless.
        if(points.Count!=3 || (edges.Count!=2 && edges.Count!=3)) return false;
        int tip;
        DirectionalSymbolKind kind;
        if(edges.Count==2)
        {
            kind=DirectionalSymbolKind.Chevron;
            var degrees=new int[3];
            foreach(var edge in edges){degrees[edge.A]++;degrees[edge.B]++;}
            tip=Array.IndexOf(degrees,2);
            if(tip<0 || degrees.Count(d=>d==1)!=2) return false;
        }
        else
        {
            kind=DirectionalSymbolKind.Triangle;
            var ordered=edges.Select(e=>(Edge:e,Length:(points[e.A]-points[e.B]).Length))
                .OrderBy(e=>e.Length).ToArray();
            // Only a clearly narrower base identifies an arrow tip. Equilateral
            // or otherwise ambiguous triangles must not invent a facing direction.
            if(ordered[0].Length>=ordered[1].Length*0.85) return false;
            tip=3-ordered[0].Edge.A-ordered[0].Edge.B;
        }
        var ends=Enumerable.Range(0,3).Where(i=>i!=tip).ToArray();
        var a0=points[ends[0]]; var b0=points[ends[1]]; var apex=points[tip];
        var span=b0-a0; var width=span.Length;
        if(width<=epsilon) return false;
        double foot=(apex-a0).Dot(span)/(width*width);
        if(foot<0.15 || foot>0.85) return false;
        var perpendicular=apex-(a0+span*foot);
        var depth=perpendicular.Length;
        if(depth<=epsilon || width/depth<0.08 || width/depth>4.0) return false;
        // Perpendicular to the mouth/base, not tip-to-bounding-box-center:
        // an asymmetric chevron must not acquire an artificial rotation.
        symbol=new(kind,apex,perpendicular*(1.0/depth),depth,width,Math.Abs(foot-0.5));
        return true;
    }

    internal static bool TryMatch(DirectionalSymbol source, DirectionalSymbol target,
        SymbolPoint planeNormal, out double rotation)
    {
        rotation=0;
        if(source.Kind!=target.Kind || !planeNormal.Finite || planeNormal.Length<1e-10) return false;
        var ratio=(source.Width/source.Depth)/(target.Width/target.Depth);
        if(!double.IsFinite(ratio) || ratio<0.8 || ratio>1.25 || Math.Abs(source.Skew-target.Skew)>0.2) return false;
        var normal=planeNormal*(1.0/planeNormal.Length);
        if(Math.Abs(normal.Dot(source.Direction))>1e-6 || Math.Abs(normal.Dot(target.Direction))>1e-6) return false;
        double cosine=Math.Clamp(target.Direction.Dot(source.Direction),-1.0,1.0);
        double sine=normal.Dot(target.Direction.Cross(source.Direction));
        rotation=Math.Atan2(sine,cosine);
        return double.IsFinite(rotation);
    }
}
