# Voxel Survival and Crafting Ruleset

This document defines the canonical Blockiverse survival/crafting ruleset, including block types, terrain, resources, tools, crafting, mining, world generation, inventory behavior, and progression. Runtime implementation should replace temporary registries and world presets with the canonical IDs, schemas, and rules defined here and in the companion documents.

Assumptions:

- `1 block = 1 cubic tile`
- World height is `0–255`
- Sea level is `96`
- Chunks are `16 × 16` columns
- Game simulation runs at `20 ticks/second`


## 0. Canonical implementation direction

The ruleset-defined Blockiverse world is the source of truth for implementation. Existing temporary worlds, temporary block IDs, and simplified item/tool registries should be treated as migration inputs only, not as target gameplay vocabulary.

Implementation rules:

| Area | Canonical Rule |
|---|---|
| World registry | Runtime block, item, recipe, and tool registries should use the IDs in this ruleset and companion rulesets. |
| Existing worlds | Older saved or test worlds may be migrated through explicit ID mappings, then saved with canonical IDs. |
| Movement and block editing | Keep the refined VR locomotion, XR ray interaction, block placement, and block removal behavior, but point it at canonical world data. |
| Networking | Keep host-authoritative mutation validation and delta synchronization, but synchronize canonical world metadata, registry versions, structure state, vegetation state, and environment state. |
| Vertical slices | A development slice may implement a subset of the canonical content, but it should not introduce separate gameplay names or incompatible schemas. Missing canonical content should be marked unavailable, not replaced by a parallel vocabulary. |

### 0.1 Legacy temporary migration mapping

Use this table only for save migration, test fixture migration, or code refactors from earlier temporary registries. Player-facing UI and new saves should use the canonical IDs.

| Legacy / Temporary Term | Canonical Target | Migration Notes |
|---|---|---|
| `Loam` | `loose_loam` | Direct terrain-soil migration. |
| `Slate` | `dark_slate` or `graystone` | Prefer `dark_slate` for deeper stone and `graystone` for generic generated stone. |
| `Timber` | `branchwood_log` | If the old block represented processed lumber, migrate to `work_plank`. |
| `Leafmass` | `leafmoss` | Preserve leaf metadata when available. |
| `Clearstone` | `lumen_quartz_cluster` or `clearpane_glass` | Use `lumen_quartz_cluster` for natural crystal, `clearpane_glass` for crafted transparent blocks. |
| `Coalstone` | `embercoal_seam` | Natural resource node migration. |
| `Copperstone` | `rosycopper_bloom` | Natural resource node migration. |
| `Ironstone` | `rustcore_ore` | Natural resource node migration. |
| `Workbench` | `build_table` | Station migration. |
| `Torchbud` | `glowwick` | Early light-source migration. |
| `Storage Crate` | `storage_crate` | Container migration. |
| `Chipper` | `feller` or `carver` family | Use `feller` for wood/leaf harvesting; use `carver` for cutting/resin behavior. |
| `Pick` | `delver` family | Stone and ore harvesting. |
| `Mallet` | `mallet` | Preserve as canonical tool class. |
| `Recovery Wrap` | `field_bandage` | Healing/utility item migration. |

### 0.2 Canonical development slice rule

When implementing in stages, start with the smallest canonical slice that proves the game loop while remaining compatible with the full ruleset:

```txt
survival_terrain world preset
canonical chunk size 16
canonical block registry loader
branchwood / flint / rosycopper / bronze / ironroot / deepsteel / starforged material path
Delver, Spade, Feller, Sickle, Mallet, Carver, and Tiller tool classes
Build Table, Clay Kiln, Bellows Forge, Campfire, and Storage Crate stations
host-authoritative block mutation commands
save/version metadata from voxel_save_versioning_schema.md
```

---

## 1. Core Stat Model

### 1.1 Harvest Tiers

| Tier | Tier Name | Main Materials | Intended Progression |
|---:|---|---|---|
| 0 | Bare | Hands, simple gathering | Plants, loose soil, surface items |
| 1 | Reedwood | Branchwood tools | Basic stone, logs, soft blocks |
| 2 | Flint | Flint tools | Coal, copper, tin, granite |
| 3 | Rosycopper | Copper tools | Iron, basalt, quartz |
| 4 | Bronze | Bronze tools | Sunmetal, tougher stone |
| 5 | Ironroot | Iron tools | Deep stone, umbralite |
| 6 | Deepsteel | Deepsteel tools | Staropal, highest natural blocks |
| 7 | Starforged | Endgame tools | All breakable blocks quickly |

### 1.2 Tool Classes

| Tool Class | Game Name | Primary Use |
|---|---|---|
| `HAND` | Bare Hand | Very soft blocks and gathering |
| `DELVER` | Delver | Stone, ores, crystals |
| `SPADE` | Spade | Soil, sand, clay, snow |
| `FELLER` | Feller | Logs, planks, woody blocks |
| `SICKLE` | Sickle | Plants, fiber, crops |
| `MALLET` | Mallet | Crafted stone/brick blocks, salvage |
| `CARVER` | Carver | Cutting, food prep, hide, vines |
| `TILLER` | Tiller | Farming soil preparation |

### 1.3 Suggested Block Schema

```ts
type BlockDef = {
  id: string;
  name: string;
  category: "terrain" | "stone" | "resource_node" | "plant" | "crafted" | "fluid";
  hardness: number;              // Used by mining formula
  preferredTool: ToolClass | null;
  minTier: number;
  stackMax: number;
  drops: DropRule[];
  tags: string[];
  placement?: PlacementRule;
};

type DropRule = {
  itemId: string;
  min: number;
  max: number;
  chance: number;                 // 0.0 to 1.0
};
```

---

## 2. Terrain and Block Catalog

Blocks stack to `99` unless otherwise noted.

| Name | ID | Description | Hardness | Tool / Tier | Drops |
|---|---|---|---:|---|---|
| Air | `air` | Empty space. Not collectible. | — | — | None |
| Worldroot | `worldroot` | Unbreakable bottom crust. Prevents falling out of the world. | ∞ | — | None |
| Deepmantle | `deepmantle` | Dense lower-world stone. Hosts rare ores. | 6.0 | Delver / 5 | `deepmantle_rubble ×1` |
| Graystone | `graystone` | Common underground stone. Main building stone. | 2.0 | Delver / 1 | `stone_rubble ×1` |
| Dark Slate | `dark_slate` | Layered dark stone. More common underground. | 2.4 | Delver / 1 | `slate_rubble ×1` |
| Warm Granite | `warm_granite` | Tough speckled stone. Common in highlands. | 2.8 | Delver / 2 | `granite_rubble ×1` |
| White Limestone | `white_limestone` | Pale stone found near caves and coastlines. | 2.0 | Delver / 1 | `limestone_rubble ×1` |
| Black Basalt | `black_basalt` | Hard volcanic stone near emberflow pockets. | 3.8 | Delver / 3 | `basalt_rubble ×1` |
| Meadow Turf | `meadow_turf` | Grassy surface soil. Can spread to nearby loose loam in light. | 0.7 | Spade / 0 | `loose_loam ×1`, 15% `meadow_seed ×1` |
| Dry Turf | `dry_turf` | Dry grass surface block for warm biomes. | 0.7 | Spade / 0 | `loose_loam ×1`, 10% `drygrass_seed ×1` |
| Snowcap Turf | `snowcap_turf` | Frozen grassy soil. Surface block in cold regions. | 0.8 | Spade / 0 | `loose_loam ×1`, 25% `snow_clump ×1` |
| Loose Loam | `loose_loam` | Basic dirt-like soil. Used for farming. | 0.5 | Spade / 0 | `loose_loam ×1` |
| Rootsoil | `rootsoil` | Dark fertile soil beneath forest surfaces. | 0.8 | Spade / 0 | `rootsoil ×1`, 10% `reed_fiber ×1` |
| Claybed | `claybed` | Moist clay deposit. Used for bricks and kilns. | 0.9 | Spade / 0 | `clay_lump ×2–4` |
| River Silt | `river_silt` | Soft sediment near rivers and wetlands. | 0.4 | Spade / 0 | `river_silt ×1`, 20% `clay_lump ×1` |
| Pale Sand | `pale_sand` | Loose sand from beaches and dunes. | 0.4 | Spade / 0 | `pale_sand ×1` |
| Shingle Gravel | `shingle_gravel` | Loose gravel. Early source of flint. | 0.6 | Spade / 0 | `shingle_gravel ×1`, 25% `flint_shard ×1` |
| Snowpack | `snowpack` | Soft snow layer. Can be compressed into snow blocks. | 0.2 | Spade / 0 | `snow_clump ×1–3` |
| Frostglass | `frostglass` | Blue natural ice. Slippery and translucent. | 0.5 | Delver / 1 | 40% `frost_shard ×1` |
| Branchwood Log | `branchwood_log` | Common tree trunk. Core early crafting material. | 1.6 | Feller / 0 | `branchwood_log ×1` |
| Leafmoss | `leafmoss` | Soft leafy canopy block. | 0.2 | Sickle / 0 | `leafmoss ×1`, 20% `meadow_seed ×1` |
| Thornbrush | `thornbrush` | Hazardous shrub. Damages entities walking through it. | 0.3 | Sickle / 0 | `reed_fiber ×1`, 25% `resin_blob ×1` |
| Reedgrass | `reedgrass` | Tall wetland plant. Source of fiber. | 0.1 | Sickle / 0 | `reed_fiber ×1–3` |
| Freshwater | `freshwater` | Drinkable fluid. Flows up to 8 blocks horizontally. | — | Bucket | `freshwater_bucket` |
| Brine | `brine` | Salty water. Can be boiled into brightsalt. | — | Bucket | `brine_bucket` |
| Emberflow | `emberflow` | Hot damaging fluid. Flows slowly. Creates basalt when cooled. | — | Special | None |
| Work Plank | `work_plank` | Basic crafted wood block. | 1.2 | Feller / 0 | `work_plank ×1` |
| Cutstone Block | `cutstone_block` | Refined stone building block. | 2.8 | Mallet / 1 | `cutstone_block ×1` |
| Fired Brick | `fired_brick_block` | Strong clay brick block. | 2.6 | Mallet / 1 | `fired_brick_block ×1` |
| Clearpane Glass | `clearpane_glass` | Transparent glass block. Fragile. | 0.4 | Mallet / 1 | 50% `glass_shard ×1–2` |

---

## 3. Resource Nodes and Raw Resources

Resource nodes are blocks placed during generation. Most require a `Delver`.

| Node Name | ID | Resource Dropped | Description / Use | Hardness | Tool / Tier | Drop Amount |
|---|---|---|---|---:|---|---|
| Surface Pebbles | `surface_pebbles` | `stone_pebble` | Loose stones for first tools and campfires. | 0.2 | Hand / 0 | 1–2 |
| Flinty Shingle | `flinty_shingle` | `flint_shard` | Sharp stone used for early tools. | 0.6 | Spade / 0 | 1–2 |
| Embercoal Seam | `embercoal_seam` | `embercoal` | Fuel for torches, kilns, and forges. | 2.5 | Delver / 2 | 1–3 |
| Rosycopper Bloom | `rosycopper_bloom` | `raw_rosycopper` | Copper-bearing ore. First metal tier. | 3.0 | Delver / 2 | 1–3 |
| Paletin Thread | `paletin_thread` | `raw_paletin` | Tin-like ore used with copper to make bronze. | 3.0 | Delver / 2 | 1–2 |
| Rustcore Ore | `rustcore_ore` | `raw_rustcore` | Iron-bearing ore. Midgame tool material. | 3.8 | Delver / 3 | 1–2 |
| Sunmetal Fleck | `sunmetal_fleck` | `raw_sunmetal` | Rare decorative and electrical-conductive metal. | 4.8 | Delver / 4 | 1 |
| Lumen Quartz Cluster | `lumen_quartz_cluster` | `lumen_crystal` | Glowing crystal for lamps and advanced crafting. | 3.5 | Delver / 3 | 1–3 |
| Niterstone Pocket | `niterstone_pocket` | `spark_niter` | Powder ingredient for flares and blasting charges. | 2.4 | Delver / 2 | 2–5 |
| Brightsalt Crust | `brightsalt_crust` | `brightsalt` | Salt deposit for food preservation and recipes. | 0.5 | Spade / 0 | 2–5 |
| Shellgrit Bed | `shellgrit_bed` | `shellgrit` | Crushed shell mineral for glass, lime, and fertilizer. | 0.6 | Spade / 0 | 2–4 |
| Resin Knot | `resin_knot` | `resin_blob` | Sticky tree resin for torches, glue, and bandages. | 0.4 | Carver / 0 | 1–2 |
| Berrybush | `berrybush` | `berry_cluster` | Early food source. Regrows after 2 game days. | 0.2 | Hand / 0 | 1–3 |
| Grain Stalk | `grain_stalk` | `grain_bundle` | Farm crop used for rations. | 0.2 | Sickle / 0 | 1–2 |
| Umbralite Node | `umbralite_node` | `raw_umbralite` | Deep ore used to make Deepsteel. | 6.5 | Delver / 5 | 1 |
| Staropal Geode | `staropal_geode` | `staropal_shard` | Very rare endgame crystal. | 8.0 | Delver / 6 | 1 |

---

## 4. Resource Placement Rules

Use resource placement after terrain, caves, and fluids are generated.

```ts
attempts = floor(ratePerChunk * biomeModifier)
if random() < fractionalPart(ratePerChunk * biomeModifier):
    attempts += 1

for each attempt:
    center = random position in chunk with Y in allowed range
    if host block is valid:
        place vein using random-walk replacement
```

### 4.1 Vein Placement Table

| Resource Node | Valid Host Blocks | Y Range | Rate / Chunk | Vein or Patch Size | Biome Modifiers |
|---|---|---:|---:|---:|---|
| `surface_pebbles` | Surface loam, turf, silt, gravel | Surface | 14 | 1–3 | Highlands ×1.5, Dunes ×0.7 |
| `flinty_shingle` | Shingle gravel, river silt | Surface to 120 | 8 | 2–8 | Rivers/Wetlands ×1.6, Dunes ×1.2 |
| `embercoal_seam` | Graystone, dark slate | 35–135 | 10 | 4–16 | Pinewild ×1.2, Tundra ×1.3 |
| `rosycopper_bloom` | Graystone, granite, limestone | 45–150 | 8 | 3–10 | Drybrush ×1.4, Dunes ×1.2 |
| `paletin_thread` | Limestone, dark slate | 25–115 | 4 | 2–7 | Wetlands ×1.7, Highlands ×1.2 |
| `rustcore_ore` | Dark slate, basalt, deepmantle | 15–95 | 6 | 3–8 | Highlands ×1.5 |
| `sunmetal_fleck` | Granite, basalt | 10–65 | 1.2 | 1–4 | Dunes ×1.8, Drybrush ×1.3 |
| `lumen_quartz_cluster` | Limestone, graystone, cave walls | 15–120 | 3 | 2–6 | Caves exposed to air ×2.0 |
| `niterstone_pocket` | Limestone, dry stone | 40–130 | 3 | 2–6 | Dunes ×1.5, Drybrush ×1.4 |
| `brightsalt_crust` | Pale sand, brine shore, dry lakebed | Surface to 105 | 5 | 3–12 | Dunes ×2.0, Coast ×1.4 |
| `shellgrit_bed` | Beach sand, limestone, river silt | Surface to 105 | 4 | 3–10 | Coast ×2.0, Wetlands ×1.3 |
| `resin_knot` | Branchwood log | Tree surface | 0.15 per log | 1 | Pinewild ×1.5 |
| `berrybush` | Meadow turf, rootsoil | Surface | 2 | 1–4 | Meadow ×1.6, Pinewild ×1.2 |
| `grain_stalk` | Meadow turf, river silt | Surface | 1.5 | 2–6 | Meadow ×1.4, Wetlands ×1.3 |
| `umbralite_node` | Deepmantle, basalt | 5–35 | 0.8 | 1–4 | Near emberflow ×1.8 |
| `staropal_geode` | Deepmantle | 2–22 | 0.18 | 1–2 | Cave wall exposure ×2.5 |

---

## 5. World Generation Rules

### 5.1 Chunk and Height Generation

World constants:

```ts
CHUNK_SIZE = 16;
WORLD_MIN_Y = 0;
WORLD_MAX_Y = 255;
SEA_LEVEL = 96;
BEDROCK_TOP_Y = 3;
```

Height formula:

```ts
continent = noise2D(seed, x * 0.002, z * 0.002);   // large shapes
hills     = noise2D(seed, x * 0.015, z * 0.015);   // regional terrain
detail    = noise2D(seed, x * 0.060, z * 0.060);   // small bumps

height = round(96 + continent * 42 + hills * 18 + detail * 5);
height = clamp(height, 40, 190);
```

### 5.2 Biome Selection

Generate two extra noise values:

```ts
temperature = noise2D(seed + 11, x * 0.004, z * 0.004);
moisture    = noise2D(seed + 23, x * 0.004, z * 0.004);

temperature -= max(0, height - 120) * 0.006; // higher places are colder
```

| Biome | Conditions | Surface Block | Notes |
|---|---|---|---|
| Meadow | Moderate temp, moderate moisture | `meadow_turf` | Balanced starter biome |
| Pinewild | Cool, moist | `rootsoil` with `leafmoss` trees | More trees and resin |
| Wetland | High moisture, near sea level | `river_silt`, `claybed` | More clay, reeds, tin |
| Drybrush | Warm, low moisture | `dry_turf` | More copper and niter |
| Dunes | Hot, very dry | `pale_sand` | More salt, sunmetal, sparse wood |
| Tundra | Very cold | `snowcap_turf`, `snowpack` | More coal, frostglass |
| Highlands | Height above 130 | Exposed stone, `warm_granite` | More iron and quartz |

### 5.3 Terrain Layering

For each `(x, z)` column:

```ts
for y = 0..3:
    place worldroot

for y = 4..height:
    if y <= 24:
        place deepmantle
    else if y < height - subsoilDepth:
        place baseStoneForBiome()
    else:
        place subsoilForBiome()

place surfaceBlockForBiome() at y = height
```

Suggested `subsoilDepth`:

```ts
subsoilDepth = randomInt(3, 6);
```

Stone selection:

| Condition | Stone Choice |
|---|---|
| `y < 35` | Mostly `deepmantle`, some `black_basalt` |
| `35 <= y < 70` | `dark_slate`, `graystone`, rare `black_basalt` |
| `70 <= y < 130` | Mostly `graystone`, some `limestone` |
| Highlands | More `warm_granite` |
| Caves near coast | More `white_limestone` |

### 5.4 Water and Fluids

After solid terrain:

```ts
if height + 1 < SEA_LEVEL:
    fill from height + 1 to SEA_LEVEL - 1 with freshwater
```

The water table tops out one block below sea level so shorelines keep a one-block step down
from dry land — a readable shore edge in VR instead of water sitting flush with the beach.

Use `brine` instead of `freshwater` in dune coastlines, salt flats, and enclosed dry basins.

Emberflow placement:

```ts
if y < 18 and caveAirNearby and random() < 0.035:
    place emberflow source pocket
```

Fluid behavior:

| Fluid | Flow Distance | Tick Rate | Special Rule |
|---|---:|---:|---|
| Freshwater | 8 blocks | Every 5 ticks | Turns emberflow source contact into `black_basalt` |
| Brine | 6 blocks | Every 6 ticks | Can be boiled into `brightsalt` |
| Emberflow | 4 blocks | Every 12 ticks | Deals heat damage; ignites wood-adjacent blocks |

### 5.5 Cave Generation

Use 3D noise plus random tunnels.

```ts
caveValue = noise3D(seed + 99, x * 0.045, y * 0.045, z * 0.045);

if y < 130 and caveValue > 0.62:
    replace solid block with air
```

Additional rules:

| Rule | Value |
|---|---|
| Caves below sea level | 35% chance to flood with freshwater or brine |
| Caves below `y=18` | 10% chance of emberflow pools |
| Cave exposure bonus | Lumen quartz and staropal get higher placement chance on exposed cave walls |
| Minimum cave roof thickness | Do not carve within 4 blocks of surface unless biome is Highlands |

---

## 6. Mining Rules

### 6.1 Mining Formula

```ts
function getMineTimeSeconds(block, tool) {
    if (block.hardness === Infinity) return Infinity;

    let toolClassMatches = block.preferredTool === tool.toolClass;
    let tierMatches = tool.tier >= block.minTier;

    let speed = tool.speed;

    if (!toolClassMatches) speed *= 0.25;
    if (!tierMatches) speed *= 0.15;

    return block.hardness / Math.max(speed, 0.05);
}
```

Convert to ticks:

```ts
mineTicks = ceil(getMineTimeSeconds(block, tool) * 20);
```

### 6.2 Harvest Success

```ts
canHarvest =
    block.preferredTool === null ||
    block.preferredTool === tool.toolClass ||
    block.minTier === 0;

canHarvest = canHarvest && tool.tier >= block.minTier;
```

Rules:

| Situation | Result |
|---|---|
| Correct tool and sufficient tier | Full drops |
| Correct tool but insufficient tier | Block breaks very slowly, resource nodes drop nothing |
| Wrong tool but sufficient tier | Block breaks slowly; terrain drops normally; resource nodes drop nothing |
| Bare hand on soft block | Allowed if `minTier = 0` |
| Bare hand on stone/ore | Block may break very slowly but drops nothing |
| Mining interrupted for 1.25 seconds | Progress resets |
| Tool reaches 0 durability | Tool breaks immediately after the action |

### 6.3 Durability Cost

| Block Category | Durability Cost |
|---|---:|
| Plants, leaves, snow | 1 |
| Soil, sand, clay, gravel | 1 |
| Logs, planks | 1 |
| Stone | 1 |
| Common ore | 2 |
| Deep ore | 3 |
| Crafted brick/stone blocks | 1 |
| Wrong tool used | +1 extra |
| Tool tier below required tier | +2 extra |

Bare hands have no durability.

### 6.4 Drop Modifiers

Default drop roll:

```ts
for each dropRule in block.drops:
    if random() <= dropRule.chance:
        amount = randomInt(dropRule.min, dropRule.max)
        spawnItem(dropRule.itemId, amount)
```

Optional tool yield bonus:

| Tool Tier | Bonus |
|---:|---|
| 0–3 | No bonus |
| 4 | 10% chance for +1 extra raw ore |
| 5 | 15% chance for +1 extra raw ore |
| 6 | 20% chance for +1 extra raw ore |
| 7 | 25% chance for +1 extra raw ore or crystal |

---

## 7. Tool System

### 7.1 Tool Material Stats

| Material Name | Tier | Head Ingredient | Speed | Base Durability | Notes |
|---|---:|---|---:|---:|---|
| Reedwood | 1 | `work_plank` | 0.9 | 48 | Cheap, weak, cannot mine ores |
| Knapped Flint | 2 | `flint_shard` | 1.5 | 90 | First real mining tier |
| Rosycopper | 3 | `rosycopper_bar` | 2.2 | 160 | Mines iron and quartz |
| Bronze | 4 | `bronze_bar` | 3.0 | 300 | Strong midgame material |
| Ironroot | 5 | `ironroot_bar` | 4.1 | 550 | Mines deepmantle and umbralite |
| Deepsteel | 6 | `deepsteel_bar` | 5.6 | 1000 | Mines staropal geodes |
| Starforged | 7 | `starforged_core` | 7.5 | 1800 | Endgame tool material |

### 7.2 Tool Class Stats

Final tool speed:

```ts
tool.speed = material.speed * class.speedMultiplier;
tool.maxDurability = round(material.baseDurability * class.durabilityMultiplier);
tool.tier = material.tier;
```

| Tool Name | Class | Speed Multiplier | Durability Multiplier | Main Uses |
|---|---|---:|---:|---|
| Delver | `DELVER` | 1.00 | 1.00 | Stone, ore, crystal |
| Spade | `SPADE` | 1.25 | 0.80 | Soil, sand, clay, snow |
| Feller | `FELLER` | 1.10 | 0.90 | Logs, planks, woody blocks |
| Sickle | `SICKLE` | 1.60 | 0.70 | Plants, fiber, crops |
| Mallet | `MALLET` | 0.85 | 1.20 | Crafted blocks, salvage |
| Carver | `CARVER` | 0.60 | 0.60 | Food prep, resin, hide, vines |
| Tiller | `TILLER` | 0.70 | 0.80 | Farming and soil conversion |

### 7.3 Tool Naming

Use this pattern:

```txt
<Material> <ToolName>
```

Examples:

| Item Name | ID | Class | Tier |
|---|---|---|---:|
| Knapped Flint Delver | `flint_delver` | Delver | 2 |
| Rosycopper Spade | `rosycopper_spade` | Spade | 3 |
| Bronze Feller | `bronze_feller` | Feller | 4 |
| Ironroot Mallet | `ironroot_mallet` | Mallet | 5 |
| Deepsteel Sickle | `deepsteel_sickle` | Sickle | 6 |
| Starforged Delver | `starforged_delver` | Delver | 7 |

### 7.4 Special Tool Actions

| Tool | Action | Logic |
|---|---|---|
| Tiller | Create Tended Soil | Use on `loose_loam`, `rootsoil`, or `river_silt`; requires freshwater within 4 blocks or consumes 1 `clean_water_flask` (the emptied `water_flask` returns) |
| Sickle | Efficient harvesting | Plant drops roll twice and keep the better result |
| Mallet | Salvage crafted blocks | Crafted blocks drop themselves instead of rubble |
| Feller | Strip log | Optional alternate action turns `branchwood_log` into `smooth_branchwood` |
| Carver | Collect resin | Required for full `resin_knot` drops |
| Delver | Mine ore | Required for all mineral resource nodes |

---

## 8. Crafting System

### 8.1 Crafting Stations

| Station | ID | Purpose | Inventory Slots |
|---|---|---|---:|
| Handcraft | `handcraft` | Basic recipes available anywhere | None |
| Build Table | `build_table` | Main 3×3-style crafting station | None |
| Clay Kiln | `clay_kiln` | Smelting clay, glass, copper, tin | Input 1, Fuel 1, Output 1 |
| Bellows Forge | `bellows_forge` | Alloys, iron, advanced tools | Input 3, Fuel 1, Output 1 |
| Prep Board | `prep_board` | Food, bandages, small utility items | Input 3, Output 1 |
| Mend Bench | `mend_bench` | Repair tools | Tool 1, Material 1, Output 1 |

### 8.2 Fuel Values

| Fuel Item | ID | Burn Time |
|---|---|---:|
| Work Plank | `work_plank` | 3 sec |
| Branchwood Log | `branchwood_log` | 10 sec |
| Resin Blob | `resin_blob` | 20 sec |
| Embercoal | `embercoal` | 80 sec |
| Embercoal Block | `embercoal_block` | 720 sec |

Kiln consumes fuel normally. Forge consumes fuel at `2×` speed.

---

## 9. Crafting Recipes

### 9.1 Basic Recipes

| Output | Station | Ingredients | Purpose |
|---|---|---|---|
| `work_plank ×6` | Handcraft | `branchwood_log ×1` | Basic building and crafting |
| `stout_pole ×4` | Handcraft | `work_plank ×2` | Tool handles, ladders, torches |
| `fiber_cord ×2` | Handcraft | `reed_fiber ×3` | Binding material |
| `stone_rubble ×4` | Handcraft | `graystone ×1` or `cutstone_block ×1` | Converts stone blocks to raw material |
| `glowwick ×4` | Handcraft | `stout_pole ×1`, `embercoal ×1`, `fiber_cord ×1` | Placeable light |
| `campfire ×1` | Handcraft | `stone_pebble ×4`, `stout_pole ×3`, `resin_blob ×1` | Cooking and light |
| `reed_basket ×1` | Handcraft | `reed_fiber ×8`, `fiber_cord ×2` | Portable 8-slot container |
| `flint_carver ×1` | Handcraft | `flint_shard ×1`, `stout_pole ×1`, `fiber_cord ×1` | Early cutting tool |

### 9.2 Build Table Recipes

| Output | Station | Ingredients | Purpose |
|---|---|---|---|
| `build_table ×1` | Handcraft | `work_plank ×8`, `fiber_cord ×2` | Unlocks advanced crafting |
| `storage_crate ×1` | Build Table | `work_plank ×12`, `stout_pole ×2` | 24-slot storage |
| `clay_kiln ×1` | Build Table | `clay_lump ×12`, `stone_rubble ×8`, `embercoal ×2` | Smelting station |
| `prep_board ×1` | Build Table | `work_plank ×4`, `flint_shard ×1` | Food and bandage station |
| `mend_bench ×1` | Build Table | `work_plank ×10`, `stone_rubble ×6`, `resin_blob ×2` | Repairs tools |
| `rope_ladder ×6` | Build Table | `stout_pole ×2`, `fiber_cord ×4` | Climbing |
| `doorleaf ×1` | Build Table | `work_plank ×6`, `fiber_cord ×1` | Door block |
| `trap_hatch ×2` | Build Table | `work_plank ×6`, `resin_blob ×1` | Horizontal door |
| `bedroll ×1` | Build Table | `leafmoss ×6`, `reed_fiber ×8`, `fiber_cord ×4` | Sets respawn point |
| `wayflag ×3` | Build Table | `reed_fiber ×2`, `fiber_cord ×1`, `stout_pole ×1` | Map marker |
| `cutstone_block ×4` | Build Table | `stone_rubble ×8` | Building block |
| `fired_brick_block ×4` | Build Table | `fired_brick ×8` | Strong building block |

### 9.3 Kiln Recipes

| Output | Station | Ingredients | Time | Purpose |
|---|---|---|---:|---|
| `fired_brick ×1` | Clay Kiln | `clay_lump ×2` | 8 sec | Brick crafting |
| `glass_shard ×2` | Clay Kiln | `pale_sand ×2` | 8 sec | Glass component |
| `clearpane_glass ×1` | Clay Kiln | `glass_shard ×4`, `shellgrit ×1` | 10 sec | Transparent block |
| `rosycopper_bar ×1` | Clay Kiln | `raw_rosycopper ×2` | 12 sec | Copper tools |
| `paletin_bar ×1` | Clay Kiln | `raw_paletin ×2` | 12 sec | Bronze ingredient |
| `lumen_dust ×2` | Clay Kiln | `lumen_crystal ×1` | 6 sec | Lamps and advanced recipes |
| `water_flask ×1` | Clay Kiln | `glass_shard ×3`, `resin_blob ×1` | 8 sec | Empty drink flask (fill at a Campfire) |

Brine boiling is an instant Campfire craft (see §9.6), not a timed kiln run: timed stations grant
only their primary output, and boiling must hand the emptied bucket back.

### 9.4 Forge Recipes

| Output | Station | Ingredients | Time | Purpose |
|---|---|---|---:|---|
| `bellows_forge ×1` | Build Table | `fired_brick ×16`, `rosycopper_bar ×4`, `fiber_cord ×4`, `work_plank ×4` | — | Advanced metal station |
| `bronze_bar ×4` | Bellows Forge | `rosycopper_bar ×3`, `paletin_bar ×1` | 16 sec | Bronze tools |
| `ironroot_bar ×1` | Bellows Forge | `raw_rustcore ×2`, `embercoal ×1` | 16 sec | Iron tools |
| `sunmetal_bar ×1` | Bellows Forge | `raw_sunmetal ×2`, `embercoal ×1` | 18 sec | Advanced utility items |
| `deepsteel_bar ×1` | Bellows Forge | `raw_umbralite ×2`, `ironroot_bar ×1`, `lumen_dust ×1` | 24 sec | Deep tools |
| `starforged_core ×1` | Bellows Forge | `staropal_shard ×4`, `deepsteel_bar ×2`, `lumen_crystal ×2` | 30 sec | Endgame tools |

### 9.5 Generic Tool Recipes

For tool recipes, substitute the head material based on tier.

| Tool | Ingredients | Station |
|---|---|---|
| Reedwood Delver | `work_plank ×3`, `stout_pole ×2` | Build Table |
| Reedwood Spade | `work_plank ×1`, `stout_pole ×2` | Build Table |
| Reedwood Feller | `work_plank ×3`, `stout_pole ×2` | Build Table |
| Flint Delver | `flint_shard ×3`, `stout_pole ×2`, `fiber_cord ×1` | Build Table |
| Flint Spade | `flint_shard ×1`, `stout_pole ×2`, `fiber_cord ×1` | Build Table |
| Flint Feller | `flint_shard ×3`, `stout_pole ×2`, `fiber_cord ×1` | Build Table |
| Metal Delver | `<bar> ×3`, `stout_pole ×2` | Bellows Forge |
| Metal Spade | `<bar> ×1`, `stout_pole ×2` | Bellows Forge |
| Metal Feller | `<bar> ×3`, `stout_pole ×2` | Bellows Forge |
| Metal Sickle | `<bar> ×2`, `stout_pole ×1`, `fiber_cord ×1` | Bellows Forge |
| Metal Mallet | `<bar> ×4`, `stout_pole ×2` | Bellows Forge |
| Metal Tiller | `<bar> ×2`, `stout_pole ×2` | Bellows Forge |
| Metal Carver | `<bar> ×1`, `stout_pole ×1`, `fiber_cord ×1` | Bellows Forge |
| Starforged Tool | `starforged_core ×1`, `deepsteel_bar ×2`, `stout_pole ×2` | Bellows Forge |

Valid `<bar>` values:

```txt
rosycopper_bar
bronze_bar
ironroot_bar
deepsteel_bar
```

### 9.6 Utility and Survival Items

| Output | Station | Ingredients | Use |
|---|---|---|---|
| `lumen_lamp ×2` | Build Table | `lumen_crystal ×1`, `glass_shard ×2`, `sunmetal_bar ×1` | Strong permanent light |
| `clean_water_flask ×1` | Campfire | `water_flask ×1`, `freshwater_bucket ×1` | Restores thirst and stamina; returns `empty_bucket` |
| `brightsalt ×3` | Campfire | `brine_bucket ×1` | Boiled brine; returns `empty_bucket` |
| `trail_ration ×2` | Prep Board | `grain_bundle ×2`, `berry_cluster ×2`, `brightsalt ×1` | Long-lasting food |
| `berry_mash ×1` | Prep Board | `berry_cluster ×3` | Quick food |
| `field_bandage ×2` | Prep Board | `reed_fiber ×4`, `resin_blob ×1` | Slow healing item |
| `spark_flare ×3` | Build Table | `spark_niter ×1`, `reed_fiber ×1`, `embercoal ×1` | Temporary bright light |
| `small_blast_charge ×1` | Bellows Forge | `spark_niter ×4`, `fiber_cord ×2`, `clay_lump ×2` | Breaks weak stone in radius 2 |
| `empty_bucket ×1` | Build Table | `rosycopper_bar ×3` | Fluid container |
| `freshwater_bucket ×1` | Fill action | `empty_bucket ×1` on freshwater | Carries freshwater |
| `brine_bucket ×1` | Fill action | `empty_bucket ×1` on brine | Carries brine |

Buckets are only crafted empty; filled buckets come from the fill action on a fluid source.
Pouring a filled bucket places its source block and hands the empty bucket back.

---

## 10. Inventory Rules

### 10.1 Player Inventory

| Inventory Area | Slots | Notes |
|---|---:|---|
| Hotbar | 10 | Active quick-use slots |
| Backpack | 30 | General inventory |
| Equipment | 4 | Optional: head, body, charm, satchel |
| Cursor / Held Stack | 1 | For UI movement and crafting |

Default total: `40` regular item slots.

### 10.2 Stack Sizes

| Item Type | Stack Max |
|---|---:|
| Terrain blocks | 99 |
| Crafted blocks | 99 |
| Pebbles, fiber, sticks, planks | 99 |
| Raw ore | 50 |
| Metal bars | 50 |
| Crystals | 30 |
| Food | 20 |
| Buckets and fluid containers | 1 |
| Tools | 1 |
| Stations | 10 |
| Storage containers | 1 if filled, 10 if empty |

### 10.3 Stack Merge Rules

Two stacks can merge only when:

```ts
same itemId
same metadata
same durability state, if applicable
same fluid contents, if applicable
same container contents hash, if applicable
```

Examples:

| Item A | Item B | Can Stack? |
|---|---|---|
| `loose_loam ×20` | `loose_loam ×40` | Yes |
| `flint_delver` at 90 durability | `flint_delver` at 30 durability | No |
| Empty bucket | Empty bucket | Yes, up to stack limit |
| Freshwater bucket | Brine bucket | No |
| Empty crate | Empty crate | Yes |
| Filled crate | Filled crate | No |

### 10.4 Pickup Rules

```ts
pickupRadius = 2.5 blocks;
```

When picking up items:

1. Merge with existing partial stacks first.
2. Then place into the first empty hotbar slot.
3. Then place into the first empty backpack slot.
4. If no space exists, leave the item in the world.

Dropped item rules:

| Rule | Value |
|---|---:|
| Ground item despawn time | 10 minutes in loaded chunks |
| Recently dropped by player | Cannot be picked up by another player for 3 seconds |
| Stack merge radius | 1.25 blocks |
| Max merged ground stack | Same as item stack max |

### 10.5 Container Rules

| Container | ID | Slots | Special Rule |
|---|---|---:|---|
| Reed Basket | `reed_basket` | 8 | Can be carried while filled |
| Storage Crate | `storage_crate` | 24 | Drops contents when broken |
| Deep Locker | `deep_locker` | 48 | Requires `ironroot_bar ×6`; keeps contents when moved only with Mallet tier 5+ |
| Tool Rack | `tool_rack` | 6 | Holds tools only |
| Pantry Jar | `pantry_jar` | 12 | Food lasts 2× longer inside |

### 10.6 Crafting Inventory Consumption

When crafting:

```ts
consume from smallest valid stacks first
preserve hotbar slots when possible
do not consume locked slots
return container items after consuming their contents
```

Example: crafting with a `freshwater_bucket` consumes the water but returns `empty_bucket`.

### 10.7 Durability and Repair

Repair formula:

```ts
repairAmount = round(tool.maxDurability * 0.25);
materialCost = 1 matching head material;
```

Repair rules:

| Tool Material | Repair Ingredient |
|---|---|
| Reedwood | `work_plank` |
| Flint | `flint_shard` |
| Rosycopper | `rosycopper_bar` |
| Bronze | `bronze_bar` |
| Ironroot | `ironroot_bar` |
| Deepsteel | `deepsteel_bar` |
| Starforged | `starforged_core` |

At the Mend Bench:

```ts
newDurability = min(maxDurability, currentDurability + repairAmount)
```

Optional balancing rule: each repair increases future repair cost by `+1` material after the third repair.

---

## 11. Farming and Regrowth Rules

### 11.1 Tended Soil

A `Tiller` can convert eligible soil into `tended_soil`.

Eligible blocks:

```txt
loose_loam
rootsoil
river_silt
```

Conversion requirements:

```ts
hasWaterNearby = any freshwater block within 4 horizontal blocks and 1 vertical block
```

If no water is nearby, the action consumes `clean_water_flask ×1` (the emptied `water_flask` returns).

### 11.2 Crop Growth

Each planted crop has growth stages.

```ts
growthTickInterval = 60 seconds;
```

At each growth tick:

```ts
if lightLevel >= crop.minLight and soilIsMoist:
    growthChance = crop.baseGrowthChance * biomeModifier
else:
    growthChance = crop.baseGrowthChance * 0.25

if random() < growthChance:
    crop.stage += 1
```

| Crop | Seed Item | Stages | Base Growth Chance | Harvest |
|---|---|---:|---:|---|
| Meadow Grain | `meadow_seed` | 5 | 0.35 | `grain_bundle ×1–3` |
| Drygrass Grain | `drygrass_seed` | 5 | 0.28 | `grain_bundle ×1–2`, 20% `brightsalt ×1` in Dunes |
| Reedgrass | `reed_cutting` | 4 | 0.40 near water | `reed_fiber ×2–5` |
| Berrybush | `berry_seed` | 6 | 0.22 | `berry_cluster ×2–4` |

---

## 12. Light and Survival Items

### 12.1 Light Levels

| Light Source | ID | Light Level | Duration |
|---|---|---:|---:|
| Glowwick | `glowwick` | 9 | Permanent unless waterlogged |
| Campfire | `campfire` | 12 | Requires fuel |
| Lumen Lamp | `lumen_lamp` | 14 | Permanent |
| Spark Flare | `spark_flare` | 15 | 45 seconds |
| Emberflow | `emberflow` | 10 | Permanent fluid light |

### 12.2 Campfire Rules

Campfire has:

```ts
fuelSlot: 1
cookSlot: 1
outputSlot: 1
```

Campfire actions:

| Input | Output | Time |
|---|---|---:|
| `berry_cluster ×3` | `berry_mash ×1` | 6 sec |
| `grain_bundle ×2` | `flatbread ×1` | 10 sec |
| `raw_morsel ×1` | `cooked_morsel ×1` | 8 sec |

Clean water comes from the §9.6 Campfire craft (`water_flask` + `freshwater_bucket`, one flask
per craft with the bucket returned) — there is no batch purification recipe.

---

## 13. Progression Path

A simple intended progression:

1. Gather `surface_pebbles`, `reed_fiber`, `branchwood_log`, and `flint_shard`.
2. Craft `work_plank`, `stout_pole`, `fiber_cord`, and basic Flint tools.
3. Mine `embercoal_seam`, `rosycopper_bloom`, and `paletin_thread`.
4. Build a `clay_kiln`.
5. Smelt copper and tin-like ore into `rosycopper_bar` and `paletin_bar`.
6. Build a `bellows_forge`.
7. Alloy `bronze_bar`.
8. Mine `rustcore_ore`.
9. Forge `ironroot_bar`.
10. Explore deep caves for `umbralite_node`.
11. Forge `deepsteel_bar`.
12. Mine `staropal_geode`.
13. Craft `starforged_core` and endgame tools.

---

## 14. Example Data Definitions

### 14.1 Block Example

```json
{
  "id": "rosycopper_bloom",
  "name": "Rosycopper Bloom",
  "category": "resource_node",
  "hardness": 3.0,
  "preferredTool": "DELVER",
  "minTier": 2,
  "stackMax": 99,
  "tags": ["stone", "ore", "metal_ore"],
  "drops": [
    {
      "itemId": "raw_rosycopper",
      "min": 1,
      "max": 3,
      "chance": 1.0
    }
  ],
  "placement": {
    "hosts": ["graystone", "warm_granite", "white_limestone"],
    "minY": 45,
    "maxY": 150,
    "ratePerChunk": 8,
    "veinMin": 3,
    "veinMax": 10
  }
}
```

### 14.2 Tool Example

```json
{
  "id": "bronze_delver",
  "name": "Bronze Delver",
  "category": "tool",
  "toolClass": "DELVER",
  "tier": 4,
  "speed": 3.0,
  "maxDurability": 300,
  "stackMax": 1,
  "repairItem": "bronze_bar"
}
```

### 14.3 Recipe Example

```json
{
  "id": "craft_bronze_bar",
  "station": "bellows_forge",
  "inputs": [
    { "itemId": "rosycopper_bar", "count": 3 },
    { "itemId": "paletin_bar", "count": 1 }
  ],
  "outputs": [
    { "itemId": "bronze_bar", "count": 4 }
  ],
  "craftTimeSeconds": 16
}
```

---

## 15. Canonical Implementation Checklist

Core systems required to implement this ruleset:

```txt
Block registry
Item registry
Tool registry
Recipe registry
Chunk terrain generator
Biome selector
Ore/resource placement pass
Mining progress system
Drop table resolver
Tool durability system
Inventory stack manager
Crafting station logic
Smelting/forge fuel logic
Fluid update logic
Crop growth tick
Container storage logic
```

A minimal canonical survival slice should begin with these materials and tiers:

```txt
Branchwood
Flint
Rosycopper
Bronze
Ironroot
Deepsteel
Starforged
```

And these canonical tool classes:

```txt
Delver
Spade
Feller
Sickle
Mallet
Carver
Tiller
```
