from pathlib import Path
import sys

base = Path(sys.argv[1])
root = base / 'src/CadSyncFindAndReplaceBlock'

# ---------- Block summary supports names that are available but have no instances ----------
p = root / 'BlockRecord.cs'
s = p.read_text()
old = '''    public bool AnyDynamic { get; init; }
    public bool MixedScale { get; init; }
    public string TypeText => AnyDynamic ? "Dynamic" : "Static";
    public string ScaleText => MixedScale ? "Mixed" : "Same";'''
new = '''    public bool AnyDynamic { get; init; }
    public bool MixedScale { get; init; }
    public bool AvailableOnly { get; init; }
    public string TypeText => AvailableOnly ? "Available" : AnyDynamic ? "Dynamic" : "Static";
    public string ScaleText => AvailableOnly ? "" : MixedScale ? "Mixed" : "Same";'''
assert s.count(old) == 1, 'BlockNameSummary patch point not found'
p.write_text(s.replace(old, new))

# ---------- Main palette: RENAMEBLOCKS + All Blocks / All Layers ----------
p = root / 'FindAndReplaceControl.cs'
s = p.read_text()

s = s.replace(
    '    private bool _disposed;\n',
    '''    private bool _disposed;
    private const string AllBlocksItem = "All Blocks";
    private const string AllLayersItem = "All Layers";
''',
    1)

old = '        _refresh.Click += (_, _) => CaptureAndRefresh();'
new = '        _refresh.Click += async (_, _) => await RefreshWithRenameBlocksAsync();'
assert s.count(old) == 1, 'Refresh click patch point not found'
s = s.replace(old, new)

marker = '''    private bool IsActiveDocument => !_disposed && _document != null &&
        ReferenceEquals(Application.DocumentManager.MdiActiveDocument, _document);
'''
helper = '''    internal static void TryRunRenameBlocksSilently()
    {
        try
        {
            using var args = new ResultBuffer(
                new TypedValue((int)Autodesk.AutoCAD.Runtime.LispDataType.Text, "c:RENAMEBLOCKS"));
            using var result = Autodesk.AutoCAD.ApplicationServices.Application.Invoke(args);
        }
        catch
        {
            // Optional macro. Missing/unavailable RENAMEBLOCKS is deliberately silent.
        }
    }

    private async Task RefreshWithRenameBlocksAsync()
    {
        var doc = _document;
        if (doc == null) return;

        try
        {
            await Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                TryRunRenameBlocksSilently();
                return Task.CompletedTask;
            }, null);
        }
        catch
        {
            // If command-context execution is unavailable, regular Refresh must still work.
        }

        if (_disposed || !ReferenceEquals(_document, doc) ||
            !ReferenceEquals(Application.DocumentManager.MdiActiveDocument, doc))
            return;

        CaptureAndRefresh();
    }

'''
assert s.count(marker) == 1, 'IsActiveDocument insertion point not found'
s = s.replace(marker, helper + marker)

old = '''        var previousLayers = _layers.SelectedItems.Cast<string>().Where(s => s != "<All Layers>").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousOpening = _opening;
        _opening = true;
        _layers.BeginUpdate();
        try
        {
        _layers.Items.Clear();
        _layers.Items.Add("<All Layers>");
        foreach (var layer in _records.Select(r => r.Layer).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            _layers.Items.Add(layer);

        if (previousLayers.Count > 0)
        {
            for (var i = 0; i < _layers.Items.Count; i++)
                if (_layers.Items[i] is string layer && previousLayers.Contains(layer))
                    _layers.SetSelected(i, true);
        }

        if (_layers.SelectedIndices.Count == 0 && _layers.Items.Count > 0)
            _layers.SetSelected(0, true);
        }'''
new = '''        var previousItems = _layers.SelectedItems.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousOpening = _opening;
        _opening = true;
        _layers.BeginUpdate();
        try
        {
        _layers.Items.Clear();
        _layers.Items.Add(AllBlocksItem);
        _layers.Items.Add(AllLayersItem);
        foreach (var layer in _records.Select(r => r.Layer).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            _layers.Items.Add(layer);

        if (previousItems.Count > 0)
        {
            for (var i = 0; i < _layers.Items.Count; i++)
                if (_layers.Items[i] is string item && previousItems.Contains(item))
                    _layers.SetSelected(i, true);
        }

        // Preserve the old default behavior: all drawing layers.
        if (_layers.SelectedIndices.Count == 0 && _layers.Items.Count > 1)
            _layers.SetSelected(1, true);
        }'''
assert s.count(old) == 1, 'Layer list rebuild patch point not found'
s = s.replace(old, new)

start = s.index('    private void RefreshBlockNames()')
end = s.index('    private HashSet<string> SelectedLayers()', start)
replacement = '''    private void RefreshBlockNames()
    {
        CancelPendingGridSelection();
        var previousOpening = _opening;
        var showAllBlocks = _layers.SelectedItems.Cast<string>()
            .Any(item => string.Equals(item, AllBlocksItem, StringComparison.OrdinalIgnoreCase));
        var selectedLayers = SelectedLayers();
        var filter = _find.Text.Trim();

        List<BlockNameSummary> summaries;

        if (showAllBlocks && _document != null)
        {
            var allNames = BlockFinderScanner.TargetDefinitionNames(_document.Database);
            var instancesByName = _records
                .GroupBy(r => r.EffectiveName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            IEnumerable<string> names = allNames;
            if (!string.IsNullOrWhiteSpace(filter))
                names = names.Where(name => name.Contains(filter, StringComparison.OrdinalIgnoreCase));

            summaries = names
                .Select(name =>
                {
                    instancesByName.TryGetValue(name, out var instances);
                    instances ??= new List<BlockRecord>();
                    return new BlockNameSummary
                    {
                        Name = name,
                        Count = instances.Count,
                        AnyDynamic = instances.Any(x => x.IsDynamic),
                        MixedScale = HasMixedScale(instances),
                        AvailableOnly = instances.Count == 0
                    };
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            IEnumerable<BlockRecord> source = _records;
            if (selectedLayers.Count > 0)
                source = source.Where(r => selectedLayers.Contains(r.Layer));
            if (!string.IsNullOrWhiteSpace(filter))
                source = source.Where(r => r.EffectiveName.Contains(filter, StringComparison.OrdinalIgnoreCase));

            summaries = source
                .GroupBy(r => r.EffectiveName, StringComparer.OrdinalIgnoreCase)
                .Select(g => new BlockNameSummary
                {
                    Name = g.Key,
                    Count = g.Count(),
                    AnyDynamic = g.Any(x => x.IsDynamic),
                    MixedScale = HasMixedScale(g)
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        _opening = true;
        try
        {
            _summaries.RaiseListChangedEvents = false;
            _summaries.Clear();
            foreach (var summary in summaries) _summaries.Add(summary);
            _summaries.RaiseListChangedEvents = true;
            _summaries.ResetBindings();
            _blocks.ClearSelection();
        }
        finally
        {
            _summaries.RaiseListChangedEvents = true;
            _opening = previousOpening;
        }

        _status.Text = showAllBlocks
            ? $"{summaries.Count.ToString(CultureInfo.InvariantCulture)} available block name(s) shown.  {_records.Count.ToString(CultureInfo.InvariantCulture)} block instance(s) in scope."
            : $"{_records.Count.ToString(CultureInfo.InvariantCulture)} block instance(s) in scope.  {summaries.Count.ToString(CultureInfo.InvariantCulture)} block name(s) shown.";
        UpdateChangeButtons();
    }

'''
s = s[:start] + replacement + s[end:]

old = '''    private HashSet<string> SelectedLayers()
    {
        var selected = _layers.SelectedItems.Cast<string>().ToList();
        if (selected.Count == 0 || selected.Contains("<All Layers>"))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }'''
new = '''    private HashSet<string> SelectedLayers()
    {
        var selected = _layers.SelectedItems.Cast<string>().ToList();
        if (selected.Count == 0 ||
            selected.Any(item => string.Equals(item, AllBlocksItem, StringComparison.OrdinalIgnoreCase)) ||
            selected.Any(item => string.Equals(item, AllLayersItem, StringComparison.OrdinalIgnoreCase)))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }'''
assert s.count(old) == 1, 'SelectedLayers patch point not found'
s = s.replace(old, new)

old = '''        CancelPendingGridSelection();
        if (SelectedSummary() == null) return;
        try
        {
            // Pin the request now, not by looking up a later grid row.
            _drawingIdsAtGridRequest = ReadDrawingSelection();
            _pendingGridIds = SelectedSourceIds();
            _selectionSyncTimer.Start();
        }'''
new = '''        CancelPendingGridSelection();
        var summary = SelectedSummary();
        if (summary == null) return;
        try
        {
            var ids = SelectedSourceIds();
            if (ids.Length == 0)
            {
                _status.Text = $"'{summary.Name}' is available as a replacement target but has no instances in the current scope.";
                UpdateChangeButtons();
                return;
            }

            // Pin the request now, not by looking up a later grid row.
            _drawingIdsAtGridRequest = ReadDrawingSelection();
            _pendingGridIds = ids;
            _selectionSyncTimer.Start();
        }'''
assert s.count(old) == 1, 'QueueCadSelection patch point not found'
s = s.replace(old, new)

p.write_text(s)

# ---------- Run optional RENAMEBLOCKS immediately when FINDANDREPLACEBLOCK is invoked ----------
p = root / 'Commands.cs'
s = p.read_text()
old = '''        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;

        FindAndReplacePalette.Show(doc);
    }

    [CommandMethod("CHANGEBLOCKSCALE"'''
new = '''        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;

        FindAndReplaceControl.TryRunRenameBlocksSilently();
        FindAndReplacePalette.Show(doc);
    }

    [CommandMethod("CHANGEBLOCKSCALE"'''
assert s.count(old) == 1, 'FINDANDREPLACEBLOCK command patch point not found'
p.write_text(s.replace(old, new))

# ---------- Version ----------
p = root / 'FindAndReplacePalette.cs'
s = p.read_text()
assert 'Find and Replace Block 0.2.6' in s
p.write_text(s.replace('Find and Replace Block 0.2.6', 'Find and Replace Block 0.2.7'))

for p in [
    root / 'AssemblyInfo.cs',
    root / 'CadSyncFindAndReplaceBlock.csproj',
    base / 'bundle/FindAndReplaceBlock.bundle/PackageContents.xml'
]:
    s = p.read_text().replace('0.2.6', '0.2.7').replace('V026', 'V027')
    p.write_text(s)

# ---------- Test-host compatibility and regression additions ----------
fake = base / 'tests/FakeAutoCAD.cs'
if fake.exists():
    s = fake.read_text()
    s = s.replace(
        '    public enum OpenMode { ForRead, ForWrite }',
        '''    public enum OpenMode { ForRead, ForWrite }
    public readonly record struct TypedValue(int TypeCode, object? Value);
    public sealed class ResultBuffer : IDisposable
    {
        public TypedValue[] Values;
        public ResultBuffer(params TypedValue[] values){Values=values;}
        public TypedValue[] AsArray()=>Values;
        public void Dispose(){}
    }''')
    s = s.replace(
        'namespace Autodesk.AutoCAD.EditorInput',
        '''namespace Autodesk.AutoCAD.Runtime
{
    public enum LispDataType { Text = 5005 }
}
namespace Autodesk.AutoCAD.EditorInput''')
    s = s.replace(
        '''    public class DocumentCollection
    {
        public Document? MdiActiveDocument;
        public event EventHandler<DocumentCollectionEventArgs>? DocumentActivated,DocumentToBeDestroyed;''',
        '''    public class DocumentCollection
    {
        public Document? MdiActiveDocument;
        public event EventHandler<DocumentCollectionEventArgs>? DocumentActivated,DocumentToBeDestroyed;
        public Task ExecuteInCommandContextAsync(Func<object,Task> action, object? data)=>action(data!);''')
    s = s.replace(
        '''    public static class Application
    {
        public static Action<Form>? OnDialog;''',
        '''    public static class Application
    {
        public static int InvokeCalls;
        public static bool InvokeThrows;
        public static Autodesk.AutoCAD.DatabaseServices.ResultBuffer? Invoke(Autodesk.AutoCAD.DatabaseServices.ResultBuffer args)
        {
            InvokeCalls++;
            if(InvokeThrows) throw new InvalidOperationException("Optional LISP unavailable");
            return null;
        }
        public static Action<Form>? OnDialog;''')
    fake.write_text(s)

tests = base / 'tests/Tests.cs'
if tests.exists():
    s = tests.read_text()
    s = s.replace(
        '        BlockReplacementEngine.Calls=0;BlockReplacementEngine.Commit=false;BlockReplacementEngine.Throw=false;BlockReplacementEngine.LastIds=Array.Empty<ObjectId>();',
        '''        BlockReplacementEngine.Calls=0;BlockReplacementEngine.Commit=false;BlockReplacementEngine.Throw=false;BlockReplacementEngine.LastIds=Array.Empty<ObjectId>();
        DialogHost.InvokeCalls=0;DialogHost.InvokeThrows=false;''')
    needle = '            Check("manual single activates both buttons without grid row"'
    insert = '''            Check("layer selector exposes All Blocks then All Layers without angle brackets",()=>Fresh((d,c,x)=>{
                var layers=Field<ListBox>(c,"_layers");
                Assert((string)layers.Items[0]=="All Blocks","All Blocks missing/position");
                Assert((string)layers.Items[1]=="All Layers","All Layers missing/position");
                Assert(!layers.Items.Cast<object>().Any(v=>v?.ToString()=="<All Layers>"),"old angle-bracket label remains");
            }));
            Check("manual Refresh silently attempts optional RENAMEBLOCKS and still refreshes when unavailable",()=>Fresh((d,c,x)=>{
                var refresh=Field<Button>(c,"_refresh");
                DialogHost.InvokeThrows=true;
                refresh.PerformClick();Pump();
                Assert(DialogHost.InvokeCalls==1,"RENAMEBLOCKS was not attempted");
                Assert(Field<DataGridView>(c,"_blocks").Rows.Count>0,"regular refresh did not continue");
            }));
'''
    assert needle in s, 'Test insertion point not found'
    s = s.replace(needle, insert + needle)
    tests.write_text(s)

# Guards
control = (root/'FindAndReplaceControl.cs').read_text()
assert '"c:RENAMEBLOCKS"' in control
assert 'ExecuteInCommandContextAsync' in control
assert '_layers.Items.Add(AllBlocksItem);' in control
assert '_layers.Items.Add(AllLayersItem);' in control
assert '<All Layers>' not in control
assert 'BlockFinderScanner.TargetDefinitionNames(_document.Database)' in control
assert 'AvailableOnly = instances.Count == 0' in control
assert 'FindAndReplaceControl.TryRunRenameBlocksSilently();' in (root/'Commands.cs').read_text()
print('0.2.7 staged-source checks PASS: silent RENAMEBLOCKS on command/manual Refresh, All Blocks browser, plain All Layers, existing selection behavior retained.')
