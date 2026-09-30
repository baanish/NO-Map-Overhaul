"""Print the full map's canvas and the game's HUD around it (the VirtualMFD) from the game's asset files, offline.

Usage (any Python with UnityPy and TypeTreeGeneratorAPI installed):
    python tools/dump_map_ui.py "D:/SteamLibrary/steamapps/common/Nuclear Option"

For each object it prints the RectTransform (anchors, anchored position, size, pivot, scale) and its components, with
the canvases' sort order and CanvasScaler settings and DynamicMap's map sizes. MenuLayout.HudBoxes in the mod holds the
speed, altitude, and attitude readouts, the mission clock, and the MFD button columns this prints; rerun it after a game
update to check them. The clock's panel sizes itself to its text at runtime, so its box comes from the text's width and
the panel's layout padding.
"""

import sys
from pathlib import Path

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

UNITY_VERSION = "2022.3.62f1"
ROOTS = {"MaximizedMapCanvas": 1, "VirtualMFD": 1, "TopInstruments": 2}
MARKERS = (("m_fontSize", "Text"), ("m_HorizontalFit", "ContentSizeFitter"), ("m_UiScaleMode", "CanvasScaler"), ("m_Softness", "RectMask2D"), ("m_BlockingObjects", "GraphicRaycaster"),
           ("maximizedMapCanvas", "DynamicMap"), ("leftButtons", "VirtualMFD"), ("m_Spacing", "LayoutGroup"), ("m_Sprite", "Image"))


def v2(v):
    return f"({v.x:g}, {v.y:g})"


def rect(transform):
    return (f"anchors {v2(transform.m_AnchorMin)}-{v2(transform.m_AnchorMax)} pos {v2(transform.m_AnchoredPosition)} "
            f"size {v2(transform.m_SizeDelta)} pivot {v2(transform.m_Pivot)} scale {transform.m_LocalScale.x:g}")


def component(obj):
    if obj.type.name == "Canvas":
        tree = obj.read_typetree()
        return f"Canvas(order {tree['m_SortingOrder']}, override {tree['m_OverrideSorting']})"
    if obj.type.name != "MonoBehaviour":
        return obj.type.name
    try:
        tree = obj.read_typetree()
    except Exception:
        return "?"
    kind = next((name for key, name in MARKERS if key in tree), "MonoBehaviour")
    if kind == "CanvasScaler":
        return (f"CanvasScaler(mode {tree['m_UiScaleMode']}, reference {tree['m_ReferenceResolution']}, "
                f"match {tree['m_MatchWidthOrHeight']})")
    if kind == "DynamicMap":
        return f"DynamicMap(maximized {tree['mapScaleMaximized']}, minimized {tree['mapScaleMinimized']})"
    if kind == "Text":
        return f"Text({tree['m_text']!r}, size {tree['m_fontSize']:g})"
    if kind == "LayoutGroup":
        padding = tree["m_Padding"]
        return f"LayoutGroup(padding {padding['m_Left']} {padding['m_Right']} {padding['m_Top']} {padding['m_Bottom']})"
    return kind


def dump(transform, depth, max_depth):
    go = transform.m_GameObject.read()
    parts = ", ".join(component(c.component.deref()) for c in go.m_Component if c.component.deref().type.name != "RectTransform")
    print(f"{'  ' * depth}{go.m_Name}: {rect(transform)} [{parts}]")
    if depth < max_depth:
        for child in transform.m_Children:
            dump(child.read(), depth + 1, max_depth)


def main(game_dir):
    data = Path(game_dir) / "NuclearOption_Data"
    generator = TypeTreeGenerator(UNITY_VERSION)
    generator.load_local_game(str(game_dir))
    env = UnityPy.load(str(data / "globalgamemanagers.assets"), str(data / "sharedassets1.assets"), str(data / "level1"))
    env.typetree_generator = generator
    for obj in env.objects:
        if obj.type.name != "RectTransform":
            continue
        transform = obj.read()
        name = transform.m_GameObject.read().m_Name
        if name in ROOTS:
            print(f"\n== {name} ({obj.assets_file.name})")
            dump(transform, 0, ROOTS[name])


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
