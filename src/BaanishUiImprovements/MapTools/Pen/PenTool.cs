namespace BaanishUiImprovements.MapTools.Pen;

/// <summary>Freehand strokes on the map. Not built yet: picking it does nothing.</summary>
public sealed class PenTool : MapTool
{
    public PenTool(IMapToolContext context)
        : base(context)
    {
    }

    public override string Name => "Pen";
}
