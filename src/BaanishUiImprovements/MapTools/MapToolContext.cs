using BaanishUiImprovements.Tracking;
using UnityEngine;
using FlatVector = System.Numerics.Vector2;
using WorldVector = System.Numerics.Vector3;

namespace BaanishUiImprovements.MapTools;

/// <summary>The game side of <see cref="IMapToolContext"/>: reads the map, the local aircraft, units, and the settings.</summary>
internal sealed class MapToolContext : IMapToolContext
{
    /// <summary>The same drop as <c>DynamicMap.JumpCameraTo</c>: straight down from 10 km above sea level.</summary>
    private const float RayHeight = 10000f;

    private readonly ModSettings _settings;
    private readonly KnownPositions _known = new();
    private readonly RaycastHit[] _groundHits = new RaycastHit[16];

    public MapToolContext(ModSettings settings, ShapeStore shapes)
    {
        _settings = settings;
        Shapes = shapes;
    }

    public ShapeStore Shapes { get; }

    public ShapeColor Color => _settings.MapToolColor.Value.ToShapeColor();

    public DistanceUnit Units =>
        NavFormat.Resolve(_settings.MapToolUnits.Value, PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric);

    public float TextSize => _settings.MapToolTextSize.Value;

    /// <summary>Map units are meters times <c>mapDisplayFactor</c>; the game sizes its icons by the inverse map scale in map units.</summary>
    public float MetersPerIconUnit
    {
        get
        {
            var map = SceneSingleton<DynamicMap>.i;
            return map == null || map.mapDisplayFactor <= 0f ? 1f : 1f / (map.mapDisplayFactor * map.mapImage.transform.localScale.x);
        }
    }

    public MapPoint? OwnAircraft
    {
        get
        {
            var hud = SceneSingleton<CombatHUD>.i;
            var aircraft = hud != null ? hud.aircraft : null;
            if (aircraft == null || aircraft.disabled)
            {
                return null;
            }

            var global = aircraft.GlobalPosition();
            return new MapPoint(new FlatVector(global.x, global.z), aircraft.persistentID.Id);
        }
    }

    public bool TryResolve(MapPoint point, out FlatVector position)
    {
        var found = TryResolveWorld(point, out var world);
        position = new FlatVector(world.X, world.Z);
        return found;
    }

    public bool TryResolveWorld(MapPoint point, out WorldVector position)
    {
        if (!point.IsAnchored)
        {
            position = new WorldVector(point.Position.X, 0f, point.Position.Y);
            return true;
        }

        var state = Locate(point.UnitId, out var global);
        if (!_known.TryResolve(point.UnitId, state, new WorldVector(global.x, global.y, global.z), out position))
        {
            position = new WorldVector(point.Position.X, 0f, point.Position.Y);
        }

        return state == TrackState.Live;
    }

    public float GroundElevation(FlatVector position)
    {
        var sea = new GlobalPosition(position.X, 0f, position.Y).ToLocalPosition();
        var count = Physics.RaycastNonAlloc(sea + Vector3.up * RayHeight, Vector3.down, _groundHits, RayHeight, PhysicsLayers.Everything);
        var top = float.NegativeInfinity;
        for (var i = 0; i < count; i++)
        {
            var hit = _groundHits[i];
            if (hit.point.y > top && hit.collider.GetComponentInParent<Unit>() == null)
            {
                top = hit.point.y;
            }
        }

        return float.IsNegativeInfinity(top) ? 0f : top - Datum.LocalSeaY;
    }

    public bool IsUnitGone(uint unitId) => !TryGetUnit(unitId, out _);

    /// <summary>Units from a mission that ended are gone; the next mission reuses ids.</summary>
    public void ForgetUnits() => _known.Forget();

    /// <summary>
    /// What the player's side knows, read the way the game shows it. With no faction the map shows every unit where it
    /// is (<c>UnitMapIcon.UpdateIcon</c>). Otherwise the position is the faction's (<c>FactionHQ.TryGetKnownPosition</c>):
    /// a friendly's own, a tracked enemy's live while spotted in the last 4 s (<c>IsTargetBeingTracked</c>) and frozen
    /// after. An enemy the faction never tracked is never read, and a destroyed unit is lost, as its icon goes.
    /// </summary>
    private static TrackState Locate(uint unitId, out GlobalPosition position)
    {
        position = default;
        if (!TryGetUnit(unitId, out var unit))
        {
            return TrackState.Unknown;
        }

        var map = SceneSingleton<DynamicMap>.i;
        var hq = map != null ? map.HQ : null;
        if (hq == null)
        {
            position = unit.GlobalPosition();
            return TrackState.Live;
        }

        if (!hq.TryGetKnownPosition(unit, out position))
        {
            return TrackState.Unknown;
        }

        return hq.IsTargetBeingTracked(unit) ? TrackState.Live : TrackState.Stale;
    }

    /// <summary>
    /// The unit while it's in the game: registered, not destroyed, and not disabled. A unit leaves as the game sets
    /// <c>Unit.disabled</c> (on the host and, through its sync, on every client) or destroys it, and either way raises
    /// <c>Unit.onDisableUnit</c>, which takes its map icon away (<c>UnitMapIcon_OnUnitDisabled</c>) and its track
    /// (<c>FactionHQ.DeregisterTrackedUnit</c>).
    /// </summary>
    private static bool TryGetUnit(uint unitId, out Unit unit) =>
        UnitRegistry.TryGetUnit(new PersistentID { Id = unitId }, out unit) && unit != null && !unit.disabled;
}
