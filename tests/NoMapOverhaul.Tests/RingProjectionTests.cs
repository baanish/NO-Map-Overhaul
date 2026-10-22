using System.Diagnostics;
using System.Numerics;
using NoMapOverhaul.MapTools.Circle;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

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
        ("a ring centred off screen that reaches into view still draws", RingReachingIntoViewDraws),
        ("a small turn slides a ring's samples without reshaping it", TurnSlidesRing),
        ("nearing a ring reshapes it past the shift tolerance", NearingReshapesRing),
        ("a ring partly out of view gives no samples", RingPartlyOutOfViewGivesNoSamples),
        ("cached ring clipping matches the original projection exactly", ProjectionMatchesReference),
        ("ring projection reuses buffers without allocating", ProjectionReusesBuffers),
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

    /// <summary>
    /// A 500 m ring whose centre is 2.6 km right of a camera looking north, 1 km ahead: its centre lies past the guarded
    /// right edge by 365 m measured square to that plane, so its near side reaches 135 m back inside it.
    /// </summary>
    private static void RingReachingIntoViewDraws()
    {
        var projection = new RingProjection();
        projection.Project(new Vector3(2600, 0, 1000), 500f, Camera(new Vector3(0, 100, 0), Quaternion.Identity));
        Expect(projection.Runs.Count > 0 && !projection.Closed, $"expected part of the ring drawn, got {projection.Runs.Count} runs");
        foreach (var point in projection.Points)
        {
            ExpectWithinGuard(point);
        }
    }

    /// <summary>Turning 0.2 degrees right moves a ring 1 km ahead left by the focal length times the turn's tangent.</summary>
    private static void TurnSlidesRing()
    {
        var center = new Vector3(0, 0, 1000);
        var then = new Vector2[RingProjection.SampleCount];
        var now = new Vector2[RingProjection.SampleCount];
        Expect(RingProjection.TrySample(center, 100f, Camera(new Vector3(0, 100, 0), Quaternion.Identity), then), "expected the ring in view");
        var turn = 0.2f * MathF.PI / 180f;
        var turned = Camera(new Vector3(0, 100, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn));
        Expect(RingProjection.TrySample(center, 100f, turned, now), "expected the ring in view after the turn");
        var slid = RingProjection.TryShift(then, now, 0.4f, out var shift);
        var expected = -Focal * MathF.Tan(turn);
        Expect(slid && MathF.Abs(shift.X - expected) < 0.05f && MathF.Abs(shift.Y) < 0.05f,
            $"expected a slide of {expected} px, got {shift} (within tolerance: {slid})");
    }

    /// <summary>50 m nearer a 100 m ring 1 km ahead, it spans 5% more of the screen: nearly 3 pixels wider.</summary>
    private static void NearingReshapesRing()
    {
        var center = new Vector3(0, 0, 1000);
        var then = new Vector2[RingProjection.SampleCount];
        var now = new Vector2[RingProjection.SampleCount];
        RingProjection.TrySample(center, 100f, Camera(new Vector3(0, 100, 0), Quaternion.Identity), then);
        RingProjection.TrySample(center, 100f, Camera(new Vector3(0, 100, 50), Quaternion.Identity), now);
        Expect(!RingProjection.TryShift(then, now, 0.4f, out _), "expected the nearer ring to need rebuilding");
    }

    private static void RingPartlyOutOfViewGivesNoSamples() =>
        Expect(!RingProjection.TrySample(new Vector3(2600, 0, 1000), 500f, Camera(new Vector3(0, 100, 0), Quaternion.Identity), new Vector2[RingProjection.SampleCount]),
            "expected no samples for a ring cut by the screen's edge");

    private static void ProjectionMatchesReference()
    {
        var projection = new RingProjection();
        var reference = new ReferenceProjection();
        var samples = new Vector2[RingProjection.SampleCount];
        var oldSamples = new Vector2[RingProjection.SampleCount];
        var random = new Random(39417);
        var checkedRings = 0;
        var chordCounts = new HashSet<int>();
        var closed = false;
        var clipped = false;
        var invisible = false;

        void Check(Vector3 center, float radius, ScreenCamera camera)
        {
            projection.Project(center, radius, camera);
            reference.Project(center, radius, camera);
            var context = $"ring {checkedRings++}: center {center}, radius {radius}, camera {camera.Position}, rotation {camera.Rotation}";
            Expect(projection.Chords == reference.Chords && projection.Closed == reference.Closed &&
                projection.Runs.SequenceEqual(reference.Runs) && projection.Points.Count == reference.Points.Count,
                $"projection topology changed for {context}");
            for (var i = 0; i < projection.Points.Count; i++)
            {
                Expect(SamePoint(projection.Points[i], reference.Points[i]), $"point {i} changed for {context}");
            }

            var sampled = RingProjection.TrySample(center, radius, camera, samples);
            var oldSampled = ReferenceProjection.TrySample(center, radius, camera, oldSamples);
            Expect(sampled == oldSampled, $"sample visibility changed for {context}");
            if (sampled)
            {
                for (var i = 0; i < samples.Length; i++)
                {
                    Expect(SamePoint(samples[i], oldSamples[i]), $"sample {i} changed for {context}");
                }
            }

            chordCounts.Add(projection.Chords);
            closed |= projection.Closed;
            clipped |= !projection.Closed && projection.Runs.Count > 0;
            invisible |= projection.Runs.Count == 0;
        }

        var straight = Camera(Vector3.Zero, Quaternion.Identity);
        Check(new Vector3(0, 0, 1000), 100f, straight);
        Check(new Vector3(0, 0, -1000), 100f, straight);
        Check(new Vector3(0, 0, 1), 0.5f, straight);
        Check(Vector3.Zero, 1000f, straight);
        Check(Vector3.Zero, 1000f, default);
        foreach (var depth in new[] { 1f, 100f })
        {
            var across = (HalfScreen.X + RingProjection.GuardPixels) / Focal * depth;
            var up = (HalfScreen.Y + RingProjection.GuardPixels) / Focal * depth;
            foreach (var edge in new[] { new Vector3(across, 0, depth), new Vector3(-across, 0, depth),
                         new Vector3(0, up, depth), new Vector3(0, -up, depth), new Vector3(0, 0, 1) })
            {
                Check(edge, depth * 0.2f, straight);
                foreach (var component in new[] { 0, 1, 2 })
                {
                    foreach (var direction in new[] { -1, 0, 1 })
                    {
                        var from = edge;
                        var value = component == 0 ? edge.X : component == 1 ? edge.Y : edge.Z;
                        value = direction < 0 ? MathF.BitDecrement(value) : direction > 0 ? MathF.BitIncrement(value) : value;
                        if (component == 0) from.X = value;
                        else if (component == 1) from.Y = value;
                        else from.Z = value;
                        CheckChord(from, new Vector3(0, 0, 10), straight);
                        CheckChord(new Vector3(0, 0, 10), from, straight);
                        CheckChord(from, from, straight);
                        Check(from, 0f, straight);
                    }
                }
            }
        }

        float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);
        for (var i = 0; i < 10000; i++)
        {
            var position = new Vector3(Next(-100000, 100000), Next(-10000, 10000), Next(-100000, 100000));
            var rotation = Quaternion.CreateFromYawPitchRoll(Next(-MathF.PI, MathF.PI), Next(-MathF.PI, MathF.PI), Next(-MathF.PI, MathF.PI));
            var halfScreen = new Vector2(Next(160, 1920), Next(120, 1080));
            var camera = new ScreenCamera(position, rotation, halfScreen, halfScreen.Y * Next(0.3f, 3f), Next(0.01f, 20f));
            var radius = MathF.Pow(10f, Next(-2f, 4f));
            var offset = new Vector3(Next(-4, 4), Next(-4, 4), Next(-2, 8)) * radius;
            Check(position + Vector3.Transform(offset, rotation), radius, camera);
            CheckChord(offset, new Vector3(Next(-4, 4), Next(-4, 4), Next(-2, 8)) * radius, camera);
        }

        Expect(closed && clipped && invisible && chordCounts.SetEquals(new[] { 16, 32, 64, 128 }), "expected all visibility states and chord counts covered");
        Console.WriteLine($"Compared {checkedRings} rings with bit-identical points, runs, chords and samples.");
        if (Environment.GetEnvironmentVariable("RING_PROJECTION_TIMING") == "1")
        {
            TimeProjection(projection, reference);
        }
    }

    private static bool SamePoint(Vector2 a, Vector2 b) => SameFloat(a.X, b.X) && SameFloat(a.Y, b.Y);

    private static bool SameFloat(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static void CheckChord(Vector3 from, Vector3 to, ScreenCamera camera)
    {
        var clipped = RingProjection.TryClip(from, to, camera, out var enter, out var exit);
        var oldClipped = ReferenceProjection.TryClip(from, to, camera, out var oldEnter, out var oldExit);
        Expect(clipped == oldClipped && SameFloat(enter, oldEnter) && SameFloat(exit, oldExit),
            $"chord clipping changed for {from} to {to}");
    }

    private static void ProjectionReusesBuffers()
    {
        var projection = new RingProjection();
        var camera = Camera(new Vector3(0, 100, 0), Quaternion.Identity);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            projection.Project(new Vector3(0, 0, 1000), 100f, camera);
            projection.Project(Vector3.Zero, 1000f, camera);
            projection.Project(new Vector3(0, 0, -1000), 100f, camera);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Expect(allocated == 0, $"expected no projection allocations, got {allocated} bytes");
    }

    private static void TimeProjection(RingProjection projection, ReferenceProjection reference)
    {
        var camera = Camera(new Vector3(0, 100, 0), Quaternion.Identity);
        projection.Project(Vector3.Zero, 1000f, camera);
        Expect(projection.Chords == 128 && !projection.Closed && projection.Runs.Count > 0, "expected a clipped 128-chord benchmark ring");
        const int iterations = 100000;
        void Run(bool old)
        {
            for (var i = 0; i < iterations; i++)
            {
                if (old) reference.Project(Vector3.Zero, 1000f, camera);
                else projection.Project(Vector3.Zero, 1000f, camera);
            }
        }

        Run(true);
        Run(false);
        var oldTimes = new double[7];
        var newTimes = new double[7];
        for (var round = 0; round < oldTimes.Length; round++)
        {
            for (var part = 0; part < 2; part++)
            {
                var old = (round + part) % 2 == 0;
                var start = Stopwatch.GetTimestamp();
                Run(old);
                var nanoseconds = (Stopwatch.GetTimestamp() - start) * (1e9 / Stopwatch.Frequency) / iterations;
                (old ? oldTimes : newTimes)[round] = nanoseconds;
            }
        }

        Array.Sort(oldTimes);
        Array.Sort(newTimes);
        Console.WriteLine($"Ring projection timing on .NET 8: old {oldTimes[3]:F0} ns, new {newTimes[3]:F0} ns, old/new {oldTimes[3] / newTimes[3]:F2}x (median of 7 x {iterations} projections).");
    }

    private sealed class ReferenceProjection
    {
        public const int Segments = 128;

        public const int MinSegments = 16;

        public const int SampleCount = MinSegments;

        public const float GuardPixels = 32f;

        private const float MaxStrayPixels = 0.25f;

        private static readonly float[] Cosines = new float[Segments];
        private static readonly float[] Sines = new float[Segments];

        private readonly Vector3[] _samples = new Vector3[Segments];
        private readonly List<Vector2> _points = new(Segments * 2);
        private readonly List<(int Start, int Count)> _runs = new();

        static ReferenceProjection()
        {
            for (var i = 0; i < Segments; i++)
            {
                var angle = i * 2.0 * Math.PI / Segments;
                Cosines[i] = (float)Math.Cos(angle);
                Sines[i] = (float)Math.Sin(angle);
            }
        }

        public IReadOnlyList<Vector2> Points => _points;

        public IReadOnlyList<(int Start, int Count)> Runs => _runs;

        public bool Closed { get; private set; }

        public int Chords { get; private set; }

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

        private static void ToCamera(Vector3 center, float radius, in ScreenCamera camera, out Vector3 middle, out Vector3 east, out Vector3 north)
        {
            var toCamera = Quaternion.Conjugate(camera.Rotation);
            middle = Vector3.Transform(center - camera.Position, toCamera);
            east = Vector3.Transform(Vector3.UnitX, toCamera) * radius;
            north = Vector3.Transform(Vector3.UnitZ, toCamera) * radius;
        }

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

    private static ScreenCamera Camera(Vector3 position, Quaternion rotation) => new(position, rotation, HalfScreen, Focal, 1f);

    private static void ExpectWithinGuard(Vector2 point)
    {
        var reach = HalfScreen + new Vector2(RingProjection.GuardPixels + 0.01f);
        Expect(MathF.Abs(point.X - HalfScreen.X) <= reach.X && MathF.Abs(point.Y - HalfScreen.Y) <= reach.Y,
            $"expected {point} within the guard band");
    }
}
