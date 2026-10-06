using System.Reflection;
using System.Diagnostics;
using System.Windows.Forms;
using CadSyncFindAndReplaceBlock;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Host=Autodesk.AutoCAD.ApplicationServices.Core.Application;
using DialogHost=Autodesk.AutoCAD.ApplicationServices.Application;
internal static class Tests
{
    static int Passed;
    internal static T Field<T>(object x,string name)=>(T)x.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(x)!;
    static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Pump(){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<120){System.Windows.Forms.Application.DoEvents();Thread.Sleep(2);}}
    static void Check(string name,Action action){action();Passed++;Console.WriteLine("PASS "+name);}
    static bool Enabled(FindAndReplaceControl c)=>Field<Button>(c,"_changeScale").Enabled&&Field<Button>(c,"_changeStandard").Enabled;
    static void Grid(FindAndReplaceControl c,string name)
    {
        var grid=Field<DataGridView>(c,"_blocks");grid.ClearSelection();
        foreach(DataGridViewRow row in grid.Rows)
            if(((BlockNameSummary)row.DataBoundItem).Name==name){grid.CurrentCell=row.Cells[0];row.Selected=true;return;}
        throw new Exception("Missing row "+name);
    }
    static void Fresh(Action<Document,FindAndReplaceControl,ObjectId[]> action)
    {
        var doc=new Document();
        var ids=new[]{doc.Database.Add(1,"BALL VALVE"),doc.Database.Add(2,"PLUG VALVE"),doc.Database.Add(3,"BALL VALVE"),doc.Database.Add(4,"GATE VALVE")};
        Host.DocumentManager.MdiActiveDocument=doc;
        DialogHost.OnDialog=null;DialogHost.NextResult=DialogResult.OK;DialogHost.Target="GATE VALVE";
        BlockReplacementEngine.Calls=0;BlockReplacementEngine.Commit=false;BlockReplacementEngine.Throw=false;BlockReplacementEngine.LastIds=Array.Empty<ObjectId>();
        using(var form=new Form())using(var control=new FindAndReplaceControl())
        {
            form.Controls.Add(control);form.Show();control.OpenFor(doc);Pump();action(doc,control,ids);
        }
        Assert(doc.SelectionSubscribers==0,"Selection handler leaked after dispose");
    }
    [STAThread] static int Main()
    {
        try
        {
            Check("selection handler attached immediately on OpenFor",()=>Fresh((d,c,x)=>Assert(d.SelectionSubscribers==1,"Not attached")));
            Check("manual single activates both buttons without grid row",()=>Fresh((d,c,x)=>{d.Editor.SetImpliedSelection(new[]{x[0]});Pump();Assert(Enabled(c),"disabled");}));
            Check("manual mixed activates both buttons without grid row",()=>Fresh((d,c,x)=>{d.Editor.SetImpliedSelection(new[]{x[0],x[1]});Pump();Assert(Enabled(c),"disabled");}));
            Check("deselect disables both without stale row fallback",()=>Fresh((d,c,x)=>{Grid(c,"BALL VALVE");Pump();d.Editor.SetImpliedSelection(Array.Empty<ObjectId>());Pump();Assert(!Enabled(c),"still enabled");c.ExecuteMode(ReplacementMode.Scale);Assert(BlockReplacementEngine.Calls==0,"Used old grid");}));
            Check("working grid selects its entire type",()=>Fresh((d,c,x)=>{Grid(c,"BALL VALVE");Pump();Assert(d.Editor.Ids.ToHashSet().SetEquals(new[]{x[0],x[2]}),"Grid mismatch");c.ExecuteMode(ReplacementMode.Scale);Assert(BlockReplacementEngine.LastIds.ToHashSet().SetEquals(new[]{x[0],x[2]}),"Engine source mismatch");}));
            Check("manual selection overrides populated grid row",()=>Fresh((d,c,x)=>{Grid(c,"BALL VALVE");Pump();d.Editor.SetImpliedSelection(new[]{x[1]});Pump();c.ExecuteMode(ReplacementMode.Standard);Assert(BlockReplacementEngine.LastIds.SequenceEqual(new[]{x[1]}),"Used stale grid");}));
            Check("manual selection cancels queued grid timer",()=>Fresh((d,c,x)=>{Grid(c,"BALL VALVE");d.Editor.SetImpliedSelection(new[]{x[1]});Pump();Assert(d.Editor.Ids.SequenceEqual(new[]{x[1]}),"Queued grid overwrote picks");}));
            Check("click-time guard catches missed selection event",()=>Fresh((d,c,x)=>{Grid(c,"BALL VALVE");Pump();d.Editor.SetWithoutEvent(new[]{x[1]});c.ExecuteMode(ReplacementMode.Scale);Assert(BlockReplacementEngine.LastIds.SequenceEqual(new[]{x[1]}),"Stale row won");}));
            Check("pending grid guard catches missed drawing event",()=>Fresh((d,c,x)=>{Grid(c,"BALL VALVE");d.Editor.SetWithoutEvent(new[]{x[1]});c.ExecuteMode(ReplacementMode.Scale);Assert(BlockReplacementEngine.LastIds.SequenceEqual(new[]{x[1]}),"Pending row won");}));
            Check("mixed picks only, not all matching instances",()=>Fresh((d,c,x)=>{d.Editor.SetImpliedSelection(new[]{x[0],x[1]});Pump();c.ExecuteMode(ReplacementMode.Scale);Assert(BlockReplacementEngine.LastIds.SequenceEqual(new[]{x[0],x[1]}),"Selection expanded");}));
            Check("manual picks ignore finder layer and name filter",()=>Fresh((d,c,x)=>{Field<TextBox>(c,"_find").Text="missing";d.Editor.SetImpliedSelection(new[]{x[0],x[1]});Pump();Assert(Enabled(c),"disabled by filter");c.ExecuteMode(ReplacementMode.Standard);Assert(BlockReplacementEngine.LastIds.Length==2,"filtered source");}));
            Check("non-blocks excluded from source and restored on cancel",()=>Fresh((d,c,x)=>{var text=d.Database.AddText(77);d.Editor.SetImpliedSelection(new[]{text,x[1]});Pump();c.ExecuteMode(ReplacementMode.Scale);Assert(BlockReplacementEngine.LastIds.SequenceEqual(new[]{x[1]}),"text passed to engine");Assert(d.Editor.Ids.Contains(text),"text lost from selection");}));
            Check("source snapshot fixed while target dialog changes picks",()=>Fresh((d,c,x)=>{d.Editor.SetImpliedSelection(new[]{x[1]});Pump();DialogHost.OnDialog=_=>{d.Editor.SetImpliedSelection(new[]{x[0],x[2]});Pump();};c.ExecuteMode(ReplacementMode.Standard);Assert(BlockReplacementEngine.LastIds.SequenceEqual(new[]{x[1]}),"snapshot mutated");Assert(d.Editor.Ids.SequenceEqual(new[]{x[1]}),"cancel selection not restored");}));
            Check("target cancel performs no replacement",()=>Fresh((d,c,x)=>{d.Editor.SetImpliedSelection(new[]{x[1]});Pump();DialogHost.NextResult=DialogResult.Cancel;c.ExecuteMode(ReplacementMode.Standard);Assert(BlockReplacementEngine.Calls==0,"engine invoked");Assert(Enabled(c),"buttons stayed disabled");}));
            Check("engine failure restores selection and permits retry",()=>Fresh((d,c,x)=>{d.Editor.SetImpliedSelection(new[]{x[1]});Pump();BlockReplacementEngine.Throw=true;c.ExecuteMode(ReplacementMode.Scale);Assert(d.Editor.Ids.SequenceEqual(new[]{x[1]})&&Enabled(c),"failed recovery");}));
            Check("commit selects replacements, not old grid",()=>Fresh((d,c,x)=>{d.Editor.SetImpliedSelection(new[]{x[1]});Pump();BlockReplacementEngine.Commit=true;c.ExecuteMode(ReplacementMode.Standard);Assert(d.Editor.Ids.Length==1&&d.Editor.Ids[0].Value==1002,"wrong postcommit picks");Assert(Enabled(c),"buttons disabled");}));
            Check("one-row finder does not imply replacement of all",()=>Fresh((d,c,x)=>{Field<TextBox>(c,"_find").Text="PLUG";c.ExecuteMode(ReplacementMode.Scale);Assert(BlockReplacementEngine.Calls==0,"autoselected sole row");}));
            Check("drawing switch detaches old and attaches new selection",()=>Fresh((d,c,x)=>{var other=new Document();var id=other.Database.Add(8,"CAP");Host.DocumentManager.Activate(other);other.Editor.SetImpliedSelection(new[]{id});Pump();Assert(d.SelectionSubscribers==0&&other.SelectionSubscribers==1&&Enabled(c),"doc binding mismatch");Host.DocumentManager.Destroy(other);Assert(other.SelectionSubscribers==0&&!Enabled(c),"destroy leak");}));
            Check("reopen does not duplicate subscriptions",()=>Fresh((d,c,x)=>{c.OpenFor(d);c.OpenFor(d);Assert(d.SelectionSubscribers==1,"duplicate handlers");}));
            Check("old pending row cannot write to new drawing",()=>Fresh((d,c,x)=>{Grid(c,"BALL VALVE");var other=new Document();var id=other.Database.Add(8,"CAP");other.Editor.SetImpliedSelection(new[]{id});Host.DocumentManager.Activate(other);Pump();Assert(other.Editor.Ids.SequenceEqual(new[]{id}),"cross-document overwrite");Host.DocumentManager.Destroy(other);}));
            Check("no match then valid match recovers safely",()=>{
                using var f=new TargetBlockDialog("CAP",new[]{"BALL VALVE","CHECK BALL VALVE","BASKETBALL"});f.Show();Pump();
                var input=Field<TextBox>(f,"_target");var list=Field<ListBox>(f,"_suggestions");var ok=Field<Button>(f,"_ok");
                input.Text="zzzz";Assert(list.Items.Count==0&&!ok.Enabled,"no-match state");
                input.Text="ball";Assert(list.Items.Count==3,"contains results");
                list.SelectedIndex=1;Assert(list.Items.Count==3&&ok.Enabled,"selection rebuilt list");
                list.SelectedIndex=2;Assert(ok.Enabled,"second suggestion failed");
                input.Text="absent";Assert(list.Items.Count==0&&!ok.Enabled,"empty after selection");f.Close();
            });
            Check("rapid typing and backspace never index empty list",()=>{
                using var f=new TargetBlockDialog("CAP",new[]{"BALL VALVE","CHECK BALL VALVE","BASKETBALL"});f.Show();
                var input=Field<TextBox>(f,"_target");for(int i=0;i<50;i++)foreach(var s in new[]{"b","ba","ball","balls-not-here",""," "})input.Text=s;
                input.Text="BALL VALVE";Assert(Field<Button>(f,"_ok").Enabled,"exact name disabled");f.Close();
            });
            Console.WriteLine($"{Passed} regression scenarios passed. Host shims used; no native AutoCAD/DWG geometry was executed.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
