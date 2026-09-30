using UnityEngine;

namespace BaanishUiImprovements.MapTools;

/// <summary>Converts the Unity-free <see cref="ShapeColor"/> where shapes meet Unity.</summary>
internal static class ShapeColorUnity
{
    public static Color32 ToColor32(this ShapeColor color) => new(color.R, color.G, color.B, color.A);

    public static ShapeColor ToShapeColor(this Color color)
    {
        Color32 bytes = color;
        return new ShapeColor(bytes.r, bytes.g, bytes.b, bytes.a);
    }
}
