using NoMapOverhaul.MapTools;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

/// <summary>The custom colours' saved list: what's read back from the settings file, what's written, and adding and removing a slot.</summary>
internal static class CustomColorsTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("an empty or blank setting holds no custom colours", EmptySettingHoldsNone),
        ("a saved list reads back in order, in either case and with spaces", SavedListReadsBack),
        ("entries that aren't #RRGGBB are skipped", InvalidEntriesSkipped),
        ("a list past six keeps the first six valid colours", ListPastSixKeepsSix),
        ("a list formats as #RRGGBB and parses back to itself", FormatRoundTrips),
        ("adding fills the next slot, opaque, until all six are used", AddingFillsNextSlot),
        ("removing frees its slot and moves the later ones up", RemovingFreesSlot),
        ("adding a colour a swatch already has saves no copy", AddingExistingColorSavesNothing),
    };

    private static void EmptySettingHoldsNone()
    {
        Expect(CustomColors.Parse(string.Empty).Count == 0, "expected nothing from an empty setting");
        Expect(CustomColors.Parse(" , ,").Count == 0, "expected nothing from blank entries");
        ExpectText(CustomColors.Format(CustomColors.None), string.Empty);
    }

    private static void SavedListReadsBack()
    {
        var colors = CustomColors.Parse("#FF8000, #00ff7f ,#123456");
        ExpectColors(colors, "#FF8000", "#00FF7F", "#123456");
        Expect(colors[1] == new ShapeColor(0, 255, 127), "expected lower-case hex to read the same");
    }

    private static void InvalidEntriesSkipped()
    {
        var colors = CustomColors.Parse("#GG0000,#FFF,FF8000,#FF800080,# FF800,#+F8000,#FF8000,red,##FF800,#00FF00");
        ExpectColors(colors, "#FF8000", "#00FF00");
        Expect(!CustomColors.TryParseHex("#-10000", out _), "expected a sign rejected");
        Expect(!CustomColors.TryParseHex(" #FF8000", out _), "expected untrimmed text rejected");
    }

    private static void ListPastSixKeepsSix()
    {
        var colors = CustomColors.Parse("#000001,bad,#000002,#000003,#000004,#000005,#000006,#000007");
        ExpectColors(colors, "#000001", "#000002", "#000003", "#000004", "#000005", "#000006");
    }

    private static void FormatRoundTrips()
    {
        var colors = new[] { new ShapeColor(255, 140, 26), new ShapeColor(0, 0, 0), new ShapeColor(10, 171, 205) };
        var text = CustomColors.Format(colors);
        ExpectText(text, "#FF8C1A,#000000,#0AABCD");
        var back = CustomColors.Parse(text);
        Expect(back.SequenceEqual(colors), $"expected the list back, got {CustomColors.Format(back)}");
    }

    private static void AddingFillsNextSlot()
    {
        var colors = CustomColors.None;
        for (var i = 1; i <= CustomColors.Max; i++)
        {
            colors = CustomColors.With(colors, new ShapeColor((byte)i, 0, 0, 100));
            Expect(colors.Count == i, $"expected {i} colours, got {colors.Count}");
            Expect(colors[i - 1] == new ShapeColor((byte)i, 0, 0), "expected the new colour last, and opaque");
        }

        var full = CustomColors.With(colors, new ShapeColor(9, 9, 9));
        Expect(ReferenceEquals(full, colors), "expected a full list left as it is");
    }

    private static void RemovingFreesSlot()
    {
        var colors = CustomColors.Parse("#000001,#000002,#000003");
        ExpectColors(CustomColors.Without(colors, 1), "#000001", "#000003");
        ExpectColors(CustomColors.Without(colors, 0), "#000002", "#000003");
        Expect(ReferenceEquals(CustomColors.Without(colors, 3), colors), "expected an empty slot's removal to change nothing");
        Expect(ReferenceEquals(CustomColors.Without(colors, -1), colors), "expected a slot before the first to change nothing");
        var one = CustomColors.Parse("#000001");
        Expect(CustomColors.Without(one, 0).Count == 0, "expected the last colour's removal to leave none");
        ExpectColors(CustomColors.With(CustomColors.Without(colors, 0), new ShapeColor(0, 0, 4)), "#000002", "#000003", "#000004");
    }

    /// <summary>A built-in or custom swatch's RGB, whatever the alpha, leaves the list as it is; one step off is a new colour.</summary>
    private static void AddingExistingColorSavesNothing()
    {
        var colors = CustomColors.Parse("#000001,#FF8000");
        Expect(ReferenceEquals(CustomColors.With(colors, ShapeColor.Palette[2]), colors), "expected a built-in swatch's colour left out");
        Expect(ReferenceEquals(CustomColors.With(colors, new ShapeColor(255, 128, 0, 100)), colors), "expected a custom swatch's colour left out");
        ExpectColors(CustomColors.With(colors, new ShapeColor(255, 128, 1)), "#000001", "#FF8000", "#FF8001");
    }

    private static void ExpectColors(IReadOnlyList<ShapeColor> colors, params string[] hex) =>
        ExpectText(CustomColors.Format(colors), string.Join(",", hex));
}
