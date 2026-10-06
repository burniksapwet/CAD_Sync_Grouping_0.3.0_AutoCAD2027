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
                ? "Choose one source block row or manually select one block type in AutoCAD, then use Change Block (Scale)."
                : "Choose one source block row or manually select one block type in AutoCAD, then use Change Block (Standard).";
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

        if (names.Count != 1)
            return false;

        sourceName = names[0];
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
                _status.Text = $"Manual CAD selection: {manualIds.Length.ToString(CultureInfo.InvariantCulture)} '{manualName}' block(s) ready to change.";
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
            _status.Text = "Choose one source block row or manually select blocks of one source type in AutoCAD.";
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
            _status.Text = $"Changed {result.ReplacementIds.Count.ToString(CultureInfo.InvariantCulture)} '{sourceName}' instance(s) to '{dialog.TargetName}' using {modeText}. New blocks are selected in AutoCAD.";
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
