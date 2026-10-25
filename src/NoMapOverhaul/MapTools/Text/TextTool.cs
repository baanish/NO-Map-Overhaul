using System;
using System.Globalization;
using System.Text;

namespace NoMapOverhaul.MapTools.Text;

/// <summary>
/// Typed notes: click the map, type, and Enter places the text there in the picked colour; Escape drops it. While
/// typing, the keyboard belongs to the tool (see <see cref="MapTool.CapturesKeyboard"/>), so keys don't fly the plane.
/// Clicking elsewhere mid-note places it and starts the next. A click on or just beside a unit's icon puts the note on
/// that unit, beside its icon, and it follows the unit. Each note also shows in the 3D view, on its unit or pinned to
/// the ground under it.
/// </summary>
public sealed class TextTool : MapTool
{
    /// <summary>Longest note in UTF-16 code units. A surrogate pair uses two units and is accepted or dropped together.</summary>
    public const int MaxLength = 64;

    private const string Caret = "_";
    private const string IdleStatus = "Click the map or a unit to place text.";
    private const string TypingStatus = "Type, then Enter to place or Esc to cancel.";

    private static readonly string MaxLengthText = MaxLength.ToString(CultureInfo.InvariantCulture);

    private MapPoint _at;
    private string _text = string.Empty;
    private string _preview = Caret;
    private string _counter = string.Empty;
    private bool _typing;
    private char _pendingHighSurrogate;

    public TextTool(IMapToolContext context)
        : base(context)
    {
    }

    public override string Name => "Text";

    public override bool CapturesKeyboard => _typing;

    public override bool InProgress => _typing;

    /// <summary>Empty at the shape cap, so the menu shows its own limit message.</summary>
    public override string Status => _typing ? TypingStatus : Context.Shapes.IsFull ? string.Empty : IdleStatus;

    /// <summary>"8/64" while typing: characters typed out of <see cref="MaxLength"/>.</summary>
    public override string Counter => _typing ? _counter : string.Empty;

    public override void OnDeactivate() => Cancel();

    public override void OnMissionStart() => Cancel();

    public override void OnClick(MapPointer pointer)
    {
        Place();
        InvalidateOverlay();
        if (Context.Shapes.IsFull)
        {
            return;
        }

        _at = pointer.Point;
        _typing = true;
        SetText(string.Empty);
    }

    public override void OnTextInput(char character)
    {
        if (!_typing)
        {
            return;
        }

        InvalidateOverlay();
        var pending = _pendingHighSurrogate;
        _pendingHighSurrogate = '\0';
        if (char.IsHighSurrogate(character))
        {
            if (_text.Length <= MaxLength - 2)
            {
                _pendingHighSurrogate = character;
            }

            return;
        }

        if (char.IsLowSurrogate(character))
        {
            if (pending != '\0')
            {
                SetText(_text + pending + character);
            }

            return;
        }

        switch (character)
        {
            case '\n':
                Place();
                break;
            case '\u001b':
                Cancel();
                break;
            case '\b':
                if (pending == '\0' && _text.Length > 0)
                {
                    var count = char.IsLowSurrogate(_text[_text.Length - 1]) ? 2 : 1;
                    SetText(_text.Substring(0, _text.Length - count));
                }

                break;
            default:
                if (IsAllowedCharacter(character) && _text.Length < MaxLength)
                {
                    SetText(_text + character);
                }

                break;
        }
    }

    public override void DrawOverlay(IMapCanvas canvas)
    {
        if (_typing)
        {
            TextNote.DrawLabel(canvas, _at, _preview, _preview, Context.Color);
        }
    }

    /// <summary>
    /// Reads the store each frame, so a note undone or erased loses its 3D label at once. A note on a unit that's gone
    /// goes with the unit's map icon, and out of the undo history too, so nothing brings it back onto a dead unit
    /// (<see cref="ShapeStore.Forget"/>). A note an undo or redo brings back onto a unit that went meanwhile goes the
    /// same frame, before the map draws it.
    /// </summary>
    public override void OnFrame(IWorldLabels labels)
    {
        var shapes = Context.Shapes.Shapes; // Forget replaces the store's lists and leaves this one as it was
        for (var i = 0; i < shapes.Count; i++)
        {
            if (shapes[i] is not TextNote note)
            {
                continue;
            }

            if (note.At.IsAnchored && Context.IsUnitGone(note.At.UnitId))
            {
                Context.Shapes.Forget(note);
                continue;
            }

            note.AddWorldLabel(Context, labels);
        }
    }

    /// <summary>
    /// A single-line note without directional overrides, and more than whitespace and invisible format
    /// characters such as a soft hyphen. Shared notes must pass this too, so a note the tool keeps can always be sent.
    /// </summary>
    public static bool IsPlaceable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text!.Length > MaxLength) return false;
        var hasVisibleText = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (!IsAllowedCharacter(text[i])) return false;
            if (!char.IsWhiteSpace(text[i]) && CharUnicodeInfo.GetUnicodeCategory(text, i) != UnicodeCategory.Format)
                hasVisibleText = true;
            if (char.IsSurrogatePair(text, i)) i++;
        }
        return hasVisibleText;
    }

    private static bool IsAllowedCharacter(char character)
    {
        var category = char.GetUnicodeCategory(character);
        return category != UnicodeCategory.Control && category != UnicodeCategory.LineSeparator &&
               category != UnicodeCategory.ParagraphSeparator &&
               !(character >= '\u202a' && character <= '\u202e') && !(character >= '\u2066' && character <= '\u2069');
    }

    /// <summary>
    /// Notes use only existing glyphs. Missing Unicode scalars become one question mark, including a whole surrogate
    /// pair. A font without that fixed replacement shows no text rather than asking TMP to choose another glyph.
    /// </summary>
    internal static string ReplaceUnsupportedCharacters(string text, Func<uint, bool> canRender)
    {
        if (!canRender('?')) return string.Empty;
        StringBuilder? filtered = null;
        for (var i = 0; i < text.Length; i++)
        {
            var length = char.IsSurrogatePair(text, i) ? 2 : 1;
            var character = length == 2 ? (uint)char.ConvertToUtf32(text, i) : text[i];
            var supported = !(length == 1 && char.IsSurrogate(text[i])) && canRender(character);
            if (!supported && filtered == null)
            {
                filtered = new StringBuilder(text.Length);
                filtered.Append(text, 0, i);
            }

            if (filtered != null)
            {
                if (supported) filtered.Append(text, i, length);
                else filtered.Append('?');
            }

            i += length - 1;
        }

        return filtered?.ToString() ?? text;
    }

    /// <summary>Stores the note being typed, if it has any visible text, and stops typing.</summary>
    private void Place()
    {
        var text = _text.Trim();
        if (_typing && IsPlaceable(text))
        {
            Context.Shapes.Add(new TextNote(_at, _at.IsAnchored ? 0f : Context.GroundElevation(_at.Position), text, Context.Color));
        }

        Cancel();
    }

    private void Cancel()
    {
        _typing = false;
        _pendingHighSurrogate = '\0';
        SetText(string.Empty);
    }

    /// <summary>Builds the preview and counter strings once per keystroke, since the overlay may redraw ten times a second.</summary>
    private void SetText(string text)
    {
        _text = text;
        _preview = text + Caret;
        _counter = text.Length.ToString(CultureInfo.InvariantCulture) + "/" + MaxLengthText;
    }
}
