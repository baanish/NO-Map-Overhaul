using System.Collections.Generic;
using NoMapOverhaul.Diagnostics;
using TMPro;
using UnityEngine;
using NumericsVector3 = System.Numerics.Vector3;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// Labels pinned to points in the 3D view, which tools request each frame. Each label is a copy of the HUD runway
/// callout, itself a copy of the game's own airbase label, so it shares the HUD canvas, font, and visibility: it hides
/// with the HUD and while the map is open. Placed through the camera like the callout, and hidden when the point is
/// behind it. Rings go to a <see cref="WorldRingPool"/> under the same parent.
/// </summary>
internal sealed class WorldLabelPool : IWorldLabels
{
    /// <summary>Enough for every tool's labels at once; requests past it are dropped rather than growing the HUD.</summary>
    private const int MaxLabels = 32;

    /// <summary>How far past the screen's edge a short label's point may be and still show some of its text. A long one gets up to half its width more.</summary>
    private const float OffScreenPixels = 200f;

    /// <summary>Screen points closer than this count as the same point, so their labels stack instead of overlapping.</summary>
    private const float SamePointPixels = 4f;

    /// <summary>Line height as a share of the font size, for stacking labels on one point.</summary>
    private const float LineSpacing = 1.15f;

    /// <summary>
    /// The extra vertex data TextMeshPro's shader reads (the SDF scale is in TexCoord1); a canvas passes on only what it
    /// asks for, and the labels' own canvas must ask for it as the HUD's does.
    /// </summary>
    private const AdditionalCanvasShaderChannels TextChannels =
        AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;

    private readonly List<TextMeshProUGUI> _labels = new();

    /// <summary>
    /// The labels' container, the last child of the HUD label's parent so the labels draw over the game's HUD, with a
    /// canvas of its own: a label moved or retyped re-batches the labels alone, not the game's whole HUD.
    /// </summary>
    private RectTransform? _layer;

    /// <summary>
    /// Each placed label's point this frame and the stack it joined, by the index of the stack's first label, and each
    /// stack's lowest edge by that index. Labels on one point, such as a bearing and a note on the same unit, stack
    /// downward in the order tools add them, so the bearing stays on top. A label near any label in a stack joins it.
    /// </summary>
    private readonly Vector2[] _points = new Vector2[MaxLabels];
    private readonly int[] _stacks = new int[MaxLabels];
    private readonly float[] _stackBottoms = new float[MaxLabels];
    private readonly WorldRingPool _rings;
    private TextMeshProUGUI? _source;
    private Camera? _camera;
    private int _used;

    public WorldLabelPool(ModSettings settings) => _rings = new WorldRingPool(settings);

    /// <summary>
    /// Starts a frame's requests. A new HUD means a new source label, and leaving the mission destroys the old HUD with
    /// our layer in it.
    /// </summary>
    public void Begin(TextMeshProUGUI? source)
    {
        if (!ReferenceEquals(source, _source) || (_layer is not null && _layer == null))
        {
            Reset();
            _source = source;
        }

        var camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
        _camera = _source != null && camera != null ? camera : null;
        _used = 0;
        var rings = ModTimings.Start();
        _rings.Begin(_source != null ? _source.transform.parent : null, _camera);
        ModTimings.Stop(ModSection.WorldRings, rings);
    }

    public void Add(NumericsVector3 position, string text, ShapeColor color)
    {
        if (_camera == null || _used >= MaxLabels)
        {
            return;
        }

        // Behind the camera, or far enough off screen that none of the text shows. The text is centred on the point, so
        // a long note reaches half its width past it: at most an em per character, in pixels at the HUD canvas's scale.
        var screen = _camera.WorldToScreenPoint(new GlobalPosition(position.X, position.Y, position.Z).ToLocalPosition());
        var fontSize = (int)PlayerSettings.overlayTextSize;
        var margin = OffScreenPixels + 0.5f * text.Length * fontSize * _source!.transform.lossyScale.x;
        if (screen.z <= 0f || screen.x < -margin || screen.x > Screen.width + margin ||
            screen.y < -margin || screen.y > Screen.height + margin)
        {
            return;
        }

        var height = LineCount(text) * fontSize * LineSpacing * _source.transform.lossyScale.y;
        var point = new Vector2(screen.x, screen.y);
        var centerY = screen.y;
        var stack = _used;
        for (var i = 0; i < _used; i++)
        {
            if ((_points[i] - point).sqrMagnitude < SamePointPixels * SamePointPixels)
            {
                stack = _stacks[i];
                centerY = _stackBottoms[stack] - 0.5f * height;
                break;
            }
        }

        _points[_used] = point;
        _stacks[_used] = stack;
        _stackBottoms[stack] = centerY - 0.5f * height;
        var label = Label(_used++);
        label.transform.position = new Vector3(screen.x, centerY, 0f);
        label.text = NoteFont.Filter(text, label.font);
        label.fontSize = fontSize;
        label.color = color.ToColor32();
        label.enabled = true;
    }

    public void Ring(NumericsVector3 center, float radius, ShapeColor color, object owner)
    {
        var rings = ModTimings.Start();
        _rings.Add(center, radius, color, owner);
        ModTimings.Stop(ModSection.WorldRings, rings);
    }

    /// <summary>Hides the labels and rings no tool asked for this frame.</summary>
    public void End()
    {
        for (var i = _used; i < _labels.Count; i++)
        {
            _labels[i].enabled = false;
        }

        var rings = ModTimings.Start();
        _rings.End();
        ModTimings.Stop(ModSection.WorldRings, rings);
    }

    public void Reset()
    {
        if (_layer != null)
        {
            Object.Destroy(_layer.gameObject);
        }

        _layer = null;
        _labels.Clear();
        _rings.Reset();
        _source = null;
        _camera = null;
        _used = 0;
    }

    private RectTransform Layer()
    {
        if (_layer == null)
        {
            var parent = _source!.transform.parent;
            _layer = new GameObject("BaanishWorldLabels", typeof(RectTransform)).GetComponent<RectTransform>();
            _layer.SetParent(parent, false);
            _layer.SetAsLastSibling();
            var canvas = _layer.gameObject.AddComponent<Canvas>();
            var hud = parent.GetComponentInParent<Canvas>();
            canvas.additionalShaderChannels = (hud != null ? hud.additionalShaderChannels : 0) | TextChannels;
        }

        return _layer;
    }

    private static int LineCount(string text)
    {
        var lines = 1;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private TextMeshProUGUI Label(int index)
    {
        if (index < _labels.Count)
        {
            return _labels[index];
        }

        var label = Object.Instantiate(_source!, Layer());
        label.name = "BaanishWorldLabel";
        label.richText = false;
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        _labels.Add(label);
        return label;
    }
}
