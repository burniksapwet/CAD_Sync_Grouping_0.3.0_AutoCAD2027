using Autodesk.AutoCAD.DatabaseServices;

namespace CadSyncFindAndReplaceBlock;

internal static class BlockFinderScanner
{
    public static List<BlockRecord> Scan(Database db, IReadOnlyCollection<ObjectId>? scopeIds = null)
    {
        var rows = new List<BlockRecord>();
        HashSet<ObjectId>? scope = scopeIds == null ? null : new HashSet<ObjectId>(scopeIds);

        using var tr = db.TransactionManager.StartTransaction();
        var modelId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        var model = (BlockTableRecord)tr.GetObject(modelId, OpenMode.ForRead);

        foreach (ObjectId id in model)
        {
            if (scope != null && !scope.Contains(id)) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference block) continue;

            var actual = (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead);
            if (actual.IsFromExternalReference || actual.IsFromOverlayReference || actual.IsLayout) continue;

            var effectiveId = block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord;
            var effective = (BlockTableRecord)tr.GetObject(effectiveId, OpenMode.ForRead);

            rows.Add(new BlockRecord
            {
                Id = id,
                Layer = block.Layer,
                EffectiveName = effective.Name,
                ActualName = actual.Name,
                IsDynamic = block.IsDynamicBlock,
                XScale = block.ScaleFactors.X,
                YScale = block.ScaleFactors.Y,
                ZScale = block.ScaleFactors.Z
            });
        }

        tr.Commit();
        return rows;
    }

    public static List<string> TargetDefinitionNames(Database db)
    {
        var names = new List<string>();
        using var tr = db.TransactionManager.StartTransaction();
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId id in table)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockTableRecord definition) continue;
            if (definition.IsLayout || definition.IsFromExternalReference || definition.IsFromOverlayReference || definition.IsAnonymous) continue;
            if (string.IsNullOrWhiteSpace(definition.Name)) continue;
            names.Add(definition.Name);
        }

        tr.Commit();
        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
