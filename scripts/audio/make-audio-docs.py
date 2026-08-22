#!/usr/bin/env python3
"""Render `docs/audio/audio-asset-manifest.md` from the build manifest.

    python3 scripts/audio/make-audio-docs.py

Generated rather than hand-maintained so the published provenance table cannot
drift from what the build actually produces. Ruleset §7 requires every committed
cue to have a row here naming its source and license; a cue without one fails
`validate-audio-assets.py`.
"""
import json
import os
import wave

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
MANIFEST = os.path.join(HERE, "audio-manifest.json")
AUDIO_DIR = os.path.join(ROOT, "Assets/Blockiverse/Audio")
OUT = os.path.join(ROOT, "docs/audio/audio-asset-manifest.md")

# Cues the generator owns rather than the third-party build.
GENERATED_ROWS = [
    ("music_menu", "Title and pause menus", "Original — `scripts/audio/generate-audio.py`"),
    ("music_day", "Daytime music bed", "Original — `scripts/audio/generate-audio.py`"),
    ("music_night", "Nighttime music bed", "Original — `scripts/audio/generate-audio.py`"),
    ("music_cave", "Underground music bed", "Original — `scripts/audio/generate-audio.py`"),
    ("classic_block_break", "Block break, Classic Block Sounds enabled",
     "Original — `scripts/audio/generate-audio.py`"),
    ("classic_block_place", "Block place, Classic Block Sounds enabled",
     "Original — `scripts/audio/generate-audio.py`"),
]

# Human-readable grouping for the cue table.
GROUPS = [
    ("Block break and place", lambda c: c.startswith("block_")),
    ("Footsteps", lambda c: c.startswith("footstep_")),
    ("Tool contact", lambda c: c.startswith("tool_")),
    ("Interface, inventory, and crafting",
     lambda c: c.startswith(("ui_", "inventory_", "craft_")) or c in {"pickup_item"}),
    ("Multiplayer", lambda c: c.startswith("multiplayer_")),
    ("Containers", lambda c: c.startswith("container_")),
    ("Fire and light", lambda c: c in {"torch_ignite", "torch_loop", "campfire_loop", "emberflow_loop"}),
    ("Weather", lambda c: c.startswith(("rain_", "thunder_", "snow_"))),
    ("Ambience", lambda c: c.endswith("ambience_loop")),
    ("Player vitals", lambda c: c in {"player_hurt", "low_health", "player_death"}),
    ("Water and fluids", lambda c: c.startswith(("water_", "swim_", "submerged"))),
    ("Consumption and movement", lambda c: c in {"eat", "drink", "landing"}),
]


def clip_info(cue):
    path = os.path.join(AUDIO_DIR, f"{cue}.wav")
    if not os.path.exists(path):
        return "—", "—"
    with wave.open(path, "rb") as handle:
        seconds = handle.getnframes() / handle.getframerate()
        channels = "stereo" if handle.getnchannels() == 2 else "mono"
    return f"{seconds:.2f}s", channels


def main():
    with open(MANIFEST) as handle:
        manifest = json.load(handle)

    packs = manifest["packs"]
    licenses = manifest["licenses"]
    cues = {c["cue"]: c for c in manifest["cues"]}

    lines = [
        "# Audio Asset Manifest",
        "",
        "**Generated file — do not edit by hand.** Regenerate with "
        "`python3 scripts/audio/make-audio-docs.py`.",
        "",
        "Every sound in Blockiverse VR, what triggers it, where it came from, and under what "
        "license. This is the provenance record required by "
        "[`voxel_audio_vfx_ruleset.md`](../rulesets/voxel_audio_vfx_ruleset.md) §7 — a committed "
        "cue with no row here fails `scripts/audio/validate-audio-assets.py`.",
        "",
        "Music is original to the project. Sound effects are edited from licensed third-party "
        "recordings by `scripts/audio/build-audio-assets.py`, driven by "
        "`scripts/audio/audio-manifest.json`. Raw source bundles are staged outside the "
        "repository and are never committed.",
        "",
        "## Sources",
        "",
        "| Pack | License | Commercial use | Modification | Attribution |",
        "|---|---|---|---|---|",
    ]

    used_packs = {c["pack"] for c in cues.values()}
    for pack_id in sorted(used_packs):
        pack = packs[pack_id]
        terms = licenses[pack["license"]]
        lines.append(
            f"| {pack['name']} | [{terms['name']}]({terms['url']}) | "
            f"{'Yes' if terms['commercial_use'] else 'No'} | "
            f"{'Yes' if terms['modification'] else 'No'} | "
            f"{'Required' if terms['attribution_required'] else 'Not required'} |"
        )
    lines += [
        "| Blockiverse VR (original) | All Rights Reserved | — | — | — |",
        "",
        "### License restrictions",
        "",
    ]
    for key in sorted(licenses):
        terms = licenses[key]
        lines.append(f"- **{terms['name']}** — {terms['restrictions']}")
    lines += [
        "",
        "Terms were verified at each original provider rather than from search results or a "
        "mirror. Attribution is not required by any source used; this record exists for "
        "provenance.",
        "",
        "## Original project audio",
        "",
        "| Cue | Trigger | Provenance | Length | Channels |",
        "|---|---|---|---|---|",
    ]
    for cue, trigger, provenance in GENERATED_ROWS:
        length, channels = clip_info(cue)
        lines.append(f"| `{cue}` | {trigger} | {provenance} | {length} | {channels} |")

    lines += ["", "## Sound effects", ""]

    assigned = set()
    for title, predicate in GROUPS:
        group = sorted(c for c in cues if predicate(c) and c not in assigned)
        if not group:
            continue
        assigned.update(group)
        lines += [
            f"### {title}",
            "",
            "| Cue | Source pack | Source file | License | Length | Channels |",
            "|---|---|---|---|---|---|",
        ]
        for cue in group:
            entry = cues[cue]
            pack = packs[entry["pack"]]
            source_file = os.path.basename(entry["source"]).replace("__", " / ")
            terms = licenses[entry["license"]]
            length, channels = clip_info(cue)
            lines.append(
                f"| `{cue}` | {pack['name']} | `{source_file}` | "
                f"[{terms['name']}]({terms['url']}) | {length} | {channels} |"
            )
        lines.append("")

    leftover = sorted(set(cues) - assigned)
    if leftover:
        raise SystemExit(f"cues not covered by any documentation group: {leftover}")

    lines += [
        "## Rebuilding",
        "",
        "```sh",
        "# 1. Stage the source packs outside the repo (see the staging SOURCES.md)",
        "# 2. Regenerate the build manifest if the cue set changed",
        "python3 scripts/audio/make-audio-manifest.py",
        "# 3. Confirm every source resolves before writing anything",
        "python3 scripts/audio/build-audio-assets.py --check",
        "# 4. Build the cues",
        "python3 scripts/audio/build-audio-assets.py",
        "# 5. Regenerate the original music and classic block cues",
        "python3 scripts/audio/generate-audio.py",
        "# 6. Validate what landed, and refresh this document",
        "python3 scripts/audio/validate-audio-assets.py",
        "python3 scripts/audio/make-audio-docs.py",
        "```",
        "",
    ]

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", newline="\n") as handle:
        handle.write("\n".join(lines))

    print(f"wrote {OUT}")
    print(f"  {len(cues)} sound effects + {len(GENERATED_ROWS)} original clips")


if __name__ == "__main__":
    main()
