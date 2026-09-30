namespace NoMapOverhaul.MapTools;

/// <summary>
/// Tells a key's double tap from single taps: a press within <see cref="Window"/> of the one before is a double tap.
/// A double tap uses up both presses, so a third quick press starts over rather than making a second double tap.
/// </summary>
public sealed class DoubleTap
{
    /// <summary>Seconds from the first press to the second: room for a deliberate double press, short enough that two separate picks don't count.</summary>
    public const float Window = 0.35f;

    private float? _lastPress;

    /// <summary>A press at <paramref name="time"/>, in seconds. True when it completes a double tap.</summary>
    public bool Press(float time)
    {
        if (_lastPress is { } last && time - last <= Window)
        {
            _lastPress = null;
            return true;
        }

        _lastPress = time;
        return false;
    }
}
