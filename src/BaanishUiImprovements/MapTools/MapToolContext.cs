using System.Collections.Generic;
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
    private readonly Dictionary<uint, WorldVector> _lastSeen = new();

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

        if (TryLocate(point.UnitId, out var global))
        {
            position = new WorldVector(global.x, global.y, global.z);
            _lastSeen[point.UnitId] = position;
            return true;
        }

        if (!_lastSeen.TryGetValue(point.UnitId, out position))
        {
            position = new WorldVector(point.Position.X, 0f, point.Position.Y);
        }

        return false;
    }

    public float GroundElevation(FlatVector position)
    {
        var sea = new GlobalPosition(position.X, 0f, position.Y).ToLocalPosition();
        return Physics.Raycast(sea + Vector3.up * RayHeight, Vector3.down, out var hit, RayHeight, PhysicsLayers.Everything)
            ? hit.point.y - Datum.LocalSeaY
            : 0f;
    }

    /// <summary>Units from a mission that ended are gone; the next mission reuses ids.</summary>
    public void ForgetUnits() => _lastSeen.Clear();

    /// <summary>
    /// The map icon's own rule (<c>UnitMapIcon.UpdateIcon</c>): the faction's tracked position when it tracks the unit,
    /// else the unit itself. An enemy the faction doesn't track isn't read straight from the scene, which would reveal
    /// it, and a destroyed unit counts as lost even while its track lingers.
    /// </summary>
    private static bool TryLocate(uint unitId, out GlobalPosition position)
    {
        position = default;
        var id = new PersistentID { Id = unitId };
        if (!UnitRegistry.TryGetUnit(id, out var unit) || unit == null || unit.disabled)
        {
            return false;
        }

        var map = SceneSingleton<DynamicMap>.i;
        var hq = map != null ? map.HQ : null;
        if (hq != null && hq.GetTrackingData(id) is { } tracking)
        {
            position = tracking.GetPosition();
            return true;
        }

        if (hq == null || unit.NetworkHQ == hq)
        {
            position = unit.GlobalPosition();
            return true;
        }

        return false;
    }
}
