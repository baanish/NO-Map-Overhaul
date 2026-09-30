using System.Collections.Generic;
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

    private readonly List<TextMeshProUGUI> _labels = new();
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
        _rings.Begin(_source != null ? _source.transform.parent : null, _camera);
    }

    public void Add(NumericsVector3 position, string text, ShapeColor color)
    {
        if (_camera == null || _used >= MaxLabels)
        {
            return;
        }

        var screen = _camera.WorldToScreenPoint(new GlobalPosition(position.X, position.Y, position.Z).ToLocalPosition());
        if (screen.z <= 0f)
        {
            return;
        }

        var label = Label(_used++);
        label.transform.position = new Vector3(screen.x, screen.y, 0f);
        label.text = text;
        label.fontSize = (int)PlayerSettings.overlayTextSize;
        label.color = color.ToColor32();
        label.enabled = true;
    }

    public void Ring(NumericsVector3 center, float radius, ShapeColor color) => _rings.Add(center, radius, color);

    /// <summary>Hides the labels and rings no tool asked for this frame.</summary>
    public void End()
    {
        for (var i = _used; i < _labels.Count; i++)
        {
            _labels[i].enabled = false;
        }

        _rings.End();
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
