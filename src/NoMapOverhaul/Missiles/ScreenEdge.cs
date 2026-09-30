using System;
using System.Numerics;

namespace NoMapOverhaul.Missiles;

/// <summary>
/// Places an arrow on the screen edge toward something off screen. Works in camera space (X right, Y up, Z forward)
/// rather than from Camera.WorldToScreenPoint, which mirrors points behind the camera: an arrow built from it,
/// like the game's own target arrow, points away from a missile closing from behind.
/// </summary>
public static class ScreenEdge
{
    /// <summary>
    /// Null when the point projects inside the screen. Otherwise the arrow's offset from the screen centre in pixels:
    /// on the edge, in the direction to turn toward the point. A point dead astern gets the bottom edge.
    /// </summary>
    /// <param name="focalPixels">Half the screen height over the tangent of half the vertical field of view.</param>
    public static Vector2? Pin(Vector3 cameraSpace, Vector2 halfScreen, float focalPixels)
    {
        var across = new Vector2(cameraSpace.X, cameraSpace.Y);
        if (cameraSpace.Z > 0f)
        {
            var projected = across * (focalPixels / cameraSpace.Z);
            if (Math.Abs(projected.X) <= halfScreen.X && Math.Abs(projected.Y) <= halfScreen.Y)
            {
                return null;
            }
        }

        if (across.LengthSquared() < 1e-6f)
        {
            across = -Vector2.UnitY;
        }

        var toSide = across.X == 0f ? float.MaxValue : halfScreen.X / Math.Abs(across.X);
        var toTopOrBottom = across.Y == 0f ? float.MaxValue : halfScreen.Y / Math.Abs(across.Y);
        return across * Math.Min(toSide, toTopOrBottom);
    }
}
