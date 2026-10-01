using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
namespace CadSyncGrouping;
internal static class PluginState
{
    public static ScanResult? LastScan { get; private set; }
    internal static bool SameDatabase(Database? first,Database? second)=>first!=null&&second!=null&&first.UnmanagedObject==second.UnmanagedObject;
    public static void SetScan(Document doc,ScanResult result)
    {
        // Initialize the UI selection from the scanner's final safe selections,
        // after conflicts, existing groups and saved Ignore decisions are applied.
        foreach(var row in result.Candidates)
            row.ActionSelected=row.Selected && row.Status is CandidateStatus.Ready or CandidateStatus.Reviewed;
        LastScan=result;
    }
    // AutoCAD can return distinct managed wrappers for the same native database.
    // GroupWriter rechecks the source fingerprint and membership before writing.
    public static bool Matches(Document doc)=>LastScan!=null&&SameDatabase(LastScan.Database,doc.Database);
    public static void Clear(){LastScan=null;}
}
