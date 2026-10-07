using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace CadSyncGrouping;

internal static class OptionalMacros
{
    internal static void TryRunRenameBlocksSilently()
    {
        try
        {
            using var args = new ResultBuffer(
                new TypedValue((int)LispDataType.Text, "c:RENAMEBLOCKS"));
            using var result = Application.Invoke(args);
        }
        catch
        {
            // RENAMEBLOCKS is optional. If the LSP/FAS is not loaded, do
            // nothing and leave GROUPSCAN/Rescan behavior unchanged.
        }
    }

    internal static async Task TryRunRenameBlocksInCommandContextSilently()
    {
        try
        {
            await Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                TryRunRenameBlocksSilently();
                return Task.CompletedTask;
            }, null);
        }
        catch
        {
            // The optional macro must never block a normal rescan.
        }
    }
}
