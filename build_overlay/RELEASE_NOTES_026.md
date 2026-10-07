# Find and Replace Block 0.2.6

This update intentionally changes only the replacement target library behavior.

- Replacement targets still include every eligible named local block definition already loaded in the active drawing.
- The target list now also includes every top-level .DWG file in:
  %APPDATA%\Autodesk\_DMG_Folders\Work ToolPalettte
- Subfolders are intentionally ignored.
- The current drawing's definition wins when the same block name also exists as a library DWG.
- If a chosen target exists only as a library DWG, it is imported into the active drawing on demand before replacement.
- If the ToolPalettte folder is missing or inaccessible, Find and Replace continues using drawing definitions only.
- Existing 0.2.5 manual-selection, Scale, Standard 1:1, preview, group-transfer, and target-search behavior is retained.

The separate RENAMEBLOCK-on-open/Refresh request is not included in this version so changes can be tested one at a time.
