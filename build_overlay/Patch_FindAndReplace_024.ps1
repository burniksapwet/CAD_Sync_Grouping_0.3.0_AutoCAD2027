param(
    [Parameter(Mandatory=$true)]
    [string]$ProjectDir
)

$ErrorActionPreference = 'Stop'
$controlPath = Join-Path $ProjectDir 'FindAndReplaceControl.cs'
$control = (Get-Content $controlPath -Raw).Replace("`r`n","`n")

function Replace-Once([string]$Text, [string]$Pattern, [string]$Replacement, [string]$Label) {
    $regex = [regex]::new($Pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $matches = $regex.Matches($Text)
    if ($matches.Count -ne 1) {
        throw "$Label patch expected exactly one match; found $($matches.Count)."
    }
    return $regex.Replace($Text, $Replacement, 1)
}

$control = $control.Replace(
    '    private string? _lastCadSelectionKey;',
    "    private string? _lastCadSelectionKey;`n    private Document? _selectionDocument;"
)

$control = $control.Replace(
    '        _blocks.CellDoubleClick += (_, _) => ZoomToCadSelection();',
    @'
        _blocks.CellDoubleClick += (_, _) => ZoomToCadSelection();
        Disposed += (_, _) =>
        {
            if (_selectionDocument != null)
                _selectionDocument.ImpliedSelectionChanged -= OnImpliedSelectionChanged;
            _selectionDocument = null;
        };
'@
)

$control = $control.Replace(
    @'
    public void OpenFor(Document doc)
    {
        _document = doc;
'@,
    @'
    public void OpenFor(Document doc)
    {
        if (!ReferenceEquals(_selectionDocument, doc))
        {
            if (_selectionDocument != null)
                _selectionDocument.ImpliedSelectionChanged -= OnImpliedSelectionChanged;

            _selectionDocument = doc;
            _selectionDocument.ImpliedSelectionChanged += OnImpliedSelectionChanged;
        }

        _document = doc;
'@
)

$executeMode = @'
    public void ExecuteMode(ReplacementMode mode)
    {
        if (_document == null) return;

        // A valid manual AutoCAD selection takes precedence over the finder
        // grid. This lets CHANGEBLOCKSCALE / CHANGEBLOCKSTANDARD work directly
        // from selected drawing blocks.
        var hasManualSelection = TryGetManualSourceSelection(out _, out _);

        if (!hasManualSelection && SelectedSummary() == null && _summaries.Count == 1)
        {
            _opening = true;
            try
            {
                _blocks.ClearSelection();
                if (_blocks.Rows.Count == 1)
                {
                    _blocks.Rows[0].Selected = true;
                    _blocks.CurrentCell = _blocks.Rows[0].Cells[0];
                }
            }
            finally
            {
                _opening = false;
            }
            UpdateChangeButtons();
        }

        if (SelectedSummary() == null && !TryGetManualSourceSelection(out _, out _))
        {
            _status.Text = mode == ReplacementMode.Scale
                ? "Choose one source block row or manually select one or more blocks in AutoCAD, then use Change Block (Scale)."
                : "Choose one source block row or manually select one or more blocks in AutoCAD, then use Change Block (Standard).";
            return;
        }

        ChangeSelectedBlock(mode);
    }

'@
$control = Replace-Once $control '    public void ExecuteMode\(ReplacementMode mode\).*?(?=    private void ConfigureBlockGrid\(\))' $executeMode 'ExecuteMode'

$changeBlockSection = @'
    private bool TryGetManualSourceSelection(out string sourceName, out ObjectId[] ids)
    {
        sourceName = string.Empty;
        ids = Array.Empty<ObjectId>();
        if (_document == null) return false;

        var implied = _document.Editor.SelectImplied();
        if (implied.Status != PromptStatus.OK || implied.Value == null || implied.Value.Count == 0)
            return false;

        var selectedIds = implied.Value.GetObjectIds();
        var selectedBlocks = BlockFinderScanner.Scan(_document.Database, selectedIds);
        if (selectedBlocks.Count == 0)
            return false;

        var names = selectedBlocks
            .Select(r => r.EffectiveName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        sourceName = names.Count == 1
            ? names[0]
            : $"{names.Count.ToString(CultureInfo.InvariantCulture)} mixed block types";
        ids = selectedBlocks.Select(r => r.Id).Distinct().ToArray();
        return ids.Length > 0;
    }

    private void OnImpliedSelectionChanged(object? sender, EventArgs e)
    {
        if (_opening || _document == null || IsDisposed || !IsHandleCreated)
            return;

        void RefreshManualSelectionState()
        {
            if (_opening || _document == null || IsDisposed)
                return;

            // If a finder row is selected, its programmatic AutoCAD selection
            // should match exactly. A different implied selection means the user
            // changed the selection manually in the drawing, so release the grid
            // row and let the manual selection drive the Change buttons.
            var summary = SelectedSummary();
            if (summary != null)
            {
                var implied = _document.Editor.SelectImplied();
                var actual = implied.Status == PromptStatus.OK && implied.Value != null
                    ? implied.Value.GetObjectIds().ToHashSet()
                    : new HashSet<ObjectId>();
                var expected = SelectedSourceIds().ToHashSet();

                if (!actual.SetEquals(expected))
                {
                    _opening = true;
                    try
                    {
                        _blocks.ClearSelection();
                    }
                    finally
                    {
                        _opening = false;
                    }
                    _lastCadSelectionKey = null;
                }
            }

            UpdateChangeButtons();

            if (SelectedSummary() == null &&
                TryGetManualSourceSelection(out var manualName, out var manualIds))
            {
                _status.Text = manualName.Contains("mixed block types", StringComparison.OrdinalIgnoreCase)
                    ? $"Manual CAD selection: {manualIds.Length.ToString(CultureInfo.InvariantCulture)} blocks across {manualName} ready to change."
                    : $"Manual CAD selection: {manualIds.Length.ToString(CultureInfo.InvariantCulture)} '{manualName}' block(s) ready to change.";
            }
        }

        if (InvokeRequired)
            BeginInvoke((Action)RefreshManualSelectionState);
        else
            RefreshManualSelectionState();
    }

    private void ChangeSelectedBlock(ReplacementMode mode)
    {
        if (_document == null) return;

        var source = SelectedSummary();
        string sourceName;
        ObjectId[] ids;

        if (source != null)
        {
            sourceName = source.Name;
            ids = SelectedSourceIds();
        }
        else if (TryGetManualSourceSelection(out var manualName, out var manualIds))
        {
            sourceName = manualName;
            ids = manualIds;
        }
        else
        {
            _status.Text = "Choose one source block row or manually select one or more blocks in AutoCAD.";
            return;
        }

        if (ids.Length == 0)
        {
            _status.Text = "No valid source block instances are available to replace.";
            return;
        }

        var targetNames = BlockFinderScanner.TargetDefinitionNames(_document.Database);
        using var dialog = new TargetBlockDialog(sourceName, targetNames);
        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            ReplacementResult result;
            using (_document.LockDocument())
            {
                result = BlockReplacementEngine.Replace(_document, ids, dialog.TargetName, mode);
            }

            if (!result.Committed) return;

            if (_scope.SelectedIndex == 1 && _capturedSelection != null)
            {
                var oldIds = result.SourceIds.ToHashSet();
                _capturedSelection = _capturedSelection
                    .Where(id => !oldIds.Contains(id))
                    .Concat(result.ReplacementIds)
                    .Distinct()
                    .ToArray();
            }

            RefreshRecords();
            using (_document.LockDocument())
                _document.Editor.SetImpliedSelection(result.ReplacementIds.ToArray());

            Application.UpdateScreen();
            var modeText = mode == ReplacementMode.Standard ? "Standard 1:1" : "Scale";
            _status.Text = sourceName.Contains("mixed block types", StringComparison.OrdinalIgnoreCase)
                ? $"Changed {result.ReplacementIds.Count.ToString(CultureInfo.InvariantCulture)} manually selected blocks across {sourceName} to '{dialog.TargetName}' using {modeText}. New blocks are selected in AutoCAD."
                : $"Changed {result.ReplacementIds.Count.ToString(CultureInfo.InvariantCulture)} '{sourceName}' instance(s) to '{dialog.TargetName}' using {modeText}. New blocks are selected in AutoCAD.";
        }
        catch (System.Exception ex)
        {
            _status.Text = "Change cancelled: " + ex.Message;
            _document.Editor.WriteMessage("\nChange Block cancelled: " + ex.Message);
        }
        finally
        {
            _document.Editor.Regen();
        }
    }

'@
$control = Replace-Once $control '    private void ChangeSelectedBlock\(ReplacementMode mode\).*?(?=    private void ClearCadSelection\(\))' $changeBlockSection 'ChangeSelectedBlock'

$updateButtons = @'
    private void UpdateChangeButtons()
    {
        var active = false;
        if (_document != null)
        {
            active = SelectedSummary() != null ||
                     TryGetManualSourceSelection(out _, out _);
        }

        _changeScale.Enabled = active;
        _changeStandard.Enabled = active;
        _changeScale.Invalidate();
        _changeStandard.Invalidate();
    }

'@
$control = Replace-Once $control '    private void UpdateChangeButtons\(\).*?(?=    private void ZoomToCadSelection\(\))' $updateButtons 'UpdateChangeButtons'

Set-Content $controlPath $control


$enginePath = Join-Path $ProjectDir 'BlockReplacementEngine.cs'
$engine = (Get-Content $enginePath -Raw).Replace("`r`n","`n")

$engineSourceBlock = @'
        var sourceNames = sources
            .Select(source => GetEffectiveName(tr, source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Mixed manual selections are valid. Each selected source block is
        // processed independently, so Scale mode calculates visual scaling from
        // that source's own geometry and Standard mode inserts the target at 1:1
        // while preserving that source's position/orientation/mirroring.
        var sourcesToReplace = sources
            .Where(source => !string.Equals(GetEffectiveName(tr, source), targetName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sourcesToReplace.Count == 0)
            throw new InvalidOperationException("All selected blocks already use the requested target definition.");

        var sourceDescription = sourceNames.Count == 1
            ? $"'{sourceNames[0]}'"
            : $"{sourceNames.Count.ToString()} source block types";

        var alreadyTargetCount = sources.Count - sourcesToReplace.Count;
        if (alreadyTargetCount > 0)
            ed.WriteMessage($"\n{alreadyTargetCount} selected block(s) already use '{targetName}' and will be left unchanged.");

        ed.WriteMessage($"\nFind and Replace Block: {sourcesToReplace.Count} selected block(s) from {sourceDescription} -> '{targetName}'.");

        var dynamicExtensionCount = sourcesToReplace.Count(source => source.IsDynamicBlock && !source.ExtensionDictionary.IsNull);
'@

$engine = Replace-Once $engine '        var sourceNames = sources.*?        var dynamicExtensionCount = sources\.Count\(source => source\.IsDynamicBlock && !source\.ExtensionDictionary\.IsNull\);' $engineSourceBlock 'Mixed-source engine header'

$engine = Replace-Once $engine '        foreach \(var source in sources\)\s*\{\s*            var replacementId = CreatePreviewReplacement\(doc\.Database, tr, source, definition, ed, mode\);' @'
        foreach (var source in sourcesToReplace)
        {
            var replacementId = CreatePreviewReplacement(doc.Database, tr, source, definition, ed, mode);
'@ 'Mixed-source replacement loop'

$engine = Replace-Once $engine '\$"Replace all \{replacements\.Count\} selected ''\{sourceNames\[0\]\}'' instance\(s\) with ''\{targetName\}''\?\\n\\nThe highlighted blocks are the preview\.",' @'
$"Replace {replacements.Count} selected block(s) from {sourceDescription} with '{targetName}'?\n\nThe highlighted blocks are the preview.",
'@ 'Mixed-source confirmation'

Set-Content $enginePath $engine
