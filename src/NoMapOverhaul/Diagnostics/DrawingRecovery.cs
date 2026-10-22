using System;

namespace NoMapOverhaul.Diagnostics;

/// <summary>
/// When the mod's graphics may be built again after drawing them threw. The plugin then takes every graphic down,
/// leaving the drawings, the received drawings and sharing as they are, and waits: 1 second after the first failure,
/// twice as long after each failure that follows soon after, up to a minute, and back to a second once drawing has
/// run <see cref="HealthySeconds"/> past its pause without failing. A fault that keeps coming back costs one rebuild a
/// minute instead of disabling the mod. Unity-free, so the schedule is unit-tested.
/// </summary>
public sealed class DrawingRecovery
{
    public const double FirstPauseSeconds = 1;
    public const double MaxPauseSeconds = 60;
    public const double HealthySeconds = 30;

    private double _resumeAt;
    private double _pause;

    public int Failures { get; private set; }

    public bool CanDraw(double now) => now >= _resumeAt;

    /// <summary>Records a failure at <paramref name="now"/> and returns how many seconds drawing now waits.</summary>
    public double Fail(double now)
    {
        Failures++;
        var healthy = _pause == 0 || now - _resumeAt >= HealthySeconds;
        _pause = healthy ? FirstPauseSeconds : Math.Min(MaxPauseSeconds, _pause * 2);
        _resumeAt = now + _pause;
        return _pause;
    }
}
