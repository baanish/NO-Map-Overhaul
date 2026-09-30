using System.Collections.Generic;
using BaanishUiImprovements.Drawing;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
/// The map tools' menu on the full map: a slim rail of line icons down the map's top-left corner, and beside the rail's
/// head a one-line strip that names the picked tool, says what a click does, and holds the tool's option buttons.
/// Together they frame the corner as an L in the game's MFD green on dark. The rail holds the Tools head, which opens
/// and closes it, a cell per tool, Undo, Redo, and Clear, and the colour swatches. Hovering a cell names it, with the
/// key for Undo and Redo. A warning turns the strip amber and badges the tool that raised it.
/// Sizes are the design's pixels at 2560x1440 (see docs/DESIGN.md), turned into map canvas units by <see cref="Px"/>, so
/// the menu scales with the game's UI. Text is in the HUD label's font, as the runway numbers are.
/// It is built on the map's own canvas, after the map, so it takes clicks before the map under it, and it hides
/// whenever the map isn't the full map. Under the menu lies the <see cref="MapPointerCatcher"/> that covers the map
/// while a tool is active. Clicks only record a <see cref="MenuCommand"/>; the host applies it in the plugin's guarded
/// update, so nothing the menu starts can throw inside Unity's event system.
/// </summary>
internal sealed class MapToolMenu
{
    /// <summary>Canvas units per design pixel: the design is drawn at 2560x1440, where the map canvas scales by 4/3.</summary>
    private const float Px = 0.75f;

    /// <summary>The rail's corner from the map's top-left corner: just clear of the grid's row letters and column numbers.</summary>
    private const float RailX = 32f;

    private const float RailY = 38f;
    private const float RailWidth = 48f;
    private const float HeadHeight = 48f;
    private const float ToolHeight = 44f;
    private const float ActionHeight = 40f;
    private const float GroupGap = 8f;
    private const float SwatchCell = 24f;
    private const float SwatchChip = 14f;
    private const float SwatchRing = 20f;
    private const float Chamfer = 6f;
    private const float StripPad = 14f;
    private const float StripGap = 12f;
    private const float StripMaxWidth = 640f;
    private const float MinHintWidth = 160f;
    private const float ButtonHeight = 28f;
    private const float ButtonMinWidth = 32f;
    private const float TagGap = 6f;
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
    private readonly RectTransform[] _areas = new RectTransform[2];
    private RectTransform? _root;
    private RectTransform _rail = null!;
    private GameObject _open = null!;
    private PolygonGraphic _railFill = null!;
    private PolygonGraphic _stripFill = null!;
    private StrokeGraphic _frameRim = null!;
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
    private float _swatchTop;
    private float _railHeight;
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

    /// <summary>The rail and the strip, for map labels to keep clear of. Inactive ones aren't showing.</summary>
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
    public void Render(DynamicMap map, IReadOnlyList<MapTool> tools, IReadOnlyList<RailIcon> icons, int activeTool, ShapeStore store,
        ShapeColor color, TextMeshProUGUI? hudStyle)
    {
        EnsureBuilt(map, tools, icons);
        var open = activeTool >= 0;
        SetActive(_root!.gameObject, true);
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
            SetActive(_stripFill.gameObject, open);
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
        if (!open)
        {
            ShowTag(null);
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

        RailCell? hovered = null;
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
        if (_root != null)
        {
            SetActive(_root.gameObject, false);
        }
    }

    public void Reset()
    {
        if (_root != null)
        {
            Object.Destroy(_root.gameObject);
        }

        _root = null;
        Catcher = null;
        _cells.Clear();
        _options.Clear();
        _texts.Clear();
        System.Array.Clear(_areas, 0, _areas.Length);
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
    /// tool. The strip grows to fit, up to <see cref="StripMaxWidth"/>, past which the hint wraps onto a second line.
    /// </summary>
    private void LayoutStrip(string toolName, bool open)
    {
        if (!open)
        {
            SetFrame(open: false, 0f);
            return;
        }

        var x = RailWidth + StripPad;
        const float middle = HeadHeight * 0.5f;
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

        if (_warning)
        {
            RailIcons.DrawWarning(_warnIcon, P(x + 8f, middle - 8f), Px, Feather, WarnColor, Ground);
            x += WarnIconWidth;
        }
        else
        {
            _name.text = toolName;
            var width = Measure(_name);
            Box(_name.rectTransform, x, 0f, width, HeadHeight);
            x += width + StripGap;
        }

        if (hasHint && !_warning)
        {
            DrawLine(_nameDivider, P(x, middle - 11f), P(x, middle + 11f), Fade(Green, 0.3f));
            x += StripGap;
        }

        _hint.text = _status;
        _hint.color = _warning ? WarnColor : HintColor;
        _hint.enableWordWrapping = false;
        var hintWidth = hasHint ? Measure(_hint) : 0f;

        var tail = StripPad;
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
            tail += 18f + Measure(_counter);
        }

        var overflow = x + hintWidth + tail - RailWidth - StripMaxWidth;
        if (hasHint && overflow > 0f)
        {
            hintWidth = Mathf.Max(hintWidth - overflow, MinHintWidth);
            _hint.enableWordWrapping = true;
        }

        Box(_hint.rectTransform, x, 0f, hintWidth, HeadHeight);
        x += hintWidth;

        for (var i = 0; i < _optionCount; i++)
        {
            var button = _options[i];
            x += StripGap;
            var width = ButtonWidth(button);
            Box(button.Rect, x, middle - ButtonHeight * 0.5f, width, ButtonHeight);
            button.Show(i == _pickedOption, width);
            x += width;
        }

        if (_suffix.gameObject.activeSelf)
        {
            x += 8f;
            var width = Measure(_suffix);
            Box(_suffix.rectTransform, x, 0f, width, HeadHeight);
            x += width;
        }

        if (_counter.gameObject.activeSelf)
        {
            x += 18f;
            var width = Measure(_counter);
            Box(_counter.rectTransform, x, 0f, width, HeadHeight);
            x += width;
        }

        SetFrame(open: true, x + StripPad - RailWidth);
    }

    /// <summary>The L's ground and rim: the rail, with one chamfer on its outer corner, and while open the strip beside its head.</summary>
    private void SetFrame(bool open, float stripWidth)
    {
        var height = open ? _railHeight : HeadHeight;
        Box(_railFill.rectTransform, 0f, 0f, RailWidth, height);
        _railFill.SetPoints(P(Chamfer, 0f), P(RailWidth, 0f), P(RailWidth, height), P(0f, height), P(0f, Chamfer));
        Box(_stripFill.rectTransform, RailWidth, 0f, stripWidth, HeadHeight);
        _stripFill.SetPoints(P(0f, 0f), P(stripWidth, 0f), P(stripWidth, HeadHeight), P(0f, HeadHeight));

        _frameRim.Clear();
        _frameRim.AddPoint(P(Chamfer, 0f));
        if (open)
        {
            var right = RailWidth + stripWidth;
            _frameRim.AddPoint(P(right, 0f));
            _frameRim.AddPoint(P(right, HeadHeight));
        }
        else
        {
            _frameRim.AddPoint(P(RailWidth, 0f));
        }

        _frameRim.AddPoint(P(RailWidth, HeadHeight));
        _frameRim.AddPoint(P(RailWidth, height));
        _frameRim.AddPoint(P(0f, height));
        _frameRim.AddPoint(P(0f, Chamfer));
        _frameRim.EndStroke(true, 0.5f * Px, Rim, 0f, Rim, Feather);
        _frameRim.Apply();
        LayoutVersion++;
    }

    /// <summary>The name tag right of a hovered cell: its name, plus its key in a box for Undo and Redo.</summary>
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
        var key = cell == _undo ? KeyText(_settings.MapToolUndoKey.Value) : cell == _redo ? KeyText(_settings.MapToolRedoKey.Value) : string.Empty;
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
        Box(_tag, RailWidth + TagGap, cell.Top + cell.Height * 0.5f - TagHeight * 0.5f, width, TagHeight);
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
            var (x, y) = SwatchCenter(index);
            DrawBox(_swatchRing, x - SwatchRing * 0.5f, y - SwatchRing * 0.5f, SwatchRing, SwatchRing, RingColor, strokeWidth: 2f);
        }
    }

    /// <summary>A scene change destroys the map and the menu with it; a missing root means everything here is gone.</summary>
    private void EnsureBuilt(DynamicMap map, IReadOnlyList<MapTool> tools, IReadOnlyList<RailIcon> icons)
    {
        if (_root != null && _root.parent == map.transform)
        {
            if (_root.GetSiblingIndex() != _root.parent.childCount - 1)
            {
                _root.SetAsLastSibling();
            }

            return;
        }

        Reset();
        _root = Stretch(NewRect("BaanishMapTools", map.transform));

        var catcher = Stretch(NewRect("PointerCatcher", _root));
        catcher.gameObject.AddComponent<Image>().color = Color.clear;
        Catcher = catcher.gameObject.AddComponent<MapPointerCatcher>();

        var actionTop = HeadHeight + 1f + tools.Count * ToolHeight + GroupGap;
        _swatchTop = actionTop + 3f * ActionHeight + GroupGap;
        _railHeight = _swatchTop + 3f * SwatchCell + 6f;

        _rail = NewRect("Rail", _root);
        Box(_rail, RailX, RailY, 0f, 0f);
        _railFill = NewGraphic<PolygonGraphic>("RailGround", _rail, raycast: true);
        _railFill.color = Ground;
        _stripFill = NewGraphic<PolygonGraphic>("StripGround", _rail, raycast: true);
        _stripFill.color = Ground;
        _frameRim = NewGraphic<StrokeGraphic>("Rim", _rail, raycast: false);
        _areas[0] = _railFill.rectTransform;
        _areas[1] = _stripFill.rectTransform;

        _head = NewCell(_rail, "Tools", RailIcon.Tools, 0f, HeadHeight, iconY: 18f, iconScale: 0.8f, insetY: 3f,
            () => Record(MenuCommand.Toggle, 0));
        _headLabel = NewText(_head.Rect, "Tools", HeadLabelSize, FontStyles.Bold | FontStyles.UpperCase, TextAlignmentOptions.Center);
        _headLabel.characterSpacing = HeadTracking;
        Box(_headLabel.rectTransform, 0f, 31f, RailWidth, 14f);

        var open = NewRect("Open", _rail);
        Box(open, 0f, 0f, 0f, 0f);
        _open = open.gameObject;
        BuildStrip(open);

        for (var i = 0; i < tools.Count; i++)
        {
            var index = i;
            var cell = NewCell(open, tools[i].Name, icons[i], HeadHeight + 1f + i * ToolHeight, ToolHeight, ToolHeight * 0.5f, 1f, 2f,
                () => Record(MenuCommand.Tool, index));
            var badge = NewGraphic<FeatheredRect>("Badge", cell.Rect, raycast: false);
            badge.color = WarnColor;
            badge.SetEdges(0.75f * Px, Ground, Feather);
            Box(badge.rectTransform, RailWidth - 13f, 6f, 7f, 7f);
            cell.Badge = badge;
            _cells.Add(cell);
        }

        _undo = NewCell(open, "Undo", RailIcon.Undo, actionTop, ActionHeight, ActionHeight * 0.5f, 1f, 2f, () => Record(MenuCommand.Undo, 0));
        _redo = NewCell(open, "Redo", RailIcon.Redo, actionTop + ActionHeight, ActionHeight, ActionHeight * 0.5f, 1f, 2f,
            () => Record(MenuCommand.Redo, 0));
        _clear = NewCell(open, "Clear", RailIcon.Clear, actionTop + 2f * ActionHeight, ActionHeight, ActionHeight * 0.5f, 1f, 2f,
            () => Record(MenuCommand.Clear, 0));
        _cells.Add(_undo);
        _cells.Add(_redo);
        _cells.Add(_clear);

        var dividers = NewGraphic<StrokeGraphic>("Dividers", open, raycast: false);
        dividers.AddPoint(P(RailWidth, 8f));
        dividers.AddPoint(P(RailWidth, HeadHeight - 8f));
        EndLine(dividers, Fade(Green, 0.3f));
        dividers.AddPoint(P(8f, HeadHeight));
        dividers.AddPoint(P(RailWidth - 8f, HeadHeight));
        EndLine(dividers, Fade(Green, 0.3f));
        foreach (var y in new[] { actionTop - GroupGap * 0.5f, _swatchTop - GroupGap * 0.5f })
        {
            dividers.AddPoint(P(10f, y));
            dividers.AddPoint(P(RailWidth - 10f, y));
            EndLine(dividers, Fade(Green, 0.25f));
        }

        dividers.Apply();

        for (var i = 0; i < ShapeColor.Palette.Count; i++)
        {
            var index = i;
            var (x, y) = SwatchCenter(i);
            var cell = NewGraphic<PolygonGraphic>("Swatch", open, raycast: true);
            Box(cell.rectTransform, x - SwatchCell * 0.5f, y - SwatchCell * 0.5f, SwatchCell, SwatchCell);
            var button = cell.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => Record(MenuCommand.Swatch, index));
            var chip = NewGraphic<Image>("Chip", cell.rectTransform, raycast: false);
            chip.color = ShapeColor.Palette[i].ToColor32();
            Box(chip.rectTransform, (SwatchCell - SwatchChip) * 0.5f, (SwatchCell - SwatchChip) * 0.5f, SwatchChip, SwatchChip);
        }

        _swatchRing = NewGraphic<StrokeGraphic>("SwatchRing", open, raycast: false);
        BuildTag();
        SetFrame(open: false, 0f);
    }

    private void BuildStrip(RectTransform open)
    {
        _warnBar = NewGraphic<Image>("WarnBar", open, raycast: false);
        _warnBar.color = WarnColor;
        Box(_warnBar.rectTransform, RailWidth, 1f, 2f, HeadHeight - 2f);
        _warnIcon = NewGraphic<StrokeGraphic>("WarnIcon", open, raycast: false);
        _name = NewText(open, string.Empty, NameSize, FontStyles.Bold | FontStyles.UpperCase, TextAlignmentOptions.Left);
        _name.characterSpacing = NameTracking;
        _name.color = Green;
        _nameDivider = NewGraphic<StrokeGraphic>("Divider", open, raycast: false);
        _hint = NewText(open, string.Empty, HintSize, FontStyles.Normal, TextAlignmentOptions.Left);
        for (var i = 0; i < MaxOptions; i++)
        {
            var index = i;
            var rect = NewRect("Option", open);
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

        _suffix = NewText(open, string.Empty, NameSize, FontStyles.Normal, TextAlignmentOptions.Left);
        _suffix.color = Fade(Green, 0.7f);
        _counter = NewText(open, string.Empty, NameSize, FontStyles.Normal, TextAlignmentOptions.Left);
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
    /// icon. <paramref name="iconY"/> is the icon's centre from the cell's top, in design pixels.
    /// </summary>
    private RailCell NewCell(Transform parent, string name, RailIcon icon, float top, float height, float iconY, float iconScale,
        float insetY, UnityEngine.Events.UnityAction onClick)
    {
        var hit = NewGraphic<PolygonGraphic>(name, parent, raycast: true);
        var rect = hit.rectTransform;
        Box(rect, 0f, top, RailWidth, height);
        var fill = NewGraphic<Image>("Fill", rect, raycast: false);
        fill.color = HudGreen;
        Box(fill.rectTransform, 3f, insetY, RailWidth - 6f, height - 2f * insetY);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.navigation = new Navigation { mode = Navigation.Mode.None }; // a selected button would take Enter and Space
        button.onClick.AddListener(onClick);
        var strokes = NewGraphic<StrokeGraphic>("Icon", rect, raycast: false);
        var hover = rect.gameObject.AddComponent<MenuHover>();
        return new RailCell(rect, button, hover, strokes, icon, P(RailWidth * 0.5f, iconY), iconScale * Px, name, top, height);
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

    private (float X, float Y) SwatchCenter(int index) =>
        (SwatchCell * (0.5f + index % 2), _swatchTop + SwatchCell * (0.5f + index / 2));

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

    /// <summary>"Z", or empty for an unbound key.</summary>
    private static string KeyText(KeyboardShortcut shortcut) => shortcut.MainKey == KeyCode.None ? string.Empty : shortcut.ToString();

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
            float iconScale, string name, float top, float height)
        {
            Rect = rect;
            Button = button;
            Hover = hover;
            _icon = icon;
            _kind = kind;
            _iconCenter = iconCenter;
            _iconScale = iconScale;
            Name = name;
            Top = top;
            Height = height;
        }

        public RectTransform Rect { get; }

        public Button Button { get; }

        public MenuHover Hover { get; }

        public string Name { get; }

        /// <summary>Design pixels from the rail's top.</summary>
        public float Top { get; }

        public float Height { get; }

        /// <summary>The amber square a tool's cell shows while its status is a warning.</summary>
        public FeatheredRect? Badge { get; set; }

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
