using System;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// When the heading-up minimap has turned far enough for its labels and markers to be turned upright again, or laid
/// out again. The game turns the minimap to the aircraft's heading every frame, which never holds exactly still in
/// flight, so turning on any change would redo every label every frame. Unity-free, so the steps are unit-tested.
/// </summary>
public static class MinimapHeading
{
    /// <summary>How far the minimap turns before labels and markers stand upright again: a tilt under this doesn't show.</summary>
    public const float UprightStepDegrees = 1f;

    /// <summary>How far the minimap turns before its labels step clear of each other again; in between each keeps its slot.</summary>
    public const float RespaceStepDegrees = 5f;

    /// <summary>
    /// Whether the map has turned at least <paramref name="step"/> degrees from <paramref name="from"/> to
    /// <paramref name="to"/>, either way round, across north too. A step of 0 counts any change.
    /// </summary>
    public static bool Turned(float from, float to, float step)
    {
        var turn = Math.Abs(Delta(from, to));
        return step <= 0f ? turn > 0f : turn >= step;
    }

    /// <summary>The shortest turn from one heading to the other, in degrees between -180 and 180.</summary>
    public static float Delta(float from, float to)
    {
        var delta = (to - from) % 360f;
        if (delta > 180f)
        {
            delta -= 360f;
        }
        else if (delta < -180f)
        {
            delta += 360f;
        }

        return delta;
    }
}
