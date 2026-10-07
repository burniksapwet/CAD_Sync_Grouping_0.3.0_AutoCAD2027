from pathlib import Path
import sys

base = Path(sys.argv[1])
root = base / 'src/CadSyncFindAndReplaceBlock'

# ---- Target list: current drawing + top-level DWG files in Work ToolPalettte ----
scanner = root / 'BlockFinderScanner.cs'
s = scanner.read_text()

old = '''        tr.Commit();
        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}'''

new = '''        tr.Commit();

        // Also offer top-level DWG files from the user's DMG Work ToolPalettte
        // folder. Subfolders are intentionally ignored. A definition already in
        // the current drawing wins because the merged name list is de-duplicated.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var libraryFolder = Path.Combine(appData, "Autodesk", "_DMG_Folders", "Work ToolPalettte");
        try
        {
            if (Directory.Exists(libraryFolder))
            {
                foreach (var file in Directory.EnumerateFiles(libraryFolder, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!string.Equals(Path.GetExtension(file), ".dwg", StringComparison.OrdinalIgnoreCase)) continue;
                    var name = Path.GetFileNameWithoutExtension(file).Trim();
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}'''

assert s.count(old) == 1, 'TargetDefinitionNames patch point not found'
scanner.write_text(s.replace(old, new))

# ---- If selected target exists only as a library DWG, import it on demand ----
engine = root / 'BlockReplacementEngine.cs'
s = engine.read_text()

old = '''        using var tr = doc.Database.TransactionManager.StartTransaction();
        var table = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        if (!table.Has(targetName))
            throw new InvalidOperationException($"Target definition '{targetName}' does not exist in this drawing.");

        var definition = (BlockTableRecord)tr.GetObject(table[targetName], OpenMode.ForRead);'''

new = '''        EnsureTargetDefinitionAvailable(doc.Database, targetName);

        using var tr = doc.Database.TransactionManager.StartTransaction();
        var table = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        if (!table.Has(targetName))
            throw new InvalidOperationException($"Target definition '{targetName}' could not be loaded.");

        var definition = (BlockTableRecord)tr.GetObject(table[targetName], OpenMode.ForRead);'''

assert s.count(old) == 1, 'Engine target lookup patch point not found'
s = s.replace(old, new)

insert_before = '''    private static bool IsBlockReferenceId(ObjectId id)
    {'''

helper = '''    private static void EnsureTargetDefinitionAvailable(Database db, string targetName)
    {
        using (var check = db.TransactionManager.StartTransaction())
        {
            var table = (BlockTable)check.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (table.Has(targetName)) return; // Current drawing always wins.
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var libraryFolder = Path.Combine(appData, "Autodesk", "_DMG_Folders", "Work ToolPalettte");
        string? libraryDwg = null;

        try
        {
            if (Directory.Exists(libraryFolder))
            {
                libraryDwg = Directory.EnumerateFiles(libraryFolder, "*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(file =>
                        string.Equals(Path.GetExtension(file), ".dwg", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(Path.GetFileNameWithoutExtension(file), targetName, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        if (libraryDwg == null)
            throw new InvalidOperationException($"Target definition '{targetName}' is not in this drawing or the Work ToolPalettte folder.");

        using var sourceDb = new Database(false, true);
        sourceDb.ReadDwgFile(libraryDwg, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
        db.Insert(targetName, sourceDb, false);
    }

'''

assert s.count(insert_before) == 1, 'Engine helper insertion point not found'
s = s.replace(insert_before, helper + insert_before)
s = s.replace('Find and Replace Block 0.2.5:', 'Find and Replace Block 0.2.6:')
engine.write_text(s)

# ---- Version only; behavior otherwise remains the 0.2.5 baseline ----
palette = root / 'FindAndReplacePalette.cs'
s = palette.read_text()
assert 'Find and Replace Block 0.2.5' in s
palette.write_text(s.replace('Find and Replace Block 0.2.5', 'Find and Replace Block 0.2.6'))

for p in [
    root / 'AssemblyInfo.cs',
    root / 'CadSyncFindAndReplaceBlock.csproj',
    base / 'bundle/FindAndReplaceBlock.bundle/PackageContents.xml'
]:
    s = p.read_text()
    s = s.replace('0.2.5', '0.2.6').replace('V025', 'V026')
    p.write_text(s)

# Staged-source guards for this one change.
scanner_text = scanner.read_text()
engine_text = engine.read_text()
assert 'SearchOption.TopDirectoryOnly' in scanner_text
assert '"Work ToolPalettte"' in scanner_text
assert 'Directory.EnumerateFiles(libraryFolder, "*", SearchOption.TopDirectoryOnly)' in scanner_text
assert 'EnsureTargetDefinitionAvailable(doc.Database, targetName);' in engine_text
assert 'db.Insert(targetName, sourceDb, false);' in engine_text
assert 'SearchOption.AllDirectories' not in scanner_text
print('0.2.6 staged-source checks PASS: drawing targets retained, top-level ToolPalettte DWGs merged, subfolders ignored, library target imported on demand.')
