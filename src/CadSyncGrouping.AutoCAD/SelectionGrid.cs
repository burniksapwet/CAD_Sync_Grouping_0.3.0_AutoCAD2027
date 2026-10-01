using System.Windows.Forms;
namespace CadSyncGrouping;

internal sealed class SelectionGrid : DataGridView
{
    internal event Action<int>? ToggleCheck;
    protected override void OnMouseDown(MouseEventArgs e)
    {
        var hit=HitTest(e.X,e.Y);
        // Checkbox clicks toggle one selection without clearing other selected rows.
        if(hit.ColumnIndex==0 && hit.RowIndex>=0 && e.Button==MouseButtons.Left)
        {Focus();ToggleCheck?.Invoke(hit.RowIndex);return;}
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        var hit=HitTest(e.X,e.Y);
        if(hit.ColumnIndex==0 && hit.RowIndex>=0 && e.Button==MouseButtons.Left)return;
        base.OnMouseUp(e);
    }
}
