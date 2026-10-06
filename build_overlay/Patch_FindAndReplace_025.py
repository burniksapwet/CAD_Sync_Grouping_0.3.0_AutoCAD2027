from pathlib import Path
import sys
base = Path(sys.argv[1])
root = base / 'src/CadSyncFindAndReplaceBlock'
engineBefore = (root/'BlockReplacementEngine.cs').read_text()
p = root/'FindAndReplaceControl.cs'
s = p.read_text()
def once(old,new):
    global s
    assert s.count(old)==1, (old[:90],s.count(old))
    s=s.replace(old,new)
def method(start,next_,new):
    global s
    a=s.index(start); b=s.index(next_,a)
    s=s[:a]+new.rstrip()+'\n\n'+s[b:]
once('    private string? _lastCadSelectionKey;\n    private Document? _selectionDocument;', '''    private Document? _selectionDocument;
    private readonly System.Windows.Forms.Timer _drawingSelectionTimer = new() { Interval = 30 };
    private ObjectId[]? _pendingGridIds;
    private ObjectId[] _drawingIdsAtGridRequest = Array.Empty<ObjectId>();
    private bool _writingCadSelection;
    private bool _changingBlocks;
    private bool _disposed;''')
once('Text = "Block Names — choose one source block type at a time",','Text = "Block Names — choose a row, or select mixed blocks in the drawing",')
once('        _layers.SelectedIndexChanged += (_, _) => RefreshBlockNames();','        _layers.SelectedIndexChanged += (_, _) => { if (!_opening) RefreshBlockNames(); };')
once('        _find.TextChanged += (_, _) => RefreshBlockNames();','        _find.TextChanged += (_, _) => { if (!_opening) RefreshBlockNames(); };')
once('        _select.Click += (_, _) => ApplyCadSelectionNow();','        _select.Click += (_, _) => { QueueCadSelection(); ApplyCadSelectionNow(); };')
a=s.index('        _blocks.SelectionChanged +='); b=s.index('\n    public void OpenFor',a)
s=s[:a]+'''        _blocks.SelectionChanged += (_, _) =>
        {
            if (_opening || _changingBlocks) return;
            QueueCadSelection();
            UpdateChangeButtons();
        };
        _blocks.CellDoubleClick += (_, _) => ZoomToCadSelection();
        _drawingSelectionTimer.Tick += (_, _) =>
        {
            _drawingSelectionTimer.Stop();
            SynchronizeDrawingSelection();
        };
        Application.DocumentManager.DocumentActivated += OnDocumentActivated;
        Application.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
        Enter += (_, _) =>
        {
            if (!_changingBlocks && _pendingGridIds == null) SynchronizeDrawingSelection();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            CancelPendingGridSelection();
            _drawingSelectionTimer.Stop();
            AttachSelectionDocument(null);
            Application.DocumentManager.DocumentActivated -= OnDocumentActivated;
            Application.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
            _selectionSyncTimer.Dispose();
            _drawingSelectionTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private bool IsActiveDocument => !_disposed && _document != null &&
        ReferenceEquals(Application.DocumentManager.MdiActiveDocument, _document);

    private void AttachSelectionDocument(Document? doc)
    {
        if (ReferenceEquals(_selectionDocument, doc)) return;
        if (_selectionDocument != null)
            _selectionDocument.ImpliedSelectionChanged -= OnImpliedSelectionChanged;
        _selectionDocument = doc;
        if (_selectionDocument != null)
            _selectionDocument.ImpliedSelectionChanged += OnImpliedSelectionChanged;
    }

    private void OnDocumentActivated(object? sender, DocumentCollectionEventArgs e)
    {
        if (_disposed || _changingBlocks || ReferenceEquals(_document, e.Document)) return;
        try { OpenFor(e.Document); }
        catch (System.Exception ex) { _status.Text = "Selection unavailable: " + ex.Message; }
    }

    private void OnDocumentToBeDestroyed(object? sender, DocumentCollectionEventArgs e)
    {
        if (!ReferenceEquals(_document, e.Document)) return;
        CancelPendingGridSelection();
        _drawingSelectionTimer.Stop();
        AttachSelectionDocument(null);
        _document = null;
        _capturedSelection = null;
        _records.Clear();
        ClearFinderRow();
        _changeScale.Enabled = _changeStandard.Enabled = false;
    }
''' + s[b:]
method('    public void OpenFor(Document doc)','    public void ExecuteMode', '''    public void OpenFor(Document doc)
    {
        CancelPendingGridSelection();
        _drawingSelectionTimer.Stop();
        _document = doc;
        AttachSelectionDocument(doc); // Actual subscription was absent in 0.2.4.
        var previousOpening = _opening;
        _opening = true;
        try
        {
            var ids = ReadDrawingSelection();
            _capturedSelection = ids.Length > 0 ? ids : null;
            _scope.SelectedIndex = ids.Length > 0 ? 1 : 0;
            RefreshRecords();
        }
        finally { _opening = previousOpening; }
        SynchronizeDrawingSelection();
    }''')
method('    public void ExecuteMode(ReplacementMode mode)','    private void ConfigureBlockGrid', '''    public void ExecuteMode(ReplacementMode mode)
    {
        // Commands and buttons share one path; the sole visible finder row does
        // not authorize replacement of every instance represented by that row.
        ChangeSelectedBlock(mode);
    }''')
s=s.replace('        _selectionSyncTimer.Stop();\n        _lastCadSelectionKey = null;','        CancelPendingGridSelection();')
once('''        _layers.BeginUpdate();
        _layers.Items.Clear();''','''        var previousOpening = _opening;
        _opening = true;
        _layers.BeginUpdate();
        try
        {
        _layers.Items.Clear();''')
once('''        _layers.EndUpdate();

        if (previousLayers.Count > 0)''','''
        if (previousLayers.Count > 0)''')
once('''            _layers.SetSelected(0, true);

        RefreshBlockNames();''','''            _layers.SetSelected(0, true);
        }
        finally
        {
            _layers.EndUpdate();
            _opening = previousOpening;
        }
        RefreshBlockNames();''')
once('''    private void RefreshBlockNames()
    {
        var selectedLayers''','''    private void RefreshBlockNames()
    {
        CancelPendingGridSelection();
        var previousOpening = _opening;
        var selectedLayers''')
once('''        finally
        {
            _opening = false;
        }

        _status.Text = $"{_records.Count''','''        finally
        {
            _summaries.RaiseListChangedEvents = true;
            _opening = previousOpening;
        }

        _status.Text = $"{_records.Count''')
method('    private void QueueCadSelection()', '    private void ChangeSelectedBlock', '''    private ObjectId[] ReadDrawingSelection()
    {
        if (!IsActiveDocument) return Array.Empty<ObjectId>();
        var implied = _document!.Editor.SelectImplied();
        return implied.Status == PromptStatus.OK && implied.Value != null
            ? implied.Value.GetObjectIds().Distinct().ToArray()
            : Array.Empty<ObjectId>();
    }

    private static bool SameIds(IEnumerable<ObjectId> a, IEnumerable<ObjectId> b) =>
        new HashSet<ObjectId>(a).SetEquals(b);

    private void CancelPendingGridSelection()
    {
        _selectionSyncTimer.Stop();
        _pendingGridIds = null;
        _drawingIdsAtGridRequest = Array.Empty<ObjectId>();
    }

    private void ClearFinderRow()
    {
        var previous = _opening;
        _opening = true;
        try { _blocks.ClearSelection(); }
        finally { _opening = previous; }
    }

    private void QueueCadSelection()
    {
        if (_opening || _changingBlocks || !IsActiveDocument) return;
        CancelPendingGridSelection();
        if (SelectedSummary() == null) return;
        try
        {
            // Pin the request now, not by looking up a later grid row.
            _drawingIdsAtGridRequest = ReadDrawingSelection();
            _pendingGridIds = SelectedSourceIds();
            _selectionSyncTimer.Start();
        }
        catch (System.Exception ex) { _status.Text = "Selection unavailable: " + ex.Message; }
    }

    private void ApplyCadSelectionNow()
    {
        _selectionSyncTimer.Stop();
        if (_opening || _changingBlocks || !IsActiveDocument || _pendingGridIds == null) return;
        var requested = _pendingGridIds.ToArray();
        var before = _drawingIdsAtGridRequest.ToArray();
        CancelPendingGridSelection();
        try
        {
            var live = ReadDrawingSelection();
            // A newer drawing selection wins even if its event is delayed.
            if (!SameIds(live, before) && !SameIds(live, requested))
            {
                ClearFinderRow();
                SynchronizeDrawingSelection();
                return;
            }
            SetDrawingSelection(requested);
            _status.Text = $"Finder selection: {requested.Length} block(s).";
            UpdateChangeButtons();
        }
        catch (System.Exception ex) { _status.Text = "Selection unavailable: " + ex.Message; }
    }

    private void SetDrawingSelection(ObjectId[] ids)
    {
        if (!IsActiveDocument) return;
        _writingCadSelection = true;
        try
        {
            using (_document!.LockDocument()) _document.Editor.SetImpliedSelection(ids);
            Application.UpdateScreen();
        }
        finally { _writingCadSelection = false; }
    }

    private void OnImpliedSelectionChanged(object? sender, EventArgs e)
    {
        if (_disposed || _writingCadSelection || _changingBlocks || !IsActiveDocument) return;
        // Cancel pending grid writes immediately. Read in the UI loop, not in
        // the AutoCAD reactor; no drawing modifications happen here.
        CancelPendingGridSelection();
        _drawingSelectionTimer.Stop();
        _drawingSelectionTimer.Start();
    }

    private void SynchronizeDrawingSelection()
    {
        if (_opening || _changingBlocks || !IsActiveDocument) return;
        try
        {
            var raw = ReadDrawingSelection();
            if (SelectedSummary() != null && !SameIds(raw, SelectedSourceIds())) ClearFinderRow();
            var blocks = BlockFinderScanner.SelectedBlocks(_document!.Database, raw);
            _changeScale.Enabled = _changeStandard.Enabled = blocks.Count > 0;
            var types = blocks.Select(b => b.EffectiveName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            _status.Text = blocks.Count == 0
                ? "Select blocks in the drawing or choose a finder row."
                : $"Drawing selection: {blocks.Count} block(s), {types} type(s). Only these selected blocks will change.";
            var nonBlocks = raw.Length - blocks.Count;
            if (nonBlocks > 0) _status.Text += $" {nonBlocks} non-block/unsupported object(s) not included.";
        }
        catch (System.Exception ex)
        {
            _changeScale.Enabled = _changeStandard.Enabled = false;
            _status.Text = "Selection unavailable: " + ex.Message;
        }
    }

    private void UpdateChangeButtons()
    {
        if (_changingBlocks || !IsActiveDocument)
        {
            _changeScale.Enabled = _changeStandard.Enabled = false;
            return;
        }
        try
        {
            var ids = _pendingGridIds ?? ReadDrawingSelection();
            var active = BlockFinderScanner.SelectedBlocks(_document!.Database, ids).Count > 0;
            _changeScale.Enabled = _changeStandard.Enabled = active;
        }
        catch { _changeScale.Enabled = _changeStandard.Enabled = false; }
    }
''')
method('    private void ChangeSelectedBlock(ReplacementMode mode)','    private void ZoomToCadSelection', '''    private void ChangeSelectedBlock(ReplacementMode mode)
    {
        if (_changingBlocks || !IsActiveDocument) return;
        ApplyCadSelectionNow();
        CancelPendingGridSelection();
        _drawingSelectionTimer.Stop();
        var doc = _document!;
        ObjectId[] originalSelection = Array.Empty<ObjectId>();
        ObjectId[]? selectionAfterCommit = null;
        var captured = false;
        _changingBlocks = true;
        _changeScale.Enabled = _changeStandard.Enabled = false;
        try
        {
            originalSelection = ReadDrawingSelection();
            captured = true;
            var blocks = BlockFinderScanner.SelectedBlocks(doc.Database, originalSelection);
            if (blocks.Count == 0)
            {
                _status.Text = "Select one or more blocks in the drawing or choose a finder row first.";
                return;
            }
            // The live pick set is the only replacement source. A stale grid row
            // cannot add unselected instances, even if its event was missed.
            var ids = blocks.Select(b => b.Id).Distinct().ToArray();
            var names = blocks.Select(b => b.EffectiveName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (SelectedSummary() != null && !SameIds(ids, SelectedSourceIds())) ClearFinderRow();
            var description = names.Length == 1 ? names[0] : $"{ids.Length} selected blocks ({names.Length} types)";
            var targetNames = BlockFinderScanner.TargetDefinitionNames(doc.Database);
            doc.Editor.WriteMessage($"\\nFind and Replace Block 0.2.5: {mode}, {ids.Length} selected source(s): " +
                string.Join(", ", blocks.Select(b => $"{b.EffectiveName} [{b.Id.Handle}]")));
            using var dialog = new TargetBlockDialog(description, targetNames);
            if (Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(this, dialog) != DialogResult.OK) return;
            if (!ReferenceEquals(Application.DocumentManager.MdiActiveDocument, doc))
                throw new InvalidOperationException("The active drawing changed. Select the blocks again.");

            ReplacementResult result;
            using (doc.LockDocument())
            {
                // This operation uses a fixed private source snapshot throughout
                // the target dialog and preview. Highlighting cannot replace it.
                result = BlockReplacementEngine.Replace(doc, ids, dialog.TargetName, mode);
            }
            if (!result.Committed) return;
            var replaced = result.SourceIds.ToHashSet();
            selectionAfterCommit = originalSelection.Where(id => !replaced.Contains(id))
                .Concat(result.ReplacementIds).Distinct().ToArray();
            if (_scope.SelectedIndex == 1 && _capturedSelection != null)
                _capturedSelection = _capturedSelection.Where(id => !replaced.Contains(id))
                    .Concat(result.ReplacementIds).Distinct().ToArray();
            RefreshRecords();
            var modeText = mode == ReplacementMode.Standard ? "Standard 1:1" : "Scale";
            _status.Text = $"Changed {result.ReplacementIds.Count} selected block(s) to '{dialog.TargetName}' using {modeText}.";
        }
        catch (System.Exception ex)
        {
            _status.Text = "Change cancelled: " + ex.Message;
            doc.Editor.WriteMessage("\\nChange Block cancelled: " + ex.Message);
        }
        finally
        {
            if (captured && IsActiveDocument)
            {
                var desired = selectionAfterCommit ?? originalSelection;
                try { SetDrawingSelection(desired.Where(id => id.IsValid && !id.IsErased).ToArray()); }
                catch (System.Exception ex) { _status.Text += " Selection refresh: " + ex.Message; }
                try { doc.Editor.Regen(); }
                catch (System.Exception ex) { _status.Text += " Display refresh: " + ex.Message; }
            }
            _changingBlocks = false;
            UpdateChangeButtons();
        }
    }

    private void ClearCadSelection()
    {
        if (_changingBlocks || !IsActiveDocument) return;
        CancelPendingGridSelection();
        _drawingSelectionTimer.Stop();
        ClearFinderRow();
        try { SetDrawingSelection(Array.Empty<ObjectId>()); }
        catch (System.Exception ex) { _status.Text = "Selection refresh: " + ex.Message; }
        SynchronizeDrawingSelection();
    }''')
assert '_lastCadSelectionKey' not in s
assert s.count('ImpliedSelectionChanged +=')==1
p.write_text(s)
p=root/'BlockFinderScanner.cs'
s=p.read_text(); i=s.index('    public static List<string> TargetDefinitionNames')
s=s[:i]+'''    public static List<BlockRecord> SelectedBlocks(Database db, IEnumerable<ObjectId> ids)
    {
        var rows = new List<BlockRecord>();
        using var tr = db.TransactionManager.StartTransaction();
        var model = SymbolUtilityServices.GetBlockModelSpaceId(db);
        foreach (var id in ids.Distinct())
        {
            if (id.IsNull || !id.IsValid || id.IsErased || id.Database.UnmanagedObject != db.UnmanagedObject) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference block || block.OwnerId != model) continue;
            var actual = (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead);
            if (actual.IsLayout || actual.IsFromExternalReference || actual.IsFromOverlayReference) continue;
            var effectiveId = block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord;
            var effective = (BlockTableRecord)tr.GetObject(effectiveId, OpenMode.ForRead);
            rows.Add(new BlockRecord
            {
                Id = id, Layer = block.Layer, EffectiveName = effective.Name, ActualName = actual.Name,
                IsDynamic = block.IsDynamicBlock,
                XScale = block.ScaleFactors.X, YScale = block.ScaleFactors.Y, ZScale = block.ScaleFactors.Z
            });
        }
        tr.Commit();
        return rows;
    }

'''+s[i:];p.write_text(s)
p=root/'BlockReplacementEngine.cs';s=p.read_text()
old='''            if (id.IsNull || !id.IsValid) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference source) continue;'''
assert s.count(old)==1
s=s.replace(old,'''            if (id.IsNull || !id.IsValid || id.IsErased || id.Database.UnmanagedObject != doc.Database.UnmanagedObject)
                throw new InvalidOperationException("A selected source is no longer available. Select the blocks again; no replacements were committed.");
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference source)
                throw new InvalidOperationException("The captured source selection is no longer valid. No replacements were committed.");''')
s=s.replace('Choose one source block row first.','Select blocks in the drawing or choose a source block row first.')
p.write_text(s)
p=root/'Commands.cs';s=p.read_text()
assert s.count('CommandFlags.Modal | CommandFlags.UsePickSet')==3
s=s.replace('CommandFlags.Modal | CommandFlags.UsePickSet','CommandFlags.Modal | CommandFlags.UsePickSet | CommandFlags.Redraw');p.write_text(s)
p=root/'FindAndReplacePalette.cs';s=p.read_text().replace('new PaletteSet("Find and Replace Block")','new PaletteSet("Find and Replace Block 0.2.5")');p.write_text(s)
for p in [root/'AssemblyInfo.cs',root/'CadSyncFindAndReplaceBlock.csproj',base/'bundle/FindAndReplaceBlock.bundle/PackageContents.xml']:
    s=p.read_text().replace('0.2.4','0.2.5').replace('0.2.1','0.2.5').replace('V024','V025').replace('V021','V025');p.write_text(s)
p=root/'TargetBlockDialog.cs';s=p.read_text()
s=s.replace('    private bool _expanded;', '    private bool _expanded;\n    private bool _updatingSuggestions;\n    private bool _settingTargetText;')
s=s.replace('''        _target.TextChanged += (_, _) =>
        {
            UpdateOkState();''','''        _target.TextChanged += (_, _) =>
        {
            if (_settingTargetText) return;
            UpdateOkState();''')
s=s.replace('''            if (_suggestions.SelectedItem is string name)
            {
                _target.Text = name;
                _target.SelectionStart = _target.Text.Length;
                _target.SelectionLength = 0;
                UpdateOkState();
            }''','''            if (!_updatingSuggestions && _suggestions.SelectedItem is string name)
            {
                _settingTargetText = true;
                try
                {
                    _target.Text = name;
                    _target.SelectionStart = _target.Text.Length;
                    _target.SelectionLength = 0;
                }
                finally { _settingTargetText = false; }
                UpdateOkState();
            }''')
s=s.replace('''        _suggestions.BeginUpdate();
        _suggestions.Items.Clear();
        foreach (var name in choices) _suggestions.Items.Add(name);
        _suggestions.EndUpdate();''','''        _updatingSuggestions = true;
        _suggestions.BeginUpdate();
        try
        {
            _suggestions.ClearSelected();
            _suggestions.Items.Clear();
            foreach (var name in choices) _suggestions.Items.Add(name);
        }
        finally
        {
            _suggestions.EndUpdate();
            _updatingSuggestions = false;
        }''')
p.write_text(s)
engineAfter=(root/'BlockReplacementEngine.cs').read_text()
marker='    private static ObjectId CreatePreviewReplacement('
assert engineBefore[engineBefore.index(marker):] == engineAfter[engineAfter.index(marker):], 'Geometry, attribute, or group routines changed unexpectedly'
control=(root/'FindAndReplaceControl.cs').read_text()
assert control.count('ImpliedSelectionChanged += OnImpliedSelectionChanged')==1
assert 'AttachSelectionDocument(doc);' in control[control.index('public void OpenFor'):control.index('public void ExecuteMode')]
assert 'if (source != null)' not in control[control.index('private void ChangeSelectedBlock'):control.index('private void ClearCadSelection')]
assert 'BlockReplacementEngine.Replace(doc, ids, dialog.TargetName, mode)' in control
assert (root/'Commands.cs').read_text().count('CommandFlags.Redraw') == 3
assert 'new(785, 890)' in (root/'FindAndReplacePalette.cs').read_text()
print('Staged-source checks PASS: live selection wiring, no stale-row replacement, 3 Redraw flags, unchanged placement/scale/group routines, 785x890 retained.')
