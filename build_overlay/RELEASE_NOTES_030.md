# Find and Replace Block 0.2.10

## Findings from drawing2.dwg

The original supplied DWG was decoded locally with LibreDWG 0.13.3. Relevant LINE, SOLID, LWPOLYLINE and HATCH coordinates were cross-checked against its decoded DXF. Some unrelated legacy/custom classes emitted DXF conversion warnings, so that conversion was used for inspection only, not to rewrite the user's DWG. The full drawing has not been added to this repository.

### Sensor -> Sensor Wash Spray

Both symbols have their actual connection/junction at (0,0,0). However, the two source arms end at (0.15,-0.08414999485016) and (0.15,+0.06374999609858), while the target arms end at (0.15,+/-0.08414999485016). The source is slightly asymmetric; the target is symmetric.

The old bounding-box scaling/centering reproduces the saved Scale-mode error: scale 0.937436866561 and a tip offset of approximately (+0.004692235008,-0.010199999376) for the unrotated example. The mirrored-facing example has the opposite displacement. The algorithm was matching rectangle centers, not preserving the connection point.

### G1ARRI08 -> Piping Arrow Head

The source is a triangular SOLID extending in +X from its tip at the origin. The target's triangle is drawn in -X from its tip, using a polyline boundary plus a SOLID hatch. Their authored directions are opposite. The existing instance rotation of 270 degrees was copied unchanged, which reversed the visible arrow. Old centering also moved the tip approximately -0.178774289364 in Y, matching the saved replacement in the supplied drawing.

## Correction

- Recognize comparable simple open chevrons and unambiguous triangular arrows from their actual primitive geometry, including repeated SOLID corners and duplicate hatch/outline boundaries.
- Use the shared chevron junction or arrow tip as the alignment point.
- Determine facing from the base/mouth normal pointing toward the tip. This avoids introducing a false small rotation into an asymmetric sensor.
- Match actual visible direction before applying the existing visual-size correction; scaling occurs about the tip so it does not drift.
- Standard mode remains absolute 1:1. Corrections use only rotation, positive uniform scaling in Scale mode, and translation; they do not add reflections.
- No block names, specific drawing positions, or fixed 180-degree rule are used to recognize or place symbols.
- Non-comparable, complex, attribute-bearing, curved or directionally ambiguous symbols retain the prior placement path. Dynamic-property matching, attributes, group transfer and the numeric visual-size formula are unchanged.
- Existing mixed manual selection, fixed source snapshots, All Blocks/All Layers, reopen defaults, 785 x 890 session sizing, ToolPalettte library and silent rename integration remain.

## Validation scope

The build compiles the actual plugin against AutoCAD.NET 26.0.0, runs the existing Windows selection/UI regression harness with explicit AutoCAD shims, and runs the actual pure geometry matcher against the primitive fixture from drawing2.dwg plus rotated, mirrored, shifted-basepoint and degenerate synthetic cases. The two old-placement reproductions are compared against the measured scales and offsets in the supplied DWG.

Native AutoCAD is NOT run by these tests. The native adapter is compiled against Autodesk's API, but native dynamic-block evaluation and preview behavior still require a check inside AutoCAD.

## Install and check

Close AutoCAD, replace FindAndReplaceBlock.bundle, then reopen AutoCAD. The palette title is Find and Replace Block 0.2.10 and the DLL is CadSyncFindAndReplaceBlock2027V0210.dll. No GROUPSCAN update is included.

Test a COPY of drawing2.dwg. Select the original Sensor examples and replace with Sensor Wash Spray. Then select the original G1ARRI08 examples and replace with Piping Arrow Head. Check both Scale and Standard, and confirm the preview only when the tips remain at their original pipe connections and the arrows still face in the source direction. Cancel must retain the originals.

This update does not retroactively relocate replacements already saved in incorrect positions. Start from the original source instances when retesting.
