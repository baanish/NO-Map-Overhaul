using BaanishUiImprovements.Diagnostics;
using UnityEngine.UI;

namespace BaanishUiImprovements.Drawing;

/// <summary>
/// The base of every mesh the mod builds itself. Unity rebuilds a graphic's mesh in its canvas update, outside the
/// mod's frame, so this times each rebuild for the perf test's Meshes column. TextMeshPro's own rebuilds aren't in it.
/// </summary>
internal abstract class ModGraphic : MaskableGraphic
{
    protected override void UpdateGeometry()
    {
        var start = ModTimings.Start();
        base.UpdateGeometry();
        ModTimings.Stop(ModSection.MeshRebuilds, start);
    }
}
