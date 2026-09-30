using TMPro;
using UnityEngine;

namespace BaanishUiImprovements.Drawing;

/// <summary>
/// Map text in the HUD label's font with a one-unit dark rim, so small, faint text stays readable over bright map
/// linework. The rim is a <see cref="TextRim"/>, the face's glyphs copied eight times around it in one mesh, so a label
/// is two graphics rather than nine. TMP's own SDF outline can't do it: the HUD font's atlas leaves too little padding
/// around each glyph, so an outline wide enough to see draws every glyph's quad as a box, and a narrower one eats into
/// the thin font. The container is scaled by the inverse map scale rather than resizing the text, so zooming
/// regenerates no meshes.
/// </summary>
internal sealed class OutlinedText
{
    /// <summary>How far the text's world scale may drift before the rim is copied again for the face's new SDF scale.</summary>
    private const float RebakeScaleChange = 0.01f;

    private readonly TextRim _rim;
    private readonly TextMeshProUGUI _face;
    private Vector2 _offset;
    private bool _rimmed = true;
    private string? _bakedText;
    private float _bakedSize;
    private Color _bakedRim;
    private Object? _bakedFont;
    private float _bakedScale;

    /// <summary>Children draw after their parent, so the rim and the face are siblings under an empty container, face last.</summary>
    public OutlinedText(string name, Transform parent, Vector2 pivot, TextAlignmentOptions alignment)
    {
        Rect = NewRect(name, parent);
        _rim = NewRect("Rim", Rect).gameObject.AddComponent<TextRim>();
        _rim.raycastTarget = false;
        _face = NewRect("Text", Rect).gameObject.AddComponent<TextMeshProUGUI>();
        _face.rectTransform.pivot = pivot;
        _face.raycastTarget = false;
        _face.enableWordWrapping = false;
        _face.overflowMode = TextOverflowModes.Overflow;
        _face.alignment = alignment;
    }

    public RectTransform Rect { get; }

    public bool Visible
    {
        set
        {
            if (Rect.gameObject.activeSelf != value)
            {
                Rect.gameObject.SetActive(value);
            }
        }
    }

    /// <summary>Off for text on a plate of its own, which needs no rim.</summary>
    public bool Rimmed
    {
        set
        {
            _rimmed = value;
            if (_rim.enabled != value)
            {
                _rim.enabled = value;
                _bakedText = null; // a rim switched off stops following the face
            }
        }
    }

    /// <summary>The text's natural size in the container's units. TextMeshPro caches it until the text or style changes.</summary>
    public Vector2 PreferredSize => new(_face.preferredWidth, _face.preferredHeight);

    /// <summary>
    /// Every TMP setter here returns early on an unchanged value, so calling this each refresh is cheap. Set the
    /// container's scale first: the rim is copied from the face again when the text, its style, or its world scale changed.
    /// </summary>
    public void Set(string text, float size, Color color, Color rim, TextMeshProUGUI? hudStyle)
    {
        if (hudStyle != null && _face.font != hudStyle.font)
        {
            _face.font = hudStyle.font;
            _face.fontSharedMaterial = hudStyle.fontSharedMaterial;
            _face.fontStyle = hudStyle.fontStyle;
        }

        _face.text = text;
        _face.fontSize = size;
        _face.color = color;
        if (!_rimmed)
        {
            return;
        }

        var scale = _face.rectTransform.lossyScale.y;
        if (text == _bakedText && size == _bakedSize && rim == _bakedRim && ReferenceEquals(_face.font, _bakedFont) &&
            Mathf.Abs(scale - _bakedScale) <= RebakeScaleChange * Mathf.Abs(_bakedScale))
        {
            return;
        }

        _face.ForceMeshUpdate(ignoreActiveState: true);
        _rim.Bake(_face, rim);
        // TextMeshPro generates nothing before its Awake, which waits for an inactive parent; the next call tries again.
        _bakedText = text.Length == 0 || _face.textInfo.characterCount > 0 ? text : null;
        _bakedSize = size;
        _bakedRim = rim;
        _bakedFont = _face.font;
        _bakedScale = scale;
    }

    /// <summary>
    /// Which point of the text sits <paramref name="offset"/> from <see cref="Rect"/>'s position, for a label that must
    /// clear what it names. The offset is in the container's own units and frame, so it stays upright with the text
    /// when the container is turned upright on a rotating map. Cheap when unchanged.
    /// </summary>
    public void Align(Vector2 pivot, TextAlignmentOptions alignment, Vector2 offset)
    {
        var relayout = _face.alignment != alignment || _face.rectTransform.pivot != pivot;
        if (!relayout && _offset == offset)
        {
            return;
        }

        _offset = offset;
        _face.rectTransform.pivot = pivot;
        _face.rectTransform.localPosition = offset;
        _face.alignment = alignment;
        _rim.rectTransform.localPosition = offset;
        if (relayout && _rimmed && _bakedText != null)
        {
            _face.ForceMeshUpdate(ignoreActiveState: true);
            _rim.Bake(_face, _bakedRim);
        }
    }

    public void Destroy() => Object.Destroy(Rect.gameObject);

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }
}
