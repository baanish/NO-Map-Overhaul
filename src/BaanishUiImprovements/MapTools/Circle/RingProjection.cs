using System;
using System.Collections.Generic;
using System.Numerics;

namespace BaanishUiImprovements.MapTools.Circle;

/// <summary>
/// A perspective camera as the screen sees it. World axes are Unity's (X east, Y up, Z north) and the camera looks
/// along its rotated +Z with +Y up, as Unity's does. Screen pixels count from the bottom-left corner.
/// </summary>
public readonly struct ScreenCamera : IEquatable<ScreenCamera>
{
    /// <param name="focalPixels">Half the screen height over the tangent of half the vertical field of view.</param>
    /// <param name="near">The near clip distance: nothing closer is drawn.</param>
    public ScreenCamera(Vector3 position, Quaternion rotation, Vector2 halfScreen, float focalPixels, float near)
    {
        Position = position;
        Rotation = rotation;
        HalfScreen = halfScreen;
        FocalPixels = focalPixels;
        Near = near;
    }

    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public Vector2 HalfScreen { get; }
    public float FocalPixels { get; }
    public float Near { get; }

    public bool Equals(ScreenCamera other) =>
        Position == other.Position && Rotation == other.Rotation && HalfScreen == other.HalfScreen &&
        FocalPixels == other.FocalPixels && Near == other.Near;

    public override bool Equals(object? obj) => obj is ScreenCamera other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Position, Rotation, HalfScreen, FocalPixels, Near);
}

/// <summary>
/// Projects a level ring in the world onto the screen as polylines, for a circle drawn in the 3D view.
/// Each chord is clipped in camera space against the near plane and the four sides of the screen grown by
/// <see cref="GuardPixels"/>, before it is projected. So a ring passing behind the camera is cut where it crosses the
/// near plane rather than mirrored onto the screen the way <c>Camera.WorldToScreenPoint</c> mirrors it, and no point
/// lands so far off screen that float precision bends the line. The buffers are reused, so projecting allocates nothing.
/// </summary>
public sealed class RingProjection
{
    /// <summary>Chords for a ring that fills the view. At 20 nm a chord strays at most 6 m from the true circle, and at 1 km under 0.3 m.</summary>
    public const int Segments = 128;

    /// <summary>Chords for a ring a few pixels across, far away.</summary>
    public const int MinSegments = 16;

    /// <summary>Points <see cref="TrySample"/> takes around a ring: the corners of its smallest mesh.</summary>
    public const int SampleCount = MinSegments;

    /// <summary>How far past the screen edge a cut line runs, so its faded end stays off screen.</summary>
    public const float GuardPixels = 32f;

    /// <summary>How far a chord may stray from the true circle on screen, from a camera looking straight at it.</summary>
    private const float MaxStrayPixels = 0.25f;

    private static readonly float[] Cosines = new float[Segments];
    private static readonly float[] Sines = new float[Segments];

    private readonly Vector3[] _samples = new Vector3[Segments];
    private readonly List<Vector2> _points = new(Segments * 2);
    private readonly List<(int Start, int Count)> _runs = new();

    static RingProjection()
    {
        for (var i = 0; i < Segments; i++)
        {
            var angle = i * 2.0 * Math.PI / Segments;
            Cosines[i] = (float)Math.Cos(angle);
            Sines[i] = (float)Math.Sin(angle);
        }
    }

    /// <summary>Screen points of every run, one run after another.</summary>
    public IReadOnlyList<Vector2> Points => _points;

    /// <summary>The visible pieces of the ring, each a range of <see cref="Points"/>. None when it is all out of view.</summary>
    public IReadOnlyList<(int Start, int Count)> Runs => _runs;

    /// <summary>The whole ring is in view: one run that closes on itself.</summary>
    public bool Closed { get; private set; }

    /// <summary>How many chords the last projection cut the ring into, from <see cref="ChordsFor"/>.</summary>
    public int Chords { get; private set; }

    /// <summary>
    /// The fewest chords, doubling from <see cref="MinSegments"/> up to <see cref="Segments"/>, that keep the ring round on
    /// screen. No point of the ring is nearer the camera than <paramref name="distance"/> less the radius, so the ring's
    /// radius spans at most the focal length times the radius over that many pixels, and a chord of a ring that size
    /// strays from it by <see cref="MaxStrayPixels"/> at most, from a camera looking straight at it. Off the screen's
    /// centre, perspective can stretch that to about twice as much. A camera inside the ring gets them all.
    /// </summary>
    /// <param name="distance">From the camera to the ring's centre, in meters.</param>
    public static int ChordsFor(float distance, float radius, float focalPixels)
    {
        if (distance <= radius)
        {
            return Segments;
        }

        var pixels = focalPixels * radius / (distance - radius);
        var chords = MinSegments;
        while (chords < Segments && pixels * (1f - MathF.Cos(MathF.PI / chords)) > MaxStrayPixels)
        {
            chords *= 2;
        }

        return chords;
    }

    /// <summary>
    /// Projects the ring around <paramref name="center"/> in the level plane through it, radius in meters. A ring whose
    /// bounding sphere lies wholly outside one of the clip planes projects to nothing without being sampled.
    /// </summary>
    public void Project(Vector3 center, float radius, in ScreenCamera camera)
    {
        _points.Clear();
        _runs.Clear();
        Closed = false;

        ToCamera(center, radius, camera, out var middle, out var east, out var north);
        var chords = Chords = ChordsFor(middle.Length(), radius, camera.FocalPixels);
        if (!MayShow(middle, radius, camera))
        {
            return;
        }

        var stride = Segments / chords;
        var outside = -1;
        for (var i = 0; i < chords; i++)
        {
            _samples[i] = middle + east * Cosines[i * stride] + north * Sines[i * stride];
            if (outside < 0 && !Inside(_samples[i], camera))
            {
                outside = i;
            }
        }

        if (outside < 0)
        {
            for (var i = 0; i < chords; i++)
            {
                _points.Add(ToScreen(_samples[i], camera));
            }

            _runs.Add((0, chords));
            Closed = true;
            return;
        }

        // Starting from a point out of view, no run wraps past the end of the samples.
        var open = false;
        for (var step = 0; step < chords; step++)
        {
            var i = (outside + step) % chords;
            var from = _samples[i];
            var to = _samples[(i + 1) % chords];
            if (!TryClip(from, to, camera, out var enter, out var exit))
            {
                open = false;
                continue;
            }

            if (!open || enter > 0f)
            {
                _runs.Add((_points.Count, 0));
                _points.Add(ToScreen(Vector3.Lerp(from, to, enter), camera));
            }

            _points.Add(ToScreen(Vector3.Lerp(from, to, exit), camera));
            var last = _runs.Count - 1;
            _runs[last] = (_runs[last].Start, _points.Count - _runs[last].Start);
            open = exit >= 1f;
        }
    }

    /// <summary>
    /// Screen points of <see cref="SampleCount"/> points evenly spaced around the ring, into <paramref name="points"/>,
    /// for telling how far the ring has moved on screen without projecting all of it. False when any of them is out of
    /// the guarded view.
    /// </summary>
    public static bool TrySample(Vector3 center, float radius, in ScreenCamera camera, Vector2[] points)
    {
        ToCamera(center, radius, camera, out var middle, out var east, out var north);
        const int stride = Segments / SampleCount;
        for (var i = 0; i < SampleCount; i++)
        {
            var sample = middle + east * Cosines[i * stride] + north * Sines[i * stride];
            if (!Inside(sample, camera))
            {
                return false;
            }

            points[i] = ToScreen(sample, camera);
        }

        return true;
    }

    /// <summary>
    /// The shift that best carries a ring's samples from where they were, <paramref name="then"/>, to where they are,
    /// <paramref name="now"/>: their mean movement. True when it brings every sample within <paramref name="tolerance"/>
    /// pixels of its place, so moving the old mesh by it draws the ring as rebuilding it would, to that tolerance.
    /// </summary>
    public static bool TryShift(Vector2[] then, Vector2[] now, float tolerance, out Vector2 shift)
    {
        shift = Vector2.Zero;
        for (var i = 0; i < SampleCount; i++)
        {
            shift += now[i] - then[i];
        }

        shift /= SampleCount;
        for (var i = 0; i < SampleCount; i++)
        {
            if (Vector2.DistanceSquared(then[i] + shift, now[i]) > tolerance * tolerance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The part of the chord from <paramref name="from"/> to <paramref name="to"/>, both in camera space, that lies in
    /// front of the near plane and within the guarded screen, as fractions along it. False when none of it does.
    /// </summary>
    public static bool TryClip(Vector3 from, Vector3 to, in ScreenCamera camera, out float enter, out float exit)
    {
        enter = 0f;
        exit = 1f;
        for (var plane = 0; plane < 5; plane++)
        {
            var start = Distance(plane, from, camera);
            var end = Distance(plane, to, camera);
            if (start < 0f && end < 0f)
            {
                return false;
            }

            if (start < 0f)
            {
                enter = Math.Max(enter, start / (start - end));
            }
            else if (end < 0f)
            {
                exit = Math.Min(exit, start / (start - end));
            }
        }

        return enter <= exit;
    }

    /// <summary>The ring's centre in camera space, and its east and north radii turned into camera space.</summary>
    private static void ToCamera(Vector3 center, float radius, in ScreenCamera camera, out Vector3 middle, out Vector3 east, out Vector3 north)
    {
        var toCamera = Quaternion.Conjugate(camera.Rotation);
        middle = Vector3.Transform(center - camera.Position, toCamera);
        east = Vector3.Transform(Vector3.UnitX, toCamera) * radius;
        north = Vector3.Transform(Vector3.UnitZ, toCamera) * radius;
    }

    /// <summary>
    /// False when the sphere around the ring lies wholly outside one of the clip planes, so none of the ring can show.
    /// <see cref="Distance"/> scales each side plane's distance by the length of its normal, which this divides out.
    /// </summary>
    private static bool MayShow(Vector3 middle, float radius, in ScreenCamera camera)
    {
        var across = (camera.HalfScreen.X + GuardPixels) / camera.FocalPixels;
        var up = (camera.HalfScreen.Y + GuardPixels) / camera.FocalPixels;
        var sideReach = radius * MathF.Sqrt(1f + across * across);
        var topReach = radius * MathF.Sqrt(1f + up * up);
        for (var plane = 0; plane < 5; plane++)
        {
            var reach = plane switch
            {
                0 => radius,
                1 or 2 => sideReach,
                _ => topReach,
            };
            if (Distance(plane, middle, camera) < -reach)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Inside(Vector3 point, in ScreenCamera camera)
    {
        for (var plane = 0; plane < 5; plane++)
        {
            if (Distance(plane, point, camera) < 0f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>How far inside one clip plane a camera-space point is, scaled but signed: negative is outside.</summary>
    private static float Distance(int plane, Vector3 point, in ScreenCamera camera)
    {
        var across = (camera.HalfScreen.X + GuardPixels) / camera.FocalPixels;
        var up = (camera.HalfScreen.Y + GuardPixels) / camera.FocalPixels;
        return plane switch
        {
            0 => point.Z - camera.Near,
            1 => across * point.Z + point.X,
            2 => across * point.Z - point.X,
            3 => up * point.Z + point.Y,
            _ => up * point.Z - point.Y,
        };
    }

    private static Vector2 ToScreen(Vector3 point, in ScreenCamera camera) =>
        camera.HalfScreen + new Vector2(point.X, point.Y) * (camera.FocalPixels / point.Z);
}
