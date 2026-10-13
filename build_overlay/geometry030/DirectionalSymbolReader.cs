using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CadSyncFindAndReplaceBlock;

internal static class DirectionalSymbolReader
{
    internal static bool TryRead(BlockReference reference, Transaction tr, out DirectionalSymbol? symbol)
    {
        symbol=null;
        try
        {
            // Leave all attribute-bearing symbols on the established path.
            if(reference.AttributeCollection.Count>0) return false;
            var edges=new List<SymbolEdge>();
            var stack=new HashSet<ObjectId>();
            int visited=0;
            if(!ReadBlock(reference,tr,Matrix3d.Identity,edges,stack,0,ref visited)) return false;
            return DirectionalSymbolGeometry.TryDescribe(edges,out symbol);
        }
        catch(System.Exception)
        {
            // Read-only optional recognition. Unsupported native geometry must
            // not stop or change the established replacement/preview behavior.
            return false;
        }
    }

    private static SymbolPoint Point(Point3d p) => new(p.X,p.Y,p.Z);

    private static bool ReadBlock(BlockReference reference,Transaction tr,Matrix3d parent,
        List<SymbolEdge> edges,HashSet<ObjectId> stack,int depth,ref int visited)
    {
        if(depth>6 || !stack.Add(reference.BlockTableRecord)) return false;
        try
        {
            var block=(BlockTableRecord)tr.GetObject(reference.BlockTableRecord,OpenMode.ForRead);
            if(block.IsLayout || block.IsFromExternalReference || block.IsFromOverlayReference) return false;
            var transform=parent*reference.BlockTransform;
            foreach(ObjectId id in block)
            {
                if(++visited>64 || edges.Count>32) return false;
                if(tr.GetObject(id,OpenMode.ForRead) is not Entity e) continue;
                if(!e.Visible) continue;
                var layer=(LayerTableRecord)tr.GetObject(e.LayerId,OpenMode.ForRead);
                if(layer.IsOff || layer.IsFrozen) continue;
                if(e is BlockReference nested)
                {
                    if(nested.AttributeCollection.Count>0 || !ReadBlock(nested,tr,transform,edges,stack,depth+1,ref visited)) return false;
                }
                else if(!ReadPrimitive(e,transform,edges)) return false;
            }
            return edges.Count<=32;
        }
        finally { stack.Remove(reference.BlockTableRecord); }
    }

    private static bool ReadPrimitive(Entity e,Matrix3d transform,List<SymbolEdge> edges)
    {
        void Add(Point3d a,Point3d b)=>edges.Add(new(Point(a.TransformBy(transform)),Point(b.TransformBy(transform))));
        void Polygon(IReadOnlyList<Point3d> points)
        {
            for(int i=0;i<points.Count;i++) Add(points[i],points[(i+1)%points.Count]);
        }
        switch(e)
        {
            case Line line:
                if(Math.Abs(line.Thickness)>1e-10) return false;
                Add(line.StartPoint,line.EndPoint);
                return true;
            case Polyline poly:
            {
                if(Math.Abs(poly.Thickness)>1e-10 || poly.NumberOfVertices>16) return false;
                int count=poly.Closed?poly.NumberOfVertices:poly.NumberOfVertices-1;
                if(count<1) return false;
                for(int i=0;i<count;i++)
                {
                    if(Math.Abs(poly.GetBulgeAt(i))>1e-12) return false;
                    var a=poly.GetPoint3dAt(i); var b=poly.GetPoint3dAt((i+1)%poly.NumberOfVertices);
                    double start=poly.GetStartWidthAt(i),end=poly.GetEndWidthAt(i);
                    if(start>1e-10 || end>1e-10)
                    {
                        // Read the actual tapered outline, never its centerline.
                        if(count!=1 || (b-a).Length<1e-10) return false;
                        var side=poly.Normal.GetNormal().CrossProduct((b-a).GetNormal());
                        Polygon(new[]{a+side*(start/2),b+side*(end/2),b-side*(end/2),a-side*(start/2)});
                    }
                    else Add(a,b);
                }
                return true;
            }
            case Solid solid:
            {
                if(Math.Abs(solid.Thickness)>1e-10) return false;
                // SOLID storage is 0,1,3,2 around the perimeter. Repeated corners
                // need not be indices 2 and 3 (the supplied source repeats 0/2).
                Polygon(new[]{solid.GetPointAt(0),solid.GetPointAt(1),solid.GetPointAt(3),solid.GetPointAt(2)});
                return true;
            }
            case Hatch hatch:
            {
                if(!hatch.IsSolidFill || hatch.NumberOfLoops!=1) return false;
                var loop=hatch.GetLoopAt(0);
                var plane=Matrix3d.PlaneToWorld(hatch.Normal);
                Point3d Lift(Point2d p)=>new Point3d(p.X,p.Y,hatch.Elevation).TransformBy(plane);
                if(loop.IsPolyline)
                {
                    var vertices=loop.Polyline;
                    if(vertices.Count<3 || vertices.Count>16) return false;
                    var points=new List<Point3d>();
                    for(int i=0;i<vertices.Count;i++)
                    {
                        if(Math.Abs(vertices[i].Bulge)>1e-12) return false;
                        points.Add(Lift(vertices[i].Vertex));
                    }
                    Polygon(points);
                    return true;
                }
                if(loop.Curves.Count<3 || loop.Curves.Count>16) return false;
                foreach(Curve2d curve in loop.Curves)
                {
                    if(curve is not LineSegment2d line2) return false;
                    Add(Lift(line2.StartPoint),Lift(line2.EndPoint));
                }
                return true;
            }
            default: return false;
        }
    }
}
