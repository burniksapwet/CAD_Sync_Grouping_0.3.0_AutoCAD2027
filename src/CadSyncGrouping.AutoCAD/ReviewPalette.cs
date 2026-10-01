using Autodesk.AutoCAD.Windows;
using System.Drawing;

namespace CadSyncGrouping;

internal static class ReviewPalette
{
    private static PaletteSet? _palette;
    private static ReviewControl? _control;

    public static void Refresh()=>_control?.RefreshFromState();
    public static void Show()
    {
        if (_palette == null)
        {
            _control = new ReviewControl();
            _palette = new PaletteSet("CAD Sync Grouping")
            {
                Size = new Size(785, 890),
                MinimumSize = new Size(760, 420),
                DockEnabled = DockSides.Left | DockSides.Right,
                Style = PaletteSetStyles.ShowAutoHideButton |
                        PaletteSetStyles.ShowCloseButton |
                        PaletteSetStyles.ShowPropertiesMenu
            };
            _palette.Add("Relationships", _control);
        }

        _control!.RefreshFromState();
        _palette.Visible = true;
    }
}
