using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System.Text;
using System.Globalization;
namespace CadSyncGrouping;

internal static class LeaderEvidence
{
    internal static IEnumerable<(ObjectId Id,Point3d Arrow,Point3d Tail,ObjectId Annotation)> Paths(Entity e)
    {
        if(e is Leader l && l.NumVertices>=2 && l.HasArrowHead)
            yield return (l.ObjectId,l.VertexAt(0),l.VertexAt(l.NumVertices-1),l.Annotation);
        // Separate DBText/MText annotations only. Embedded MLeader content stays
        // outside the original text scope; shared/multiple paths are never grouped.
        if(e is MLeader m)
            foreach(int leader in m.GetLeaderIndexes())foreach(int line in m.GetLeaderLineIndexes(leader))
                yield return (m.ObjectId,m.GetFirstVertex(line),m.GetLastVertex(line),ObjectId.Null);
    }
    internal static void Apply(ScanResult scan,List<Entity> texts,List<Entity> leaders,
        List<BlockReference> valves,Dictionary<ObjectId,Extents2d> boxes,Transaction tr,StringBuilder signature)
    {
        double Distance(Point3d p,Extents2d b){var x=Math.Max(b.MinX-p.X,Math.Max(0,p.X-b.MaxX));var y=Math.Max(b.MinY-p.Y,Math.Max(0,p.Y-b.MaxY));return Math.Sqrt(x*x+y*y);}
        var links=new List<(ObjectId Text,ObjectId Block,ObjectId Leader)>();
        var ambiguous=new HashSet<ObjectId>();
        foreach(var leader in leaders)
        {
            var paths=Paths(leader).ToList();
            foreach(var path in paths)
            {
                signature.Append("LEADER|").Append(path.Id.Handle).Append('|').Append(DrawingScanner.UsableLayer(leader,tr));
                foreach(var v in new[]{path.Arrow.X,path.Arrow.Y,path.Arrow.Z,path.Tail.X,path.Tail.Y,path.Tail.Z})signature.Append('|').Append(v.ToString("R",CultureInfo.InvariantCulture));
                signature.AppendLine();
                if(!DrawingScanner.UsableLayer(leader,tr))continue;
                var labels=texts.Where(t=>DrawingScanner.IsLocationId(DrawingScanner.TextOf(t))&&DrawingScanner.UsableLayer(t,tr))
                    .Select(t=>(Text:t,Has:GeometryHelper.TryGetExtents(t,out var b),Box:b))
                    .Where(t=>t.Has && (path.Annotation==t.Text.ObjectId || Distance(path.Tail,t.Box)<=Math.Min(t.Box.Width,t.Box.Height)*.8)).ToList();
                if(labels.Count!=1){foreach(var candidate in labels)ambiguous.Add(candidate.Text.ObjectId);continue;}
                var label=labels[0];var size=Math.Min(label.Box.Width,label.Box.Height);
                if(paths.Count!=1){ambiguous.Add(label.Text.ObjectId);continue;}
                var hits=valves.Where(v=>boxes.ContainsKey(v.ObjectId)&&DrawingScanner.UsableLayer(v,tr)&&Distance(path.Arrow,boxes[v.ObjectId])<=Math.Min(size*.35,boxes[v.ObjectId].Diagonal*.15)).ToList();
                if(hits.Count==1)links.Add((label.Text.ObjectId,hits[0].ObjectId,path.Id));
                else if(hits.Count>1)
                {
                    var row=scan.Candidates.Single(c=>c.TextId==label.Text.ObjectId);
                    row.Status=CandidateStatus.Ambiguous;row.Selected=false;row.Reason="Native leader endpoint overlaps multiple equipment candidates; review required.";
                    ambiguous.Add(row.TextId);
                }
            }
        }
        foreach(var group in links.GroupBy(l=>l.Text))
        {
            var row=scan.Candidates.Single(c=>c.TextId==group.Key);
            if(ambiguous.Contains(row.TextId))continue;
            var unique=group.Select(l=>l.Block).Distinct().ToList();
            if(unique.Count!=1 || group.Any(l=>links.Any(other=>other.Leader==l.Leader&&other.Text!=l.Text)))
            {row.Status=CandidateStatus.Ambiguous;row.Selected=false;row.Reason="Shared or conflicting native leader paths; review required.";continue;}
            var block=unique[0];
            if(row.Status==CandidateStatus.Ready && row.ProposedBlockId!=block)
            {row.Status=CandidateStatus.Ambiguous;row.Selected=false;row.Reason="Native leader evidence disagrees with a strong proximity result; review required.";continue;}
            row.ProposedBlockId=block;row.ProposedBlockName=DrawingScanner.GetEffectiveBlockName(block,tr);
            row.CandidateBlockIds=new[]{block}.Concat(row.CandidateBlockIds).Distinct().Take(4).ToList();
            row.LeaderIds=group.Select(l=>l.Leader).Distinct().ToList();
            row.NearestDistance=0;row.Status=scan.UnusableValveGeometry==0?CandidateStatus.Ready:CandidateStatus.Ambiguous;row.Selected=scan.UnusableValveGeometry==0;
            row.Reason="Unique native leader connects this Location ID to the proposed equipment.";
            if(scan.UnusableValveGeometry>0)row.Reason+=" Other valve geometry is unavailable; review required.";
        }
        foreach(var row in scan.Candidates.Where(c=>ambiguous.Contains(c.TextId)))
        {row.Status=CandidateStatus.Ambiguous;row.Selected=false;row.LeaderIds.Clear();row.Reason="Shared or ambiguous native leader geometry; review required.";}
    }
}
