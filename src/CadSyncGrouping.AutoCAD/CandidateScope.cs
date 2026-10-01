using Autodesk.AutoCAD.DatabaseServices;
namespace CadSyncGrouping;
internal static class CandidateScope
{
    // No automatic name or shape exclusions: unwanted rows are ignored manually.
    internal static bool Eligible(BlockReference br, Transaction tr)
    {
        if(!br.Layer.Equals("Valve",StringComparison.OrdinalIgnoreCase))return false;
        var definition=(BlockTableRecord)tr.GetObject(br.BlockTableRecord,OpenMode.ForRead);
        return !definition.IsFromExternalReference;
    }
}
