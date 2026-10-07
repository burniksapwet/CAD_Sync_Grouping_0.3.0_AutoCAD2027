from pathlib import Path
import sys

base = Path(sys.argv[1])
root = base / 'src/CadSyncFindAndReplaceBlock'

# Reset layer view only when the palette is first shown or reopened after being closed.
p = root / 'FindAndReplaceControl.cs'
s = p.read_text()

marker = '''    public void ExecuteMode(ReplacementMode mode)
    {'''
method = '''    internal void ResetLayerViewForOpen()
    {
        var previousOpening = _opening;
        _opening = true;
        try
        {
            _layers.ClearSelected();
            for (var i = 0; i < _layers.Items.Count; i++)
            {
                if (_layers.Items[i] is string item &&
                    string.Equals(item, AllLayersItem, StringComparison.OrdinalIgnoreCase))
                {
                    _layers.SetSelected(i, true);
                    break;
                }
            }
        }
        finally
        {
            _opening = previousOpening;
        }

        RefreshBlockNames();
    }

'''
assert s.count(marker) == 1, 'ExecuteMode insertion point not found'
s = s.replace(marker, method + marker)
p.write_text(s)

p = root / 'FindAndReplacePalette.cs'
s = p.read_text()

old = '''    public static void Show(Document doc)
    {
        if (_palette == null)
        {'''
new = '''    public static void Show(Document doc)
    {
        var reopening = _palette == null || !_palette.Visible;

        if (_palette == null)
        {'''
assert s.count(old) == 1, 'Palette Show header patch point not found'
s = s.replace(old, new)

old = '''        _control!.OpenFor(doc);
        _palette.Visible = true;'''
new = '''        _control!.OpenFor(doc);
        if (reopening)
            _control.ResetLayerViewForOpen();

        _palette.Visible = true;'''
assert s.count(old) == 1, 'Palette Show body patch point not found'
s = s.replace(old, new)

assert 'Find and Replace Block 0.2.7' in s
p.write_text(s.replace('Find and Replace Block 0.2.7', 'Find and Replace Block 0.2.8'))

for p in [
    root / 'AssemblyInfo.cs',
    root / 'CadSyncFindAndReplaceBlock.csproj',
    base / 'bundle/FindAndReplaceBlock.bundle/PackageContents.xml'
]:
    s = p.read_text().replace('0.2.7', '0.2.8').replace('V027', 'V028')
    p.write_text(s)

# Regression: a prior All Blocks/real-layer selection must reset to only All Layers.
tests = base / 'tests/Tests.cs'
if tests.exists():
    s = tests.read_text()
    needle = '            Check("manual single activates both buttons without grid row"'
    insert = '''            Check("reopen resets layer view to All Layers",()=>Fresh((d,c,x)=>{
                var layers=Field<ListBox>(c,"_layers");
                layers.ClearSelected();
                layers.SetSelected(0,true); // All Blocks
                if(layers.Items.Count>2) layers.SetSelected(2,true); // a real layer
                c.ResetLayerViewForOpen();
                var selected=layers.SelectedItems.Cast<string>().ToList();
                Assert(selected.Count==1&&selected[0]=="All Layers","layer view did not reset to All Layers");
            }));
'''
    assert needle in s, 'Regression insertion point not found'
    s = s.replace(needle, insert + needle)
    tests.write_text(s)

control = (root/'FindAndReplaceControl.cs').read_text()
palette = (root/'FindAndReplacePalette.cs').read_text()
assert 'internal void ResetLayerViewForOpen()' in control
assert 'string.Equals(item, AllLayersItem' in control
assert 'var reopening = _palette == null || !_palette.Visible;' in palette
assert 'if (reopening)' in palette and '_control.ResetLayerViewForOpen();' in palette
print('0.2.8 staged-source checks PASS: first show/reopen resets only the layer view to All Layers.')
