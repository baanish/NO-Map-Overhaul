using System.Collections.Generic;
using BaanishUiImprovements.Drawing;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static BaanishUiImprovements.MapTools.MenuLayout;
using static BaanishUiImprovements.MapTools.RailGrid;

namespace BaanishUiImprovements.MapTools;

/// <summary>What a click in the menu asked for. Tool, Option, and Swatch carry an index.</summary>
internal enum MenuCommand
{
    None,
    Toggle,
    Tool,
    Option,
    Swatch,
    Undo,
    Redo,
    Clear,
}

/// <summary>
/// The map tools' menu on the full map: a slim rail of line icons, and a strip that names the picked tool, says
/// what a click does, and holds the tool's option buttons, in the game's MFD green on dark. The rail holds the Tools head,
/// which opens and closes it, a cell per tool, Undo, Redo, and Clear, and the colour swatches. Hovering a cell names it,
/// with its key. A warning turns the strip amber and badges the tool that raised it.
/// Where there's room, the rail stands left of the map in columns and the strip above it, so the map stays clear; each
/// falls back to the map's top-left corner, where together they frame the corner as an L (<see cref="MenuLayout"/>).
/// Sizes are the design's pixels at 2560x1440 (see docs/DESIGN.md), turned into map canvas units by <see cref="Px"/>, so
/// the menu scales with the game's UI. Text is in the HUD label's font, as the runway numbers are.
/// Inside the map it is built on the map's own canvas, after the map, so it takes clicks before the map under it.
/// Outside, the map's <c>RectMask2D</c> would clip it, so it sits beside the map on the map's parent canvas, last, which
/// draws over the game's HUD canvas. It hides whenever the map isn't the full map. Under the menu lies the
/// <see cref="MapPointerCatcher"/> that covers the map while a tool is active. Clicks only record a
/// <see cref="MenuCommand"/>; the host applies it in the plugin's guarded update, so nothing the menu starts can throw
/// inside Unity's event system.
/// </summary>
internal sealed class MapToolMenu
{
    /// <summary>Canvas units per design pixel: the design is drawn at 2560x1440, where the map canvas scales by 4/3.</summary>
    private const float Px = 0.75f;

    private const float SwatchChip = 14f;
    private const float SwatchRing = 20f;
    private const float Chamfer = 6f;

    /// <summary>Above and below a wrapped hint, inside the strip it makes taller than the head.</summary>
    private const float StripTextPad = 4f;

    private const float ButtonHeight = 28f;

    /// <summary>A second row holds the buttons with the margin below them that one row has.</summary>
    private const float SecondRowHeight = ButtonHeight + (HeadHeight - ButtonHeight) * 0.5f;

    private const float CounterGap = 18f;
    private const float ButtonMinWidth = 32f;
    private const float TagHeight = 28f;
    private const float TagPad = 12f;
    private const float WarnIconWidth = 24f;

    private const float NameSize = 13f;
    private const float HintSize = 15f;
    private const float HeadLabelSize = 10f;

    /// <summary>Letter spacing in TextMeshPro's hundredths of an em: 1.5 px on 13 px caps, 1 px on buttons, 0.5 px on the head label.</summary>
    private const float NameTracking = 1.5f / NameSize * 100f;

    private const float ButtonTracking = 1f / NameSize * 100f;
    private const float HeadTracking = 0.5f / HeadLabelSize * 100f;

    /// <summary>Edges fade over one design pixel.</summary>
    private const float Feather = Px;

    private const float RestOpacity = 0.72f;
    private const float DisabledOpacity = 0.22f;
    private const int MaxOptions = 6;
    private const string FullStatus = "Drawing limit reached: erase or undo to draw more.";

    private static readonly Color Ground = Rgb(0x050E07, 0.78f);
    private static readonly Color Rim = Rgb(0x2BE127, 0.32f);
    private static readonly Color Green = Rgb(0x41FF52, 1f);
    private static readonly Color HudGreen = Rgb(0x2BE127, 1f);
    private static readonly Color Ink = Rgb(0x03140A, 1f);
    private static readonly Color HintColor = Rgb(0xC8F5CD, 1f);
    private static readonly Color WarnColor = Rgb(0xFFB23E, 1f);
    private static readonly Color RingColor = Rgb(0xE8FFE8, 1f);

    /// <summary>Cell and button fills: the HUD green at 0% at rest, 16% under the cursor, 32% pressed.</summary>
    private static readonly ColorBlock CellLook = Look(0f, 0.16f, 0.32f);

    /// <summary>The picked tool or option: a solid fill, with its icon or text in <see cref="Ink"/>.</summary>
    private static readonly ColorBlock PickedLook = Look(0.9f, 0.9f, 0.9f);

    private readonly ModSettings _settings;
    private readonly List<RailCell> _cells = new();
    private readonly List<StripButton> _options = new();
    private readonly List<(TextMeshProUGUI Text, FontStyles Extra)> _texts = new();
    private readonly List<RectTransform> _swatches = new();
    private readonly List<RectTransform> _areas = new(2);
    private readonly List<Vector2> _outline = new();
    private readonly Vector3[] _corners = new Vector3[4];
    private RectTransform? _root;
    private RectTransform? _outside;
    private RectTransform _rail = null!;
    private RectTransform _strip = null!;
    private GameObject _open = null!;
    private PolygonGraphic _railFill = null!;
    private PolygonGraphic _stripFill = null!;
    private StrokeGraphic _frameRim = null!;
    private StrokeGraphic _stripRim = null!;
    private StrokeGraphic _dividers = null!;
    private RailCell _head = null!;
    private TextMeshProUGUI _headLabel = null!;
    private RailCell _undo = null!;
    private RailCell _redo = null!;
    private RailCell _clear = null!;
    private TextMeshProUGUI _name = null!;
    private StrokeGraphic _nameDivider = null!;
    private Image _warnBar = null!;
    private StrokeGraphic _warnIcon = null!;
    private TextMeshProUGUI _hint = null!;
    private TextMeshProUGUI _suffix = null!;
    private TextMeshProUGUI _counter = null!;
    private StrokeGraphic _swatchRing = null!;
    private RectTransform _tag = null!;
    private TextMeshProUGUI _tagName = null!;
    private TextMeshProUGUI _tagKey = null!;
    private StrokeGraphic _tagKeyBox = null!;
    private TextMeshProUGUI? _fontSource;
    private (MenuCommand Command, int Index) _command;
    private RailGrid _grid = null!;
    private RailPlacement _railPlace;
    private StripPlacement _stripPlace;

    /// <summary>The map's rect on screen and the screen's size when the menu was last placed; null once the map closes.</summary>
    private (Rect Map, int Width, int Height)? _placedFor;

    private int _shownTool = -2;
    private int _shownSwatch = -2;
    private RailCell? _shownTag;
    private bool _stripDirty;
    private string _status = string.Empty;
    private bool _warning;
    private IReadOnlyList<string>? _optionLabels;
    private int _optionCount;
    private int _pickedOption;
    private string _optionSuffix = string.Empty;
    private string _count = string.Empty;

    public MapToolMenu(ModSettings settings) => _settings = settings;

    public MapPointerCatcher? Catcher { get; private set; }

    /// <summary>The rail and the strip while they sit inside the map, for map labels to keep clear of. Inactive ones aren't showing.</summary>
    public IReadOnlyList<RectTransform> Areas => _areas;

    /// <summary>Goes up whenever <see cref="Areas"/> open, close, or change size.</summary>
    public int LayoutVersion { get; private set; }

    /// <summary>The click recorded since the last call, if any.</summary>
    public (MenuCommand Command, int Index) TakeCommand()
    {
        var command = _command;
        _command = default;
        return command;
    }

    /// <summary>Per frame while the full map is open. <paramref name="activeTool"/> is -1 while the rail is closed.</summary>
    public void Render(DynamicMap map, IReadOnlyList<MapTool> tools, IReadOnlyList<RailIcon> icons,
        IReadOnlyList<ConfigEntry<KeyboardShortcut>> keys, int activeTool, ShapeStore store, ShapeColor color, TextMeshProUGUI? hudStyle)
    {
        EnsureBuilt(map, tools, icons, keys);
        Place(map, tools.Count);
        var open = activeTool >= 0;
        SetActive(_root!.gameObject, true);
        SetActive(_outside!.gameObject, true);
        SetActive(Catcher!.gameObject, open);
        if (!ReferenceEquals(hudStyle, _fontSource) && hudStyle != null)
        {
            _fontSource = hudStyle;
            foreach (var (text, extra) in _texts)
            {
                text.font = hudStyle.font;
                text.fontSharedMaterial = hudStyle.fontSharedMaterial;
                text.fontStyle = hudStyle.fontStyle | extra;
            }

            _stripDirty = true;
            _shownTag = null; // measured in the old font
        }

        if (activeTool != _shownTool)
        {
            _shownTool = activeTool;
            SetActive(_open, open);
            SetActive(_strip.gameObject, open);
            _stripDirty = true;
        }

        if (open)
        {
            ReadStrip(tools[activeTool], store);
        }

        if (_stripDirty)
        {
            _stripDirty = false;
            LayoutStrip(open ? tools[activeTool].Name : string.Empty, open);
        }

        _head.Show(picked: false, interactable: true, bright: open || _head.Hover.Hovered);
        _headLabel.color = Fade(Green, open || _head.Hover.Hovered ? 1f : RestOpacity);
        // Right of the head, joined to it, is the open strip, which its tag would cover.
        var hovered = _head.Hover.Hovered && !(open && !_railPlace.Outside && !_stripPlace.Outside) ? _head : null;
        if (!open)
        {
            ShowTag(hovered);
            return;
        }

        for (var i = 0; i < tools.Count; i++)
        {
            var cell = _cells[i];
            cell.Show(i == activeTool, interactable: true, cell.Hover.Hovered);
            cell.Badge!.enabled = i == activeTool && _warning;
        }

        _undo.Show(false, store.CanUndo, _undo.Hover.Hovered);
        _redo.Show(false, store.CanRedo, _redo.Hover.Hovered);
        _clear.Show(false, store.Shapes.Count > 0, _clear.Hover.Hovered);
        ShowSwatch(IndexOf(color));

        foreach (var cell in _cells)
        {
            if (cell.Hover.Hovered && cell.Button.interactable)
            {
                hovered = cell;
            }
        }

        ShowTag(hovered);
    }

    /// <summary>The map closed or went back to the minimap. A click from the frame it closed is dropped, not replayed on reopening.</summary>
    public void Hide()
    {
        _command = default;
        _placedFor = null;
        if (_root != null)
        {
            SetActive(_root.gameObject, false);
        }

        if (_outside != null)
        {
            SetActive(_outside.gameObject, false);
        }
    }

    public void Reset()
    {
        if (_root != null)
        {
            Object.Destroy(_root.gameObject);
        }

        if (_outside != null)
        {
            Object.Destroy(_outside.gameObject);
        }

        _root = null;
        _outside = null;
        Catcher = null;
        _cells.Clear();
        _options.Clear();
        _texts.Clear();
        _swatches.Clear();
        _areas.Clear();
        _placedFor = null;
        _fontSource = null;
        _command = default;
        _shownTool = -2;
        _shownSwatch = -2;
        _shownTag = null;
        _optionLabels = null;
        LayoutVersion++;
    }

    /// <summary>Reads what the strip shows. Compares by reference, so a tool that keeps returning the same strings costs nothing per frame.</summary>
    private void ReadStrip(MapTool tool, ShapeStore store)
    {
        var status = tool.Status;
        var warning = tool.Warning;
        if (status.Length == 0 && store.IsFull)
        {
            status = FullStatus;
            warning = true;
        }

        var labels = tool.Options;
        var count = Mathf.Min(labels.Count, MaxOptions);
        var changed = !ReferenceEquals(status, _status) || warning != _warning || !ReferenceEquals(labels, _optionLabels) ||
            count != _optionCount || tool.PickedOption != _pickedOption || !ReferenceEquals(tool.OptionSuffix, _optionSuffix) ||
            !ReferenceEquals(tool.Counter, _count);
        for (var i = 0; !changed && i < count; i++)
        {
            changed = !ReferenceEquals(labels[i], _options[i].Label.text);
        }

        if (!changed)
        {
            return;
        }

        _status = status;
        _warning = warning;
        _optionLabels = labels;
        _optionCount = count;
        _pickedOption = tool.PickedOption;
        _optionSuffix = tool.OptionSuffix;
        _count = tool.Counter;
        for (var i = 0; i < count; i++)
        {
            _options[i].Label.text = labels[i];
        }

        _stripDirty = true;
    }

    /// <summary>
    /// Lays the strip out left to right in design pixels: the tool name, a divider, the hint, then any option buttons,
    /// their unit, and a count. A warning replaces the name with an amber sign, since the badge on the rail names the
    /// tool. The strip grows to fit, up to its placement's width, past which the buttons, unit, and count take a second
    /// row under the hint, and a hint too long for its row wraps (<see cref="MenuLayout.FitStrip"/>). A second row or a
    /// wrapped hint makes the strip taller: down inside the map, up outside it.
    /// </summary>
    private void LayoutStrip(string toolName, bool open)
    {
        if (!open)
        {
            SetFrame(open: false, 0f, 0f);
            return;
        }

        var hasHint = _status.Length > 0;
        SetActive(_warnBar.gameObject, _warning);
        SetActive(_warnIcon.gameObject, _warning);
        SetActive(_name.gameObject, !_warning);
        SetActive(_nameDivider.gameObject, hasHint && !_warning);
        SetActive(_hint.gameObject, hasHint);
        SetActive(_suffix.gameObject, _optionCount > 0 && _optionSuffix.Length > 0);
        SetActive(_counter.gameObject, _count.Length > 0);
        for (var i = 0; i < _options.Count; i++)
        {
            SetActive(_options[i].Rect.gameObject, i < _optionCount);
        }

        _name.text = toolName;
        var lead = StripPad + (_warning ? WarnIconWidth : Measure(_name) + StripGap) + (hasHint && !_warning ? StripGap : 0f);
        _hint.text = _status;
        _hint.color = _warning ? WarnColor : HintColor;
        _hint.enableWordWrapping = false;
        var hintWidth = hasHint ? Measure(_hint) : 0f;

        var tail = 0f;
        for (var i = 0; i < _optionCount; i++)
        {
            tail += StripGap + ButtonWidth(_options[i]);
        }

        if (_suffix.gameObject.activeSelf)
        {
            _suffix.text = _optionSuffix;
            tail += 8f + Measure(_suffix);
        }

        if (_counter.gameObject.activeSelf)
        {
            _counter.text = _count;
            tail += CounterGap + Measure(_counter);
        }

        var fit = FitStrip(lead, hintWidth, tail, _stripPlace.MaxWidth);
        hintWidth = fit.HintWidth;
        _hint.enableWordWrapping = fit.Wraps;
        var below = fit.SecondRow ? SecondRowHeight : 0f;
        var rowHeight = fit.Wraps
            ? Mathf.Clamp(_hint.GetPreferredValues(_status, hintWidth * Px, 0f).y / Px + 2f * StripTextPad, HeadHeight,
                _stripPlace.MaxHeight - below)
            : HeadHeight;
        var height = rowHeight + below;
        var middle = rowHeight * 0.5f;
        var x = StripPad;
        if (_warning)
        {
            Box(_warnBar.rectTransform, 0f, 1f, 2f, height - 2f);
            RailIcons.DrawWarning(_warnIcon, P(x + 8f, middle - 8f), Px, Feather, WarnColor, Ground);
            x += WarnIconWidth;
        }
        else
        {
            var width = Measure(_name);
            Box(_name.rectTransform, x, 0f, width, rowHeight);
            x += width + StripGap;
        }

        if (hasHint && !_warning)
        {
            DrawLine(_nameDivider, P(x, middle - 11f), P(x, middle + 11f), Fade(Green, 0.3f));
            x += StripGap;
        }

        Box(_hint.rectTransform, x, 0f, hintWidth, rowHeight);
        var hintX = x;
        x += hintWidth;
        var rowEnd = x;

        // The buttons, unit, and count go beside the hint, or on the second row starting under it.
        var top = 0f;
        var band = rowHeight;
        if (fit.SecondRow)
        {
            x = hintX - (_optionCount > 0 ? StripGap : CounterGap);
            top = rowHeight;
            band = ButtonHeight;
        }

        for (var i = 0; i < _optionCount; i++)
        {
            var button = _options[i];
            x += StripGap;
            var width = ButtonWidth(button);
            Box(button.Rect, x, top + band * 0.5f - ButtonHeight * 0.5f, width, ButtonHeight);
            button.Show(i == _pickedOption, width);
            x += width;
        }

        if (_suffix.gameObject.activeSelf)
        {
            x += 8f;
            var width = Measure(_suffix);
            Box(_suffix.rectTransform, x, top, width, band);
            x += width;
        }

        if (_counter.gameObject.activeSelf)
        {
            x += CounterGap;
            var width = Measure(_counter);
            Box(_counter.rectTransform, x, top, width, band);
            x += width;
        }

        Box(_strip, _stripPlace.X, _stripPlace.Outside ? _stripPlace.Y - height : _stripPlace.Y, 0f, 0f);
        SetFrame(open: true, Mathf.Max(rowEnd, x) + StripPad, height);
    }

    /// <summary>
    /// The ground and rim of the rail, with one chamfer on its head's outer corner, and while open of the strip. With both
    /// inside the map the strip sits beside the head and one rim goes round the L they make; apart, each has its own rim
    /// and chamfer. Outside the map in columns, the head stands at the top right with an empty notch left of it.
    /// </summary>
    private void SetFrame(bool open, float stripWidth, float stripHeight)
    {
        var joined = !_railPlace.Outside && !_stripPlace.Outside;
        var width = _grid.Width;
        var height = open ? _grid.Height : HeadHeight;
        var head = _grid.Head.X;
        _outline.Clear();
        if (!open)
        {
            AddOutline(head + Chamfer, 0f, head + RailWidth, 0f, head + RailWidth, HeadHeight, head, HeadHeight);
            _outline.Add(new Vector2(head, Chamfer));
            SetFill(_railFill, head, RailWidth, HeadHeight, 0);
        }
        else if (head == 0f)
        {
            AddOutline(Chamfer, 0f, width, 0f, width, height, 0f, height);
            _outline.Add(new Vector2(0f, Chamfer));
            SetFill(_railFill, 0f, width, height, 0);
        }
        else
        {
            AddOutline(head + Chamfer, 0f, width, 0f, width, height, 0f, height);
            AddOutline(0f, HeadHeight, head, HeadHeight, head, Chamfer);
            SetFill(_railFill, 0f, width, height, 5); // a fan from the notch's inner corner covers the whole shape
        }

        if (open && joined)
        {
            // Round the strip on the way from the head's top edge down to the rail's right edge.
            _outline.RemoveAt(1);
            _outline.InsertRange(1, new[]
            {
                new Vector2(RailWidth + stripWidth, 0f), new Vector2(RailWidth + stripWidth, stripHeight), new Vector2(RailWidth, stripHeight),
            });
        }

        DrawOutline(_frameRim);
        if (open)
        {
            _outline.Clear();
            AddOutline(joined ? 0f : Chamfer, 0f, stripWidth, 0f, stripWidth, stripHeight, 0f, stripHeight);
            if (!joined)
            {
                _outline.Add(new Vector2(0f, Chamfer));
            }

            SetFill(_stripFill, 0f, stripWidth, stripHeight, 0);
            _outline.Clear(); // joined, the rail's rim goes round the strip
            if (!joined)
            {
                AddOutline(Chamfer, 0f, stripWidth, 0f, stripWidth, stripHeight, 0f, stripHeight);
                _outline.Add(new Vector2(0f, Chamfer));
            }

            DrawOutline(_stripRim);
        }

        LayoutVersion++;
    }

    private void AddOutline(params float[] xy)
    {
        for (var i = 0; i < xy.Length; i += 2)
        {
            _outline.Add(new Vector2(xy[i], xy[i + 1]));
        }
    }

    /// <summary>
    /// Fills <see cref="_outline"/>, in design pixels from the parent's top-left, as a fan from point <paramref name="from"/>,
    /// in a rect from <paramref name="x"/> sized to the shape, which is also its click area.
    /// </summary>
    private void SetFill(PolygonGraphic fill, float x, float width, float height, int from)
    {
        Box(fill.rectTransform, x, 0f, width, height);
        var points = new Vector2[_outline.Count];
        for (var i = 0; i < points.Length; i++)
        {
            var point = _outline[(from + i) % points.Length];
            points[i] = P(point.x - x, point.y);
        }

        fill.SetPoints(points);
    }

    /// <summary>Strokes <see cref="_outline"/> as a closed rim, or clears the rim for an empty outline.</summary>
    private void DrawOutline(StrokeGraphic rim)
    {
        rim.Clear();
        foreach (var point in _outline)
        {
            rim.AddPoint(P(point.x, point.y));
        }

        if (_outline.Count > 0)
        {
            rim.EndStroke(true, 0.5f * Px, Rim, 0f, Rim, Feather);
        }

        rim.Apply();
    }

    /// <summary>The name tag beside a hovered cell: its name, plus its key in a box, placed by <see cref="MenuLayout.HoverTag"/>.</summary>
    private void ShowTag(RailCell? cell)
    {
        if (ReferenceEquals(cell, _shownTag))
        {
            return;
        }

        _shownTag = cell;
        SetActive(_tag.gameObject, cell != null);
        if (cell == null)
        {
            return;
        }

        _tagName.text = cell.Name;
        var nameWidth = Measure(_tagName);
        var key = cell.Key == null ? string.Empty : KeyText(cell.Key.Value);
        SetActive(_tagKey.gameObject, key.Length > 0);
        SetActive(_tagKeyBox.gameObject, key.Length > 0);
        var width = nameWidth + 2f * TagPad;
        if (key.Length > 0)
        {
            _tagKey.text = key;
            var keyWidth = Measure(_tagKey);
            var keyX = TagPad + nameWidth + 10f;
            Box(_tagKey.rectTransform, keyX + 2f, 0f, keyWidth, TagHeight);
            DrawBox(_tagKeyBox, keyX - 2f, 5f, keyWidth + 8f, 18f, Fade(Green, 0.45f));
            width += keyWidth + 16f;
        }

        Box(_tagName.rectTransform, TagPad, 0f, nameWidth, TagHeight);
        var (x, y) = HoverTag(_railPlace, cell.Slot, width, TagHeight);
        Box(_tag, x, y, width, TagHeight);
    }

    private void ShowSwatch(int index)
    {
        if (index == _shownSwatch)
        {
            return;
        }

        _shownSwatch = index;
        SetActive(_swatchRing.gameObject, index >= 0);
        if (index >= 0)
        {
            var center = _grid.SwatchCenters[index];
            DrawBox(_swatchRing, center.X - SwatchRing * 0.5f, center.Y - SwatchRing * 0.5f, SwatchRing, SwatchRing, RingColor, strokeWidth: 2f);
        }
    }

    /// <summary>
    /// Places the rail and the strip (<see cref="MenuLayout.Place"/>) when the map opens, and again if the map's rect on
    /// screen or the screen's size changes. The outside parent is made to match the map's rect, so the menu's design
    /// pixels count from the map's top-left corner inside the map and out.
    /// </summary>
    private void Place(DynamicMap map, int toolCount)
    {
        var mapRect = (RectTransform)map.transform;
        mapRect.GetWorldCorners(_corners);
        (Rect Map, int Width, int Height) key = (Rect.MinMaxRect(_corners[0].x, _corners[0].y, _corners[2].x, _corners[2].y), Screen.width, Screen.height);
        if (_placedFor == key)
        {
            return;
        }

        _placedFor = key;
        var outside = _outside!;
        if (outside.parent != mapRect.parent)
        {
            outside.SetParent(mapRect.parent, false);
        }

        outside.anchorMin = mapRect.anchorMin;
        outside.anchorMax = mapRect.anchorMax;
        outside.pivot = mapRect.pivot;
        outside.sizeDelta = mapRect.sizeDelta;
        outside.anchoredPosition = mapRect.anchoredPosition;
        outside.localRotation = mapRect.localRotation;
        outside.localScale = mapRect.localScale;

        // A screen-space overlay canvas's world units are screen pixels, Y up.
        var scale = mapRect.lossyScale.x;
        var mapBox = new PixelBox(key.Map.xMin, key.Height - key.Map.yMax, key.Map.width, key.Map.height);
        (_railPlace, _stripPlace) = MenuLayout.Place(key.Width, key.Height, mapBox, scale * Px, MenuLayout.GameHud(key.Width, key.Height, scale),
            toolCount, ShapeColor.Palette.Count);

        _strip.SetParent(_stripPlace.Outside ? outside : _root, false);
        _rail.SetParent(_railPlace.Outside ? outside : _root, false);
        _strip.SetAsLastSibling();
        _rail.SetAsLastSibling(); // over the strip, for the joined rim and the hover tag
        Box(_rail, _railPlace.X, _railPlace.Y, 0f, 0f);
        _grid = new RailGrid(_railPlace.Columns, toolCount, ShapeColor.Palette.Count);
        _head.Place(_grid.Head);
        for (var i = 0; i < toolCount; i++)
        {
            _cells[i].Place(_grid.Tools[i]);
        }

        _undo.Place(_grid.Actions[0]);
        _redo.Place(_grid.Actions[1]);
        _clear.Place(_grid.Actions[2]);
        for (var i = 0; i < _swatches.Count; i++)
        {
            var center = _grid.SwatchCenters[i];
            Box(_swatches[i], center.X - SwatchCell * 0.5f, center.Y - SwatchCell * 0.5f, SwatchCell, SwatchCell);
        }

        DrawDividers(joined: !_railPlace.Outside && !_stripPlace.Outside);
        _areas.Clear();
        if (!_railPlace.Outside)
        {
            _areas.Add(_railFill.rectTransform);
        }

        if (!_stripPlace.Outside)
        {
            _areas.Add(_stripFill.rectTransform);
        }

        _shownSwatch = -2;
        _shownTag = null;
        _stripDirty = true;
    }

    /// <summary>Lines between the rail's groups, and while the strip is joined to the head, between the two.</summary>
    private void DrawDividers(bool joined)
    {
        _dividers.Clear();
        var head = _grid.Head.X;
        if (joined)
        {
            _dividers.AddPoint(P(RailWidth, 8f));
            _dividers.AddPoint(P(RailWidth, HeadHeight - 8f));
            EndLine(_dividers, Fade(Green, 0.3f));
        }

        _dividers.AddPoint(P(head + 8f, HeadHeight));
        _dividers.AddPoint(P(head + RailWidth - 8f, HeadHeight));
        EndLine(_dividers, Fade(Green, 0.3f));
        foreach (var y in new[] { _grid.ActionTop - GroupGap * 0.5f, _grid.SwatchTop - GroupGap * 0.5f })
        {
            _dividers.AddPoint(P(10f, y));
            _dividers.AddPoint(P(_grid.Width - 10f, y));
            EndLine(_dividers, Fade(Green, 0.25f));
        }

        _dividers.Apply();
    }

    /// <summary>
    /// A scene change destroys the map and the menu with it; a missing root means everything here is gone. The game
    /// parents the map anew each time it opens, which puts it last, so the outside part moves back after it.
    /// </summary>
    private void EnsureBuilt(DynamicMap map, IReadOnlyList<MapTool> tools, IReadOnlyList<RailIcon> icons,
        IReadOnlyList<ConfigEntry<KeyboardShortcut>> keys)
    {
        if (_root != null && _root.parent == map.transform && _outside != null)
        {
            if (_root.GetSiblingIndex() != _root.parent.childCount - 1)
            {
                _root.SetAsLastSibling();
            }

            if (_outside.parent != null && _outside.GetSiblingIndex() != _outside.parent.childCount - 1)
            {
                _outside.SetAsLastSibling();
            }

            return;
        }

        Reset();
        _root = Stretch(NewRect("BaanishMapTools", map.transform));
        _outside = NewRect("BaanishMapToolsOutside", map.transform.parent);

        var catcher = Stretch(NewRect("PointerCatcher", _root));
        catcher.gameObject.AddComponent<Image>().color = Color.clear;
        Catcher = catcher.gameObject.AddComponent<MapPointerCatcher>();

        _strip = NewRect("Strip", _root);
        BuildStrip(_strip);
        SetActive(_strip.gameObject, false);

        _rail = NewRect("Rail", _root);
        _railFill = NewGraphic<PolygonGraphic>("RailGround", _rail, raycast: true);
        _railFill.color = Ground;
        _frameRim = NewGraphic<StrokeGraphic>("Rim", _rail, raycast: false);

        _head = NewCell(_rail, "Tools", RailIcon.Tools, _settings.MapToolsKey, HeadHeight, iconY: 18f, iconScale: 0.8f, insetY: 3f,
            () => Record(MenuCommand.Toggle, 0));
        _headLabel = NewText(_head.Rect, "Tools", HeadLabelSize, FontStyles.Bold | FontStyles.UpperCase, TextAlignmentOptions.Center);
        _headLabel.characterSpacing = HeadTracking;
        Box(_headLabel.rectTransform, 0f, 31f, RailWidth, 14f);

        var open = NewRect("Open", _rail);
        Box(open, 0f, 0f, 0f, 0f);
        _open = open.gameObject;

        for (var i = 0; i < tools.Count; i++)
        {
            var index = i;
            var cell = NewCell(open, tools[i].Name, icons[i], keys[i], ToolHeight, ToolHeight * 0.5f, 1f, 2f, () => Record(MenuCommand.Tool, index));
            var badge = NewGraphic<FeatheredRect>("Badge", cell.Rect, raycast: false);
            badge.color = WarnColor;
            badge.SetEdges(0.75f * Px, Ground, Feather);
            Box(badge.rectTransform, RailWidth - 13f, 6f, 7f, 7f);
            cell.Badge = badge;
            _cells.Add(cell);
        }

        _undo = NewCell(open, "Undo", RailIcon.Undo, _settings.MapToolUndoKey, ActionHeight, ActionHeight * 0.5f, 1f, 2f,
            () => Record(MenuCommand.Undo, 0));
        _redo = NewCell(open, "Redo", RailIcon.Redo, _settings.MapToolRedoKey, ActionHeight, ActionHeight * 0.5f, 1f, 2f,
            () => Record(MenuCommand.Redo, 0));
        _clear = NewCell(open, "Clear", RailIcon.Clear, null, ActionHeight, ActionHeight * 0.5f, 1f, 2f, () => Record(MenuCommand.Clear, 0));
        _cells.Add(_undo);
        _cells.Add(_redo);
        _cells.Add(_clear);
        _dividers = NewGraphic<StrokeGraphic>("Dividers", open, raycast: false);

        for (var i = 0; i < ShapeColor.Palette.Count; i++)
        {
            var index = i;
            var cell = NewGraphic<PolygonGraphic>("Swatch", open, raycast: true);
            var button = cell.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => Record(MenuCommand.Swatch, index));
            var chip = NewGraphic<Image>("Chip", cell.rectTransform, raycast: false);
            chip.color = ShapeColor.Palette[i].ToColor32();
            Box(chip.rectTransform, (SwatchCell - SwatchChip) * 0.5f, (SwatchCell - SwatchChip) * 0.5f, SwatchChip, SwatchChip);
            _swatches.Add(cell.rectTransform);
        }

        _swatchRing = NewGraphic<StrokeGraphic>("SwatchRing", open, raycast: false);
        BuildTag();
    }

    /// <summary>The strip's ground, rim, and contents, in design pixels from the strip's top-left corner.</summary>
    private void BuildStrip(RectTransform strip)
    {
        _stripFill = NewGraphic<PolygonGraphic>("StripGround", strip, raycast: true);
        _stripFill.color = Ground;
        _stripRim = NewGraphic<StrokeGraphic>("Rim", strip, raycast: false);
        _warnBar = NewGraphic<Image>("WarnBar", strip, raycast: false);
        _warnBar.color = WarnColor;
        _warnIcon = NewGraphic<StrokeGraphic>("WarnIcon", strip, raycast: false);
        _name = NewText(strip, string.Empty, NameSize, FontStyles.Bold | FontStyles.UpperCase, TextAlignmentOptions.Left);
        _name.characterSpacing = NameTracking;
        _name.color = Green;
        _nameDivider = NewGraphic<StrokeGraphic>("Divider", strip, raycast: false);
        _hint = NewText(strip, string.Empty, HintSize, FontStyles.Normal, TextAlignmentOptions.Left);
        for (var i = 0; i < MaxOptions; i++)
        {
            var index = i;
            var rect = NewRect("Option", strip);
            var fill = rect.gameObject.AddComponent<Image>();
            fill.color = HudGreen;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // a selected button would take Enter and Space
            button.onClick.AddListener(() => Record(MenuCommand.Option, index));
            var border = NewGraphic<StrokeGraphic>("Border", rect, raycast: false);
            var label = NewText(rect, string.Empty, NameSize, FontStyles.Bold | FontStyles.UpperCase, TextAlignmentOptions.Center);
            label.characterSpacing = ButtonTracking;
            Stretch(label.rectTransform);
            _options.Add(new StripButton(rect, button, border, label));
        }

        _suffix = NewText(strip, string.Empty, NameSize, FontStyles.Normal, TextAlignmentOptions.Left);
        _suffix.color = Fade(Green, 0.7f);
        _counter = NewText(strip, string.Empty, NameSize, FontStyles.Normal, TextAlignmentOptions.Left);
        _counter.color = Fade(Green, 0.6f);
    }

    /// <summary>Last child of the rail, so it draws over everything; it takes no clicks, so moving onto it leaves the cell and hides it.</summary>
    private void BuildTag()
    {
        _tag = NewRect("Tag", _rail);
        var plate = NewGraphic<FeatheredRect>("Plate", _tag, raycast: false);
        plate.color = Ground;
        plate.SetEdges(Px, Rim, Feather);
        Stretch(plate.rectTransform);
        _tagName = NewText(_tag, string.Empty, NameSize, FontStyles.Bold | FontStyles.UpperCase, TextAlignmentOptions.Left);
        _tagName.characterSpacing = NameTracking;
        _tagName.color = Green;
        _tagKeyBox = NewGraphic<StrokeGraphic>("KeyBox", _tag, raycast: false);
        _tagKey = NewText(_tag, string.Empty, NameSize, FontStyles.Normal, TextAlignmentOptions.Left);
        _tagKey.color = Fade(Green, 0.8f);
        SetActive(_tag.gameObject, false);
    }

    /// <summary>
    /// A rail cell: a hit area the full size of the cell, a fill inset from its edges that the button tints, and a line
    /// icon. <paramref name="iconY"/> is the icon's centre from the cell's top, in design pixels. The layout places it.
    /// </summary>
    private RailCell NewCell(Transform parent, string name, RailIcon icon, ConfigEntry<KeyboardShortcut>? key, float height, float iconY,
        float iconScale, float insetY, UnityEngine.Events.UnityAction onClick)
    {
        var hit = NewGraphic<PolygonGraphic>(name, parent, raycast: true);
        var rect = hit.rectTransform;
        var fill = NewGraphic<Image>("Fill", rect, raycast: false);
        fill.color = HudGreen;
        Box(fill.rectTransform, 3f, insetY, RailWidth - 6f, height - 2f * insetY);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.navigation = new Navigation { mode = Navigation.Mode.None }; // a selected button would take Enter and Space
        button.onClick.AddListener(onClick);
        var strokes = NewGraphic<StrokeGraphic>("Icon", rect, raycast: false);
        var hover = rect.gameObject.AddComponent<MenuHover>();
        return new RailCell(rect, button, hover, strokes, icon, P(RailWidth * 0.5f, iconY), iconScale * Px, name, key, height);
    }

    private TextMeshProUGUI NewText(Transform parent, string text, float size, FontStyles extra, TextAlignmentOptions alignment)
    {
        var label = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = alignment;
        label.fontSize = size * Px;
        label.fontStyle = extra;
        label.color = HintColor;
        label.text = text;
        _texts.Add((label, extra));
        return label;
    }

    private void Record(MenuCommand command, int index) => _command = (command, index);

    private static int IndexOf(ShapeColor color)
    {
        for (var i = 0; i < ShapeColor.Palette.Count; i++)
        {
            if (ShapeColor.Palette[i] == color)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>At least <see cref="ButtonMinWidth"/>, else the label with 10 px either side.</summary>
    private static float ButtonWidth(StripButton button) => Mathf.Max(ButtonMinWidth, Measure(button.Label) + 20f);

    /// <summary>The text's natural width in design pixels. TextMeshProUGUI caches it until the text or style changes.</summary>
    private static float Measure(TextMeshProUGUI text) => text.preferredWidth / Px;

    /// <summary>"Z", "1" for the number row's Alpha1, or empty for an unbound key.</summary>
    private static string KeyText(KeyboardShortcut shortcut) =>
        shortcut.MainKey == KeyCode.None ? string.Empty : shortcut.ToString().Replace("Alpha", string.Empty);

    /// <summary>A point in design pixels from the rail's top-left corner, Y down, as local units from a top-left pivot.</summary>
    private static Vector2 P(float x, float y) => new(x * Px, -y * Px);

    /// <summary>Places a rect by its top-left corner in design pixels within a parent whose pivot is its top-left corner.</summary>
    private static void Box(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = P(x, y);
        rect.sizeDelta = new Vector2(width * Px, height * Px);
    }

    private static void DrawLine(StrokeGraphic graphic, Vector2 from, Vector2 to, Color color)
    {
        graphic.Clear();
        graphic.AddPoint(from);
        graphic.AddPoint(to);
        EndLine(graphic, color);
        graphic.Apply();
    }

    /// <summary>A rectangle outline, centred on its edges like an SVG stroke. Design pixels from the parent's top-left.</summary>
    private static void DrawBox(StrokeGraphic graphic, float x, float y, float width, float height, Color color, float strokeWidth = 1f)
    {
        graphic.Clear();
        graphic.AddPoint(P(x, y));
        graphic.AddPoint(P(x + width, y));
        graphic.AddPoint(P(x + width, y + height));
        graphic.AddPoint(P(x, y + height));
        graphic.EndStroke(true, strokeWidth * 0.5f * Px, color, 0f, color, Feather);
        graphic.Apply();
    }

    private static void EndLine(StrokeGraphic graphic, Color color) => graphic.EndStroke(false, 0.5f * Px, color, 0f, color, Feather);

    private static T NewGraphic<T>(string name, Transform parent, bool raycast)
        where T : Graphic
    {
        var graphic = NewRect(name, parent).gameObject.AddComponent<T>();
        graphic.raycastTarget = raycast;
        return graphic;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static RectTransform Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private static Color Rgb(int hex, float alpha) =>
        new(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, alpha);

    private static Color Fade(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

    /// <summary>Tints a white-based fill: the HUD green at these opacities at rest, under the cursor, and pressed. Disabled hides it.</summary>
    private static ColorBlock Look(float normal, float highlighted, float pressed) => new()
    {
        normalColor = new Color(1f, 1f, 1f, normal),
        highlightedColor = new Color(1f, 1f, 1f, highlighted),
        pressedColor = new Color(1f, 1f, 1f, pressed),
        selectedColor = new Color(1f, 1f, 1f, normal),
        disabledColor = Color.clear,
        colorMultiplier = 1f,
        fadeDuration = 0.05f,
    };

    /// <summary>One cell of the rail. Its icon is redrawn only when its look changes.</summary>
    private sealed class RailCell
    {
        private readonly StrokeGraphic _icon;
        private readonly RailIcon _kind;
        private readonly Vector2 _iconCenter;
        private readonly float _iconScale;
        private int _look = -1;

        public RailCell(RectTransform rect, Button button, MenuHover hover, StrokeGraphic icon, RailIcon kind, Vector2 iconCenter,
            float iconScale, string name, ConfigEntry<KeyboardShortcut>? key, float height)
        {
            Rect = rect;
            Button = button;
            Hover = hover;
            _icon = icon;
            _kind = kind;
            _iconCenter = iconCenter;
            _iconScale = iconScale;
            Name = name;
            Key = key;
            Height = height;
        }

        public RectTransform Rect { get; }

        public Button Button { get; }

        public MenuHover Hover { get; }

        public string Name { get; }

        /// <summary>The key its hover tag shows, if it has one.</summary>
        public ConfigEntry<KeyboardShortcut>? Key { get; }

        /// <summary>Where the layout last put it.</summary>
        public RailSlot Slot { get; private set; }

        public float Height { get; }

        /// <summary>The amber square a tool's cell shows while its status is a warning.</summary>
        public FeatheredRect? Badge { get; set; }

        public void Place(RailSlot slot)
        {
            Slot = slot;
            Box(Rect, slot.X, slot.Y, slot.Width, Height);
        }

        /// <summary>
        /// Picked: a solid fill with a dark icon. Otherwise the icon is green at 72%, full while <paramref name="bright"/>
        /// (hovered), and 22% while not interactable, when the cell ignores hover.
        /// </summary>
        public void Show(bool picked, bool interactable, bool bright)
        {
            var look = picked ? 0 : !interactable ? 1 : bright ? 2 : 3;
            if (look == _look)
            {
                return;
            }

            if (Button.interactable != interactable)
            {
                Button.interactable = interactable;
            }

            if (picked != (_look == 0) || _look < 0)
            {
                Button.colors = picked ? PickedLook : CellLook;
            }

            _look = look;
            var color = picked ? Ink : Fade(Green, look == 1 ? DisabledOpacity : look == 2 ? 1f : RestOpacity);
            RailIcons.Draw(_icon, _kind, _iconCenter, _iconScale, Feather, color);
        }
    }

    /// <summary>An option button in the strip: a 1 px border, a fill the button tints, and a caps label.</summary>
    private sealed class StripButton
    {
        private readonly StrokeGraphic _border;

        public StripButton(RectTransform rect, Button button, StrokeGraphic border, TextMeshProUGUI label)
        {
            Rect = rect;
            Button = button;
            _border = border;
            Label = label;
        }

        public RectTransform Rect { get; }

        public Button Button { get; }

        public TextMeshProUGUI Label { get; }

        public void Show(bool picked, float width)
        {
            Button.colors = picked ? PickedLook : CellLook;
            Label.color = picked ? Ink : Green;
            DrawBox(_border, 0f, 0f, width, ButtonHeight, Fade(Green, picked ? 0.9f : 0.45f));
        }
    }
}
