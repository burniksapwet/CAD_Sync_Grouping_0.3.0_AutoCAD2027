using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CadSyncGrouping;

internal static class GeometryHelper
{
    public static bool TryGetExtents(Entity entity, out Extents2d ext)
    {
        try
        {
            // AutoCAD evaluates the transformed block contents, including nested
            // transforms. Never transform an already world-space box a second time.
            var e = entity is BlockReference block ? block.GeometryExtentsBestFit() : entity.GeometricExtents;
            ext = new Extents2d(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y);
            return double.IsFinite(ext.MinX) && double.IsFinite(ext.MinY) &&
                double.IsFinite(ext.MaxX) && double.IsFinite(ext.MaxY) && ext.Diagonal > 1e-9;
        }
        catch
        {
            // Missing geometry is uncertainty, not permission to use insertion points.
        }

        ext = default;
        return false;
    }

    public static double DistancePointToRect(double x, double y, Extents2d rect)
    {
        var dx = x < rect.MinX ? rect.MinX - x : x > rect.MaxX ? x - rect.MaxX : 0.0;
        var dy = y < rect.MinY ? rect.MinY - y : y > rect.MaxY ? y - rect.MaxY : 0.0;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static double DistanceLabelToRect(Extents2d text, Extents2d block)
    {
        // Above/below and beside-label placements use the edge midpoints. A
        // long label's center must not penalize its adjacent symbol; corners
        // alone must not attract a diagonally offset unrelated symbol.
        return new[] {
            DistancePointToRect(text.MinX,text.CenterY,block),
            DistancePointToRect(text.MaxX,text.CenterY,block),
            DistancePointToRect(text.CenterX,text.MinY,block),
            DistancePointToRect(text.CenterX,text.MaxY,block)
        }.Min();
    }

    public static Extents3d UnionExtents(IEnumerable<Entity> entities)
    {
        var has = false;
        var min = new Point3d();
        var max = new Point3d();

        foreach (var entity in entities)
        {
            try
            {
                var e = entity.GeometricExtents;
                if (!has)
                {
                    min = e.MinPoint;
                    max = e.MaxPoint;
                    has = true;
                }
                else
                {
                    min = new Point3d(Math.Min(min.X, e.MinPoint.X), Math.Min(min.Y, e.MinPoint.Y), Math.Min(min.Z, e.MinPoint.Z));
                    max = new Point3d(Math.Max(max.X, e.MaxPoint.X), Math.Max(max.Y, e.MaxPoint.Y), Math.Max(max.Z, e.MaxPoint.Z));
                }
            }
            catch
            {
                // Ignore entities without geometric extents.
            }
        }

        if (!has)
            return new Extents3d(new Point3d(-1, -1, 0), new Point3d(1, 1, 0));

        return new Extents3d(min, max);
    }
}
