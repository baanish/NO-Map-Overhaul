using BaanishUiImprovements.Drawing;
using TMPro;
using UnityEngine;
using FlatVector = System.Numerics.Vector2;

namespace BaanishUiImprovements.MapTools;

/// <summary>
/// One map label: the text, its plate for anything but a note, and a leader when the layout moved it off its
/// arrowhead. A shape's draw sets what it says (<see cref="Set"/>); the layer's placement pass then says where
/// (<see cref="Apply"/>). The layout works in the frame the labels stand upright in, the screen's, which the heading-up
/// minimap turns against the map's, so a bearing's label sits past its arrowhead as it shows on screen. Nothing on a
/// plate is an object of its own: every label adds its plate and leader to the layer's one mesh of plates
/// (<see cref="AddPlate"/>) and its text to the layer's <see cref="PlateText"/> (<see cref="AddText"/>), turned upright.
/// A note is an <see cref="OutlinedText"/>, whose container hangs from the label's origin and is turned upright.
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
    private const float RetypesetScaleChange = 0.01f;

    /// <summary>The design's plate: #050E07 at 78%, the rail's ground.</summary>
    private static readonly Color PlateColor = new(5f / 255f, 14f / 255f, 7f / 255f, 0.78f);

    private readonly Transform _parent;
    private readonly PlateText _plateText;
    private readonly PlateText.Glyphs _glyphs = new();

    /// <summary>Made the first time this label is a note, and hidden while it's on a plate.</summary>
    private OutlinedText? _note;

    /// <summary>In the map's frame, in icon units, as the shape drew it. <see cref="Placement"/> gets it in the upright frame.</summary>
    private LabelAnchor _anchor;
    private string? _measuredText;
    private float _measuredSize = -1f;
    private Object? _measuredFont;
    private bool _measuredPlated;
    private float _typesetScale;
    private Vector2 _textSize;
    private Color32 _color;
    private bool _plated;
    private bool _visible = true;

    public MapLabel(Transform parent, PlateText plateText)
    {
        _parent = parent;
        _plateText = plateText;
    }

    /// <summary>What the layout reads and writes, in icon units, in the upright frame (see <see cref="TurnUpright"/>).</summary>
    public PlacedLabel Placement { get; } = new();

    /// <summary>Off for a label this draw didn't use. A label on a plate shows only through the placement pass.</summary>
    public bool Visible
    {
        set
        {
            _visible = value;
            if (_note != null)
            {
                _note.Visible = value && !_plated;
            }
        }
    }

    /// <summary>
    /// The label's text and look, and its anchor in icon units. Measures and typesets the text only when it changes.
    /// True when anything its placement, plate, or text mesh is built from changed, so the labels need placing again.
    /// </summary>
    public bool Set(LabelAnchor anchor, string text, float size, Color32 color, Color rim, TextMeshProUGUI? hudStyle, float inverseScale)
    {
        _plated = anchor.Kind != LabelKind.Note;
        var font = hudStyle != null ? hudStyle.font : null;
        var changed = !ReferenceEquals(text, _measuredText) || size != _measuredSize || !ReferenceEquals(font, _measuredFont) ||
                      _plated != _measuredPlated;
        var redo = changed || !anchor.Equals(_anchor) || color.r != _color.r || color.g != _color.g || color.b != _color.b || color.a != _color.a;
        if (_plated)
        {
            Visible = _visible;
            var scale = _plateText.WorldScale(inverseScale);
            if (changed || Mathf.Abs(scale - _typesetScale) > RetypesetScaleChange * Mathf.Abs(_typesetScale))
            {
                _textSize = _plateText.Typeset(text, size, hudStyle, inverseScale, _glyphs);
                _typesetScale = scale;
                // TextMeshPro generates nothing before its Awake, which waits for an inactive parent; the next call tries again.
                _measuredText = text.Length == 0 || _glyphs.Vertices.Count > 0 ? text : null;
                redo = true;
            }
        }
        else
        {
            var note = _note ??= NewNote();
            note.Visible = _visible;
            // Unity counts any transform write as a change that re-batches the canvas, and live labels redraw ten times a second.
            var scale = Vector3.one * inverseScale;
            if (note.Rect.localScale != scale)
            {
                note.Rect.localScale = scale;
            }

            note.Set(text, size, color, rim, hudStyle);
            if (changed)
            {
                _textSize = note.PreferredSize;
                _measuredText = text;
            }
        }

        _measuredSize = size;
        _measuredFont = font;
        _measuredPlated = _plated;
        _color = color;
        _anchor = anchor;
        Placement.Text = text;
        Placement.HalfSize = LabelLayout.HalfSize(anchor.Kind, new FlatVector(_textSize.x, _textSize.y), size);
        return redo;
    }

    /// <summary>Hands the layout the anchor turned by the angle with this cosine and sine, from the map's frame into the upright one.</summary>
    public void TurnUpright(float cos, float sin) => Placement.Anchor = _anchor.Turned(cos, sin);

    /// <summary>Moves a note where the layout put it. <paramref name="toMap"/> turns the upright frame back into the map's. Cheap when nothing moved.</summary>
    public void Apply(float inverseScale, Quaternion toMap)
    {
        if (_plated || _note == null)
        {
            return;
        }

        var origin = ToUnity(Placement.Origin);
        var position = toMap * (origin * inverseScale);
        if (_note.Rect.localPosition != position)
        {
            _note.Rect.localPosition = position;
        }

        _note.Align(new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center, ToUnity(Placement.Center) - origin);
    }

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

    /// <summary>Adds a plated label's text, centred on its plate, to the layer's text mesh. <paramref name="toMap"/> turns the upright frame into the map's.</summary>
    public void AddText(Quaternion toMap, float inverseScale)
    {
        if (_plated)
        {
            _plateText.Add(_glyphs, _color, toMap, ToUnity(Placement.Center), inverseScale);
        }
    }

    /// <summary>Turns a note upright on the turning minimap. A label on a plate is turned upright as its text is added.</summary>
    public void StandUpright()
    {
        if (_note != null)
        {
            _note.Rect.rotation = Quaternion.identity;
        }
    }

    public void Destroy() => _note?.Destroy();

    private OutlinedText NewNote()
    {
        var note = new OutlinedText("Label", _parent, new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center);
        note.Rect.rotation = Quaternion.identity; // upright from the start, see MapShapeLayer.KeepUpright
        return note;
    }

    private static Color32 Faded(Color32 color, float opacity) => new(color.r, color.g, color.b, (byte)(color.a * opacity));

    private static Vector2 ToUnity(FlatVector vector) => new(vector.X, vector.Y);
}
