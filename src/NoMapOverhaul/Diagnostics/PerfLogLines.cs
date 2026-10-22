using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NoMapOverhaul.Sharing;

namespace NoMapOverhaul.Diagnostics;

/// <summary>
/// The performance log's second and third lines per window, after <see cref="FrameSummary.LogLine"/>: what Unity spent on
/// UI and rendering, how often the mod rebuilt things, the session around it, and each teammate's sharing. Formatting
/// only, once per window, so Unity-free and unit-tested.
/// </summary>
public static class PerfLogLines
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// The detail line. <paramref name="render"/> is null while the perf test holds the canvas timer; its counters are
    /// NaN where this build of the game doesn't record them.
    /// </summary>
    public static string Detail(RenderSummary? render, float canvasWorstMs, ActivityCounts activity, PerfContext context)
    {
        var text = new StringBuilder("Perf detail: ");
        if (render is { } r)
        {
            text.Append("Unity canvas update ").Append(Ms(r.CanvasMs)).Append(" avg, ").Append(Ms(canvasWorstMs))
                .Append(" worst frame; per frame ").Append(Count(r.Batches)).Append(" batches, ").Append(Count(r.SetPassCalls))
                .Append(" SetPass calls, ").Append(Count(r.DrawCalls)).Append(" draw calls, ").Append(Count(r.Vertices))
                .Append(" vertices.");
        }
        else
        {
            text.Append("Unity canvas and render counters are the perf test's while it runs.");
        }

        text.Append(string.Format(Invariant, " Rebuilt label layouts {0}x, meshes {1}x, received drawings {2}x.",
            activity.LabelLayouts, activity.MeshRebuilds, activity.ReceivedSyncs));
        text.Append(activity.RingPath switch
        {
            RingPath.Gpu => string.Format(Invariant, " 3D rings {0} at most, on the GPU.", activity.MostRings),
            RingPath.Cpu => string.Format(Invariant, " 3D rings {0} at most, on the CPU.", activity.MostRings),
            _ => " 3D rings not drawn.",
        });
        text.Append(' ').Append(context.Game).Append(", ")
            .Append(context.TeamPlayers < 0 ? "no team" : context.TeamPlayers.ToString(Invariant) + " on the team").Append(", ")
            .Append(context.MapIcons < 0 ? "no map" : context.MapIcons.ToString(Invariant) + " map icons, " + (context.FullMap ? "full map" : "minimap"))
            .Append(", ").Append(float.IsNaN(context.MissionSeconds) ? "no mission" : "mission time " + MissionTime(context.MissionSeconds))
            .Append('.');
        return text.ToString();
    }

    /// <summary>
    /// The teammates line, or null with none to report. Teammates not paired and silent all window, such as those
    /// without the mod, are only counted, so a full team keeps the line short.
    /// </summary>
    public static string? Teammates(IReadOnlyList<TeammateWindow> teammates)
    {
        if (teammates.Count == 0)
        {
            return null;
        }

        var text = new StringBuilder("Perf teammates:");
        var idle = 0;
        foreach (var mate in teammates)
        {
            var traffic = mate.Traffic;
            if (!mate.Ready && mate.Held == 0 && traffic.SentMessages == 0 && traffic.ReceivedMessages == 0)
            {
                idle++;
                continue;
            }

            text.Append(string.Format(Invariant,
                " peer {0} {1}: drawings {2} held, {3} shown; {4} snapshots applied; in {5} ({6:0.0} KB), out {7} ({8:0.0} KB); ",
                mate.Label, mate.Ready ? "ready" : "not ready", mate.Held, mate.Shown, traffic.SnapshotsApplied,
                traffic.ReceivedMessages, traffic.ReceivedBytes / 1024.0, traffic.SentMessages, traffic.SentBytes / 1024.0));
            if (mate.Session is { } session)
            {
                text.Append(string.Format(Invariant, "Steam {0}, ping {1} ms, quality {2}, reliable {3} pending, {4} unacked, queue {5:0.0} ms.",
                    session.State, session.PingMs, session.Quality < 0f ? "n/a" : (session.Quality * 100f).ToString("0", Invariant) + "%",
                    Bytes(session.PendingReliableBytes), Bytes(session.UnackedReliableBytes), session.QueueMs));
            }
            else
            {
                text.Append("Steam n/a.");
            }
        }

        if (idle > 0)
        {
            text.Append(string.Format(Invariant, " {0} not ready with no traffic.", idle));
        }

        return text.ToString();
    }

    /// <summary>Mission time as m:ss, or h:mm:ss from an hour.</summary>
    public static string MissionTime(float seconds)
    {
        var whole = (int)System.Math.Max(0f, seconds);
        return whole >= 3600
            ? string.Format(Invariant, "{0}:{1:00}:{2:00}", whole / 3600, whole / 60 % 60, whole % 60)
            : string.Format(Invariant, "{0}:{1:00}", whole / 60, whole % 60);
    }

    private static string Ms(float ms) => float.IsNaN(ms) ? "n/a" : ms.ToString("0.00", Invariant) + " ms";

    private static string Count(float value) => float.IsNaN(value) ? "n/a" : value.ToString("0", Invariant);

    private static string Bytes(int bytes) =>
        bytes < 1024 ? bytes.ToString(Invariant) + " B" : (bytes / 1024.0).ToString("0.0", Invariant) + " KB";
}

/// <summary>Where the player was over a logged window, read once at its end.</summary>
public readonly struct PerfContext
{
    public PerfContext(string game, int teamPlayers, int mapIcons, bool fullMap, float missionSeconds)
    {
        Game = game;
        TeamPlayers = teamPlayers;
        MapIcons = mapIcons;
        FullMap = fullMap;
        MissionSeconds = missionSeconds;
    }

    /// <summary>The game's state, such as Multiplayer or SinglePlayer.</summary>
    public string Game { get; }

    /// <summary>Players on the local player's side, the local player too; negative with no local player.</summary>
    public int TeamPlayers { get; }

    /// <summary>The map's unit and airbase icons; negative with no map.</summary>
    public int MapIcons { get; }

    public bool FullMap { get; }

    /// <summary>NaN outside a mission.</summary>
    public float MissionSeconds { get; }
}

/// <summary>One teammate over a logged window, by the network log's anonymous label.</summary>
public readonly struct TeammateWindow
{
    public TeammateWindow(int label, bool ready, int held, int shown, DrawingTraffic traffic, DrawingSessionStatus? session)
    {
        Label = label;
        Ready = ready;
        Held = held;
        Shown = shown;
        Traffic = traffic;
        Session = session;
    }

    /// <summary>The <c>peer N</c> number the network log uses, local to this client.</summary>
    public int Label { get; }

    /// <summary>The handshake is complete, so drawings flow.</summary>
    public bool Ready { get; }

    /// <summary>The drawings the teammate's last snapshot left with this client.</summary>
    public int Held { get; }

    /// <summary>Those of <see cref="Held"/> drawn on the map.</summary>
    public int Shown { get; }

    public DrawingTraffic Traffic { get; }

    /// <summary>Steam's status of the session, or null with no session, as always on the loopback test transport.</summary>
    public DrawingSessionStatus? Session { get; }
}
