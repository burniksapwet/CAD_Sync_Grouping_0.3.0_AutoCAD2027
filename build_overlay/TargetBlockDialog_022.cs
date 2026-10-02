using System.Drawing;
using System.Windows.Forms;

namespace CadSyncFindAndReplaceBlock;

internal sealed class TargetBlockDialog : Form
{
    private readonly ComboBox _target = new()
    {
        Dock = DockStyle.Top,
        DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.None,
        AutoCompleteSource = AutoCompleteSource.None,
        MaxDropDownItems = 16
    };

    private readonly List<string> _allNames;
    private bool _updatingChoices;

    public string TargetName => _target.Text.Trim();

    public TargetBlockDialog(string sourceName, IEnumerable<string> availableNames)
    {
        Text = "Change Block";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(450, 155);

        _allNames = availableNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Where(n => !string.Equals(n, sourceName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        RebuildChoices(string.Empty, openDropDown: false);

        var label = new Label
        {
            Text = $"Change '{sourceName}' to:",
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(0, 4, 0, 0)
        };

        var note = new Label
        {
            Text = "Type any part of a block name (for example BALL) to show every matching named local block in this drawing.",
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(0, 8, 0, 0)
        };

        var ok = new Button { Text = "Change", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 42,
            Padding = new Padding(0, 7, 4, 0)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        panel.Controls.Add(_target);
        panel.Controls.Add(note);
        panel.Controls.Add(label);
        Controls.Add(panel);
        Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;

        // Windows ComboBox autocomplete only matches from the beginning of the
        // string. Replace it with a contains search so typing BALL can surface
        // BALL VALVE, CHECK BALL VALVE, BASKETBALL, etc.
        _target.TextUpdate += (_, _) => RebuildChoices(_target.Text, openDropDown: true);
        _target.DropDown += (_, _) =>
        {
            if (!_updatingChoices)
                RebuildChoices(_target.Text, openDropDown: false);
        };

        Shown += (_, _) => _target.Focus();
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && string.IsNullOrWhiteSpace(TargetName))
            {
                MessageBox.Show(this, "Enter or choose a target block name.", "Change Block",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.Cancel = true;
            }
        };
    }

    private void RebuildChoices(string text, bool openDropDown)
    {
        if (_updatingChoices) return;

        var typed = text ?? string.Empty;
        var caret = Math.Min(_target.SelectionStart, typed.Length);
        var query = typed.Trim();

        IEnumerable<string> matches = _allNames;
        if (!string.IsNullOrWhiteSpace(query))
        {
            matches = matches
                .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(name => name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase);
        }

        var choices = matches.ToList();

        _updatingChoices = true;
        try
        {
            _target.BeginUpdate();
            _target.Items.Clear();
            foreach (var name in choices)
                _target.Items.Add(name);
            _target.EndUpdate();

            // Replacing Items can disturb the edit text/caret. Put the user's
            // exact text back; selecting a suggestion still works normally.
            _target.Text = typed;
            _target.SelectionStart = Math.Min(caret, _target.Text.Length);
            _target.SelectionLength = 0;
        }
        finally
        {
            _updatingChoices = false;
        }

        if (openDropDown && !string.IsNullOrWhiteSpace(query))
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                _updatingChoices = true;
                try
                {
                    _target.DroppedDown = choices.Count > 0;
                    _target.Text = typed;
                    _target.SelectionStart = Math.Min(caret, _target.Text.Length);
                    _target.SelectionLength = 0;
                }
                finally
                {
                    _updatingChoices = false;
                }
            }));
        }
    }
}
