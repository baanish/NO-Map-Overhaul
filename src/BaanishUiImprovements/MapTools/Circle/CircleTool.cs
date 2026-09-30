namespace BaanishUiImprovements.MapTools.Circle;

/// <summary>Circles on the map, such as a threat ring around a SAM. Not built yet: picking it does nothing.</summary>
public sealed class CircleTool : MapTool
{
    public CircleTool(IMapToolContext context)
        : base(context)
    {
    }

    public override string Name => "Circle";
}
