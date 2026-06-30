# Voxel Unified Save and Versioning Schema

Version: 2.0
Companion documents: `voxel_survival_ruleset.md`, `voxel_creative_ruleset.md`, `voxel_survival_menus.md`, `voxel_world_environment_effects.md`, `voxel_structure_generation_ruleset.md`, `voxel_biome_vegetation_ruleset.md`, `voxel_multiplayer_networking_ruleset.md`, and `voxel_audio_vfx_ruleset.md`

This document defines the unified save format, the single-canonical-schema versioning policy, registry compatibility rules, chunk serialization, player data, inventory data, environment state, structure state, vegetation state, and validation behavior for the voxel game rulesets.

The schema is single-canonical with fail-fast loading: there is exactly one supported on-disk schema version at a time, and a save must match it exactly to load. The schema supports Survival and Creative worlds with deterministic generation from a seed in bounded, finite worlds. Saves written by a different schema version are never converted or upgraded — they are refused with a clear, controlled error.

---

## 1. Design goals

The save system should:

1. Preserve player changes, inventories, containers, stations, environment state, generated structure state, and vegetation growth state.
2. Avoid saving deterministic data that can be regenerated unless it has changed or needs persistence.
3. Support bounded, finite worlds through region/chunk files. Worlds have a fixed maximum footprint and height; they never grow or stream without bound.
4. Use a single canonical on-disk schema version with fail-fast loading: a save must match the current schema exactly or it is refused.
5. Use stable string IDs for blocks, items, recipes, structures, biomes, and menus.
6. Never reuse removed IDs for different content.
7. Tolerate registry drift and missing content at load time through warnings and placeholders rather than rewriting saved data.
8. Fail safely with a clear, controlled error when a save schema does not match, or when a save is corrupted.

World bounds are fixed and finite. The horizontal footprint is selected from two presets: `small` (default, 128×128 blocks) and `medium` (192×192 blocks). World height is 128 blocks (Y 0..127) with sea level at Y 64. These bounds are the maximum extent of any world.

---

## 2. Version taxonomy

The save uses a single canonical integer schema version plus informational metadata. Registry compatibility is checked by content hash, not by a version field.

| Version Field | Example | Meaning |
|---|---|---|
| `schemaVersion` | `4` | The single canonical on-disk save schema version. Must match the engine's `CurrentSchemaVersion` exactly. |
| `engineVersion` | `0.1.0` | Game executable or engine build version (informational only). |
| `contentPackVersion` | `pack-id@1.0.0` | Optional external content pack version (informational only). |

The engine defines one supported schema version, `CurrentSchemaVersion` (currently `4`). The manifest stores a single integer `schemaVersion`.

### 2.1 Single-schema load policy

```txt
schemaVersion is a single integer.
Load requires manifest.schemaVersion == CurrentSchemaVersion exactly.
Any other value (older or newer) refuses the load with a controlled error.
Saves are never converted, upgraded, or loaded in a degraded recovery mode.
```

Rules:

```ts
if manifest.schemaVersion != engine.CurrentSchemaVersion:
    refuse to load with: "World save schema {manifest.schemaVersion} is unsupported (expected {engine.CurrentSchemaVersion}).";

// Registry compatibility is a separate, non-fatal concern (see Section 7):
if manifest.blockRegistryHash != engine.currentBlockRegistryHash:
    log non-fatal warning and continue loading;
    unknown block/item IDs fall back to missing_block / missing_item placeholders;
```

---

## 3. Save directory layout

Recommended folder layout:

```txt
<saveId>.vxlworld/
  manifest.json
  rules.json
  registries/
    registry-manifest.json
  dimensions/
    main/
      dimension.json
      environment.json
      structures.json
      vegetation.json
      regions/
        r.<regionX>.<regionZ>.vxlr
  players/
    <playerId>.json
  maps/
    map-index.json
  ui/
    local-session.json
  backups/
    <timestamp>/manifest.json
  thumbnails/
    world.png
```

Required for first playable:

```txt
manifest.json
rules.json
dimensions/main/dimension.json
dimensions/main/environment.json
dimensions/main/regions/*.vxlr
players/<playerId>.json
```

Optional files may be absent and should be recreated when needed.

---

## 4. Stable IDs

### 4.1 ID format

```ts
type RegistryId = string; // lowercase snake_case
```

Valid examples:

```txt
branchwood_log
rosycopper_bloom
lumen_grotto
creative_catalog
weather_state_thunderstorm
```

Invalid examples:

```txt
BranchWoodLog       // uppercase
branchwood log      // spaces
minecraft:stone     // external IP namespace should not be used
ore#1               // punctuation-heavy unstable ID
```

### 4.2 ID rules

| Rule | Requirement |
|---|---|
| Never reuse IDs | A removed ID cannot be assigned to new content. |
| Avoid renaming stable IDs | Stored IDs are not rewritten on load. If content is renamed in the registry, old saves keep the old ID and resolve to a placeholder if the old ID no longer exists. |
| Preserve unknown IDs | Unknown content becomes a missing placeholder with original ID metadata. |
| Keep display names separate | Display names can change without rewriting saved data. |
| Use namespaces only for content packs | Example: `base:branchwood_log`, `skylands:cloud_reed`. |

---

## 5. Manifest schema

```ts
type SaveManifest = {
  schemaVersion: number;            // single canonical integer; must equal CurrentSchemaVersion (4)
  engineVersion: string;            // informational only

  blockRegistryHash: string;        // content hash of the block registry at save time
  itemRegistryHash: string;         // content hash of the item registry at save time

  saveId: string;
  worldId: string;
  worldName: string;
  createdAtUtc: string;
  modifiedAtUtc: string;
  lastPlayedAtUtc?: string;

  gameMode: "survival" | "creative";
  worldSeed: string;
  worldPreset: "survival_terrain" | "flat_builder" | "void_builder";
  environmentPreset: "normal" | "clear_builder" | "storm_test" | "winter_test";

  spawn: Vec3i;
  spawnBiomePreference: string;
  worldSizePreset: "small" | "medium";   // small = 128x128 (default), medium = 192x192

  dimensions: DimensionManifest[];
  playerIndex: PlayerIndexEntry[];
  contentPacks: ContentPackRef[];

  saveFlags: SaveFlag[];
  checksum?: string;
};

type Vec3i = { x: number; y: number; z: number };

type SaveFlag =
  | "has_creative_edits"
  | "survival_simulation_enabled"
  | "uses_experimental_rules"
  | "has_missing_content"
  | "was_recovered_from_backup";
```

Example:

```json
{
  "schemaVersion": 4,
  "engineVersion": "0.1.0",
  "blockRegistryHash": "sha256:9f1c0a3e7b2d4c6f8a1b5d7e9c0f2a4b6d8e0c1f3a5b7d9e1c3f5a7b9d0e2c4f",
  "itemRegistryHash": "sha256:1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d9e0f1a2b",
  "saveId": "save_01jz_voxel_meadow_home",
  "worldId": "world_01jz9q2n7qk9m7v8t3qk5b4d2p",
  "worldName": "Meadow Home",
  "createdAtUtc": "2026-06-06T15:00:00Z",
  "modifiedAtUtc": "2026-06-06T15:30:00Z",
  "gameMode": "survival",
  "worldSeed": "1384750923847509238",
  "worldPreset": "survival_terrain",
  "environmentPreset": "normal",
  "spawn": { "x": 8, "y": 70, "z": -12 },
  "spawnBiomePreference": "balanced",
  "worldSizePreset": "small",
  "dimensions": [
    { "dimensionId": "main", "folder": "dimensions/main", "enabled": true }
  ],
  "playerIndex": [
    { "playerId": "local_player", "file": "players/local_player.json", "lastKnownName": "Player" }
  ],
  "contentPacks": [],
  "saveFlags": []
}
```

---

## 6. Rules file schema

`rules.json` stores world-level game rules that can differ between saves.

```ts
type WorldRules = {
  gameMode: "survival" | "creative";
  difficulty: "calm" | "normal" | "hard";
  worldPreset: "survival_terrain" | "flat_builder" | "void_builder";
  environmentPreset: "normal" | "clear_builder" | "storm_test" | "winter_test";

  survivalSimulation: boolean;
  dynamicWeather: boolean;
  daylightCycle: boolean;
  mobSpawning?: boolean;             // Reserved for future entity ruleset
  fireSpread?: boolean;              // Reserved for fire simulation

  creativeRules?: CreativeRulesSave;
  permissions?: WorldPermissionRules;
};

type CreativeRulesSave = {
  flightEnabled: boolean;
  instantBreak: boolean;
  freePlacement: boolean;
  inventoryMode: "catalog" | "normal" | "hybrid";
  worldEditEnabled: boolean;
  maxWorldEditVolume: number;
};
```

Difficulty values are placeholders until the player/entity/combat rulesets define detailed behavior.

---

## 7. Registry manifest and compatibility

### 7.1 Registry manifest

```ts
type RegistryManifest = {
  registries: {
    blocks: RegistrySummary;
    items: RegistrySummary;
    recipes: RegistrySummary;
    structures: RegistrySummary;
    biomes: RegistrySummary;
    menus: RegistrySummary;
  };
};

type RegistrySummary = {
  contentHash: string;
  entryCount: number;
};
```

The save does not need to store full registries if the base game registry is bundled with the engine. Registry compatibility is determined by content hash only. The manifest stores `blockRegistryHash` and `itemRegistryHash` so a hash mismatch can be detected on load.

### 7.2 Registry compatibility tolerance

Registry compatibility is tolerant. Stored IDs are never rewritten or converted. When the registry has changed since a save was created, loading continues and unresolved content resolves to engine-reserved placeholders.

Block registry hash mismatch (non-fatal):

```ts
if manifest.blockRegistryHash != engine.currentBlockRegistryHash:
    log warning: "World save registry hash mismatch ... block registry has changed since this save was created.";
    continue loading;
```

Missing block fallback:

```ts
if blockId not in registry:
    replace runtime block with `missing_block` placeholder;
    placeholder.metadata.originalBlockId = blockId;
    mark save flag `has_missing_content`;
```

Missing item fallback:

```ts
if itemId not in registry:
    replace runtime item with `missing_item` placeholder;
    placeholder.metadata.originalItemId = itemId;
```

`missing_block` and `missing_item` should be engine-reserved placeholders and not normal Survival items. This placeholder behavior is corruption/compatibility tolerance, not a data-conversion step — the underlying save data is left unchanged unless the world is later modified and re-saved.

---

## 8. Dimension schema

```ts
type DimensionManifest = {
  dimensionId: "main" | string;
  folder: string;
  enabled: boolean;
};

type DimensionSave = {
  dimensionId: string;
  seedSalt: string;
  minY: number;
  maxY: number;
  seaLevel: number;
  chunkSize: 16;
  regionSizeChunks: 32;
  generator: GeneratorState;
  generatedBounds?: Bounds2i;
};

type GeneratorState = {
  worldSeed: string;
  generatorVersion: string;
  terrainVersion: string;
  resourceVersion: string;
  structureVersion: string;
  vegetationVersion: string;
  environmentVersion: string;
};

type Bounds2i = {
  minChunkX: number;
  maxChunkX: number;
  minChunkZ: number;
  maxChunkZ: number;
};
```

---

## 9. Region and chunk storage

### 9.1 Region files

A region file stores `32 × 32` chunks.

```ts
REGION_SIZE_CHUNKS = 32;
```

Region coordinate:

```ts
regionX = floorDiv(chunkX, REGION_SIZE_CHUNKS);
regionZ = floorDiv(chunkZ, REGION_SIZE_CHUNKS);
```

Region file contents:

```ts
type RegionFile = {
  format: "vxlr";
  schemaVersion: number;            // must equal the manifest schemaVersion / CurrentSchemaVersion
  dimensionId: string;
  regionX: number;
  regionZ: number;
  chunkIndex: RegionChunkIndexEntry[];
  chunks: SerializedChunk[];
  checksum?: string;
};

type RegionChunkIndexEntry = {
  chunkX: number;
  chunkZ: number;
  offset: number;
  byteLength: number;
  compression: "none" | "zstd" | "gzip";
  chunkChecksum?: string;
};
```

For a first playable build, each chunk may also be stored as a separate JSON or binary file. Region packing can be added later without changing higher-level schemas.

### 9.2 Chunk schema

```ts
type SerializedChunk = {
  chunkX: number;
  chunkZ: number;
  minY: number;
  maxY: number;
  status: ChunkStatus;
  lastSavedAtWorldTimeTicks: number;

  generationStages: GenerationStageFlags;
  biomeMap: ChunkBiomeMap;
  heightmaps: ChunkHeightmaps;
  sections: ChunkSection[];
  blockEntities: BlockEntitySave[];
  scheduledTicks: ScheduledTick[];
  dirtyFlags: ChunkDirtyFlag[];
};

type ChunkStatus =
  | "empty"
  | "terrain_generated"
  | "features_generated"
  | "fully_generated"
  | "player_modified";

type GenerationStageFlags = {
  terrain: boolean;
  caves: boolean;
  fluids: boolean;
  structures: boolean;
  resources: boolean;
  largeVegetation: boolean;
  detailVegetation: boolean;
  environmentOverlays: boolean;
  lighting: boolean;
};
```

### 9.3 Chunk section schema

Each chunk is split into `16 × 16 × 16` vertical sections.

```ts
type ChunkSection = {
  sectionY: number;                     // floor(y / 16)
  blockPalette: BlockState[];
  blockData: string;                    // bit-packed or RLE-encoded indices into blockPalette
  skyLight?: string;                    // packed 0..15 nibbles
  blockLight?: string;                  // packed 0..15 nibbles
  fluidData?: string;                   // optional if fluids are separate from blocks
  metadata?: Record<string, unknown>;
};
```

Recommended first playable encoding:

```txt
JSON + run-length encoding for blockData
zstd or gzip compression at region-file level
```

Recommended later encoding:

```txt
Palette indices bit-packed by minimum required bits per section
Nibble arrays for light
Binary region file with chunk index
```

---

## 10. Block state schema

```ts
type BlockState = {
  blockId: string;
  state?: Record<string, string | number | boolean>;
};
```

Examples:

```json
{ "blockId": "branchwood_log", "state": { "axis": "y", "treeVariant": "crownbranch", "natural": true } }
```

```json
{ "blockId": "snowpack", "state": { "depth": 3 } }
```

```json
{ "blockId": "tended_soil", "state": { "moisture": 5, "plantedCropId": "meadow_grain" } }
```

Block state rules:

| Rule | Requirement |
|---|---|
| State keys use camelCase | Example: `treeVariant`, `decayDistance`, `fuelSecondsRemaining`. |
| State values are primitive | Use block entities for inventories or complex nested data. |
| Unknown state keys are preserved | Engine may ignore unknown keys but should not delete them during load/save. |
| State defaults come from registry | Missing state keys use default values from the block registry. |
| Large state is not allowed | Inventories, station progress, and custom names belong in block entities. |

---

## 11. Block entity schema

Use block entities for blocks with inventory, timers, ownership, custom names, or complex state.

```ts
type BlockEntitySave = {
  blockEntityId: string;
  blockId: string;
  position: Vec3i;
  type: BlockEntityType;
  version: number;
  data: Record<string, unknown>;
};

type BlockEntityType =
  | "container"
  | "crafting_station"
  | "fuel_station"
  | "campfire"
  | "kiln"
  | "forge"
  | "mend_bench"
  | "tool_rack"
  | "crop"
  | "loot_container"
  | "sign_or_note";
```

Container data:

```ts
type ContainerBlockEntityData = {
  displayName?: string;
  slots: InventorySlot[];
  locked?: boolean;
  ownerPlayerId?: string;
  lootState?: GeneratedLootContainerState;
};
```

Station data:

```ts
type StationBlockEntityData = {
  stationId: "campfire" | "clay_kiln" | "bellows_forge" | "prep_board" | "mend_bench";
  inputSlots: InventorySlot[];
  fuelSlot?: InventorySlot;
  outputSlots: InventorySlot[];
  activeRecipeId?: string;
  progressTicks: number;
  fuelTicksRemaining: number;
  totalCraftTicks?: number;
};
```

---

## 12. Item stack and inventory schema

### 12.1 Item stack

```ts
type ItemStack = {
  itemId: string;
  count: number;
  metadata?: ItemMetadata;
};

type ItemMetadata = {
  durability?: {
    current: number;
    max: number;
    repairCount: number;
  };
  fluid?: {
    fluidId: "freshwater" | "brine" | "emberflow";
    amount: number;
  };
  blockState?: Record<string, string | number | boolean>;
  customName?: string;
  containerContents?: InventorySlot[];
  originalMissingId?: string;
};
```

### 12.2 Inventory slot

```ts
type InventorySlot = {
  slotIndex: number;
  locked: boolean;
  filterItemId?: string;
  stack?: ItemStack;
};
```

### 12.3 Inventory container

```ts
type InventorySave = {
  inventoryId: string;
  ownerType: "player" | "block_entity" | "entity" | "temporary_ui";
  slots: InventorySlot[];
  selectedHotbarSlot?: number;
};
```

Stack compatibility must match the Survival inventory rules:

```ts
canStack = same itemId
    && same metadata
    && same durability state if applicable
    && same fluid contents if applicable
    && same container contents hash if applicable;
```

---

## 13. Player save schema

```ts
type PlayerSave = {
  playerId: string;
  displayName: string;
  schemaVersion: string;
  lastSavedAtUtc: string;

  dimensionId: string;
  position: Vec3d;
  rotation: Vec2f;
  velocity: Vec3d;

  gameMode: "survival" | "creative";
  spawnPoint?: PlayerSpawnPoint;

  hotbar: InventorySave;
  backpack: InventorySave;
  equipment: InventorySave;
  cursorStack?: ItemStack;

  survivalState?: SurvivalPlayerState;
  creativeState?: CreativePlayerState;
  recipeState: PlayerRecipeState;
  mapState?: PlayerMapState;
  permissions?: PlayerPermissionState;
};

type Vec3d = { x: number; y: number; z: number };
type Vec2f = { yaw: number; pitch: number };
```

Reserved survival fields:

```ts
type SurvivalPlayerState = {
  health: number;
  maxHealth: number;
  hunger?: number;
  thirst?: number;
  stamina?: number;
  temperatureExposure?: number;
  statusEffects: StatusEffectSave[];
  deathState?: PlayerDeathState;
};
```

The detailed survival stat math is reserved for a future player ruleset.

Creative state:

```ts
type CreativePlayerState = {
  flightEnabled: boolean;
  noClipEnabled: boolean;
  selectedCatalogCategory?: string;
  selectedWorldEditTool?: string;
  worldEditClipboardId?: string;
  undoStackRefs: string[];
  redoStackRefs: string[];
};
```

---

## 14. Environment save schema

Environment data belongs in `dimensions/main/environment.json`.

Save only durable state and transition state. Derived values such as current sky light can be recalculated unless a cache is needed.

```ts
type EnvironmentSave = {
  environmentVersion: string;
  worldTimeTicks: number;
  dayIndex: number;
  timeOfDayTicks: number;

  daylightCycle: boolean;
  dynamicWeather: boolean;

  currentWeather: WeatherRuntimeState;
  nextWeather?: WeatherTransitionState;
  cloudState: CloudSaveState;
  windState: WindSaveState;
  randomState: DeterministicRandomState;
};

type WeatherRuntimeState = {
  weatherState:
    | "CLEAR"
    | "PARTLY_CLOUDY"
    | "OVERCAST"
    | "LIGHT_RAIN"
    | "HEAVY_RAIN"
    | "THUNDERSTORM"
    | "LIGHT_SNOW"
    | "HEAVY_SNOW"
    | "BLIZZARD"
    | "FOG";
  precipitationIntensity: number;
  stormIntensity: number;
  fogDensity: number;
  startedAtTick: number;
  minimumEndTick: number;
};
```

Snowpack, ice, stormglass, and charred logs are saved as normal chunk blocks or block states.

---

## 15. Structure save schema

Structure data belongs in `dimensions/main/structures.json`.

```ts
type StructureSaveFile = {
  structureVersion: string;
  instances: StructureInstanceState[];
  generatedRegionClaims: StructureRegionClaim[];
};

type StructureInstanceState = {
  instanceId: string;
  defId: string;
  defVersion: number;
  structureSeed: string;
  anchor: Vec3i;
  rotation: 0 | 90 | 180 | 270;
  boundingBox: AABB;
  generatedAtWorldTimeTicks: number;
  generatedInChunks: ChunkCoord[];
  stateFlags: string[];
  lootContainerIds: string[];
  discoveredByPlayerIds: string[];
};

type StructureRegionClaim = {
  regionSizeChunks: number;
  regionX: number;
  regionZ: number;
  structureDefId: string;
  instanceId?: string;
  placementResult: "placed" | "rejected" | "reserved";
};
```

Loot containers are stored as block entities in chunks and referenced by `lootContainerIds`.

Do not rebuild already-placed structures automatically when structure definitions change. Saved structure instances keep their stored state; if a structure definition no longer exists, the instance is left as-is and its blocks resolve through the normal missing-content placeholder behavior.

---

## 16. Vegetation save schema

Vegetation data belongs in `dimensions/main/vegetation.json`.

```ts
type VegetationSaveFile = {
  vegetationVersion: string;
  saplings: SaplingState[];
  wildRegrowthMarkers: WildRegrowthMarker[];
  pendingLeafDecayPositions: Vec3i[];
  generatedChunkVegetationVersions: ChunkVegetationVersion[];
};

type SaplingState = {
  position: Vec3i;
  ageStage: 0 | 1 | 2 | 3 | 4;
  plantedByPlayer: boolean;
  targetVariant?: string;
  nextGrowthCheckTick: number;
};

type WildRegrowthMarker = {
  blockId: string;
  position: Vec3i;
  biomeId: string;
  harvestedAtTick: number;
  regrowAfterTicks: number;
  maxAttempts: number;
};

type ChunkVegetationVersion = {
  chunkX: number;
  chunkZ: number;
  largeVegetationVersion: string;
  detailVegetationVersion: string;
};
```

Generated trees and plants are primarily saved as normal chunk block data after chunk generation.

---

## 17. Map, discovery, and wayflag schema

Maps and wayflags may be saved separately from chunks if the UI needs quick access.

```ts
type MapIndex = {
  mapVersion: string;
  playerMaps: Record<string, PlayerMapState>;
  globalMarkers: MapMarker[];
};

type PlayerMapState = {
  discoveredChunks: ChunkCoord[];
  discoveredStructures: string[];
  customMarkers: MapMarker[];
};

type MapMarker = {
  markerId: string;
  dimensionId: string;
  position: Vec3i;
  markerType: "wayflag" | "structure" | "death" | "custom";
  label?: string;
  color?: string;
  visibleToPlayerIds?: string[];
};
```

---

## 18. Menu and local session schema

Most menu state should not be stored in the world save. Store only local convenience state.

```ts
type LocalSessionSave = {
  lastOpenedScreen?: string;
  selectedSaveId?: string;
  uiScale?: number;
  language?: string;
  autosaveMinutes?: number;
  lastSelectedCreativeCategory?: string;
  lastSelectedWorldEditTool?: string;
};
```

Do not store temporary confirmation dialogs, drag-and-drop cursor previews, or unsaved UI text fields in the world save.

---


## 19. LAN multiplayer save profile

LAN multiplayer uses the same world save schema as single-player, with host-only authority for world writes.

Rules:

| Rule | Behavior |
|---|---|
| Authoritative writer | The host is the only writer of multiplayer world state. |
| Client storage | Clients may store local UI/session convenience values, but not authoritative world chunks, registries, structure state, vegetation state, or environment state. |
| Host start | Before hosting, the host loads the selected save or generates the canonical world preset, then validates ruleset, registry, bounds, chunk size, seed, environment, structure, and vegetation metadata. |
| Client join | Client receives host metadata, validates compatibility, generates the deterministic baseline, then applies host snapshot/deltas. |
| Host shutdown | Host saves dirty world state before completing session shutdown. |
| Save failure | If host save-on-shutdown fails, keep the session active when possible and show a blocking error instead of silently disconnecting clients. |
| Reconnect | Reconnected clients treat the host as authoritative and resync from snapshot/deltas. |

Default LAN save intent:

```txt
<saveId>.vxlworld/
  manifest.json
  rules.json
  dimensions/main/*
  players/<hostPlayerId>.json
  players/<remotePlayerId>.json
  ui/local-session.json
```

Required metadata match before restoring a host save:

```txt
schemaVersion
worldPreset
worldSizePreset (small or medium)
bounds / dimension limits
chunkSize
seed
blockRegistryHash, itemRegistryHash (mismatch is a non-fatal warning, not a join blocker)
environmentStateHash, when environment state is deterministic
structureStateHash, when structures are generated
vegetationStateHash, when vegetation is generated
```

Clients should not overwrite host state during join. Any local cached session data must be discarded or treated as non-authoritative when the host snapshot arrives.

---

## 20. Autosave and transaction rules

Autosave interval is controlled by the Settings menu.

```ts
autosaveIntervalSeconds = settings.autosaveMinutes * 60;
```

Save transaction sequence:

```txt
1. Pause chunk unload for dirty chunks selected for save.
2. Snapshot player state, environment state, structure state, and dirty chunk list.
3. Write changed files to temporary `.tmp` files.
4. Flush temporary files.
5. Validate checksums.
6. Atomically rename `.tmp` files over old files.
7. Update `manifest.json` last, also using temporary write + atomic rename.
8. Clear dirty flags only for data included in the successful transaction.
```

Crash safety rule:

```ts
if .tmp files exist on load:
    ignore incomplete tmp files unless a transaction journal says commit was complete;
```

Optional transaction journal:

```ts
type SaveTransactionJournal = {
  transactionId: string;
  startedAtUtc: string;
  files: string[];
  status: "started" | "files_written" | "committed";
};
```

---

## 21. Dirty tracking

Dirty flags should be granular enough to avoid full-world saves.

| Dirty Flag | Meaning | Save Target |
|---|---|---|
| `chunk_blocks` | Blocks changed in chunk | Region file chunk entry |
| `chunk_light` | Light cache changed | Region file chunk entry or cache rebuild |
| `block_entities` | Container/station data changed | Region file chunk entry |
| `player_state` | Player moved, inventory changed, mode changed | Player file |
| `environment_state` | Time/weather transition changed | Environment file |
| `structure_state` | Discovery/loot/structure state changed | Structures file and affected chunks |
| `vegetation_state` | Growth marker, sapling, decay queue changed | Vegetation file and affected chunks |
| `rules_state` | World settings changed | rules.json and manifest if needed |

---

## 22. Validation and repair

### 22.1 Load validation

Required checks:

```txt
manifest.json exists and parses
schemaVersion matches the current schema (exact match; otherwise refuse load)
worldSeed exists
main dimension exists
player file exists for selected player
registry hashes match, or mismatch is logged as a non-fatal warning
region files have valid chunk indexes
chunk coordinates match region coordinates
block palette IDs exist or can become missing_block
item IDs exist or can become missing_item
block entities point to matching block positions
container slot counts match container definitions
```

### 22.2 Repair behavior

| Problem | Repair |
|---|---|
| Missing optional UI file | Recreate with defaults |
| Missing thumbnail | Regenerate when world loads |
| Missing non-loaded region | Treat as not generated and regenerate from seed |
| Corrupt generated-only chunk with no player edits | Regenerate from seed |
| Corrupt player-modified chunk | Load backup or quarantine chunk file |
| Unknown block ID | Use `missing_block` placeholder |
| Unknown item ID | Use `missing_item` placeholder |
| Invalid player position | Move player to world spawn or last safe position |
| Invalid environment value | Clamp to valid range and mark save repaired |

### 22.3 Last safe position

```ts
type LastSafePosition = {
  dimensionId: string;
  position: Vec3d;
  recordedAtWorldTimeTicks: number;
};
```

Update rule:

```ts
if player is standing on solid block
   and head space is air
   and not in damage fluid
   and not falling:
      update lastSafePosition every 5 seconds;
```

---

## 23. Compatibility policy

| Scenario | Behavior |
|---|---|
| Save schema does not match current schema | Refuse load with: "World save schema {n} is unsupported (expected {m})." |
| Registry hash mismatch | Log a non-fatal warning and continue loading. |
| Missing/unknown block or item content | Resolve to `missing_block` / `missing_item` placeholders and warn. |
| Missing optional content pack | Load with placeholders if content is not required. |
| Corrupt generated-only chunk with no player edits | Regenerate from seed. |
| Corrupt player-modified chunk | Load backup or quarantine the chunk file. |
| Experimental rules enabled | Show warning in World Details menu. |

There is no save-conversion path and no degraded recovery mode. A save either matches the current schema and loads, or it is refused with a clear, controlled error.

---

## 24. Checksums and integrity

Recommended checksum fields:

| Scope | Field | Purpose |
|---|---|---|
| Manifest | `checksum` | Detect partial writes or manual edits |
| Region file | `checksum` | Detect region-level corruption |
| Chunk entry | `chunkChecksum` | Detect individual chunk corruption |
| Registry manifest | `contentHash` | Detect registry mismatch |

Checksum rule:

```ts
checksum = hash(canonicalSerializedBytesWithoutChecksumField);
```

Use a stable canonical JSON serialization for JSON files, or a binary checksum for binary region files.

---

## 25. Save size strategy

| Data Type | Save Strategy |
|---|---|
| Deterministic untouched terrain | Regenerate from seed if chunk not saved |
| Player-modified blocks | Save full changed chunk section or chunk |
| Lighting | Save cache only if useful; otherwise rebuild on load |
| Weather derived values | Recalculate from environment state |
| Structure templates | Do not save templates inside world unless custom player templates exist |
| Loot state | Save generated container contents and opened state |
| Vegetation growth | Save only saplings, regrowth markers, and decay queues |
| Creative undo history | Save only if configured; otherwise session-only |

First playable recommendation:

```txt
Save full chunks once generated or modified.
Optimize to generated-delta storage later.
```

---

## 26. Example player save

```json
{
  "playerId": "local_player",
  "displayName": "Player",
  "schemaVersion": "1.0.0",
  "lastSavedAtUtc": "2026-06-06T15:30:00Z",
  "dimensionId": "main",
  "position": { "x": 8.5, "y": 105.0, "z": -12.5 },
  "rotation": { "yaw": 90, "pitch": 0 },
  "velocity": { "x": 0, "y": 0, "z": 0 },
  "gameMode": "survival",
  "spawnPoint": {
    "dimensionId": "main",
    "position": { "x": 8, "y": 104, "z": -12 },
    "source": "world_spawn"
  },
  "hotbar": {
    "inventoryId": "local_player_hotbar",
    "ownerType": "player",
    "selectedHotbarSlot": 0,
    "slots": [
      {
        "slotIndex": 0,
        "locked": false,
        "stack": {
          "itemId": "flint_delver",
          "count": 1,
          "metadata": {
            "durability": { "current": 74, "max": 90, "repairCount": 0 }
          }
        }
      }
    ]
  },
  "backpack": {
    "inventoryId": "local_player_backpack",
    "ownerType": "player",
    "slots": []
  },
  "equipment": {
    "inventoryId": "local_player_equipment",
    "ownerType": "player",
    "slots": []
  },
  "recipeState": {
    "knownRecipeIds": ["craft_work_plank", "craft_stout_pole", "craft_flint_delver"],
    "pinnedRecipeIds": []
  }
}
```

---

## 27. Example chunk section

```json
{
  "sectionY": 6,
  "blockPalette": [
    { "blockId": "air" },
    { "blockId": "graystone" },
    { "blockId": "loose_loam" },
    { "blockId": "meadow_turf" },
    { "blockId": "branchwood_log", "state": { "axis": "y", "treeVariant": "crownbranch", "natural": true } },
    { "blockId": "leafmoss", "state": { "leafVariant": "crownbranch", "persistent": false, "decayDistance": 2 } }
  ],
  "blockData": "rle:1x120,2x20,3x16,0x3800,4x5,5x30",
  "skyLight": "packed-nibbles-base64-or-rle",
  "blockLight": "packed-nibbles-base64-or-rle"
}
```

---

## 28. Minimal implementation checklist

Required for first playable save/versioning:

```txt
Save manifest schema
World rules schema
Player save schema
Inventory and item stack schema
Chunk schema with section palette storage
Block entity schema for containers and stations
Environment save file
Structure instance save file
Vegetation save file for saplings/regrowth/decay
Stable registry ID policy
Missing block/item placeholder policy
Autosave transaction sequence
Exact schemaVersion match check (refuse load on mismatch)
Registry hash mismatch warning
Load validation and safe player position repair
```

Recommended later:

```txt
Binary region format
Content pack dependency resolver
Chunk-level checksums
World diff/export format
Cloud sync conflict resolution
Multiplayer authority snapshots
```
