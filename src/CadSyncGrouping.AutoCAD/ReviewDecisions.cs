using Autodesk.AutoCAD.DatabaseServices;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace CadSyncGrouping;

internal static class ReviewDecisions
{
    const string DictionaryName="CAD_SYNC_GROUPING_REVIEW_V1";
    internal static string Fingerprint(GroupingCandidate row,Transaction tr)
    {
        var b=new StringBuilder("v1|").Append(row.LocationId).Append('|').Append(row.ProposedBlockName).Append('|')
            .Append(row.Equipment.Type).Append('|').Append(row.Reason).Append('|').Append(row.ExistingMembership);
        foreach(var id in new[]{row.TextId,row.ProposedBlockId}.Concat(row.CandidateBlockIds).Concat(row.LeaderIds).Distinct())
        {
            if(id.IsNull||!id.IsValid||id.IsErased)continue;
            var e=(Entity)tr.GetObject(id,OpenMode.ForRead);
            b.Append('|').Append(id.Handle).Append('|').Append(e.Layer).Append('|').Append(DrawingScanner.UsableLayer(e,tr));
            if(e is BlockReference br)
            {
                foreach(var value in br.BlockTransform.ToArray())b.Append('|').Append(value.ToString("R",CultureInfo.InvariantCulture));
                b.Append('|').Append(br.BlockTableRecord.Handle);
                var definition=(BlockTableRecord)tr.GetObject(br.BlockTableRecord,OpenMode.ForRead);
                foreach(ObjectId childId in definition)
                {
                    if(tr.GetObject(childId,OpenMode.ForRead) is not Entity child)continue;
                    b.Append('|').Append(child.Handle).Append('|').Append(child.GetType().Name).Append('|').Append(child.Visible);
                    if(GeometryHelper.TryGetExtents(child,out var bounds))foreach(var n in new[]{bounds.MinX,bounds.MinY,bounds.MaxX,bounds.MaxY})b.Append('|').Append(n.ToString("R",CultureInfo.InvariantCulture));
                    if(child is Polyline poly)for(int i=0;i<poly.NumberOfVertices;i++)
                    {var point=poly.GetPoint3dAt(i);foreach(var n in new[]{point.X,point.Y,point.Z,poly.GetBulgeAt(i)})b.Append('|').Append(n.ToString("R",CultureInfo.InvariantCulture));}
                }
            }
            if(GeometryHelper.TryGetExtents(e,out var box))foreach(var v in new[]{box.MinX,box.MinY,box.MaxX,box.MaxY})b.Append('|').Append(v.ToString("R",CultureInfo.InvariantCulture));
        }
        var groups=GroupMembership.Read(row.TextId.Database,tr);
        foreach(var members in groups.Groups.Where(g=>g.Contains(row.TextId)||g.Contains(row.ProposedBlockId))
            .Select(g=>string.Join(",",g.Select(id=>id.Handle.ToString()).OrderBy(s=>s,StringComparer.Ordinal))).OrderBy(s=>s,StringComparer.Ordinal))
            b.Append('|').Append(members);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(b.ToString())));
    }
    internal static void Apply(Database db,Transaction tr,IEnumerable<GroupingCandidate> rows)
    {
        var nod=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForRead);
        DBDictionary? dict=nod.Contains(DictionaryName)?(DBDictionary)tr.GetObject(nod.GetAt(DictionaryName),OpenMode.ForRead):null;
        foreach(var row in rows)
        {
            if(row.Status==CandidateStatus.Existing)continue;
            row.ReviewFingerprint=Fingerprint(row,tr);
            var key=row.TextId.Handle.ToString();

            string? savedDecision=null;
            if(dict!=null&&dict.Contains(key))
            {
                using var data=((Xrecord)tr.GetObject(dict.GetAt(key),OpenMode.ForRead)).Data;
                var values=data?.AsArray();
                if(values!=null && values.Length>1 && values[0].Value as string == row.ReviewFingerprint)
                    savedDecision=values[1].Value as string;
            }

            if(savedDecision=="Ignored/v1")
            {
                row.Status=CandidateStatus.Accepted;
                row.Selected=false;
                continue;
            }

            // Group-membership conflicts are noise by default: the scanner has
            // already protected the existing group, so place them in Ignored
            // unless the user explicitly reopens this exact relationship.
            if(row.ExistingMembership && row.Status==CandidateStatus.Conflict)
            {
                if(savedDecision!="ReopenedGroupConflict/v1")
                {
                    row.Status=CandidateStatus.Accepted;
                    row.Selected=false;
                }
            }
        }
    }
    internal static void SaveBatch(Database db,IReadOnlyList<GroupingCandidate> rows,bool accept)
    {
        using var tr=db.TransactionManager.StartTransaction();
        var nod=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForRead);
        DBDictionary dict;
        var needsReopenOverride=!accept && rows.Any(row=>row.ExistingMembership);
        if(!nod.Contains(DictionaryName))
        {
            if(!accept && !needsReopenOverride)return;
            nod.UpgradeOpen();dict=new DBDictionary();nod.SetAt(DictionaryName,dict);tr.AddNewlyCreatedDBObject(dict,true);
        }
        else dict=(DBDictionary)tr.GetObject(nod.GetAt(DictionaryName),OpenMode.ForWrite);

        foreach(var row in rows)
        {
            var key=row.TextId.Handle.ToString();
            if(dict.Contains(key)){var old=tr.GetObject(dict.GetAt(key),OpenMode.ForWrite);dict.Remove(key);old.Erase();}

            // Reopening an automatically ignored group-membership conflict must
            // survive the next rescan. Store a fingerprint-specific override so
            // it returns to Conflict until the underlying relationship changes.
            var decision=accept ? "Ignored/v1" : row.ExistingMembership ? "ReopenedGroupConflict/v1" : null;
            if(decision==null)continue;

            var record=new Xrecord();dict.SetAt(key,record);tr.AddNewlyCreatedDBObject(record,true);
            using var data=new ResultBuffer(new TypedValue(1,Fingerprint(row,tr)),new TypedValue(1,decision),
                new TypedValue(1,row.LocationId),new TypedValue(1,row.TextId.Handle.ToString()),new TypedValue(1,row.ProposedBlockId.IsNull?"":row.ProposedBlockId.Handle.ToString()),
                new TypedValue(1,row.ProposedBlockName),new TypedValue(1,row.Equipment.Type),new TypedValue(1,row.Reason),new TypedValue(1,DateTime.UtcNow.ToString("O")));
            record.Data=data;
        }
        tr.Commit();
    }
}
