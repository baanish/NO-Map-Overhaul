using System.Numerics;
using BaanishUiImprovements.MapTools.Circle;
using static BaanishUiImprovements.Tests.Program;

namespace BaanishUiImprovements.Tests;

/// <summary>
/// Circles in the 3D view: rings projected through a 1920x1080 camera with a 90 degree vertical field of view, so the
/// focal length is 540 pixels. World axes are Unity's: X east, Y up, Z north.
/// </summary>
internal static class RingProjectionTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("a ring fully in view projects as one closed run", RingInViewIsClosed),
        ("a ring around the camera is cut, not mirrored", RingAroundCameraIsCut),
        ("a ring behind the camera draws nothing", RingBehindDrawsNothing),
        ("the camera's heading turns the ring", HeadingTurnsRing),
        ("a chord through the near plane is split there", ChordSplitAtNearPlane),
        ("a chord wholly behind the camera is dropped", ChordBehindDropped),
        ("a chord past the screen side stops at the guard band", ChordStopsAtGuardBand),
        ("a ring gets fewer chords the smaller it shows", ChordsFollowScreenSize),
    };

    private const float Focal = 540f;
    private static readonly Vector2 HalfScreen = new(960, 540);

    /// <summary>Looking north from 100 m up at a 100 m ring 1 km ahead on the ground.</summary>
    private static void RingInViewIsClosed()
    {
        var projection = new RingProjection();
        projection.Project(new Vector3(0, 0, 1000), 100f, Camera(new Vector3(0, 100, 0), Quaternion.Identity));
        Expect(projection.Closed && projection.Runs.Count == 1, "expected one closed run");
        Expect(projection.Points.Count == projection.Chords, $"expected a point per chord, got {projection.Points.Count} for {projection.Chords}");
        var east = projection.Points[0];
        var expected = HalfScreen + new Vector2(100, -100) * (Focal / 1000f);
        Expect(Vector2.Distance(east, expected) < 0.01f, $"expected the ring's east point at {expected}, got {east}");
    }

    /// <summary>
    /// A 100 m ring 20 km off spans about 3 pixels, 1 km off about 60, and a camera inside a ring sees it fill the view.
    /// Each chord count keeps the stray under half a pixel.
    /// </summary>
    private static void ChordsFollowScreenSize()
    {
        var far = RingProjection.ChordsFor(20000f, 100f, Focal);
        var near = RingProjection.ChordsFor(1000f, 100f, Focal);
        var inside = RingProjection.ChordsFor(500f, 1000f, Focal);
        Expect(far == RingProjection.MinSegments, $"expected {RingProjection.MinSegments} chords far off, got {far}");
        Expect(near > far && near < RingProjection.Segments, $"expected between {far} and {RingProjection.Segments} chords at 1 km, got {near}");
        Expect(inside == RingProjection.Segments, $"expected every chord from inside the ring, got {inside}");

        var pixels = Focal * 100f / 900f;
        var stray = pixels * (1f - MathF.Cos(MathF.PI / near));
        Expect(stray < 0.5f, $"expected a chord at 1 km to stray under half a pixel, got {stray}");
    }

    /// <summary>
    /// Standing 100 m over the middle of a 1 km ring, looking north. The front arc lies below the horizon. Mirrored like
    /// WorldToScreenPoint, the back arc would show above it.
    /// </summary>
    private static void RingAroundCameraIsCut()
    {
        var projection = new RingProjection();
        projection.Project(Vector3.Zero, 1000f, Camera(new Vector3(0, 100, 0), Quaternion.Identity));
        Expect(!projection.Closed && projection.Runs.Count == 1, $"expected one open run, got {projection.Runs.Count}");
        foreach (var point in projection.Points)
        {
            Expect(point.Y < HalfScreen.Y, $"expected every point below the horizon, got {point}");
            ExpectWithinGuard(point);
        }

        var (start, count) = projection.Runs[0];
        var ends = new[] { projection.Points[start].X, projection.Points[start + count - 1].X };
        Expect(ends.Min() <= -RingProjection.GuardPixels + 0.01f && ends.Max() >= 2 * HalfScreen.X + RingProjection.GuardPixels - 0.01f,
            $"expected the arc to run from one side's guard band to the other, got {ends[0]} and {ends[1]}");
    }

    private static void RingBehindDrawsNothing()
    {
        var projection = new RingProjection();
        projection.Project(new Vector3(0, 0, -5000), 1000f, Camera(new Vector3(0, 100, 0), Quaternion.Identity));
        Expect(projection.Runs.Count == 0 && projection.Points.Count == 0, "expected nothing for a ring behind the camera");
    }

    /// <summary>Turned to face east, a ring to the east is in view and one to the north is not.</summary>
    private static void HeadingTurnsRing()
    {
        var east = Camera(new Vector3(0, 100, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f));
        var projection = new RingProjection();
        projection.Project(new Vector3(1000, 0, 0), 100f, east);
        Expect(projection.Closed, "expected the ring ahead in view");
        var center = HalfScreen + new Vector2(0, -100) * (Focal / 1000f);
        var nearest = projection.Points.Min(point => Vector2.Distance(point, center));
        Expect(nearest < 60f, $"expected the ring around the screen point below its centre, nearest {nearest}");

        projection.Project(new Vector3(0, 0, 1000), 100f, east);
        Expect(projection.Runs.Count == 0, "expected the ring off to the left out of view");
    }

    private static void ChordSplitAtNearPlane()
    {
        var camera = Camera(Vector3.Zero, Quaternion.Identity);
        Expect(RingProjection.TryClip(new Vector3(0, 0, -10), new Vector3(0, 0, 10), camera, out var enter, out var exit),
            "expected the part in front kept");
        Expect(MathF.Abs(enter - 0.55f) < 1e-4f && exit == 1f, $"expected the chord cut at the 1 m near plane, got {enter} to {exit}");
    }

    private static void ChordBehindDropped()
    {
        var camera = Camera(Vector3.Zero, Quaternion.Identity);
        Expect(!RingProjection.TryClip(new Vector3(-100, 0, -10), new Vector3(100, 0, -5), camera, out _, out _),
            "expected nothing kept of a chord behind the camera");
    }

    /// <summary>A chord 100 m ahead from the middle to far right: the guarded right edge is (960 + 32) / 540 of the distance across.</summary>
    private static void ChordStopsAtGuardBand()
    {
        var camera = Camera(Vector3.Zero, Quaternion.Identity);
        Expect(RingProjection.TryClip(new Vector3(0, 0, 100), new Vector3(10000, 0, 100), camera, out var enter, out var exit),
            "expected the part on screen kept");
        var edge = (HalfScreen.X + RingProjection.GuardPixels) / Focal * 100f;
        Expect(enter == 0f && MathF.Abs(exit * 10000f - edge) < 0.01f, $"expected the chord to stop {edge} m across, got {exit * 10000f}");
    }

    private static ScreenCamera Camera(Vector3 position, Quaternion rotation) => new(position, rotation, HalfScreen, Focal, 1f);

    private static void ExpectWithinGuard(Vector2 point)
    {
        var reach = HalfScreen + new Vector2(RingProjection.GuardPixels + 0.01f);
        Expect(MathF.Abs(point.X - HalfScreen.X) <= reach.X && MathF.Abs(point.Y - HalfScreen.Y) <= reach.Y,
            $"expected {point} within the guard band");
    }
}
