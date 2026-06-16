#!/usr/bin/env python3
from __future__ import annotations

import json
import random
import shutil
import subprocess
import struct
import zlib
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2]
BASELINE_TAG = "kg/20260607-1933-before-registry-migration"
BASELINE_SOURCE_DIR = "Assets/Blockiverse/Art/Textures/Blocks/Source"
CURRENT_SOURCE_DIR = PROJECT_ROOT / "Assets/Blockiverse/Art/Textures/Blocks/Source"
OUTPUT_DIR = PROJECT_ROOT / "docs/art/texture-method-comparison-2026-06-13"
TILE_SIZE = 32

TEXTURES = [
    {
        "legacy": "meadow_turf",
        "canonical": "meadow_turf",
        "label": "Meadow Turf",
        "prompt": "lush green meadow turf grass block top texture, lively speckled grass blades, voxel game pixel art tile",
    },
    {
        "legacy": "loam",
        "canonical": "loose_loam",
        "label": "Loose Loam",
        "prompt": "rich brown loose loam soil block top texture, small roots and soil crumbs, voxel game pixel art tile",
    },
    {
        "legacy": "slate",
        "canonical": "graystone",
        "label": "Graystone",
        "prompt": "cool gray stone block texture, fractured stone flecks and subtle strata, voxel game pixel art tile",
    },
    {
        "legacy": "timber",
        "canonical": "branchwood_log",
        "label": "Branchwood Log",
        "prompt": "warm branchwood log bark block texture, vertical bark ridges and cut knots, voxel game pixel art tile",
    },
    {
        "legacy": "leafmass",
        "canonical": "leafmoss",
        "label": "Leafmoss",
        "prompt": "dense leafy green leafmoss block texture, clustered leaves and mossy variation, voxel game pixel art tile",
    },
    {
        "legacy": "clearstone",
        "canonical": "lumen_quartz_cluster",
        "label": "Lumen Quartz Cluster",
        "prompt": "cyan glowing quartz crystal cluster embedded in stone, luminous facets, voxel game pixel art tile",
    },
    {
        "legacy": "coalstone",
        "canonical": "embercoal_seam",
        "label": "Embercoal Seam",
        "prompt": "dark stone with warm embercoal vein flecks, smoky black rock with orange glow details, voxel game pixel art tile",
    },
    {
        "legacy": "copperstone",
        "canonical": "rosycopper_bloom",
        "label": "Rosycopper Bloom",
        "prompt": "stone with rose copper mineral blooms and green oxidation specks, voxel game pixel art tile",
    },
    {
        "legacy": "ironstone",
        "canonical": "rustcore_ore",
        "label": "Rustcore Ore",
        "prompt": "dark stone with rusty iron ore chunks and red brown mineral veins, voxel game pixel art tile",
    },
    {
        "legacy": "workbench",
        "canonical": "build_table",
        "label": "Build Table",
        "prompt": "crafted wooden build table top texture, planks, tool marks, small pegs, voxel game pixel art tile",
    },
    {
        "legacy": "torchbud",
        "canonical": "glowwick",
        "label": "Glowwick",
        "prompt": "small warm glowing plant lamp texture, amber wick glow in mossy casing, voxel game pixel art tile",
    },
    {
        "legacy": "storage_crate",
        "canonical": "storage_crate",
        "label": "Storage Crate",
        "prompt": "wooden storage crate block texture, plank seams, reinforced corners, voxel game pixel art tile",
    },
]


def png_chunk(tag: bytes, payload: bytes) -> bytes:
    return (
        struct.pack(">I", len(payload))
        + tag
        + payload
        + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)
    )


def write_rgb_png(path: Path, pixels: list[list[tuple[int, int, int]]]) -> None:
    height = len(pixels)
    width = len(pixels[0])
    raw_rows = []
    for row in pixels:
        raw_rows.append(b"\x00" + b"".join(bytes(rgb) for rgb in row))

    payload = b"".join(
        [
            b"\x89PNG\r\n\x1a\n",
            png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)),
            png_chunk(b"IDAT", zlib.compress(b"".join(raw_rows), level=9)),
            png_chunk(b"IEND", b""),
        ]
    )
    path.write_bytes(payload)


def new_tile(color: tuple[int, int, int]) -> list[list[tuple[int, int, int]]]:
    return [[color for _ in range(TILE_SIZE)] for _ in range(TILE_SIZE)]


def put(
    tile: list[list[tuple[int, int, int]]],
    x: int,
    y: int,
    color: tuple[int, int, int],
) -> None:
    tile[y % TILE_SIZE][x % TILE_SIZE] = color


def rect(
    tile: list[list[tuple[int, int, int]]],
    x: int,
    y: int,
    width: int,
    height: int,
    color: tuple[int, int, int],
) -> None:
    for yy in range(y, y + height):
        for xx in range(x, x + width):
            put(tile, xx, yy, color)


def line(
    tile: list[list[tuple[int, int, int]]],
    x0: int,
    y0: int,
    x1: int,
    y1: int,
    color: tuple[int, int, int],
) -> None:
    dx = abs(x1 - x0)
    sx = 1 if x0 < x1 else -1
    dy = -abs(y1 - y0)
    sy = 1 if y0 < y1 else -1
    err = dx + dy
    x = x0
    y = y0
    while True:
        put(tile, x, y, color)
        if x == x1 and y == y1:
            break
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x += sx
        if e2 <= dx:
            err += dx
            y += sy


def scatter_rects(
    tile: list[list[tuple[int, int, int]]],
    rng: random.Random,
    colors: list[tuple[int, int, int]],
    count: int,
    min_size: int,
    max_size: int,
) -> None:
    for _ in range(count):
        color = rng.choice(colors)
        width = rng.randint(min_size, max_size)
        height = rng.randint(min_size, max_size)
        rect(tile, rng.randrange(TILE_SIZE), rng.randrange(TILE_SIZE), width, height, color)


def stipple(
    tile: list[list[tuple[int, int, int]]],
    rng: random.Random,
    colors: list[tuple[int, int, int]],
    count: int,
) -> None:
    for _ in range(count):
        put(tile, rng.randrange(TILE_SIZE), rng.randrange(TILE_SIZE), rng.choice(colors))


def meadow_turf_tile() -> list[list[tuple[int, int, int]]]:
    rng = random.Random(1101)
    tile = new_tile((41, 137, 58))
    scatter_rects(tile, rng, [(69, 180, 74), (88, 204, 91), (34, 111, 54)], 84, 1, 3)
    scatter_rects(tile, rng, [(111, 221, 105), (81, 196, 89)], 30, 1, 2)
    stipple(tile, rng, [(25, 99, 49), (121, 225, 118)], 44)
    return tile


def loose_loam_tile() -> list[list[tuple[int, int, int]]]:
    rng = random.Random(1102)
    tile = new_tile((112, 69, 42))
    scatter_rects(tile, rng, [(132, 83, 49), (91, 54, 34), (151, 99, 58)], 62, 1, 3)
    for _ in range(9):
        x = rng.randrange(TILE_SIZE)
        y = rng.randrange(TILE_SIZE)
        color = rng.choice([(177, 136, 82), (81, 52, 35)])
        line(tile, x, y, x + rng.randint(-5, 5), y + rng.randint(3, 8), color)
    stipple(tile, rng, [(64, 40, 28), (170, 112, 64)], 38)
    return tile


def graystone_tile() -> list[list[tuple[int, int, int]]]:
    rng = random.Random(1103)
    tile = new_tile((101, 114, 120))
    scatter_rects(tile, rng, [(121, 136, 143), (78, 88, 95), (145, 156, 160)], 52, 2, 5)
    for y in (7, 16, 25):
        for x in range(0, TILE_SIZE, 2):
            put(tile, x, y + rng.choice((-1, 0, 1)), rng.choice([(70, 80, 87), (132, 145, 150)]))
    stipple(tile, rng, [(61, 70, 78), (155, 164, 168)], 42)
    return tile


def branchwood_log_tile() -> list[list[tuple[int, int, int]]]:
    rng = random.Random(1104)
    tile = new_tile((134, 84, 35))
    for x in range(0, TILE_SIZE, 4):
        rect(tile, x, 0, 1, TILE_SIZE, rng.choice([(94, 54, 28), (164, 103, 42)]))
        rect(tile, x + 2, 0, 1, TILE_SIZE, rng.choice([(112, 67, 31), (184, 118, 48)]))
    for _ in range(10):
        x = rng.randrange(TILE_SIZE)
        y = rng.randrange(TILE_SIZE)
        color = rng.choice([(78, 44, 25), (196, 134, 57)])
        line(tile, x, y, x + rng.randint(-1, 2), y + rng.randint(4, 9), color)
    for _ in range(3):
        x = rng.randrange(TILE_SIZE)
        y = rng.randrange(TILE_SIZE)
        rect(tile, x, y, 3, 2, (91, 49, 22))
        put(tile, x + 1, y, (185, 119, 50))
    return tile


def leafmoss_tile() -> list[list[tuple[int, int, int]]]:
    rng = random.Random(1105)
    tile = new_tile((48, 142, 60))
    scatter_rects(tile, rng, [(61, 171, 66), (88, 193, 73), (31, 114, 51)], 94, 1, 3)
    for _ in range(24):
        x = rng.randrange(TILE_SIZE)
        y = rng.randrange(TILE_SIZE)
        color = rng.choice([(119, 215, 84), (29, 100, 47)])
        rect(tile, x, y, rng.randint(2, 4), 1, color)
        rect(tile, x + 1, y - 1, 1, rng.randint(2, 3), color)
    return tile


def lumen_quartz_cluster_tile() -> list[list[tuple[int, int, int]]]:
    rng = random.Random(1106)
    tile = new_tile((104, 186, 200))
    scatter_rects(tile, rng, [(126, 209, 221), (83, 157, 176), (162, 236, 239)], 44, 1, 3)
    for offset in (-1, 0, 1):
        line(tile, 3, 28 + offset, 28, 3 + offset, (204, 250, 250))
        line(tile, 4, 3 + offset, 27, 27 + offset, (191, 239, 243))
    for x, y, h in ((7, 17, 10), (16, 10, 14), (24, 18, 8)):
        line(tile, x, y, x + 3, y - h, (218, 255, 255))
        line(tile, x + 3, y - h, x + 6, y, (109, 200, 220))
        line(tile, x, y, x + 6, y, (65, 142, 165))
    stipple(tile, rng, [(221, 255, 255), (55, 132, 158)], 34)
    return tile


def embercoal_seam_tile() -> list[list[tuple[int, int, int]]]:
    rng = random.Random(1107)
    tile = new_tile((36, 38, 42))
    scatter_rects(tile, rng, [(48, 51, 56), (24, 25, 29), (60, 60, 64)], 54, 1, 4)
    for _ in range(8):
        x = rng.randrange(TILE_SIZE)
        y = rng.randrange(TILE_SIZE)
        color = rng.choice([(224, 82, 32), (255, 136, 45), (129, 49, 31)])
        line(tile, x, y, x + rng.randint(-7, 7), y + rng.randint(2, 9), color)
        put(tile, x + 1, y, (255, 179, 69))
    stipple(tile, rng, [(16, 17, 20), (92, 45, 34)], 28)
    return tile


def rosycopper_bloom_tile() -> list[list[tuple[int, int, int]]]:
    rng = random.Random(1108)
    tile = new_tile((82, 83, 78))
    scatter_rects(tile, rng, [(101, 98, 88), (58, 62, 62), (123, 117, 98)], 42, 2, 4)
    scatter_rects(tile, rng, [(192, 101, 73), (223, 131, 86), (150, 79, 60)], 26, 1, 4)
    stipple(tile, rng, [(84, 168, 146), (58, 130, 119), (232, 156, 99)], 34)
    return tile


def rustcore_ore_tile() -> list[list[tuple[int, int, int]]]:
    rng = random.Random(1109)
    tile = new_tile((80, 86, 85))
    scatter_rects(tile, rng, [(98, 104, 101), (57, 63, 65), (119, 121, 114)], 46, 1, 4)
    for _ in range(12):
        x = rng.randrange(TILE_SIZE)
        y = rng.randrange(TILE_SIZE)
        color = rng.choice([(168, 72, 43), (206, 91, 48), (112, 48, 37)])
        line(tile, x, y, x + rng.randint(-5, 5), y + rng.randint(-4, 7), color)
    scatter_rects(tile, rng, [(207, 112, 60), (139, 58, 40)], 16, 1, 2)
    return tile


def build_table_tile() -> list[list[tuple[int, int, int]]]:
    rng = random.Random(1110)
    tile = new_tile((157, 103, 43))
    for x in (8, 16, 24):
        rect(tile, x, 0, 2, TILE_SIZE, (104, 63, 29))
        rect(tile, x + 2, 0, 1, TILE_SIZE, (197, 134, 59))
    for y in (10, 21):
        rect(tile, 0, y, TILE_SIZE, 2, (118, 72, 31))
    scatter_rects(tile, rng, [(186, 126, 52), (111, 66, 30), (213, 153, 70)], 32, 1, 2)
    for x, y in ((5, 5), (27, 7), (13, 18), (24, 26)):
        rect(tile, x, y, 2, 2, (84, 49, 23))
        put(tile, x, y, (222, 164, 78))
    return tile


def glowwick_tile() -> list[list[tuple[int, int, int]]]:
    rng = random.Random(1111)
    tile = new_tile((47, 84, 45))
    scatter_rects(tile, rng, [(62, 110, 53), (34, 67, 42), (82, 132, 57)], 38, 1, 3)
    for radius, color in (
        (8, (96, 126, 45)),
        (6, (165, 156, 47)),
        (4, (245, 180, 51)),
        (2, (255, 226, 92)),
    ):
        for y in range(16 - radius, 16 + radius + 1):
            for x in range(16 - radius, 16 + radius + 1):
                if (x - 16) * (x - 16) + (y - 16) * (y - 16) <= radius * radius:
                    put(tile, x, y, color)
    for _ in range(18):
        put(tile, rng.randrange(TILE_SIZE), rng.randrange(TILE_SIZE), rng.choice([(255, 206, 70), (96, 148, 61)]))
    return tile


def storage_crate_tile() -> list[list[tuple[int, int, int]]]:
    rng = random.Random(1112)
    tile = new_tile((139, 88, 39))
    for x in range(0, TILE_SIZE, 5):
        rect(tile, x, 0, 1, TILE_SIZE, (93, 55, 25))
        rect(tile, x + 1, 0, 1, TILE_SIZE, (176, 115, 51))
    rect(tile, 0, 0, TILE_SIZE, 3, (90, 52, 23))
    rect(tile, 0, 29, TILE_SIZE, 3, (90, 52, 23))
    rect(tile, 0, 0, 3, TILE_SIZE, (90, 52, 23))
    rect(tile, 29, 0, 3, TILE_SIZE, (90, 52, 23))
    line(tile, 5, 5, 26, 26, (88, 51, 24))
    line(tile, 6, 26, 26, 6, (191, 128, 59))
    scatter_rects(tile, rng, [(177, 113, 48), (103, 61, 27)], 22, 1, 2)
    for x, y in ((4, 4), (27, 4), (4, 27), (27, 27)):
        rect(tile, x, y, 2, 2, (49, 37, 29))
    return tile


PRODUCTION_HYBRID_GENERATORS = {
    "meadow_turf": meadow_turf_tile,
    "loose_loam": loose_loam_tile,
    "graystone": graystone_tile,
    "branchwood_log": branchwood_log_tile,
    "leafmoss": leafmoss_tile,
    "lumen_quartz_cluster": lumen_quartz_cluster_tile,
    "embercoal_seam": embercoal_seam_tile,
    "rosycopper_bloom": rosycopper_bloom_tile,
    "rustcore_ore": rustcore_ore_tile,
    "build_table": build_table_tile,
    "glowwick": glowwick_tile,
    "storage_crate": storage_crate_tile,
}


def run_git_show(path: str) -> bytes:
    return subprocess.check_output(["git", "show", f"{BASELINE_TAG}:{path}"], cwd=PROJECT_ROOT)


def lfs_object_from_pointer(pointer: bytes) -> Path | None:
    try:
        text = pointer.decode("utf-8")
    except UnicodeDecodeError:
        return None

    if "https://git-lfs.github.com/spec/v1" not in text:
        return None

    oid = None
    for line in text.splitlines():
        if line.startswith("oid sha256:"):
            oid = line.split(":", 1)[1].strip()
            break

    if not oid:
        raise RuntimeError("Git LFS pointer did not contain a sha256 oid.")

    path = PROJECT_ROOT / ".git/lfs/objects" / oid[:2] / oid[2:4] / oid
    if not path.exists():
        raise FileNotFoundError(f"Missing local Git LFS object for {oid}: {path}")
    return path


def extract_baseline_texture(legacy_id: str, destination: Path) -> None:
    source_path = f"{BASELINE_SOURCE_DIR}/{legacy_id}.png"
    payload = run_git_show(source_path)
    lfs_path = lfs_object_from_pointer(payload)
    if lfs_path is not None:
        shutil.copyfile(lfs_path, destination)
    else:
        destination.write_bytes(payload)


def write_html() -> None:
    rows = []
    for texture in TEXTURES:
        legacy = texture["legacy"]
        canonical = texture["canonical"]
        label = texture["label"]
        ai_path = f"ai/{canonical}.png"
        rows.append(
            f"""
            <section class="texture-row" id="{canonical}">
              <h2>{label}</h2>
              <p><code>{legacy}</code> -> <code>{canonical}</code></p>
              <div class="grid">
                <figure>
                  <img src="original/{legacy}.png" alt="{label} original MVP texture">
                  <figcaption>Original MVP</figcaption>
                </figure>
                <figure>
                  <img src="current/{canonical}.png" alt="{label} current procedural texture">
                  <figcaption>Current Procedural</figcaption>
                </figure>
                <figure>
                  <img src="{ai_path}" alt="{label} AI candidate texture" onerror="this.classList.add('missing'); this.alt='AI candidate pending';">
                  <figcaption>AI Candidate</figcaption>
                </figure>
                <figure>
                  <img src="ai_simplified/{canonical}.png" alt="{label} simplified AI candidate texture" onerror="this.classList.add('missing'); this.alt='simplified AI candidate pending';">
                  <figcaption>AI Simplified</figcaption>
                </figure>
                <figure>
                  <img src="production_hybrid/{canonical}.png" alt="{label} production hybrid texture">
                  <figcaption>Production Hybrid</figcaption>
                </figure>
              </div>
            </section>
            """.strip()
        )

    html = f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Blockiverse Texture Method Comparison</title>
  <style>
    :root {{
      color-scheme: dark;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
      background: #101315;
      color: #e9eef0;
    }}
    body {{
      margin: 0;
      padding: 32px;
    }}
    header {{
      max-width: 1680px;
      margin: 0 auto 28px;
    }}
    h1 {{
      margin: 0 0 8px;
      font-size: 28px;
      font-weight: 720;
    }}
    p {{
      margin: 0;
      color: #b8c5c9;
      line-height: 1.45;
    }}
    .texture-row {{
      max-width: 1680px;
      margin: 0 auto 30px;
      padding-bottom: 30px;
      border-bottom: 1px solid #273034;
    }}
    .texture-row h2 {{
      margin: 0 0 4px;
      font-size: 18px;
      font-weight: 680;
    }}
    code {{
      color: #d7f3ba;
      font-size: 13px;
    }}
    .grid {{
      display: grid;
      grid-template-columns: repeat(5, minmax(0, 1fr));
      gap: 14px;
      margin-top: 14px;
    }}
    figure {{
      margin: 0;
      padding: 14px;
      background: #171d20;
      border: 1px solid #2d383d;
      border-radius: 8px;
    }}
    img {{
      display: block;
      width: 100%;
      aspect-ratio: 1 / 1;
      object-fit: contain;
      background:
        linear-gradient(45deg, #263035 25%, transparent 25%),
        linear-gradient(-45deg, #263035 25%, transparent 25%),
        linear-gradient(45deg, transparent 75%, #263035 75%),
        linear-gradient(-45deg, transparent 75%, #263035 75%);
      background-size: 24px 24px;
      background-position: 0 0, 0 12px, 12px -12px, -12px 0;
      image-rendering: pixelated;
    }}
    img.missing {{
      opacity: 0.2;
    }}
    figcaption {{
      margin-top: 10px;
      color: #dfe8eb;
      font-size: 13px;
      text-align: center;
    }}
    @media (max-width: 760px) {{
      body {{
        padding: 18px;
      }}
      .grid {{
        grid-template-columns: 1fr;
      }}
    }}
  </style>
</head>
<body>
  <header>
    <h1>Blockiverse Texture Method Comparison</h1>
    <p>Legacy MVP textures from <code>{BASELINE_TAG}</code>, current canonical textures from the working tree, first-pass AI candidates, simplified AI candidates, and deterministic production-hybrid candidates for the same texture concepts.</p>
  </header>
  {"".join(rows)}
</body>
</html>
"""
    (OUTPUT_DIR / "comparison.html").write_text(html, encoding="utf-8")


def main() -> None:
    for subdir in ("original", "current", "ai", "ai_simplified", "production_hybrid"):
        (OUTPUT_DIR / subdir).mkdir(parents=True, exist_ok=True)

    manifest = []
    for texture in TEXTURES:
        legacy = texture["legacy"]
        canonical = texture["canonical"]
        original_path = OUTPUT_DIR / "original" / f"{legacy}.png"
        current_path = OUTPUT_DIR / "current" / f"{canonical}.png"
        production_hybrid_path = OUTPUT_DIR / "production_hybrid" / f"{canonical}.png"
        current_source = CURRENT_SOURCE_DIR / f"{canonical}.png"

        extract_baseline_texture(legacy, original_path)
        if not current_source.exists():
            raise FileNotFoundError(f"Missing current canonical texture: {current_source}")
        shutil.copyfile(current_source, current_path)

        production_hybrid_generator = PRODUCTION_HYBRID_GENERATORS.get(canonical)
        if production_hybrid_generator is None:
            raise KeyError(f"Missing production hybrid generator for {canonical}.")
        write_rgb_png(production_hybrid_path, production_hybrid_generator())

        manifest.append(
            {
                **texture,
                "ai_simplified": f"ai_simplified/{canonical}.png",
                "production_hybrid": f"production_hybrid/{canonical}.png",
            }
        )

    (OUTPUT_DIR / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    write_html()
    print(OUTPUT_DIR)


if __name__ == "__main__":
    main()
