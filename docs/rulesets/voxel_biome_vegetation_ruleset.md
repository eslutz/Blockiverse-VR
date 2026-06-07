# Voxel Biome Vegetation Ruleset

Version: 1.0
Companion documents: `voxel_survival_ruleset.md`, `voxel_creative_ruleset.md`, `voxel_world_environment_effects.md`, `voxel_structure_generation_ruleset.md`, `voxel_survival_menus.md`, `voxel_save_versioning_schema.md`, `voxel_multiplayer_networking_ruleset.md`, and `voxel_audio_vfx_ruleset.md`

This document expands the Survival biome rules with deeper vegetation generation, tree variants, plant placement, groundcover, regrowth, saplings, vegetation/environment interactions, and save hooks.

The goal is to keep vegetation deterministic, readable, and easy to convert into chunk-generation logic.

---

## 1. Design goals

Biome vegetation should:

1. Give each biome a recognizable silhouette and resource profile.
2. Reuse existing custom resources such as Branchwood, Leafmoss, Reedgrass, Berrybush, Thornbrush, Grain Stalk, Resin Knot, and Reed Fiber.
3. Add new small vegetation blocks only where they improve biome identity.
4. Respect generated structures, paths, fluids, caves, and spawn safety.
5. Support predictable farming, regrowth, harvesting, and Creative testing.
6. Avoid heavy per-block updates by using deterministic generation plus sparse growth tick queues.

---

## 2. Core constants

These constants match the Survival and Environment rulesets.

```ts
TICKS_PER_SECOND = 20;
TICKS_PER_DAY = 24000;
CHUNK_SIZE = 16;
WORLD_MIN_Y = 0;
WORLD_MAX_Y = 255;
SEA_LEVEL = 96;
MAX_LIGHT_LEVEL = 15;
```

Vegetation-specific constants:

```ts
TREE_PLACEMENT_CELL_SIZE = 4;          // Candidate grid inside a chunk
MAX_TREE_ATTEMPTS_PER_CHUNK = 24;
MAX_PLANT_PATCH_ATTEMPTS_PER_CHUNK = 48;
DEFAULT_TREE_MIN_SPACING = 5;
DEFAULT_SHRUB_MIN_SPACING = 2;
GROUND_COVER_SAMPLE_COUNT = 96;
SPAWN_STARTER_SCAN_RADIUS = 64;
LEAF_DECAY_DISTANCE = 5;
SAPLING_GROWTH_CHECK_TICKS = 1200;     // 60 seconds
WILD_REGROWTH_CHECK_TICKS = 24000;     // 1 game day
```

---

## 3. Relationship to existing blocks and items

The Survival ruleset already defines these vegetation-related blocks and resources:

| Existing ID | Existing Name | Use in this document |
|---|---|---|
| `branchwood_log` | Branchwood Log | Trunks, fallen logs, roots, supports |
| `charred_log` | Charred Log | Lightning-burned or fire-damaged tree remains |
| `smooth_branchwood` | Smooth Branchwood | Optional stripped/decorative trunk state output |
| `leafmoss` | Leafmoss | Canopies, moss, leaf clumps, forest groundcover drops |
| `thornbrush` | Thornbrush | Hazard shrub in Pinewild, Drybrush, and overgrown structures |
| `reedgrass` | Reedgrass | Wetland fiber plant and farmable crop |
| `berrybush` | Berrybush | Wild food source and farmable bush |
| `grain_stalk` | Grain Stalk | Wild grain and farm crop source |
| `resin_knot` | Resin Knot | Tree-side resource node |
| `snowpack` | Snowpack | Environmental snow layer over vegetation |
| `snow_block` | Snow Block | Full-depth snow and compressed snow |
| `tended_soil` | Tended Soil | Farming block created by Tiller |

---

## 4. Vegetation registry additions

These are canonical additions to the block registry. They use existing item drops where possible, so they do not require a large new item catalog.

| Name | ID | Description | Hardness | Tool / Tier | Drops |
|---|---|---|---:|---|---|
| Branchwood Sapling | `branchwood_sapling` | Young tree that can grow into biome-specific Branchwood variants. | 0.1 | Hand / 0 | `branchwood_sapling ×1` |
| Drygrass Tuft | `drygrass_tuft` | Dry fiber plant common in warm biomes. | 0.1 | Sickle / 0 | `reed_fiber ×1`, 15% `drygrass_seed ×1` |
| Moss Carpet | `moss_carpet` | Thin groundcover in damp shaded areas. | 0.1 | Sickle / 0 | 60% `leafmoss ×1` |
| Wildflower Cluster | `wildflower_cluster` | Decorative meadow plant that supports pollinator ambience. | 0.1 | Sickle / 0 | 20% `meadow_seed ×1` |
| Dune Sage | `dune_sage` | Sparse desert shrub with dry fibers and occasional resin. | 0.2 | Sickle / 0 | `reed_fiber ×1`, 10% `resin_blob ×1` |
| Salt Reed | `salt_reed` | Brine-tolerant reed that grows near salty water. | 0.1 | Sickle / 0 | `reed_fiber ×1–2`, 8% `brightsalt ×1` |
| Snow Lichen | `snow_lichen` | Hardy cold-ground plant. | 0.1 | Sickle / 0 | 40% `leafmoss ×1` |
| Frost Fern | `frost_fern` | Cold biome plant that survives under light snow. | 0.1 | Sickle / 0 | `reed_fiber ×1`, 5% `frost_shard ×1` |
| Windroot Shrub | `windroot_shrub` | Tough highland shrub that indicates windy exposed slopes. | 0.2 | Sickle / 0 | `reed_fiber ×1`, 15% `resin_blob ×1` |
| Hanging Reed | `hanging_reed` | Damp overhang vegetation placed under Leafmoss or cave mouths. | 0.1 | Sickle / 0 | `reed_fiber ×1–2` |
| Fallen Leaves | `fallen_leaves` | Thin decorative ground layer that decays into soil richness. | 0.1 | Sickle / 0 | 50% `leafmoss ×1` |

Stack size rule:

```txt
All small vegetation blocks stack to 99 unless they have metadata. Metadata variants stack only with identical metadata.
```

---

## 5. Tree variant model

All natural trees use `branchwood_log` and `leafmoss` with block state metadata instead of separate wood item families.

```ts
type BranchwoodLogState = {
  axis: "x" | "y" | "z";
  treeVariant: TreeVariant;
  stripped?: boolean;
  natural?: boolean;
};

type LeafmossState = {
  leafVariant: TreeVariant;
  persistent: boolean;
  decayDistance: number;       // 0..LEAF_DECAY_DISTANCE
};

type TreeVariant =
  | "crownbranch"
  | "needlebranch"
  | "leanbranch"
  | "scrubbranch"
  | "fanbranch"
  | "frostbranch"
  | "windbranch";
```

Inventory rule:

```ts
if a `branchwood_log` has treeVariant metadata:
    it may stack only with logs of the same treeVariant and axis state;

if simplified inventory is enabled:
    harvested logs normalize to plain `branchwood_log` without treeVariant metadata;
```

Recommended first playable setting:

```ts
normalizeHarvestedTreeMetadata = true;
```

---

## 6. Vegetation generation pipeline

Vegetation generation is split into large and detail passes so structures can reserve clear space.

```txt
1. Receive biome map, heightmap, surface blocks, base fluids, and structure reservation masks.
2. Generate large trees and major shrubs outside `no_large_tree` masks.
3. Place major structures if they were only reserved earlier.
4. Generate small shrubs, wild crops, and plant patches.
5. Generate groundcover such as Moss Carpet, Fallen Leaves, Snow Lichen, and Drygrass Tuft.
6. Place tree-side resources such as Resin Knot.
7. Apply initial environment overlays such as Snowpack in freezing biomes.
8. Build vegetation growth queues for saplings, wild regrowth, and berrybush timers.
```

If structures are placed before vegetation, steps 2 and 3 may be swapped, but vegetation must still respect structure masks and protected interiors.

---

## 7. Biome vegetation profile schema

```ts
type BiomeVegetationProfile = {
  biomeId: string;
  treeDensityPerChunk: { min: number; max: number };
  shrubDensityPerChunk: { min: number; max: number };
  plantPatchAttemptsPerChunk: number;
  groundCoverChance: number;             // Chance per sampled groundcover point
  preferredTreeVariants: Weighted<TreeVariant>[];
  validTreeSurfaces: string[];
  validPlantSurfaces: string[];
  maxTreeSlopeDelta: number;
  requiresWaterForPrimaryPlants?: boolean;
  temperatureBias: "cold" | "temperate" | "warm" | "hot";
  moistureBias: "dry" | "balanced" | "wet";
};

type Weighted<T> = {
  value: T;
  weight: number;
};
```

---

## 8. Biome vegetation profiles

| Biome | Trees / Chunk | Shrubs / Chunk | Ground Cover Chance | Primary Tree Variants | Signature Plants |
|---|---:|---:|---:|---|---|
| Meadow | 2–5 | 2–6 | 0.30 | Crownbranch, occasional Leanbranch near water | Wildflower Cluster, Grain Stalk, Berrybush |
| Pinewild | 8–16 | 4–10 | 0.55 | Needlebranch, Crownbranch | Moss Carpet, Thornbrush, Resin Knot, Hanging Reed |
| Wetland | 2–6 | 3–8 | 0.42 | Leanbranch, Crownbranch | Reedgrass, Salt Reed, Moss Carpet, Berrybush |
| Drybrush | 1–4 | 5–12 | 0.25 | Scrubbranch | Drygrass Tuft, Dune Sage, Thornbrush |
| Dunes | 0–1 | 1–5 | 0.08 | Fanbranch rare | Dune Sage, Salt Reed near brine, Brightsalt Crust nearby |
| Tundra | 0–3 | 1–5 | 0.20 | Frostbranch | Snow Lichen, Frost Fern, sparse Berrybush |
| Highlands | 0–4 | 2–7 | 0.18 | Windbranch, Frostbranch at cold height | Windroot Shrub, Frost Fern, Moss Carpet in shaded cracks |

---

## 9. Tree templates

### 9.1 Crownbranch Tree

A broad temperate tree used in Meadows and mixed Pinewild edges.

| Field | Value |
|---|---|
| Variant ID | `crownbranch` |
| Trunk height | 4–7 blocks |
| Trunk width | 1 block; 10% chance of 2×2 old tree in Pinewild |
| Canopy radius | 2–4 blocks |
| Canopy shape | Rounded crown with sparse lower Leafmoss |
| Resin chance | 8% per tree |
| Valid surfaces | `meadow_turf`, `rootsoil`, `loose_loam` |
| Minimum spacing | 6 blocks |

Generation:

```ts
height = randomInt(4, 7);
place vertical branchwood_log from y=1 to y=height;
canopyCenterY = height;
for each block in sphere(radius=randomInt(2,4)):
    if noise3D(local) > -0.25:
        place leafmoss with leafVariant="crownbranch";
```

### 9.2 Needlebranch Tree

A tall conifer-like Pinewild tree using custom naming and Branchwood materials.

| Field | Value |
|---|---|
| Variant ID | `needlebranch` |
| Trunk height | 7–13 blocks |
| Canopy radius | 2–3 blocks |
| Canopy shape | Stacked cones or tapering vertical leaf layers |
| Resin chance | 18% per tree |
| Valid surfaces | `rootsoil`, `meadow_turf`, `snowcap_turf` |
| Minimum spacing | 5 blocks |

Generation:

```ts
height = randomInt(7, 13);
for y in 1..height:
    place branchwood_log(axis="y", treeVariant="needlebranch");

for layerY in 3..height step 2:
    radius = clamp(floor((height - layerY) / 3) + 1, 1, 3);
    place leafmoss disk with radius and random edge gaps;
```

### 9.3 Leanbranch Tree

A wetland tree that leans over water and grows hanging reeds.

| Field | Value |
|---|---|
| Variant ID | `leanbranch` |
| Trunk height | 4–8 blocks |
| Lean distance | 1–3 blocks toward nearest water |
| Canopy radius | 2–3 blocks |
| Resin chance | 6% per tree |
| Hanging Reed chance | 35% below canopy if air below |
| Valid surfaces | `river_silt`, `rootsoil`, `loose_loam`, `meadow_turf` near water |
| Minimum spacing | 6 blocks |

Generation:

```ts
waterDirection = directionToNearestFluid(anchor, "freshwater", radius=8) ?? randomCardinal();
for y in 1..height:
    offset = floor((y / height) * leanDistance);
    place branchwood_log at anchor + waterDirection * offset + (0,y,0);
```

### 9.4 Scrubbranch Tree

A short warm-biome tree that provides sparse wood without making Drybrush too easy.

| Field | Value |
|---|---|
| Variant ID | `scrubbranch` |
| Trunk height | 2–4 blocks |
| Canopy radius | 1–2 blocks |
| Branch chance | 45% for 1–2 horizontal branches |
| Resin chance | 12% per tree |
| Valid surfaces | `dry_turf`, `loose_loam`, `pale_sand` near dry_turf |
| Minimum spacing | 7 blocks |

### 9.5 Fanbranch Tree

A very rare Dunes tree that appears near brine oases or dry basin edges.

| Field | Value |
|---|---|
| Variant ID | `fanbranch` |
| Trunk height | 5–8 blocks |
| Canopy radius | 2 blocks in flat fan clusters |
| Resin chance | 4% per tree |
| Required nearby fluid | Freshwater or brine within 12 blocks |
| Valid surfaces | `pale_sand`, `dry_turf`, `river_silt` |
| Minimum spacing | 12 blocks |

### 9.6 Frostbranch Tree

A cold-biome tree with limited height and snow-friendly canopy.

| Field | Value |
|---|---|
| Variant ID | `frostbranch` |
| Trunk height | 3–6 blocks |
| Canopy radius | 1–3 blocks |
| Snow support | Can receive Snowpack on top of Leafmoss |
| Resin chance | 10% per tree |
| Valid surfaces | `snowcap_turf`, `rootsoil`, `meadow_turf` in cold temperatures |
| Minimum spacing | 7 blocks |

### 9.7 Windbranch Tree

A highland tree that is short, bent, and sparse.

| Field | Value |
|---|---|
| Variant ID | `windbranch` |
| Trunk height | 2–5 blocks |
| Horizontal bend | 1–4 blocks away from prevailing wind |
| Canopy radius | 1–2 blocks |
| Resin chance | 15% per tree |
| Valid surfaces | `warm_granite`, `graystone`, `meadow_turf`, `snowcap_turf` |
| Minimum spacing | 9 blocks |

---

## 10. Tree placement algorithm

### 10.1 Candidate selection

```ts
function generateTreeCandidates(chunk, biomeProfile, seed) {
  count = randomInt(seed, biomeProfile.treeDensityPerChunk.min, biomeProfile.treeDensityPerChunk.max);
  candidates = [];

  for i in 0..MAX_TREE_ATTEMPTS_PER_CHUNK:
      x = chunk.minX + randomInt(hash(seed, i, "x"), 0, 15);
      z = chunk.minZ + randomInt(hash(seed, i, "z"), 0, 15);
      y = surfaceHeightAt(x, z);

      if validateTreeCandidate(x, y, z, biomeProfile):
          candidates.push({x,y,z});

      if candidates.length == count:
          break;

  return poissonFilter(candidates, biomeProfile.minSpacing ?? DEFAULT_TREE_MIN_SPACING);
}
```

### 10.2 Candidate validation

```ts
function validateTreeCandidate(x, y, z, profile) {
  surface = blockAt(x, y, z);
  above = blockAt(x, y + 1, z);

  if surface.id not in profile.validTreeSurfaces:
      return false;

  if above.id != "air" and above.id != "snowpack":
      return false;

  if slopeDeltaAround(x, z, radius=2) > profile.maxTreeSlopeDelta:
      return false;

  if structureMaskAt(x, z).contains("no_large_tree"):
      return false;

  if isWithinFluidOrTooCloseToEmberflow(x, y, z):
      return false;

  if skyLightAt(x, y + 1, z) < 9:
      return false;

  return true;
}
```

### 10.3 Tree-side resource placement

Resin Knot placement:

```ts
if random() < tree.resinChance:
    side = randomHorizontalFace();
    logPos = chooseTrunkBlock(y between 1 and trunkHeight - 1);
    if adjacent block is air:
        place `resin_knot` attached to log side;
```

Biome resin modifiers:

| Biome | Resin Modifier |
|---|---:|
| Pinewild | ×1.5 |
| Drybrush | ×1.2 |
| Highlands | ×1.1 |
| Wetland | ×0.8 |
| Dunes | ×0.6 |
| Tundra | ×0.7 |

---

## 11. Plant and groundcover placement table

| Plant | ID | Valid Surfaces | Biomes | Attempts / Chunk | Patch Size | Placement Notes |
|---|---|---|---|---:|---:|---|
| Berrybush | `berrybush` | `meadow_turf`, `rootsoil`, `snowcap_turf` | Meadow, Pinewild, Tundra sparse | 2–5 | 1–4 | Avoids direct water; prefers partial shade in Pinewild |
| Grain Stalk | `grain_stalk` | `meadow_turf`, `river_silt`, `tended_soil` | Meadow, Wetland edge | 1–4 | 2–6 | Requires sky light ≥ 10 |
| Reedgrass | `reedgrass` | `river_silt`, `claybed`, `tended_soil` | Wetland, river edges | 4–10 | 3–9 | Requires freshwater within 3 blocks |
| Thornbrush | `thornbrush` | `rootsoil`, `dry_turf`, `loose_loam` | Pinewild, Drybrush, overgrown structures | 2–8 | 1–5 | Avoids spawn starter zone paths |
| Drygrass Tuft | `drygrass_tuft` | `dry_turf`, `pale_sand`, `loose_loam` | Drybrush, Dunes edge, Highlands dry slopes | 4–12 | 2–7 | Common fiber source in dry regions |
| Moss Carpet | `moss_carpet` | `rootsoil`, `graystone`, `white_limestone` | Pinewild, Wetland, cave mouths | 6–16 | 2–12 | Requires moisture or shade |
| Wildflower Cluster | `wildflower_cluster` | `meadow_turf`, `loose_loam` | Meadow | 3–10 | 2–8 | Decorative; light ≥ 11 |
| Dune Sage | `dune_sage` | `pale_sand`, `dry_turf` | Dunes, Drybrush | 2–7 | 1–3 | Sparse; cannot be adjacent to another Dune Sage |
| Salt Reed | `salt_reed` | `pale_sand`, `river_silt`, `claybed` | Dunes shore, Wetland brine edge | 2–6 | 2–6 | Requires brine within 4 blocks |
| Snow Lichen | `snow_lichen` | `snowcap_turf`, `graystone`, `warm_granite` | Tundra, Highlands cold | 3–9 | 1–5 | Survives under Snowpack depth ≤ 2 |
| Frost Fern | `frost_fern` | `snowcap_turf`, `rootsoil` | Tundra, cold Pinewild, Highlands | 1–5 | 1–4 | Temperature must be ≤ 2°C |
| Windroot Shrub | `windroot_shrub` | `warm_granite`, `graystone`, `meadow_turf` | Highlands | 2–7 | 1–3 | Prefers slopes and exposed columns |
| Hanging Reed | `hanging_reed` | underside of `leafmoss`, `graystone`, `white_limestone` | Wetland, Pinewild, cave mouths | 1–4 | 1–5 vertical | Requires air below support |
| Fallen Leaves | `fallen_leaves` | `rootsoil`, `meadow_turf`, `loose_loam` | Pinewild, Meadow | 4–12 | 2–10 | Cannot spawn under heavy Snowpack |

---

## 12. Groundcover algorithm

```ts
for i in 1..GROUND_COVER_SAMPLE_COUNT:
    x = chunk.minX + randomInt(seed, 0, 15);
    z = chunk.minZ + randomInt(seed + i, 0, 15);
    y = surfaceHeightAt(x, z);

    if random() > biomeProfile.groundCoverChance:
        continue;

    if structureMaskAt(x,z).contains("no_groundcover"):
        continue;

    plantRule = weightedGroundCoverChoice(biome, temperature, moisture, light);

    if validatePlantPlacement(plantRule, x, y, z):
        place plantRule.blockId at (x, y + 1, z);
```

Groundcover validation:

```ts
function validatePlantPlacement(rule, x, y, z) {
  if blockAt(x, y + 1, z).id != "air":
      return false;

  if blockAt(x, y, z).id not in rule.validSurfaces:
      return false;

  if lightAt(x, y + 1, z) < rule.minLight:
      return false;

  if currentTemperatureC < rule.minTemperatureC or currentTemperatureC > rule.maxTemperatureC:
      return false;

  if rule.requiresFluidNearby and !hasFluidWithinRadius(x, y, z, rule.fluidId, rule.fluidRadius):
      return false;

  return true;
}
```

---

## 13. Biome-specific rules

### 13.1 Meadow

Meadow vegetation should be open and readable, with scattered Crownbranch trees and visible food/fiber resources.

| Rule | Value |
|---|---|
| Tree coverage target | 8–18% canopy cover |
| Main resources | Berrybush, Grain Stalk, Branchwood, Wildflower Cluster |
| Groundcover | Wildflowers, Fallen Leaves near trees, occasional Moss Carpet near water |
| Spawn suitability | High |

Special rule:

```ts
if Meadow is selected as spawn biome:
    ensure within SPAWN_STARTER_SCAN_RADIUS:
        at least 4 Branchwood trees;
        at least 8 surface_pebbles or shingle_gravel/flinty_shingle opportunities;
        at least 4 fiber-bearing plants;
        at least 1 freshwater source within 80 blocks;
```

### 13.2 Pinewild

Pinewild should feel dense, shaded, and resource-rich but slightly harder to navigate.

| Rule | Value |
|---|---|
| Tree coverage target | 35–60% canopy cover |
| Main resources | Branchwood, Resin Knot, Leafmoss, Thornbrush, Berrybush |
| Groundcover | Moss Carpet, Fallen Leaves, Hanging Reed at wet edges |
| Navigation | Avoid fully blocking paths for more than 12 blocks in any cardinal direction |

Density thinning:

```ts
if localCanopyCover > 0.65:
    skip additional tree candidates;
```

### 13.3 Wetland

Wetland vegetation should cluster around water and clay/silt, with lots of Reedgrass and Leanbranch trees.

| Rule | Value |
|---|---|
| Tree coverage target | 12–30% canopy cover |
| Main resources | Reedgrass, Salt Reed near brine, Claybed, Berrybush |
| Groundcover | Moss Carpet, Reedgrass, Hanging Reed |
| Fluid dependency | Most vegetation requires freshwater or brine within 4 blocks |

Placement around water:

```ts
if distanceToFreshwater <= 3:
    reedgrassChance *= 2.0;
if distanceToBrine <= 4:
    saltReedChance *= 2.0;
```

### 13.4 Drybrush

Drybrush should have sparse trees, many shrubs, and enough fiber to support early survival even without Wetlands.

| Rule | Value |
|---|---|
| Tree coverage target | 4–12% canopy cover |
| Main resources | Drygrass Tuft, Dune Sage, Thornbrush, Rosycopper nearby |
| Groundcover | Drygrass Tufts in clusters, sparse Dune Sage |
| Hazard | Thornbrush appears more often but must not surround spawn |

Spawn adjustment:

```ts
if spawn biome is Drybrush:
    guarantee at least 12 Drygrass Tuft or Reedgrass-equivalent fiber plants within 64 blocks;
```

### 13.5 Dunes

Dunes should be sparse and visually open, with plant life focused near brine, freshwater pockets, and salt flats.

| Rule | Value |
|---|---|
| Tree coverage target | 0–3% canopy cover |
| Main resources | Dune Sage, Salt Reed, Brightsalt Crust, Shellgrit near shore |
| Groundcover | Very sparse; avoid visual clutter |
| Food availability | Low unless near shore or oasis |

Oasis rule:

```ts
if freshwater pocket exists in Dunes:
    within radius 10:
        increase Fanbranch tree chance ×4;
        increase Dune Sage chance ×2;
        allow Berrybush with 10% of Meadow rate;
```

### 13.6 Tundra

Tundra should be cold, open, and snowy, with hardy vegetation and low food availability.

| Rule | Value |
|---|---|
| Tree coverage target | 0–10% canopy cover |
| Main resources | Frostbranch, Snow Lichen, Frost Fern, Frostglass |
| Groundcover | Snow Lichen and Frost Fern under light snow |
| Snow behavior | Snowpack may cover small plants without immediately destroying them |

Plant burial rule:

```ts
if snowpack.depth >= 4 and plant.tags contains "small_cold_hardy":
    plant becomes dormant but not destroyed;
```

### 13.7 Highlands

Highlands should feel exposed, rocky, and windy, with small trees and tough shrubs.

| Rule | Value |
|---|---|
| Tree coverage target | 0–15% canopy cover |
| Main resources | Windroot Shrub, Windbranch, boulder clusters, Iron/Rustcore nearby |
| Groundcover | Windroot Shrub, Frost Fern at high/cold elevations, Moss Carpet in cracks |
| Slope behavior | Vegetation prefers flatter ledges and avoids cliffs |

Wind direction rule:

```ts
windbranchBendDirection = opposite(environment.windDirectionDegrees);
```

---

## 14. Sapling and tree growth rules

### 14.1 Sapling state

```ts
type SaplingState = {
  ageStage: 0 | 1 | 2 | 3 | 4;
  plantedByPlayer: boolean;
  targetVariant?: TreeVariant;
  nextGrowthCheckTick: number;
};
```

### 14.2 Growth check

```ts
if worldTimeTicks >= sapling.nextGrowthCheckTick:
    if validateSaplingGrowth(saplingPos):
        if random() < growthChance:
            sapling.ageStage += 1;
    sapling.nextGrowthCheckTick += SAPLING_GROWTH_CHECK_TICKS;
```

Growth chance:

| Condition | Modifier |
|---|---:|
| On `rootsoil` | ×1.4 |
| On `loose_loam` | ×1.0 |
| On `meadow_turf` | ×1.1 |
| On `dry_turf` | ×0.7 |
| Freshwater within 4 blocks | ×1.2 |
| Light level below 8 | ×0.4 |
| Snowpack depth 1–2 | ×0.8 |
| Snowpack depth ≥ 3 | ×0.2 |
| Emberflow within 5 blocks | ×0.0 |

When `ageStage = 4`:

```ts
variant = sapling.targetVariant ?? chooseTreeVariantFromBiome(currentBiome);
if treeTemplateFits(variant, saplingPos):
    replace sapling with generated tree;
else:
    delay next check by TICKS_PER_DAY;
```

### 14.3 Sapling drops

```ts
Leafmoss decay or tree leaf harvesting:
    5% chance to drop `branchwood_sapling`;

Sickle used on Leafmoss:
    sapling drop chance increases to 8%;
```

---

## 15. Wild regrowth rules

Wild plants use lightweight regrowth markers only when harvested.

```ts
type WildRegrowthMarker = {
  blockId: string;
  position: Vec3i;
  biomeId: string;
  harvestedAtTick: number;
  regrowAfterTicks: number;
  maxAttempts: number;
};
```

Regrowth delays:

| Plant | Regrowth Delay |
|---|---:|
| Berrybush | 2 game days |
| Grain Stalk | 3 game days |
| Reedgrass | 1 game day if near water |
| Thornbrush | 4 game days |
| Drygrass Tuft | 2 game days |
| Moss Carpet | 3 game days in wet/shade |
| Dune Sage | 5 game days |
| Salt Reed | 2 game days if near brine |
| Frost Fern | 4 game days |

Regrowth validation:

```ts
if position was replaced by player block:
    remove marker;
else if current surface and light still valid:
    restore plant block;
else if marker.maxAttempts > 0:
    marker.maxAttempts -= 1;
    marker.regrowAfterTicks += TICKS_PER_DAY;
else:
    remove marker;
```

---

## 16. Leaf decay rules

Leafmoss generated by trees should decay if disconnected from natural logs unless marked persistent.

```ts
if block.id == "leafmoss" and block.state.persistent == false:
    nearestNaturalLogDistance = floodSearchFor(
      blockId="branchwood_log",
      state.natural=true,
      maxDistance=LEAF_DECAY_DISTANCE
    );

    if nearestNaturalLogDistance == null:
        schedule leaf decay check;
```

Decay check:

```ts
if random() < 0.35:
    remove leafmoss;
    if random() < 0.05:
        drop `branchwood_sapling ×1`;
    if random() < 0.15:
        place `fallen_leaves` on solid block below if air;
```

Player-placed Leafmoss should default to:

```ts
persistent = true;
```

---

## 17. Harvest rules

Vegetation harvest should remain simple and tool-aware.

| Action | Rule |
|---|---|
| Hand harvest small plants | Allowed for tier 0 plants but drops are normal only for non-fiber plants |
| Sickle harvest | Best tool for all small plants; rolls plant drops twice and keeps better result |
| Feller harvest trees | Required for efficient Branchwood Log harvesting |
| Carver harvest Resin Knot | Required for full Resin Knot drops |
| Burning vegetation | May produce Charred Log or destroy small plants without drops |

Sickle bonus:

```ts
if tool.toolClass == "SICKLE":
    roll drop table twice;
    keep higher total item value;
```

Tree felling rule for first playable:

```ts
breaking one `branchwood_log` breaks only that block;
```

Optional advanced rule:

```ts
if Feller tier >= 3 and player holds harvest action on base log:
    perform connected-tree harvest up to maxLogsByToolTier;
```

---

## 18. Environment interactions

### 18.1 Rain and moisture

Rain increases vegetation growth only when the plant can use moisture.

| Plant Group | Rain Effect |
|---|---|
| Reedgrass, Salt Reed | Growth/regrowth chance ×1.4 during rain if near fluid |
| Berrybush, Grain Stalk | Growth chance ×1.2 during light rain; ×0.9 during heavy rain |
| Drygrass Tuft, Dune Sage | Growth chance ×0.8 during heavy rain |
| Moss Carpet, Hanging Reed | Regrowth chance ×1.5 during fog or rain |
| Snow Lichen, Frost Fern | Rain has no bonus unless precipitation is snow |

### 18.2 Snow

Snowpack can occupy the block above short vegetation if the plant allows burial.

```ts
type PlantSnowBehavior = "blocks_snow" | "buried_by_snow" | "destroyed_by_heavy_snow";
```

| Plant | Snow Behavior |
|---|---|
| Snow Lichen | `buried_by_snow` up to depth 4 |
| Frost Fern | `buried_by_snow` up to depth 2 |
| Berrybush | `blocks_snow` unless dormant |
| Drygrass Tuft | `destroyed_by_heavy_snow` at depth ≥ 5 for 3 days |
| Dune Sage | `destroyed_by_heavy_snow` |
| Moss Carpet | `buried_by_snow` up to depth 3 |

### 18.3 Lightning and fire

```ts
if lightning strikes Branchwood tree:
    replace 20–60% of connected `branchwood_log` with `charred_log`;
    remove 30–80% of nearby non-persistent `leafmoss`;
    chance to start local fire if fire system exists and rain intensity < 0.5;
```

### 18.4 Fog

Fog does not change block generation, but it may increase Moss Carpet and Hanging Reed regrowth checks if the biome is wet or shaded.

```ts
if fogDensity >= 0.5 and moistureBias != "dry":
    mossRegrowthChance *= 1.25;
```

---

## 19. Structure integration

Vegetation must respect structure masks from the Generated Structures ruleset.

| Mask Flag | Vegetation Behavior |
|---|---|
| `no_large_tree` | No trees or Windroot Shrub inside this cell |
| `no_groundcover` | No small vegetation or fallen leaves |
| `allow_groundcover` | Groundcover may be placed using structure-specific biome weighting |
| `path_clear` | Only low grass-like plants allowed; no Thornbrush |
| `protected_interior` | No vegetation unless template placed it explicitly |

Overgrown structure rule:

```ts
if structure.tags contains "vegetation_integrated":
    allow Moss Carpet, Fallen Leaves, Berrybush, and Thornbrush in defined outer mask cells;
```

---

## 20. Spawn vegetation guarantees

When a world is created, spawn selection must verify basic vegetation resources.

```ts
type SpawnVegetationRequirement = {
  requiredBranchwoodLogs: 16;
  requiredFiberDrops: 12;
  requiredFoodDrops: 4;
  requiredClearSurfaceRadius: 5;
};
```

Validation:

```ts
if countPotentialDrops("branchwood_log", radius=64) < 16:
    add Crownbranch or Scrubbranch trees near valid surfaces;

if countPotentialDrops("reed_fiber", radius=64) < 12:
    add Reedgrass, Drygrass Tuft, or Thornbrush depending on biome;

if countPotentialDrops("berry_cluster", radius=64) < 4 and biome is not Dunes:
    add 1–3 Berrybush patches;
```

Dunes exception:

```ts
if spawn biome is Dunes:
    require freshwater/brine-edge oasis within 96 blocks or reject spawn;
```

---

## 21. Creative mode vegetation tools

Creative mode can expose vegetation generation actions through World Edit Tools.

| Action | Action ID | Rule |
|---|---|---|
| Paint biome vegetation | `creative.vegetation.paint_profile` | Applies selected biome vegetation profile to a brush region |
| Place tree variant | `creative.vegetation.place_tree` | Places a selected tree template at target block |
| Grow sapling instantly | `creative.vegetation.grow_sapling` | Runs tree growth validation and places tree if valid |
| Clear vegetation | `creative.vegetation.clear` | Removes blocks tagged `plant`, `leaf`, `tree_generated`, or `groundcover` within region |
| Toggle leaf decay | `creative.vegetation.toggle_decay` | Sets persistent state for selected Leafmoss blocks |
| Regrow wild plants | `creative.vegetation.regrow_area` | Processes wild regrowth markers in selected area |

Creative vegetation placement should not consume items unless `SURVIVAL_SIMULATION` is enabled.

---


## 22. Vegetation audio/VFX hooks

Vegetation systems should expose lightweight presentation events for harvesting, weather interaction, and decay. These hooks must not perform simulation changes directly.

| Event ID | Trigger | Audio/VFX intent | Budget rule |
|---|---|---|---|
| `leaf_rustle` | Player brushes or harvests Leafmoss/groundcover. | Soft leaf rustle, tiny leaf motes. | Rate-limit per player and per vegetation cluster. |
| `timber_chip` | Branchwood Log is chopped or removed. | Wood chip cue, small rectangular chip particles. | Cap particles for connected-tree harvests. |
| `plant_harvest` | Berrybush, Reedgrass, Grain Stalk, Thornbrush, or small plant harvested. | Short pluck/snap cue, small plant fragments. | Merge repeated harvests in one area. |
| `leaf_decay` | Non-persistent Leafmoss decays. | Optional quiet leaf fall, low-priority particles. | Disabled or heavily throttled for offscreen chunks. |
| `sapling_grow` | Sapling successfully becomes a tree. | Soft growth pop, leaf swirl. | Local only after authoritative growth result. |
| `weather_leaf_drip` | Rain hits canopy or snow melts from leaves. | Drips, leaf-water particles. | Only near listener/camera. |
| `snow_shed` | Snow layer falls or melts from vegetation. | Powder puff, soft snow drop. | Suppress during heavy world-edit operations. |

Weather interaction hooks must read the authoritative Environment state. In multiplayer, clients may render local rain/snow-on-vegetation particles, but vegetation growth, decay, harvest drops, and block changes remain host-authoritative.

---

## 23. Save hooks

Vegetation save data should be sparse.

Save as normal chunk blocks:

```txt
branchwood_log
leafmoss
small vegetation blocks
resin_knot
snowpack over vegetation
charred_log after lightning/fire
```

Save as tick/state data:

```ts
type VegetationSaveState = {
  saplings: SaplingState[];
  wildRegrowthMarkers: WildRegrowthMarker[];
  pendingLeafDecayPositions: Vec3i[];
  biomeVegetationVersion: number;
};
```

Do not save deterministic generated vegetation instances unless they have special state. Normal generated trees and plants are represented by chunk block data after generation.

Migration rule:

```ts
if vegetation rules change but chunk is already generated:
    do not re-run vegetation pass over player-modified chunks;
    new rules affect only newly generated chunks unless explicit world upgrade is requested;
```

---

## 24. Example biome vegetation profile

```json
{
  "biomeId": "Pinewild",
  "treeDensityPerChunk": { "min": 8, "max": 16 },
  "shrubDensityPerChunk": { "min": 4, "max": 10 },
  "plantPatchAttemptsPerChunk": 28,
  "groundCoverChance": 0.55,
  "preferredTreeVariants": [
    { "value": "needlebranch", "weight": 8 },
    { "value": "crownbranch", "weight": 2 }
  ],
  "validTreeSurfaces": ["rootsoil", "meadow_turf", "snowcap_turf"],
  "validPlantSurfaces": ["rootsoil", "loose_loam", "graystone", "white_limestone"],
  "maxTreeSlopeDelta": 3,
  "temperatureBias": "temperate",
  "moistureBias": "wet"
}
```

---

## 25. Minimal implementation checklist

Required for first playable deeper vegetation:

```txt
BiomeVegetationProfile registry
Tree variant metadata for Branchwood Log and Leafmoss
Tree placement candidate validation
At least 4 tree variants: Crownbranch, Needlebranch, Leanbranch, Scrubbranch
Plant placement table for Berrybush, Grain Stalk, Reedgrass, Thornbrush, Drygrass Tuft, Moss Carpet
Structure mask awareness
Spawn vegetation guarantees
Sapling growth state
Wild plant regrowth markers
Leaf decay for non-persistent Leafmoss
Weather hooks for rain and snow
Vegetation audio/VFX presentation hooks
Save hooks for saplings, regrowth markers, and decay queues
```

Recommended later:

```txt
All 7 tree variants
Dune oasis generation integration
Advanced connected-tree harvesting
Seasonal vegetation color and growth modifiers
Biome-specific ambience hooks
Leaf rustle, timber chip, and plant harvest cue families
Creative vegetation brush UI
```
