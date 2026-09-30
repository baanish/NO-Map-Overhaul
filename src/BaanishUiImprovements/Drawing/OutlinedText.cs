using TMPro;
using UnityEngine;

namespace BaanishUiImprovements.Drawing;

/// <summary>
/// Map text in the HUD label's font with a one-unit dark rim, so small, faint text stays readable over bright map
/// linework. The rim is eight dark copies offset around the face copy. TMP's own SDF outline can't do it: the HUD
/// font's atlas leaves too little padding around each glyph, so an outline wide enough to see draws every glyph's
/// quad as a box, and a narrower one eats into the thin font.
/// The container is scaled by the inverse map scale rather than resizing the text, so zooming regenerates no meshes.
/// </summary>
internal sealed class OutlinedText
{
    private static readonly Vector2[] RimOffsets =
    {
        new(-1f, -1f), new(0f, -1f), new(1f, -1f), new(-1f, 0f), new(1f, 0f), new(-1f, 1f), new(0f, 1f), new(1f, 1f),
    };

    private readonly TextMeshProUGUI[] _rims = new TextMeshProUGUI[RimOffsets.Length];
    private readonly TextMeshProUGUI _face;

    /// <summary>Children draw after their parent, so the rims and the face are siblings under an empty container, face last.</summary>
    public OutlinedText(string name, Transform parent, Vector2 pivot, TextAlignmentOptions alignment)
    {
        Rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        Rect.SetParent(parent, false);
        for (var i = 0; i < RimOffsets.Length; i++)
        {
            _rims[i] = NewText(Rect, RimOffsets[i], pivot, alignment);
        }

        _face = NewText(Rect, Vector2.zero, pivot, alignment);
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

    /// <summary>Every TMP setter here returns early on an unchanged value, so calling this each refresh is cheap.</summary>
    public void Set(string text, float size, Color color, Color rim, TextMeshProUGUI? hudStyle)
    {
        foreach (var copy in _rims)
        {
            Style(copy, text, size, rim, hudStyle);
        }

        Style(_face, text, size, color, hudStyle);
    }

    /// <summary>Which point of the text sits on <see cref="Rect"/>'s position, for a label that must clear what it names. Cheap when unchanged.</summary>
    public void Align(Vector2 pivot, TextAlignmentOptions alignment)
    {
        if (_face.alignment == alignment && _face.rectTransform.pivot == pivot)
        {
            return;
        }

        foreach (var copy in _rims)
        {
            copy.rectTransform.pivot = pivot;
            copy.alignment = alignment;
        }

        _face.rectTransform.pivot = pivot;
        _face.alignment = alignment;
    }

    public void Destroy() => Object.Destroy(Rect.gameObject);

    private static void Style(TextMeshProUGUI copy, string text, float size, Color color, TextMeshProUGUI? hudStyle)
    {
        if (hudStyle != null && copy.font != hudStyle.font)
        {
            copy.font = hudStyle.font;
            copy.fontSharedMaterial = hudStyle.fontSharedMaterial;
            copy.fontStyle = hudStyle.fontStyle;
        }

        copy.text = text;
        copy.fontSize = size;
        copy.color = color;
    }

    private static TextMeshProUGUI NewText(Transform parent, Vector2 offset, Vector2 pivot, TextAlignmentOptions alignment)
    {
        var rect = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.pivot = pivot;
        rect.localPosition = offset;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = alignment;
        return text;
    }
}
