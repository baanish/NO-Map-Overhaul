using BaanishUiImprovements.Drawing;
using TMPro;
using UnityEngine;
using FlatVector = System.Numerics.Vector2;

namespace BaanishUiImprovements.MapTools;

/// <summary>
/// One map label's graphics: the text, its plate for anything but a note, and a leader when the layout moved it off
/// its arrowhead. A shape's draw sets what it says (<see cref="Set"/>); the layer's placement pass then says where
/// (<see cref="Apply"/>). The container hangs from the label's origin and is turned upright on the minimap, so the
/// plate and leader, its children, turn with it.
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
    private FeatheredRect? _plate;
    private StrokeGraphic? _leader;
    private (Vector2 From, Vector2 To, Color32 Color) _leaderShown;
    private string? _measuredText;
    private float _measuredSize = -1f;
    private Object? _measuredFont;
    private Vector2 _textSize;
    private Color32 _color;

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
        var plated = anchor.Kind != LabelKind.Note;
        Rect.localScale = Vector3.one * inverseScale;
        _text.Rimmed = !plated;
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
        if (_plate != null)
        {
            _plate.enabled = plated;
        }
        else if (plated)
        {
            _plate = NewChild<FeatheredRect>("Plate");
            _plate.rectTransform.SetAsFirstSibling();
        }
    }

    /// <summary>Moves the label where the layout put it. Cheap when nothing moved.</summary>
    public void Apply(float inverseScale)
    {
        var origin = ToUnity(Placement.Origin);
        var offset = ToUnity(Placement.Center) - origin;
        Rect.localPosition = origin * inverseScale;
        _text.Align(new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center, offset);
        if (_plate != null && _plate.enabled)
        {
            var plate = _plate.rectTransform;
            var size = 2f * ToUnity(Placement.HalfSize);
            if ((Vector2)plate.localPosition != offset || plate.sizeDelta != size)
            {
                plate.localPosition = offset;
                plate.sizeDelta = size;
            }

            _plate.color = PlateColor;
            _plate.SetEdges(PlateRimWidth, Faded(_color, PlateRimOpacity), EdgeProfile.AntiAliasWidth);
        }

        ShowLeader(origin);
    }

    private void ShowLeader(Vector2 origin)
    {
        if (!Placement.Leader)
        {
            if (_leader != null)
            {
                _leader.enabled = false;
            }

            return;
        }

        if (_leader == null)
        {
            _leader = NewChild<StrokeGraphic>("Leader");
            _leader.rectTransform.SetAsFirstSibling(); // under the plate, which it ends at
        }

        _leader.enabled = true;
        var from = ToUnity(Placement.LeaderFrom) - origin;
        var to = ToUnity(Placement.LeaderTo) - origin;
        var color = Faded(_color, LeaderOpacity);
        if (from == _leaderShown.From && to == _leaderShown.To && ColorsEqual(color, _leaderShown.Color))
        {
            return;
        }

        _leaderShown = (from, to, color);
        _leader.Clear();
        _leader.AddPoint(from);
        _leader.AddPoint(to);
        _leader.EndStroke(false, LeaderWidth * 0.5f, color, 0f, color, EdgeProfile.AntiAliasWidth);
        _leader.Apply();
    }

    private T NewChild<T>(string name)
        where T : UnityEngine.UI.Graphic
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(Rect, false);
        var graphic = rect.gameObject.AddComponent<T>();
        graphic.raycastTarget = false;
        return graphic;
    }

    private static Color32 Faded(Color32 color, float opacity) => new(color.r, color.g, color.b, (byte)(color.a * opacity));

    private static bool ColorsEqual(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

    private static Vector2 ToUnity(FlatVector vector) => new(vector.X, vector.Y);
}
