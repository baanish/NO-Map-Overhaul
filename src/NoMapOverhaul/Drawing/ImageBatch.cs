using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// Any number of copies of one Image, each at its own place, size, and colour, as one mesh: one graphic for Unity to
/// cull and batch instead of one per copy. The copies share one turn, so a whole set can be stood upright on a turning
/// map by rebuilding the mesh rather than writing a transform per copy. Positions are in this graphic's parent space;
/// <see cref="Apply"/> fits the rect around the copies, since a RectMask2D culls a graphic by its rect.
/// </summary>
internal sealed class ImageBatch : ModGraphic
{
    private readonly List<(Vector2 Center, float Scale, Color32 Color)> _items = new();
    private readonly List<UIVertex> _shape = new();
    private ImageTemplate? _template;
    private GameObject? _source;
    private Quaternion _turn = Quaternion.identity;

    /// <summary>How far the Image's rect reaches from its pivot, turned any way.</summary>
    private float _reach;

    private bool _wasEmpty = true;

    public override Texture mainTexture => _template != null ? _template.mainTexture : s_WhiteTexture;

    /// <summary>Every copy's turn in the parent's space. Rebuilds the mesh when it changes.</summary>
    public Quaternion Turn
    {
        set
        {
            if (value.Equals(_turn))
            {
                return;
            }

            _turn = value;
            if (_items.Count > 0)
            {
                SetVerticesDirty();
            }
        }
    }

    /// <summary>Copies the Image on <paramref name="source"/>'s root. False, drawing nothing, if it has none.</summary>
    public bool Use(GameObject source)
    {
        if (ReferenceEquals(source, _source))
        {
            return _template != null;
        }

        _source = source;
        if (!source.TryGetComponent<Image>(out var image))
        {
            return false;
        }

        if (_template == null)
        {
            var rect = new GameObject("Template", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            _template = rect.gameObject.AddComponent<ImageTemplate>();
            _template.enabled = false;
            _template.raycastTarget = false;
        }

        _template.sprite = image.sprite;
        _template.type = image.type;
        _template.preserveAspect = image.preserveAspect;
        _template.fillCenter = image.fillCenter;
        _template.fillMethod = image.fillMethod;
        _template.fillAmount = image.fillAmount;
        _template.fillClockwise = image.fillClockwise;
        _template.fillOrigin = image.fillOrigin;
        _template.useSpriteMesh = image.useSpriteMesh;
        _template.pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
        var sourceRect = image.rectTransform;
        _template.rectTransform.pivot = sourceRect.pivot;
        _template.rectTransform.sizeDelta = sourceRect.rect.size;
        var size = sourceRect.rect.size;
        var pivot = sourceRect.pivot;
        _reach = new Vector2(Mathf.Max(pivot.x, 1f - pivot.x) * size.x, Mathf.Max(pivot.y, 1f - pivot.y) * size.y).magnitude;
        material = image.material;
        SetVerticesDirty();
        return true;
    }

    public void Clear() => _items.Clear();

    /// <param name="scale">The copy's size, as a multiple of the Image's.</param>
    public void Add(Vector2 center, float scale, Color32 color) => _items.Add((center, scale, color));

    /// <summary>Fits the rect and queues the mesh rebuild. Skips the rebuild when the batch was empty and still is.</summary>
    public void Apply()
    {
        var empty = _items.Count == 0;
        if (empty && _wasEmpty)
        {
            return;
        }

        _wasEmpty = empty;
        if (!empty)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var (center, scale, _) in _items)
            {
                var reach = Vector2.one * (_reach * scale);
                min = Vector2.Min(min, center - reach);
                max = Vector2.Max(max, center + reach);
            }

            rectTransform.localPosition = (min + max) * 0.5f;
            rectTransform.sizeDelta = max - min;
        }

        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_template == null || _items.Count == 0)
        {
            return;
        }

        _template.Generate(vh);
        vh.GetUIVertexStream(_shape); // three vertices per triangle
        vh.Clear();
        var origin = (Vector2)rectTransform.localPosition;
        foreach (var (center, scale, color) in _items)
        {
            var offset = center - origin;
            for (var i = 0; i + 2 < _shape.Count; i += 3)
            {
                var first = vh.currentVertCount;
                for (var corner = 0; corner < 3; corner++)
                {
                    var vertex = _shape[i + corner];
                    vertex.position = (Vector3)offset + _turn * (vertex.position * scale);
                    vertex.color = color;
                    vh.AddVert(vertex);
                }

                vh.AddTriangle(first, first + 1, first + 2);
            }
        }
    }
}
