# Find and Replace Block 0.2.5

## What was wrong in 0.2.4

The delivered source defined OnImpliedSelectionChanged and a detach handler, but OpenFor never attached the drawing-selection event. The earlier patch silently missed that insertion. Manual drawing picks therefore did not automatically refresh the Change buttons.

ChangeSelectedBlock also preferred SelectedSummary/SelectedSourceIds over the current drawing selection. A leftover finder row could select the wrong replacement source set. A queued grid-selection timer could overwrite newer manual picks. These are confirmed source defects; the screenshot alone does not prove why its extra/offset symbol appeared.

## This update

- Drawing-selection events are attached on OpenFor, rebound on document activation, and removed on document close/control disposal.
- Manual picks of mixed block types activate both Change buttons without a finder row. Finder name/layer filters do not restrict manual picks.
- Selecting a finder row still selects that row's matching block instances. A subsequent manual drawing selection cancels queued grid updates and takes precedence.
- At replacement time, the current drawing selection is captured once. Only selected local Model Space block references are sent to the replacement engine. Non-block objects are not converted.
- The source snapshot cannot be changed by target-dialog focus, preview highlighting, or delayed selection events. Cancel/failure restores the original selection; success selects replacements plus originally selected unchanged objects.
- CHANGEBLOCKSCALE, CHANGEBLOCKSTANDARD, and FINDANDREPLACEBLOCK retain pick sets through their read operations using the Redraw command flag.
- The replacement dialog now uses AutoCAD's supported modal-dialog API. Contains-search suggestions are guarded against selection/rebuild reentrancy.
- The window title displays 0.2.5. The 785 x 890 first-show sizing and same-session manual sizing are unchanged.

## Intentionally unchanged

The CreatePreviewReplacement method and every method following it are text-compared against the prior staged source. Scale correction, Standard 1:1 handling, orientation, mirroring, graphical center alignment, dynamic-state mapping, attribute copying, and group transfer calculations are unchanged. This repair fixes source selection rather than guessing new geometry calculations from a screenshot.

## Verification and limits

The build compiles the actual plugin against AutoCAD.NET 26.0.0. A separate Windows test harness links the actual selection control, target dialog, scanner and record types, using explicit host shims instead of native AutoCAD. It checks real WinForms events and source-set routing. RegressionResults.txt records the result; these tests do not load AutoCAD or validate native drawing geometry.

Native AutoCAD checks still required on a COPY of a drawing:
1. Open the palette with no selected finder row. Select three mixed block types in the drawing; both buttons should activate and the status line should report three selected blocks and their type count.
2. Select a finder row, then manually select a smaller or different set. Only the new manual set should be passed to replacement. Try both Scale and Standard.
3. Cancel at the target dialog and at the replacement confirmation; no preview entities should remain after cancellation.
4. Verify position, orientation and intended size in the preview before confirming. Existing originals intentionally remain visible until confirmation, as in prior builds.
5. Press Escape to clear drawing selection; both buttons should disable. Switch drawings and verify old selections never cross into the new drawing.

Install by fully closing AutoCAD and replacing FindAndReplaceBlock.bundle with this bundle. Do not keep older copies of the same plugin in auto-loaded folders. The DLL is CadSyncFindAndReplaceBlock2027V025.dll.
