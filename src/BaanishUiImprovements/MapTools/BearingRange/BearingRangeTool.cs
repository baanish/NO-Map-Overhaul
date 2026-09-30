namespace BaanishUiImprovements.MapTools.BearingRange;

/// <summary>Measures bearing and range between two points on the map. Not built yet: picking it does nothing.</summary>
public sealed class BearingRangeTool : MapTool
{
    public BearingRangeTool(IMapToolContext context)
        : base(context)
    {
    }

    public override string Name => "Bearing/range";
}
