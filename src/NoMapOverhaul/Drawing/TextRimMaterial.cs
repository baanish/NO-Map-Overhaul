using TMPro;
using UnityEngine;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// The material a text rim draws with. The game's HUD text, whose look map text copies, uses the font's additive material
/// ("TextMeshPro/Distance Field Additive"), which can only brighten what's under it: a dark rim drawn with it adds
/// nothing, and a bright one glows as a box. A rim uses the font asset's own material instead, the same atlas with
/// ordinary blending.
/// </summary>
internal static class TextRimMaterial
{
    public static Material For(TMP_FontAsset? font, Material face) => font != null && font.material != null ? font.material : face;
}
