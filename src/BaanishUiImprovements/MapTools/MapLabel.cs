using BaanishUiImprovements.Drawing;
using TMPro;
using UnityEngine;
using FlatVector = System.Numerics.Vector2;

namespace BaanishUiImprovements.MapTools;

/// <summary>
/// One map label: the text, its plate for anything but a note, and a leader when the layout moved it off its
/// arrowhead. A shape's draw sets what it says (<see cref="Set"/>); the layer's placement pass then says where
/// (<see cref="Apply"/>). The text's container hangs from the label's origin and is turned upright on the minimap. The
/// plate and leader aren't objects of their own: every label adds them to the layer's one mesh of plates
/// (<see cref="AddPlate"/>), turned the same way.
/// </summary>
internal sealed class MapLabel
{
    /// <summary>The design's plate: #050E07 at 78%.</summary>
    private const float PlateRimOpacity = 0.55f;
    private const float LeaderOpacity = 0.7f;

    /// <summary>1 px and 1.5 px at 1440p, in icon units.</summary>
    private const float PlateRimWidth = 0.75f;

    private const float LeaderWidth = 1.125f;

    /// <summary>The design's plate: #050E07 at 78%, the rail's ground.</summary>
    private static readonly Color PlateColor = new(5f / 255f, 14f / 255f, 7f / 255f, 0.78f);

    private readonly OutlinedText _text;
    private string? _measuredText;
    private float _measuredSize = -1f;
    private Object? _measuredFont;
    private Vector2 _textSize;
    private Color32 _color;
    private bool _plated;

    public MapLabel(Transform parent)
    {
        _text = new OutlinedText("Label", parent, new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);
    }

    public RectTransform Rect => _text.Rect;

    /// <summary>What the layout reads and writes, in icon units.</summary>
    public PlacedLabel Placement { get; } = new();

    public bool Visible
    {
        set => _text.Visible = value;
    }

    /// <summary>The label's text and look, and its anchor in icon units. Measures the text only when it changes.</summary>
    public void Set(LabelAnchor anchor, string text, float size, Color32 color, Color rim, TextMeshProUGUI? hudStyle, float inverseScale)
    {
        _plated = anchor.Kind != LabelKind.Note;
        // Unity counts any transform write as a change that re-batches the canvas, and live labels redraw ten times a second.
        var scale = Vector3.one * inverseScale;
        if (Rect.localScale != scale)
        {
            Rect.localScale = scale;
        }

        _text.Rimmed = !_plated;
        _text.Set(text, size, color, rim, hudStyle);
        var font = hudStyle != null ? hudStyle.font : null;
        if (!ReferenceEquals(text, _measuredText) || size != _measuredSize || !ReferenceEquals(font, _measuredFont))
        {
            _measuredText = text;
            _measuredSize = size;
            _measuredFont = font;
            _textSize = _text.PreferredSize;
        }

        _color = color;
        Placement.Anchor = anchor;
        Placement.Text = text;
        Placement.HalfSize = LabelLayout.HalfSize(anchor.Kind, new FlatVector(_textSize.x, _textSize.y), size);
    }

    /// <summary>Moves the label where the layout put it. Cheap when nothing moved.</summary>
    public void Apply(float inverseScale)
    {
        var origin = ToUnity(Placement.Origin);
        var position = (Vector3)(origin * inverseScale);
        if (Rect.localPosition != position)
        {
            Rect.localPosition = position;
        }

        _text.Align(new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center, ToUnity(Placement.Center) - origin);
    }

    /// <summary>
    /// Adds this label's leader and plate, the leader first since it ends under the plate, in the space of the text's
    /// container's parent. <paramref name="upright"/> is the container's local rotation there.
    /// </summary>
    public void AddPlate(FeatheredRectBatch plates, Quaternion upright, float inverseScale)
    {
        if (!_plated && !Placement.Leader)
        {
            return;
        }

        var origin = ToUnity(Placement.Origin);
        var across = (Vector2)(upright * Vector3.right);
        var up = (Vector2)(upright * Vector3.up);
        Vector2 At(FlatVector point) => (origin + Turn(ToUnity(point) - origin)) * inverseScale;
        Vector2 Turn(Vector2 offset) => across * offset.x + up * offset.y;

        if (Placement.Leader)
        {
            plates.AddLine(At(Placement.LeaderFrom), At(Placement.LeaderTo), LeaderWidth * 0.5f * inverseScale, Faded(_color, LeaderOpacity),
                EdgeProfile.AntiAliasWidth * inverseScale);
        }

        if (_plated)
        {
            plates.Add(At(Placement.Center), across, ToUnity(Placement.HalfSize) * inverseScale, PlateColor, PlateRimWidth * inverseScale,
                Faded(_color, PlateRimOpacity), EdgeProfile.AntiAliasWidth * inverseScale);
        }
    }

    private static Color32 Faded(Color32 color, float opacity) => new(color.r, color.g, color.b, (byte)(color.a * opacity));

    private static Vector2 ToUnity(FlatVector vector) => new(vector.X, vector.Y);
}
