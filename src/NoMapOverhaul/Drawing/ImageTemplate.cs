using UnityEngine.UI;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// An Image that is never drawn, kept switched off, whose mesh an <see cref="ImageBatch"/> copies: Image's own geometry
/// for its sprite, type, and rect, so the copies look exactly like the Image would.
/// </summary>
internal sealed class ImageTemplate : Image
{
    /// <summary>Image's mesh for this one's sprite and rect, in its own local space.</summary>
    public void Generate(VertexHelper vh) => OnPopulateMesh(vh);
}
