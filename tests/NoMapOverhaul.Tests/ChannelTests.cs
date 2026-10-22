using System.Numerics;
using NoMapOverhaul.MapTools;
using NoMapOverhaul.MapTools.Text;
using static NoMapOverhaul.Tests.Program;

namespace NoMapOverhaul.Tests;

internal static class ChannelTests
{
    /// <summary>The allocation-free check agrees with configuring the name as a one-channel list, on names near every rule.</summary>
    private static void CanonicalNames()
    {
        foreach (var name in new[]
        {
            "a", "alpha", "perf-test", "a.b_c-d 9", "a b", new string('x', 32), new string('x', 33), "", " a", "a ", "Alpha",
            "a,b", "a!", "ä", "\t", "-", " ", "a  b",
        })
        {
            var channels = new DrawingChannels();
            var kept = channels.TryConfigure(name, out _) && channels.Names.Count == 1 && channels.Names[0] == name;
            Expect(DrawingChannels.IsCanonicalName(name) == kept, $"'{name}': expected canonical {kept}");
        }

        Expect(!DrawingChannels.IsCanonicalName(null), "null is not a name");
    }

    public static readonly (string Name, Action Test)[] All =
    {
        ("channels normalize names and deduplicate in first occurrence order", Normalize),
        ("a canonical channel name is exactly what configuring it keeps", CanonicalNames),
        ("empty channel settings select local drawings", Empty),
        ("channel settings accept ten distinct names and reject eleven", CountLimit),
        ("channel names accept 32 characters and reject 33", NameLimit),
        ("channel settings accept 512 characters and reject 513", ConfigurationLimit),
        ("invalid channel characters reject the whole configuration", InvalidCharacters),
        ("channel selection follows its name across reorder and falls back on removal", Selection),
        ("unchanged canonical channel lists preserve peer session identity", NoOpConfiguration),
        ("invalid receive settings preserve both channel lists and selections", InvalidReceivePair),
        ("invalid broadcast settings preserve both channel lists and selections", InvalidBroadcastPair),
        ("channel pairs allow empty and receive-only configurations", EmptyPair),
        ("channel pair selections follow their names across reorder", PairSelection),
        ("broadcast channels receive drawings without receive-only subscriptions", BroadcastReceives),
        ("receive-only channels accept drawings but cannot send or collaborate", ReceiveOnly),
        ("unsubscribed channels cannot send receive or collaborate", UnauthorizedChannels),
        ("collaborative edits require both players to broadcast the channel", CollaborativeDirections),
        ("ten broadcast and ten disjoint receive-only channels keep their separate permissions", DisjointLists),
        ("new drawings keep their chosen channels through undo and redo", AddHistory),
        ("replacing a drawing preserves its original channel through undo and redo", ReplaceHistory),
        ("rejected adds and replacements do not stamp channels", RejectedEdits),
        ("keeping drawings local revokes channels throughout undo and redo without changing geometry", KeepLocalHistory),
        ("keeping drawings local preserves replacement privacy and selected channel for new work", KeepLocalReplacement),
        ("keeping already local drawings does not change the version", KeepLocalNoOp),
    };

    private static void Configure(DrawingChannels channels, string value) =>
        Expect(channels.TryConfigure(value, out var error), error);

    private static void Normalize()
    {
        var channels = new DrawingChannels();
        Configure(channels, "  Medusa ,BRA_1,medusa, , Flight.A-2 ,bra_1");
        ExpectText(string.Join("|", channels.Names), "medusa|bra_1|flight.a-2");
        ExpectText(channels.Selected, "medusa");
    }

    private static void Empty()
    {
        var channels = new DrawingChannels();
        ExpectText(channels.Selected, "");
        Configure(channels, "medusa");
        Configure(channels, " , , ");
        Expect(channels.Names.Count == 0, "expected no subscriptions");
        ExpectText(channels.Selected, "");
        Configure(channels, "");
        ExpectText(channels.Selected, "");
    }

    private static void CountLimit()
    {
        var channels = new DrawingChannels();
        var ten = string.Join(",", Enumerable.Range(1, 10));
        Configure(channels, ten + ",1,2");
        Expect(channels.Names.Count == 10, "expected exactly ten unique channels");
        Expect(!channels.TryConfigure(ten + ",11", out var error), "eleven names must fail");
        ExpectText(error, "List at most 10 channels.");
        ExpectText(string.Join(",", channels.Names), ten);
    }

    private static void NameLimit()
    {
        var channels = new DrawingChannels();
        Configure(channels, " " + new string('A', 32) + " ");
        ExpectText(channels.Selected, new string('a', 32));
        Expect(!channels.TryConfigure(new string('a', 33), out var error), "33 characters must fail");
        ExpectText(error, "Channel names must be at most 32 characters.");
        ExpectText(channels.Selected, new string('a', 32));
    }

    private static void ConfigurationLimit()
    {
        var channels = new DrawingChannels();
        Configure(channels, "a" + new string(' ', 511));
        ExpectText(channels.Selected, "a");
        Expect(!channels.TryConfigure("b" + new string(' ', 512), out var error), "513 characters must fail");
        ExpectText(error, "Channel settings must be at most 512 characters.");
        ExpectText(channels.Selected, "a");
    }

    private static void InvalidCharacters()
    {
        var channels = new DrawingChannels();
        Configure(channels, "one,two");
        Expect(channels.TrySelect(1), "expected second selection");
        foreach (var invalid in new[] { "<tag>", "a/b", "a\\b", "a\tb", "a\nb", "caf\u00e9", "\u00a0one" })
        {
            Expect(!channels.TryConfigure("replacement," + invalid, out var error), "expected rejection: " + invalid);
            Expect(error.Length > 0, "expected an explanation");
            ExpectText(string.Join(",", channels.Names), "one,two");
            ExpectText(channels.Selected, "two");
        }
    }

    private static void Selection()
    {
        var channels = new DrawingChannels();
        Configure(channels, "one,two,three");
        Expect(channels.TrySelect(1), "expected second selection");
        Configure(channels, "THREE,one,TWO");
        ExpectText(channels.Selected, "two");
        Expect(!channels.TrySelect(-1) && !channels.TrySelect(3), "invalid indices must fail");
        ExpectText(channels.Selected, "two");
        Configure(channels, "three,one");
        ExpectText(channels.Selected, "three");
        Configure(channels, string.Join(",", Enumerable.Range(1, 10)));
        Expect(channels.TrySelect(9), "index nine selects channel ten");
        ExpectText(channels.Selected, "10");
        Expect(!channels.TrySelect(10), "index ten is out of range");
    }

    private static void InvalidReceivePair()
    {
        var broadcast = new DrawingChannels();
        var receive = new DrawingChannels();
        Configure(broadcast, "one,two");
        Configure(receive, "three,four");
        Expect(broadcast.TrySelect(1) && receive.TrySelect(1), "select both second channels");
        var broadcastNames = broadcast.Names;
        var receiveNames = receive.Names;
        Expect(!DrawingChannels.TryConfigurePair(broadcast, receive, "replacement", "invalid/receive", out var error),
            "invalid receive settings must reject the pair");
        ExpectText(error, "Receive only: Channels allow only ASCII letters, digits, spaces, dots, underscores and hyphens.");
        Expect(ReferenceEquals(broadcast.Names, broadcastNames) && ReferenceEquals(receive.Names, receiveNames),
            "rejected pair retains both lists");
        ExpectText(broadcast.Selected, "two");
        ExpectText(receive.Selected, "four");
    }

    private static void InvalidBroadcastPair()
    {
        var broadcast = new DrawingChannels();
        var receive = new DrawingChannels();
        Configure(broadcast, "one,two");
        Configure(receive, "three,four");
        Expect(broadcast.TrySelect(1) && receive.TrySelect(1), "select both second channels");
        var broadcastNames = broadcast.Names;
        var receiveNames = receive.Names;
        Expect(!DrawingChannels.TryConfigurePair(broadcast, receive, new string('a', 33), "replacement", out var error),
            "invalid broadcast settings must reject the pair");
        ExpectText(error, "Broadcast: Channel names must be at most 32 characters.");
        Expect(ReferenceEquals(broadcast.Names, broadcastNames) && ReferenceEquals(receive.Names, receiveNames),
            "rejected pair retains both lists");
        ExpectText(broadcast.Selected, "two");
        ExpectText(receive.Selected, "four");
    }

    private static void EmptyPair()
    {
        var broadcast = new DrawingChannels();
        var receive = new DrawingChannels();
        Configure(broadcast, "outgoing");
        Expect(DrawingChannels.TryConfigurePair(broadcast, receive, "", " INCOMING ", out var error), error);
        Expect(broadcast.Names.Count == 0, "receive-only configuration clears broadcasts");
        ExpectText(broadcast.Selected, "");
        ExpectText(string.Join(",", receive.Names), "incoming");
        ExpectText(receive.Selected, "incoming");
        ExpectText(error, "");
        Expect(DrawingChannels.TryConfigurePair(broadcast, receive, "outgoing", "", out error), error);
        ExpectText(broadcast.Selected, "outgoing");
        Expect(receive.Names.Count == 0, "empty receive configuration clears subscriptions");
        ExpectText(receive.Selected, "");
        Expect(DrawingChannels.TryConfigurePair(broadcast, receive, "", "", out error), error);
        Expect(broadcast.Names.Count == 0 && receive.Names.Count == 0, "both directions can be empty");
        ExpectText(broadcast.Selected, "");
        ExpectText(receive.Selected, "");
    }

    private static void PairSelection()
    {
        var broadcast = new DrawingChannels();
        var receive = new DrawingChannels();
        Configure(broadcast, "one,two,three");
        Configure(receive, "four,five,six");
        Expect(broadcast.TrySelect(1) && receive.TrySelect(2), "select non-first channels");
        Expect(DrawingChannels.TryConfigurePair(broadcast, receive, "THREE,TWO,one", "SIX,four,FIVE", out var error), error);
        ExpectText(string.Join(",", broadcast.Names), "three,two,one");
        ExpectText(string.Join(",", receive.Names), "six,four,five");
        ExpectText(broadcast.Selected, "two");
        ExpectText(receive.Selected, "six");
    }

    private static void NoOpConfiguration()
    {
        var broadcast = new DrawingChannels();
        var receive = new DrawingChannels();
        Configure(broadcast, "one,two");
        Configure(receive, "command");
        Expect(broadcast.TrySelect(1), "select second Broadcast channel");
        var broadcastNames = broadcast.Names;
        var receiveNames = receive.Names;
        Configure(broadcast, " ONE, two,one ");
        for (var i = 0; i < 10; i++)
        {
            Expect(DrawingChannels.TryConfigurePair(broadcast, receive, "one, TWO", " Command,command ", out var error), error);
            Expect(ReferenceEquals(broadcast.Names, broadcastNames) && ReferenceEquals(receive.Names, receiveNames),
                "a no-op Apply must not restart either direction's peer session");
            ExpectText(broadcast.Selected, "two");
        }

        Expect(DrawingChannels.TryConfigurePair(broadcast, receive, "two,one", "command", out var reorderError), reorderError);
        Expect(!ReferenceEquals(broadcast.Names, broadcastNames) && ReferenceEquals(receive.Names, receiveNames),
            "only a changed direction gets a new list identity");
        ExpectText(broadcast.Selected, "two");
    }

    private static void BroadcastReceives()
    {
        var command = new[] { "command" };
        var empty = Array.Empty<string>();
        Expect(ChannelRouting.CanSend("command", command, command, empty),
            "two broadcasters exchange drawings without receive-only subscriptions");
        Expect(ChannelRouting.CanReceive("command", command, empty),
            "broadcast membership alone accepts command drawings");
        Expect(ChannelRouting.CanCollaborate("command", command, command),
            "broadcast membership alone permits collaborative erasure");
    }

    private static void ReceiveOnly()
    {
        var command = new[] { "command" };
        var empty = Array.Empty<string>();
        Expect(ChannelRouting.CanSend("command", command, empty, command),
            "broadcaster delivers to a receive-only reader");
        Expect(ChannelRouting.CanReceive("command", empty, command), "receive-only reader accepts command drawings");
        Expect(!ChannelRouting.CanSend("command", empty, command, empty),
            "receive-only reader cannot publish to a broadcaster");
        Expect(!ChannelRouting.CanSend("command", empty, empty, command),
            "receive-only reader cannot publish to another reader");
        Expect(!ChannelRouting.CanCollaborate("command", empty, command) &&
            !ChannelRouting.CanCollaborate("command", command, empty),
            "receive-only membership on either side cannot permit erasure");
    }

    private static void UnauthorizedChannels()
    {
        var command = new[] { "command" };
        var other = new[] { "other" };
        var empty = Array.Empty<string>();
        foreach (var absent in new[] { empty, other })
        {
            Expect(!ChannelRouting.CanSend("command", absent, command, command), "sender must broadcast the target channel");
            Expect(!ChannelRouting.CanSend("command", command, absent, absent), "recipient must subscribe to the target channel");
            Expect(!ChannelRouting.CanReceive("command", absent, absent), "unsubscribed target is rejected");
            Expect(!ChannelRouting.CanCollaborate("command", absent, command) &&
                !ChannelRouting.CanCollaborate("command", command, absent), "both collaborators must broadcast the target");
        }
        Expect(!ChannelRouting.CanSend("other", command, command, command) &&
            !ChannelRouting.CanReceive("other", command, command) &&
            !ChannelRouting.CanCollaborate("other", command, command), "no configured list authorizes a different channel");
    }

    private static void CollaborativeDirections()
    {
        var localBroadcast = new[] { "command", "local" };
        var remoteBroadcast = new[] { "command" };
        Expect(ChannelRouting.CanCollaborate("command", localBroadcast, remoteBroadcast),
            "broadcast membership permits collaboration without identical lists");
        foreach (var missing in new[] { Array.Empty<string>(), new[] { "other" } })
        {
            Expect(!ChannelRouting.CanCollaborate("command", missing, remoteBroadcast), "local nonmember cannot edit");
            Expect(!ChannelRouting.CanCollaborate("command", localBroadcast, missing), "remote nonmember cannot collaborate");
        }
    }

    private static void DisjointLists()
    {
        var broadcast = new DrawingChannels();
        var receiveOnly = new DrawingChannels();
        var writable = Enumerable.Range(1, 10).Select(i => "broadcast-" + i).ToArray();
        var readable = Enumerable.Range(1, 10).Select(i => "receive-" + i).ToArray();
        Expect(DrawingChannels.TryConfigurePair(broadcast, receiveOnly, string.Join(",", writable),
            string.Join(",", readable), out var error), error);
        foreach (var channel in writable)
        {
            Expect(ChannelRouting.CanReceive(channel, broadcast.Names, receiveOnly.Names), "every broadcast channel also receives");
            Expect(ChannelRouting.CanSend(channel, broadcast.Names, writable, readable) &&
                ChannelRouting.CanCollaborate(channel, broadcast.Names, writable), "every broadcast channel retains write permission");
        }
        foreach (var channel in readable)
        {
            Expect(ChannelRouting.CanReceive(channel, broadcast.Names, receiveOnly.Names), "every receive-only channel receives");
            Expect(!ChannelRouting.CanSend(channel, broadcast.Names, readable, writable) &&
                !ChannelRouting.CanCollaborate(channel, broadcast.Names, readable), "receive-only channels never acquire write permission");
        }
    }

    private static TextNote Note(string text) => new(new MapPoint(Vector2.Zero), 0f, text, new ShapeColor(255, 255, 255));

    private static void KeepLocalHistory()
    {
        var store = new ShapeStore { DrawingChannel = "team-one" };
        var original = Note("original");
        var replacement = Note("replacement");
        var redoOnly = Note("redo only");
        Expect(store.Add(original), "add original");
        var id = original.Id;
        Expect(store.Replace(original, replacement) && store.Add(redoOnly) && store.Undo(), "set up live undo and redo versions");
        var current = store.Shapes;
        var version = store.Version;
        store.KeepLocal();
        Expect(store.Version == version + 1 && ReferenceEquals(store.Shapes, current), "only metadata and one version change");
        Expect(original.Id == id && replacement.Id == id && redoOnly.Id != id, "IDs preserved");
        foreach (var shape in new[] { original, replacement, redoOnly }) ExpectText(shape.Channel, "");
        ExpectText(replacement.Text, "replacement");
        Expect(store.Undo() && ReferenceEquals(store.Shapes[0], original), "original history retained");
        ExpectText(store.Shapes[0].Channel, "");
        Expect(store.Redo() && store.Redo() && store.Shapes.Count == 2, "redo history retained");
        Expect(ReferenceEquals(store.Shapes[1], redoOnly), "redo geometry retained");
        ExpectText(store.Shapes[1].Channel, "");
        store.Clear();
        Expect(store.Undo(), "clear remains undoable");
        foreach (var shape in store.Shapes) ExpectText(shape.Channel, "");
    }

    private static void KeepLocalReplacement()
    {
        var store = new ShapeStore { DrawingChannel = "squadron" };
        var original = Note("old team");
        Expect(store.Add(original), "add old work");
        store.KeepLocal();
        ExpectText(store.DrawingChannel, "squadron");
        var replacement = Note("edited old work");
        Expect(store.Replace(original, replacement), "edit retained drawing");
        ExpectText(replacement.Channel, "");
        Expect(store.Undo() && store.Redo(), "undo and redo retained edit");
        ExpectText(store.Shapes[0].Channel, "");
        var fresh = Note("new team");
        Expect(store.Add(fresh), "add new work");
        ExpectText(fresh.Channel, "squadron");
        ExpectText(replacement.Channel, "");
    }

    private static void KeepLocalNoOp()
    {
        var store = new ShapeStore();
        store.KeepLocal();
        Expect(store.Version == 0, "empty store unchanged");
        var local = Note("local");
        Expect(store.Add(local), "add local drawing");
        var version = store.Version;
        store.KeepLocal();
        Expect(store.Version == version && ReferenceEquals(store.Shapes[0], local), "local-only state unchanged");
        store.DrawingChannel = "one";
        Expect(store.Add(Note("shared")), "add shared drawing");
        store.KeepLocal();
        version = store.Version;
        store.KeepLocal();
        Expect(store.Version == version, "repeated revocation unchanged");
    }

    private static void AddHistory()
    {
        var store = new ShapeStore();
        var local = Note("local");
        Expect(store.Add(local), "expected local add");
        ExpectText(local.Channel, "");
        store.DrawingChannel = "one";
        var shared = Note("shared");
        Expect(store.Add(shared), "expected shared add");
        store.DrawingChannel = "two";
        ExpectText(shared.Channel, "one");
        Expect(store.Undo() && store.Redo(), "expected undo and redo");
        ExpectText(store.Shapes[1].Channel, "one");
        store.Clear();
        Expect(store.Undo(), "clear should undo");
        ExpectText(store.Shapes[0].Channel, "");
        ExpectText(store.Shapes[1].Channel, "one");
        store.Reset();
        ExpectText(store.DrawingChannel, "two");
    }

    private static void ReplaceHistory()
    {
        var store = new ShapeStore { DrawingChannel = "one" };
        var original = Note("original");
        Expect(store.Add(original), "expected original add");
        store.DrawingChannel = "two";
        var replacement = Note("replacement");
        Expect(store.Replace(original, replacement), "expected replacement");
        ExpectText(replacement.Channel, "one");
        Expect(replacement.Id == original.Id, "replacement keeps identity");
        Expect(store.Undo(), "expected undo");
        ExpectText(store.Shapes[0].Channel, "one");
        Expect(store.Redo(), "expected redo");
        ExpectText(store.Shapes[0].Channel, "one");
    }

    private static void RejectedEdits()
    {
        var store = new ShapeStore { DrawingChannel = "one", MaxShapes = 1 };
        var original = Note("original");
        Expect(store.Add(original), "expected original add");
        store.DrawingChannel = "two";
        Expect(!store.Add(original), "duplicate add must fail");
        ExpectText(original.Channel, "one");
        var rejected = Note("rejected");
        Expect(!store.Add(rejected), "cap must reject new add");
        ExpectText(rejected.Channel, "");
        Expect(!store.Replace(Note("missing"), rejected), "missing shape must reject replacement");
        ExpectText(rejected.Channel, "");
    }
}
