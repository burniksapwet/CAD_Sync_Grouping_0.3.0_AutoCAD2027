using Autodesk.AutoCAD.DatabaseServices;

namespace CadSyncGrouping;

internal static class ManualVerifications
{
    private const string DictionaryName="CAD_SYNC_GROUPING_MANUAL_VERIFIED_V1";
    private const string Decision="ManuallyVerified/v1";

    internal static bool Matches(Database db,Transaction tr,GroupingCandidate row)
    {
        if(row.TextId.IsNull||row.ProposedBlockId.IsNull||!row.TextId.IsValid||!row.ProposedBlockId.IsValid)
            return false;

        var nod=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForRead);
        if(!nod.Contains(DictionaryName))return false;
        var dict=(DBDictionary)tr.GetObject(nod.GetAt(DictionaryName),OpenMode.ForRead);
        var key=row.TextId.Handle.ToString();
        if(!dict.Contains(key))return false;

        using var data=((Xrecord)tr.GetObject(dict.GetAt(key),OpenMode.ForRead)).Data;
        var values=data?.AsArray();
        if(values==null||values.Length<5)return false;

        return values[0].Value as string==Decision
            && values[1].Value as string==row.ProposedBlockId.Handle.ToString()
            && values[2].Value as string==row.LocationId
            && string.Equals(values[3].Value as string,row.ProposedBlockName,StringComparison.OrdinalIgnoreCase);
    }

    internal static void Save(Database db,Transaction tr,IReadOnlyCollection<GroupingCandidate> rows)
    {
        if(rows.Count==0)return;
        var dict=GetOrCreateDictionary(db,tr);

        foreach(var row in rows)
        {
            if(row.TextId.IsNull||row.ProposedBlockId.IsNull)continue;
            var key=row.TextId.Handle.ToString();
            RemoveExisting(dict,tr,key);

            var record=new Xrecord();
            dict.SetAt(key,record);
            tr.AddNewlyCreatedDBObject(record,true);
            using var data=new ResultBuffer(
                new TypedValue(1,Decision),
                new TypedValue(1,row.ProposedBlockId.Handle.ToString()),
                new TypedValue(1,row.LocationId),
                new TypedValue(1,row.ProposedBlockName),
                new TypedValue(1,row.StatusText),
                new TypedValue(1,row.Reason),
                new TypedValue(1,DateTime.UtcNow.ToString("O")));
            record.Data=data;
        }
    }

    internal static void Clear(Database db,Transaction tr,IReadOnlyCollection<GroupingCandidate> rows)
    {
        if(rows.Count==0)return;
        var nod=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForRead);
        if(!nod.Contains(DictionaryName))return;
        var dict=(DBDictionary)tr.GetObject(nod.GetAt(DictionaryName),OpenMode.ForWrite);
        foreach(var row in rows)
        {
            if(row.TextId.IsNull)continue;
            RemoveExisting(dict,tr,row.TextId.Handle.ToString());
        }
    }

    private static DBDictionary GetOrCreateDictionary(Database db,Transaction tr)
    {
        var nod=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForRead);
        if(nod.Contains(DictionaryName))
            return (DBDictionary)tr.GetObject(nod.GetAt(DictionaryName),OpenMode.ForWrite);

        nod.UpgradeOpen();
        var dict=new DBDictionary();
        nod.SetAt(DictionaryName,dict);
        tr.AddNewlyCreatedDBObject(dict,true);
        return dict;
    }

    private static void RemoveExisting(DBDictionary dict,Transaction tr,string key)
    {
        if(!dict.Contains(key))return;
        var old=tr.GetObject(dict.GetAt(key),OpenMode.ForWrite);
        dict.Remove(key);
        old.Erase();
    }
}
