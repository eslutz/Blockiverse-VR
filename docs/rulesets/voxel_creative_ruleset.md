# Voxel Creative Ruleset

Version: 1.0
Companion documents: `voxel_survival_ruleset.md`, `voxel_survival_menus.md`, `voxel_save_versioning_schema.md`, `voxel_multiplayer_networking_ruleset.md`, and `voxel_audio_vfx_ruleset.md`

This ruleset defines the rule modifications needed to switch the game from Survival mode to Creative mode. Creative mode should reuse the same canonical block, item, terrain, crafting, inventory, world-generation, environment, structure, vegetation, save, networking, and feedback registries used by Survival mode, then apply the overrides below.

The Creative rules do not preserve temporary reduced worlds as a separate target. Builder worlds should use canonical presets such as `survival_terrain`, `flat_builder`, and `void_builder`. Existing working VR interaction behavior should be retained while the underlying world content migrates to the canonical rules.

---

## 0. Retained VR interaction requirements

These behaviors are implementation requirements for Creative mode because they are central to comfortable VR building:

| Area | Required behavior |
|---|---|
| Movement | Use local XR locomotion that never waits on network round trips: continuous movement, snap or continuous turn, height reset, and target-based teleport where enabled. |
| Block targeting | Use native XR ray interaction for block highlight, placement preview, place, and remove actions. |
| UI safety | Suppress block placement/removal while the controller ray is interacting with UI. |
| Edit toggle | Provide a local block-editing toggle so builders can move and use menus without accidental edits. |
| Player collision | Prevent placement inside the local player collision/head space. |
| Multiplayer | Clients send edit commands to the host; only accepted host deltas apply final world changes. |
| Feedback | Play final block audio, haptics, and VFX only after an authoritative local edit or accepted host delta. |

---

## 1. Design goals

Creative mode is for building, testing, world editing, and exploration without resource pressure.

Core differences from Survival mode:

| System | Survival Mode | Creative Mode |
|---|---|---|
| Resources | Must be gathered, mined, refined, and crafted | Available instantly from catalog |
| Mining | Tool-dependent, tier-gated, durability-based | Instant or near-instant, no tool requirement |
| Crafting | Consumes ingredients and uses stations | Optional; all recipes unlocked and free |
| Inventory | Limited slots and stack sizes | Unlimited catalog access, optional normal inventory |
| Player damage | Normal damage and survival costs | Disabled by default |
| Movement | Grounded exploration | Flight enabled by default |
| Progression | Material tier progression | All tiers available immediately |
| World editing | Single-block interaction | Optional area tools, copy, fill, replace, undo |

---

## 2. Mode flag and rule priority

Creative mode should be represented as a world setting and a player setting.

```ts
type GameMode = "survival" | "creative";

type PlayerModeState = {
  playerId: string;
  gameMode: GameMode;
  canUseCreativeCatalog: boolean;
  canFly: boolean;
  invulnerable: boolean;
  canUseWorldEditActions: boolean;
};

type WorldModeState = {
  defaultGameMode: GameMode;
  allowModeSwitching: boolean;
  allowCreativeDrops: boolean;
  protectSurvivalInventories: boolean;
};
```

Rule priority:

```ts
if player.gameMode == "creative":
    applyCreativeOverrides()
else:
    applySurvivalRules()
```

Creative rules override Survival rules only for the player using Creative mode. This allows mixed-mode multiplayer worlds where some players are builders or admins and others remain in Survival mode.

---

## 3. Global Creative overrides

| Rule Area | Creative Override |
|---|---|
| Player health | Player cannot lose health from normal sources |
| Hunger/thirst/stamina | Disabled or locked at maximum |
| Fall damage | Disabled |
| Heat, cold, drowning, lightning damage | Disabled by default |
| Hostile damage | Disabled by default |
| Tool tier checks | Ignored |
| Tool durability loss | Disabled |
| Resource consumption | Disabled for catalog placement and crafting |
| Crafting station requirement | Disabled by default; stations remain usable for testing |
| Recipe unlocks | All recipes unlocked |
| Pickup restrictions | Optional; default is normal pickup allowed but unnecessary |
| Item despawn | Unchanged unless placed by Creative world-edit preview |
| Death | Disabled by default; void death still teleports to safe spawn |
| Build reach | Increased |
| Flight | Enabled |
| Inventory size | Creative catalog plus optional standard inventory |

Suggested constants:

```ts
CREATIVE_BLOCK_REACH = 12.0;
CREATIVE_ENTITY_REACH = 8.0;
CREATIVE_BREAK_DELAY_TICKS = 0;
CREATIVE_PLACE_COOLDOWN_TICKS = 1;
CREATIVE_MAX_FILL_VOLUME = 32768;
CREATIVE_MAX_REPLACE_VOLUME = 32768;
CREATIVE_UNDO_HISTORY_LIMIT = 50;
```

---

## 4. Player ability rules

### 4.1 Movement

Creative players can fly.

```ts
creative.canFly = true;
creative.flightEnabledDefault = true;
creative.flightSpeed = 0.10 blocksPerTick;
creative.sprintFlightSpeed = 0.22 blocksPerTick;
creative.verticalFlightSpeed = 0.12 blocksPerTick;
```

Recommended controls:

| Action | Rule |
|---|---|
| Jump while grounded | Normal jump |
| Double jump | Toggle flight on |
| Sneak while flying | Descend |
| Jump while flying | Ascend |
| Sprint while flying | Increase flight speed |
| Touch ground while sneaking | Optional auto-disable flight |

Collision remains enabled by default. Noclip can exist as a separate admin/debug permission.

```ts
type CreativeMovementSettings = {
  flightEnabled: boolean;
  noclipEnabled: boolean;
  sprintFlightMultiplier: number;
};
```

### 4.2 Invulnerability

Creative players are invulnerable to normal gameplay damage.

```ts
function applyDamage(target, damage) {
  if (target.gameMode == "CREATIVE" && damage.type != "ADMIN_KILL") {
    return 0;
  }
  return damage.amount;
}
```

Damage types disabled by default:

```txt
fall
fire
emberflow
cold
starvation
thirst
drowning
lightning
creature_attack
suffocation
blast
poison
```

Void handling:

```ts
if player.y < WORLD_MIN_Y - 32 and player.gameMode == "CREATIVE":
    teleport player to nearest safe spawn or world spawn
```

---

## 5. Creative inventory model

Creative mode should use a catalog instead of relying only on the Survival inventory.

```ts
type CreativeCatalogEntry = {
  itemId: string;
  displayName: string;
  category: CreativeCatalogCategory;
  tags: string[];
  defaultCount: number;
  metadataPresets?: Record<string, unknown>[];
};

type CreativeCatalogCategory =
  | "terrain"
  | "stone"
  | "resource_nodes"
  | "plants"
  | "crafted_blocks"
  | "tools"
  | "materials"
  | "food"
  | "lighting"
  | "stations"
  | "containers"
  | "fluids"
  | "utility"
  | "world_edit";
```

### 5.1 Catalog categories

| Category | Contents |
|---|---|
| Terrain | Turf, soil, sand, gravel, clay, snow, ice |
| Stone | Graystone, Dark Slate, Warm Granite, Limestone, Basalt, Deepmantle |
| Resource Nodes | Ore nodes, crystal nodes, salt crusts, surface resource blocks |
| Plants | Trees, logs, leaves, reeds, berrybushes, crops, seeds |
| Crafted Blocks | Planks, cutstone, bricks, glass, doors, hatches |
| Tools | Every tool material and tool class |
| Materials | Raw ore, bars, crystals, fibers, fuel, food ingredients |
| Food | Berry Mash, Trail Rations, Flatbread, cooked items |
| Lighting | Glowwicks, Lumen Lamps, Campfires, Spark Flares |
| Stations | Build Table, Clay Kiln, Bellows Forge, Prep Board, Mend Bench |
| Containers | Reed Basket, Storage Crate, Deep Locker, Tool Rack, Pantry Jar |
| Fluids | Freshwater bucket, Brine bucket, optional Emberflow container if enabled |
| Utility | Wayflags, ladders, buckets, flasks, bandages |
| World Edit | Fill tool, replace tool, copy tool, region wand, delete tool |

### 5.2 Catalog item granting

Clicking a catalog item grants a stack to the cursor or hotbar.

```ts
function grantCreativeItem(player, itemId, countMode) {
  let count = getCreativeGrantCount(itemId, countMode);
  let stack = createItemStack(itemId, count, defaultCreativeMetadata(itemId));
  placeOnCursorOrHotbar(player, stack);
}
```

Grant count rules:

| Item Type | Single Click | Shift Click | Middle Click / Pick Block |
|---|---:|---:|---:|
| Blocks | 99 | 999 virtual stack | 99 |
| Raw materials | 99 | 999 virtual stack | 99 |
| Food | 20 | 99 virtual stack | 20 |
| Tools | 1 | 1 | Copy selected tool metadata |
| Buckets/fluid items | 1 | 1 | 1 |
| Stations | 10 | 99 virtual stack | 10 |
| Filled containers | 1 | 1 | Copy exact contents if allowed |

A `virtual stack` behaves like a large stack in Creative mode but should not be transferable into Survival inventories unless converted or validated by admin rules.

### 5.3 Search and filters

Catalog search should match:

```txt
item ID
item display name
category
tags
description text
material tier
tool class
```

Examples:

| Search Text | Expected Matches |
|---|---|
| `stone` | Graystone, Cutstone Block, Niterstone Pocket, stone rubble |
| `light` | Glowwick, Lumen Lamp, Spark Flare, Lumen Crystal |
| `tier 5` | Ironroot tools, Deepmantle mining tools |
| `ore` | All raw ores and resource nodes |
| `farm` | Tiller, seeds, crops, tended soil |

---

## 6. Inventory behavior in Creative mode

Creative mode can either preserve the normal Survival inventory or replace it with a Creative inventory. The safest implementation is to keep both separate.

```ts
type CreativeInventoryState = {
  hotbar: ItemStack[];
  survivalInventorySnapshot?: ItemStack[];
  creativeCatalogUnlocked: boolean;
  selectedCatalogTab: string;
};
```

Recommended rule:

```ts
if switching SURVIVAL -> CREATIVE:
    save survival inventory snapshot
    keep hotbar visible
    enable catalog

if switching CREATIVE -> SURVIVAL:
    restore survival inventory snapshot
    remove virtual creative stacks
    keep only survival-legal items if admin allows conversion
```

### 6.1 Stack rules

| Rule | Creative Behavior |
|---|---|
| Stack limits | Ignored for virtual stacks; normal for physical stacks |
| Durability metadata | Preserved when copied, ignored when used |
| Container contents | Copying filled containers requires permission |
| Locked slots | Still respected |
| Item pickup | Allowed, but catalog access makes it optional |
| Auto pickup | Can be disabled to avoid clutter |
| Dropping items | Allowed by default; can be disabled in multiplayer |

### 6.2 Creative item deletion

Creative inventory should include a delete slot.

```ts
if itemStack dropped on creativeDeleteSlot:
    destroy stack immediately
```

Optional shortcuts:

| Input | Action |
|---|---|
| Drop key over catalog stack | Do nothing |
| Drop key over inventory stack | Delete one item or stack, depending modifier |
| Shift + drop | Delete full stack |
| Drag over delete slot | Delete all dragged stacks |

---

## 7. Mining and breaking rules

Creative mining ignores the Survival mining formula.

```ts
function getMineTimeSeconds(block, tool, player) {
  if (player.gameMode == "CREATIVE") {
    if (block.tags.includes("unbreakable") && !player.canBreakProtectedBlocks) {
      return Infinity;
    }
    return CREATIVE_BREAK_DELAY_TICKS / 20;
  }
  return getSurvivalMineTimeSeconds(block, tool);
}
```

### 7.1 Break permissions

| Block | Default Creative Rule |
|---|---|
| Normal terrain blocks | Instant break |
| Crafted blocks | Instant break |
| Resource nodes | Instant break |
| Plants | Instant break |
| Fluids | Removed with delete action or replaced by placement |
| Containers | Break allowed; contents behavior depends setting |
| Worldroot | Not breakable unless admin setting is enabled |
| Protected spawn blocks | Not breakable unless player has build permission |

### 7.2 Drop behavior

By default, Creative breaking should not spawn drops. This prevents item clutter.

```ts
if player.gameMode == "creative":
    if world.allowCreativeDrops:
        resolveDropsNormally()
    else:
        dropNothing()
```

Recommended setting:

```ts
allowCreativeDrops = false;
```

### 7.3 Tool durability

```ts
if player.gameMode == "creative":
    durabilityCost = 0
```

Tools can still be used for specialized actions such as tilling, carving, or testing, but they do not wear down.

---

## 8. Placement rules

Creative placement uses normal collision and block-placement rules unless explicitly overridden.

```ts
function canPlaceBlock(player, block, targetPosition) {
  if (!isInsideWorldBounds(targetPosition)) return false;
  if (isProtected(targetPosition) && !player.canBuildInProtectedAreas) return false;
  if (block.id == "worldroot" && !player.canPlaceProtectedBlocks) return false;
  if (player.gameMode == "CREATIVE") return creativePlacementCheck(block, targetPosition);
  return survivalPlacementCheck(player, block, targetPosition);
}
```

### 8.1 Placement consumption

```ts
if player.gameMode == "creative":
    do not decrement held stack
else:
    decrement held stack by 1
```

### 8.2 Support rules

| Block Type | Creative Placement Rule |
|---|---|
| Terrain blocks | Can be placed on any exposed face |
| Logs/planks/stone | Can be placed on any exposed face |
| Plants | Can ignore soil support if `freePlantPlacement` is enabled |
| Crops | Can be placed at any growth stage from catalog presets |
| Fluids | Place source block if fluid placement is enabled |
| Light sources | Normal placement by default |
| Doors/hatches | Normal placement by default |
| Containers | Place empty unless copied from existing filled container |
| Resource nodes | Can be placed manually regardless of natural host block |

Recommended defaults:

```ts
freePlantPlacement = false;
allowManualResourceNodePlacement = true;
allowCreativeFluidPlacement = true;
allowCreativeEmberflowPlacement = false; // enable only for admins/debug
```

### 8.3 Replace placement

When Creative player places a block into a replaceable block, the replaceable block disappears without drops.

Replaceable blocks:

```txt
air
snowpack layer
reedgrass
grass-like decoration
small plant
fluid, only if replaceFluidWithBlock is true
```

---

## 9. Crafting rules

Creative mode keeps recipes available for inspection and testing but does not require crafting for progression.

```ts
function canCraft(player, recipe) {
  if (player.gameMode == "CREATIVE") return true;
  return survivalCanCraft(player, recipe);
}

function craft(player, recipe) {
  if (player.gameMode == "CREATIVE") {
    return createOutputs(recipe.outputs);
  }
  return survivalCraft(player, recipe);
}
```

### 9.1 Station requirement

| Station Rule | Creative Default |
|---|---|
| Handcraft recipes | Always available |
| Build Table recipes | Always available from recipe book |
| Kiln recipes | Can be previewed instantly or run through station for testing |
| Forge recipes | Can be previewed instantly or run through station for testing |
| Prep Board recipes | Always available from recipe book |
| Mend Bench recipes | Optional; repair unnecessary because durability does not decrease |

Recommended implementation:

```ts
creativeCraftingMode = "FREE_RECIPE_BOOK";
```

Valid values:

| Value | Behavior |
|---|---|
| `FREE_RECIPE_BOOK` | All recipes craftable from inventory UI for free |
| `STATION_REQUIRED_FREE` | Station required, but ingredients are not consumed |
| `CATALOG_ONLY` | Crafting disabled; use catalog instead |
| `SURVIVAL_SIMULATION` | Crafting behaves like Survival for testing |

### 9.2 Recipe visibility

Creative recipe book should show every recipe and recipe source.

| Field | Purpose |
|---|---|
| Output item | What the recipe creates |
| Station | Where it is normally crafted |
| Ingredients | Survival cost reference |
| Craft time | Survival timing reference |
| Creative action | Spawn output instantly |

---

## 10. Tools in Creative mode

Tools are optional but useful for testing and specialized actions.

| Tool Class | Creative Use |
|---|---|
| Delver | Break stone/ore instantly; optional area mining brush |
| Spade | Break soil/sand/snow instantly; optional terrain smoothing brush |
| Feller | Break logs instantly; optional tree-removal brush |
| Sickle | Harvest plants instantly; optional regrow/test action |
| Mallet | Move crafted blocks and containers; copy block metadata |
| Carver | Test resin/plant/drop interactions |
| Tiller | Convert soil and test crop rules without durability cost |

Creative tool rules:

```ts
tool.tier checks ignored
tool.durability unchanged
tool.speed ignored for breaking
special tool actions still run unless disabled
```

---

## 11. World generation in Creative mode

Creative mode can use the same world generation as Survival mode. Resource scarcity does not matter because items are available from the catalog, but natural generation should still exist so builders can start from a normal world.

### 11.1 World presets

| Preset | ID | Description |
|---|---|---|
| Survival Terrain | `survival_terrain` | Uses the normal terrain, biome, cave, fluid, and resource placement rules |
| Flat Builder | `flat_builder` | Flat terrain at a selected height with no caves by default |
| Void Builder | `void_builder` | Empty world with a starting platform |
| Island Builder | `island_builder` | One large island surrounded by water or brine |
| Cave Builder | `cave_builder` | Exposed cavern world for underground construction |
| Sky Shelf | `sky_shelf` | Floating terrain shelves and empty space |

### 11.2 Flat Builder preset

```ts
flatBuilder = {
  height: 96,
  surfaceBlock: "meadow_turf",
  subsoilBlock: "loose_loam",
  subsoilDepth: 3,
  baseBlock: "graystone",
  baseDepth: 16,
  generateCaves: false,
  generateResources: false,
  generateFluids: false,
  generateVegetation: false
}
```

Column rules:

```ts
for y = 0..3:
    place worldroot
for y = 4..79:
    place graystone
for y = 80..95:
    place loose_loam
at y = 96:
    place meadow_turf
above y = 96:
    place air
```

### 11.3 Void Builder preset

```ts
voidBuilder = {
  platformCenter: [0, 96, 0],
  platformSize: [16, 1, 16],
  platformBlock: "cutstone_block",
  generateTerrain: false,
  generateWeather: true
}
```

---

## 12. World editing actions

Creative mode may include special actions that operate on regions. These should be permission-gated in multiplayer.

```ts
type RegionSelection = {
  start: Vec3i;
  end: Vec3i;
  volume: number;
};
```

### 12.1 Region limits

| Action | Default Max Volume | Notes |
|---|---:|---|
| Fill | 32,768 blocks | Place one block type into a region |
| Replace | 32,768 blocks | Replace matching block types only |
| Delete | 32,768 blocks | Replace region with air |
| Copy | 65,536 blocks | Store block IDs and metadata |
| Paste | 65,536 blocks | Place copied region at target origin |
| Undo | 50 actions | Per-player history |
| Redo | 50 actions | Cleared after new edit |

### 12.2 Fill action

```ts
function creativeFill(player, region, blockId) {
  require player.canUseWorldEditActions;
  require region.volume <= CREATIVE_MAX_FILL_VOLUME;

  let undo = captureRegion(region);
  for pos in region.positions:
      if canPlaceBlock(player, blockId, pos):
          setBlock(pos, blockId)
  pushUndo(player, undo);
}
```

### 12.3 Replace action

```ts
function creativeReplace(player, region, fromBlockId, toBlockId) {
  require player.canUseWorldEditActions;
  require region.volume <= CREATIVE_MAX_REPLACE_VOLUME;

  let undo = captureRegion(region);
  for pos in region.positions:
      if getBlock(pos).id == fromBlockId:
          setBlock(pos, toBlockId)
  pushUndo(player, undo);
}
```

### 12.4 Copy and paste

```ts
type ClipboardRegion = {
  size: Vec3i;
  originOffset: Vec3i;
  blocks: SerializedBlock[];
};
```

Paste rule:

```ts
paste position = targeted block position + clicked face normal
```

Default paste behavior:

| Setting | Default |
|---|---|
| Paste air blocks | False |
| Paste fluids | False |
| Paste containers with contents | Permission required |
| Rotate pasted structure | Optional, 90-degree increments |
| Mirror pasted structure | Optional |

### 12.5 Undo/redo

```ts
type UndoRecord = {
  actionId: string;
  playerId: string;
  timestamp: number;
  beforeBlocks: SerializedBlock[];
  afterBlocks: SerializedBlock[];
};
```

Undo rule:

```ts
undo restores beforeBlocks exactly, including metadata
redo restores afterBlocks exactly, including metadata
```

---

## 13. Pick block and metadata copy

Creative should support a pick-block action.

```ts
function pickBlock(player, targetedBlock) {
  if player.gameMode != "CREATIVE": return;
  let stack = createItemStack(targetedBlock.itemId, getDefaultCreativeCount(targetedBlock.itemId));

  if player.input.copyMetadata:
      stack.metadata = targetedBlock.metadata;

  placeStackInSelectedHotbarSlot(player, stack);
}
```

Metadata copy examples:

| Block | Copied Metadata |
|---|---|
| Crop | Growth stage, hydration state |
| Doorleaf | Facing direction, open/closed state |
| Storage Crate | Contents only if permission allows |
| Lumen Lamp | Color/tint setting, if supported |
| Water or brine | Fluid level/source state |
| Sign/label block, if added | Text only if permission allows |

---

## 14. Containers in Creative mode

Creative players can create, fill, copy, and delete containers.

| Action | Rule |
|---|---|
| Place container from catalog | Places empty container |
| Pick empty container | Gives empty container item |
| Pick filled container | Requires `canCopyContainerContents` |
| Break container | Deletes block and contents by default if Creative drops are disabled |
| Use container | Opens normal container UI |
| Fill container | Optional action to populate with selected catalog item |
| Clear container | Optional action to remove all contents |

Container content copy should be logged in multiplayer.

```ts
if container.hasContents and player.copyMode == "WITH_CONTENTS":
    require player.canCopyContainerContents
```

---

## 15. Fluid rules in Creative mode

Creative fluid placement should be controlled because fluids can cause large updates.

| Fluid | Creative Placement Default |
|---|---|
| Freshwater | Allowed |
| Brine | Allowed |
| Emberflow | Admin/debug only |

Fluid placement behavior:

```ts
if player places freshwater_bucket in Creative:
    place freshwater source block
    do not consume bucket

if player uses empty_bucket on fluid in Creative:
    remove fluid source if deleteFluidWithBucket is true
    selected bucket becomes matching fluid bucket
```

Recommended safeguards:

| Setting | Default |
|---|---|
| Max fluid updates per tick | 4096 |
| Allow infinite fluid spread | True for normal fluid rules |
| Allow emberflow placement | False |
| Allow replace fluid with block | True |
| Allow delete fluid by clicking | True |

---

## 16. Farming and growth testing

Creative mode can accelerate or bypass farming.

| Action | Creative Rule |
|---|---|
| Place seed | Allowed without consuming seed |
| Place mature crop | Available from catalog as metadata preset |
| Use growth action | Advances crop by one stage |
| Use regress action | Decreases crop by one stage |
| Tiller action | Converts eligible soil without durability cost |
| Hydration checks | Still run unless disabled for testing |
| Crop drops | Disabled by default when Creative-breaking crops |

Crop metadata presets:

```ts
cropPresets = [
  { label: "Stage 0", metadata: { growthStage: 0 } },
  { label: "Half-grown", metadata: { growthStage: 2 } },
  { label: "Mature", metadata: { growthStage: "max" } }
]
```

---

## 17. Survival/Creative conversion rules

Switching modes can create balance issues. Keep Creative inventory separate from Survival inventory unless the world is explicitly marked as Creative-only.

### 17.1 Switching from Survival to Creative

```ts
function switchToCreative(player) {
  player.survivalInventorySnapshot = clone(player.inventory);
  player.gameMode = "CREATIVE";
  player.canFly = true;
  player.invulnerable = true;
  enableCreativeCatalog(player);
}
```

### 17.2 Switching from Creative to Survival

```ts
function switchToSurvival(player) {
  removeVirtualCreativeStacks(player);
  restoreSurvivalInventorySnapshot(player);
  player.gameMode = "SURVIVAL";
  player.canFly = false;
  player.invulnerable = false;
}
```

Optional conversion setting:

```ts
allowCreativeItemsIntoSurvival = false;
```

If conversion is enabled, validate each stack:

```ts
stack must have legal itemId
stack count <= survival stack max
stack metadata must be survival-legal
stack must not be virtual
```

---

## 18. Multiplayer permissions

Creative mode should support role-based permissions.

```ts
type CreativePermissions = {
  canUseCreativeCatalog: boolean;
  canFly: boolean;
  canUseWorldEditActions: boolean;
  canBreakProtectedBlocks: boolean;
  canPlaceProtectedBlocks: boolean;
  canPlaceFluids: boolean;
  canPlaceEmberflow: boolean;
  canCopyContainerContents: boolean;
  canSwitchOwnMode: boolean;
  canSwitchOthersMode: boolean;
};
```

Suggested roles:

| Role | Permissions |
|---|---|
| Survival Player | No Creative permissions |
| Builder | Catalog, flight, normal placement, no dangerous fluids |
| Senior Builder | Builder permissions plus fill/replace/copy/paste |
| Admin | All Creative permissions |
| Spectator Builder | Flight and inspection only, no block edits |

Protected areas:

```ts
if area.isProtected and !player.canBuildInProtectedAreas:
    deny block break/place/world edit action
```

---

## 19. Creative menu changes

Creative mode adds or modifies these menus:

| Menu | Creative Change |
|---|---|
| Inventory | Adds catalog tabs, search, delete slot, and virtual stacks |
| Crafting | Shows all recipes and allows free crafting depending setting |
| Pause | Adds mode switch if permission allows |
| World Settings | Adds Creative world-edit and permission settings |
| Container | Adds fill/clear/copy controls if permission allows |
| Map | Adds teleport-to-wayflag action if enabled |

Creative-specific menu actions:

| Action | Result |
|---|---|
| Click catalog item | Grants item stack |
| Search catalog | Filters visible entries |
| Select category tab | Filters by category |
| Drop item on delete slot | Deletes item stack |
| Pick block | Copies targeted block to selected hotbar slot |
| Open region tools | Shows fill/replace/copy/paste controls |
| Undo edit | Restores previous edited region |
| Redo edit | Reapplies undone edit |

---

## 20. Save data rules

Creative mode changes must be stored in world and player save data.

```ts
type SavedPlayerModeData = {
  playerId: string;
  gameMode: GameMode;
  creativePermissions: CreativePermissions;
  creativeHotbar: ItemStack[];
  survivalInventorySnapshot?: ItemStack[];
  undoHistory?: UndoRecord[];
};

type SavedWorldModeData = {
  defaultGameMode: GameMode;
  allowModeSwitching: boolean;
  allowCreativeDrops: boolean;
  worldPreset: string;
  creativeOnlyWorld: boolean;
};
```

Save rule:

```ts
if world.creativeOnlyWorld:
    survivalInventorySnapshot not required
else:
    keep Survival and Creative inventories separate
```

---

## 21. Implementation override table

| Function | Survival Behavior | Creative Override |
|---|---|---|
| `canHarvestBlock` | Checks tool class and tier | True unless block is protected/unbreakable |
| `getMineTimeSeconds` | Uses hardness, tool class, tier, speed | Returns 0 or configured creative break delay |
| `resolveBlockDrops` | Rolls drop table | Returns empty unless Creative drops enabled |
| `consumePlacedBlock` | Decrements held stack | Does nothing for Creative stacks |
| `consumeCraftingIngredients` | Removes recipe inputs | Does nothing |
| `applyToolDurabilityCost` | Reduces durability | Does nothing |
| `applyPlayerDamage` | Applies damage | Returns 0 for normal damage |
| `canFly` | False unless ability/item grants it | True by default |
| `getRecipeVisibility` | Shows unlocked recipes | Shows all recipes |
| `pickupItem` | Merge into available inventory slots | Optional; can behave normally or delete clutter |

---

## 22. Example Creative mode config

```json
{
  "mode": "CREATIVE",
  "inventory": {
    "useCreativeCatalog": true,
    "keepSurvivalInventorySeparate": true,
    "allowVirtualStacks": true,
    "autoPickupWorldItems": false
  },
  "player": {
    "invulnerable": true,
    "canFly": true,
    "noclipEnabled": false,
    "blockReach": 12.0,
    "entityReach": 8.0
  },
  "blocks": {
    "instantBreak": true,
    "creativeBreakDelayTicks": 0,
    "allowCreativeDrops": false,
    "allowProtectedBlockEditing": false
  },
  "crafting": {
    "mode": "FREE_RECIPE_BOOK",
    "consumeIngredients": false,
    "allRecipesUnlocked": true
  },
  "fluids": {
    "allowFreshwaterPlacement": true,
    "allowBrinePlacement": true,
    "allowEmberflowPlacement": false,
    "maxFluidUpdatesPerTick": 4096
  },
  "worldEdit": {
    "enabled": true,
    "maxFillVolume": 32768,
    "maxReplaceVolume": 32768,
    "maxCopyVolume": 65536,
    "undoHistoryLimit": 50
  }
}
```

---

## 23. Minimal Creative implementation checklist

Required for a first usable Creative mode:

```txt
Game mode flag
Creative player ability flags
Creative catalog UI data source
Catalog item granting
Instant block breaking
No block drops by default
No placement consumption
No tool durability loss
All recipe unlocks
Free crafting or catalog-only crafting
Flight controls
Creative inventory delete slot
Survival/Creative inventory separation
Save/load support for mode state
```

Recommended second pass:

```txt
Pick block
Catalog search
Metadata presets
Region selection
Fill/replace/delete actions
Copy/paste
Undo/redo
Creative world presets
Permission-gated multiplayer Creative roles
Fluid placement safeguards
Container copy permissions
```
