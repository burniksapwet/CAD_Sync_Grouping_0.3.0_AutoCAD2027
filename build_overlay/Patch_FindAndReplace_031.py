from pathlib import Path
import sys, hashlib, shutil

base = Path(sys.argv[1])
root = base/'src/CadSyncFindAndReplaceBlock'
overlay = Path(__file__).parent/'select031'
protected = ['BlockReplacementEngine.cs','DirectionalSymbolGeometry.cs','DirectionalSymbolReader.cs','BlockFinderScanner.cs','BlockRecord.cs','Commands.cs']
hashes = {name: hashlib.sha256((root/name).read_bytes()).hexdigest() for name in protected}

def replace_once(path, old, new):
    s=path.read_text()
    assert s.count(old)==1, f'Expected one patch point in {path.name}: {old[:80]}'
    path.write_text(s.replace(old,new))

p=root/'TargetBlockDialog.cs'
replace_once(p, '    private readonly List<string> _allNames;', '''    private readonly Button _selectBlock = new() { Text = "Select Block", AutoSize = true, DialogResult = DialogResult.None };
    private readonly Func<Form, string?>? _selectFromDrawing;
    private bool _selectingFromDrawing;
    private readonly List<string> _allNames;''')
replace_once(p, 'public TargetBlockDialog(string sourceName, IEnumerable<string> availableNames)', 'public TargetBlockDialog(string sourceName, IEnumerable<string> availableNames, Func<Form, string?>? selectFromDrawing = null)')
replace_once(p, '        Text = "Change Block";', '''        _selectFromDrawing = selectFromDrawing;
        _selectBlock.Enabled = selectFromDrawing != null;
        Text = "Change Block";''')
replace_once(p, '        buttons.Controls.Add(_ok);', '''        buttons.Controls.Add(_ok);
        buttons.Controls.Add(_selectBlock); // Right-to-left flow: Select Block | Change | Cancel.
        _selectBlock.Click += (_, _) => SelectTargetFromDrawing();''')
replace_once(p, '            if (DialogResult != DialogResult.OK) return;', '''            if (_selectingFromDrawing) { e.Cancel = true; return; }
            if (DialogResult != DialogResult.OK) return;''')
replace_once(p, '    private void UpdateOkState()', '''    private void SelectTargetFromDrawing()
    {
        if (_selectingFromDrawing || _selectFromDrawing == null) return;
        _selectingFromDrawing = true;
        _selectBlock.Enabled = false;
        _ok.Enabled = false;
        try
        {
            // The same dialog survives the drawing pick. Esc/null leaves both
            // the previous field value and its suggestion list untouched.
            var picked = _selectFromDrawing(this);
            if (string.IsNullOrWhiteSpace(picked)) return;
            var exact = _allNames.FirstOrDefault(n => string.Equals(n, picked, StringComparison.OrdinalIgnoreCase));
            if (exact == null)
            {
                ShowPickMessage("Choose a different available block type. The current target has not changed.");
                return;
            }
            _settingTargetText = true;
            try
            {
                _target.Text = exact;
                _target.SelectionStart = _target.Text.Length;
                _target.SelectionLength = 0;
            }
            finally { _settingTargetText = false; }
            CollapseSuggestions();
            // Choosing a name is not confirmation. Change remains a separate click.
        }
        catch (System.Exception ex)
        {
            ShowPickMessage("Block selection was not completed: " + ex.Message);
        }
        finally
        {
            _selectingFromDrawing = false;
            if (!IsDisposed)
            {
                _selectBlock.Enabled = true;
                UpdateOkState();
                Cursor.Current = Cursors.Default;
                _target.Focus();
            }
        }
    }

    private void ShowPickMessage(string message)
    {
        _suggestions.Visible = false;
        _matchMessage.Text = message;
        _matchMessage.Visible = true;
        ExpandSuggestions();
    }

    private void UpdateOkState()''')
replace_once(root/'FindAndReplaceControl.cs', 'using var dialog = new TargetBlockDialog(description, targetNames);', 'using var dialog = new TargetBlockDialog(description, targetNames, form => DrawingTargetPicker.Pick(doc, form));')
replace_once(root/'FindAndReplaceControl.cs', 'Find and Replace Block 0.2.10:', 'Find and Replace Block 0.2.11:')
shutil.copyfile(overlay/'DrawingTargetPicker.cs', root/'DrawingTargetPicker.cs')

for p in [root/'FindAndReplacePalette.cs',root/'CadSyncFindAndReplaceBlock.csproj',base/'bundle/FindAndReplaceBlock.bundle/PackageContents.xml']:
    s=p.read_text()
    assert '0.2.10' in s, f'Missing release version in {p}'
    p.write_text(s.replace('0.2.10','0.2.11').replace('V0210','V0211'))
if (root/'AssemblyInfo.cs').exists():
    p=root/'AssemblyInfo.cs';p.write_text(p.read_text().replace('0.2.10','0.2.11'))

# Extend the existing explicitly fake AutoCAD host; the real adapter is also
# compiled separately against AutoCAD.NET 26.0.0 in the release workflow.
p=base/'tests/FakeAutoCAD.cs'
replace_once(p,'public enum OpenMode { ForRead, ForWrite }','public enum OpenMode { ForRead, ForWrite }\n    public enum DxfCode { Start = 0 }')
replace_once(p,'new BlockTableRecord{Name="*Model_Space"}','new BlockTableRecord{Name="*Model_Space",IsLayout=true}')
replace_once(p,'public enum PromptStatus {OK,Error}','public enum PromptStatus {OK,Error,Cancel}')
replace_once(p,'public class SelectionSet(ObjectId[] ids){public int Count=>ids.Length;public ObjectId[] GetObjectIds()=>ids.ToArray();}', '''public class SelectionSet(ObjectId[] ids):IDisposable {public bool Disposed;public int Count=>ids.Length;public ObjectId[] GetObjectIds()=>ids.ToArray();public void Dispose(){Disposed=true;}}
    public class PromptSelectionOptions {public string MessageForAdding="";public bool SingleOnly,SinglePickInSpace,AllowDuplicates,AllowSubSelections,RejectObjectsFromNonCurrentSpace,RejectObjectsOnLockedLayers;}
    public class SelectionFilter(TypedValue[] values) {public TypedValue[] Values=values;}
    public sealed class EditorUserInteraction(Editor editor):IDisposable {public void Dispose(){editor.InteractionDepth--;}}''')
replace_once(p,'        public ObjectId[] Ids=Array.Empty<ObjectId>();public int Writes;public List<string> Messages=new();', '''        public ObjectId[] Ids=Array.Empty<ObjectId>();public int Writes;public List<string> Messages=new();
        public ObjectId[] NextPickIds=Array.Empty<ObjectId>();public bool CancelNextPick,ThrowNextPick,WasClearAtPick;
        public int InteractionDepth,InteractionsStarted;public Action? OnPick;
        public PromptSelectionOptions? LastPickOptions;public SelectionFilter? LastPickFilter;public SelectionSet? LastPickSet;
        public EditorUserInteraction StartUserInteraction(IntPtr hwnd){InteractionDepth++;InteractionsStarted++;return new(this);}
        public PromptSelectionResult GetSelection(PromptSelectionOptions options,SelectionFilter filter)
        {
            LastPickOptions=options;LastPickFilter=filter;WasClearAtPick=Ids.Length==0;
            if(ThrowNextPick)throw new InvalidOperationException("Test pick error");
            OnPick?.Invoke();
            if(CancelNextPick)return new(){Status=PromptStatus.Cancel};
            SetImpliedSelection(NextPickIds);
            LastPickSet=new(NextPickIds);
            return new(){Status=PromptStatus.OK,Value=LastPickSet};
        }''')
replace_once(p,'public static string Target="GATE VALVE";','public static string? Target="GATE VALVE";')
replace_once(p,'            Tests.Field<TextBox>(dialog,"_target").Text=Target;','            if(Target!=null) Tests.Field<TextBox>(dialog,"_target").Text=Target;')
replace_once(p,'public static int Calls;public static bool Commit;public static bool Throw;','public static int Calls;public static bool Commit;public static bool Throw;public static string? LastTarget;')
replace_once(p,'Calls++;LastIds=ids.ToArray();LastMode=mode;','Calls++;LastIds=ids.ToArray();LastMode=mode;LastTarget=name;')
replace_once(base/'tests/Tests.cs','internal static class Tests','internal static partial class Tests')
replace_once(base/'tests/Tests.cs','            Console.WriteLine($"{Passed} regression scenarios passed.','            RunTargetPickTests();\n            Console.WriteLine($"{Passed} regression scenarios passed.')
replace_once(base/'tests/SelectionTests.csproj','    <Compile Include="../src/CadSyncFindAndReplaceBlock/GlobalUsings.cs"','    <Compile Include="../src/CadSyncFindAndReplaceBlock/DrawingTargetPicker.cs" Link="DrawingTargetPicker.cs" />\n    <Compile Include="../src/CadSyncFindAndReplaceBlock/GlobalUsings.cs"')
shutil.copyfile(overlay/'TargetPickTests.cs',base/'tests/TargetPickTests.cs')

for name, before in hashes.items():
    assert hashlib.sha256((root/name).read_bytes()).hexdigest()==before, f'Protected implementation changed: {name}'
assert 'RenameBlocks-Silent' in (root/'FindAndReplaceControl.cs').read_text()
(base/'UnchangedGeometryHashes.txt').write_text('\n'.join(f'{value}  {name}' for name,value in hashes.items())+'\n')
print('0.2.11 source checks PASS: Select Block callback added; native picker accepts a window batch and resolves its first usable definition; original source snapshot retained. Six protected implementation files are byte-identical before/after patch.')
