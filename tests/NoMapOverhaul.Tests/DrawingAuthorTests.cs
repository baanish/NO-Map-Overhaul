using System.Numerics;
using NoMapOverhaul.MapTools;
using NoMapOverhaul.MapTools.Pen;
using NoMapOverhaul.Sharing;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class DrawingAuthorTests
{
    public static readonly (string Name, Action Test)[] All =
    {
        ("a teammate's drawing under the cursor names its owner, and the player's own names no one", NamesOwner),
        ("a drawing whose owner the roster can't name reads as a teammate's", UnknownOwner),
        ("the owner's name is looked up only when the drawing under the cursor changes", LooksUpOnChange),
    };

    private static PenStroke Stroke(int id) =>
        new(new[] { Vector2.Zero, Vector2.One }, new ShapeColor(1, 2, 3)) { Id = id, Channel = "a" };

    private static string? Roster(ulong owner) => owner switch { 10 => "Maverick", 20 => "Goose", _ => null };

    private static void NamesOwner()
    {
        var received = new ReceivedDrawings();
        var maverick = Stroke(1);
        var goose = Stroke(1);
        Expect(received.TryReplace(10, new MapShape[] { maverick }) && received.TryReplace(20, new MapShape[] { goose }),
            "accept two teammates");
        var author = new DrawingAuthor();
        author.Hover(maverick, received, Roster);
        ExpectText(author.Name, "Maverick");
        author.Hover(goose, received, Roster);
        ExpectText(author.Name, "Goose");
        author.Hover(Stroke(1), received, Roster);
        ExpectText(author.Name, "");
        author.Hover(goose, received, Roster);
        author.Hover(null, received, Roster);
        ExpectText(author.Name, "");
    }

    private static void UnknownOwner()
    {
        var received = new ReceivedDrawings();
        var stranger = Stroke(1);
        Expect(received.TryReplace(30, new MapShape[] { stranger }), "accept a teammate");
        var author = new DrawingAuthor();
        author.Hover(stranger, received, Roster);
        ExpectText(author.Name, DrawingAuthor.UnknownName);
        author = new DrawingAuthor();
        author.Hover(stranger, received, _ => "");
        ExpectText(author.Name, DrawingAuthor.UnknownName);
        author = new DrawingAuthor();
        author.Hover(stranger, received, null);
        ExpectText(author.Name, DrawingAuthor.UnknownName);
    }

    private static void LooksUpOnChange()
    {
        var received = new ReceivedDrawings();
        var first = Stroke(1);
        var second = Stroke(2);
        Expect(received.TryReplace(10, new MapShape[] { first, second }), "accept a teammate");
        var lookups = 0;
        string? Count(ulong owner)
        {
            lookups++;
            return Roster(owner);
        }

        Func<ulong, string?> nameOf = Count;
        var author = new DrawingAuthor();
        author.Hover(first, received, nameOf);
        var name = author.Name;
        for (var frame = 0; frame < 10; frame++)
        {
            author.Hover(first, received, nameOf);
        }

        Expect(lookups == 1 && ReferenceEquals(author.Name, name), $"expected one lookup and the same name, got {lookups}");
        author.Hover(second, received, nameOf);
        author.Hover(second, received, nameOf);
        Expect(lookups == 2, $"expected a lookup for the new drawing, got {lookups}");
    }
}
