#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import shutil
import subprocess
import tempfile
import zlib
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2]
BASELINE_TAG = "kg/20260607-1933-before-registry-migration"
BASELINE_SOURCE_DIR = "Assets/Blockiverse/Art/Textures/Blocks/Source"
CURRENT_SOURCE_DIR = PROJECT_ROOT / "Assets/Blockiverse/Art/Textures/Blocks/Source"
COMPARISON_DIR = PROJECT_ROOT / "docs/art/texture-method-comparison-2026-06-13"
RAW_GENERATED_DIR = PROJECT_ROOT / "docs/art/block-texture-set-generation/raw"
TEXTURE_SET_ROOT = PROJECT_ROOT / "Assets/Blockiverse/Art/Textures/Blocks/TextureSets"
GENERATE_ART_ASSETS_PATH = PROJECT_ROOT / "scripts/art/generate-art-assets.py"


SET_IDS = ("original", "enhanced", "ai_simplified", "ai")
TILE_PIXELS = 32

MVP_LEGACY_TO_CANONICAL = {
    "meadow_turf": "meadow_turf",
    "loam": "loose_loam",
    "slate": "graystone",
    "timber": "branchwood_log",
    "leafmass": "leafmoss",
    "clearstone": "lumen_quartz_cluster",
    "coalstone": "embercoal_seam",
    "copperstone": "rosycopper_bloom",
    "ironstone": "rustcore_ore",
    "workbench": "build_table",
    "torchbud": "glowwick",
    "storage_crate": "storage_crate",
}


def load_art_generator():
    spec = importlib.util.spec_from_file_location("generate_art_assets", GENERATE_ART_ASSETS_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load art generator: {GENERATE_ART_ASSETS_PATH}")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


ART = load_art_generator()


def all_block_source_names() -> list[str]:
    names = [block[0] for block in ART.BLOCKS]
    names.extend(alias[0] for alias in ART.BLOCK_SOURCE_ALIASES)
    return names


def block_lookup() -> dict[str, tuple]:
    lookup = {block[0]: block for block in ART.BLOCKS}
    for name, _, base, accent, pattern, seed in ART.BLOCK_SOURCE_ALIASES:
        lookup[name] = (name, -1, base, accent, pattern, seed)
    return lookup


def relative(path: Path) -> str:
    return path.relative_to(PROJECT_ROOT).as_posix()


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
    payload = run_git_show(f"{BASELINE_SOURCE_DIR}/{legacy_id}.png")
    lfs_path = lfs_object_from_pointer(payload)
    destination.parent.mkdir(parents=True, exist_ok=True)
    if lfs_path is not None:
        shutil.copyfile(lfs_path, destination)
    else:
        destination.write_bytes(payload)


def png_chunk(tag: bytes, payload: bytes) -> bytes:
    return (
        len(payload).to_bytes(4, "big")
        + tag
        + payload
        + (zlib.crc32(tag + payload) & 0xFFFFFFFF).to_bytes(4, "big")
    )


def read_rgba_png(path: Path) -> list[list[tuple[int, int, int, int]]]:
    data = path.read_bytes()
    if not data.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError(f"Not a PNG: {path}")

    offset = 8
    width = height = bit_depth = color_type = None
    idat = bytearray()
    while offset < len(data):
        length = int.from_bytes(data[offset : offset + 4], "big")
        tag = data[offset + 4 : offset + 8]
        payload = data[offset + 8 : offset + 8 + length]
        offset += 12 + length

        if tag == b"IHDR":
            width = int.from_bytes(payload[0:4], "big")
            height = int.from_bytes(payload[4:8], "big")
            bit_depth = payload[8]
            color_type = payload[9]
            if payload[12] != 0:
                raise ValueError(f"Interlaced PNG is not supported: {path}")
        elif tag == b"IDAT":
            idat.extend(payload)
        elif tag == b"IEND":
            break

    if width is None or height is None or bit_depth != 8 or color_type not in (2, 6):
        raise ValueError(f"Unsupported PNG format: {path}")

    channels = 4 if color_type == 6 else 3
    stride = width * channels
    raw = zlib.decompress(bytes(idat))
    rows: list[bytes] = []
    cursor = 0
    previous = bytearray(stride)

    for _ in range(height):
        filter_type = raw[cursor]
        cursor += 1
        row = bytearray(raw[cursor : cursor + stride])
        cursor += stride

        for i in range(stride):
            left = row[i - channels] if i >= channels else 0
            up = previous[i]
            up_left = previous[i - channels] if i >= channels else 0

            if filter_type == 1:
                row[i] = (row[i] + left) & 0xFF
            elif filter_type == 2:
                row[i] = (row[i] + up) & 0xFF
            elif filter_type == 3:
                row[i] = (row[i] + ((left + up) >> 1)) & 0xFF
            elif filter_type == 4:
                row[i] = (row[i] + paeth(left, up, up_left)) & 0xFF
            elif filter_type != 0:
                raise ValueError(f"Unsupported PNG filter {filter_type}: {path}")

        rows.append(bytes(row))
        previous = row

    pixels: list[list[tuple[int, int, int, int]]] = []
    for row in rows:
        out_row = []
        for x in range(width):
            base = x * channels
            r = row[base]
            g = row[base + 1]
            b = row[base + 2]
            a = row[base + 3] if channels == 4 else 255
            out_row.append((r, g, b, a))
        pixels.append(out_row)
    return pixels


def paeth(a: int, b: int, c: int) -> int:
    p = a + b - c
    pa = abs(p - a)
    pb = abs(p - b)
    pc = abs(p - c)
    if pa <= pb and pa <= pc:
        return a
    if pb <= pc:
        return b
    return c


def pixelate_to_mvp_style(tile: list[list[tuple[int, int, int, int]]]) -> list[list[tuple[int, int, int, int]]]:
    small: list[list[tuple[int, int, int, int]]] = []
    for y in range(16):
        row = []
        for x in range(16):
            row.append(tile[y * 2][x * 2])
        small.append(row)

    out: list[list[tuple[int, int, int, int]]] = []
    for row in small:
        expanded_a = []
        expanded_b = []
        for pixel in row:
            expanded_a.extend([pixel, pixel])
            expanded_b.extend([pixel, pixel])
        out.append(expanded_a)
        out.append(expanded_b)
    return out


def scale_with_ffmpeg(source: Path, destination: Path, flags: str) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    subprocess.check_call(
        [
            "ffmpeg",
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            str(source),
            "-vf",
            f"scale={TILE_PIXELS}:{TILE_PIXELS}:flags={flags},format=rgba",
            "-frames:v",
            "1",
            str(destination),
        ]
    )


def write_texture(path: Path, pixels: list[list[tuple[int, int, int, int]]]) -> None:
    ART.write_png(relative(path), pixels)
    ART.write_texture_meta(relative(path), sprite=False, max_size=TILE_PIXELS)


def write_text_meta(path: Path) -> None:
    relative_path = relative(path)
    meta_path = PROJECT_ROOT / f"{relative_path}.meta"
    meta_path.write_text(
        "fileFormatVersion: 2\n"
        f"guid: {ART.guid_for(relative_path)}\n"
        "TextScriptImporter:\n"
        "  externalObjects: {}\n"
        "  userData:\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n",
        encoding="utf-8",
        newline="\n",
    )


def write_folder_metas() -> None:
    for folder in [
        "Assets/Blockiverse/Art/Textures/Blocks/TextureSets",
        *[
            f"Assets/Blockiverse/Art/Textures/Blocks/TextureSets/{set_id}"
            for set_id in SET_IDS
        ],
        *[
            f"Assets/Blockiverse/Art/Textures/Blocks/TextureSets/{set_id}/Source"
            for set_id in SET_IDS
        ],
    ]:
        Path(PROJECT_ROOT / folder).mkdir(parents=True, exist_ok=True)
        ART.write_folder_meta(folder)


def prepare_enhanced_set(names: list[str]) -> None:
    source_dir = TEXTURE_SET_ROOT / "enhanced" / "Source"
    for name in names:
        source = CURRENT_SOURCE_DIR / f"{name}.png"
        destination = source_dir / f"{name}.png"
        if not source.exists():
            raise FileNotFoundError(f"Missing enhanced source texture: {source}")
        shutil.copyfile(source, destination)
        ART.write_texture_meta(relative(destination), sprite=False, max_size=TILE_PIXELS)


def prepare_original_set(names: list[str], lookup: dict[str, tuple]) -> None:
    source_dir = TEXTURE_SET_ROOT / "original" / "Source"
    canonical_to_legacy = {canonical: legacy for legacy, canonical in MVP_LEGACY_TO_CANONICAL.items()}

    with tempfile.TemporaryDirectory(prefix="blockiverse-original-textures-") as temp_dir:
        temp = Path(temp_dir)
        for name in names:
            destination = source_dir / f"{name}.png"
            legacy = canonical_to_legacy.get(name)
            if legacy is not None:
                extracted = temp / f"{legacy}.png"
                extract_baseline_texture(legacy, extracted)
                scale_with_ffmpeg(extracted, destination, flags="neighbor")
                ART.write_texture_meta(relative(destination), sprite=False, max_size=TILE_PIXELS)
                continue

            tile = ART.make_block_tile(lookup[name])
            write_texture(destination, pixelate_to_mvp_style(tile))


def prepare_existing_ai_set(set_id: str, comparison_folder: str, names: list[str]) -> list[str]:
    source_dir = TEXTURE_SET_ROOT / set_id / "Source"
    missing: list[str] = []
    for name in names:
        raw_generated = RAW_GENERATED_DIR / set_id / f"{name}.png"
        comparison = COMPARISON_DIR / comparison_folder / f"{name}.png"
        destination = source_dir / f"{name}.png"
        source = None
        for candidate in (raw_generated, comparison, destination):
            if candidate.exists():
                source = candidate
                break

        if source is None:
            missing.append(name)
        elif source == destination:
            ART.write_texture_meta(relative(destination), sprite=False, max_size=TILE_PIXELS)
        else:
            scale_with_ffmpeg(source, destination, flags="lanczos")
            ART.write_texture_meta(relative(destination), sprite=False, max_size=TILE_PIXELS)
    return missing


def write_missing_prompt_manifest(set_id: str, missing: list[str], lookup: dict[str, tuple]) -> None:
    manifest_path = TEXTURE_SET_ROOT / set_id / "missing-prompts.json"
    prompts = []
    for name in missing:
        block = lookup[name]
        _, _, base, accent, pattern, _ = block
        prompts.append(
            {
                "id": name,
                "baseRgb": base,
                "accentRgb": accent,
                "pattern": pattern,
                "prompt": prompt_for(name, pattern, base, accent, simplified=set_id == "ai_simplified"),
            }
        )
    manifest_path.write_text(json.dumps(prompts, indent=2) + "\n", encoding="utf-8")
    write_text_meta(manifest_path)


def prompt_for(
    name: str,
    pattern: str,
    base: tuple[int, int, int],
    accent: tuple[int, int, int],
    simplified: bool,
) -> str:
    label = name.replace("_", " ")
    style = (
        "production-quality but calm and less busy, low visual frequency, broad readable blocky shapes"
        if simplified
        else "production-quality stylized voxel material, rich detail but still readable as a repeating block texture"
    )
    avoid = (
        "no tiny noisy speckles, no dense micro-detail, no photographic realism, no blur, no text, no watermark"
        if simplified
        else "no blur, no text, no watermark, no realistic photo crop, no lighting gradient"
    )
    return (
        "Use case: stylized-concept\n"
        "Asset type: Blockiverse VR voxel block texture candidate, seamless square tile\n"
        f"Primary request: {label} block texture using a {pattern} material language.\n"
        "Style/medium: crisp stylized voxel pixel-art texture, not photorealistic.\n"
        "Composition/framing: top-down or front-facing orthographic square tile as appropriate for the material, seamless repeating pattern, no perspective.\n"
        f"Color palette: use colors near base RGB {base} with accents near RGB {accent}.\n"
        f"Materials/textures: {style}.\n"
        f"Constraints: {avoid}."
    )


def write_summary(summary: dict[str, object]) -> None:
    summary_path = TEXTURE_SET_ROOT / "texture-set-summary.json"
    summary_path.write_text(
        json.dumps(summary, indent=2) + "\n",
        encoding="utf-8",
    )
    write_text_meta(summary_path)


def main() -> None:
    names = all_block_source_names()
    lookup = block_lookup()
    if len(names) != 80:
        raise RuntimeError(f"Expected 80 block source textures, found {len(names)}.")

    write_folder_metas()
    prepare_enhanced_set(names)
    prepare_original_set(names, lookup)
    missing_ai_simplified = prepare_existing_ai_set("ai_simplified", "ai_simplified", names)
    missing_ai = prepare_existing_ai_set("ai", "ai", names)
    for set_id in SET_IDS:
        ART.write_texture_set_atlas(set_id)
    write_missing_prompt_manifest("ai_simplified", missing_ai_simplified, lookup)
    write_missing_prompt_manifest("ai", missing_ai, lookup)
    write_summary(
        {
            "textureCount": len(names),
            "sets": {
                "original": {"sourceCount": len(list((TEXTURE_SET_ROOT / "original" / "Source").glob("*.png")))},
                "enhanced": {"sourceCount": len(list((TEXTURE_SET_ROOT / "enhanced" / "Source").glob("*.png")))},
                "ai_simplified": {
                    "sourceCount": len(list((TEXTURE_SET_ROOT / "ai_simplified" / "Source").glob("*.png"))),
                    "missingCount": len(missing_ai_simplified),
                    "missingPromptManifest": "ai_simplified/missing-prompts.json",
                },
                "ai": {
                    "sourceCount": len(list((TEXTURE_SET_ROOT / "ai" / "Source").glob("*.png"))),
                    "missingCount": len(missing_ai),
                    "missingPromptManifest": "ai/missing-prompts.json",
                },
            },
        }
    )
    print(TEXTURE_SET_ROOT)


if __name__ == "__main__":
    main()
