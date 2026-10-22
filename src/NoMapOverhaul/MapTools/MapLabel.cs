using NoMapOverhaul.Drawing;
using TMPro;
using UnityEngine;
using FlatVector = System.Numerics.Vector2;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// One map label: the text, its plate for anything but a note, and a leader when the layout moved it off its
/// arrowhead. A shape's draw sets what it says (<see cref="Set"/>); the layer's placement pass then says where, and
/// adds it to the layer's meshes. The layout works in the frame the labels stand upright in, the screen's, which the
/// heading-up minimap turns against the map's, so a bearing's label sits past its arrowhead as it shows on screen. No
/// label is an object of its own: every label adds its plate and leader to the layer's one mesh of plates
/// (<see cref="AddPlate"/>) and its text, turned upright, to the layer's <see cref="PlateText"/>, or for a note with its
/// rim to the layer's <see cref="NoteText"/> (<see cref="AddText"/>).
/// </summary>
internal sealed class MapLabel
{
    /// <summary>The design's plate: #050E07 at 78%.</summary>
    private const float PlateRimOpacity = 0.55f;
    private const float LeaderOpacity = 0.7f;

    /// <summary>1 px and 1.5 px at 1440p, in icon units.</summary>
    private const float PlateRimWidth = 0.75f;

    private const float LeaderWidth = 1.125f;

    /// <summary>How far the text's world scale may drift before it's typeset again for the glyphs' new SDF scale.</summary>
    internal const float RetypesetScaleChange = 0.01f;

    /// <summary>The design's plate: #050E07 at 78%, the rail's ground.</summary>
    private static readonly Color PlateColor = new(5f / 255f, 14f / 255f, 7f / 255f, 0.78f);

    private readonly PlateText _plateText;
    private readonly NoteText _noteText;
    private readonly PlateText.Glyphs _glyphs = new();

    /// <summary>Made the first time this label is a note, so it covers the notes made before it.</summary>
    private NoteText.Note? _note;

    /// <summary>In the map's frame, in icon units, as the shape drew it. <see cref="Placement"/> gets it in the upright frame.</summary>
    private LabelAnchor _anchor;
    private string? _measuredText;
    private float _measuredSize = -1f;
    private Object? _measuredFont;
    private bool _measuredPlated;
    private float _typesetScale;
    private Vector2 _textSize;
    private Color32 _color;
    private Color32 _rim;
    private bool _plated;

    public MapLabel(PlateText plateText, NoteText noteText)
    {
        _plateText = plateText;
        _noteText = noteText;
    }

    /// <summary>What the layout reads and writes, in icon units, in the upright frame (see <see cref="TurnUpright"/>).</summary>
    public PlacedLabel Placement { get; } = new();

    /// <summary>
    /// The label's text and look, and its anchor in icon units. Measures and typesets the text only when it changes.
    /// Says whether its text or size changed, so the labels need laying out again, or only where it hangs or its colour,
    /// so it keeps its slot and only its plate and text move.
    /// </summary>
    public LabelChange Set(LabelAnchor anchor, string text, float size, Color32 color, Color rim, TextMeshProUGUI? hudStyle, float inverseScale)
    {
        _plated = !LabelLayout.IsNote(anchor.Kind);
        var font = hudStyle != null ? hudStyle.font : null;
        var changed = !ReferenceEquals(text, _measuredText) || size != _measuredSize || !ReferenceEquals(font, _measuredFont) ||
                      _plated != _measuredPlated;
        Color32 rimColor = rim;
        var moved = !anchor.Equals(_anchor) || !Same(color, _color) || !Same(rimColor, _rim);
        if (!_plated)
        {
            _note ??= _noteText.NewNote();
        }

        var scale = _plateText.WorldScale(inverseScale);
        if (changed || Mathf.Abs(scale - _typesetScale) > RetypesetScaleChange * Mathf.Abs(_typesetScale))
        {
            _textSize = _plateText.Typeset(text, size, hudStyle, inverseScale, _glyphs);
            _typesetScale = scale;
            // TextMeshPro generates nothing before its Awake, which waits for an inactive parent; the next call tries again.
            _measuredText = text.Length == 0 || _glyphs.Ready ? text : null;
            changed = true;
        }

        _measuredSize = size;
        _measuredFont = font;
        _measuredPlated = _plated;
        _color = color;
        _rim = rimColor;
        _anchor = anchor;
        Placement.Text = text;
        Placement.HalfSize = LabelLayout.HalfSize(anchor.Kind, new FlatVector(_textSize.x, _textSize.y), size);
        return changed ? LabelChange.Changed : moved ? LabelChange.Moved : LabelChange.None;
    }

    /// <summary>Hands the layout the anchor turned by the angle with this cosine and sine, from the map's frame into the upright one.</summary>
    public void TurnUpright(float cos, float sin) => Placement.Anchor = _anchor.Turned(cos, sin);

    /// <summary>
    /// Adds this label's leader and plate, the leader first since it ends under the plate, in the label canvas's space,
    /// the map's frame. <paramref name="toMap"/> turns the upright frame into it.
    /// </summary>
    public void AddPlate(FeatheredRectBatch plates, Quaternion toMap, float inverseScale)
    {
        if (!_plated && !Placement.Leader)
        {
            return;
        }

        var across = (Vector2)(toMap * Vector3.right);
        var up = (Vector2)(toMap * Vector3.up);
        Vector2 At(FlatVector point) => (across * point.X + up * point.Y) * inverseScale;

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

    /// <summary>
    /// Adds the label's text, centred where the layout put it: a plated label's to the layer's text mesh, a note's
    /// to the layer's notes. <paramref name="toMap"/> turns the upright frame into the map's.
    /// </summary>
    public void AddText(Quaternion toMap, float inverseScale)
    {
        if (_plated)
        {
            _plateText.Add(_glyphs, _color, toMap, ToUnity(Placement.Center), inverseScale);
        }
        else
        {
            _noteText.Add(_note!, _glyphs, _color, _rim, ToUnity(Placement.Center));
        }
    }

    private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

    private static Color32 Faded(Color32 color, float opacity) => new(color.r, color.g, color.b, (byte)(color.a * opacity));

    private static Vector2 ToUnity(FlatVector vector) => new(vector.X, vector.Y);
}

/// <summary>What a label's latest draw changed: nothing, only where it hangs or its colour, or its text or size.</summary>
internal enum LabelChange
{
    None,
    Moved,
    Changed,
}
