using NoMapOverhaul.MapTools;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class ChannelKeyModifiersTests
{
    private enum Key { LeftShift, RightShift, LeftControl, RightControl, LeftAlt, Letter }

    public static readonly (string Name, Action Test)[] All =
    {
        ("channel Shift bindings accept either Shift or both", EitherShiftMatches),
        ("channel Shift bindings require Shift", MissingShiftDoesNotMatch),
        ("channel Shift bindings reject extra keys", ExtraKeysDoNotMatch),
        ("channel Shift combinations still require every other bound key", ShiftCombinationMatches),
        ("channel bindings without modifiers reject held keys", NoModifiersMatch),
        ("channel non-Shift modifiers match exactly as bound", OtherModifiersMatch),
    };

    private static bool Match(Key[] required, params Key[] held) =>
        ChannelKeyModifiers.Match(required, held, Key.LeftShift, Key.RightShift);

    private static void EitherShiftMatches()
    {
        foreach (var required in new[] { new[] { Key.LeftShift }, new[] { Key.RightShift }, new[] { Key.LeftShift, Key.RightShift } })
        {
            Expect(Match(required, Key.LeftShift), "left Shift should match");
            Expect(Match(required, Key.RightShift), "right Shift should match");
            Expect(Match(required, Key.LeftShift, Key.RightShift), "both Shift keys should match");
        }
    }

    private static void MissingShiftDoesNotMatch()
    {
        Expect(!Match(new[] { Key.LeftShift }), "Shift must be held");
        Expect(!Match(new[] { Key.RightShift }, Key.LeftControl), "Control must not replace Shift");
    }

    private static void ExtraKeysDoNotMatch()
    {
        foreach (var extra in new[] { Key.LeftControl, Key.LeftAlt, Key.Letter })
        {
            Expect(!Match(new[] { Key.LeftShift }, Key.RightShift, extra), "an unbound extra key must block the shortcut");
        }
    }

    private static void ShiftCombinationMatches()
    {
        var required = new[] { Key.RightShift, Key.LeftControl, Key.Letter };
        Expect(Match(required, Key.LeftShift, Key.LeftControl, Key.Letter), "Shift may switch sides in a combination");
        Expect(!Match(required, Key.LeftShift, Key.LeftControl), "the bound letter must be held");
        Expect(!Match(required, Key.LeftShift, Key.RightControl, Key.Letter), "Control must keep its bound side");
    }

    private static void NoModifiersMatch()
    {
        Expect(Match(Array.Empty<Key>()), "no modifiers should match no held keys");
        Expect(!Match(Array.Empty<Key>(), Key.LeftShift), "Shift must block an unmodified shortcut");
        Expect(!Match(Array.Empty<Key>(), Key.LeftAlt), "Alt must block an unmodified shortcut");
    }

    private static void OtherModifiersMatch()
    {
        Expect(Match(new[] { Key.LeftControl }, Key.LeftControl), "Control should match its bound side");
        Expect(!Match(new[] { Key.LeftControl }, Key.RightControl), "Control must not switch sides");
        Expect(Match(new[] { Key.LeftAlt, Key.Letter }, Key.Letter, Key.LeftAlt), "modifier order should not matter");
        Expect(!Match(new[] { Key.LeftAlt }), "Alt must be held when bound");
    }
}
