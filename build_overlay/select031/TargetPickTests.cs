using System.Windows.Forms;
using System.Drawing;
using CadSyncFindAndReplaceBlock;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Host = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using DialogHost = Autodesk.AutoCAD.ApplicationServices.Application;

internal static partial class Tests
{
    private static void RunTargetPickTests()
    {
        Check("Select Block is left of Change with dialog dimensions retained", () =>
        {
            using var f = new TargetBlockDialog("Sensor", new[] { "Sensor Wash Spray" }, _ => null);
            f.Show(); Pump();
            var pick = Field<Button>(f,"_selectBlock"); var change = Field<Button>(f,"_ok");
            Assert(pick.Text=="Select Block" && pick.Enabled && pick.DialogResult==DialogResult.None,"pick button setup");
            Assert(pick.Parent==change.Parent && pick.Right<=change.Left,"button not left of Change");
            Assert(f.ClientSize==new Size(470,170),"default dialog resized");
            Directory.CreateDirectory("ui_preview");
            using var bitmap=new Bitmap(f.Width,f.Height);
            f.DrawToBitmap(bitmap,new Rectangle(0,0,f.Width,f.Height));
            bitmap.Save("ui_preview/Select_Block_Dialog.png");
        });
        Check("picked name fills target but never confirms automatically", () =>
        {
            using var f=new TargetBlockDialog("CAP",new[]{"BALL VALVE","GATE VALVE"},_=>"gate valve"); f.Show();
            Field<Button>(f,"_selectBlock").PerformClick();
            Assert(f.TargetName=="GATE VALVE" && Field<Button>(f,"_ok").Enabled,"name not populated");
            Assert(f.Visible && f.DialogResult==DialogResult.None,"pick submitted dialog");
        });
        Check("Esc from picking preserves previous target and suggestions", () =>
        {
            using var f=new TargetBlockDialog("CAP",new[]{"BALL VALVE","GATE VALVE"},_=>null); f.Show();
            Field<TextBox>(f,"_target").Text="BALL VALVE";
            var size=f.ClientSize;
            Field<Button>(f,"_selectBlock").PerformClick();
            Assert(f.TargetName=="BALL VALVE" && f.ClientSize==size && Field<Button>(f,"_ok").Enabled,"cancel changed field");
            Assert(f.Visible && f.DialogResult==DialogResult.None,"cancel dismissed dialog");
        });
        Check("empty cancelled pick keeps Change disabled", () =>
        {
            using var f=new TargetBlockDialog("CAP",new[]{"BALL VALVE"},_=>null); f.Show();
            Field<Button>(f,"_selectBlock").PerformClick();
            Assert(f.TargetName=="" && !Field<Button>(f,"_ok").Enabled && Field<Button>(f,"_selectBlock").Enabled,"empty cancel state");
        });
        Check("same-source pick does not substitute a different target", () =>
        {
            using var f=new TargetBlockDialog("CAP",new[]{"CAP","GATE VALVE"},_=>"CAP"); f.Show();
            Field<TextBox>(f,"_target").Text="GATE VALVE";
            Field<Button>(f,"_selectBlock").PerformClick();
            Assert(f.TargetName=="GATE VALVE" && Field<Button>(f,"_ok").Enabled,"unavailable pick changed name");
        });
        Check("failed pick restores controls and allows a successful retry", () =>
        {
            int calls=0;
            using var f=new TargetBlockDialog("CAP",new[]{"BALL VALVE"},_=>++calls==1?throw new InvalidOperationException("test failure"):"BALL VALVE");
            f.Show();var pick=Field<Button>(f,"_selectBlock");pick.PerformClick();
            Assert(pick.Enabled && !Field<Button>(f,"_ok").Enabled,"failed selection left controls disabled");
            pick.PerformClick();Assert(f.TargetName=="BALL VALVE" && Field<Button>(f,"_ok").Enabled,"retry failed");
        });
        Check("repeated picks use only latest target and keep normal name search", () =>
        {
            int calls=0;
            using var f=new TargetBlockDialog("CAP",new[]{"BALL VALVE","GATE VALVE"},_=>++calls==1?"BALL VALVE":"GATE VALVE");f.Show();
            var pick=Field<Button>(f,"_selectBlock");pick.PerformClick();pick.PerformClick();
            Assert(f.TargetName=="GATE VALVE","second pick ignored");
            Field<TextBox>(f,"_target").Text="ball";
            Assert(Field<ListBox>(f,"_suggestions").Items.Count==1,"search after picking broken");
        });
        Check("first returned block wins, not alphabetical or lowest handle", () =>Fresh((d,c,x)=>
            Assert(DrawingTargetPicker.FirstTargetName(d.Database,new[]{x[1],x[0],x[3]})=="PLUG VALVE","selection order changed")));
        Check("nonblocks and invalid IDs are skipped before first block", () =>Fresh((d,c,x)=>
        {
            var text=d.Database.AddText(77);
            Assert(DrawingTargetPicker.FirstTargetName(d.Database,new[]{default(ObjectId),text,new ObjectId(999,d.Database),x[3],x[0]})=="GATE VALVE","not first valid block");
        }));
        Check("xref and overlay references cannot become targets", () =>Fresh((d,c,x)=>
        {
            ((BlockTableRecord)d.Database.Objects[10001]).IsFromExternalReference=true;
            ((BlockTableRecord)d.Database.Objects[10002]).IsFromOverlayReference=true;
            Assert(DrawingTargetPicker.FirstTargetName(d.Database,new[]{x[0],x[1],x[3]})=="GATE VALVE","xref selected");
        }));
        Check("dynamic block resolves named definition rather than anonymous instance", () =>Fresh((d,c,x)=>
        {
            var id=d.Database.Add(9,"DYNAMIC TARGET",dynamic:true);var block=(BlockReference)d.Database.Objects[9];
            d.Database.Objects[20009]=new BlockTableRecord{Name="*U45",IsAnonymous=true};block.BlockTableRecord=new(20009,d.Database);
            Assert(DrawingTargetPicker.FirstTargetName(d.Database,new[]{id})=="DYNAMIC TARGET","anonymous name used");
        }));
        Check("unnamed static and nested block internals are not targets", () =>Fresh((d,c,x)=>
        {
            ((BlockTableRecord)d.Database.Objects[10001]).IsAnonymous=true;
            ((BlockReference)d.Database.Objects[2]).OwnerId=new ObjectId(10003,d.Database);
            Assert(DrawingTargetPicker.FirstTargetName(d.Database,new[]{x[0],x[1],x[3]})=="GATE VALVE","unsupported target chosen");
        }));
        Check("foreign drawing IDs cannot become targets", () =>Fresh((d,c,x)=>
        {
            var other=new Database();var foreign=other.Add(9,"FOREIGN");
            Assert(DrawingTargetPicker.FirstTargetName(d.Database,new[]{foreign,x[0]})=="BALL VALVE","foreign ID accepted");
        }));
        Check("direct pick restores source selection and disposes interaction", () =>Fresh((d,c,x)=>
        {
            d.Editor.SetImpliedSelection(new[]{x[0],x[1]});d.Editor.NextPickIds=new[]{x[3]};
            using var f=new Form();
            Assert(DrawingTargetPicker.Pick(d,f)=="GATE VALVE","wrong target");
            Assert(d.Editor.Ids.SequenceEqual(new[]{x[0],x[1]}) && d.Editor.WasClearAtPick,"source picks not restored/cleared");
            Assert(d.Editor.InteractionDepth==0 && d.Editor.InteractionsStarted==1 && d.Editor.LastPickSet!.Disposed,"resources not disposed");
        }));
        Check("window batch permits multiple and only consumes first block", () =>Fresh((d,c,x)=>
        {
            d.Editor.NextPickIds=new[]{x[1],x[0],x[3]};using var f=new Form();
            Assert(DrawingTargetPicker.Pick(d,f)=="PLUG VALVE","batch target order");
            var o=d.Editor.LastPickOptions!;
            Assert(o.SingleOnly && !o.SinglePickInSpace && !o.AllowSubSelections && !o.RejectObjectsOnLockedLayers,"window/read-only options wrong");
            Assert(d.Editor.LastPickFilter!.Values.Length==1 && (string)d.Editor.LastPickFilter.Values[0].Value! == "INSERT","not block filtered");
        }));
        Check("cancelled editor prompt restores original picks without a target", () =>Fresh((d,c,x)=>
        {
            d.Editor.SetImpliedSelection(new[]{x[1]});d.Editor.CancelNextPick=true;using var f=new Form();
            Assert(DrawingTargetPicker.Pick(d,f)==null && d.Editor.Ids.SequenceEqual(new[]{x[1]}) && d.Editor.InteractionDepth==0,"editor cancel recovery");
        }));
        Check("editor exception restores selection and releases interaction", () =>Fresh((d,c,x)=>
        {
            d.Editor.SetImpliedSelection(new[]{x[1]});d.Editor.ThrowNextPick=true;using var f=new Form();bool threw=false;
            try{DrawingTargetPicker.Pick(d,f);}catch(InvalidOperationException){threw=true;}
            Assert(threw && d.Editor.Ids.SequenceEqual(new[]{x[1]}) && d.Editor.InteractionDepth==0,"exception recovery");
        }));
        Check("drawing switch during pick cannot write into another document", () =>Fresh((d,c,x)=>
        {
            var other=new Document();var keep=other.Database.Add(9,"OTHER");other.Editor.SetImpliedSelection(new[]{keep});
            d.Editor.NextPickIds=new[]{x[3]};d.Editor.OnPick=()=>Host.DocumentManager.Activate(other);using var f=new Form();
            Assert(DrawingTargetPicker.Pick(d,f)==null && other.Editor.Ids.SequenceEqual(new[]{keep}),"cross document mutation");
            Host.DocumentManager.Destroy(other);
        }));
        foreach(var mode in new[]{ReplacementMode.Scale,ReplacementMode.Standard})
            Check($"{mode}: picked target never enters fixed mixed-source snapshot", () =>Fresh((d,c,x)=>
            {
                d.Editor.SetImpliedSelection(new[]{x[0],x[1]});Pump();d.Editor.NextPickIds=new[]{x[3],x[2]};DialogHost.Target=null;
                DialogHost.OnDialog=f=>{f.Show();Field<Button>(f,"_selectBlock").PerformClick();Assert(f.DialogResult==DialogResult.None,"automatic confirmation");};
                c.ExecuteMode(mode);
                Assert(BlockReplacementEngine.LastIds.SequenceEqual(new[]{x[0],x[1]}) && BlockReplacementEngine.LastTarget=="GATE VALVE" && BlockReplacementEngine.LastMode==mode,"source or mode changed");
                Assert(d.Editor.Ids.SequenceEqual(new[]{x[0],x[1]}),"preview cancel source restore");
            }));
        Check("dialog Cancel after target pick makes no replacement", () =>Fresh((d,c,x)=>
        {
            d.Editor.SetImpliedSelection(new[]{x[1]});Pump();d.Editor.NextPickIds=new[]{x[3]};DialogHost.NextResult=DialogResult.Cancel;DialogHost.Target=null;
            DialogHost.OnDialog=f=>{f.Show();Field<Button>(f,"_selectBlock").PerformClick();};c.ExecuteMode(ReplacementMode.Scale);
            Assert(BlockReplacementEngine.Calls==0 && d.Editor.Ids.SequenceEqual(new[]{x[1]}) && Enabled(c),"cancel replaced sources");
        }));
        Check("committed replacement leaves example target instance untouched", () =>Fresh((d,c,x)=>
        {
            d.Editor.SetImpliedSelection(new[]{x[1]});Pump();d.Editor.NextPickIds=new[]{x[3]};DialogHost.Target=null;BlockReplacementEngine.Commit=true;
            DialogHost.OnDialog=f=>{f.Show();Field<Button>(f,"_selectBlock").PerformClick();};c.ExecuteMode(ReplacementMode.Standard);
            Assert(x[3].IsValid && d.Editor.Ids.Length==1 && d.Editor.Ids[0].Value==1002,"picked example replaced or reselected");
        }));
    }
}
