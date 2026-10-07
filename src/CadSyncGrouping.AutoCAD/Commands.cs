using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(CadSyncGrouping.Commands))]

namespace CadSyncGrouping;

public sealed class Commands : IExtensionApplication
{
    public void Initialize()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        Application.DocumentManager.DocumentActivated+=(_,_)=>ReviewPalette.Refresh();
        Application.DocumentManager.DocumentToBeDestroyed+=(_,e)=>{if(PluginState.SameDatabase(PluginState.LastScan?.Database,e.Document.Database))PluginState.Clear();};
        doc?.Editor.WriteMessage("\nCAD Sync Grouping loaded. Run GROUPSCAN.");
    }

    public void Terminate() { PluginState.Clear(); }

    [CommandMethod("GROUPSCAN", CommandFlags.Modal)]
    public void ScanDrawing()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;

        // Optional staff macro: run it first so the scan sees any block-name
        // cleanup it performs. Missing RENAMEBLOCKS is intentionally silent.
        OptionalMacros.TryRunRenameBlocksSilently();

        var scan = DrawingScanner.Scan(doc.Database);
        PluginState.SetScan(doc, scan);

        var ready = scan.Candidates.Count(c => c.Status == CandidateStatus.Ready);
        var review = scan.Candidates.Count(c => c.Status is CandidateStatus.Ambiguous or CandidateStatus.Conflict or CandidateStatus.NoMatch);
        var verified = scan.Candidates.Count(c => c.Status == CandidateStatus.ManuallyVerified);
        doc.Editor.WriteMessage(
            $"\nCAD Sync Grouping: {scan.LocationTextCount} Location-ID text entities, {scan.ValveBlockCount} Valve blocks, " +
            $"{scan.ExistingValveGroups} existing Valve groups, {verified} manually verified, {ready} ready, {review} needing review.");

        ReviewPalette.Show();
    }
}
