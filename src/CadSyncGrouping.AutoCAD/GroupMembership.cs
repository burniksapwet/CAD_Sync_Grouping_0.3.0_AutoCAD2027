using Autodesk.AutoCAD.DatabaseServices;
namespace CadSyncGrouping;
internal sealed class GroupMembership
{
    public List<HashSet<ObjectId>> Groups { get; } = new();
    public bool Contains(ObjectId id) => !id.IsNull && Groups.Any(g=>g.Contains(id));
    public bool SameGroup(ObjectId a,ObjectId b) => !a.IsNull&&!b.IsNull&&Groups.Any(g=>g.Contains(a)&&g.Contains(b));
    public static GroupMembership Read(Database db,Transaction tr)
    {
        var index=new GroupMembership();
        var dictionary=(DBDictionary)tr.GetObject(db.GroupDictionaryId,OpenMode.ForRead);
        foreach(DBDictionaryEntry entry in dictionary)
            if(tr.GetObject(entry.Value,OpenMode.ForRead) is Autodesk.AutoCAD.DatabaseServices.Group group)
                index.Groups.Add(group.GetAllEntityIds().ToHashSet());
        return index;
    }
}
