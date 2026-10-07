using Autodesk.AutoCAD.DatabaseServices;
namespace CadSyncGrouping;
public static class GroupWriter
{
    public static int CreateApprovedGroups(Database db,ScanResult scan)
        => CreateApprovedGroups(db,scan,out _);

    public static int CreateApprovedGroups(Database db,ScanResult scan,out int manuallyVerifiedCount)
    {
        if(!PluginState.SameDatabase(scan.Database,db))throw new InvalidOperationException("Results belong to another drawing. Rescan.");
        var fresh=DrawingScanner.Scan(db,false);
        if(scan.GeometrySignature!=fresh.GeometrySignature)throw new InvalidOperationException("Drawing geometry or text changed. Rescan and review again.");
        var approved=scan.Candidates.Where(c=>c.Selected && c.Status is CandidateStatus.Ready or CandidateStatus.Reviewed or CandidateStatus.Ambiguous or CandidateStatus.NoMatch or CandidateStatus.Conflict).ToList();
        var manuallyVerified=approved.Where(c=>c.Status is CandidateStatus.Ambiguous or CandidateStatus.NoMatch or CandidateStatus.Conflict).ToHashSet();
        manuallyVerifiedCount=0;
        if(approved.GroupBy(c=>c.TextId).Any(g=>g.Count()>1)||approved.GroupBy(c=>c.ProposedBlockId).Any(g=>g.Count()>1)||approved.GroupBy(c=>c.LocationId,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))
            throw new InvalidOperationException("Selected relationships contain duplicate text, Location IDs or valves. Resolve the conflict first.");
        using var tr=db.TransactionManager.StartTransaction();
        var groups=GroupMembership.Read(db,tr);var create=new List<GroupingCandidate>();
        foreach(var row in approved)
        {
            if(row.TextId.IsNull||row.ProposedBlockId.IsNull||!row.TextId.IsValid||!row.ProposedBlockId.IsValid||row.TextId.IsErased||row.ProposedBlockId.IsErased)
                throw new InvalidOperationException("A selected entity is unavailable. Rescan.");
            var text=tr.GetObject(row.TextId,OpenMode.ForRead) as Entity;var valve=tr.GetObject(row.ProposedBlockId,OpenMode.ForRead) as BlockReference;
            if(text is not (DBText or MText)||valve==null||text.OwnerId!=SymbolUtilityServices.GetBlockModelSpaceId(db)||valve.OwnerId!=text.OwnerId||
               !DrawingScanner.LocationLayers.Contains(text.Layer)||!CandidateScope.Eligible(valve,tr)||
               !DrawingScanner.IsLocationId(DrawingScanner.TextOf(text))||DrawingScanner.TextOf(text)!=row.LocationId||
               !DrawingScanner.UsableLayer(text,tr)||!DrawingScanner.UsableLayer(valve,tr)||!GeometryHelper.TryGetExtents(valve,out _))
                throw new InvalidOperationException("A selected relationship is outside the supported visible, unlocked Model Space scope. Rescan.");
            if(((BlockTableRecord)tr.GetObject(valve.BlockTableRecord,OpenMode.ForRead)).IsFromExternalReference)
                throw new InvalidOperationException("External references are outside the supported grouping scope.");
            if(groups.SameGroup(row.TextId,row.ProposedBlockId))continue;
            if(groups.Contains(row.TextId)||groups.Contains(row.ProposedBlockId))throw new InvalidOperationException("An entity already belongs to an existing group. No groups were changed.");
            foreach(var leaderId in row.LeaderIds)
            {
                if(!leaderId.IsValid||leaderId.IsErased||groups.Contains(leaderId)||approved.Any(other=>other!=row&&other.LeaderIds.Contains(leaderId)))
                    throw new InvalidOperationException("Leader is shared, unavailable or already grouped. Rescan.");
                var leader=tr.GetObject(leaderId,OpenMode.ForRead) as Entity;
                if(leader is not (Leader or MLeader)||leader.OwnerId!=text.OwnerId||!DrawingScanner.UsableLayer(leader,tr))
                    throw new InvalidOperationException("Leader is no longer eligible. Rescan.");
            }
            create.Add(row);
        }
        var dictionary=(DBDictionary)tr.GetObject(db.GroupDictionaryId,OpenMode.ForWrite);
        foreach(var row in create)
        {
            using var group=new Autodesk.AutoCAD.DatabaseServices.Group("CAD Sync reviewed Location ID / Valve relationship",true);
            dictionary.SetAt("*",group);tr.AddNewlyCreatedDBObject(group,true);
            group.Append(new ObjectIdCollection(new[]{row.TextId,row.ProposedBlockId}.Concat(row.LeaderIds).Distinct().ToArray()));
        }

        var manualRows=create.Where(manuallyVerified.Contains).ToList();
        var automaticRows=create.Where(row=>!manuallyVerified.Contains(row)).ToList();
        ManualVerifications.Save(db,tr,manualRows);
        ManualVerifications.Clear(db,tr,automaticRows);
        manuallyVerifiedCount=manualRows.Count;

        tr.Commit();return create.Count;
    }
}
