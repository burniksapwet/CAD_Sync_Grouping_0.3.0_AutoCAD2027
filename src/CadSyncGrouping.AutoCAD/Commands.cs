using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
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
        doc?.Editor.WriteMessage("\nCAD Sync Grouping loaded. Run CADGROUPSCAN.");
    }

    public void Terminate() { PluginState.Clear(); }

    [CommandMethod("CADGROUPSCAN", CommandFlags.Modal)]
    public void ScanDrawing()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;

        var scan = DrawingScanner.Scan(doc.Database);
        PluginState.SetScan(doc, scan);

        var ready = scan.Candidates.Count(c => c.Status == CandidateStatus.Ready);
        var review = scan.Candidates.Count(c => c.Status is CandidateStatus.Ambiguous or CandidateStatus.Conflict or CandidateStatus.NoMatch);
        doc.Editor.WriteMessage(
            $"\nCAD Sync Grouping: {scan.LocationTextCount} Location-ID text entities, {scan.ValveBlockCount} Valve blocks, " +
            $"{scan.ExistingValveGroups} existing Valve groups, {ready} ready, {review} needing review.");

        ReviewPalette.Show();
    }

    [CommandMethod("CADGROUPREVIEW", CommandFlags.Modal)]
    public void ShowReview()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;

        if (!PluginState.Matches(doc))
        {
            var scan = DrawingScanner.Scan(doc.Database);
            PluginState.SetScan(doc, scan);
        }
        ReviewPalette.Show();
    }

    [CommandMethod("CADGROUPCREATE", CommandFlags.Modal)]
    public void CreateGroups()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;

        if (!PluginState.Matches(doc) || PluginState.LastScan == null)
        {
            doc.Editor.WriteMessage("\nRun CADGROUPSCAN first, review the relationships, then use CADGROUPCREATE.");
            return;
        }

        var selected=PluginState.LastScan.Candidates.Count(c=>c.Selected && c.Status is CandidateStatus.Ready or CandidateStatus.Reviewed);
        if(selected==0){doc.Editor.WriteMessage("\nNo Ready/Reviewed relationships selected.");return;}
        var prompt=new PromptKeywordOptions($"\nCreate {selected} selected Ready/Reviewed groups? [Yes/No] <No>: ","Yes No"){AllowNone=true};
        prompt.Keywords.Default="No";
        var answer=doc.Editor.GetKeywords(prompt);if(answer.Status!=PromptStatus.OK||answer.StringResult!="Yes")return;
        int count;try{count=GroupWriter.CreateApprovedGroups(doc.Database,PluginState.LastScan);}catch(System.Exception ex){doc.Editor.WriteMessage("\nNo groups created: "+ex.Message);return;}
        doc.Editor.WriteMessage($"\nCreated {count} approved CAD Sync group(s).");

        var refreshed = DrawingScanner.Scan(doc.Database);
        PluginState.SetScan(doc, refreshed);
        ReviewPalette.Show();
    }
}
