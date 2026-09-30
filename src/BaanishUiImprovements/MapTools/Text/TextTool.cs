namespace BaanishUiImprovements.MapTools.Text;

/// <summary>Typed notes on the map. Not built yet: picking it does nothing.</summary>
public sealed class TextTool : MapTool
{
    public TextTool(IMapToolContext context)
        : base(context)
    {
    }

    public override string Name => "Text";
}
