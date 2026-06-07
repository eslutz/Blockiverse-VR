# Voxel Structure Generation Ruleset

Version: 1.0
Companion documents: `voxel_survival_ruleset.md`, `voxel_creative_ruleset.md`, `voxel_survival_menus.md`, `voxel_world_environment_effects.md`, `voxel_biome_vegetation_ruleset.md`, `voxel_save_versioning_schema.md`, `voxel_multiplayer_networking_ruleset.md`, and `voxel_audio_vfx_ruleset.md`

This ruleset defines deterministic generation for ruins, camps, shelters, landmarks, underground rooms, cave features, and loot-bearing structures. It is designed to plug into the existing terrain, cave, resource, vegetation, weather, Creative, inventory, and save systems.

This document is the canonical source for structure placement, structure templates, structure loot generation, and structure persistence rules. Player-facing screens for world creation, Creative world editing, and structure-related settings remain in `voxel_survival_menus.md`.

---

## 1. Design goals

Structures should:

1. Make exploration more interesting without overwhelming natural terrain.
2. Use the custom block, item, biome, and resource names from the Survival ruleset.
3. Generate deterministically from world seed, chunk coordinates, and structure definition version.
4. Be easy to represent as templates or procedural builders.
5. Avoid clipping into cliffs, caves, fluids, vegetation, and other structures unless explicitly allowed.
6. Support simple loot, lighting, campfire, container, and station placement.
7. Save only dynamic state after generation: opened containers, modified blocks, active stations, and player changes.

---

## 2. Shared constants and IDs

Use the shared constants from the Survival and Environment rulesets.

```ts
CHUNK_SIZE = 16;
WORLD_MIN_Y = 0;
WORLD_MAX_Y = 255;
SEA_LEVEL = 96;
TICKS_PER_SECOND = 20;
TICKS_PER_DAY = 24000;
```

Canonical biome IDs:

```txt
meadow
pinewild
wetland
drybrush
dunes
tundra
highlands
```

Canonical game mode IDs used in save data and generation config:

```txt
survival
creative
```

Display names may remain capitalized in menus, but serialized IDs should stay lowercase.

---

## 3. Structure generation pipeline

Structures are generated after terrain shape, caves, fluids, and resource nodes exist, but before the final vegetation decoration pass. This allows structures to adapt to terrain and allows vegetation to avoid roads, foundations, entrances, and cleared courtyards.

Recommended world-generation order:

```txt
1. Heightmap and biome pass
2. Terrain layering pass
3. Cave carving pass
4. Fluid fill pass
5. Resource placement pass
6. Structure candidate selection pass
7. Structure terrain-fit and reservation pass
8. Structure block placement pass
9. Structure interior/loot/station marker pass
10. Vegetation and surface decoration pass
11. Initial lighting pass
12. Initial environment overlay pass, such as snow in cold weather presets
```

Structure placement should be deterministic and chunk-safe. A chunk can ask for structures whose bounding boxes overlap it, even when the anchor lies in a neighboring chunk.

```ts
function generateChunk(chunkX, chunkZ, worldSeed) {
  terrainPass(chunkX, chunkZ);
  cavePass(chunkX, chunkZ);
  fluidPass(chunkX, chunkZ);
  resourcePass(chunkX, chunkZ);

  const overlappingStructures = resolveStructuresForChunk(chunkX, chunkZ, worldSeed);
  applyStructureBlocks(chunkX, chunkZ, overlappingStructures);

  vegetationPass(chunkX, chunkZ, overlappingStructures.structureMasks);
  lightingPass(chunkX, chunkZ);
}
```

Resource-node preservation rule:

```ts
if targetBlock has tag "resource_node"
   and structure.resourceConflictPolicy != "allow_for_resource_site":
       reject candidate or move the structure anchor
```

Default policy is `reject`. A resource-site structure such as `drybrush_niter_pit`, `lumen_hollow`, or `ember_vent_outpost` may use `allow_for_resource_site`, but it should replace no more than `maxResourceNodesReplaced` nodes and should only replace resources related to the structure theme.

---

## 4. Structure categories

| Category ID | Purpose | Examples | Saves Dynamic State? |
|---|---|---|---|
| `surface_micro` | Tiny landmarks and loose sites | Pathmark Stones, Forager Cache | Usually no |
| `surface_minor` | Small shelters and work sites | Forager Lean-To, Resin Tap Grove | Yes if loot or stations exist |
| `surface_major` | Larger ruins or landmarks | Weathered Watchpost, Ruined Kiln Yard | Yes |
| `settlement_cluster` | Multiple small pieces placed together | Mossroot Hamlet, Wetland Stilt Camp | Yes |
| `underground_room` | Buried chambers connected to caves or surface | Stoneburrow Cellar, Deep Locker Room | Yes |
| `cave_feature` | Cave-integrated generated feature | Lumen Hollow, Ember Vent Outpost | Yes |
| `landmark` | Rare navigation-scale structure | Sunmetal Survey Tower, Frost Beacon | Yes |
| `decorative` | Pure scenery, no loot | Fallen Log, Old Wayflag | No unless modified |

---

## 5. Structure definition schema

```ts
type StructureDef = {
  id: string;
  displayName: string;
  category: StructureCategory;
  version: number;

  allowedBiomes: string[];
  disallowedBiomes?: string[];
  placementMode: "surface" | "shore" | "underwater" | "underground" | "cave_wall" | "cliff";

  footprint: { width: number; depth: number; height: number };
  clearance: { horizontal: number; above: number; below: number };
  minY: number;
  maxY: number;
  minSlope: number;
  maxSlope: number;
  minDistanceFromSpawn: number;
  maxDistanceFromSpawn?: number;
  minDistanceFromSameStructure: number;
  minDistanceFromAnyMajorStructure: number;

  regionChance: number;              // 0.0..1.0 chance per structure region
  maxPerRegion: number;
  weight: number;

  terrainFit: TerrainFitRule;
  resourceConflictPolicy?: "reject" | "avoid" | "allow_for_resource_site";
  maxResourceNodesReplaced?: number;
  rotationMode: "none" | "cardinal" | "any_90";
  mirrorAllowed: boolean;

  templateId?: string;
  proceduralBuilderId?: string;
  paletteId: string;
  lootTables: StructureLootBinding[];
  markerRules: StructureMarkerRule[];
  maskRules: StructureMaskRule[];
};

type TerrainFitRule = {
  mode: "none" | "snap_to_surface" | "flatten" | "terrace" | "stilts" | "embed";
  maxRaiseBlocks: number;
  maxLowerBlocks: number;
  foundationBlock: string;
  fillBlock: string;
  preserveFluids: boolean;
};
```

Required behavior:

```ts
if structure.templateId is set:
    place from template
else if structure.proceduralBuilderId is set:
    run procedural builder
else:
    reject definition at registry load time
```

---

## 6. Structure region selection

Use structure regions to prevent clusters from appearing too often.

```ts
STRUCTURE_REGION_SIZE_CHUNKS = 32;       // 512 × 512 blocks
STRUCTURE_REGION_SIZE_BLOCKS = 512;
STRUCTURE_SALT = 0x5A77C7;
```

For each structure region:

```ts
regionX = floor(chunkX / STRUCTURE_REGION_SIZE_CHUNKS);
regionZ = floor(chunkZ / STRUCTURE_REGION_SIZE_CHUNKS);
regionSeed = hash(worldSeed, STRUCTURE_SALT, regionX, regionZ);

candidateCount = randomInt(regionSeed, 2, 7);
for i in 0..candidateCount-1:
    anchorX = regionX * 512 + randomInt(regionSeed+i, 32, 479);
    anchorZ = regionZ * 512 + randomInt(regionSeed+i, 32, 479);
    biome = sampleBiome(anchorX, anchorZ);
    weightedDefs = getStructureDefsForBiome(biome);
    candidateDef = weightedPick(weightedDefs, regionSeed+i);
    if validateStructureCandidate(candidateDef, anchorX, anchorZ):
        reserveAndGenerate(candidateDef, anchorX, anchorZ);
```

Spacing rules:

| Rule | Value |
|---|---:|
| Minimum distance from world spawn for loot structures | 96 blocks |
| Minimum distance from world spawn for decorative structures | 24 blocks |
| Minimum distance between major structures | 384 blocks |
| Minimum distance between same structure ID | 512 blocks |
| Minimum distance between minor surface structures | 96 blocks |
| Minimum distance between micro structures | 32 blocks |
| Maximum major structures per region | 1 |
| Maximum total structures per region | 7 |

Creative presets may override these rules if `generateStructures = true`.

---

## 7. Candidate validation

A candidate is valid only if every required check passes.

```ts
type StructureCandidate = {
  structureId: string;
  anchor: BlockPos;
  rotation: 0 | 90 | 180 | 270;
  mirrored: boolean;
  biomeId: string;
  averageSurfaceY: number;
  slopeScore: number;
  waterCoverage: number;
  caveExposure: number;
  collisionScore: number;
};
```

Validation checklist:

| Check | Logic |
|---|---|
| Biome allowed | `biomeId in allowedBiomes` and not in `disallowedBiomes` |
| Height allowed | `minY <= anchorY <= maxY` |
| Slope allowed | sampled surface height range must fit `minSlope..maxSlope` |
| Footprint loaded or generatable | all chunks overlapping inflated bounding box are available to query |
| No major collision | no existing structure reservation overlaps protected masks |
| Fluid allowed | surface structures reject if water coverage exceeds structure limit |
| Cave placement allowed | underground structures require enough cave air or enough solid host blocks, depending on mode |
| Spawn distance allowed | candidate must respect minimum distance from spawn |
| Terrain fit possible | flatten/terrace/stilts/embed changes must stay within max raise/lower values |
| Entry reachable | at least one entrance marker must connect to surface, cave, or open air |

Slope score:

```ts
sampledHeights = sampleHeightmapWithinFootprint(candidate.footprint);
slopeScore = max(sampledHeights) - min(sampledHeights);
```

Water coverage:

```ts
waterCoverage = waterColumnCount / footprintColumnCount;
```

---

## 8. Terrain fit rules

| Fit Mode | Use Case | Behavior |
|---|---|---|
| `none` | Floating or fully embedded templates | Does not alter terrain before placement |
| `snap_to_surface` | Micro structures | Places anchor at median surface Y |
| `flatten` | Camps, yards, small ruins | Raises/lowers footprint to a target Y using foundation/fill blocks |
| `terrace` | Hillside structures | Creates stepped support levels every 1–2 blocks of height change |
| `stilts` | Wetlands and shallow water | Leaves terrain mostly intact and adds vertical supports to solid ground/waterbed |
| `embed` | Cellars, cave rooms | Cuts into terrain and creates an entrance or hatch |

Flatten logic:

```ts
targetY = round(median(sampledSurfaceY));
for each column in footprint:
    delta = surfaceY - targetY;
    if delta > maxLowerBlocks: reject;
    if -delta > maxRaiseBlocks: reject;
    if surfaceY > targetY: remove blocks from targetY+1..surfaceY;
    if surfaceY < targetY: fill blocks from surfaceY+1..targetY using fillBlock;
place foundationBlock at targetY;
```

Stilt logic:

```ts
for each support marker:
    y = markerY - 1;
    while y > WORLD_MIN_Y and blockAt(x,y,z) is air or fluid:
        place branchwood_log with metadata { variant: "support" };
        y -= 1;
```

Embed logic:

```ts
carve template air markers first;
replace walls/floors with structure palette;
ensure entrance path has at least 2 blocks of vertical clearance;
```

---

## 9. Template format

Templates are local-coordinate block volumes with palettes and markers.

```ts
type StructureTemplate = {
  id: string;
  version: number;
  size: { x: number; y: number; z: number };
  pivot: { x: number; y: number; z: number };
  palettes: Record<string, string[]>;
  blocks: TemplateBlock[];
  markers: TemplateMarker[];
};

type TemplateBlock = {
  pos: [number, number, number];
  block: string;             // block ID or palette token
  metadata?: Record<string, any>;
  placeMode?: "replace" | "replace_air_only" | "replace_solid_only" | "keep_existing";
};

type TemplateMarker = {
  id: string;
  pos: [number, number, number];
  facing?: 0 | 90 | 180 | 270;
  data?: Record<string, any>;
};
```

Template marker IDs are not gameplay blocks. They are consumed during generation.

| Marker ID | Purpose | Result |
|---|---|---|
| `marker_loot_common` | Small loot container | Places `storage_crate` with common loot table |
| `marker_loot_food` | Food-heavy loot | Places `storage_crate` with food or travel supplies |
| `marker_loot_builder` | Building materials | Places `storage_crate` with blocks/materials |
| `marker_loot_metal` | Metal progression loot | Places `storage_crate` with low-volume ore/bars |
| `marker_station_kiln` | Worksite station | Places `clay_kiln` |
| `marker_station_prep` | Food prep area | Places `prep_board` |
| `marker_station_mend` | Repair area | Places `mend_bench` |
| `marker_light_glowwick` | Basic light | Places `glowwick` |
| `marker_light_lumen` | Strong light | Places `lumen_lamp`, only in tier 3+ structures |
| `marker_entrance` | Pathfinding/clearance marker | Clears two-block-high passage |
| `marker_no_vegetation` | Mask marker | Prevents vegetation placement in radius from marker |
| `marker_road` | Path marker | Replaces surface with gravel, cutstone, or planks |
| `marker_spawn_hook` | Future entity hook | Saved but ignored until entity system exists |

---

## 10. Palette rules

Templates should prefer palette tokens over hard-coded blocks so the same layout can adapt to biome.

```ts
type StructurePalette = {
  id: string;
  biomeOverrides: Record<string, Record<string, string>>;
  defaults: Record<string, string>;
};
```

Default palette tokens:

| Token | Default Block | Notes |
|---|---|---|
| `$foundation` | `cutstone_block` | Base floor/foundation |
| `$wall_stone` | `graystone` | Can become `warm_granite`, `dark_slate`, or `black_basalt` |
| `$wall_wood` | `work_plank` | Built walls, floors, platforms |
| `$support` | `branchwood_log` | Posts, beams, stilts |
| `$roof` | `leafmoss` | Simple natural roof; can become snow-covered via environment overlay |
| `$path` | `shingle_gravel` | Road/path surface |
| `$floor_soft` | `rootsoil` | Interior natural floor |
| `$window` | `clearpane_glass` | Used sparingly |
| `$ruin_gap` | `air` | Missing blocks or broken areas |

Biome palette overrides:

| Biome | `$wall_stone` | `$foundation` | `$roof` | `$path` |
|---|---|---|---|---|
| `meadow` | `graystone` | `cutstone_block` | `leafmoss` | `shingle_gravel` |
| `pinewild` | `dark_slate` | `cutstone_block` | `leafmoss` | `rootsoil` |
| `wetland` | `white_limestone` | `branchwood_log` | `leafmoss` | `river_silt` |
| `drybrush` | `warm_granite` | `cutstone_block` | `work_plank` | `shingle_gravel` |
| `dunes` | `white_limestone` | `cutstone_block` | `work_plank` | `pale_sand` |
| `tundra` | `dark_slate` | `cutstone_block` | `snow_block` | `shingle_gravel` |
| `highlands` | `warm_granite` | `cutstone_block` | `work_plank` | `warm_granite` |

---

## 11. Structure mask rules

Structures write masks into the generation context. The vegetation pass and later structure candidates must respect these masks.

| Mask ID | Blocks Affected | Meaning |
|---|---|---|
| `structure_core` | Inside footprint | No other structure may overlap |
| `foundation` | Floors/supports | Do not replace with vegetation or fluids |
| `entrance_clearance` | Entrance path | Keep two-block-high air passage clear |
| `path` | Roads/paths | Allow short grass/ground cover only if decorative density is low |
| `no_tree` | Around structure | Trees may not generate here |
| `no_tall_plant` | Around windows/doors/roads | Tall plants may not generate here |
| `allow_overgrowth` | Ruin edges | Vines, moss, leafmoss, and shrubs can generate at low density |
| `loot_protected` | Containers/stations | Do not overwrite generated interactable blocks |

Mask radius recommendations:

| Structure Category | `no_tree` Radius | `no_tall_plant` Radius | Path Radius |
|---|---:|---:|---:|
| `surface_micro` | 2 | 1 | 0 |
| `surface_minor` | 8 | 4 | 2 |
| `surface_major` | 14 | 6 | 3 |
| `settlement_cluster` | 20 | 8 | 4 |
| `underground_room` | 4 near entrance | 2 near entrance | 1 |
| `cave_feature` | 0 | 0 | 0 |
| `landmark` | 20 | 8 | 4 |

---

## 12. Structure catalog

### 12.1 Surface micro structures

| Structure | ID | Biomes | Placement | Rarity | Footprint | Loot |
|---|---|---|---|---:|---:|---|
| Pathmark Stones | `pathmark_stones` | All except `dunes` | Surface | Common | `3×2×3` | None |
| Old Wayflag | `old_wayflag` | `meadow`, `drybrush`, `highlands` | Surface | Uncommon | `2×3×2` | 20% common supply |
| Fallen Branchwood | `fallen_branchwood` | `meadow`, `pinewild`, `tundra` | Surface | Common | `5×2×2` | None |
| Saltmarker Cairn | `saltmarker_cairn` | `dunes`, `drybrush` | Surface | Uncommon | `3×3×3` | 25% brightsalt |
| Frostmarker Cairn | `frostmarker_cairn` | `tundra`, `highlands` | Surface | Uncommon | `3×3×3` | 20% frost shards |

### 12.2 Surface minor structures

| Structure | ID | Biomes | Placement | Rarity | Footprint | Main Blocks | Loot |
|---|---|---|---|---:|---:|---|---|
| Forager Lean-To | `forager_lean_to` | `meadow`, `pinewild`, `drybrush` | Surface | Common | `7×5×6` | `branchwood_log`, `work_plank`, `leafmoss` | Food + fiber |
| Resin Tap Grove | `resin_tap_grove` | `pinewild`, `meadow` | Surface | Uncommon | `9×6×9` | `branchwood_log`, `resin_knot`, `storage_crate` | Resin + cord |
| Wetland Stilt Cache | `wetland_stilt_cache` | `wetland` | Shore/shallow water | Uncommon | `7×7×7` | `branchwood_log`, `work_plank`, `river_silt` | Food + Paletin chance |
| Drybrush Niter Pit | `drybrush_niter_pit` | `drybrush`, `dunes` | Surface | Uncommon | `8×5×8` | `shingle_gravel`, `warm_granite`, `niterstone_pocket` | Spark niter |
| Frost Shelter | `frost_shelter` | `tundra`, `highlands` | Surface | Uncommon | `7×5×6` | `dark_slate`, `snow_block`, `branchwood_log` | Embercoal + food |

### 12.3 Surface major structures and landmarks

| Structure | ID | Biomes | Placement | Rarity | Footprint | Main Blocks | Loot Tier |
|---|---|---|---|---:|---:|---|---:|
| Weathered Watchpost | `weathered_watchpost` | `meadow`, `drybrush`, `highlands` | Surface or cliff | Rare | `11×14×11` | `branchwood_log`, `work_plank`, `cutstone_block` | 1–2 |
| Ruined Kiln Yard | `ruined_kiln_yard` | `meadow`, `drybrush`, `dunes` | Surface | Rare | `15×7×15` | `fired_brick_block`, `clay_kiln`, `shingle_gravel` | 1–3 |
| Mossroot Hut Cluster | `mossroot_hut_cluster` | `pinewild`, `wetland` | Surface | Rare | `21×8×21` | `rootsoil`, `branchwood_log`, `leafmoss` | 1–2 |
| Sunmetal Survey Tower | `sunmetal_survey_tower` | `dunes`, `highlands` | Surface | Very rare | `13×22×13` | `warm_granite`, `clearpane_glass`, `sunmetal_bar` marker loot | 3–4 |
| Frost Beacon Ruin | `frost_beacon_ruin` | `tundra`, `highlands` | Surface | Very rare | `13×16×13` | `dark_slate`, `frostglass`, `lumen_lamp` | 2–4 |

### 12.4 Underground and cave structures

| Structure | ID | Biomes / Hosts | Placement | Rarity | Footprint | Loot Tier |
|---|---|---|---|---:|---:|---:|
| Stoneburrow Cellar | `stoneburrow_cellar` | Any surface biome, stone below | Underground room | Rare | `11×7×11` | 1–2 |
| Lumen Hollow | `lumen_hollow` | Cave walls with `lumen_quartz_cluster` nearby | Cave feature | Uncommon below `y=80` | `12×9×12` | 2–3 |
| Ember Vent Outpost | `ember_vent_outpost` | `black_basalt`, emberflow nearby | Cave feature | Rare below `y=35` | `13×9×13` | 3–4 |
| Deep Locker Room | `deep_locker_room` | `deepmantle` below `y=28` | Underground room | Very rare | `9×7×9` | 4–5 |
| Staropal Pocket Shrine | `staropal_pocket_shrine` | `deepmantle`, cave wall exposure | Cave feature | Extremely rare below `y=22` | `9×8×9` | 5 |

The word “shrine” here is a neutral landmark term. It should not imply a real-world religion or faction unless narrative systems are added later.

---

## 13. Detailed placement tables

### 13.1 Surface structure rules

| Structure ID | Allowed Biomes | Y Range | Region Chance | Max / Region | Min Same Distance | Max Slope | Water Limit |
|---|---|---:|---:|---:|---:|---:|---:|
| `pathmark_stones` | all except dunes | `50–190` | `0.95` | 4 | 48 | 4 | 0.20 |
| `old_wayflag` | meadow, drybrush, highlands | `55–190` | `0.45` | 2 | 128 | 5 | 0.10 |
| `forager_lean_to` | meadow, pinewild, drybrush | `55–160` | `0.55` | 2 | 192 | 3 | 0.05 |
| `resin_tap_grove` | pinewild, meadow | `55–155` | `0.35` | 1 | 256 | 4 | 0.05 |
| `wetland_stilt_cache` | wetland | `80–105` | `0.35` | 1 | 256 | 2 | 0.70 |
| `drybrush_niter_pit` | drybrush, dunes | `55–140` | `0.30` | 1 | 256 | 3 | 0.00 |
| `frost_shelter` | tundra, highlands | `75–180` | `0.30` | 1 | 256 | 4 | 0.10 |
| `weathered_watchpost` | meadow, drybrush, highlands | `65–190` | `0.20` | 1 | 512 | 5 | 0.05 |
| `ruined_kiln_yard` | meadow, drybrush, dunes | `55–150` | `0.18` | 1 | 512 | 2 | 0.05 |
| `mossroot_hut_cluster` | pinewild, wetland | `60–130` | `0.12` | 1 | 640 | 3 | 0.20 |
| `sunmetal_survey_tower` | dunes, highlands | `80–190` | `0.06` | 1 | 1024 | 4 | 0.00 |
| `frost_beacon_ruin` | tundra, highlands | `90–190` | `0.06` | 1 | 1024 | 5 | 0.05 |

### 13.2 Underground and cave structure rules

| Structure ID | Required Host / Context | Y Range | Attempts / Region | Chance / Attempt | Air Requirement | Fluid Limit |
|---|---|---:|---:|---:|---:|---:|
| `stoneburrow_cellar` | Solid stone within 20 blocks of surface | `45–110` | 5 | `0.18` | Low, generated room | 0.00 |
| `lumen_hollow` | Cave wall, `lumen_quartz_cluster` within 20 blocks | `20–100` | 8 | `0.22` | At least 35% cave air | 0.10 |
| `ember_vent_outpost` | `black_basalt` and emberflow within 24 blocks | `5–35` | 6 | `0.12` | At least 25% cave air | Emberflow allowed below floor only |
| `deep_locker_room` | `deepmantle`, no cave required | `8–28` | 4 | `0.06` | Low, generated room | 0.00 |
| `staropal_pocket_shrine` | `deepmantle`, cave wall exposure | `2–22` | 3 | `0.03` | At least 20% cave air | 0.00 |

---

## 14. Structure degradation and overgrowth

Structure age should be deterministic and decorative. It should not remove required gameplay objects such as guaranteed entrances or required loot containers.

```ts
ageRoll = randomFloat(hash(worldSeed, structureInstanceId, "age"));
if ageRoll < 0.20: ageState = "intact";
else if ageRoll < 0.65: ageState = "weathered";
else if ageRoll < 0.92: ageState = "ruined";
else: ageState = "collapsed";
```

| Age State | Missing Block Chance | Extra Rubble Chance | Overgrowth Chance | Loot Modifier |
|---|---:|---:|---:|---:|
| `intact` | 0.00 | 0.02 | 0.03 | ×1.00 |
| `weathered` | 0.04 | 0.08 | 0.08 | ×1.00 |
| `ruined` | 0.12 | 0.18 | 0.16 | ×0.85 |
| `collapsed` | 0.24 | 0.30 | 0.22 | ×0.70 |

Block replacement rules:

| Original Block | Weathered Result | Collapsed Result |
|---|---|---|
| `work_plank` | `work_plank` with metadata `{ age: "weathered" }` | 50% `air`, 50% `branchwood_log` with fallen orientation |
| `branchwood_log` | `branchwood_log` with metadata `{ age: "weathered" }` | 25% `charred_log` if dry/hot biome, otherwise `branchwood_log` |
| `cutstone_block` | `cutstone_block` with metadata `{ cracked: true }` | `stone_rubble` item marker or `graystone` block |
| `fired_brick_block` | `fired_brick_block` with metadata `{ cracked: true }` | `claybed` or `stone_rubble` item marker |
| `clearpane_glass` | 30% broken into `glass_shard` item marker | 70% `air`, 30% `glass_shard` item marker |

Overgrowth rules:

```ts
if mask allows overgrowth:
    place leafmoss, hanging_vine, thornbrush, snowpack, or dry_sagebrush based on biome
```

The optional plant blocks `hanging_vine`, `dry_sagebrush`, and `snow_lichen` are defined in the Biome Vegetation ruleset.

---

## 15. Loot generation

Loot should be useful but not replace core progression. Early structures may give small amounts of materials; deep or rare structures may provide one or two advanced items.

```ts
type LootTable = {
  id: string;
  rolls: { min: number; max: number };
  entries: LootEntry[];
};

type LootEntry = {
  itemId: string;
  min: number;
  max: number;
  weight: number;
  chance?: number;
  requiredStructureTier?: number;
};
```

Loot algorithm:

```ts
function generateLoot(containerId, tableId, structureInstanceId) {
  const seed = hash(worldSeed, structureInstanceId, containerId, tableId);
  const table = lootTables[tableId];
  const rolls = randomInt(seed, table.rolls.min, table.rolls.max);
  const output = [];

  for i in 0..rolls-1:
      entry = weightedPick(table.entries, seed+i);
      if entry.chance == null or randomFloat(seed+i+99) <= entry.chance:
          output.add(entry.itemId, randomInt(seed+i+77, entry.min, entry.max));

  return normalizeStacks(output);
}
```

### 15.1 Loot tables

| Loot Table | ID | Rolls | Typical Use |
|---|---|---:|---|
| Common Supply | `loot_common_supply` | 2–4 | Small caches and old wayflags |
| Forager Food | `loot_forager_food` | 2–5 | Lean-tos, shelters, huts |
| Builder Cache | `loot_builder_cache` | 3–6 | Kiln yards, work sites, watchposts |
| Miner Cache | `loot_miner_cache` | 2–5 | Niter pits, cellars, cave structures |
| Metal Cache | `loot_metal_cache` | 1–4 | Rare surface landmarks and deep rooms |
| Deep Cache | `loot_deep_cache` | 2–4 | Deep Locker Room, Staropal Pocket |
| Empty Ruin | `loot_empty_ruin` | 0–1 | Mostly decorative ruins |

Common Supply entries:

| Item | Count | Weight |
|---|---:|---:|
| `reed_fiber` | 2–8 | 16 |
| `fiber_cord` | 1–4 | 12 |
| `stout_pole` | 1–5 | 12 |
| `stone_pebble` | 2–8 | 10 |
| `flint_shard` | 1–4 | 8 |
| `glowwick` | 1–3 | 6 |
| `berry_cluster` | 1–4 | 5 |

Forager Food entries:

| Item | Count | Weight |
|---|---:|---:|
| `berry_cluster` | 2–8 | 14 |
| `grain_bundle` | 2–6 | 12 |
| `trail_ration` | 1–3 | 7 |
| `clean_water_flask` | 1–2 | 5 |
| `brightsalt` | 1–4 | 4 |
| `field_bandage` | 1–2 | 3 |

Builder Cache entries:

| Item | Count | Weight |
|---|---:|---:|
| `work_plank` | 4–16 | 12 |
| `branchwood_log` | 2–8 | 10 |
| `stone_rubble` | 6–18 | 10 |
| `fired_brick` | 2–10 | 7 |
| `glass_shard` | 1–6 | 5 |
| `clay_lump` | 3–12 | 8 |
| `resin_blob` | 1–5 | 5 |

Miner Cache entries:

| Item | Count | Weight |
|---|---:|---:|
| `embercoal` | 2–8 | 12 |
| `spark_niter` | 1–5 | 8 |
| `raw_rosycopper` | 1–4 | 6 |
| `raw_paletin` | 1–3 | 4 |
| `raw_rustcore` | 1–2 | 2 |
| `small_blast_charge` | 1 | 1 |

Metal Cache entries:

| Item | Count | Weight |
|---|---:|---:|
| `rosycopper_bar` | 1–3 | 10 |
| `paletin_bar` | 1–2 | 6 |
| `bronze_bar` | 1–2 | 4 |
| `ironroot_bar` | 1 | 2 |
| `sunmetal_bar` | 1 | 1 |
| `lumen_crystal` | 1–2 | 2 |

Deep Cache entries:

| Item | Count | Weight |
|---|---:|---:|
| `raw_umbralite` | 1–2 | 6 |
| `deepsteel_bar` | 1 | 2 |
| `lumen_crystal` | 1–3 | 6 |
| `lumen_dust` | 2–5 | 5 |
| `staropal_shard` | 1 | 1 |
| `field_bandage` | 1–3 | 4 |

---

## 16. Structure container and station rules

Generated loot containers should use normal inventory rules from the Survival ruleset.

```ts
container.generatedByStructure = true;
container.lootTableId = marker.lootTableId;
container.lootSeed = hash(worldSeed, structureInstanceId, marker.localPos);
container.generated = false;
```

Lazy loot generation is preferred:

```ts
onContainerOpened(container):
    if container.generatedByStructure and container.generated == false:
        container.inventory = generateLoot(container.id, container.lootTableId, container.structureInstanceId)
        container.generated = true
        markChunkDirty(container.chunkPos)
```

Station rules:

| Station | Structure Use | Initial State |
|---|---|---|
| `clay_kiln` | Kiln yards, desert camps | Empty, 25% chance to contain 1–2 `embercoal` as fuel |
| `prep_board` | Lean-tos, huts, shelters | Empty |
| `mend_bench` | Watchposts, miner rooms | Empty, rare in early structures |
| `campfire` | Shelters and camps | Unlit unless weather preset or structure flag says lit |
| `storage_crate` | Loot containers | Lazy-generated loot |

---

## 17. Roads, paths, and entrances

Structures may create local paths but should not generate long roads until a broader settlement/road system exists.

Path placement:

```ts
for each marker_road:
    pathWidth = marker.data.width ?? 2;
    pathLength = marker.data.length ?? 8;
    carve surface vegetation along path;
    replace surface with biome path block;
```

Path block by biome:

| Biome | Path Block |
|---|---|
| `meadow` | `shingle_gravel` |
| `pinewild` | `rootsoil` |
| `wetland` | `branchwood_log` with metadata `{ variant: "plank_walkway" }` |
| `drybrush` | `shingle_gravel` |
| `dunes` | `pale_sand` with metadata `{ compacted: true }` |
| `tundra` | `snow_block` or `shingle_gravel` |
| `highlands` | `warm_granite` |

Entrance clearance:

```ts
for each marker_entrance:
    clear area width 2, height 3, length marker.data.length ?? 4
    do not remove worldroot, deepmantle below y=4, or protected fluid sources
```

---

## 18. Environment interaction rules

Structures should respond to weather without requiring expensive whole-structure updates.

| Environment Effect | Structure Rule |
|---|---|
| Rain | Exposed campfires extinguish unless under roof blocks; wetland structures gain wetness metadata on planks |
| Thunderstorm | Lightning can convert exposed `branchwood_log` to `charred_log`; lightning rods can be added later using `sunmetal_bar` |
| Snow | Snow can accumulate on exposed roofs and paths unless mask has `no_snow` |
| Fog | No generation impact; rendering can increase landmark discovery distance penalty |
| Cloud coverage | No generation impact |
| Seasonal cold | Tundra and highland structures may receive initial `snowpack` overlay |
| Emberflow proximity | Wooden blocks within 3 blocks of exposed emberflow are replaced by `charred_log` or avoided during validation |

Roof detection:

```ts
isSheltered(pos) = any solid block above pos within y+1..WORLD_MAX_Y before sky
```

---

## 19. Creative mode structure rules

Creative mode can reuse natural structure generation or disable it by world preset.

| World Preset | Structure Default |
|---|---|
| `survival_terrain` | Enabled |
| `flat_builder` | Disabled |
| `void_builder` | Disabled |
| `island_builder` | Enabled unless player disables it |
| `cave_builder` | Underground/cave structures enabled; surface structures disabled |
| `sky_shelf` | Disabled unless floating structures are added |

Creative catalog/world-edit integration:

| Action | Rule |
|---|---|
| Place structure template | Allowed only in Creative or with admin permission |
| Copy generated structure | Saves only selected block volume, not generated loot seeds unless explicitly included |
| Undo placed structure | Restores previous blocks from world-edit undo buffer |
| Fill/replace inside generated structure | Marks affected chunks dirty and records player modifications |
| Generate structure from menu | Uses the same `StructureDef`, validation can be bypassed with warning |

---


## 20. Structure audio/VFX hooks

Generated structures should emit gameplay-neutral discovery and interaction events that presentation systems can subscribe to.

| Event ID | Trigger | Audio/VFX intent | Multiplayer rule |
|---|---|---|---|
| `structure_discovered` | Player first enters discovery radius or reveals marker. | Short discovery cue, optional map/wayflag sparkle. | Local to discovering player unless shared discovery is enabled. |
| `structure_chest_open` | Loot container opens for the first time. | Container open cue, dust motes, subtle glow from generated loot marker. | Host validates container state before final cue. |
| `structure_place_vfx` | Creative/admin places a structure template. | Block placement burst at bounds corners, short build shimmer. | Host-only or host-validated batch placement. |
| `structure_loot_empty` | Player opens already-looted container. | Soft empty/wood knock cue. | Uses authoritative opened-container state. |

These events should not regenerate loot, mutate blocks, or unlock progression by themselves. They are presentation hooks only.

---

## 21. Persistence rules

The save system should not need to store every untouched generated structure block individually. Store enough data to regenerate unmodified structure blocks and store deltas for player changes.

Persisted structure state:

```ts
type StructureInstanceSave = {
  instanceId: string;
  structureId: string;
  structureVersion: number;
  anchor: { x: number; y: number; z: number };
  rotation: 0 | 90 | 180 | 270;
  mirrored: boolean;
  boundingBox: Aabb;
  generatedAtDataVersion: number;
  ageState: "intact" | "weathered" | "ruined" | "collapsed";
  lootContainerIds: string[];
  openedContainerIds: string[];
  markerState?: Record<string, any>;
};
```

Chunk save references:

```ts
type ChunkStructureState = {
  overlappingStructureIds: string[];
  localStructureMasks: StructureMaskRun[];
  blockDeltasFromGeneratedStructures: BlockDelta[];
};
```

Rules:

1. Generated structure instance data is saved once per owning region or chunk group.
2. Opened loot and changed containers are saved as dynamic state.
3. Player-placed/removed blocks inside structure bounds are saved as chunk block data or deltas.
4. Regenerating a chunk must not reset opened containers or undo player edits.
5. If a structure definition changes version, existing instances keep their original `structureVersion` unless migrated.

The full save schema is defined in `voxel_save_versioning_schema.md`.

---

## 22. Example structure definition

```json
{
  "id": "forager_lean_to",
  "displayName": "Forager Lean-To",
  "category": "surface_minor",
  "version": 1,
  "allowedBiomes": ["meadow", "pinewild", "drybrush"],
  "placementMode": "surface",
  "footprint": { "width": 7, "depth": 6, "height": 5 },
  "clearance": { "horizontal": 2, "above": 2, "below": 1 },
  "minY": 55,
  "maxY": 160,
  "minSlope": 0,
  "maxSlope": 3,
  "minDistanceFromSpawn": 96,
  "minDistanceFromSameStructure": 192,
  "minDistanceFromAnyMajorStructure": 128,
  "regionChance": 0.55,
  "maxPerRegion": 2,
  "weight": 12,
  "terrainFit": {
    "mode": "flatten",
    "maxRaiseBlocks": 2,
    "maxLowerBlocks": 2,
    "foundationBlock": "work_plank",
    "fillBlock": "loose_loam",
    "preserveFluids": false
  },
  "rotationMode": "cardinal",
  "mirrorAllowed": true,
  "templateId": "template_forager_lean_to_v1",
  "paletteId": "palette_basic_surface_v1",
  "lootTables": [
    { "markerId": "marker_loot_food", "lootTableId": "loot_forager_food" },
    { "markerId": "marker_loot_common", "lootTableId": "loot_common_supply" }
  ],
  "markerRules": [
    { "markerId": "marker_light_glowwick", "chance": 0.7 },
    { "markerId": "marker_station_prep", "chance": 0.4 }
  ],
  "maskRules": [
    { "maskId": "no_tree", "radius": 8 },
    { "maskId": "no_tall_plant", "radius": 4 }
  ]
}
```

---

## 23. Minimal implementation checklist

```txt
Structure definition registry
Structure template loader
Structure region selector
Candidate validation
Terrain fitting
Template placement with rotation/mirroring
Palette resolution
Structure masks
Loot table registry
Lazy loot container generation
Structure dynamic save state
Vegetation pass integration
Creative structure-placement hooks
Structure discovery and container feedback hooks
Structure migration/version handling
```
