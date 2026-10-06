using System.Drawing;
using System.Windows.Forms;

namespace CadSyncFindAndReplaceBlock;

internal sealed class TargetBlockDialog : Form
{
    private readonly TextBox _target = new() { Dock = DockStyle.Fill };
    private readonly Button _drop = new() { Text = "▼", Dock = DockStyle.Right, Width = 30, TabStop = false };
    private readonly ListBox _suggestions = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        Visible = false,
        Cursor = Cursors.Default
    };
    private readonly Label _matchMessage = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Visible = false
    };
    private readonly Button _ok = new() { Text = "Change", DialogResult = DialogResult.OK, AutoSize = true, Enabled = false };
    private readonly List<string> _allNames;
    private readonly TableLayoutPanel _layout = new();
    private bool _expanded;

    public string TargetName
    {
        get
        {
            var typed = _target.Text.Trim();
            return _allNames.FirstOrDefault(n => string.Equals(n, typed, StringComparison.OrdinalIgnoreCase)) ?? typed;
        }
    }

    public TargetBlockDialog(string sourceName, IEnumerable<string> availableNames)
    {
        Text = "Change Block";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(470, 170);

        _allNames = availableNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Where(n => !string.Equals(n, sourceName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var label = new Label
        {
            Text = $"Change '{sourceName}' to:",
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        var note = new Label
        {
            Text = "Type any part of a block name (for example BALL). Matching names appear below; names beginning with the text are listed first.",
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        var input = new Panel { Dock = DockStyle.Fill, Height = 26 };
        input.Controls.Add(_target);
        input.Controls.Add(_drop);

        var suggestionHost = new Panel { Dock = DockStyle.Fill };
        suggestionHost.Controls.Add(_suggestions);
        suggestionHost.Controls.Add(_matchMessage);

        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_ok);

        _layout.Dock = DockStyle.Fill;
        _layout.Padding = new Padding(12);
        _layout.ColumnCount = 1;
        _layout.RowCount = 5;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.Controls.Add(label, 0, 0);
        _layout.Controls.Add(note, 0, 1);
        _layout.Controls.Add(input, 0, 2);
        _layout.Controls.Add(suggestionHost, 0, 3);
        _layout.Controls.Add(buttons, 0, 4);
        Controls.Add(_layout);

        AcceptButton = _ok;
        CancelButton = cancel;

        _target.TextChanged += (_, _) =>
        {
            UpdateOkState();
            if (string.IsNullOrWhiteSpace(_target.Text))
                CollapseSuggestions();
            else
                ShowMatches(showAll: false);
        };

        _target.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _expanded && _suggestions.Items.Count > 0)
            {
                _suggestions.Focus();
                if (_suggestions.SelectedIndex < 0) _suggestions.SelectedIndex = 0;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape && _expanded)
            {
                CollapseSuggestions();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        _drop.Click += (_, _) =>
        {
            if (_expanded)
                CollapseSuggestions();
            else
                ShowMatches(showAll: string.IsNullOrWhiteSpace(_target.Text));
            _target.Focus();
        };

        _suggestions.SelectedIndexChanged += (_, _) =>
        {
            if (_suggestions.SelectedItem is string name)
            {
                _target.Text = name;
                _target.SelectionStart = _target.Text.Length;
                _target.SelectionLength = 0;
                UpdateOkState();
            }
        };

        _suggestions.MouseClick += (_, _) =>
        {
            if (_suggestions.SelectedItem is string)
            {
                // Keep the list visible long enough for the click to feel normal,
                // then return focus to the editable name field.
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed) _target.Focus();
                }));
            }
        };

        _suggestions.DoubleClick += (_, _) =>
        {
            if (_suggestions.SelectedItem is string && _ok.Enabled)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        _suggestions.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _suggestions.SelectedItem is string && _ok.Enabled)
            {
                DialogResult = DialogResult.OK;
                Close();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                CollapseSuggestions();
                _target.Focus();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        // Use our own in-form results list instead of the native ComboBox
        // drop-down. This avoids the zero-item index exception and keeps a
        // normal arrow cursor over selectable results.
        _suggestions.MouseEnter += (_, _) => Cursor.Current = Cursors.Default;
        _suggestions.MouseMove += (_, _) => Cursor.Current = Cursors.Default;

        Shown += (_, _) =>
        {
            _target.Focus();
            _target.SelectionStart = _target.Text.Length;
        };

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;

            var exact = _allNames.FirstOrDefault(n =>
                string.Equals(n, _target.Text.Trim(), StringComparison.OrdinalIgnoreCase));

            if (exact == null)
            {
                var typed = _target.Text.Trim();
                MessageBox.Show(
                    this,
                    string.IsNullOrWhiteSpace(typed)
                        ? "Choose or type an existing target block name."
                        : $"No block named '{typed}' exists in this drawing. Choose one of the matching block names.",
                    "Change Block",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                e.Cancel = true;
                _target.Focus();
            }
            else
            {
                _target.Text = exact;
            }
        };
    }

    private void UpdateOkState()
    {
        var typed = _target.Text.Trim();
        _ok.Enabled = _allNames.Any(n => string.Equals(n, typed, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowMatches(bool showAll)
    {
        var query = _target.Text.Trim();

        IEnumerable<string> matches = _allNames;
        if (!showAll && !string.IsNullOrWhiteSpace(query))
        {
            matches = matches
                .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(name => name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase);
        }

        var choices = matches.ToList();

        _suggestions.BeginUpdate();
        _suggestions.Items.Clear();
        foreach (var name in choices) _suggestions.Items.Add(name);
        _suggestions.EndUpdate();

        if (choices.Count == 0)
        {
            _suggestions.Visible = false;
            _matchMessage.Text = $"No block names contain '{query}'.";
            _matchMessage.Visible = true;
        }
        else
        {
            _matchMessage.Visible = false;
            _suggestions.Visible = true;
        }

        ExpandSuggestions();
    }

    private void ExpandSuggestions()
    {
        if (_expanded) return;
        _expanded = true;
        _layout.RowStyles[3].Height = 150;
        ClientSize = new Size(ClientSize.Width, 320);
        Cursor.Current = Cursors.Default;
    }

    private void CollapseSuggestions()
    {
        if (!_expanded) return;
        _expanded = false;
        _suggestions.Visible = false;
        _matchMessage.Visible = false;
        _layout.RowStyles[3].Height = 0;
        ClientSize = new Size(ClientSize.Width, 170);
    }
}
