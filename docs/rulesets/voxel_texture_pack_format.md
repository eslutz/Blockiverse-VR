# Blockiverse Texture Pack Format

**Status:** normative. Version 1.

This document defines the format for user-supplied texture packs: the directory layout, the manifest schema, the canonical texture names, and what happens when a pack is partial, absent, or broken. It is the document a pack author reads.

Related: [ADR 0012](../adr/0012-user-supplied-texture-packs.md) for why the format is shaped this way, and [voxel_save_versioning_schema.md §5.1](voxel_save_versioning_schema.md) for how a selection is stored in a world save.

---

## 1. Scope and non-goals

A texture pack replaces **block textures**. It does not change models, sounds, UI, item icons, shaders, or game rules.

**This format is Blockiverse's own.** It is not Minecraft's, it does not read Minecraft resource packs, and no Minecraft asset, namespace, or file layout appears anywhere in the game. Packs are keyed on Blockiverse's canonical texture names.

Nothing prevents a third party from writing a converter that reads some other game's pack layout and emits a Blockiverse pack. That conversion happens on the author's own machine, against art they hold the rights to, and produces a normal pack. It is not part of the game.

---

## 2. Directory layout

```
<persistentDataPath>/TexturePacks/
  <pack_id>/
    blockiverse-pack.json      required
    blocks/
      meadow_turf.png          0..N, named for canonical textures
      graystone.png
      meadow_turf_side.png
      ...
    pack_icon.png              optional, reserved
```

On Quest this resolves to `/sdcard/Android/data/<package>/files/TexturePacks`, reachable over USB MTP and `hzdb push` with no root. The directory is created on first scan, so it exists to drop a pack into before you own one.

**The folder name must equal `packId`** (compared case-insensitively). That lets the game answer "is this pack installed?" with a directory probe rather than parsing every manifest, and stops two folders claiming one id.

### 2.1 Pack ids

`^[a-z0-9_]{1,48}$`, matched case-insensitively and stored lowercase.

`.`, `/` and `\` are excluded deliberately: a pack id becomes a directory name and is interpolated into log lines, so restricting the character set means a pack id can never be turned into a path and never needs escaping to be logged.

---

## 3. `blockiverse-pack.json`

```json
{
  "formatVersion": 1,
  "packId": "mossy_stones",
  "displayName": "Mossy Stones",
  "author": "Some Person",
  "packVersion": "1.0.0",
  "description": "Damp, overgrown stonework.",
  "license": "CC-BY-4.0",
  "attribution": "Textures by Some Person, CC BY 4.0",
  "tilePixels": 32,
  "baseTextureSet": "enhanced",
  "minGameVersion": "0.4.0"
}
```

| Field | Type | Required | Rule |
|---|---|---|---|
| `formatVersion` | int | **yes** | Must be `1`. A missing field parses as `0` and is rejected as missing. |
| `packId` | string | **yes** | §2.1, and must equal the folder name. |
| `displayName` | string | **yes** | 1–48 chars after trimming. Rendered verbatim. |
| `author` | string | no | ≤48. |
| `packVersion` | string | no | ≤16, free-form. Changing it invalidates the composited-atlas cache. |
| `description` | string | no | ≤240. |
| `license` | string | no | ≤64. SPDX id or free text. |
| `attribution` | string | no | ≤240. **Shown in the pack list.** See §7. |
| `tilePixels` | int | **yes** | `16`, `32`, or `64`. See §5. |
| `baseTextureSet` | string | no | One of `original`, `enhanced`, `ai_simplified`, `ai`. Default `enhanced`. An unknown value coerces with a warning rather than failing the pack. |
| `minGameVersion` | string | no | Advisory only — a newer requirement **warns and loads anyway**. A cosmetic pack must never lock a player out of their own world. |

Parsing uses Unity's `JsonUtility`, which **ignores unknown keys** (so the format is forward-compatible) and **silently defaults missing ones** (so every requirement above is an explicit check, not an assumption).

Strings are trimmed, stripped of control characters, and truncated. They are rendered verbatim and are **never** looked up as localization keys, so a pack named `ui.status.crate.shared` stays that literal text.

---

## 4. Textures

`blocks/<canonical_name>.png` — PNG, RGBA, exactly `tilePixels` square.

- **Packs may be partial.** Any texture not supplied falls through to `baseTextureSet`. Ship one file or all of them.
- A non-square or wrongly sized tile is **skipped with a warning**; the rest of the pack still loads.
- An unrecognised filename is ignored with one aggregated warning, so a pack may ship art for textures a future version adds.
- The three flow variants (`freshwater_flow`, `brine_flow`, `emberflow_flow`) are **recognised but unused** — a flowing cell renders with its family's source tile. Supplying one is not an error; it simply has no effect, and you are told so specifically rather than being told the filename is unknown.

### 4.1 Canonical names

The authoritative list is the `BLOCKS` table in [`scripts/art/generate-art-assets.py`](../../scripts/art/generate-art-assets.py), mirrored in C# by `BlockAtlasTileNames`. The two are pinned to each other by `BlockAtlasTileNameTableEditModeTests`, so they cannot drift.

97 textures. Beyond the obvious per-block names, six are **per-face**:

| Name | Used for |
|---|---|
| `meadow_turf_side`, `dry_turf_side`, `snowcap_turf_side`, `rootsoil_side` | The four vertical faces of turf blocks (the grass-over-dirt fringe) |
| `branchwood_log_end`, `smooth_branchwood_end` | Log end grain, on both cut ends |

A turf block's own name is its **top** face; its bottom reuses `loose_loam`. So restyling grass properly means supplying `meadow_turf`, `meadow_turf_side`, and `loose_loam`. Supplying only some of them is allowed and produces a deliberate mixed look.

---

## 5. Resolution and cost

`tilePixels` sets how large the composited atlas becomes. The atlas grid is 16×10 tiles at 32 px with 8 px of padding per side, so:

| `tilePixels` | Atlas scale | Composited atlas | Uncompressed + mips |
|---|---|---|---|
| 16 or 32 | 1 | 768×480 | 1.41 MiB |
| 64 | 2 | 1536×960 | 5.63 MiB |

A 16 px pack is **upscaled** into the shipped grid rather than shrinking it, so built-in textures the pack does not override keep their full detail.

**128 px and above is rejected.** At scale 4 the transient cost of building the atlas is roughly 54 MiB while a world is already resident on a Quest, for a resolution the game's own 32 px art direction never targets. `MaxAtlasScale` is declared at 4 so raising this later is a validation change rather than a UV change.

Only integer upscales are performed. A tile size that does not divide evenly into the target is skipped rather than resampled — fractional resampling destroys pixel art.

---

## 6. Failure behaviour

| Situation | Result |
|---|---|
| Pack installed and valid | Used |
| Pack **not installed** | Falls back to the default set, tells the player, and **keeps the selection in the save** |
| Pack directory present but manifest missing/unparseable/invalid | Falls back, reports it as *invalid* rather than *missing* |
| Individual tile missing | That texture comes from `baseTextureSet` |
| Individual tile malformed | That tile is skipped; the pack still loads |

The missing/invalid distinction is not pedantry: the player's next action differs. A missing pack needs reinstalling; an invalid one needs repairing, and the message names the field that is wrong.

**A world save keeps the pack you chose even while that pack is uninstalled.** Reinstall it and the world uses it again. This is why the save stores the *requested* selection rather than what was actually drawn — see [voxel_save_versioning_schema.md §5.1](voxel_save_versioning_schema.md).

---

## 7. Licensing and attribution

**You are responsible for holding the rights to everything in a pack you install or distribute.** Blockiverse reads packs from your device; it does not host, bundle, or transmit them.

The `license` and `attribution` fields are shown in the pack list. Fill them in for anything you did not draw yourself — most art you find is all-rights-reserved by default, and a permissive licence is usually conditional on visible credit.

---

## 8. Multiplayer

**Packs are never shared between players.** The selection is local to each device and is not in the connection-approval payload, the world-snapshot header, or any RPC, and no pack file is ever transmitted.

In a multiplayer session each player sees their own textures. A host using a pack their friend does not have is not an error and not a desync — nothing about which texture a peer draws can affect world state.

This is both the correct engineering answer (a client cannot use a token for a pack it lacks) and the right one for the art: rendering a file already on your own device is not redistribution, and transmitting it would be.

---

## 9. Quick start

```bash
mkdir -p /sdcard/Android/data/<package>/files/TexturePacks/my_pack/blocks
```

Write `blockiverse-pack.json`:

```json
{ "formatVersion": 1, "packId": "my_pack", "displayName": "My Pack", "tilePixels": 32 }
```

Drop a 32×32 `blocks/meadow_turf.png` beside it, then pick the pack in **Settings → Textures**. Use **Refresh** to pick up a pack added while the game is running — no restart needed.
