from pathlib import Path
import sys

base=Path(sys.argv[1])
root=base/'src/CadSyncFindAndReplaceBlock'

# Use the internal non-command AutoLISP entry point from the same RENAMEBLOCKS LSP.
# This keeps manual RENAMEBLOCKS verbose while plugin-triggered runs are silent.
p=root/'FindAndReplaceControl.cs'
s=p.read_text()
assert s.count('"c:RENAMEBLOCKS"')==1, 'Expected one RENAMEBLOCKS invoke target'
s=s.replace('"c:RENAMEBLOCKS"','"RenameBlocks-Silent"')
p.write_text(s)

# Version bump only.
p=root/'FindAndReplacePalette.cs'
s=p.read_text()
assert 'Find and Replace Block 0.2.8' in s
p.write_text(s.replace('Find and Replace Block 0.2.8','Find and Replace Block 0.2.9'))

for p in [
    root/'AssemblyInfo.cs',
    root/'CadSyncFindAndReplaceBlock.csproj',
    base/'bundle/FindAndReplaceBlock.bundle/PackageContents.xml'
]:
    s=p.read_text().replace('0.2.8','0.2.9').replace('V028','V029')
    p.write_text(s)

control=(root/'FindAndReplaceControl.cs').read_text()
assert '"RenameBlocks-Silent"' in control
assert '"c:RENAMEBLOCKS"' not in control
assert 'TryRunRenameBlocksSilently' in control
print('0.2.9 staged-source checks PASS: plugin calls internal RenameBlocks-Silent; manual RENAMEBLOCKS command remains separate.')
