using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NoMapOverhaul.MapTools.Text;
using TMPro;

namespace NoMapOverhaul.MapTools;

/// <summary>Filters note text before TMP can warn about missing glyphs or add them to a dynamic atlas.</summary>
internal sealed class NoteFont
{
    private static readonly ConditionalWeakTable<TMP_FontAsset, NoteFont> Fonts = new();
    private readonly TMP_FontAsset _font;
    private readonly Dictionary<uint, bool> _characters = new();
    private readonly HashSet<TMP_FontAsset> _searched = new();
    private readonly Func<uint, bool> _contains;

    private NoteFont(TMP_FontAsset font)
    {
        _font = font;
        _contains = Contains;
    }

    public static string Filter(string text, TMP_FontAsset? font) => font == null
        ? string.Empty
        : TextTool.ReplaceUnsupportedCharacters(text, Fonts.GetValue(font, Create)._contains);

    private static NoteFont Create(TMP_FontAsset font) => new(font);

    private bool Contains(uint character)
    {
        if (_characters.TryGetValue(character, out var present)) return present;
        _searched.Clear();
        var result = Find(_font, character);
        if (result == Lookup.Missing && TMP_Settings.fallbackFontAssets != null)
        {
            foreach (var fallback in TMP_Settings.fallbackFontAssets)
            {
                result = Find(fallback, character);
                if (result != Lookup.Missing) break;
            }
        }

        if (result == Lookup.Missing) result = Find(TMP_Settings.defaultFontAsset, character);
        present = result == Lookup.Present || result == Lookup.Missing && TMP_Settings.defaultSpriteAsset != null &&
            TMP_SpriteAsset.SearchForSpriteByUnicode(TMP_Settings.defaultSpriteAsset, character, true, out _) != null;
        // Changing snapshots must not retain an unbounded history of rejected characters.
        if (_characters.Count >= 4096) _characters.Clear();
        _characters[character] = present;
        return present;
    }

    private Lookup Find(TMP_FontAsset? font, uint character)
    {
        if (font == null || !_searched.Add(font)) return Lookup.Missing;
        if (font.characterLookupTable.ContainsKey(character)) return Lookup.Present;
        // TMP tries to populate a dynamic font before searching its fallbacks. Stop here even if a later font has
        // the glyph: the per-note limit cannot bound atlas growth across changing snapshots.
        if (font.atlasPopulationMode != AtlasPopulationMode.Static) return Lookup.Dynamic;
        if (font.fallbackFontAssetTable != null)
        {
            foreach (var fallback in font.fallbackFontAssetTable)
            {
                var result = Find(fallback, character);
                if (result != Lookup.Missing) return result;
            }
        }

        return Lookup.Missing;
    }

    private enum Lookup { Missing, Present, Dynamic }
}
