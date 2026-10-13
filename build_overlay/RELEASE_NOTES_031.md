# Find and Replace Block 0.2.11

## Select Block

The Change Block dialog now has this button order:

Select Block | Change | Cancel

Select Block temporarily hands focus to AutoCAD so a replacement block can be picked by clicking or by window/crossing selection. Only the first usable block in AutoCAD's returned selection order supplies the target definition name; there is no leftmost, nearest, alphabetical, or lowest-handle rule. Non-block objects are filtered out. Xrefs and unsupported anonymous definitions are not accepted. Dynamic block references resolve to their named authoring definition, not an anonymous *U name.

The same dialog returns with the picked name populated. The user must still press Change to enter the existing replacement/preview flow. The example target instance is only read and is not modified. Its instance rotation, scale, location, attributes, and dynamic state are not copied; picking is another way to choose the same definition that could be chosen by name.

Esc during picking leaves the previous target text unchanged and restores the original source selection. Cancel in the dialog still cancels replacement. Source object IDs remain a fixed private snapshot while the separate target pick takes place, including when replacing mixed source block types. Repeated picks change only the proposed target.

## Preserved behavior

The v0.2.10 placement engine, directional geometry matcher/reader, block scanner, block record model, and command definitions are byte-identical before and after this patch. This preserves the Sensor/Sensor Wash Spray and G1ARRI08/Piping Arrow Head corrections and the established Scale/Standard, attribute, dynamic-property and group-handling paths. The picker does not add another command or change the palette layout, 785 x 890 session sizing, All Layers reopen default, ToolPalettte library, or silent rename behavior.

## Validation

The release workflow compiles the real plugin against AutoCAD.NET 26.0.0. It runs the existing Windows UI/selection harness, new target-pick tests using explicit AutoCAD host shims, and the unchanged 54 pure-geometry cases. Test output is included in RegressionResults.txt. The dialog image in the build artifact is a Windows Forms test-host capture, not a native AutoCAD screenshot.

Native AutoCAD is not executed by these tests. The final mouse/window selection and focus restoration should be checked in AutoCAD after installation.

## Install and test

Fully close AutoCAD and replace the existing FindAndReplaceBlock.bundle with this bundle. Do not keep two versions of this bundle active. Reopen AutoCAD; the palette should show Find and Replace Block 0.2.11.

On a copy of a drawing, select the source block(s), open Change Block (Scale) or Change Block (Standard), click Select Block, and click or window-select the example replacement block. Confirm that its name appears and nothing changes until Change is pressed. Also test a window containing multiple block types, Esc from a pick, and Cancel from the dialog. Only the original selected sources should be replaced; the example block must remain untouched.

No GROUPSCAN or LSP/FAS update is required for this release.
