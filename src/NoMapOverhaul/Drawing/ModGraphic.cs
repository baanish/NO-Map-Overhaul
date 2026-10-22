using NoMapOverhaul.Diagnostics;
using UnityEngine.UI;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// The base of every mesh the mod builds itself. Unity rebuilds a graphic's mesh in its canvas update, outside the
/// mod's frame, so this times each rebuild for the perf test's Meshes column. TextMeshPro's own rebuilds aren't in it.
/// </summary>
internal abstract class ModGraphic : MaskableGraphic
{
    protected override void UpdateGeometry()
    {
        var start = ModTimings.Start();
        ModActivity.MeshRebuilds++;
        base.UpdateGeometry();
        ModTimings.Stop(ModSection.MeshRebuilds, start);
    }

    /// <summary>
    /// Fits the rect around the mesh, writing only what changed: Unity counts any transform write as a change that
    /// re-batches the canvas, even one that sets the same value.
    /// </summary>
    protected void FitRect(UnityEngine.Vector2 center, UnityEngine.Vector2 size)
    {
        var rect = rectTransform;
        if ((UnityEngine.Vector2)rect.localPosition != center)
        {
            rect.localPosition = center;
        }

        if (rect.sizeDelta != size)
        {
            rect.sizeDelta = size;
        }
    }
}
