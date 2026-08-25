#!/usr/bin/env python3
"""Fetch the CC0 source packs used by the curated texture pilot.

Downloads go to the external `Blockiverse-VR-texture-staging` directory, never
into the repository. ZIPs are retained under `raw/`, extracted under each pack's
manifest staging path, and any concretely referenced source files are normalized
to the root of that staging path for deterministic builds.

    python3 scripts/art/fetch-curated-texture-sources.py
    python3 scripts/art/fetch-curated-texture-sources.py cethiel_grass_1 ancient_civ_plants
    python3 scripts/art/fetch-curated-texture-sources.py --force

The script writes SOURCES.json in staging with SHA-256 hashes. Before a candidate
is promoted to `adopted`, copy its pack hash into the committed manifest so the
exact upstream bytes are pinned rather than merely trusting a mutable URL.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import tempfile
import urllib.request
import zipfile
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
MANIFEST_PATH = HERE / "curated-texture-manifest.json"
STAGING_DIR_NAME = "Blockiverse-VR-texture-staging"
USER_AGENT = "Blockiverse-VR curated texture source fetcher/1.0"


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


def load_manifest() -> dict:
    with MANIFEST_PATH.open(encoding="utf-8") as handle:
        return json.load(handle)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def download(url: str, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=120) as response, destination.open("wb") as output:
        shutil.copyfileobj(response, output)


def safe_extract(archive: Path, destination: Path) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    destination_root = destination.resolve()
    with zipfile.ZipFile(archive) as handle:
        for member in handle.infolist():
            target = (destination / member.filename).resolve()
            if target != destination_root and destination_root not in target.parents:
                raise RuntimeError(f"Unsafe ZIP member path in {archive.name}: {member.filename}")
        handle.extractall(destination)


def referenced_files(manifest: dict, pack_id: str) -> set[str]:
    return {
        entry["sourceFile"]
        for entry in manifest["pilot"]
        if entry.get("pack") == pack_id and entry.get("sourceFile")
    }


def normalize_referenced_files(pack_root: Path, filenames: set[str]) -> None:
    extracted_root = pack_root / "_pack"
    for filename in sorted(filenames):
        exact = pack_root / filename
        if exact.is_file():
            continue

        matches = [
            path
            for path in extracted_root.rglob("*")
            if path.is_file() and path.name.casefold() == filename.casefold()
        ]
        if not matches:
            raise FileNotFoundError(f"Downloaded pack does not contain referenced source file: {filename}")
        if len(matches) > 1:
            candidates = ", ".join(str(path.relative_to(extracted_root)) for path in matches)
            raise RuntimeError(f"Ambiguous source filename {filename}: {candidates}")
        shutil.copyfile(matches[0], exact)


def fetch_pack(manifest: dict, pack_id: str, staging: Path, force: bool) -> dict:
    pack = manifest["packs"][pack_id]
    url = pack.get("downloadUrl")
    if not url:
        raise RuntimeError(f"{pack_id}: no downloadUrl in manifest")

    raw_path = staging / "raw" / f"{pack_id}.zip"
    pack_root = staging / pack["stagingPath"]
    extracted_root = pack_root / "_pack"

    if force or not raw_path.is_file():
        with tempfile.NamedTemporaryFile(prefix=f"{pack_id}-", suffix=".zip", delete=False) as temp:
            temp_path = Path(temp.name)
        try:
            print(f"download {pack_id}: {url}")
            download(url, temp_path)
            if not zipfile.is_zipfile(temp_path):
                raise RuntimeError(f"{pack_id}: downloaded file is not a valid ZIP")
            raw_path.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(str(temp_path), raw_path)
        finally:
            temp_path.unlink(missing_ok=True)
    else:
        print(f"reuse {pack_id}: {raw_path}")

    if force and extracted_root.exists():
        shutil.rmtree(extracted_root)
    if not extracted_root.exists():
        safe_extract(raw_path, extracted_root)

    normalize_referenced_files(pack_root, referenced_files(manifest, pack_id))

    return {
        "name": pack["name"],
        "author": pack["author"],
        "license": pack["license"],
        "sourcePage": pack["sourcePage"],
        "downloadUrl": url,
        "rawFile": str(raw_path.relative_to(staging)),
        "sha256": sha256(raw_path),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("packs", nargs="*", help="Pack IDs to fetch (default: all manifest packs).")
    parser.add_argument("--staging", type=Path, default=default_staging(), help="External source staging directory.")
    parser.add_argument("--force", action="store_true", help="Redownload and re-extract selected packs.")
    args = parser.parse_args()

    manifest = load_manifest()
    known = set(manifest["packs"])
    selected = args.packs or list(manifest["packs"])
    unknown = sorted(set(selected) - known)
    if unknown:
        raise SystemExit(f"Unknown pack ID(s): {', '.join(unknown)}")

    staging = args.staging.expanduser().resolve()
    staging.mkdir(parents=True, exist_ok=True)

    source_record_path = staging / "SOURCES.json"
    if source_record_path.is_file():
        with source_record_path.open(encoding="utf-8") as handle:
            records = json.load(handle)
    else:
        records = {"packs": {}}

    for pack_id in selected:
        records.setdefault("packs", {})[pack_id] = fetch_pack(manifest, pack_id, staging, args.force)

    source_record_path.write_text(json.dumps(records, indent=2) + "\n", encoding="utf-8")
    print(source_record_path)
    for pack_id in selected:
        print(f"{pack_id} sha256 {records['packs'][pack_id]['sha256']}")


if __name__ == "__main__":
    main()
