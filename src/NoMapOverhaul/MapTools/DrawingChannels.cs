using System;
using System.Collections.Generic;
using System.Linq;

namespace NoMapOverhaul.MapTools;

/// <summary>Ordered channel names and the channel assigned to new drawings. An empty selection keeps drawings local.</summary>
public sealed class DrawingChannels
{
    public const int MaxChannels = 10;
    public const int MaxNameLength = 32;
    public const int MaxConfigurationLength = 512;

    private IReadOnlyList<string> _names = Array.Empty<string>();

    public IReadOnlyList<string> Names => _names;

    public string Selected { get; private set; } = string.Empty;

    /// <summary>Rejects either invalid direction without changing either list or selection.</summary>
    public static bool TryConfigurePair(DrawingChannels broadcast, DrawingChannels receive,
        string broadcastConfiguration, string receiveConfiguration, out string error)
    {
        var nextBroadcast = new DrawingChannels { _names = broadcast._names, Selected = broadcast.Selected };
        if (!nextBroadcast.TryConfigure(broadcastConfiguration, out error))
        {
            error = "Broadcast: " + error;
            return false;
        }

        var nextReceive = new DrawingChannels { _names = receive._names, Selected = receive.Selected };
        if (!nextReceive.TryConfigure(receiveConfiguration, out error))
        {
            error = "Receive only: " + error;
            return false;
        }

        broadcast._names = nextBroadcast._names;
        broadcast.Selected = nextBroadcast.Selected;
        receive._names = nextReceive._names;
        receive.Selected = nextReceive.Selected;
        return true;
    }

    /// <summary>Rejects the whole configuration on error, preserving the previous names and selection.</summary>
    public bool TryConfigure(string configuration, out string error)
    {
        error = string.Empty;
        if (configuration.Length > MaxConfigurationLength)
        {
            error = "Channel settings must be at most 512 characters.";
            return false;
        }

        var names = new List<string>();
        foreach (var entry in configuration.Split(','))
        {
            foreach (var character in entry)
            {
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') ||
                      (character >= '0' && character <= '9') || character is ' ' or '.' or '_' or '-'))
                {
                    error = "Channels allow only ASCII letters, digits, spaces, dots, underscores and hyphens.";
                    return false;
                }
            }

            var name = entry.Trim(' ').ToLowerInvariant();
            if (name.Length > MaxNameLength)
            {
                error = "Channel names must be at most 32 characters.";
                return false;
            }

            if (name.Length == 0 || names.Contains(name))
            {
                continue;
            }

            if (names.Count == MaxChannels)
            {
                error = "List at most 10 channels.";
                return false;
            }

            names.Add(name);
        }

        // Sharing treats a new list reference as a subscription change and restarts the peer session.
        if (!_names.SequenceEqual(names))
        {
            _names = names.AsReadOnly();
        }
        if (!names.Contains(Selected))
        {
            Selected = names.Count == 0 ? string.Empty : names[0];
        }

        return true;
    }

    /// <summary>
    /// A name exactly as <see cref="TryConfigure"/> keeps it: 1 to 32 allowed characters, lower case, no space at either
    /// end. Allocates nothing, since every drawing in every teammate's snapshot is checked.
    /// </summary>
    public static bool IsCanonicalName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name!.Length > MaxNameLength || name[0] == ' ' || name[name.Length - 1] == ' ')
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') ||
                  character is ' ' or '.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    public bool TrySelect(int index)
    {
        if (index < 0 || index >= _names.Count)
        {
            return false;
        }

        Selected = _names[index];
        return true;
    }
}
