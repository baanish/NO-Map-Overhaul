using System.Collections.Generic;
using NoMapOverhaul.Drawing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// The text of every map label on a plate: waypoint numbers, bearings, and radii. One hidden TextMeshPro typesets each
/// label's text when it changes, and the glyphs it makes are copied into one <see cref="GlyphBatch"/> per font
/// material, rebuilt whenever the labels are placed. So a hundred labels are one graphic rather than a hundred: Unity
/// culls every graphic under the map's mask on every frame and updates every TextMeshPro, whether anything changed or
/// not. The typesetter sits at the labels' own scale, since TextMeshPro bakes the text's world scale into each glyph
/// for its SDF edge. Notes are typeset here too, and drawn with their rims by <see cref="NoteText"/>, over this text.
/// </summary>
internal sealed class PlateText
{
    private const AdditionalCanvasShaderChannels TextChannels =
        AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;

    /// <summary>
    /// Every fallback font material a typeset has handed out, each holding one reference in TextMeshPro's count. The
    /// typesetter's sub-meshes count only the materials of its last text, and TextMeshPro destroys a fallback material
    /// nothing counts, while labels typeset earlier still draw with it. Never released, and shared by every layer the
    /// scenes make: there's one per fallback font.
    /// </summary>
    private static readonly HashSet<Material> Pinned = new();

    private readonly RectTransform _root;
    private readonly RectTransform _scale;
    private readonly TextMeshProUGUI _typesetter;
    private readonly List<GlyphBatch> _batches = new();

    /// <param name="labels">The label canvas; the batches go at its end, so put them after the plates.</param>
    public PlateText(RectTransform labels)
    {
        // TextMeshPro asks its canvas for these as it draws, and this one never draws. The glyphs' SDF scale is in TexCoord1.
        var canvas = labels.GetComponent<Canvas>();
        canvas.additionalShaderChannels |= TextChannels;
        if (canvas.rootCanvas != null)
        {
            canvas.rootCanvas.additionalShaderChannels |= TextChannels;
        }
        _root = NewRect("PlateText", labels);
        _scale = NewRect("Typesetter", labels);
        _typesetter = NewRect("Text", _scale).gameObject.AddComponent<TextMeshProUGUI>();
        _typesetter.enabled = false; // typesets with ignoreActiveState, never drawn, never culled
        _typesetter.raycastTarget = false;
        _typesetter.richText = false; // a note's typed tags stay as typed, as its own text did
        _typesetter.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        _typesetter.enableWordWrapping = false;
        _typesetter.overflowMode = TextOverflowModes.Overflow;
        _typesetter.alignment = TextAlignmentOptions.Center;
    }

    /// <summary>
    /// TextMeshPro has woken and can typeset. It wakes with its object, so under a map made inactive, such as a hidden
    /// minimap, every typeset makes nothing until the map shows.
    /// </summary>
    public bool Awake => _typesetter.textInfo != null;

    /// <summary>A label's text at the labels' scale of <paramref name="inverseScale"/>, in world units, for telling when it needs typesetting again.</summary>
    public float WorldScale(float inverseScale)
    {
        SetScale(inverseScale);
        return _typesetter.rectTransform.lossyScale.y;
    }

    /// <summary>
    /// Typesets <paramref name="text"/> as the label's text would be, centred on its middle, into
    /// <paramref name="glyphs"/>, and returns its natural size in the text's units.
    /// </summary>
    public Vector2 Typeset(string text, float size, TextMeshProUGUI? hudStyle, float inverseScale, Glyphs glyphs)
    {
        SetScale(inverseScale);
        if (hudStyle != null && _typesetter.font != hudStyle.font)
        {
            _typesetter.font = hudStyle.font;
            _typesetter.fontSharedMaterial = hudStyle.fontSharedMaterial;
            _typesetter.fontStyle = hudStyle.fontStyle;
        }

        _typesetter.text = NoteFont.Filter(text, _typesetter.font);
        _typesetter.fontSize = size;
        _typesetter.ForceMeshUpdate(ignoreActiveState: true);
        glyphs.Vertices.Clear();
        glyphs.Materials.Clear();
        glyphs.MaterialIndices.Clear();
        glyphs.Sprites.Clear();
        glyphs.Version++;
        var info = _typesetter.textInfo;
        glyphs.Ready = info != null;
        if (info == null)
        {
            // Not awake yet: the layer was made under an inactive map. No glyphs, so the label tries again next draw.
            return Vector2.zero;
        }

        for (var i = 0; i < info.characterCount; i++)
        {
            var character = info.characterInfo[i];
            if (!character.isVisible)
            {
                continue;
            }

            var mesh = info.meshInfo[character.materialReferenceIndex];
            for (var corner = 0; corner < 4; corner++)
            {
                var index = character.vertexIndex + corner;
                glyphs.Vertices.Add(new UIVertex
                {
                    position = mesh.vertices[index],
                    normal = mesh.normals[index],
                    tangent = mesh.tangents[index],
                    uv0 = mesh.uvs0[index],
                    uv1 = mesh.uvs2[index],
                });
            }

            if (character.materialReferenceIndex > 0 && Pinned.Add(mesh.material))
            {
                TMP_MaterialManager.AddFallbackMaterialReference(mesh.material); // does nothing for a material that isn't a fallback
            }

            glyphs.Materials.Add(mesh.material);
            glyphs.MaterialIndices.Add(character.materialReferenceIndex);
            glyphs.Sprites.Add(character.elementType == TMP_TextElementType.Sprite);
        }

        return new Vector2(_typesetter.preferredWidth, _typesetter.preferredHeight);
    }

    public void Clear()
    {
        foreach (var batch in _batches)
        {
            batch.Clear();
        }
    }

    /// <summary>
    /// Adds a label's glyphs with their middle at <paramref name="center"/>, in the upright frame's icon units, turned
    /// into the map's frame by <paramref name="toMap"/>, as the label's own text would stand.
    /// </summary>
    public void Add(Glyphs glyphs, Color32 color, Quaternion toMap, Vector2 center, float inverseScale)
    {
        var at = (Vector3)center;
        for (var quad = 0; quad < glyphs.Materials.Count; quad++)
        {
            var batch = Batch(glyphs.Materials[quad]);
            if (!batch.HasRoom)
            {
                continue;
            }

            for (var corner = 0; corner < 4; corner++)
            {
                var vertex = glyphs.Vertices[quad * 4 + corner];
                vertex.position = toMap * ((at + vertex.position) * inverseScale);
                vertex.color = color;
                batch.Add(vertex);
            }
        }
    }

    public void Apply()
    {
        foreach (var batch in _batches)
        {
            batch.Apply();
        }
    }

    private void SetScale(float inverseScale)
    {
        var scale = Vector3.one * inverseScale;
        if (_scale.localScale != scale)
        {
            _scale.localScale = scale;
        }
    }

    /// <summary>The batch for a font material: nearly always the HUD font's own, plus one per fallback font a label uses.</summary>
    private GlyphBatch Batch(Material material)
    {
        foreach (var batch in _batches)
        {
            if (ReferenceEquals(batch.material, material))
            {
                return batch;
            }
        }

        var created = NewRect("Glyphs", _root).gameObject.AddComponent<GlyphBatch>();
        created.raycastTarget = false;
        created.material = material;
        _batches.Add(created);
        return created;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>One label's typeset text: four vertices per glyph, and each glyph's font material.</summary>
    public sealed class Glyphs
    {
        public List<UIVertex> Vertices { get; } = new();

        public List<Material> Materials { get; } = new();

        /// <summary>Each glyph's place among the text's materials: 0 for the HUD font, then one per fallback font it used.</summary>
        public List<int> MaterialIndices { get; } = new();

        /// <summary>Each glyph is a sprite, such as an emoji from TextMeshPro's default sprite asset, which isn't tinted the text's colour.</summary>
        public List<bool> Sprites { get; } = new();

        /// <summary>Counts typesets, so a copy of these glyphs can tell it's stale.</summary>
        public int Version { get; set; }

        /// <summary>
        /// The typesetter had woken by the last typeset. Before that it makes nothing, so a label made under an
        /// inactive map, such as a hidden minimap, is typeset again on its next draw.
        /// </summary>
        public bool Ready { get; set; }
    }
}
