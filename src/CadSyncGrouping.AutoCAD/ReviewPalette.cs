using Autodesk.AutoCAD.Windows;
using System.Drawing;

namespace CadSyncGrouping;

internal static class ReviewPalette
{
    private static PaletteSet? _palette;
    private static ReviewControl? _control;
    private static bool _initialSizeApplied;
    private static System.Windows.Forms.Timer? _initialSizeTimer;
    private static readonly Size InitialSize = new(785, 890);

    public static void Refresh()=>_control?.RefreshFromState();
    public static void Show()
    {
        var reopening = _palette == null || !_palette.Visible;

        if (_palette == null)
        {
            _control = new ReviewControl();
            _palette = new PaletteSet("CAD Sync Grouping")
            {
                Size = InitialSize,
                MinimumSize = new Size(760, 420),
                DockEnabled = DockSides.Left | DockSides.Right,
                Style = PaletteSetStyles.ShowAutoHideButton |
                        PaletteSetStyles.ShowCloseButton |
                        PaletteSetStyles.ShowPropertiesMenu
            };
            _palette.Add("Relationships", _control);
        }

        if (reopening)
            _control!.ResetForOpen();
        else
            _control!.RefreshFromState();

        _palette.Visible = true;

        // AutoCAD can re-layout a newly shown floating PaletteSet after the
        // constructor Size has been applied. Force the requested size once,
        // after the first show has entered the Windows message loop.
        // We never touch the size again during this AutoCAD session, so any
        // manual resize is retained until AutoCAD is closed.
        if (!_initialSizeApplied)
        {
            _initialSizeApplied = true;
            _initialSizeTimer?.Stop();
            _initialSizeTimer?.Dispose();
            _initialSizeTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _initialSizeTimer.Tick += (_, _) =>
            {
                _initialSizeTimer?.Stop();
                _initialSizeTimer?.Dispose();
                _initialSizeTimer = null;
                if (_palette != null && _palette.Visible)
                    _palette.Size = InitialSize;
            };
            _initialSizeTimer.Start();
        }
    }
}
