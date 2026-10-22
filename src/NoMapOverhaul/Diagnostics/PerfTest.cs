using System.Collections.Generic;
using System.Globalization;
using NoMapOverhaul.MapTools;
using NoMapOverhaul.MapTools.Text;
using NoMapOverhaul.Sharing;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using FlatVector = System.Numerics.Vector2;

namespace NoMapOverhaul.Diagnostics;

/// <summary>
/// The Run perf test button in F1. One press measures the whole game's frame rate in eight conditions: the minimap with
/// the mod off, on, on with typical drawings, and on with heavy drawings (both from <see cref="StressDrawings"/>), then
/// the same on the full map. Within each view the four conditions take turns in short slices
/// (<see cref="PerfSchedule"/>), so drift while flying spreads over all four,
/// and each slice waits after a switch so the switch (graphics rebuilt, the map opened, a teammate's drawings arriving)
/// stays out of the numbers. The mod-off conditions override General.Enabled through <see cref="ModOn"/>, and
/// the drawings show through <see cref="ModSettings.PerfTestShowsDrawings"/>, instead of writing the settings, so a
/// crash mid-test can't leave any of them saved. Afterwards the player's drawings come back with their undo history,
/// along with the map. Leaving the mission, losing the aircraft, opening or closing the map, or a second press stops it
/// early and puts everything back the same way.
/// Sharing keeps running throughout, mod-off slices included: teammates go on getting the player's own drawings, and
/// theirs are kept, out of sight until the test ends. The Perf test sharing setting picks whether the test's drawings
/// also travel (<see cref="PerfTestSharing"/>), on a channel of the test's own that joins the channel lists in memory
/// (<see cref="PerfSharing"/>). A test that sends replaces one note every <see cref="ChurnSeconds"/> while it shows
/// drawings, so they go out at the rate a teammate's may change. Slices run on the wall clock, from the next half
/// minute in a sharing mode, so a test started on each of two PCs within the same half minute measures each condition
/// at the same time on both.
/// </summary>
internal sealed class PerfTest
{
    /// <summary>Time to close F1, whose window costs frames too.</summary>
    private const double FirstSettleSeconds = 5;

    /// <summary>After opening the full map.</summary>
    private const double ViewSettleSeconds = 2;

    private const double SliceSettleSeconds = 0.5;

    /// <summary>A sending teammate's snapshot every 0.5 s and the receiver's 0.5 s cadence, plus the trip between them.</summary>
    private const double SharedSliceSettleSeconds = 1.5;

    private const double SliceSeconds = 2;

    /// <summary>Sharing modes start on a whole multiple of this many seconds of UTC, so two PCs line up.</summary>
    private const double AlignSeconds = 30;

    /// <summary>How often a test that sends changes one of its drawings: the fastest a receiver takes a teammate's changes.</summary>
    private const double ChurnSeconds = DrawingNetworkSchedule.SnapshotInterval;

    /// <summary>How long an aircraft must have been flown before a test the command line asked for starts.</summary>
    private const double AutoStartSeconds = 15;

    /// <summary>How far ahead of the aircraft <c>DynamicMap.CenterMinimizedMap</c> centres the minimap.</summary>
    private const float MinimapLead = 4000f;

    /// <summary>For a minimap whose size can't be read.</summary>
    private const float FallbackRadius = 8000f;

    /// <summary>The most live units the drawings ride on, so runs in busy and quiet places redraw about the same amount.</summary>
    private const int MaxAnchors = 10;

    private static readonly (string Name, bool FullMap, bool ModOn, DrawingSet Drawings)[] Conditions =
    {
        ("Minimap, mod off", false, false, DrawingSet.None),
        ("Minimap, mod on", false, true, DrawingSet.None),
        ("Minimap, mod on, typical drawings", false, true, DrawingSet.Typical),
        ("Minimap, mod on, heavy drawings", false, true, DrawingSet.Heavy),
        ("Full map, mod off", true, false, DrawingSet.None),
        ("Full map, mod on", true, true, DrawingSet.None),
        ("Full map, mod on, typical drawings", true, true, DrawingSet.Typical),
        ("Full map, mod on, heavy drawings", true, true, DrawingSet.Heavy),
    };

    private readonly ModSettings _settings;
    private readonly PerformanceLog _performance;
    private readonly MapToolHost _mapTools;
    private readonly MapToolContext _context;
    private readonly ManualLogSource _log;
    private readonly FrameStats[] _stats = new FrameStats[Conditions.Length];
    private readonly double[][] _sectionMs = new double[Conditions.Length][];
    private readonly double[] _settlingMs = new double[ModTimings.Count];
    private readonly double[][] _sectionWorstMs = new double[Conditions.Length][];
    private readonly RenderCounters _render = new(Conditions.Length);
    private readonly PerfSharingStats[] _sharing = new PerfSharingStats[Conditions.Length];

    /// <summary>Each cycle's frames and frame time per condition, for the drift line of a long test.</summary>
    private readonly List<(int Frames, double Ms)[]> _cycles = new();

    /// <summary>How many of each <see cref="DrawingSet"/>'s shapes the store took, by its value.</summary>
    private readonly int[] _shown = new int[3];

    /// <summary>The live units both sets ride on, picked at the first slice that shows drawings, and kept for the whole test.</summary>
    private List<MapPoint>? _anchors;

    private float _radius;
    private DrawingSet? _showing;
    private ShapeStore.Saved? _savedShapes;
    private NoMapOverhaul.MapTools.Waypoint.RouteProgress.Saved? _savedProgress;

    /// <summary>Set before the test changes anything, so a failure part way through starting still puts everything back.</summary>
    private bool _started;
    private int _savedMissionVersion;
    private DynamicMap? _map;
    private bool _mapWasOpen;
    private PerfTestSharing _mode;
    private int _cycleCount = PerfSchedule.Cycles;
    private int _slice = -1;

    /// <summary>On <see cref="Clock"/>: when the current slice starts settling, and when it starts and stops measuring.</summary>
    private double _measureAt;

    private double _measureEnd;
    private double _nextChurn;
    private int _churned;
    private int _collections;
    private bool _measuring;
    private bool _requested;

    /// <summary>The wall clock at the test's start, and the stopwatch then, so the clock ticks steadily from there.</summary>
    private double _clockUtc;

    private long _clockTicks;

    private readonly PerfTestSharing? _autoMode;
    private readonly double? _autoStartAt;
    private double _autoAircraftSince = -1;
    private bool _autoDone;

    public PerfTest(ModSettings settings, PerformanceLog performance, MapToolHost mapTools, ManualLogSource log)
    {
        _settings = settings;
        _performance = performance;
        _mapTools = mapTools;
        _context = mapTools.Context;
        _log = log;
        for (var i = 0; i < Conditions.Length; i++)
        {
            _stats[i] = new FrameStats();
            _sectionMs[i] = new double[ModTimings.Count];
            _sectionWorstMs[i] = new double[ModTimings.Count];
        }

        settings.PerfTestButton.CustomDrawer = DrawButton;
        settings.PerfTest.SettingChanged += (_, _) =>
        {
            if (settings.PerfTest.Value)
            {
                settings.PerfTest.Value = false;
                _requested = true;
            }
        };

        (_autoMode, _autoStartAt) = PerfSchedule.ReadAutoFlag(System.Environment.GetCommandLineArgs());
    }

    /// <summary>Whether the mod runs this frame while the test is on, overriding General.Enabled; null otherwise.</summary>
    public bool? ModOn => _slice >= 0 ? Conditions[PerfSchedule.Condition(_slice, _cycleCount)].ModOn : null;

    /// <summary>Per frame, first thing in the plugin's guarded update. Between slice switches it only compares a few numbers.</summary>
    public void Update()
    {
        if (_requested)
        {
            _requested = false;
            if (_slice >= 0)
            {
                Stop("cancelled");
            }
            else
            {
                TryBegin(_settings.PerfTestSharing.Value, _settings.PerfTestCycles.Value, null);
            }

            return;
        }

        if (_slice < 0)
        {
            AutoStart();
            return;
        }

        var map = SceneSingleton<DynamicMap>.i;
        if (map == null || !ReferenceEquals(map, _map) || _mapTools.MissionVersion != _savedMissionVersion)
        {
            Stop("the mission ended");
            return;
        }

        if (PlayerAircraft() == null)
        {
            Stop("the aircraft is gone");
            return;
        }

        var condition = PerfSchedule.Condition(_slice, _cycleCount);
        if (DynamicMap.mapMaximized != Conditions[condition].FullMap)
        {
            Stop("the map was opened or closed");
            return;
        }

        var now = Clock();
        Churn(condition, now);
        if (!_measuring)
        {
            if (now >= _measureAt)
            {
                _measuring = true;
                _render.BeginSlice();
                ModTimings.TakeMs(_settlingMs); // the settling frames' times, dropped
                ModTimings.TakeWorstMs(_settlingMs);
                SteamDrawingSharing.TakeTraffic(TrafficReader.PerfTest);
                _collections = System.GC.CollectionCount(0);
                _performance.Phase = _stats[condition];
            }

            return;
        }

        _render.Sample(condition);
        if (now < _measureEnd)
        {
            return;
        }

        var frames = _stats[condition].Frames;
        _performance.Phase = null;
        ModTimings.TakeMs(_sectionMs[condition]);
        ModTimings.TakeWorstMs(_sectionWorstMs[condition]);
        _render.CountGraphics(condition);
        _sharing[condition].Add(SteamDrawingSharing.TakeTraffic(TrafficReader.PerfTest), System.GC.CollectionCount(0) - _collections,
            _mapTools.VisibleReceived.Count, now - _measureAt);
        RecordCycle(condition);
        if (_slice + 1 < PerfSchedule.SliceCountFor(_cycleCount))
        {
            StartSlice(_slice + 1, _measureEnd);
        }
        else
        {
            Finish(Conditions.Length, null);
        }
    }

    /// <summary>The plugin is shutting down: drawings, and with <paramref name="moveMap"/> the map, come back.</summary>
    public void Shutdown(bool moveMap) => Restore(moveMap);

    private void DrawButton(ConfigEntryBase entry)
    {
        var label = _slice >= 0
            ? "Cancel perf test (slice " + (_slice + 1).ToString(CultureInfo.InvariantCulture) + " of " +
              PerfSchedule.SliceCountFor(_cycleCount).ToString(CultureInfo.InvariantCulture) + ")"
            : "Run perf test";
        if (GUILayout.Button(label, GUILayout.ExpandWidth(true)))
        {
            _requested = true;
        }
    }

    /// <summary>Starts the one test the command line asked for, once an aircraft has been flown for a while.</summary>
    private void AutoStart()
    {
        if (_autoMode is not { } mode || _autoDone)
        {
            return;
        }

        var now = Time.realtimeSinceStartupAsDouble;
        if (PlayerAircraft() == null || SceneSingleton<DynamicMap>.i == null)
        {
            _autoAircraftSince = -1;
            return;
        }

        if (_autoAircraftSince < 0)
        {
            _autoAircraftSince = now;
        }

        if (now - _autoAircraftSince >= AutoStartSeconds)
        {
            _autoDone = true;
            TryBegin(mode, _settings.PerfTestCycles.Value, _autoStartAt);
        }
    }

    /// <summary>Starts the test, and on any failure while starting puts back what it changed rather than leaving it half set up.</summary>
    private void TryBegin(PerfTestSharing mode, int cycles, double? startAt)
    {
        try
        {
            Begin(mode, cycles, startAt);
        }
        catch (System.Exception error)
        {
            _log.LogError("Perf test couldn't start; everything it changed is put back. " + error);
            Restore(moveMap: true);
            Tell("Perf test couldn't start; see LogOutput.log.");
        }
    }

    private void Begin(PerfTestSharing mode, int cycles, double? startAt)
    {
        var map = SceneSingleton<DynamicMap>.i;
        if (map == null || PlayerAircraft() == null)
        {
            Tell("Perf test: fly an aircraft in a mission first.");
            return;
        }

        if (mode != PerfTestSharing.LocalOnly && SharingRefusal(new PerfSharing(mode)) is { } refusal)
        {
            Tell("Perf test: " + refusal);
            _log.LogInfo("Perf test not started: " + refusal);
            return;
        }

        _started = true;
        _map = map;
        _mode = mode;
        _cycleCount = System.Math.Max(2, cycles + cycles % 2);
        _mapWasOpen = DynamicMap.mapMaximized;
        _settings.PerfTestShowsDrawings = true;
        _mapTools.TrackMission(map); // with the mod off the store may still hold a mission that has ended
        _savedShapes = _context.Shapes.Save();
        _savedProgress = _mapTools.RouteProgress.Save();
        _savedMissionVersion = _mapTools.MissionVersion;
        var perf = mode == PerfTestSharing.LocalOnly ? null : new PerfSharing(mode);
        _mapTools.PerfSharing = perf;
        // With Sharing off, the player's drawings stay off the network: only the test's own go out.
        _mapTools.HoldOutgoing(_settings.MapSharingEnabled.Value ? _savedShapes.Shapes : System.Array.Empty<MapShape>(), sendStore: perf?.Sends ?? false);
        _anchors = null;
        _showing = null;
        _cycles.Clear();
        foreach (var stats in _stats)
        {
            stats.Clear();
        }

        foreach (var sections in _sectionMs)
        {
            System.Array.Clear(sections, 0, sections.Length);
        }

        foreach (var worst in _sectionWorstMs)
        {
            System.Array.Clear(worst, 0, worst.Length);
        }

        System.Array.Clear(_sharing, 0, _sharing.Length);
        _render.Start();
        ModTimings.Testing = true;
        _clockUtc = (System.DateTime.UtcNow - System.DateTime.UnixEpoch).TotalSeconds;
        _clockTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        var first = PerfSchedule.FirstSliceAt(_clockUtc, FirstSettleSeconds, perf != null, startAt);
        _log.LogInfo(string.Format(CultureInfo.InvariantCulture, "Perf test started: {0}, {1} cycles, first slice at {2:0} s UTC.", mode,
            _cycleCount, first));
        Tell(string.Format(CultureInfo.InvariantCulture, "Perf test running for about {0:0} seconds. Close F1, and hold the view still.",
            first - _clockUtc + PerfSchedule.SliceCountFor(_cycleCount) * (SliceSeconds + SettleFor(perf != null))));
        StartSlice(0, first);
    }

    /// <summary>Why a sharing mode can't run here, or null.</summary>
    private string? SharingRefusal(PerfSharing perf)
    {
        if (GameManager.gameState != GameState.Multiplayer)
        {
            return "sharing modes need a Steam multiplayer mission, on the same team as the other PC.";
        }

        if (!_settings.MapSharingEnabled.Value)
        {
            return null; // the test shares on its own channel alone, whatever the player's lists hold
        }

        if (!_mapTools.ChannelsValid)
        {
            return "the channel settings are invalid. " + _mapTools.ChannelError;
        }

        var list = perf.Sends ? _mapTools.Channels.Names : _mapTools.ReceiveChannels.Names;
        return list.Count >= DrawingChannels.MaxChannels && !Contains(list, PerfSharing.Channel)
            ? "a channel list is full, so the test's own channel can't join it."
            : null;
    }

    private static bool Contains(IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i] == name) return true;
        }

        return false;
    }

    private static double SettleFor(bool shared) => shared ? SharedSliceSettleSeconds : SliceSettleSeconds;

    /// <summary>Seconds of UTC, ticking steadily from the test's start.</summary>
    private double Clock() =>
        _clockUtc + (System.Diagnostics.Stopwatch.GetTimestamp() - _clockTicks) / (double)System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Starts slice <paramref name="index"/> settling now, to measure from <paramref name="settled"/> or after its settle.</summary>
    private void StartSlice(int index, double from)
    {
        _slice = index;
        _measuring = false;
        _measureAt = index == 0 ? from
            : from + PerfSchedule.SettleSeconds(index, _cycleCount, FirstSettleSeconds, ViewSettleSeconds, SettleFor(_mode != PerfTestSharing.LocalOnly));
        _measureEnd = _measureAt + SliceSeconds;
        var condition = Conditions[PerfSchedule.Condition(index, _cycleCount)];
        if (condition.FullMap != DynamicMap.mapMaximized)
        {
            if (condition.FullMap)
            {
                _map!.Maximize();
            }
            else
            {
                _map!.Minimize();
            }
        }

        if (condition.FullMap && !DynamicMap.mapMaximized)
        {
            Finish(PerfSchedule.ConditionsPerView,
                "The game didn't open the full map (DynamicMap.AllowedToOpen was off), so only the minimap was measured.");
            return;
        }

        var receives = _mapTools.PerfSharing?.Receives ?? false;
        _mapTools.PerfShowsReceived = receives && condition.Drawings != DrawingSet.None;
        if (condition.Drawings != _showing)
        {
            _showing = condition.Drawings;
            _context.Shapes.Reset();
            // A test that only receives draws nothing of its own: its drawing slices show what the teammate sends.
            if (condition.Drawings != DrawingSet.None && _mode != PerfTestSharing.Receive)
            {
                var channel = _context.Shapes.DrawingChannel;
                _context.Shapes.DrawingChannel = _mapTools.PerfSharing?.Sends == true ? PerfSharing.Channel : string.Empty;
                try
                {
                    foreach (var shape in BuildDrawings(_map!, condition.Drawings))
                    {
                        _context.Shapes.Add(shape); // the store's caps turn away what doesn't fit
                    }
                }
                finally { _context.Shapes.DrawingChannel = channel; }

                _shown[(int)condition.Drawings] = _context.Shapes.Shapes.Count;
            }
        }
    }

    /// <summary>A test that sends changes one note's text every <see cref="ChurnSeconds"/> while it shows drawings.</summary>
    private void Churn(int condition, double now)
    {
        if (_mapTools.PerfSharing?.Sends != true || Conditions[condition].Drawings == DrawingSet.None || now < _nextChurn)
        {
            return;
        }

        _nextChurn = now + ChurnSeconds;
        var shapes = _context.Shapes.Shapes;
        var notes = 0;
        for (var i = 0; i < shapes.Count; i++)
        {
            notes += shapes[i] is TextNote ? 1 : 0;
        }

        if (notes == 0)
        {
            return;
        }

        var pick = _churned++ % notes;
        for (var i = 0; i < shapes.Count; i++)
        {
            if (shapes[i] is TextNote note && pick-- == 0)
            {
                var text = "Perf test note " + _churned.ToString(CultureInfo.InvariantCulture);
                _context.Shapes.Replace(note, new TextNote(note.At, note.Elevation, text, note.Color));
                return;
            }
        }
    }

    /// <summary>Adds the slice just measured to its cycle's line.</summary>
    private void RecordCycle(int condition)
    {
        var cycle = PerfSchedule.Cycle(_slice, _cycleCount);
        while (_cycles.Count <= cycle)
        {
            _cycles.Add(new (int, double)[Conditions.Length]);
        }

        var summary = _stats[condition].Summarize();
        var before = _cycleTotals[condition];
        _cycleTotals[condition] = (summary.Frames, summary.TotalMs);
        _cycles[cycle][condition] = (summary.Frames - before.Frames, summary.TotalMs - before.Ms);
    }

    private readonly (int Frames, double Ms)[] _cycleTotals = new (int, double)[Conditions.Length];

    /// <summary>Reports the first <paramref name="measured"/> conditions, each over all its slices.</summary>
    private void Finish(int measured, string? note)
    {
        var results = new List<PerfPhase>();
        for (var i = 0; i < measured; i++)
        {
            var summary = _stats[i].Summarize();
            var sections = new float[ModTimings.Count];
            for (var s = 0; s < sections.Length; s++)
            {
                sections[s] = summary.Frames > 0 ? (float)(_sectionMs[i][s] / summary.Frames) : 0f;
            }

            var worst = new float[ModTimings.Count];
            for (var s = 0; s < worst.Length; s++)
            {
                worst[s] = (float)_sectionWorstMs[i][s];
            }

            results.Add(new PerfPhase(Conditions[i].Name, summary, PerfSchedule.Baseline(i), _render.Summarize(i), sections,
                _sharing[i].Summarize(summary.Frames), worst));
        }

        var settle = SettleFor(_mode != PerfTestSharing.LocalOnly);
        var method = string.Format(CultureInfo.InvariantCulture,
            "each condition measured in {0} slices of {1:0.0} s, taking turns with the others in its view, {2:0.0} s to settle after each switch",
            _cycleCount, SliceSeconds, settle);
        var units = _anchors?.Count ?? 0;
        var drawings = _mode == PerfTestSharing.Receive
            ? "none of its own; the drawing slices show what teammates send on the perf-test channel"
            : string.Format(CultureInfo.InvariantCulture,
                "typical {0} shapes on {1} live units, heavy {2} shapes on {3} live units (at most {4})",
                _shown[(int)DrawingSet.Typical], System.Math.Min(units, StressDrawings.TypicalAnchors), _shown[(int)DrawingSet.Heavy], units, MaxAnchors);
        var sharing = string.Format(CultureInfo.InvariantCulture, "{0}{1}. Sharing: {2}", _mode,
            _mapTools.PerfSharing?.Sends == true ? ", one note changed every " + ChurnSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s" : string.Empty,
            _mapTools.SharingStatus);
        var table = PerfReport.Table(results, method, drawings, sharing) + PerfReport.Cycles(Names(measured), _cycles);
        _log.LogInfo(note == null ? table : table + "\n" + note);
        Restore(moveMap: true);
        Tell(PerfReport.Summary(results));
    }

    private static string[] Names(int count)
    {
        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            names[i] = Conditions[i].Name;
        }

        return names;
    }

    private void Stop(string reason)
    {
        Restore(moveMap: true);
        _log.LogInfo("Perf test stopped: " + reason + ".");
        Tell("Perf test stopped: " + reason + ".");
    }

    private void Restore(bool moveMap)
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _slice = -1;
        _measuring = false;
        _performance.Phase = null;
        ModTimings.Testing = false;
        _render.Stop();
        _settings.PerfTestShowsDrawings = false;
        _mapTools.PerfSharing = null;
        _mapTools.PerfShowsReceived = false;
        _mapTools.ReleaseOutgoing();
        System.Array.Clear(_cycleTotals, 0, _cycleTotals.Length);
        var map = SceneSingleton<DynamicMap>.i;
        _mapTools.TrackMission(map);
        if (_savedShapes == null)
        {
            // Stopped before the test touched the drawings: they're as the player left them.
        }
        else if (map != null && ReferenceEquals(map, _map) && _mapTools.MissionVersion == _savedMissionVersion)
        {
            _context.Shapes.Restore(_savedShapes);
            if (_savedProgress != null)
            {
                _mapTools.RouteProgress.Restore(_savedProgress);
            }

            if (moveMap && _mapWasOpen != DynamicMap.mapMaximized)
            {
                if (_mapWasOpen)
                {
                    map.Maximize();
                }
                else
                {
                    map.Minimize();
                }
            }
        }
        else
        {
            _context.Shapes.Reset();
        }

        _map = null;
        _savedShapes = null;
        _savedProgress = null;
        _anchors = null;
    }

    /// <summary>
    /// The set, centred where the minimap is now and sized to fit it, so every drawing is on screen on both maps. It's
    /// built again at each switch to it, since the minimap follows the aircraft and fixed drawings built once would slide
    /// out of it, leaving later slices less to draw. The units it rides on are the ones picked for the first set.
    /// </summary>
    private List<MapShape> BuildDrawings(DynamicMap map, DrawingSet set)
    {
        var aircraft = PlayerAircraft()!;
        var global = aircraft.GlobalPosition();
        var nose = aircraft.transform.forward;
        var forward = new FlatVector(nose.x, nose.z);
        var center = new FlatVector(global.x, global.z) +
                     (forward.LengthSquared() > 0f ? FlatVector.Normalize(forward) * MinimapLead : FlatVector.Zero);
        if (_anchors == null)
        {
            _radius = MinimapRadius(map);
            _anchors = NearbyUnits(center, _radius, aircraft.persistentID.Id);
            _log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "Perf test drawings in {0:0.0} km around the minimap's centre, on {1} live units.", _radius / 1000f, _anchors.Count));
        }

        return set == DrawingSet.Typical
            ? StressDrawings.BuildTypical(center, _radius, _anchors, _context.GroundElevation)
            : StressDrawings.BuildHeavy(center, _radius, _anchors, _settings.MapToolMaxPenPoints.Value, _context.GroundElevation);
    }

    /// <summary>What a condition draws, and the index of its count in <see cref="_shown"/>.</summary>
    private enum DrawingSet
    {
        None,
        Typical,
        Heavy,
    }

    /// <summary>Four fifths of half the minimap's width, in meters: its rect in world units over one map meter in world units.</summary>
    private static float MinimapRadius(DynamicMap map)
    {
        var rect = (RectTransform)map.transform;
        var halfWidth = rect.rect.width * 0.5f * rect.lossyScale.x;
        var meter = map.mapDisplayFactor * map.iconLayer.transform.lossyScale.x;
        var radius = meter > 0f ? 0.8f * halfWidth / meter : 0f;
        return radius is > 1000f and < 100000f ? radius : FallbackRadius;
    }

    /// <summary>Units the map shows within the radius, nearest first, at the positions the map shows them.</summary>
    private List<MapPoint> NearbyUnits(FlatVector center, float radius, uint ownId)
    {
        var found = new List<(float Distance, MapPoint Point)>();
        foreach (var unit in UnitRegistry.allUnits)
        {
            if (unit == null || unit.disabled || unit.persistentID.Id == ownId)
            {
                continue;
            }

            var global = unit.GlobalPosition();
            if (_context.TryResolve(new MapPoint(new FlatVector(global.x, global.z), unit.persistentID.Id), out var shown) &&
                FlatVector.Distance(shown, center) < radius)
            {
                found.Add((FlatVector.Distance(shown, center), new MapPoint(shown, unit.persistentID.Id)));
            }
        }

        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var units = new List<MapPoint>();
        for (var i = 0; i < found.Count && i < MaxAnchors; i++)
        {
            units.Add(found[i].Point);
        }

        return units;
    }

    private static Aircraft? PlayerAircraft()
    {
        var hud = SceneSingleton<CombatHUD>.i;
        var aircraft = hud != null ? hud.aircraft : null;
        return aircraft != null && !aircraft.disabled ? aircraft : null;
    }

    private static void Tell(string message)
    {
        var ui = SceneSingleton<GameplayUI>.i;
        if (ui != null)
        {
            ui.GameMessage(message);
        }
    }
}
