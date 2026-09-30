"""Build the README's screenshot galleries from docs/images with ffmpeg.

Screenshots come in every size and shape, and GitHub sizes table cells by their image, so a README table of them
comes out ragged. Each gallery here fits every screenshot into the same tile, letterboxed on the map's dark ground
rather than cropped, and joins the tiles into one image. Rerun after changing a screenshot:

    python tools/readme_galleries.py
"""
import subprocess
from pathlib import Path

IMAGES = Path(__file__).resolve().parent.parent / "docs" / "images"
GROUND = "0x10141a"

# Space around each screenshot, so neighbouring tiles don't run together.
GUTTER = 12

# Gallery name -> (tile width, tile height, columns, screenshots in reading order).
GALLERIES = {
    "gallery-map-tools": (640, 480, 2, ["bra", "circles", "hud-bra-note", "hud-ring"]),
    "gallery-runways": (480, 400, 3, ["airbase", "approach-line", "runway-callout"]),
}


def build(name, width, height, columns, shots):
    inputs, filters, tiles = [], [], []
    for index, shot in enumerate(shots):
        inputs += ["-i", str(IMAGES / f"{shot}.jpg")]
        # Shrink to fit, never enlarge: a small screenshot blown up to the tile goes soft.
        inner_width, inner_height = width - 2 * GUTTER, height - 2 * GUTTER
        filters.append(
            f"[{index}:v]scale='min(iw,{inner_width})':'min(ih,{inner_height})':force_original_aspect_ratio=decrease,"
            f"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color={GROUND}[t{index}]")
        tiles.append(f"[t{index}]")
    layout = "|".join(f"{(i % columns) * width}_{(i // columns) * height}" for i in range(len(shots)))
    filters.append(f"{''.join(tiles)}xstack=inputs={len(shots)}:layout={layout}[out]")
    subprocess.run(["ffmpeg", "-v", "error", "-y", *inputs, "-filter_complex", ";".join(filters),
                    "-map", "[out]", "-q:v", "3", str(IMAGES / f"{name}.jpg")], check=True)


for gallery, (tile_width, tile_height, gallery_columns, screenshots) in GALLERIES.items():
    build(gallery, tile_width, tile_height, gallery_columns, screenshots)
    print(f"docs/images/{gallery}.jpg")
