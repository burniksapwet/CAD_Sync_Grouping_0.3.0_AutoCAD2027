using Autodesk.AutoCAD.DatabaseServices;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CadSyncGrouping;

public static class DrawingScanner
{
    internal static readonly HashSet<string> LocationLayers = new(StringComparer.OrdinalIgnoreCase) { "Text-Legend", "Text-Legend 2" };
    internal static bool IsLocationId(string text) => Regex.IsMatch(text, @"^(?=.*[A-Za-z])(?=.*[0-9])[A-Za-z0-9][A-Za-z0-9_-]{1,99}$");
    internal static string TextOf(Entity entity) => (entity is DBText t ? t.TextString : entity is MText m ? m.Text : "").Trim();
    internal static bool UsableLayer(Entity e, Transaction tr) => e.Visible && tr.GetObject(e.LayerId, OpenMode.ForRead) is LayerTableRecord l && !l.IsOff && !l.IsFrozen && !l.IsLocked;

    public static ScanResult Scan(Database db, bool protectExisting = true)
    {
        var result = new ScanResult { Database = db };
        using var tr = db.TransactionManager.StartTransaction();
        var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        var texts = new List<Entity>();
        var valves = new List<BlockReference>();
        var leaders = new List<Entity>();
        foreach (ObjectId id in model)
        {
            if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity e) continue;
            if (e is DBText or MText && LocationLayers.Contains(e.Layer)) texts.Add(e);
            else if (e is BlockReference br && CandidateScope.Eligible(br,tr)) valves.Add(br);
            else if(e is Leader or MLeader)leaders.Add(e);
        }
        result.LocationTextCount = texts.Count; result.ValveBlockCount = valves.Count;
        var boxes = new Dictionary<ObjectId, Extents2d>();
        var textScales = new Dictionary<ObjectId, double>();
        var signature = new StringBuilder();
        void Describe(Entity e, Extents2d? box)
        {
            signature.Append(e.Handle).Append('|').Append(e.Layer).Append('|').Append(UsableLayer(e,tr)).Append('|');
            if(e is BlockReference br) signature.Append(br.BlockTransform.ToString()).Append('|').Append(GetEffectiveBlockName(br.ObjectId,tr));
            else signature.Append(TextOf(e));
            if(box is Extents2d x) foreach(var n in new[]{x.MinX,x.MinY,x.MaxX,x.MaxY}) signature.Append('|').Append(n.ToString("R",CultureInfo.InvariantCulture));
            signature.AppendLine();
        }
        foreach(var br in valves)
        {
            var definition=(BlockTableRecord)tr.GetObject(br.BlockTableRecord,OpenMode.ForRead);
            if(!definition.IsFromExternalReference && GeometryHelper.TryGetExtents(br,out var box)) boxes[br.ObjectId]=box;
            else result.UnusableValveGeometry++;
            Describe(br,boxes.TryGetValue(br.ObjectId,out var b)?b:null);
        }
        // Proposals below have no access to existing group membership or block names.
        foreach(var text in texts)
        {
            var idText=TextOf(text); var hasBox=GeometryHelper.TryGetExtents(text,out var box);Describe(text,hasBox?box:null);
            var row=new GroupingCandidate{LocationId=idText,TextId=text.ObjectId,EligibleText=IsLocationId(idText),Status=CandidateStatus.NoMatch};
            result.Candidates.Add(row);
            if(!row.EligibleText){row.Status=CandidateStatus.Ambiguous;row.Reason="Text is not one unambiguous alphanumeric Location ID; review the source text.";continue;}
            if(!hasBox){row.Status=CandidateStatus.Ambiguous;row.Reason="Text geometry unavailable. Insertion-point fallback is not used.";continue;}
            textScales[text.ObjectId]=Math.Max(1e-9,Math.Min(box.Width,box.Height));
            var scores=valves.Where(v=>boxes.ContainsKey(v.ObjectId)).Select(v=>(Block:v,Bounds:boxes[v.ObjectId],Distance:GeometryHelper.DistanceLabelToRect(box,boxes[v.ObjectId])))
                .OrderBy(x=>x.Distance).ThenBy(x=>x.Block.Handle.Value).ToList();
            if(scores.Count==0){row.Reason="No usable Valve-layer geometry.";continue;}
            var best=scores[0]; row.ProposedBlockId=best.Block.ObjectId; row.ProposedBlockName=GetEffectiveBlockName(best.Block.ObjectId,tr);
            row.CandidateBlockIds=scores.Take(4).Select(x=>x.Block.ObjectId).ToList();row.NearestDistance=best.Distance;row.SecondDistance=scores.Count>1?scores[1].Distance:double.PositiveInfinity;
            var normalized=best.Distance/best.Bounds.Diagonal;
            var tolerance=1e-8*Math.Max(1,best.Bounds.Diagonal);
            var separated=double.IsPositiveInfinity(row.SecondDistance)||(row.SecondDistance-best.Distance>tolerance && row.SecondDistance>=best.Distance*1.25);
            if(normalized>8){row.Reason="Nearest valve is too far away relative to its transformed graphical size.";continue;}
            row.Status=CandidateStatus.Ambiguous;row.Reason="Geometry is not sufficiently close or separated from alternatives.";
            if(normalized<=1 && separated){row.Status=CandidateStatus.Ready;row.Selected=true;row.Reason="Distinct nearby transformed Valve geometry at the label edge. Review selection before creating groups.";}
            if(result.UnusableValveGeometry>0 || !UsableLayer(text,tr) || !UsableLayer(best.Block,tr))
            {row.Status=CandidateStatus.Ambiguous;row.Selected=false;row.Reason="Missing valve geometry or hidden/frozen/locked source; automatic grouping withheld.";}
        }
        LeaderEvidence.Apply(result,texts,leaders,valves,boxes,tr,signature);
        // A remote weak proposal must not veto a close, clearly separated pair.
        // Resolve only a dominant Ready label; comparable contenders remain conflicts.
        foreach(var group in result.Candidates.Where(c=>!c.ProposedBlockId.IsNull && c.Status!=CandidateStatus.NoMatch).GroupBy(c=>c.ProposedBlockId).Where(g=>g.Count()>1))
        {
            var ordered=group.OrderBy(c=>c.NearestDistance).ToList();var winner=ordered[0];var next=ordered[1];
            var gap=next.NearestDistance-winner.NearestDistance;
            if(winner.Status==CandidateStatus.Ready && next.NearestDistance>=winner.NearestDistance*2 && gap>=textScales.GetValueOrDefault(winner.TextId,double.PositiveInfinity))
            {
                foreach(var row in ordered.Skip(1)){row.Status=CandidateStatus.Ambiguous;row.Selected=false;row.Reason=$"A much closer label ({winner.LocationId}) identifies this valve. Review this label; no fallback valve assigned.";}
            }
            else foreach(var row in group){row.Status=CandidateStatus.Conflict;row.Selected=false;row.Reason="Comparable Location ID proposals compete for this valve. Choose distinct relationships explicitly.";}
        }
        foreach(var group in result.Candidates.Where(c=>c.EligibleText).GroupBy(c=>c.LocationId,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
            foreach(var row in group){row.Status=CandidateStatus.Conflict;row.Selected=false;row.Reason="Location ID appears more than once; review repeated labels.";}
        result.GeometrySignature=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature.ToString())));
        // Equipment identity is descriptive evidence only; it never affects association.
        var equipmentCache=new Dictionary<ObjectId,EquipmentIdentity>();
        foreach(var row in result.Candidates.Where(c=>!c.ProposedBlockId.IsNull))
        {
            if(!equipmentCache.TryGetValue(row.ProposedBlockId,out var identity))
                equipmentCache[row.ProposedBlockId]=identity=EquipmentIdentification.Inspect(row.ProposedBlockId,tr);
            row.Equipment=identity;
        }
        // Existing groups are consulted only AFTER independent geometry proposals.
        if(protectExisting)
        {
            var groups=GroupMembership.Read(db,tr);var valveIds=valves.Select(v=>v.ObjectId).ToHashSet();
            result.ExistingValveGroups=groups.Groups.Count(g=>g.Any(valveIds.Contains));
            foreach(var row in result.Candidates)
            {
                if(!groups.Contains(row.TextId)&&!groups.Contains(row.ProposedBlockId))continue;
                row.ExistingMembership=true;row.Selected=false;
                if(groups.SameGroup(row.TextId,row.ProposedBlockId)){row.Status=CandidateStatus.Existing;row.Reason="Independent geometry agrees with an existing group. Preserved; no new group.";}
                else {row.Status=CandidateStatus.Conflict;row.Reason="An entity already belongs to a group. Preserved; no automatic regrouping.";}
            }
        }
        if(protectExisting)ReviewDecisions.Apply(db,tr,result.Candidates);
        tr.Commit();return result;
    }
    internal static string GetEffectiveBlockName(ObjectId blockId, Transaction tr)
    {
        if (blockId.IsNull)
            return string.Empty;

        if (tr.GetObject(blockId, OpenMode.ForRead, false) is not BlockReference br)
            return string.Empty;

        try
        {
            var recordId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            if (tr.GetObject(recordId, OpenMode.ForRead, false) is BlockTableRecord btr)
                return btr.Name;
        }
        catch
        {
            // Fall back to the referenced record below.
        }

        try
        {
            if (tr.GetObject(br.BlockTableRecord, OpenMode.ForRead, false) is BlockTableRecord btr)
                return btr.Name;
        }
        catch { }

        return "(unknown block)";
    }

}
