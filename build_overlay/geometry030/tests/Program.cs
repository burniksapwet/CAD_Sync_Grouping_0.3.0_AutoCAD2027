using System.Text.Json;
using CadSyncFindAndReplaceBlock;

internal static class Program
{
    static int passed;
    static readonly SymbolPoint Z=new(0,0,1);
    static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Near(double a,double b,string why,double tol=1e-9)=>Assert(Math.Abs(a-b)<tol,$"{why}: {a:R} vs {b:R}");
    static void Near(SymbolPoint a,SymbolPoint b,string why,double tol=1e-9)=>Assert((a-b).Length<tol,$"{why}: {a} vs {b}");
    static void Check(string name,Action test){test();passed++;Console.WriteLine("PASS "+name);}
    static SymbolPoint P(JsonElement p)=>new(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble());
    static SymbolEdge[] Edges(JsonElement definitions,string name)=>definitions.GetProperty(name).EnumerateArray().Select(e=>new SymbolEdge(P(e[0]),P(e[1]))).ToArray();
    static SymbolEdge[] Map(IEnumerable<SymbolEdge> edges,Func<SymbolPoint,SymbolPoint> map)=>edges.Select(e=>new SymbolEdge(map(e.A),map(e.B))).ToArray();
    static SymbolPoint Rotate(SymbolPoint p,double angle)=>new(Math.Cos(angle)*p.X-Math.Sin(angle)*p.Y,Math.Sin(angle)*p.X+Math.Cos(angle)*p.Y,p.Z);
    static (SymbolPoint Min,SymbolPoint Max) Bounds(IEnumerable<SymbolEdge> edges)
    {
        var p=edges.SelectMany(e=>new[]{e.A,e.B}).ToArray();
        return(new(p.Min(v=>v.X),p.Min(v=>v.Y),p.Min(v=>v.Z)),new(p.Max(v=>v.X),p.Max(v=>v.Y),p.Max(v=>v.Z)));
    }
    static SymbolPoint Center(IEnumerable<SymbolEdge> edges){var b=Bounds(edges);return(b.Min+b.Max)*0.5;}
    // Mathematical reproduction of the unchanged .029 size routine, compared to
    // the measured scales in the user's DWG before testing the new anchor path.
    static double Scale(SymbolEdge[] source,SymbolEdge[] target)
    {
        var sb=Bounds(source);var tb=Bounds(target);var sd=sb.Max-sb.Min;var td=tb.Max-tb.Min;
        var ratios=new[]{sd.X/td.X,sd.Y/td.Y};
        return ratios.Max()/ratios.Min()<=1.15?Math.Sqrt(ratios[0]*ratios[1]):Math.Sqrt((sd.X*sd.X+sd.Y*sd.Y)/(td.X*td.X+td.Y*td.Y));
    }
    static DirectionalSymbol Describe(SymbolEdge[] e)
    {
        Assert(DirectionalSymbolGeometry.TryDescribe(e,out var s),"Not recognized");return s!;
    }
    static (SymbolEdge[] Placed,double Scale,double Angle) Place(SymbolEdge[] source,SymbolEdge[] target,bool standard,SymbolPoint normal)
    {
        var s=Describe(source);var t=Describe(target);
        Assert(DirectionalSymbolGeometry.TryMatch(s,t,normal,out var angle),"Pair not comparable");
        // The fixtures are planar XY; rotate with the supplied normal sign.
        var placed=Map(target,p=>t.Tip+Rotate(p-t.Tip,angle*Math.Sign(normal.Z)));
        double scale=standard?1:Scale(source,placed);
        placed=Map(placed,p=>s.Tip+(p-t.Tip)*scale);
        return(placed,scale,angle);
    }
    static void PlacementCheck(SymbolEdge[] source,SymbolEdge[] target,bool standard,
        double angle,double sx,double sy,SymbolPoint pos,SymbolPoint targetOffset,double directionTolerance=1e-9)
    {
        var s=Map(source,p=>pos+Rotate(new(p.X*sx,p.Y*sy,p.Z),angle));
        var t=Map(target,p=>pos+Rotate(new((p.X+targetOffset.X)*(standard?Math.Sign(sx):sx),
            (p.Y+targetOffset.Y)*(standard?Math.Sign(sy):sy),p.Z+targetOffset.Z),angle));
        var result=Place(s,t,standard,Z);
        var sd=Describe(s);var td=Describe(result.Placed);
        Near(sd.Tip,td.Tip,"Connection moved");
        Near(sd.Direction,td.Direction,"Visible facing changed",directionTolerance);
        if(standard) Near(result.Scale,1,"Standard changed its 1:1 scale");
    }
    static int Main()
    {
        try
        {
            using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"drawing2_geometry_fixture.json")));
            var root=fixture.RootElement;var defs=root.GetProperty("definitions");
            var sensor=Edges(defs,"Sensor");var spray=Edges(defs,"Sensor Wash Spray");
            var oldArrow=Edges(defs,"G1ARRI08");var newArrow=Edges(defs,"Piping Arrow Head");
            Console.WriteLine("Geometry fixture extracted from "+root.GetProperty("sourceFile").GetString());
            Console.WriteLine("SHA256 "+root.GetProperty("sourceSha256").GetString());
            Check("DWG Sensor/Spray share origin junction, but their upper arms are not identical",()=>{
                Near(Describe(sensor).Tip,new(0,0,0),"Source apex");Near(Describe(spray).Tip,new(0,0,0),"Target apex");
                Assert(Math.Abs(sensor[1].B.Y-spray[1].B.Y)>0.02,"Fixture lost asymmetry");
            });
            Check("old sensor centering reproduces both saved scale and measured displacement",()=>{
                var k=Scale(sensor,spray);Near(k,root.GetProperty("observedSensorScale").GetDouble(),"Observed scale");
                var placed=Map(spray,p=>Center(sensor)+(p-Center(spray))*k);
                Near(Describe(placed).Tip,P(root.GetProperty("observedSensorTipOffset")),"Observed incorrect offset");
            });
            Check("SOLID source repeated corner and duplicate hatch/outline target give triangular arrows",()=>{
                Assert(Describe(oldArrow).Kind==DirectionalSymbolKind.Triangle,"Source triangle");
                Assert(Describe(newArrow).Kind==DirectionalSymbolKind.Triangle,"Target triangle");
                Near(Describe(oldArrow).Direction.Dot(Describe(newArrow).Direction),-1,"Authored directions were not opposite");
            });
            Check("old arrow centering reproduces the saved reversed-arrow displacement",()=>{
                var s=Map(oldArrow,p=>Rotate(p,Math.PI*1.5));var t=Map(newArrow,p=>Rotate(p,Math.PI*1.5));
                var k=Scale(s,t);Near(k,root.GetProperty("observedArrowScale").GetDouble(),"Observed arrow scale");
                var placed=Map(t,p=>Center(s)+(p-Center(t))*k);
                Near(Describe(placed).Tip,P(root.GetProperty("observedArrowTipOffset")),"Observed incorrect arrow offset");
            });
            foreach(bool standard in new[]{false,true})
            {
                string mode=standard?"Standard":"Scale";
                foreach(double degrees in new[]{0.0,33.0,90.0,180.0,270.0})
                {
                    Check($"Sensor/Spray {mode} at {degrees} degrees: junction and facing retained",()=>
                        PlacementCheck(sensor,spray,standard,degrees*Math.PI/180,1,1,new(2.5,18.6,0),new(0,0,0)));
                    Check($"G1ARRI08/Arrow {mode} at {degrees} degrees: no reversal and tip retained",()=>
                        PlacementCheck(oldArrow,newArrow,standard,degrees*Math.PI/180,1,1,new(2.5,17.46,0),new(0,0,0)));
                }
                foreach(var signs in new[]{(1.0,1.0),(-1.0,1.0),(1.0,-1.0),(-1.0,-1.0)})
                {
                    Check($"Sensor/Spray {mode} mirrored {signs}, displaced target basepoint",()=>
                        PlacementCheck(sensor,spray,standard,0.41,2.5*signs.Item1,2.5*signs.Item2,new(40,-12,0),new(-17,25,0)));
                    Check($"Arrow {mode} mirrored {signs}, displaced target basepoint",()=>
                        PlacementCheck(oldArrow,newArrow,standard,0.41,2.5*signs.Item1,2.5*signs.Item2,new(40,-12,0),new(-17,25,0)));
                }
            }
            Check("asymmetric Sensor does not acquire a spurious small-angle rotation",()=>{
                Assert(DirectionalSymbolGeometry.TryMatch(Describe(sensor),Describe(spray),Z,out var a),"Match");Near(a,0,"Spurious rotation");
            });
            Check("arrow facing correction is derived from geometry rather than a name/constant",()=>{
                Assert(DirectionalSymbolGeometry.TryMatch(Describe(oldArrow),Describe(newArrow),Z,out var a),"Match");Near(Math.Abs(a),Math.PI,"Correction");
                var rotated=Map(newArrow,p=>Rotate(p,0.27));
                var placed=Place(oldArrow,rotated,false,Z);
                Near(Describe(placed.Placed).Direction,Describe(oldArrow).Direction,"Arbitrary authored rotation");
            });
            Check("negative extrusion normal produces the same physical correction",()=>{
                var placed=Place(oldArrow,newArrow,false,new(0,0,-1));
                Near(Describe(placed.Placed).Direction,Describe(oldArrow).Direction,"Negative normal");
            });
            Check("shifted source and target definition origins both retain the source geometric tip",()=>{
                var shifted=Map(sensor,p=>p+new SymbolPoint(31,-4,0));
                PlacementCheck(shifted,spray,false,0,1,1,new(7,8,0),new(-23,14,0));
            });
            // Subtracting million-unit positions to recover a 0.1-unit arm loses
            // several low bits. Allow 1e-7 in unit direction only in this test;
            // the actual tip/connection position must still agree within 1e-9.
            Check("large drawing coordinates remain stable",()=>
                PlacementCheck(sensor,spray,false,0.4,1,1,new(1000000,1000000,0),new(-17,21,0),directionTolerance:1e-7));
            Check("already matching source/target leaves orientation unchanged",()=>{
                var placed=Place(newArrow,newArrow,false,Z);Near(placed.Scale,1,"Scale changed");Near(placed.Angle,0,"Rotation changed");
            });
            Check("duplicate and reversed hatch edges do not change arrow interpretation",()=>{
                var doubled=newArrow.Concat(newArrow.Reverse().Select(e=>new SymbolEdge(e.B,e.A))).ToArray();
                Near(Describe(doubled).Direction,Describe(newArrow).Direction,"Duplicate boundary");
            });
            Check("nonuniform existing Scale transform retains arrow tip and facing",()=>
                PlacementCheck(oldArrow,newArrow,false,0.0,2.0,0.6,new(2,3,0),new(0,0,0)));
            Check("equilateral triangle has no unique direction and falls back",()=>{
                var p=new[]{new SymbolPoint(0,0,0),new SymbolPoint(1,0,0),new SymbolPoint(.5,Math.Sqrt(3)/2,0)};
                var e=new[]{new SymbolEdge(p[0],p[1]),new SymbolEdge(p[1],p[2]),new SymbolEdge(p[2],p[0])};
                Assert(!DirectionalSymbolGeometry.TryDescribe(e,out _),"Ambiguous triangle accepted");
            });
            Check("rectangle / complex shape falls back to established placement",()=>{
                var e=new[]{new SymbolEdge(new(0,0,0),new(1,0,0)),new SymbolEdge(new(1,0,0),new(1,1,0)),new SymbolEdge(new(1,1,0),new(0,1,0)),new SymbolEdge(new(0,1,0),new(0,0,0))};
                Assert(!DirectionalSymbolGeometry.TryDescribe(e,out _),"Rectangle accepted");
            });
            Check("unrelated triangle and chevron do not share the special placement path",()=>
                Assert(!DirectionalSymbolGeometry.TryMatch(Describe(sensor),Describe(newArrow),Z,out _),"Unrelated symbols matched"));
            Check("incompatible proportions do not qualify",()=>{
                var stretched=Map(spray,p=>new(p.X*10,p.Y,p.Z));
                Assert(!DirectionalSymbolGeometry.TryMatch(Describe(sensor),Describe(stretched),Z,out _),"Bad proportions accepted");
            });
            Check("non-coplanar direction does not qualify",()=>
                Assert(!DirectionalSymbolGeometry.TryMatch(Describe(sensor),Describe(spray),new(1,0,0),out _),"Bad plane accepted"));
            Check("empty, degenerate and nonfinite inputs fail safely",()=>{
                Assert(!DirectionalSymbolGeometry.TryDescribe(Array.Empty<SymbolEdge>(),out _),"Empty");
                Assert(!DirectionalSymbolGeometry.TryDescribe(new[]{new SymbolEdge(new(0,0,0),new(0,0,0)),new SymbolEdge(new(0,0,0),new(0,0,0))},out _),"Degenerate");
                Assert(!DirectionalSymbolGeometry.TryDescribe(new[]{new SymbolEdge(new(double.NaN,0,0),new(0,0,0)),sensor[1]},out _),"NaN");
            });
            Console.WriteLine($"{passed} geometry scenarios passed. The same pure geometry classifier/matcher is linked into the actual plugin. DWG-derived geometry and simulated transforms were used; native AutoCAD was NOT run.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
