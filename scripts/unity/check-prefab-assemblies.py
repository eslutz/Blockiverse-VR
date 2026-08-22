#!/usr/bin/env python3
# Purpose: given a scene, find every component that will be in that scene when built for a platform
# whose script lives in an assembly EXCLUDED from that platform.
#
# Why this exists, and why no test can replace it:
#
#   Unity resolves a MonoBehaviour by the GUID in its .meta file, not by assembly. So a prefab can
#   happily reference a script whose asmdef excludes the platform you are about to build. The editor
#   is fine, every test passes, the diff looks right -- and the shipped player logs "The referenced
#   script on this Behaviour is missing!" on every start, for a component that cannot exist there.
#
#   You cannot test a build configuration you have not built, and by the time you have built it the
#   symptom is a log line rather than a failure. This checks the artefact instead of the intent.
#
#   Found exactly this: the shared network manager prefab carries SurvivalVitalsRuntime from
#   Blockiverse.Gameplay, which the dedicated server build excludes.
#
# What it understands, because a naive scan gets all three of these wrong:
#
#   1. A scene does not repeat an instantiated prefab's components -- it references the prefab. A
#      scene-only text scan therefore finds NOTHING on exactly the case this tool exists for.
#   2. A scene can strip an inherited component via a removed-component override, which shows up as
#      a fileID in m_RemovedComponents, NOT as a deleted line. Without parsing those, the tool stays
#      red forever after a correct fix -- and a gate that always fails is a gate people stop reading.
#   3. Prefabs nest, so instance-following has to recurse.
#
# --asset is required ON PURPOSE. A prefab is not "in" a build; a scene is, and prefabs arrive
# through it. Scanning every prefab in the project against a platform would flag every client prefab
# as a server problem and be pure noise.
#
# Usage:
#   scripts/unity/check-prefab-assemblies.py --platform LinuxStandalone64 \
#       --asset Assets/Blockiverse/Scenes/Server.unity
#
# Exit codes: 0 nothing excluded, 1 an excluded component was found, 2 bad usage.

import argparse
import json
import re
import sys
from pathlib import Path

META_GUID = re.compile(r"^guid:\s*([0-9a-f]{32})", re.MULTILINE)
DOC_HEADER = re.compile(r"^--- !u!(\d+) &(\d+)")
SCRIPT_GUID = re.compile(r"m_Script:\s*\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-f]{32})")
SOURCE_PREFAB = re.compile(r"m_SourcePrefab:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})")
REMOVED_BLOCK = re.compile(r"m_RemovedComponents:\s*\n((?:\s*-\s*\{[^}]*\}\s*\n)*)")
ENTRY_FILEID = re.compile(r"fileID:\s*(-?\d+)")

MONO_BEHAVIOUR = "114"
PREFAB_INSTANCE = "1001"

MAX_NEST_DEPTH = 8  # cycles are impossible in valid prefab data; this is a corrupt-file backstop


def build_index(assets: Path, suffix: str):
    """GUID -> asset path, for every asset of one kind."""
    index = {}
    for meta in assets.rglob(f"*{suffix}.meta"):
        try:
            match = META_GUID.search(meta.read_text(errors="replace"))
        except OSError:
            continue
        if match:
            index[match.group(1)] = meta.with_suffix("")  # strip .meta
    return index


def split_documents(text):
    """Unity YAML is a multi-document stream. Yields (classId, fileId, body)."""
    header = None
    body = []
    for line in text.splitlines():
        if line.startswith("--- "):
            if header is not None:
                yield header[0], header[1], "\n".join(body)
            match = DOC_HEADER.match(line)
            header = match.groups() if match else ("?", "?")
            body = []
        elif header is not None:
            body.append(line)
    if header is not None:
        yield header[0], header[1], "\n".join(body)


def prefab_components(path: Path, prefab_index, depth=0):
    """Script GUIDs a prefab contributes, following nested instances and honouring removals."""
    if depth > MAX_NEST_DEPTH:
        return set()
    try:
        text = path.read_text(errors="replace")
    except OSError:
        return set()
    return collect(text, prefab_index, depth)


def collect(text, prefab_index, depth=0):
    """Script GUIDs present in this asset once prefab instances are expanded and removals applied."""
    guids = set()

    for class_id, file_id, body in split_documents(text):
        if class_id == MONO_BEHAVIOUR:
            match = SCRIPT_GUID.search(body)
            if match:
                guids.add(match.group(1))
            continue

        if class_id != PREFAB_INSTANCE:
            continue

        source = SOURCE_PREFAB.search(body)
        if not source:
            continue
        prefab = prefab_index.get(source.group(1))
        if prefab is None:
            continue

        # Which of the source prefab's components does this instance strip?
        removed_ids = set()
        removed = REMOVED_BLOCK.search(body)
        if removed:
            for entry in removed.group(1).splitlines():
                file_ref = ENTRY_FILEID.search(entry)
                if file_ref:
                    removed_ids.add(file_ref.group(1))

        try:
            prefab_text = prefab.read_text(errors="replace")
        except OSError:
            continue

        # Map the prefab's own component fileIDs so a removal can be resolved to a script.
        for class_id2, file_id2, body2 in split_documents(prefab_text):
            if class_id2 == MONO_BEHAVIOUR and file_id2 in removed_ids:
                continue
            if class_id2 == MONO_BEHAVIOUR:
                match = SCRIPT_GUID.search(body2)
                if match:
                    guids.add(match.group(1))

        # Nested prefab instances inside the source prefab.
        for class_id2, _, body2 in split_documents(prefab_text):
            if class_id2 == PREFAB_INSTANCE:
                nested = SOURCE_PREFAB.search(body2)
                if nested and nested.group(1) in prefab_index:
                    guids |= prefab_components(prefab_index[nested.group(1)], prefab_index, depth + 1)

    return guids


def owning_asmdef(script_path: Path, assets: Path):
    """The nearest asmdef at or above a script's directory, as Unity resolves it."""
    directory = script_path.parent
    while True:
        candidates = sorted(directory.glob("*.asmdef"))
        if candidates:
            return candidates[0]
        if directory == assets or directory.parent == directory:
            return None
        directory = directory.parent


def asmdef_field(asmdef: Path, field: str):
    try:
        return json.loads(asmdef.read_text()).get(field) or []
    except (OSError, json.JSONDecodeError):
        return []


def main() -> int:
    parser = argparse.ArgumentParser(add_help=True)
    parser.add_argument("--platform", required=True,
                        help="asmdef platform name, e.g. LinuxStandalone64")
    parser.add_argument("--assets", default="Assets")
    parser.add_argument("--asset", action="append", required=True,
                        help="scene (or prefab) to check, repeatable; see the header note")
    args = parser.parse_args()

    assets = Path(args.assets)
    if not assets.is_dir():
        print(f"assets directory not found: {assets}", file=sys.stderr)
        return 2

    script_index = build_index(assets, ".cs")
    prefab_index = build_index(assets, ".prefab")
    asmdef_cache = {}

    problems = []
    scanned = 0

    for target in [Path(a) for a in args.asset]:
        if not target.exists():
            print(f"asset not found: {target}", file=sys.stderr)
            return 2
        try:
            text = target.read_text(errors="replace")
        except OSError as error:
            print(f"cannot read {target}: {error}", file=sys.stderr)
            return 2

        scanned += 1

        for guid in sorted(collect(text, prefab_index)):
            script = script_index.get(guid)
            if script is None:
                continue  # package script, or a GUID this project does not own

            if script not in asmdef_cache:
                asmdef_cache[script] = owning_asmdef(script, assets)
            asmdef = asmdef_cache[script]
            if asmdef is None:
                continue  # Assembly-CSharp; ships everywhere

            if args.platform in asmdef_field(asmdef, "excludePlatforms"):
                problems.append((target, script, asmdef, "excludePlatforms"))
            else:
                include = asmdef_field(asmdef, "includePlatforms")
                if include and args.platform not in include:
                    problems.append((target, script, asmdef, "includePlatforms"))

    print(f"scanned {scanned} asset(s) for platform {args.platform}")

    if not problems:
        print("No components reference an assembly excluded from this platform.")
        return 0

    print(f"\n{len(problems)} component(s) reference an assembly not built for {args.platform}:")
    for target, script, asmdef, reason in problems:
        print(f"  {target}")
        print(f"    {script.name}  ->  {asmdef.stem}  ({reason})")
    print("\nEach becomes a missing-script error at runtime in that build.")
    print("Fix by stripping the component in the scene generator, or by moving the script to an")
    print("assembly the platform builds.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
