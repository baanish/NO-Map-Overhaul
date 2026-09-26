using System.Collections.Generic;
using System.Numerics;

namespace BaanishUiImprovements.Runways;

/// <summary>
/// One runway flattened onto the ground plane: X is east, Y is north, in meters.
/// Landing at <see cref="Start"/> means flying toward <see cref="End"/>, which is the game's "not reversed" usage.
/// </summary>
public readonly struct RunwayLine
{
    public RunwayLine(Vector2 start, Vector2 end)
    {
        Start = start;
        End = end;
    }

    public Vector2 Start { get; }
    public Vector2 End { get; }

    /// <summary>Distance from a point to the runway strip itself, not its extended centerline.</summary>
    public float DistanceTo(Vector2 point)
    {
        var length = Vector2.Distance(Start, End);
        var axis = (End - Start) / length;
        var along = System.Math.Clamp(Vector2.Dot(point - Start, axis), 0f, length);
        return Vector2.Distance(point, Start + axis * along);
    }
}

/// <summary>The approach to show: which runway in the list, and whether it's off its End (reversed) rather than its Start.</summary>
public readonly struct ApproachChoice
{
    public ApproachChoice(int runwayIndex, bool reverse)
    {
        RunwayIndex = runwayIndex;
        Reverse = reverse;
    }

    public int RunwayIndex { get; }
    public bool Reverse { get; }
}

/// <summary>
/// Picks the nearest runway within range and the end of it closer to the pilot. Heading plays no part: the line
/// is there to judge the turn onto final, so it has to show while the pilot is still side-on to the runway.
/// Earlier versions guessed the landing end from heading and the traffic-pattern leg, and got it wrong while
/// manoeuvring near the field. Between two equally near runways or ends this can switch as the pilot moves.
/// </summary>
public static class ApproachSelector
{
    public static ApproachChoice? Select(IReadOnlyList<RunwayLine> runways, Vector2 position, float rangeMeters)
    {
        var best = -1;
        var nearest = float.MaxValue;
        for (var i = 0; i < runways.Count; i++)
        {
            var distance = runways[i].DistanceTo(position);
            if (distance <= rangeMeters && distance < nearest)
            {
                best = i;
                nearest = distance;
            }
        }

        if (best < 0)
        {
            return null;
        }

        var runway = runways[best];
        return new ApproachChoice(best, Vector2.Distance(position, runway.End) < Vector2.Distance(position, runway.Start));
    }
}
