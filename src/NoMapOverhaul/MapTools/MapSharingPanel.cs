using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using NoMapOverhaul.Drawing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NoMapOverhaul.MapTools;

internal enum SharingPanelCommand
{
    None,
    Apply,
    SelectChannel,
}

/// <summary>Map-local channel controls. Callbacks edit drafts or queue commands; the host owns configuration and drawing state.</summary>
internal sealed class MapSharingPanel
{
    private const float Px = 0.75f;
    private const float Width = 460f;
    private const float BarHeight = 40f;
    private const float Margin = 8f;
    private const float Pad = 12f;
    private static readonly Color Ground = Rgb(0x050E07, 0.96f);
    private static readonly Color Green = Rgb(0x41FF52, 1f);
    private static readonly Color HudGreen = Rgb(0x2BE127, 1f);
    private static readonly Color Hint = Rgb(0xC8F5CD, 1f);
    private static readonly Color Rim = Rgb(0x2BE127, 0.45f);
    private static readonly Color Warning = Rgb(0xFFB23E, 1f);
    private readonly RectTransform _parent;
    private readonly RectTransform _root;
    private readonly RectTransform _bar;
    private readonly RectTransform _panel;
    private readonly List<TextMeshProUGUI> _texts = new();
    private readonly List<PanelButton> _channels = new();
    private readonly List<MenuHover> _channelHover = new();
    private readonly ConfigEntry<KeyboardShortcut>[] _channelKeys;
    private readonly KeyboardShortcut[] _shownKeys = new KeyboardShortcut[DrawingChannels.MaxChannels];
    private readonly TextMeshProUGUI _destination;
    private readonly TextMeshProUGUI _sharingStatus;
    private readonly TextMeshProUGUI _selection;
    private readonly TextMeshProUGUI _status;
    private readonly TextMeshProUGUI _empty;
    private readonly TMP_InputField _broadcast;
    private readonly TMP_InputField _receive;
    private readonly PanelButton _sharing;
    private TMP_Text? _fontSource;
    private DrawingChannels? _configuredBroadcast;
    private IReadOnlyList<string>? _shownNames;
    private IReadOnlyList<string>? _receiveNames;
    private string _shownSelected = string.Empty;
    private string _configuredReceive = string.Empty;
    private bool _configuredEnabled;
    private string _error = string.Empty;
    private (SharingPanelCommand Command, int Index) _command;
    private float _height;
    private Vector2 _mapSize;
    private bool _visible;

    public MapSharingPanel(RectTransform parent, ConfigEntry<KeyboardShortcut>[] channelKeys)
    {
        _channelKeys = channelKeys;
        _parent = parent;
        _root = NewRect("Sharing", parent);
        _bar = NewPlate("ChannelBar", _root);
        _destination = NewText(_bar, "Draw to: Local", 14f);
        Box(_destination.rectTransform, Pad, 1f, Width - 126f, 23f);
        _sharingStatus = NewText(_bar, "Sharing: Off", 11f);
        Box(_sharingStatus.rectTransform, Pad, 24f, Width - 126f, 14f);
        var channels = NewButton(_bar, "Channels", () =>
        {
            if (Open) Close();
            else OpenDraft();
        });
        Box(channels.Rect, Width - 104f, 6f, 92f, 28f);

        _panel = NewPlate("Channels", _root);
        var broadcastLabel = NewText(_panel, "Broadcast", 14f);
        Box(broadcastLabel.rectTransform, Pad, 10f, Width - Pad * 2f, 22f);
        _broadcast = NewInput(_panel, "Broadcast", "e.g. flight, squadron");
        Box(_broadcast.GetComponent<RectTransform>(), Pad, 34f, Width - Pad * 2f, 32f);
        var receiveLabel = NewText(_panel, "Receive only", 14f);
        Box(receiveLabel.rectTransform, Pad, 76f, Width - Pad * 2f, 22f);
        _receive = NewInput(_panel, "ReceiveOnly", "e.g. flight, squadron");
        Box(_receive.GetComponent<RectTransform>(), Pad, 100f, Width - Pad * 2f, 32f);
        var instructions = NewText(_panel, "Comma-separated, max 10", 12f);
        Box(instructions.rectTransform, Pad, 137f, Width - Pad * 2f, 22f);
        _sharing = NewButton(_panel, "Sharing: Off", () =>
        {
            DraftEnabled = !DraftEnabled;
            ShowSharing();
        });
        Box(_sharing.Rect, Pad, 167f, 140f, 28f);
        var apply = NewButton(_panel, "Apply", () => _command = (SharingPanelCommand.Apply, 0));
        Box(apply.Rect, Width - 164f, 167f, 72f, 28f);
        var cancel = NewButton(_panel, "Cancel", Close);
        Box(cancel.Rect, Width - 84f, 167f, 72f, 28f);
        _selection = NewText(_panel, "Draw to: Local", 14f);
        _selection.enableAutoSizing = true;
        _selection.fontSizeMin = 10f * Px;
        _selection.fontSizeMax = 14f * Px;
        Box(_selection.rectTransform, Pad, 209f, Width - Pad * 2f, 22f);
        for (var i = 0; i < DrawingChannels.MaxChannels; i++)
        {
            var index = i;
            var button = NewButton(_panel, string.Empty, () =>
            {
                if (_configuredBroadcast != null && index < _configuredBroadcast.Names.Count)
                    _command = (SharingPanelCommand.SelectChannel, index);
            });
            _channels.Add(button);
            _channelHover.Add(button.Rect.gameObject.AddComponent<MenuHover>());
            var columnWidth = (Width - Pad * 2f - 8f) * 0.5f;
            Box(button.Rect, Pad + i % 2 * (columnWidth + 8f), 237f + i / 2 * 34f, columnWidth, 28f);
        }

        _empty = NewText(_panel, "New drawings stay local.", 13f);
        _empty.enableWordWrapping = true;
        Box(_empty.rectTransform, Pad, 237f, Width - Pad * 2f, 48f);
        _status = NewText(_panel, string.Empty, 12f);
        _status.enableWordWrapping = true;
        SetVisible(false);
        SetActive(_panel.gameObject, false);
    }

    public bool Open { get; private set; }
    public RectTransform Area => _root;
    public int LayoutVersion { get; private set; }
    public string DraftBroadcast => _broadcast.text;
    public string DraftReceive => _receive.text;
    public bool DraftEnabled { get; private set; }

    public (SharingPanelCommand Command, int Index) TakeCommand()
    {
        var command = _command;
        _command = default;
        return command;
    }

    public void Render(DrawingChannels broadcast, DrawingChannels receive, bool enabled, string status, TMP_Text? hudStyle)
    {
        if (_root == null) return;
        _root.SetAsLastSibling();
        _configuredBroadcast = broadcast;
        if (!ReferenceEquals(_receiveNames, receive.Names))
        {
            _receiveNames = receive.Names;
            _configuredReceive = string.Join(", ", receive.Names);
        }

        var channelsChanged = !ReferenceEquals(_shownNames, broadcast.Names);
        var selectionChanged = _shownSelected != broadcast.Selected;
        if (channelsChanged || selectionChanged)
            _destination.text = "Draw to: " + (broadcast.Selected.Length > 0 ? broadcast.Selected : "Local");
        _shownNames = broadcast.Names;
        _shownSelected = broadcast.Selected;
        _configuredEnabled = enabled;
        if (hudStyle != null && !ReferenceEquals(hudStyle, _fontSource))
        {
            _fontSource = hudStyle;
            foreach (var text in _texts)
            {
                text.font = hudStyle.font;
                text.fontSharedMaterial = hudStyle.fontSharedMaterial;
                text.fontStyle = hudStyle.fontStyle;
            }

            _broadcast.fontAsset = hudStyle.font;
            _receive.fontAsset = hudStyle.font;
        }

        _sharingStatus.text = enabled ? "Sharing: On" : "Sharing: Off";
        _selection.text = _destination.text;
        var hoveredChannel = string.Empty;
        for (var i = 0; i < _channels.Count; i++)
        {
            var button = _channels[i];
            var exists = i < broadcast.Names.Count;
            SetActive(button.Rect.gameObject, exists);
            if (!exists) continue;
            var key = _channelKeys[i].Value;
            if (channelsChanged || !_shownKeys[i].Equals(key))
            {
                var label = key.MainKey == KeyCode.None ? (i + 1).ToString()
                    : string.Join("+", key.Modifiers.Select(KeyLabel).Append(KeyLabel(key.MainKey)));
                button.Label.text = label + "  " + broadcast.Names[i];
                _shownKeys[i] = key;
            }
            if (channelsChanged || selectionChanged) button.Show(broadcast.Names[i] == broadcast.Selected);
            if (_channelHover[i].Hovered) hoveredChannel = broadcast.Names[i];
        }

        SetActive(_empty.gameObject, broadcast.Names.Count == 0);
        _status.text = _error.Length > 0 ? _error
            : hoveredChannel.Length > 0 ? "Channel: " + hoveredChannel + "\n" + status : status;
        _status.color = _error.Length > 0 ? Warning : Hint;
        ShowInputFocus(_broadcast);
        ShowInputFocus(_receive);
        Layout();
    }

    public void ShowError(string error)
    {
        _error = error;
        _status.text = error;
        _status.color = Warning;
    }

    public void Close()
    {
        if (_root == null)
        {
            Open = false;
            _command = default;
            return;
        }

        _broadcast.DeactivateInputField();
        _receive.DeactivateInputField();
        _command = default;
        _error = string.Empty;
        Open = false;
        SetActive(_panel.gameObject, false);
        Layout();
    }

    public void SetVisible(bool visible)
    {
        if (_root == null) return;
        if (!visible) Close();
        if (_visible != visible) LayoutVersion++;
        _visible = visible;
        SetActive(_root.gameObject, visible);
    }

    private void OpenDraft()
    {
        if (!_visible || _configuredBroadcast == null) return;
        _broadcast.SetTextWithoutNotify(string.Join(", ", _configuredBroadcast.Names));
        _receive.SetTextWithoutNotify(_configuredReceive);
        DraftEnabled = _configuredEnabled;
        _error = string.Empty;
        Open = true;
        ShowSharing();
        SetActive(_panel.gameObject, true);
        Layout();
    }

    private void ShowSharing()
    {
        _sharing.Label.text = DraftEnabled ? "Sharing: On" : "Sharing: Off";
        _sharing.Show(DraftEnabled);
    }

    private void Layout()
    {
        if (_root == null || _parent == null) return;
        var count = _configuredBroadcast?.Names.Count ?? 0;
        var contentBottom = count == 0 ? 285f : 237f + ((count + 1) / 2) * 34f;
        var panelHeight = contentBottom + 64f;
        var height = Open ? panelHeight + 8f + BarHeight : BarHeight;
        var mapSize = _parent.rect.size;
        if (_height != height || _mapSize != mapSize) LayoutVersion++;
        _height = height;
        _mapSize = mapSize;
        var scale = Mathf.Max(0.01f, Mathf.Min(1f, Mathf.Min(
            (mapSize.x - Margin * 2f * Px) / (Width * Px),
            (mapSize.y - Margin * 2f * Px) / (height * Px))));
        _root.anchorMin = _root.anchorMax = new Vector2(1f, 0f);
        _root.pivot = new Vector2(1f, 0f);
        _root.anchoredPosition = new Vector2(-Margin * Px, Margin * Px);
        _root.sizeDelta = new Vector2(Width * Px, height * Px);
        _root.localScale = new Vector3(scale, scale, 1f);
        Box(_bar, 0f, height - BarHeight, Width, BarHeight);
        Box(_panel, 0f, 0f, Width, panelHeight);
        Box(_status.rectTransform, Pad, contentBottom + 8f, Width - Pad * 2f, 44f);
    }

    private TMP_InputField NewInput(Transform parent, string name, string placeholder)
    {
        var rect = NewPlate(name, parent);
        var input = rect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = rect.GetComponent<FeatheredRect>();
        input.transition = Selectable.Transition.None;
        input.navigation = new Navigation { mode = Navigation.Mode.None };
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = DrawingChannels.MaxConfigurationLength;
        input.onValidateInput = (_, _, character) => character >= 'A' && character <= 'Z'
            ? (char)(character + ('a' - 'A')) : character;
        input.richText = false;
        input.customCaretColor = true;
        input.caretColor = Green;
        input.selectionColor = Rgb(0x2BE127, 0.35f);
        var viewport = NewRect("Viewport", rect);
        Stretch(viewport);
        viewport.offsetMin = new Vector2(8f * Px, 3f * Px);
        viewport.offsetMax = new Vector2(-8f * Px, -3f * Px);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = NewText(viewport, string.Empty, 14f);
        Stretch(text.rectTransform);
        text.overflowMode = TextOverflowModes.Overflow;
        var hint = NewText(viewport, placeholder, 14f);
        Stretch(hint.rectTransform);
        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = hint;
        return input;
    }

    private static string KeyLabel(KeyCode key) =>
        key is KeyCode.LeftShift or KeyCode.RightShift ? "Shift" : key.ToString().Replace("Alpha", string.Empty);

    private static void ShowInputFocus(TMP_InputField input) =>
        input.GetComponent<FeatheredRect>().SetEdges(Px, input.isFocused ? Green : Rim, Px);

    private PanelButton NewButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        var rect = NewPlate(text.Length > 0 ? text : "BroadcastChannel", parent);
        var fill = NewRect("Fill", rect).gameObject.AddComponent<Image>();
        Stretch(fill.rectTransform);
        fill.color = HudGreen;
        fill.raycastTarget = false;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(onClick);
        var label = NewText(rect, text, 13f);
        Stretch(label.rectTransform);
        label.margin = new Vector4(5f * Px, 0f, 5f * Px, 0f);
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10f * Px;
        label.fontSizeMax = 13f * Px;
        var control = new PanelButton(rect, button, label);
        control.Show(false);
        return control;
    }

    private TextMeshProUGUI NewText(Transform parent, string text, float size)
    {
        var label = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        label.richText = false;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.fontSize = size * Px;
        label.color = Hint;
        label.text = text;
        _texts.Add(label);
        return label;
    }

    private static RectTransform NewPlate(string name, Transform parent)
    {
        var rect = NewRect(name, parent);
        var plate = rect.gameObject.AddComponent<FeatheredRect>();
        plate.color = Ground;
        plate.SetEdges(Px, Rim, Px);
        return rect;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Box(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x * Px, -y * Px);
        rect.sizeDelta = new Vector2(width * Px, height * Px);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target.activeSelf != active) target.SetActive(active);
    }

    private static Color Rgb(int hex, float alpha) =>
        new(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, alpha);

    private sealed class PanelButton
    {
        public PanelButton(RectTransform rect, Button button, TextMeshProUGUI label)
        {
            Rect = rect;
            Button = button;
            Label = label;
        }

        public RectTransform Rect { get; }
        private Button Button { get; }
        public TextMeshProUGUI Label { get; }

        public void Show(bool selected)
        {
            Button.colors = new ColorBlock
            {
                normalColor = new Color(1f, 1f, 1f, selected ? 0.10f : 0f),
                highlightedColor = new Color(1f, 1f, 1f, selected ? 0.16f : 0.08f),
                pressedColor = new Color(1f, 1f, 1f, selected ? 0.22f : 0.14f),
                selectedColor = new Color(1f, 1f, 1f, selected ? 0.10f : 0f),
                disabledColor = Color.clear,
                colorMultiplier = 1f,
                fadeDuration = 0.05f,
            };
            Rect.GetComponent<FeatheredRect>().SetEdges(Px, selected ? Hint : Rim, Px);
            Label.color = Hint;
        }
    }
}
