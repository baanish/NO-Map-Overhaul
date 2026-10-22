using System.Collections.Generic;
using NoMapOverhaul.Drawing;
using NoMapOverhaul.MapTools.Circle;
using UnityEngine;
using NumericsVector3 = System.Numerics.Vector3;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// One ring of <see cref="WorldRingPool"/> drawn by the <see cref="RingShader"/>: a bare <see cref="CanvasRenderer"/>
/// holding the shared <see cref="RingMesh"/> layout and a material of its own, whose properties place the ring. Setting
/// them redraws the ring without touching the canvas, so a ring that moves re-batches nothing. Only a change of colour,
/// which is in the vertex colours as the CPU's rings have it, sets the mesh again.
/// </summary>
internal sealed class GpuRing
{
    private static readonly int CenterId = Shader.PropertyToID("_RingCenter");
    private static readonly int EastId = Shader.PropertyToID("_RingEast");
    private static readonly int NorthId = Shader.PropertyToID("_RingNorth");
    private static readonly int CameraId = Shader.PropertyToID("_RingCamera");
    private static readonly int StrokeId = Shader.PropertyToID("_RingStroke");

    private static readonly (float Point, float Lane)[] Codes = RingMesh.Codes();
    private static readonly int[] Indices = RingMesh.Indices();
    private static readonly List<Color32> Colors = new(RingMesh.VertexCount);

    /// <summary>
    /// Where the mesh's corners sit, in the canvas's units. The shader ignores positions, but the canvas skips a mesh
    /// whose positions are all one point, so they span far past any screen.
    /// </summary>
    private const float Reach = 100000f;

    private readonly CanvasRenderer _renderer;
    private readonly Material _material;
    private readonly Mesh _mesh;
    private ShapeColor? _color;

    public GpuRing(GameObject owner, Shader shader)
    {
        _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        var positions = new List<Vector3>(RingMesh.VertexCount);
        var codes = new List<Vector2>(RingMesh.VertexCount);
        foreach (var (point, lane) in Codes)
        {
            positions.Add(new Vector3(positions.Count % 2 == 0 ? -Reach : Reach, positions.Count / 2 % 2 == 0 ? -Reach : Reach, 0f));
            codes.Add(new Vector2(point, lane));
        }

        _mesh.SetVertices(positions);
        _mesh.SetUVs(0, codes);
        _mesh.SetTriangles(Indices, 0, false);
        _renderer = owner.AddComponent<CanvasRenderer>();
        _renderer.cullTransparentMesh = false;
        _renderer.materialCount = 1;
        _renderer.SetMaterial(_material, 0);
    }

    /// <summary>Draws the ring this frame, from its centre and its east and north radii in camera space.</summary>
    /// <param name="halfWidth">Half the line's width, in screen pixels.</param>
    public void Draw(NumericsVector3 center, NumericsVector3 east, NumericsVector3 north, ShapeColor color, in ScreenCamera view, float halfWidth)
    {
        if (_renderer.cull)
        {
            _renderer.cull = false;
        }

        if (_color != color)
        {
            _color = color;
            var fill = color.ToColor32();
            var clear = new Color32(fill.r, fill.g, fill.b, 0);
            Colors.Clear();
            foreach (var (_, lane) in Codes)
            {
                Colors.Add(RingMesh.IsFill(lane) ? fill : clear);
            }

            _mesh.SetColors(Colors);
            _renderer.SetMesh(_mesh);
        }

        _material.SetVector(CenterId, new Vector4(center.X, center.Y, center.Z, 0f));
        _material.SetVector(EastId, new Vector4(east.X, east.Y, east.Z, 0f));
        _material.SetVector(NorthId, new Vector4(north.X, north.Y, north.Z, 0f));
        _material.SetVector(CameraId, new Vector4(view.HalfScreen.X, view.HalfScreen.Y, view.FocalPixels, view.Near));
        _material.SetVector(StrokeId, new Vector4(halfWidth, EdgeProfile.AntiAliasWidth, view.Across, view.Up));
    }

    public void Hide()
    {
        if (!_renderer.cull)
        {
            _renderer.cull = true;
        }
    }

    /// <summary>Frees the material and mesh, which outlive the ring's object when the HUD goes.</summary>
    public void Destroy()
    {
        Object.Destroy(_material);
        Object.Destroy(_mesh);
    }
}
