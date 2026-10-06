// Test host shims, NOT native DWG/geometry validation.
using System.Collections;
using System.Windows.Forms;
namespace Autodesk.AutoCAD.Geometry
{
    public struct Point2d { public Point2d(double x,double y){} }
    public struct Point3d { public double X,Y,Z; }
}
namespace Autodesk.AutoCAD.DatabaseServices
{
    using Autodesk.AutoCAD.Geometry;
    public struct Extents3d { public Point3d MinPoint,MaxPoint; public void AddExtents(Extents3d e){} }
    public enum OpenMode { ForRead, ForWrite }
    public readonly record struct ObjectId(int Value, Database Database)
    {
        public bool IsNull => Value == 0;
        public bool IsValid => !IsNull && Database != null && Database.Objects.ContainsKey(Value);
        public bool IsErased => !IsValid;
        public int Handle => Value;
    }
    public class Entity { public virtual Extents3d GeometricExtents => new(); }
    public class BlockReference : Entity
    {
        public ObjectId OwnerId, BlockTableRecord, DynamicBlockTableRecord;
        public bool IsDynamicBlock;
        public string Layer="Valve";
        public (double X,double Y,double Z) ScaleFactors=(1,1,1);
    }
    public class BlockTableRecord : IEnumerable<ObjectId>
    {
        public string Name=""; public bool IsLayout,IsFromExternalReference,IsFromOverlayReference,IsAnonymous;
        public List<ObjectId> Ids=new();
        public IEnumerator<ObjectId> GetEnumerator()=>Ids.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    public class BlockTable : List<ObjectId> {}
    public class Database
    {
        private static int Next;
        public IntPtr UnmanagedObject {get;}=new(++Next);
        public Dictionary<int,object> Objects=new();
        public ObjectId BlockTableId => new(-1,this);
        public ObjectId ModelId => new(-2,this);
        public TransactionManager TransactionManager {get;}
        public Database()
        {
            Objects[-1]=new BlockTable(); Objects[-2]=new BlockTableRecord{Name="*Model_Space"};
            TransactionManager=new(this);
        }
        public ObjectId Add(int id,string name,string layer="Valve",bool dynamic=false)
        {
            int defid=10000+id;
            var definition=new ObjectId(defid,this);
            Objects[defid]=new BlockTableRecord{Name=name};
            ((BlockTable)Objects[-1]).Add(definition);
            var oid=new ObjectId(id,this);
            Objects[id]=new BlockReference{OwnerId=ModelId,BlockTableRecord=definition,DynamicBlockTableRecord=definition,IsDynamicBlock=dynamic,Layer=layer};
            ((BlockTableRecord)Objects[-2]).Ids.Add(oid);
            return oid;
        }
        public ObjectId AddText(int id){Objects[id]=new Entity();return new(id,this);}
    }
    public static class SymbolUtilityServices {public static ObjectId GetBlockModelSpaceId(Database db)=>db.ModelId;}
    public class TransactionManager(Database db) {public Transaction StartTransaction()=>new(db);}
    public class Transaction(Database db):IDisposable
    {
        public object GetObject(ObjectId id,OpenMode mode)=>db.Objects[id.Value];
        public void Commit(){} public void Dispose(){}
    }
    public class ViewTableRecord:IDisposable
    {
        public Point2d CenterPoint;public double Width=100,Height=100;public void Dispose(){}
    }
}
namespace Autodesk.AutoCAD.EditorInput
{
    using Autodesk.AutoCAD.DatabaseServices;
    using Autodesk.AutoCAD.ApplicationServices;
    public enum PromptStatus {OK,Error}
    public class SelectionSet(ObjectId[] ids){public int Count=>ids.Length;public ObjectId[] GetObjectIds()=>ids.ToArray();}
    public class PromptSelectionResult {public PromptStatus Status;public SelectionSet? Value;}
    public class Editor(Document doc)
    {
        public ObjectId[] Ids=Array.Empty<ObjectId>();public int Writes;public List<string> Messages=new();
        public PromptSelectionResult SelectImplied()=>new(){Status=Ids.Length==0?PromptStatus.Error:PromptStatus.OK,Value=new(Ids)};
        public void SetImpliedSelection(ObjectId[] ids){Ids=ids.ToArray();Writes++;doc.RaiseSelection();}
        public void SetWithoutEvent(ObjectId[] ids){Ids=ids.ToArray();}
        public void WriteMessage(string s)=>Messages.Add(s);
        public void Regen(){}public ViewTableRecord GetCurrentView()=>new();public void SetCurrentView(ViewTableRecord v){}
    }
}
namespace Autodesk.AutoCAD.ApplicationServices
{
    using Autodesk.AutoCAD.DatabaseServices;
    using Autodesk.AutoCAD.EditorInput;
    public class Document
    {
        public Database Database=new();public Editor Editor;public Document(){Editor=new(this);}
        private event EventHandler? Changed;
        public int SelectionSubscribers;
        public event EventHandler ImpliedSelectionChanged {add{Changed+=value;SelectionSubscribers++;}remove{Changed-=value;SelectionSubscribers--;}}
        public void RaiseSelection()=>Changed?.Invoke(this,EventArgs.Empty);
        private class DocLock:IDisposable {public void Dispose(){}}
        public IDisposable LockDocument()=>new DocLock();
    }
    public class DocumentCollectionEventArgs(Document doc):EventArgs {public Document Document=>doc;}
    public class DocumentCollection
    {
        public Document? MdiActiveDocument;
        public event EventHandler<DocumentCollectionEventArgs>? DocumentActivated,DocumentToBeDestroyed;
        public void Activate(Document d){MdiActiveDocument=d;DocumentActivated?.Invoke(this,new(d));}
        public void Destroy(Document d){DocumentToBeDestroyed?.Invoke(this,new(d));if(ReferenceEquals(d,MdiActiveDocument))MdiActiveDocument=null;}
    }
    public static class Application
    {
        public static Action<Form>? OnDialog;
        public static DialogResult NextResult=DialogResult.OK;
        public static string Target="GATE VALVE";
        public static DialogResult ShowModalDialog(IWin32Window owner,Form dialog)
        {
            OnDialog?.Invoke(dialog);
            Tests.Field<TextBox>(dialog,"_target").Text=Target;
            return NextResult;
        }
    }
}
namespace Autodesk.AutoCAD.ApplicationServices.Core
{
    public static class Application
    {
        public static Autodesk.AutoCAD.ApplicationServices.DocumentCollection DocumentManager=new();
        public static void UpdateScreen(){}
    }
}
namespace CadSyncFindAndReplaceBlock
{
    using Autodesk.AutoCAD.ApplicationServices;using Autodesk.AutoCAD.DatabaseServices;
    internal enum ReplacementMode {Scale,Standard}
    internal sealed class ReplacementResult
    {
        public bool Committed {get;init;}
        public IReadOnlyList<ObjectId> SourceIds {get;init;}=Array.Empty<ObjectId>();
        public IReadOnlyList<ObjectId> ReplacementIds {get;init;}=Array.Empty<ObjectId>();
    }
    internal static class BlockReplacementEngine
    {
        public static ObjectId[] LastIds=Array.Empty<ObjectId>();public static ReplacementMode LastMode;
        public static int Calls;public static bool Commit;public static bool Throw;
        public static ReplacementResult Replace(Document doc,IReadOnlyCollection<ObjectId> ids,string name,ReplacementMode mode)
        {
            Calls++;LastIds=ids.ToArray();LastMode=mode;
            if(Throw)throw new InvalidOperationException("Test failure before commit");
            if(!Commit)return new();
            var replacements=new List<ObjectId>();
            foreach(var id in ids){doc.Database.Objects.Remove(id.Value);replacements.Add(doc.Database.Add(id.Value+1000,name));}
            var model=(BlockTableRecord)doc.Database.Objects[-2];model.Ids.RemoveAll(id=>!id.IsValid);
            return new(){Committed=true,SourceIds=ids.ToArray(),ReplacementIds=replacements};
        }
    }
}
