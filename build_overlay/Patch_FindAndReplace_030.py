from pathlib import Path
import shutil, sys
base=Path(sys.argv[1])
root=base/'src/CadSyncFindAndReplaceBlock'
overlay=Path(__file__).parent/'geometry030'
for name in ['DirectionalSymbolGeometry.cs','DirectionalSymbolReader.cs']:
    shutil.copy2(overlay/name,root/name)
p=root/'DirectionalSymbolGeometry.cs'
p.write_text(p.read_text().replace('Y*0+Z*b.X-X*b.Z','Z*b.X-X*b.Z'))
shutil.copytree(overlay/'tests',base/'geometry_tests',dirs_exist_ok=True)
p=root/'BlockReplacementEngine.cs'
s=p.read_text()
before=s
start='        var targetBounds = replacement.GeometryExtentsBestFit();'
end='        CopyAttributes(database, tr, source, definition, replacement);'
assert s.count(start)==1 and s.count(end)==1
first=s.index(start); last=s.index(end,first)
new='''        var targetBounds = replacement.GeometryExtentsBestFit();

        // A tip/junction takes priority over bounding-box center for comparable
        // simple connection symbols. Recognition is geometric, never name-based.
        var hasSourceSymbol = DirectionalSymbolReader.TryRead(source, tr, out var sourceSymbol);
        var hasTargetSymbol = DirectionalSymbolReader.TryRead(replacement, tr, out var targetSymbol);
        double facingCorrection = 0;
        var useTip = hasSourceSymbol && hasTargetSymbol &&
            DirectionalSymbolGeometry.TryMatch(sourceSymbol!, targetSymbol!,
                new SymbolPoint(source.Normal.X, source.Normal.Y, source.Normal.Z), out facingCorrection);
        var sourceTip = useTip ? new Point3d(sourceSymbol!.Tip.X, sourceSymbol.Tip.Y, sourceSymbol.Tip.Z) : Point3d.Origin;
        var targetTip = useTip ? new Point3d(targetSymbol!.Tip.X, targetSymbol.Tip.Y, targetSymbol.Tip.Z) : Point3d.Origin;

        if (useTip && Math.Abs(facingCorrection) > 1e-10)
        {
            replacement.TransformBy(Matrix3d.Rotation(facingCorrection, source.Normal, targetTip));
            targetBounds = replacement.GeometryExtentsBestFit();
        }

        if (mode == ReplacementMode.Scale)
        {
            // Keep the established visual-size calculation. Only its pivot
            // changes for recognized symbols so their connection cannot drift.
            var visualScaleCorrection = CalculateVisualScaleCorrection(sourceBounds, targetBounds, source.Handle);
            if (Math.Abs(visualScaleCorrection - 1.0) > 1e-6)
            {
                var pivot = useTip ? targetTip : Center(targetBounds);
                replacement.TransformBy(Matrix3d.Scaling(visualScaleCorrection, pivot));
                targetBounds = replacement.GeometryExtentsBestFit();
            }
        }

        // Rotation and scaling above leave targetTip fixed. Standard mode keeps
        // absolute 1:1 scale, and both modes preserve the source's mirror parity.
        // Other shapes retain the prior center-based basepoint compensation.
        replacement.TransformBy(Matrix3d.Displacement(useTip
            ? sourceTip - targetTip
            : Center(sourceBounds) - Center(targetBounds)));

'''
s=s[:first]+new+s[last:]
s=s.replace('while retaining rotation, layer, compatible attributes/dynamic state, group membership and center alignment.', 'while retaining layer, compatible attributes/dynamic state and group membership; comparable connection symbols retain their visible direction and tip alignment.')
s=s.replace('Scale mode preserves rotation, layer, compatible attributes/dynamic state, group membership and visual-size handling from CAD Block Replace 0.4.5.1.', 'Scale mode preserves layer, compatible attributes/dynamic state, group membership and visual size; comparable connection symbols retain their visible direction and tip alignment.')
marker='    private static void CopyDynamicPropertiesWithStateMatching('
assert s[s.index(marker):]==before[before.index(marker):], 'Dynamic matching, visual scale, attributes or group transfer unexpectedly changed'
p.write_text(s)
for p in [root/'AssemblyInfo.cs',root/'CadSyncFindAndReplaceBlock.csproj',base/'bundle/FindAndReplaceBlock.bundle/PackageContents.xml',root/'FindAndReplacePalette.cs']:
    s=p.read_text()
    assert '0.2.9' in s, str(p)
    p.write_text(s.replace('0.2.9','0.2.10').replace('V029','V0210'))
p=root/'FindAndReplaceControl.cs';s=p.read_text()
p.write_text(s.replace('Find and Replace Block 0.2.5:', 'Find and Replace Block 0.2.10:'))
print('0.2.10 source checks PASS: qualified geometric tip/facing placement; unchanged scale formula, dynamic mapping, attribute and group handling; existing selection, library, silent rename and 785x890 sizing retained.')
