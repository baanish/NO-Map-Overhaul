using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NoMapOverhaul.Drawing;

/// <summary>
/// The dark rim of an <see cref="OutlinedText"/>: the face's glyph quads copied eight times, one unit around it, as one
/// mesh in the face's font material. It sits beside the face with the same origin, so the face's vertices need no
/// moving, and fits its rect around the copies, since a RectMask2D culls a graphic by its rect.
/// Glyphs the face takes from a fallback font live in its sub-meshes and get no rim.
/// </summary>
internal sealed class TextRim : ModGraphic
{
    private static readonly Vector2[] Offsets =
    {
        new(-1f, -1f), new(0f, -1f), new(1f, -1f), new(-1f, 0f), new(1f, 0f), new(-1f, 1f), new(0f, 1f), new(1f, 1f),
    };

    private TMP_Text? _face;
    private Color32 _color;

    public override Texture mainTexture =>
        m_Material != null && m_Material.mainTexture != null ? m_Material.mainTexture : s_WhiteTexture;

    /// <summary>Copies the glyphs the face last generated, so call it after <see cref="TMP_Text.ForceMeshUpdate"/>.</summary>
    public void Bake(TMP_Text face, Color32 color)
    {
        _face = face;
        _color = color;
        if (!ReferenceEquals(m_Material, face.fontSharedMaterial))
        {
            material = face.fontSharedMaterial;
        }

        var info = face.textInfo;
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
            min -= Vector2.one;
            max += Vector2.one;
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
        foreach (var offset in Offsets)
        {
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
