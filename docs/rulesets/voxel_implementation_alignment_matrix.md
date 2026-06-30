# Voxel Implementation Alignment Matrix

**Document status:** Migration and refactor aid
**Project:** Blockiverse VR
**Purpose:** Map temporary repository terms to canonical ruleset IDs while the codebase is migrated to the ruleset-defined Blockiverse world.

This document is not a second gameplay vocabulary. New UI, saves, registries, and gameplay data should use canonical IDs. Legacy names are listed only so old test fixtures, saved worlds, prefabs, or code paths can be migrated deliberately.

---

## 1. Canonical rule

```txt
canonical ruleset data wins
legacy names are migration inputs only
new saves must write canonical IDs
runtime registries should reject unknown legacy IDs unless a migration step is active
```

---

## 2. World preset mapping

| Legacy / Temporary Preset | Canonical Preset | Migration Rule |
|---|---|---|
| Flat creative validation world | `flat_builder` | Regenerate using canonical flat-builder rules or migrate blocks through the block mapping table. |
| Generated survival-lite validation world | `survival_terrain` | Regenerate using canonical terrain/biome/resource/vegetation/structure rules. |
| Empty test scene | `void_builder` | Create canonical starting platform and save preset metadata. |

Canonical presets:

```txt
survival_terrain
flat_builder
void_builder
island_builder
cave_builder
sky_shelf
```

---

## 3. Block mapping

| Legacy / Temporary Block | Canonical Block ID | Notes |
|---|---|---|
| `Air` | `air` | Direct match. |
| `Meadow Turf` | `meadow_turf` | Direct match. |
| `Loam` | `loose_loam` | Direct soil migration. |
| `Slate` | `graystone` or `dark_slate` | Use `graystone` for generic stone; use `dark_slate` for deeper stone. |
| `Timber` | `branchwood_log` or `work_plank` | Natural trunk becomes `branchwood_log`; processed material becomes `work_plank`. |
| `Leafmass` | `leafmoss` | Preserve natural/persistent leaf state if available. |
| `Clearstone` | `lumen_quartz_cluster` or `clearpane_glass` | Natural crystal becomes `lumen_quartz_cluster`; crafted transparent block becomes `clearpane_glass`. |
| `Coalstone` | `embercoal_seam` | Natural resource node. |
| `Copperstone` | `rosycopper_bloom` | Natural resource node. |
| `Ironstone` | `rustcore_ore` | Natural resource node. |
| `Workbench` | `build_table` | Crafting station. |
| `Torchbud` | `glowwick` | Early light source. |
| `Storage Crate` | `storage_crate` | Container. |

---

## 4. Item/tool mapping

| Legacy / Temporary Item | Canonical Item or Tool | Notes |
|---|---|---|
| `Chipper` | Feller/Carver family | Use Feller behavior for wood and leaves; Carver behavior for cutting/resin workflows. |
| `Pick` | Delver family | Use Delver for stone, ores, and crystals. |
| `Mallet` | Mallet | Direct conceptual match. |
| `Recovery Wrap` | `field_bandage` | Healing/utility item. |
| `Coalstone` item | `embercoal` or `embercoal_seam` drop result | Convert held resources to `embercoal`; placed block nodes to `embercoal_seam`. |
| `Copperstone` item | `raw_rosycopper` or `rosycopper_bloom` drop result | Convert held resources to raw material; placed block nodes to resource node. |
| `Ironstone` item | `raw_rustcore` or `rustcore_ore` drop result | Convert held resources to raw material; placed block nodes to resource node. |

---

## 5. Behavior mapping

| Legacy Behavior | Canonical Replacement |
|---|---|
| Direct local block set in single-player | `BlockMutationRequest` validated through the same command path used by host authority. |
| Client directly mutates local chunk state | Client sends request; host validates and sends ordered delta. |
| Flat-only builder world | `flat_builder` preset with canonical block layers and world metadata. |
| Simplified harvest work units | Translate to canonical hardness/tool/tier values or implement as a UI progress adapter over canonical mine time. |
| Simplified resource blocks | Replace with canonical resource nodes and drop tables. |
| Single shared inventory assumptions | Use canonical player inventory, container inventory, and multiplayer ownership schemas. |

---

## 6. Migration checklist

```txt
Create canonical block/item/tool registries.
Add explicit legacy ID migration table in save migration layer.
Regenerate or migrate existing test worlds to canonical presets.
Update creative hotbar/catalog to use canonical IDs.
Update world generation to emit canonical terrain, resources, vegetation, and structures.
Update networking snapshots to include canonical world metadata and registry versions.
Update save/load validation to reject incompatible unmigrated worlds.
Update UI labels and mockups to use canonical names only.
Tag known-good state before and after migration using kg/... tags.
```

---

## 7. Assembly alignment (ratified)

The implementation's assembly layout is aligned and ratified as follows (see
`docs/roadmap/blockiverse_vr_execution_plan.md` → "Ratified architecture decisions"):

| Concern | Canonical assembly | Notes |
|---|---|---|
| World generation (terrain, resources, vegetation, structures, weather) | `Blockiverse.WorldGen` | Consolidated — no separate Environment/Structures/Vegetation assemblies. |
| Gameplay systems (creative tools, vitals runtime, feedback/settings) | `Blockiverse.Gameplay` | Consolidated — no separate AudioVfx assembly. |
| Multiplayer netcode (survival sync, chunk authority, world persistence sync) | `Blockiverse.Networking` | **Extracted out of `Blockiverse.Gameplay` (A1).** Acyclic: depends on Core/Voxel/WorldGen/Survival/Persistence only. |
| Cross-cutting seams for UI decoupling | `Blockiverse.Core` | Dependency-free; hosts interface/event seams (A2). |

Networking snapshots and world metadata (matrix rows above) are emitted from
`Blockiverse.Networking` after the A1 extraction; no behavior changed in the move.
