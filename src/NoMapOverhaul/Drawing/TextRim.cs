using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// The dark rim of an <see cref="OutlinedText"/>: the face's glyph quads copied eight times around it per ring, as one
/// mesh in the face's font material. A rim wider than one unit gets a ring per unit, evenly spaced out to its width, so
/// the copies leave no gaps between them. It sits beside the face with the same origin, so the face's vertices need no
/// moving, and fits its rect around the copies, since a RectMask2D culls a graphic by its rect.
/// Glyphs the face takes from a fallback font live in its sub-meshes and get no rim.
/// </summary>
internal sealed class TextRim : ModGraphic
{
    internal static readonly Vector2[] Offsets =
    {
        new(-1f, -1f), new(0f, -1f), new(1f, -1f), new(-1f, 0f), new(1f, 0f), new(-1f, 1f), new(0f, 1f), new(1f, 1f),
    };

    private TMP_Text? _face;
    private Color32 _color;
    private float _width;

    public override Texture mainTexture =>
        m_Material != null && m_Material.mainTexture != null ? m_Material.mainTexture : s_WhiteTexture;

    /// <summary>
    /// Copies the glyphs the face last generated, so call it after <see cref="TMP_Text.ForceMeshUpdate"/>. A face that
    /// hasn't woken has generated nothing, and the rim keeps what it had.
    /// </summary>
    public void Bake(TMP_Text face, Color32 color, float width)
    {
        if (face.textInfo is not { } info)
        {
            return;
        }

        _face = face;
        _color = color;
        _width = Mathf.Max(0f, width);
        var rimMaterial = TextRimMaterial.For(face.font, face.fontSharedMaterial);
        if (!ReferenceEquals(m_Material, rimMaterial))
        {
            material = rimMaterial;
        }

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        for (var i = 0; i < info.characterCount; i++)
        {
            var character = info.characterInfo[i];
            if (!character.isVisible || character.materialReferenceIndex != 0)
            {
                continue;
            }

            for (var corner = 0; corner < 4; corner++)
            {
                var position = (Vector2)info.meshInfo[0].vertices[character.vertexIndex + corner];
                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
            }
        }

        if (min.x <= max.x)
        {
            // A pivot at the origin's place in the grown box keeps the origin, and so the face's vertices, where they are.
            min -= Vector2.one * _width;
            max += Vector2.one * _width;
            var size = max - min;
            rectTransform.sizeDelta = size;
            rectTransform.pivot = new Vector2(-min.x / size.x, -min.y / size.y);
        }

        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_face == null || _face.textInfo is not { meshInfo.Length: > 0 } info)
        {
            return;
        }

        var mesh = info.meshInfo[0];
        var rings = Mathf.CeilToInt(_width);
        for (var ring = 1; ring <= rings; ring++)
        foreach (var unit in Offsets)
        {
            var offset = unit * (_width * ring / rings);
            for (var i = 0; i < info.characterCount; i++)
            {
                var character = info.characterInfo[i];
                if (!character.isVisible || character.materialReferenceIndex != 0)
                {
                    continue;
                }

                var first = vh.currentVertCount;
                for (var corner = 0; corner < 4; corner++)
                {
                    var index = character.vertexIndex + corner;
                    vh.AddVert(mesh.vertices[index] + (Vector3)offset, _color, mesh.uvs0[index], mesh.uvs2[index], mesh.normals[index],
                        mesh.tangents[index]);
                }

                // TextMeshPro's own quad order: bottom left, top left, top right, bottom right.
                vh.AddTriangle(first, first + 1, first + 2);
                vh.AddTriangle(first + 2, first + 3, first);
            }
        }
    }
}
