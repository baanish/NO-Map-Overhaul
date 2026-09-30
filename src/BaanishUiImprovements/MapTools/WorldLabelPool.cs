using System.Collections.Generic;
using BaanishUiImprovements.Diagnostics;
using TMPro;
using UnityEngine;
using NumericsVector3 = System.Numerics.Vector3;

namespace BaanishUiImprovements.MapTools;

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

    private readonly List<TextMeshProUGUI> _labels = new();

    /// <summary>
    /// Each placed label's point this frame and the lowest edge its stack reaches. Labels on one point, such as a bearing
    /// and a note on the same unit, stack downward in the order tools add them, so the bearing stays on top.
    /// </summary>
    private readonly Vector2[] _points = new Vector2[MaxLabels];
    private readonly float[] _stackBottoms = new float[MaxLabels];
    private readonly WorldRingPool _rings;
    private TextMeshProUGUI? _source;
    private Camera? _camera;
    private int _used;

    public WorldLabelPool(ModSettings settings) => _rings = new WorldRingPool(settings);

    /// <summary>
    /// Starts a frame's requests. A new HUD means a new source label, and leaving the mission destroys the old HUD with
    /// our copies in it: they share one parent, so if the first is gone, they all are.
    /// </summary>
    public void Begin(TextMeshProUGUI? source)
    {
        if (!ReferenceEquals(source, _source) || (_labels.Count > 0 && _labels[0] == null))
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
        for (var i = 0; i < _used; i++)
        {
            if ((_points[i] - point).sqrMagnitude < SamePointPixels * SamePointPixels)
            {
                centerY = _stackBottoms[i] - 0.5f * height;
                _stackBottoms[i] -= height;
                break;
            }
        }

        _points[_used] = point;
        _stackBottoms[_used] = centerY - 0.5f * height;
        var label = Label(_used++);
        label.transform.position = new Vector3(screen.x, centerY, 0f);
        label.text = text;
        label.fontSize = fontSize;
        label.color = color.ToColor32();
        label.enabled = true;
    }

    public void Ring(NumericsVector3 center, float radius, ShapeColor color)
    {
        var rings = ModTimings.Start();
        _rings.Add(center, radius, color);
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
        foreach (var label in _labels)
        {
            if (label != null)
            {
                Object.Destroy(label.gameObject);
            }
        }

        _labels.Clear();
        _rings.Reset();
        _source = null;
        _camera = null;
        _used = 0;
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

        var label = Object.Instantiate(_source!, _source!.transform.parent);
        label.name = "BaanishWorldLabel";
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        _labels.Add(label);
        return label;
    }
}
