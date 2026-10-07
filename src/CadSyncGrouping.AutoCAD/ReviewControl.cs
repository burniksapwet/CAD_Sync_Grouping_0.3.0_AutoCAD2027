using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System.ComponentModel;
using System.Windows.Forms;

namespace CadSyncGrouping;

internal sealed class ReviewControl : UserControl
{
    private readonly SelectionGrid _grid = new();
    private readonly BindingList<GroupingCandidate> _rows = new();
    private readonly Label _summary = new();
    private readonly Label _detail = new() { Dock=DockStyle.Top, Height=128, Padding=new Padding(8), AutoEllipsis=true, BackColor=System.Drawing.Color.WhiteSmoke };
    private readonly ComboBox _view = new() { DropDownStyle=ComboBoxStyle.DropDownList, Width=150 };
    private readonly Dictionary<string,TextBox> _filters = new();
    private string _sortProperty=nameof(GroupingCandidate.LocationId);
    private bool _descending;
    private readonly ComboBox _statusFilter = new() { DropDownStyle=ComboBoxStyle.DropDownList, Width=135 };

    private bool _refreshing;
    public ReviewControl()
    {
        Dock = DockStyle.Fill;

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(5)
        };

        toolbar.Controls.Add(MakeButton("Rescan", (_, _) => Rescan()));
        toolbar.Controls.Add(MakeButton("Group Selected", (_, _) => { PrepareGrouping(); }));
        toolbar.Controls.Add(MakeButton("Select All", (_,_)=>SelectVisible(true)));
        toolbar.Controls.Add(MakeButton("Clear Selection", (_,_)=>SelectVisible(false)));
        toolbar.Controls.Add(MakeButton("Ignore", (_,_)=>SetIgnored(true)));
        toolbar.Controls.Add(MakeButton("Reopen", (_,_)=>SetIgnored(false)));
        toolbar.Controls.Add(MakeButton("Move Group", (_, _) => Application.DocumentManager.MdiActiveDocument?.SendStringToExecute("MOVEGROUP ", true, false, false)));
        _view.Items.AddRange(new object[]{"Results","Needs Attention","Existing Groups","Ignored","Show Everything"});
        _view.SelectedIndex=0;

        _view.SelectedIndexChanged+=(_,_)=>RefreshFromState();
        var views=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Padding=new Padding(5)};
        views.Controls.Add(new Label{Text="View",AutoSize=true,Padding=new Padding(0,5,0,0)});
        views.Controls.Add(_view);
        var filters=new FlowLayoutPanel { Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Padding=new Padding(5) };
        foreach(var pair in new[]{("Location ID",nameof(GroupingCandidate.LocationId)),("Block",nameof(GroupingCandidate.ProposedBlockName)),("Reason",nameof(GroupingCandidate.Reason))})
        {
            filters.Controls.Add(new Label{Text=pair.Item1,AutoSize=true,Padding=new Padding(0,5,0,0)});
            var input=new TextBox{Width=110};_filters[pair.Item2]=input;filters.Controls.Add(input);
            input.TextChanged+=(_,_) =>
            {
                // Any text search should search the complete scan, including
                // clean existing groups and ignored rows. When the final text
                // filter is cleared manually, return to the normal Results view.
                var anyTextFilter = _filters.Values.Any(box => !string.IsNullOrWhiteSpace(box.Text));
                var desiredView = anyTextFilter ? 4 : 0; // Show Everything : Results
                if(_view.SelectedIndex!=desiredView)
                {
                    _view.SelectedIndex=desiredView; // change event refreshes
                    return;
                }
                RefreshFromState();
            };
        }
        views.Controls.Add(new Label{Text="Status",AutoSize=true,Padding=new Padding(0,5,0,0)});
        _statusFilter.Items.AddRange(new object[]{"All statuses","Ready","Reviewed","Review","Conflict","No match","Existing group","Manually verified group","Ignored"});
        _statusFilter.SelectedIndex=0;
        _statusFilter.SelectedIndexChanged+=(_,_)=>RefreshFromState();
        views.Controls.Add(_statusFilter);
        filters.Controls.Add(MakeButton("Clear Filters",(_,_)=>{foreach(var input in _filters.Values)input.Clear();_view.SelectedIndex=0;_statusFilter.SelectedIndex=0;RefreshFromState();}));

        _summary.Dock = DockStyle.Bottom;
        _summary.AutoSize = true;
        _summary.Padding = new Padding(6);

        ConfigureGrid();
        Controls.Add(_grid);
        Controls.Add(_detail);
        Controls.Add(filters);
        Controls.Add(views);
        Controls.Add(toolbar);
        Controls.Add(_summary);
    }

    public void ResetForOpen()
    {
        // Closing a PaletteSet only hides it; the same control instance remains
        // alive for the AutoCAD session. Reset the working filters whenever the
        // hidden palette is explicitly opened again.
        foreach(var input in _filters.Values)
            input.Clear();

        _statusFilter.SelectedIndex=0;
        _view.SelectedIndex=0; // Results
        RefreshFromState();
    }

    private Button MakeButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (s,e)=>{try{handler(s,e);}catch(System.Exception ex){_summary.Text=ex.Message;}};
        return button;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(GroupingCandidate.ActionSelected),
            HeaderText = "Select",
            ReadOnly = true,
            Width = 50
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(GroupingCandidate.LocationId),
            HeaderText = "Location ID",
            Width = 115,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(GroupingCandidate.ProposedBlockName),
            HeaderText = "Proposed Valve Block",
            Width = 190,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(GroupingCandidate.StatusText),
            HeaderText = "Status",
            Width = 100,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(GroupingCandidate.Reason),
            HeaderText = "Reason",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true
        });

        foreach(DataGridViewColumn column in _grid.Columns)column.SortMode=column.Index==0?DataGridViewColumnSortMode.NotSortable:DataGridViewColumnSortMode.Programmatic;
        _grid.ColumnHeaderMouseClick+=(_,e)=>
        {
            if(e.ColumnIndex<=0)return;
            var property=_grid.Columns[e.ColumnIndex].DataPropertyName;
            _descending=property==_sortProperty&&!_descending;_sortProperty=property;
            RefreshFromState();
        };
        _grid.DataSource = _rows;
        _grid.SelectionChanged += (_, _) =>
        {
            if(_refreshing)return;
            foreach(DataGridViewRow row in _grid.Rows)if(row.DataBoundItem is GroupingCandidate c)c.ActionSelected=row.Selected;
            _grid.InvalidateColumn(0);UpdateDetail();
            // Don't jump the CAD view repeatedly during Ctrl/Shift selection.
            if(_grid.SelectedRows.Count==1 && Control.ModifierKeys==Keys.None)
                try{ZoomToCurrent();}catch(System.Exception ex){_summary.Text="Rescan: "+ex.Message;}
        };
        _grid.ToggleCheck+=index=>
        {
            var row=_grid.Rows[index];if(row.DataBoundItem is not GroupingCandidate c)return;
            _refreshing=true;
            c.ActionSelected=!c.ActionSelected;row.Selected=c.ActionSelected;
            _grid.InvalidateRow(index);_refreshing=false;UpdateDetail();
        };
        _grid.KeyDown+=(_,e)=>
        {
            if(e.Control&&e.KeyCode==Keys.A){SelectVisible(true);e.Handled=true;e.SuppressKeyPress=true;}
        };
    }

    private void SelectVisible(bool selected)
    {
        _refreshing=true;
        foreach(DataGridViewRow row in _grid.Rows)if(row.DataBoundItem is GroupingCandidate c){c.ActionSelected=selected;row.Selected=selected;}
        _grid.InvalidateColumn(0);_refreshing=false;
    }
    private List<GroupingCandidate> SelectedVisible()=>_rows.Where(c=>c.ActionSelected).ToList();

    private static bool CanGroup(GroupingCandidate c)=>
        (c.Status is CandidateStatus.Ready or CandidateStatus.Reviewed or CandidateStatus.Ambiguous or CandidateStatus.NoMatch or CandidateStatus.Conflict)
        && c.EligibleText
        && !c.ProposedBlockId.IsNull
        && !c.ExistingMembership;

    private static bool RequiresManualVerification(GroupingCandidate c)=>
        c.Status is CandidateStatus.Ambiguous or CandidateStatus.NoMatch or CandidateStatus.Conflict;

    private void PrepareGrouping()
    {
        var doc=Application.DocumentManager.MdiActiveDocument;
        if(doc==null||!PluginState.Matches(doc)||PluginState.LastScan==null)return;

        var selected=SelectedVisible();
        var chosen=selected.Where(CanGroup).ToHashSet();
        if(chosen.Count==0)
        {
            _summary.Text="Select a groupable relationship. Review/Conflict rows can be manually verified when they have an eligible proposed valve; existing/ignored rows and rows without a proposed valve cannot be grouped.";
            return;
        }
        if(chosen.Count!=selected.Count)
        {
            _summary.Text="Some selected rows cannot be grouped. Clear the selection and choose only Ready/Reviewed rows or Review/Conflict rows with an eligible proposed valve.";
            return;
        }

        var manualCount=chosen.Count(RequiresManualVerification);
        var message=manualCount>0
            ? $"Create {chosen.Count} selected group(s)?\n\n{manualCount} relationship(s) require manual verification and will be saved as \"Manually verified group\". Continue only if you have visually verified the proposed Location ID / valve relationship."
            : $"Create {chosen.Count} selected Ready/Reviewed group(s)?";

        var answer=MessageBox.Show(
            message,
            "CAD Sync Grouping",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if(answer!=DialogResult.Yes)return;

        foreach(var row in PluginState.LastScan.Candidates)row.Selected=chosen.Contains(row);

        int count;
        int verifiedCount;
        using(doc.LockDocument())
        {
            try
            {
                count=GroupWriter.CreateApprovedGroups(doc.Database,PluginState.LastScan,out verifiedCount);
            }
            catch(System.Exception ex)
            {
                _summary.Text="No groups created: "+ex.Message;
                return;
            }

            PluginState.SetScan(doc,DrawingScanner.Scan(doc.Database));
        }

        RefreshFromState();
        _summary.Text+=$"  Created {count} approved CAD Sync group(s).";
        if(verifiedCount>0)_summary.Text+=$"  {verifiedCount} saved as Manually verified group.";
    }

    public void RefreshFromState()
    {
        _grid.EndEdit();
        var previous=CurrentRow()?.TextId;
        _refreshing=true;
        _rows.RaiseListChangedEvents = false;
        _rows.Clear();

        var doc = Application.DocumentManager.MdiActiveDocument;
        var scan = doc != null && PluginState.Matches(doc) ? PluginState.LastScan : null;
        if (scan != null)
        {
            var allRows = scan.Candidates.AsEnumerable();
            var selectedStatus = _statusFilter.SelectedIndex > 0
                ? _statusFilter.SelectedItem?.ToString()
                : null;

            IEnumerable<GroupingCandidate> source;

            if (_view.SelectedIndex == 0)
            {
                // Results is the normal working view. With "All statuses"
                // it hides resolved/ignored rows. If the user explicitly chooses
                // a Status (including Existing group or Ignored), that explicit
                // status request takes precedence so the second-layer filter
                // never appears empty merely because the default view hid it.
                source = selectedStatus == null
                    ? allRows.Where(c => c.Status is not (CandidateStatus.Existing or CandidateStatus.ManuallyVerified or CandidateStatus.Accepted))
                    : allRows.Where(c => c.StatusText == selectedStatus);
            }
            else
            {
                source = _view.SelectedIndex switch
                {
                    1 => allRows.Where(c => c.Status is CandidateStatus.Ambiguous or CandidateStatus.NoMatch or CandidateStatus.Conflict),
                    2 => allRows.Where(c => c.Status is CandidateStatus.Existing or CandidateStatus.ManuallyVerified),
                    3 => allRows.Where(c => c.Status == CandidateStatus.Accepted),
                    // Show Everything intentionally leaves resolution state unfiltered.
                    4 => allRows,
                    _ => allRows
                };

                if (selectedStatus != null)
                    source = source.Where(c => c.StatusText == selectedStatus);
            }
            foreach(var filter in _filters.Where(f=>!string.IsNullOrWhiteSpace(f.Value.Text)))
                source=source.Where(c=>CellValue(c,filter.Key).Contains(filter.Value.Text.Trim(),StringComparison.OrdinalIgnoreCase));
            source=_descending?source.OrderByDescending(c=>CellValue(c,_sortProperty),NaturalTextComparer.Instance):source.OrderBy(c=>CellValue(c,_sortProperty),NaturalTextComparer.Instance);

            foreach (var row in source)
                _rows.Add(row);

            var ready = scan.Candidates.Count(c => c.Status is CandidateStatus.Ready or CandidateStatus.Reviewed);
            var review = scan.Candidates.Count(c => c.Status is CandidateStatus.Ambiguous or CandidateStatus.Conflict or CandidateStatus.NoMatch);
            var verified = scan.Candidates.Count(c => c.Status == CandidateStatus.ManuallyVerified);
            _summary.Text = $"Location text: {scan.LocationTextCount}   Valve blocks: {scan.ValveBlockCount}   Existing groups: {scan.ExistingValveGroups}   Manually verified: {verified}   Ready/reviewed: {ready}   Needs review: {review}";
        }
        else
        {
            _summary.Text = "Run GROUPSCAN to analyze the active drawing.";
        }

        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();
        foreach(DataGridViewColumn column in _grid.Columns)column.HeaderCell.SortGlyphDirection=column.DataPropertyName==_sortProperty?(_descending?SortOrder.Descending:SortOrder.Ascending):SortOrder.None;
        if(previous.HasValue)foreach(DataGridViewRow item in _grid.Rows)if(item.DataBoundItem is GroupingCandidate candidate && candidate.TextId==previous.Value){_grid.CurrentCell=item.Cells[1];break;}
        _grid.ClearSelection();
        foreach(DataGridViewRow item in _grid.Rows)if(item.DataBoundItem is GroupingCandidate candidate)item.Selected=candidate.ActionSelected;
        _refreshing=false;
        UpdateDetail();
    }

    private static string CellValue(GroupingCandidate c,string property)=>property switch
    {
        nameof(GroupingCandidate.LocationId)=>c.LocationId,
        nameof(GroupingCandidate.ProposedBlockName)=>c.ProposedBlockName,
        nameof(GroupingCandidate.StatusText)=>c.StatusText,
        _=>c.Reason
    };

    private void SetIgnored(bool ignore)
    {
        var doc=Application.DocumentManager.MdiActiveDocument;
        if(doc==null||!PluginState.Matches(doc))return;
        var requested=SelectedVisible().Where(c=>ignore?c.Status is not (CandidateStatus.Existing or CandidateStatus.ManuallyVerified or CandidateStatus.Accepted):c.Status==CandidateStatus.Accepted).ToList();
        if(requested.Count==0){_summary.Text=ignore?"Select ungrouped rows to ignore.":"Select ignored rows to reopen.";return;}
        using(doc.LockDocument())
        {
            var fresh=DrawingScanner.Scan(doc.Database);
            var byText=fresh.Candidates.ToDictionary(c=>c.TextId);
            var current=new List<GroupingCandidate>();
            foreach(var row in requested)
            {
                if(!byText.TryGetValue(row.TextId,out var match)||match.ReviewFingerprint!=row.ReviewFingerprint)
                {_summary.Text="A selected relationship changed. Nothing changed; rescan and review the selection.";return;}
                current.Add(match);
            }
            ReviewDecisions.SaveBatch(doc.Database,current,ignore);
            PluginState.SetScan(doc,DrawingScanner.Scan(doc.Database));
        }
        RefreshFromState();
        _summary.Text+=$"  {requested.Count} row(s) "+(ignore?"ignored":"reopened")+"; save the DWG to retain this.";
    }

    private void UpdateDetail()
    {
        var row=CurrentRow();
        _detail.Text=row==null?"Select a relationship to inspect its valve and Location ID.":
            $"{row.Equipment.Type}  |  Equipment: {row.Equipment.Status}\n{row.Equipment.EffectiveName}  (reference: {row.Equipment.ActualName})\nLocation ID: {row.LocationId}   |   Association: {row.StatusText}\n{row.Equipment.Evidence}\n{row.Reason}";
    }

    private async void Rescan()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) return;

        try
        {
            // Palette button callbacks are outside a normal AutoCAD command
            // context. Enter one briefly so the optional AutoLISP macro can run
            // before the scan. Missing RENAMEBLOCKS remains completely silent.
            await OptionalMacros.TryRunRenameBlocksInCommandContextSilently();

            if (!ReferenceEquals(Application.DocumentManager.MdiActiveDocument, doc))
                return;

            using (doc.LockDocument())
            {
                var scan = DrawingScanner.Scan(doc.Database);
                PluginState.SetScan(doc, scan);
            }
            RefreshFromState();
        }
        catch(System.Exception ex)
        {
            _summary.Text=ex.Message;
        }
    }

    private GroupingCandidate? CurrentRow()
    {
        var selected=SelectedVisible();
        return selected.Count==1?selected[0]:null;
    }

    private void ZoomToCurrent()
    {
        var row = CurrentRow();
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (row == null || doc == null || !PluginState.Matches(doc)) return;

        var ids = new List<ObjectId>(row.LeaderIds);
        if (!row.TextId.IsNull) ids.Add(row.TextId);

        if (!row.ProposedBlockId.IsNull)
            ids.Add(row.ProposedBlockId);

        if (ids.Count == 0) return;

        using (doc.LockDocument())
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            var entities = ids.Distinct().Where(id=>id.IsValid&&!id.IsErased&&PluginState.SameDatabase(id.Database,doc.Database))
                .Select(id => tr.GetObject(id, OpenMode.ForRead, false) as Entity)
                .Where(e => e != null)
                .Cast<Entity>()
                .ToList();

            if (entities.Count == 0) return;
            var ext = GeometryHelper.UnionExtents(entities);
            var width = Math.Max(ext.MaxPoint.X - ext.MinPoint.X, 0.5);
            var height = Math.Max(ext.MaxPoint.Y - ext.MinPoint.Y, 0.5);
            var center = new Point2d((ext.MinPoint.X + ext.MaxPoint.X) * 0.5, (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5);

            using var view = doc.Editor.GetCurrentView();
            var worldToDisplay=Matrix3d.PlaneToWorld(view.ViewDirection);
            worldToDisplay=Matrix3d.Displacement(view.Target-Point3d.Origin)*worldToDisplay;
            worldToDisplay=Matrix3d.Rotation(-view.ViewTwist,view.ViewDirection,view.Target)*worldToDisplay;
            ext.TransformBy(worldToDisplay.Inverse());
            width=Math.Max(ext.MaxPoint.X-ext.MinPoint.X,0.5);
            height=Math.Max(ext.MaxPoint.Y-ext.MinPoint.Y,0.5);
            center=new Point2d((ext.MinPoint.X+ext.MaxPoint.X)*0.5,(ext.MinPoint.Y+ext.MaxPoint.Y)*0.5);
            var aspect=view.Width/Math.Max(view.Height,1e-9);
            width=Math.Max(width,height*aspect); height=Math.Max(height,width/aspect);
            view.CenterPoint = center;
            view.Width = width * 3.0;
            view.Height = height * 3.0;
            doc.Editor.SetCurrentView(view);
            doc.Editor.SetImpliedSelection(entities.Select(e=>e.ObjectId).ToArray());
            tr.Commit();
        }
    }
}
