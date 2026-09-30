namespace BaanishUiImprovements.MapTools.Waypoint;

/// <summary>Places waypoints on the map, with labels in the 3D view. Not built yet: picking it does nothing.</summary>
public sealed class WaypointTool : MapTool
{
    public WaypointTool(IMapToolContext context)
        : base(context)
    {
    }

    public override string Name => "Waypoint";
}
