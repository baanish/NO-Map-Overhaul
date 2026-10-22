using System.Collections.Generic;
using NoMapOverhaul.Drawing;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NoMapOverhaul.MapTools;

/// <summary>
/// The custom colour picker the rail's + swatch opens: a wheel with hue around it and saturation from its centre out
/// (<see cref="ColorWheel"/>), a brightness slider, a preview, and Add and Cancel. It sits on the map's own canvas, after
/// the map, as <see cref="MapSharingPanel"/> does, so its plate takes clicks before the map. A press on it is only
/// recorded; <see cref="Render"/> follows the cursor in the plugin's guarded update while the button is held, and Add
/// is the host's to apply. Closed, it costs nothing per frame. The wheel and the slider's ramp are textures built once
/// and kept for the session, and brightness tints them rather than rebuilding them.
/// </summary>
internal sealed class MapColorPanel
{
    private const float Px = 0.75f;
    private const float Width = 228f;
    private const float Height = 306f;
    private const float Pad = 12f;
    private const float WheelSize = 180f;
    private const float SliderWidth = Width - 2f * Pad;
    private const float ButtonWidth = 72f;
    private const float ButtonGap = 8f;
    private const float AddX = Width - Pad - 2f * ButtonWidth - ButtonGap;
    private const float RowTop = 266f;
    private const float RowHeight = 28f;
    private const int WheelTextureSize = 256;

    private static readonly Color Ground = Rgb(0x050E07, 0.96f);
    private static readonly Color HudGreen = Rgb(0x2BE127, 1f);
    private static readonly Color Hint = Rgb(0xC8F5CD, 1f);
    private static readonly Color Rim = Rgb(0x2BE127, 0.45f);
    private static readonly Color MarkerColor = Rgb(0xE8FFE8, 1f);
    private static readonly Color MarkerRim = Rgb(0x050E07, 0.9f);

    private static Texture2D? _wheelTexture;
    private static Texture2D? _rampTexture;

    private readonly RectTransform _root;
    private readonly List<TextMeshProUGUI> _texts = new();
    private readonly RawImage _wheel;
    private readonly RectTransform _marker;
    private readonly RectTransform _slider;
    private readonly RawImage _ramp;
    private readonly RectTransform _thumb;
    private readonly FeatheredRect _preview;
    /// <summary>The plate and the buttons: a press there only holds the mouse, and a <c>Button</c> would take it before the plate.</summary>
    private readonly List<PanelPress> _holds = new();
    private readonly PanelPress _wheelPress;
    private readonly PanelPress _sliderPress;
    private TMP_Text? _fontSource;
    private (float Hue, float Saturation, float Value) _hsv;
    private (float Hue, float Saturation, float Value) _shown = (-1f, -1f, -1f);
    private Dragging _dragging;
    private bool _add;

    public MapColorPanel(RectTransform parent)
    {
        _root = NewPlate("CustomColor", parent);
        _holds.Add(_root.gameObject.AddComponent<PanelPress>());
        var title = NewText(_root, "Custom colour", 14f);
        Box(title.rectTransform, Pad, 6f, Width - 2f * Pad, 24f);

        _wheel = NewRect("Wheel", _root).gameObject.AddComponent<RawImage>();
        Box(_wheel.rectTransform, (Width - WheelSize) * 0.5f, 34f, WheelSize, WheelSize);
        _wheelPress = _wheel.gameObject.AddComponent<PanelPress>();
        _marker = NewRect("Marker", _wheel.rectTransform);
        DrawMarker(_marker, 10f, 10f);

        var brightness = NewText(_root, "Brightness", 12f);
        Box(brightness.rectTransform, Pad, 220f, SliderWidth, 16f);
        // The hit area is taller than the bar, so the thin bar is easy to catch.
        var sliderHit = NewRect("Brightness", _root).gameObject.AddComponent<PolygonGraphic>();
        _slider = sliderHit.rectTransform;
        Box(_slider, Pad, 236f, SliderWidth, 24f);
        _sliderPress = sliderHit.gameObject.AddComponent<PanelPress>();
        _ramp = NewRect("Ramp", _slider).gameObject.AddComponent<RawImage>();
        _ramp.raycastTarget = false;
        Box(_ramp.rectTransform, 0f, 6f, SliderWidth, 12f);
        var rampFrame = NewRect("Frame", _slider).gameObject.AddComponent<StrokeGraphic>();
        rampFrame.raycastTarget = false;
        AddRect(rampFrame, 0f, 6f, SliderWidth, 12f, 0.5f, Rim, 0f, Rim);
        rampFrame.Apply();
        _thumb = NewRect("Thumb", _slider);
        DrawMarker(_thumb, 6f, 20f);

        _preview = NewRect("Preview", _root).gameObject.AddComponent<FeatheredRect>();
        _preview.raycastTarget = false;
        _preview.SetEdges(Px, Rim, Px);
        Box(_preview.rectTransform, Pad, RowTop, AddX - ButtonGap - Pad, RowHeight);
        var add = NewButton(_root, "Add", () => _add = true);
        Box(add, AddX, RowTop, ButtonWidth, RowHeight);
        var cancel = NewButton(_root, "Cancel", Close);
        Box(cancel, Width - Pad - ButtonWidth, RowTop, ButtonWidth, RowHeight);
        Box(_root, 0f, 0f, Width, Height);
        SetActive(_root.gameObject, false);
    }

    private enum Dragging
    {
        None,
        Wheel,
        Slider,
    }

    public bool Open { get; private set; }

    /// <summary>For map labels to keep clear of while it's open.</summary>
    public RectTransform Area => _root;

    /// <summary>Goes up whenever it opens or closes.</summary>
    public int LayoutVersion { get; private set; }

    /// <summary>The colour the wheel and slider pick.</summary>
    public ShapeColor Picked => ColorWheel.ToRgb(_hsv.Hue, _hsv.Saturation, _hsv.Value);

    /// <summary>A press landed on the panel this frame. Read before <see cref="Render"/>, which takes it.</summary>
    public bool Pressed
    {
        get
        {
            foreach (var hold in _holds)
            {
                if (hold.Pressed) return true;
            }

            return _wheelPress.Pressed || _sliderPress.Pressed;
        }
    }

    /// <summary>Whether Add was clicked since the last call.</summary>
    public bool TakeAdd()
    {
        var add = _add;
        _add = false;
        return add;
    }

    /// <summary>Opens on <paramref name="start"/>, the drawing colour, so Add straight away keeps it.</summary>
    public void Show(ShapeColor start)
    {
        if (Open)
        {
            return;
        }

        // Built on the first opening rather than with the menu, so a session that never opens the picker never pays for them.
        if (_wheel.texture == null)
        {
            _wheel.texture = WheelTexture();
            _ramp.texture = RampTexture();
        }

        _hsv = ColorWheel.FromRgb(start);
        _shown = (-1f, -1f, -1f);
        _dragging = Dragging.None;
        _add = false;
        Open = true;
        LayoutVersion++;
        SetActive(_root.gameObject, true);
    }

    public void Close()
    {
        _add = false;
        _dragging = Dragging.None;
        if (!Open)
        {
            return;
        }

        Open = false;
        LayoutVersion++;
        SetActive(_root.gameObject, false);
    }

    /// <summary>Its top-left corner in design pixels from the map's.</summary>
    public void Place(float x, float y) => Box(_root, x, y, Width, Height);

    /// <summary>Per frame while the full map is open. Returns straight away while closed.</summary>
    public void Render(TMP_Text? hudStyle)
    {
        if (!Open)
        {
            return;
        }

        if (hudStyle != null && !ReferenceEquals(hudStyle, _fontSource))
        {
            _fontSource = hudStyle;
            foreach (var text in _texts)
            {
                text.font = hudStyle.font;
                text.fontSharedMaterial = hudStyle.fontSharedMaterial;
                text.fontStyle = hudStyle.fontStyle;
            }
        }

        foreach (var hold in _holds)
        {
            hold.Pressed = false;
        }

        if (_wheelPress.Pressed)
        {
            _wheelPress.Pressed = false;
            var (x, y) = Normalized(_wheel.rectTransform, _wheelPress.EventCamera);
            if (ColorWheel.Contains(x, y))
            {
                _dragging = Dragging.Wheel;
            }
        }

        if (_sliderPress.Pressed)
        {
            _sliderPress.Pressed = false;
            _dragging = Dragging.Slider;
        }

        if (_dragging != Dragging.None && !Input.GetMouseButton(0))
        {
            _dragging = Dragging.None;
        }

        if (_dragging == Dragging.Wheel)
        {
            var (x, y) = Normalized(_wheel.rectTransform, _wheelPress.EventCamera);
            var (hue, saturation) = ColorWheel.Pick(x, y);
            _hsv = (hue, saturation, _hsv.Value);
        }
        else if (_dragging == Dragging.Slider)
        {
            var (x, _) = Normalized(_slider, _sliderPress.EventCamera);
            _hsv = (_hsv.Hue, _hsv.Saturation, Mathf.Clamp01((x + 1f) * 0.5f));
        }

        ShowPicked();
    }

    /// <summary>Moves the marker and thumb and recolours the wheel, ramp, and preview, only when the pick changed.</summary>
    private void ShowPicked()
    {
        if (_hsv == _shown)
        {
            return;
        }

        _shown = _hsv;
        var (x, y) = ColorWheel.Position(_hsv.Hue, _hsv.Saturation);
        _marker.anchoredPosition = new Vector2((1f + x) * WheelSize * 0.5f * Px, -(1f - y) * WheelSize * 0.5f * Px);
        _thumb.anchoredPosition = new Vector2(_hsv.Value * SliderWidth * Px, -12f * Px);
        // The texture is the wheel at full brightness; a colour's RGB scales with its value, so a grey tint dims it.
        _wheel.color = new Color(_hsv.Value, _hsv.Value, _hsv.Value, 1f);
        _ramp.color = ColorWheel.ToRgb(_hsv.Hue, _hsv.Saturation, 1f).ToColor32();
        _preview.color = Picked.ToColor32();
    }

    /// <summary>The cursor over <paramref name="rect"/>, from its centre, in half its width and height, Y up.</summary>
    private static (float X, float Y) Normalized(RectTransform rect, Camera? camera)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, Input.mousePosition, camera, out var local);
        var box = rect.rect;
        return ((local.x - box.center.x) / (box.width * 0.5f), (local.y - box.center.y) / (box.height * 0.5f));
    }

    /// <summary>A light open rectangle with a dark rim, centred on <paramref name="container"/>'s origin, which is moved to place it.</summary>
    private static void DrawMarker(RectTransform container, float width, float height)
    {
        Box(container, 0f, 0f, 0f, 0f);
        var marker = NewRect("Outline", container).gameObject.AddComponent<StrokeGraphic>();
        marker.raycastTarget = false;
        AddRect(marker, -width * 0.5f, -height * 0.5f, width, height, 1f, MarkerColor, 1f, MarkerRim);
        marker.Apply();
    }

    /// <summary>A closed rectangle stroke, centred on its edges, in design pixels from the parent's top-left.</summary>
    private static void AddRect(StrokeGraphic graphic, float x, float y, float width, float height, float halfWidth, Color fill, float rimWidth,
        Color rim)
    {
        graphic.AddPoint(new Vector2(x * Px, -y * Px));
        graphic.AddPoint(new Vector2((x + width) * Px, -y * Px));
        graphic.AddPoint(new Vector2((x + width) * Px, -(y + height) * Px));
        graphic.AddPoint(new Vector2(x * Px, -(y + height) * Px));
        graphic.EndStroke(true, halfWidth * Px, fill, rimWidth * Px, rim, Px);
    }

    /// <summary>The wheel at full brightness, built the first time a panel needs it and kept, since every panel draws the same one.</summary>
    private static Texture2D WheelTexture()
    {
        if (_wheelTexture != null)
        {
            return _wheelTexture;
        }

        var pixels = new Color32[WheelTextureSize * WheelTextureSize];
        for (var y = 0; y < WheelTextureSize; y++)
        {
            for (var x = 0; x < WheelTextureSize; x++)
            {
                var (color, alpha) = ColorWheel.Texel(x, y, WheelTextureSize);
                pixels[y * WheelTextureSize + x] = new Color32(color.R, color.G, color.B, alpha);
            }
        }

        return _wheelTexture = NewTexture("NoMapOverhaulColorWheel", WheelTextureSize, WheelTextureSize, pixels);
    }

    /// <summary>Black to white, left to right: tinted with the picked hue at full brightness, it shows every brightness of it.</summary>
    private static Texture2D RampTexture()
    {
        if (_rampTexture != null)
        {
            return _rampTexture;
        }

        var pixels = new Color32[256];
        for (var x = 0; x < pixels.Length; x++)
        {
            pixels[x] = new Color32((byte)x, (byte)x, (byte)x, 255);
        }

        return _rampTexture = NewTexture("NoMapOverhaulBrightnessRamp", pixels.Length, 1, pixels);
    }

    /// <summary>Kept through scene changes and asset unloads, so it's built once a session.</summary>
    private static Texture2D NewTexture(string name, int width, int height, Color32[] pixels)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private RectTransform NewButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        var rect = NewPlate(text, parent);
        var fill = NewRect("Fill", rect).gameObject.AddComponent<Image>();
        Stretch(fill.rectTransform);
        fill.color = HudGreen;
        fill.raycastTarget = false;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.colors = new ColorBlock
        {
            normalColor = new Color(1f, 1f, 1f, 0f),
            highlightedColor = new Color(1f, 1f, 1f, 0.08f),
            pressedColor = new Color(1f, 1f, 1f, 0.14f),
            selectedColor = new Color(1f, 1f, 1f, 0f),
            disabledColor = Color.clear,
            colorMultiplier = 1f,
            fadeDuration = 0.05f,
        };
        button.onClick.AddListener(onClick);
        _holds.Add(rect.gameObject.AddComponent<PanelPress>()); // the event system calls every press handler on the button's object
        var label = NewText(rect, text, 13f);
        Stretch(label.rectTransform);
        label.alignment = TextAlignmentOptions.Center;
        return rect;
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

    /// <summary>Records a left press on part of the panel, and the camera to read the cursor through, for the panel to act on in the guarded update.</summary>
    private sealed class PanelPress : MonoBehaviour, IPointerDownHandler
    {
        public bool Pressed { get; set; }

        public Camera? EventCamera { get; private set; }

        void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            Pressed = true;
            EventCamera = eventData.pressEventCamera;
        }

        /// <summary>Hidden with the panel: no press carries over to its next opening.</summary>
        private void OnDisable() => Pressed = false;
    }
}
