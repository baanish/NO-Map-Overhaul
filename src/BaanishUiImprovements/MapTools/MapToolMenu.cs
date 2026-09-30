using System.Collections.Generic;
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
/// The Tools button in the full map's top-left corner and the menu it opens: a row per tool, the active tool's option
/// buttons and status line, colour swatches, and Undo, Redo, and Clear. Text is in the HUD label's font, as the runway
/// numbers are. It is built on the map's own canvas, after the map, so it takes clicks before the map under it, and it
/// hides whenever the map isn't the full map. Under the menu lies the <see cref="MapPointerCatcher"/> that covers the
/// map while a tool is active.
/// Clicks only record a <see cref="MenuCommand"/>; the host applies it in the plugin's guarded update, so nothing the
/// menu starts can throw inside Unity's event system.
/// </summary>
internal sealed class MapToolMenu
{
    private const float RowHeight = 20f;
    private const float PanelWidth = 136f;
    private const float SwatchSize = 18f;
    private const float FontSize = 13f;
    private const int MaxOptions = 6;
    private const string FullStatus = "Drawing limit reached: erase or undo to draw more.";

    private static readonly Color PanelColor = new(0f, 0f, 0f, 0.7f);
    private static readonly Color TextColor = new(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color DimTextColor = new(0.92f, 0.92f, 0.92f, 0.35f);
    private static readonly ColorBlock RowLook = Look(new Color(1f, 1f, 1f, 0f));
    private static readonly ColorBlock ActiveRowLook = Look(new Color(1f, 1f, 1f, 0.22f));

    /// <summary>The Tools button stands alone on the map, so it keeps the panel's dark backing to stay readable.</summary>
    private static readonly ColorBlock ToggleLook = Look(PanelColor);
    private static readonly ColorBlock OpenToggleLook = Look(new Color(0.22f, 0.22f, 0.22f, 0.85f));

    private readonly List<MenuButton> _toolRows = new();
    private readonly List<MenuButton> _options = new();
    private readonly List<Image> _swatchFrames = new();
    private readonly List<TextMeshProUGUI> _texts = new();
    private RectTransform? _root;
    private GameObject? _panel;
    private GameObject? _optionRow;
    private MenuButton? _toggle;
    private MenuButton? _undo;
    private MenuButton? _redo;
    private MenuButton? _clear;
    private TextMeshProUGUI? _status;
    private TextMeshProUGUI? _fontSource;
    private (MenuCommand Command, int Index) _command;
    private int _shownTool = -2;
    private IReadOnlyList<string>? _shownOptions;
    private int _shownOptionCount = -1;

    public MapPointerCatcher? Catcher { get; private set; }

    /// <summary>The click recorded since the last call, if any.</summary>
    public (MenuCommand Command, int Index) TakeCommand()
    {
        var command = _command;
        _command = default;
        return command;
    }

    /// <summary>Per frame while the full map is open. <paramref name="activeTool"/> is -1 while the menu is closed.</summary>
    public void Render(DynamicMap map, IReadOnlyList<MapTool> tools, int activeTool, ShapeStore store, ShapeColor color, TextMeshProUGUI? hudStyle)
    {
        EnsureBuilt(map, tools);
        var open = activeTool >= 0;
        SetActive(_root!.gameObject, true);
        SetActive(Catcher!.gameObject, open);
        SetActive(_panel!, open);
        if (!ReferenceEquals(hudStyle, _fontSource) && hudStyle != null)
        {
            _fontSource = hudStyle;
            foreach (var text in _texts)
            {
                text.font = hudStyle.font;
                text.fontSharedMaterial = hudStyle.fontSharedMaterial;
                text.fontStyle = hudStyle.fontStyle;
            }
        }

        if (activeTool != _shownTool)
        {
            _shownTool = activeTool;
            _toggle!.SetActiveLook(open);
            for (var i = 0; i < _toolRows.Count; i++)
            {
                _toolRows[i].SetActiveLook(i == activeTool);
            }
        }

        if (!open)
        {
            return;
        }

        var tool = tools[activeTool];
        ShowOptions(tool.Options);
        var status = tool.Status;
        if (string.IsNullOrEmpty(status))
        {
            status = store.IsFull ? FullStatus : string.Empty;
        }

        SetActive(_status!.gameObject, status.Length > 0);
        _status.text = status;
        for (var i = 0; i < _swatchFrames.Count; i++)
        {
            _swatchFrames[i].enabled = ShapeColor.Palette[i] == color;
        }

        _undo!.SetInteractable(store.CanUndo);
        _redo!.SetInteractable(store.CanRedo);
        _clear!.SetInteractable(store.Shapes.Count > 0);
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
        _panel = null;
        _optionRow = null;
        _toggle = _undo = _redo = _clear = null;
        _status = null;
        Catcher = null;
        _toolRows.Clear();
        _options.Clear();
        _swatchFrames.Clear();
        _texts.Clear();
        _fontSource = null;
        _command = default;
        _shownTool = -2;
        _shownOptions = null;
        _shownOptionCount = -1;
    }

    /// <summary>Compares by reference, so a tool that keeps returning the same strings costs nothing per frame.</summary>
    private void ShowOptions(IReadOnlyList<string> labels)
    {
        var count = Mathf.Min(labels.Count, _options.Count);
        var changed = !ReferenceEquals(labels, _shownOptions) || count != _shownOptionCount;
        for (var i = 0; !changed && i < count; i++)
        {
            changed = !ReferenceEquals(labels[i], _options[i].Label.text);
        }

        if (!changed)
        {
            return;
        }

        _shownOptions = labels;
        _shownOptionCount = count;
        SetActive(_optionRow!, count > 0);
        for (var i = 0; i < _options.Count; i++)
        {
            SetActive(_options[i].Button.gameObject, i < count);
            if (i < count)
            {
                _options[i].Label.text = labels[i];
            }
        }
    }

    /// <summary>A scene change destroys the map and the menu with it; a missing root means everything here is gone.</summary>
    private void EnsureBuilt(DynamicMap map, IReadOnlyList<MapTool> tools)
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

        var menu = NewRect("Menu", _root);
        menu.anchorMin = menu.anchorMax = menu.pivot = new Vector2(0f, 1f);
        menu.anchoredPosition = new Vector2(6f, -6f);
        Stack(menu.gameObject, 2f, 0);
        var fitter = menu.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _toggle = NewButton(menu, "Tools", 64f, () => Record(MenuCommand.Toggle, 0), ToggleLook, OpenToggleLook);

        var panel = NewRect("Panel", menu);
        panel.gameObject.AddComponent<Image>().color = PanelColor;
        Stack(panel.gameObject, 1f, 4);
        _panel = panel.gameObject;
        for (var i = 0; i < tools.Count; i++)
        {
            var index = i;
            _toolRows.Add(NewButton(panel, tools[i].Name, PanelWidth, () => Record(MenuCommand.Tool, index)));
        }

        var options = NewRow(panel, 2f);
        _optionRow = options.gameObject;
        for (var i = 0; i < MaxOptions; i++)
        {
            var index = i;
            _options.Add(NewButton(options, string.Empty, -1f, () => Record(MenuCommand.Option, index)));
        }

        _status = NewText(panel, string.Empty, TextAlignmentOptions.TopLeft);
        _status.enableWordWrapping = true;
        _status.color = DimTextColor;
        _status.gameObject.AddComponent<LayoutElement>().preferredWidth = PanelWidth;

        var swatches = NewRow(panel, 4f);
        for (var i = 0; i < ShapeColor.Palette.Count; i++)
        {
            var index = i;
            _swatchFrames.Add(NewSwatch(swatches, ShapeColor.Palette[i], () => Record(MenuCommand.Swatch, index)));
        }

        var actions = NewRow(panel, 2f);
        _undo = NewButton(actions, "Undo", -1f, () => Record(MenuCommand.Undo, 0));
        _redo = NewButton(actions, "Redo", -1f, () => Record(MenuCommand.Redo, 0));
        _clear = NewButton(actions, "Clear", -1f, () => Record(MenuCommand.Clear, 0));
    }

    private void Record(MenuCommand command, int index) => _command = (command, index);

    /// <summary>A text button, a menu row unless given other looks. A negative width shares the row's width with its neighbours.</summary>
    private MenuButton NewButton(Transform parent, string text, float width, UnityEngine.Events.UnityAction onClick,
        ColorBlock? look = null, ColorBlock? activeLook = null)
    {
        var rect = NewRect(text.Length > 0 ? text : "Option", parent);
        var image = rect.gameObject.AddComponent<Image>();
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.colors = look ?? RowLook;
        button.navigation = new Navigation { mode = Navigation.Mode.None }; // a selected button would take Enter and Space
        button.onClick.AddListener(onClick);
        var layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = RowHeight;
        if (width > 0f)
        {
            layout.preferredWidth = width;
        }
        else
        {
            layout.flexibleWidth = 1f;
        }

        var label = NewText(rect, text, width > PanelWidth * 0.5f ? TextAlignmentOptions.Left : TextAlignmentOptions.Center);
        var labelRect = Stretch(label.rectTransform);
        labelRect.offsetMin = new Vector2(6f, 0f);
        labelRect.offsetMax = new Vector2(-6f, 0f);
        return new MenuButton(button, label, look ?? RowLook, activeLook ?? ActiveRowLook);
    }

    /// <summary>A colour square inside a white frame that shows only while its colour is the picked one.</summary>
    private Image NewSwatch(Transform parent, ShapeColor color, UnityEngine.Events.UnityAction onClick)
    {
        var frame = NewRect("Swatch", parent);
        var frameImage = frame.gameObject.AddComponent<Image>();
        frameImage.color = TextColor;
        var layout = frame.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = layout.preferredHeight = SwatchSize;

        var fill = Stretch(NewRect("Color", frame));
        fill.offsetMin = new Vector2(2f, 2f);
        fill.offsetMax = new Vector2(-2f, -2f);
        var image = fill.gameObject.AddComponent<Image>();
        image.color = color.ToColor32();
        var button = fill.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(onClick);
        return frameImage;
    }

    private TextMeshProUGUI NewText(Transform parent, string text, TextAlignmentOptions alignment)
    {
        var label = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = alignment;
        label.fontSize = FontSize;
        label.color = TextColor;
        label.text = text;
        _texts.Add(label);
        return label;
    }

    private static RectTransform NewRow(Transform parent, float spacing)
    {
        var row = NewRect("Row", parent);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var element = row.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = RowHeight;
        element.preferredWidth = PanelWidth;
        return row;
    }

    /// <summary>Lays children out top to bottom, each as wide as the widest. The group reports its size to a parent group or a ContentSizeFitter.</summary>
    private static void Stack(GameObject target, float spacing, int padding)
    {
        var layout = target.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
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

    /// <summary>A button's background: <paramref name="normal"/> at rest, lighter under the cursor and lighter still while pressed.</summary>
    private static ColorBlock Look(Color normal) => new()
    {
        normalColor = normal,
        highlightedColor = Lighter(normal, 0.12f),
        pressedColor = Lighter(normal, 0.25f),
        selectedColor = normal,
        disabledColor = Color.clear,
        colorMultiplier = 1f,
        fadeDuration = 0.05f,
    };

    private static Color Lighter(Color color, float amount) => new(color.r + amount, color.g + amount, color.b + amount, color.a + amount);

    private sealed class MenuButton
    {
        private readonly ColorBlock _look;
        private readonly ColorBlock _activeLook;

        public MenuButton(Button button, TextMeshProUGUI label, ColorBlock look, ColorBlock activeLook)
        {
            Button = button;
            Label = label;
            _look = look;
            _activeLook = activeLook;
        }

        public Button Button { get; }

        public TextMeshProUGUI Label { get; }

        public void SetActiveLook(bool active) => Button.colors = active ? _activeLook : _look;

        public void SetInteractable(bool interactable)
        {
            if (Button.interactable != interactable)
            {
                Button.interactable = interactable;
                Label.color = interactable ? TextColor : DimTextColor;
            }
        }
    }
}
