using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System.Windows.Forms;

namespace CadSyncFindAndReplaceBlock;

// Reads a target definition name only. Never passes picked instance IDs,
// position, rotation, dynamic state or scale to the replacement engine.
internal static class DrawingTargetPicker
{
    internal static string? Pick(Document doc, Form dialog)
    {
        if (!ReferenceEquals(Application.DocumentManager.MdiActiveDocument, doc))
            return null;

        var editor = doc.Editor;
        var implied = editor.SelectImplied();
        ObjectId[] saved;
        using (var selection = implied.Value)
            saved = implied.Status == PromptStatus.OK && selection != null
                ? selection.GetObjectIds() : Array.Empty<ObjectId>();

        // AutoCAD hides the modal dialog and restores it/focus when disposed.
        // Do not manually Hide/Show or close/recreate the target dialog.
        using (editor.StartUserInteraction(dialog.Handle))
        {
            try
            {
                // Do not let the source PICKFIRST set become the target pick.
                using (doc.LockDocument())
                    editor.SetImpliedSelection(Array.Empty<ObjectId>());

                var options = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect replacement block (click or window; first valid block is used): ",
                    SingleOnly = true, // One selection gesture; a window may contain many entities.
                    SinglePickInSpace = false, // Keep window/crossing selection available.
                    AllowDuplicates = false,
                    AllowSubSelections = false,
                    RejectObjectsFromNonCurrentSpace = true,
                    RejectObjectsOnLockedLayers = false // Picking reads a definition; it does not edit it.
                };
                var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
                var result = editor.GetSelection(options, filter);
                using var selected = result.Value;
                if (result.Status != PromptStatus.OK || selected == null ||
                    !ReferenceEquals(Application.DocumentManager.MdiActiveDocument, doc))
                    return null;

                using (doc.LockDocument())
                    return FirstTargetName(doc.Database, selected.GetObjectIds());
            }
            finally
            {
                if (ReferenceEquals(Application.DocumentManager.MdiActiveDocument, doc))
                {
                    using (doc.LockDocument())
                        editor.SetImpliedSelection(saved.Where(id => !id.IsNull && id.IsValid && !id.IsErased).ToArray());
                    Application.UpdateScreen();
                }
            }
        }
    }

    internal static string? FirstTargetName(Database db, IEnumerable<ObjectId> ids)
    {
        using var tr = db.TransactionManager.StartTransaction();
        // Preserve AutoCAD's returned order. No sorting, nearest/leftmost rule,
        // bulk replacement, nested entity traversal or instance-property copy.
        foreach (var id in ids)
        {
            if (id.IsNull || !id.IsValid || id.IsErased ||
                id.Database.UnmanagedObject != db.UnmanagedObject) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference block) continue;
            if (tr.GetObject(block.OwnerId, OpenMode.ForRead) is not BlockTableRecord owner || !owner.IsLayout) continue;
            var actual = (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead);
            if (actual.IsLayout || actual.IsFromExternalReference || actual.IsFromOverlayReference) continue;

            // Dynamic references often have anonymous *U names. Use their named
            // authoring definition, just as the existing replacement catalog does.
            var effectiveId = block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord;
            var effective = (BlockTableRecord)tr.GetObject(effectiveId, OpenMode.ForRead);
            if (effective.IsLayout || effective.IsAnonymous || effective.IsFromExternalReference ||
                effective.IsFromOverlayReference || string.IsNullOrWhiteSpace(effective.Name)) continue;
            return effective.Name;
        }
        return null;
    }
}
