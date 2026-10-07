# Find and Replace Block 0.2.7

This update addresses the two requested UI/workflow changes on top of the working 0.2.6 baseline.

- Uses the actual optional AutoLISP command name RENAMEBLOCKS (plural).
- FINDANDREPLACEBLOCK attempts RENAMEBLOCKS immediately before opening/refeshing the palette.
- Pressing the palette's Refresh button also attempts RENAMEBLOCKS first, then performs the normal refresh.
- If RENAMEBLOCKS is not loaded/available, the failure is swallowed silently: no plugin popup, no status warning, and regular Refresh continues.
- The layer selector now starts with:
  1. All Blocks
  2. All Layers
  3. actual drawing layers
- All Blocks shows the combined replacement-name catalog from the active drawing plus top-level .DWG files in:
  %APPDATA%\Autodesk\_DMG_Folders\Work ToolPalettte
- All Layers preserves the previous behavior of showing block instances across all drawing layers.
- The old <All Layers> angle brackets were removed; they had only been a visual convention indicating a synthetic filter item.
- Names that are available as targets but have no instances in the current scope display Count 0 / Type Available and do not clear a manual CAD selection when clicked.
- 0.2.6 library behavior remains: subfolders are ignored, current-drawing definitions win duplicate names, and library-only targets import on demand.
