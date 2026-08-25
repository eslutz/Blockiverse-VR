#!/usr/bin/env python3
"""Build the complete curated_v1 block texture set from licensed staged sources.

Raw source packs live outside the repository in a sibling staging directory,
matching the audio asset workflow. The committed manifest decides which pilot
textures are adopted. Every non-adopted texture deliberately falls back to the
current enhanced set, so curated_v1 can be built and evaluated incrementally
without creating missing atlas tiles.

    python3 scripts/art/build-curated-textures.py
    python3 scripts/art/build-curated-textures.py --check
    python3 scripts/art/build-curated-textures.py --staging /path/to/staging

Requires ffmpeg for adopted-source transforms. Candidate/researching entries do
not affect shipping output until their manifest status is changed to `adopted`.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
import shutil
import subprocess
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
MANIFEST_PATH = HERE / "curated-texture-manifest.json"
ART_GENERATOR_PATH = HERE / "generate-art-assets.py"
ENHANCED_SOURCE_DIR = PROJECT_ROOT / "Assets/Blockiverse/Art/Textures/Blocks/TextureSets/enhanced/Source"
CURATED_ROOT = PROJECT_ROOT / "Assets/Blockiverse/Art/Textures/Blocks/TextureSets/curated_v1"
CURATED_SOURCE_DIR = CURATED_ROOT / "Source"
STATUS_PATH = CURATED_ROOT / "curated-status.json"
STAGING_DIR_NAME = "Blockiverse-VR-texture-staging"


def load_art_generator():
    spec = importlib.util.spec_from_file_location("generate_art_assets", ART_GENERATOR_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load art generator: {ART_GENERATOR_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


ART = load_art_generator()


def default_staging() -> Path:
    override = os.environ.get("BLOCKIVERSE_TEXTURE_STAGING")
    if override:
        return Path(override).expanduser().resolve()

    current = PROJECT_ROOT
    for _ in range(6):
        candidate = current.parent / STAGING_DIR_NAME
        if candidate.is_dir():
            return candidate
        if current.parent == current:
            break
        current = current.parent
    return (PROJECT_ROOT.parent / STAGING_DIR_NAME).resolve()


def relative(path: Path) -> str:
    return path.relative_to(PROJECT_ROOT).as_posix()


def load_manifest() -> dict:
    with MANIFEST_PATH.open(encoding="utf-8") as handle:
        return json.load(handle)


def all_source_names() -> list[str]:
    names = [block[0] for block in ART.BLOCKS]
    names.extend(alias[0] for alias in ART.BLOCK_SOURCE_ALIASES)
    return names


def adopted_entries(manifest: dict) -> dict[str, dict]:
    return {entry["id"]: entry for entry in manifest["pilot"] if entry["status"] == "adopted"}


def resolve_source(manifest: dict, entry: dict, staging: Path) -> Path:
    pack = manifest["packs"][entry["pack"]]
    return staging / pack["stagingPath"] / entry["sourceFile"]


def run_ffmpeg(source: Path, destination: Path, transform: dict) -> None:
    size = int(transform["size"])
    scale = transform["scale"]
    if transform["crop"] == "center-square":
        video_filter = f"crop='min(iw,ih)':'min(iw,ih)',scale={size}:{size}:flags={scale},format=rgba"
    elif transform["crop"] == "none":
        video_filter = f"scale={size}:{size}:flags={scale},format=rgba"
    else:
        raise ValueError(f"Unsupported crop mode: {transform['crop']}")

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
            video_filter,
            "-frames:v",
            "1",
            str(destination),
        ]
    )


def write_json_with_meta(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    meta_path = Path(f"{path}.meta")
    meta_path.write_text(
        "fileFormatVersion: 2\n"
        f"guid: {ART.guid_for(relative(path))}\n"
        "TextScriptImporter:\n"
        "  externalObjects: {}\n"
        "  userData:\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n",
        encoding="utf-8",
        newline="\n",
    )


def verify_sources(manifest: dict, staging: Path) -> list[tuple[str, Path]]:
    missing: list[tuple[str, Path]] = []
    for texture_id, entry in adopted_entries(manifest).items():
        source = resolve_source(manifest, entry, staging)
        if not source.is_file():
            missing.append((texture_id, source))
    return missing


def build(manifest: dict, staging: Path) -> None:
    adopted = adopted_entries(manifest)
    names = all_source_names()
    if len(names) != 100:
        raise RuntimeError(f"Expected 100 block source textures, found {len(names)}.")

    CURATED_SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    ART.write_folder_meta(relative(CURATED_ROOT))
    ART.write_folder_meta(relative(CURATED_SOURCE_DIR))

    for name in names:
        destination = CURATED_SOURCE_DIR / f"{name}.png"
        entry = adopted.get(name)
        if entry is None:
            source = ENHANCED_SOURCE_DIR / f"{name}.png"
            if not source.is_file():
                raise FileNotFoundError(f"Missing enhanced fallback texture: {source}")
            shutil.copyfile(source, destination)
        else:
            if entry["strategy"] != "direct":
                raise RuntimeError(
                    f"{name}: adopted strategy '{entry['strategy']}' is not buildable yet; "
                    "add deterministic composite support before adoption."
                )
            source = resolve_source(manifest, entry, staging)
            if not source.is_file():
                raise FileNotFoundError(f"Missing adopted source for {name}: {source}")
            run_ffmpeg(source, destination, entry["transform"])

        ART.write_texture_meta(relative(destination), sprite=False, max_size=manifest["tilePixels"])

    ART.write_texture_set_atlas(manifest["setId"])

    adopted_ids = sorted(adopted)
    status = {
        "setId": manifest["setId"],
        "sourceCount": len(names),
        "adoptedCount": len(adopted_ids),
        "fallbackCount": len(names) - len(adopted_ids),
        "adopted": adopted_ids,
        "fallbackPolicy": "All non-adopted source tiles copy from enhanced; enhanced remains the project default.",
    }
    write_json_with_meta(STATUS_PATH, status)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--staging", type=Path, default=default_staging(), help="Directory holding extracted source packs.")
    parser.add_argument("--check", action="store_true", help="Verify adopted source files resolve, then exit.")
    args = parser.parse_args()

    manifest = load_manifest()
    staging = args.staging.expanduser().resolve()
    missing = verify_sources(manifest, staging)
    if missing:
        for texture_id, path in missing:
            print(f"missing {texture_id}: {path}")
        raise SystemExit(1)

    if args.check:
        print(f"curated_v1: {len(adopted_entries(manifest))} adopted source(s) resolve under {staging}")
        return

    if adopted_entries(manifest) and not shutil.which("ffmpeg"):
        raise SystemExit("ffmpeg is required to build adopted curated textures")

    build(manifest, staging)
    print(CURATED_ROOT)


if __name__ == "__main__":
    main()
