# Codebase Review — Game Logic Expert

> Workflow run `wf_53b36881-009`, agent `a1e33244b43f3a80b`. Raw expert output, pre-verification.

## Area Reviewed

Game-logic review of Blockiverse VR's simulation and rules layer: block mutation authority and the survival command channel (MultiplayerSurvivalSync, MultiplayerChunkAuthoritySync), core mechanics (harvest/place/till/plant/buckets, crafting, smelting, repair, farming, fluids, consumables), vitals/death/respawn, timers and tick-driven systems (WorldTimeClock, FluidFlowService, FarmingService, VegetationService, EnvironmentDynamicsController, WeatherService), and save/load correctness (WorldSaveService, session controllers, multiplayer persistence). The model layer is generally well-engineered — host-authoritative validation, transactional crafting, deterministic seed-pure simulation, and atomic saves are all real and carefully done — but I found a cluster of serious persistence/consistency defects at the integration seams: world state that is generated or grown at runtime but not tracked/persisted/transmitted (sapling trees, spawn-position-dependent terrain, shared crate, multiplayer inventories), a single-player save slot that can be silently overwritten with a multiplayer world, and a durability-stripping item-duplication-class exploit through the shared crate.

## Findings (19)

### 1. Single-player save slot silently overwritten with multiplayer/host world after joining or hosting a LAN session

- **Severity:** Critical  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`
- **Impact:** A player who plays a single-player world and later joins (or hosts) a multiplayer session in the same app run gets their single-player save irreversibly replaced. BlockiverseWorldSessionController never clears currentSavePath when a network session starts; its autosave (Update, 300 s cadence) and the PauseSaveGame/PauseReturnToTitle/TitleQuit handlers keep calling SaveCurrentWorld(), which writes whatever world CreativeWorldManager currently holds. After a client join, FinalizeSnapshot (MultiplayerChunkAuthoritySync.cs:478-513) replaces worldManager.World with the host's regenerated world, so the next autosave writes the host's seed/dimensions/deltas into the player's single-player .vxlworld directory — the original world is gone.
- **Evidence:** BlockiverseWorldSessionController.cs:44 `HasActiveSession => !string.IsNullOrEmpty(currentSavePath)`; Update() lines 98-107 autosaves while HasActiveSession; HandleAction lines 153-161 saves on TitleQuit/ReturnToTitle. currentSavePath is only ever cleared in DeleteDetailsSave (line 562) — grep confirms no clear on host start/client join. MultiplayerChunkAuthoritySync.FinalizeSnapshot lines 490-493 calls worldManager.InitializeGeneratedWorld with the host's world, replacing the world the SP autosave then persists to currentSavePath.
- **Recommended fix:** Clear the single-player session (currentSavePath/currentWorldName) — or at minimum suspend SaveCurrentWorld — whenever a network session starts (subscribe to BlockiverseNetworkSession host/client start events or check NetworkManager.IsListening inside SaveCurrentWorld/Update). Also guard SaveCurrentWorld against saving when worldManager.World's seed/dimensions no longer match the manifest at currentSavePath.

### 2. Sapling-grown trees (and creative-spawned trees/ruins) are written with trackChange:false — lost from saves and late-join snapshots

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs`, `Assets/Blockiverse/Scripts/WorldGen/StructureService.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseCreativeToolsPanel.cs`, `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`
- **Impact:** When a runtime sapling matures, VegetationService places the trunk/canopy via TrySetBlock(..., trackChange:false). Saves persist only world.GetChangedBlocks() (WorldSaveService.WriteRegionFiles), and late-join sync sends only the same changed-block set (SendLateJoinSnapshot), so every player-grown tree vanishes on save/load (the tracked Sapling→Air change is saved, so not even the sapling remains) and is invisible to late-joining clients (permanent host/client world divergence, ExpectedBlockMismatch on edits). The creative-tools SpawnTree/SpawnRuin buttons hit the same untracked paths, so spawned trees/ruins also disappear on reload.
- **Evidence:** VegetationService.cs:484-488 `TrySetBlock(... world.SetBlock(pos, block, trackChange: false))` is used by PlaceTrunk/PlaceCanopyRound/PlaceCanopySquare, which AdvanceSapling (lines 193-209) calls at runtime via PlaceBiomeTree. WorldSaveService.cs:452 `foreach (BlockChange change in world.GetChangedBlocks())` is the only source of saved block deltas; MultiplayerChunkAuthoritySync.cs:678 SendLateJoinSnapshot also iterates GetChangedBlocks(). StructureService.PlaceStructureAt (line 162) → PlaceRuin uses trackChange:false throughout; BlockiverseCreativeToolsPanel.SpawnTree (line 243) and SpawnRuin (line 253) call these at runtime.
- **Recommended fix:** Split the placement paths: keep trackChange:false for generation-time placement, but make runtime growth and creative spawners use tracked SetBlock (e.g. add a `trackChanges` flag to VegetationService/PlaceBiomeTree and to StructureService.PlaceStructureAt, defaulting generation to false and runtime to true). Add an EditMode test asserting a matured sapling's trunk blocks appear in world.GetChangedBlocks().

### 3. Shared-crate (and station) transfers strip tool durability, yielding never-breaking tools

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Assets/Blockiverse/Scripts/Survival/MendBenchRepair.cs`, `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`
- **Impact:** Depositing a tool into the shared crate and withdrawing it returns the tool with Durability 0, because ProcessHostCrateTransfer rebuilds the stack as `new ItemStack(itemId, count)`. Durability 0 means 'not tracking wear' (per MendBenchRepair), and ApplyToolDurability skips slots with Durability <= 0, so the withdrawn tool never loses durability again while keeping its full ToolTier/ToolClass power — an infinite-durability exploit reachable through the normal crate UI, and an accidental wear-state loss for players legitimately sharing tools. Station input deposits have the same stripping (tools deposited there are also unrecoverable, see separate finding).
- **Evidence:** MultiplayerSurvivalSync.cs:1858 `var stack = new ItemStack(itemId, count);` then lines 1879-1899 Remove/TryAddAll move that durability-less stack both directions; station deposit does the same at line 1971. ApplyToolDurability (lines 1749-1762) returns early when `slot.Durability <= 0`. Tools are crafted with full durability via ItemRegistry.CreateItemStack (ItemRegistry.cs:268-273), and MendBenchRepair.cs:90-93 documents 'durability 0 means the slot is not tracking wear'. PlayMode tests only exercise resource (timber) crate transfers, not tools.
- **Recommended fix:** Carry durability through crate/station transfers: either transfer the actual ItemStack (slot-based transfer preserving Durability, as ContainerInventoryStore.TransferAllInto already does) or reject ItemKind.Tool / MaxDurability>0 items in IsValidTransferItem for count-based transfers. Add tests for tool round-trips through the crate.

### 4. Custom spawn position is a world-generation input but is never persisted or transmitted — loaded worlds and late-join clients regenerate different terrain

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/WorldGen/SurvivalLiteWorldPreset.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`, `Assets/Blockiverse/Scripts/Persistence/WorldSaveDirectorySchema.cs`
- **Impact:** The new-world flow picks a custom spawn via FindSpawnForBiome (also triggered for 'balanced' worlds whose center is under water). SurvivalTerrainPreset uses settings.SpawnPosition to flatten the spawn area and to exclude caves/fluids/structures/vegetation/resources from spawn-protected columns. But the manifest stores no spawn position, RegenerateBaseWorld reconstructs settings without it, and the chunk-snapshot header omits it. Result: (1) on every load of such a world the baseline terrain differs from the original — the spawn clearing reverts to raw terrain (player structures float/clip) and a spurious flattened circle appears at the world center; (2) a late-joining client regenerates terrain that differs from the host's around both spawn areas, causing visible divergence and ExpectedBlockMismatch rejections.
- **Evidence:** Creation: BlockiverseWorldSessionController.GenerateWorld lines 319-320 passes `spawn` into WorldGenerationSettings. Generation consumes it: SurvivalLiteWorldPreset.cs FlattenSpawnSurface (lines 126-147) and IsInsideSpawnProtectedColumn used at lines 269, 391, 403, 525, 543, 615, 696. Load: RegenerateBaseWorld lines 675-676 builds `new WorldGenerationSettings(data.Width, ..., WorldConstants.SeaLevel)` with no spawnPosition. Wire: WriteWorldSnapshotHeader (MultiplayerChunkAuthoritySync.cs:1072-1089) writes width/height/depth/chunkSize/seed/groundHeight only. WorldSaveDirectorySchema.cs has no spawn field (grep 'Spawn' returns nothing).
- **Recommended fix:** Persist SpawnPosition in the manifest (schema bump) and include it in the chunk-snapshot header; reconstruct WorldGenerationSettings with it in RegenerateBaseWorld and HandleChunkSnapshotMessage. Alternatively, derive the spawn deterministically from the seed inside the preset so it is never an external input.

### 5. Multiplayer host save persists no player inventory (host items lost every session) and stashed client inventories are never saved

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`, `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- **Impact:** MultiplayerWorldPersistence saves the world via the WorldSaveService overload that substitutes `new Inventory(itemRegistry)` (empty), and RestoreSavedWorldBeforeHostStart never restores any inventory. The host's vitals and position ARE saved/restored (extras.PlayerState), so on the next hosting session the host spawns at their old position with their old health but a completely empty inventory — inconsistent and clearly unintended. Remote players' inventories (stashed by GUID in memory) are also lost across host restarts, so all player progress except the world itself evaporates when the host quits.
- **Evidence:** MultiplayerWorldPersistence.cs:213 and :266 call `new WorldSaveService().Save(path, name, worldManager.World, weatherState: ..., extras: BuildSaveExtras())` — the overload at WorldSaveService.cs:214-217 that passes `new Inventory(itemRegistry)`. BuildSaveExtras (lines 234-244) includes PlayerState (vitals) and stations but no inventory. RestoreSavedWorldBeforeHostStart (lines 94-184) restores world/containers/stations/vitals but has no RestoreLocalInventory call. stashedInventoriesByGuid (MultiplayerSurvivalSync.cs:199, 2649-2654) is memory-only.
- **Recommended fix:** Save the host inventory via the existing inventory-aware Save overload (using survivalSync.BuildPersistedInventory(), like BlockiverseWorldSessionController does) and restore it with survivalSync.RestoreLocalInventory on host start. Persist remote-player inventories keyed by player GUID (the GUID infrastructure already exists) in the players/ directory.

### 6. Shared crate contents are not persisted by any save path

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`
- **Impact:** The shared crate (SharedCrateInventory, the multiplayer-economy shared storage) exists in memory only. Neither WorldSaveData/WorldSaveExtras nor either save path (single-player BlockiverseWorldSessionController or MultiplayerWorldPersistence) serializes it, and nothing restores it on load. Anything players deposit in the shared crate is silently destroyed on save/load, host shutdown, or app exit — item loss in a shipped core flow.
- **Evidence:** WorldSaveData (WorldSaveService.cs:85-109) and WorldSaveExtras (lines 73-82) contain no shared-crate field. Repo-wide grep for 'SharedCrate' outside MultiplayerSurvivalSync.cs hits only UI panels (SurvivalHudController, SurvivalCratePanel) — no persistence references. SharedCrateInventory is created fresh in Configure/CreateSharedCrateInventory (MultiplayerSurvivalSync.cs:416, 2927-2931) and cleared on client start (line 2562).
- **Recommended fix:** Add the shared crate slots to WorldSaveExtras (canonical id + count + durability), export from MultiplayerSurvivalSync in both BuildSaveExtras call sites, and restore it on load/host-start alongside RestoreStationStates.

### 7. Host never validates mining time or rate for survival harvest commands

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- **Impact:** Hold-to-mine (§7.3) is enforced purely client-side: BlockiverseCreativeInputBridge.TickMining accumulates Time.deltaTime locally and only then calls TrySubmitHarvest. ProcessHostHarvest validates tool/tier/capacity/world state but has no notion of elapsed mining time or per-client rate limiting, and no alive-check. A modified or buggy client can instant-mine arbitrary blocks (including VeryHard tier-gated blocks, as long as it owns a qualifying tool) and continue acting while dead, undermining the survival economy in LAN co-op.
- **Evidence:** BlockiverseCreativeInputBridge.cs:97-124 TickMining accumulates `miningElapsedSeconds += Time.deltaTime` and submits at the threshold; ProcessHostHarvest (MultiplayerSurvivalSync.cs:1138-1243) contains no timing, cadence, or death validation — harvest.WorkRequired (mine ticks) is computed but never compared against anything on the host.
- **Recommended fix:** Track per-client last-harvest timestamps on the host and reject (or queue) harvests arriving faster than the block's MineTicks for the authoritative tool; optionally also gate commands from clients whose vitals report dead (would require vitals reporting, or accept as a documented trust boundary for LAN play).

### 8. New-world creation never resets the WorldTimeClock — new worlds inherit elapsed ticks from boot/previous worlds

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/Gameplay/WorldTimeClock.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`
- **Impact:** The WorldTimeClock lives on a Boot-scene object and ticks from app start (including at the title screen and across world switches). LoadSave restores the clock from the save, but CreateNewWorld never calls RestoreElapsedTicks(0)/Configure, so a freshly created world starts with whatever TotalElapsedTicks accumulated so far — including a previously loaded world's restored ticks. The save list day counter ((ticks / TicksPerDay) + 1) shows e.g. 'Day 37' for a brand-new world, the starting time-of-day is arbitrary rather than the canonical morning start, and hunger/thirst interval anchoring begins at a random phase. The wrong tick count is then persisted into the new save.
- **Evidence:** CreateNewWorld (BlockiverseWorldSessionController.cs:256-293) calls InitializeGeneratedWorld + SetGameMode + SaveCurrentWorld with no clock reset; grep shows RestoreElapsedTicks is only called from CreativeWorldManager.RestoreWorldTimeTicks (load/late-join paths) and MultiplayerWorldPersistence. WorldTimeClock.Update (lines 92-107) accumulates totalElapsedTicks continuously; the clock is created once by the bootstrapper (BlockiverseProjectBootstrapper.cs:1361) and never recreated per world.
- **Recommended fix:** In CreateNewWorld, call worldManager.RestoreWorldTimeTicks(0) (and reset normalized time to DefaultStartNormalizedTime via Configure) after InitializeGeneratedWorld and before the initial SaveCurrentWorld.

### 9. Per-peer accumulator-driven environment sims (berrybush regrow, wild regrowth, sapling progress) are never synced to late joiners and their world edits are never broadcast

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/Survival/FarmingService.cs`, `Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`
- **Impact:** FarmingService.TickRegrowth, VegetationService sapling ticks, and the wild-regrowth queue advance on per-peer delta-tick accumulators, and their world.SetBlock calls bypass BroadcastDelta (only TrySubmitMutation paths broadcast). Peers present at harvest time converge because each queues the regrowth locally from the replicated block change, but a client that joins during a pending delay receives neither the queue state (the environment snapshot carries only weather + clock) nor a later delta — so plants/bushes that regrow on the host never appear on that client. The divergence persists until the client touches the cell (ExpectedBlockMismatch correction) or rejoins.
- **Evidence:** FarmingService.TickRegrowth (lines 396-420) uses per-peer berrybushRegrowAccumulator and plants via plain world.SetBlock; VegetationService.TickWildRegrowth (lines 259-295) and TickSapling likewise. SendEnvironmentSnapshot (MultiplayerChunkAuthoritySync.cs:704-731) sends only weather state/ticks/rng + worldTimeTicks. BroadcastDelta is invoked only from TrySubmitMutation/HandleMutationRequestMessage, never from CreativeWorldManager.OnWorldTick's sim calls (lines 644-660). The queues ARE persisted to disk (FillSaveExtras) but never put on the wire.
- **Recommended fix:** Include the regrowth/sapling queues in the late-join environment snapshot (the export/restore methods already exist for saves), or convert these systems to the absolute-tick deterministic pattern used by FarmingService.TickGrowth/FluidFlowService so any peer reconstructs them from synced state.

### 10. FluidFlowService reconstructed flow levels can disagree with the live host's levels, diverging spread/retraction

- **Severity:** Medium  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/WorldGen/FluidFlowService.cs`
- **Impact:** Configure() rebuilds flowing-cell budgets with a multi-source BFS that takes the best budget over all current paths, while the live simulation assigns a cell's level only once when it spreads into air and never improves it when a shorter source path later opens (ProcessCell writes only into Air cells). After terrain edits near flowing fluid, a host's stored level for a cell can be lower than the BFS-derived level a late joiner or a reloaded save computes for the same geometry. HasSupport (neighborLevel > level) and the level>=2 spread gate then make different retraction/spread decisions on different peers, so fluid extents drift apart between the host and late joiners despite the deterministic-lockstep design.
- **Evidence:** Configure BFS (lines 129-153) calls TryImproveLevel which raises existing cells to the best path budget (lines 175-193); the live path in ProcessCell only sets flowLevels when writing into Air (lines 379-411) and OnBlockChanged never recomputes levels for surviving cells. HasSupport (lines 418-450) compares neighbor levels, so level disagreements change retraction outcomes.
- **Recommended fix:** Make the live sim re-improve levels when geometry changes (e.g. on OnBlockChanged, re-run a localized TryImproveLevel relaxation around the edit), or have HasSupport/spread derive levels from a periodic local recomputation instead of sticky assignments, so continuous and reconstructed states converge to the same fixpoint.

### 11. Creative undo/redo histories and clipboard survive world switches and can corrupt a newly loaded world

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/WorldEditService.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseCreativeToolsPanel.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeInteractionController.cs`
- **Impact:** Neither CreativeInteractionController.Configure nor BlockiverseCreativeToolsPanel's WorldEditService clears undo/redo state when a different world is initialized. WorldEditService.Undo/Redo apply the recorded change lists blindly via world.SetBlock with no expected-block validation, so after loading world B a single Undo press replays world A's region edit inverse into world B at the same coordinates (silent block corruption that then gets autosaved). CreativeInteractionController's per-block undo is partially protected by its expectedCurrentBlock check but still rejects/coincidentally applies stale entries.
- **Evidence:** WorldEditService.Undo (lines 154-169) writes changes[i].PreviousBlock unconditionally; the service instance is a readonly field of the persistent panel (BlockiverseCreativeToolsPanel.cs:29) and is never recreated on world load. CreativeInteractionController.Configure (lines 32-50) replaces `world` but does not clear undoStack/redoStack (declared lines 10-12). CreativeWorldManager.ConfigureInteractionController re-Configures the same controller instance for every loaded world.
- **Recommended fix:** Clear undo/redo stacks and the clipboard whenever the bound VoxelWorld instance changes (in CreativeInteractionController.Configure and via a world-changed hook for the panel's WorldEditService), and/or record the world instance alongside history entries and refuse to apply entries from another world.

### 12. Breaking a timed station mid-craft destroys its inputs/fuel/output; deposited items can never be withdrawn

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- **Impact:** TickStations prunes a station model the moment its world block no longer matches (harvested or replaced) without returning or dropping its contents — the harvester receives only the kiln/forge block item while all deposited inputs, fuel, and uncollected output vanish. Compounding this, the command channel offers no input/fuel withdrawal (only StationOpen/DepositInput/DepositFuel/CollectOutput), so any item deposited by mistake (including tools, which IsValidTransferItem accepts) is permanently stuck until the station is broken — at which point it is destroyed.
- **Evidence:** TickStations (lines 1006-1042): mismatch adds to staleStationPositions and `stationModels.Remove(stale)` with no content handling. SurvivalCommandKind (lines 13-32) contains no withdraw-input/fuel command; ProcessHostStationCommand switch (lines 1955-2016) handles only Open/DepositInput/DepositFuel/CollectOutput. ProcessHostHarvest grants only the rolled block drop.
- **Recommended fix:** On station prune (and/or in ProcessHostHarvest when the harvested block is a timed station), transfer the model's inputs/fuel/output into the breaking player's inventory best-effort (mirroring container auto-loot), and add StationWithdrawInput/StationWithdrawFuel commands for the panel.

### 13. Container auto-loot during harvest can consume the capacity the harvest preview validated, silently destroying the crate drop

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- **Impact:** ProcessHostHarvest loots the container's contents into the harvester's inventory after TryPreviewHarvest validated capacity for the block drop but before the drop is added; the final `inventory.TryAddAll(drop)` return value is ignored. With a nearly full inventory, the crate contents can fill the last slots and the storage-crate item itself is then silently lost even though the block was already removed from the world.
- **Evidence:** MultiplayerSurvivalSync.cs:1160-1164 preview (capacity check) → lines 1184-1191 TryLootContainerInto(position, inventory) → line 1221-1222 `ItemStack drop = ...RollHarvestDrop(...); inventory.TryAddAll(drop);` with the bool result discarded (contrast ResourceHarvestService.ApplyHarvestToInventory lines 150-159, which treats the same failure as InventoryFull and aborts).
- **Recommended fix:** Re-check capacity for the drop after looting (and before committing the mutation), or add the drop first and loot afterwards; at minimum log and surface InventoryFull when TryAddAll fails on the authoritative path.

### 14. Survival command channel accepts any inventory slot as 'equipped' and other minor validation gaps

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeInteractionController.cs`
- **Impact:** The host bounds-checks the wire-supplied equippedSlotIndex against SlotCount, not HotbarSlotCount, so a modified client can harvest/place/strip/till/plant using items in backpack slots the local UI would never allow (EquippedItem getter restricts to hotbar). Survival placement also has no player-overlap check on the host (the creative path's playerBounds check is not mirrored), allowing blocks to be placed inside players, and the VR undo button calls CreativeInteractionController.UndoLast unconditionally even in survival mode (host-local only, since clients are rejected by GameModeForbidsDirectMutation).
- **Evidence:** ResolveAuthoritativeTool (MultiplayerSurvivalSync.cs:1768-1774) and ProcessHostPlace/StripLog/Till/PlantSeed (e.g. lines 1261-1263) test `equippedSlotIndex < inventory.SlotCount` only; EquippedItem (lines 349-360) limits to HotbarSlotCount. ProcessHostPlace (1245-1306) has no player-position overlap validation. BlockiverseCreativeInputBridge.TryUndo (lines 304-307) has no SurvivalInteractionActive gate, unlike TryBreakTarget/TryPlaceTarget.
- **Recommended fix:** Clamp the accepted slot index to the hotbar range on the host; reject placements intersecting any connected player's head/feet cells; gate TryUndo on creative mode.

### 15. Station and container save slots drop item durability

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/WorldSaveStateMapper.cs`, `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- **Impact:** WorldSaveStateMapper.ToSavedSlot and SavedContainerSlot serialize only CanonicalId + Count, so any durability-tracked tool sitting in a kiln/forge slot or a world container at save time comes back with Durability 0 after load — joining the same 'never wears out' family as the crate-transfer bug. Player inventory slots DO persist durability (SavedInventorySlot.Durability), making the omission inconsistent.
- **Evidence:** WorldSaveStateMapper.cs:76-85 ToSavedSlot/FromSavedSlot carry no durability; SavedContainerSlot (WorldSaveService.cs:43-47) has no Durability field, while SavedInventorySlot (lines 24-30) does.
- **Recommended fix:** Add a Durability field to SavedContainerSlot/VxlwContainerSlot and map it in ToSavedSlot/FromSavedSlot (schema-compatible additive change while pre-release).

### 16. SmeltingStationModel.TryDepositInput accepts stacks exceeding MaxStackSize into empty slots

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Survival/SmeltingStationModel.cs`
- **Impact:** The merge path enforces `inputs[i].Count + stack.Count <= max`, but the empty-slot path assigns the whole incoming stack without clamping, so a host-processed deposit of e.g. 120 clay lumps lands as a single over-max stack in a station slot. This violates the stack-size invariant the Inventory class enforces elsewhere and can later surface as an oversized stack when contents round-trip through snapshots/saves.
- **Evidence:** SmeltingStationModel.cs:83-110 — merge branch checks max (line 91) but the empty-slot branch (lines 99-107) does `inputs[i] = stack;` with no MaxStackSize check; the host passes a client-chosen count (MultiplayerSurvivalSync.cs:1964-1974) bounded only by CountOf.
- **Recommended fix:** Clamp deposits to MaxStackSize in TryDepositInput (split or reject the remainder), mirroring TryDepositFuel's check.

### 17. Sapling blocks have no harvest rule — placed saplings cannot be removed in survival

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Survival/BlockHarvestRules.cs`, `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`
- **Impact:** BlockHarvestRuleSet.CreateDefault registers rules for every crop stage and plant but none for Sapling/Sapling_S1/Sapling_S2, so TryPreviewHarvest returns NoHarvestRule and survival players cannot break a sapling (relevant for creative-built worlds later played in survival, or if a sapling item is added). There is also no sapling ItemDefinition, so saplings are currently creative-catalog-only.
- **Evidence:** BlockHarvestRuleSet.CreateDefault (BlockHarvestRules.cs:76-169) contains no BlockRegistry.Sapling* entries; ItemRegistry.CreateDefault has no sapling item or drop alias, so RegisterForBlock could not even resolve a drop today.
- **Recommended fix:** Add a sapling item (or alias the drop to an existing resource) plus Hand/Sickle harvest rules for the three sapling stages when saplings become player-obtainable.

### 18. Crop growth conditions are permanently 'favorable' — §11.2 light/moisture gating is unwired

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Survival/FarmingService.cs`
- **Impact:** CreativeWorldManager.OnWorldTick calls farmingService.TickGrowth(World, CurrentWorldTick) without a conditions callback, so every crop always rolls its full base growth chance regardless of light, soil moisture, or biome. The FarmingService machinery (CropGrowthConditions, UnfavorableGrowthMultiplier, per-crop MinLight) exists but is dead at runtime. Note: wiring a real light source must use deterministic, synced inputs (e.g. the sky map + clock), or host/client growth lockstep breaks.
- **Evidence:** CreativeWorldManager.cs:652 `farmingService?.TickGrowth(World, CurrentWorldTick);` — conditions parameter omitted, defaulting to `_ => CropGrowthConditions.Favorable` (FarmingService.cs:299).
- **Recommended fix:** When implementing, derive conditions from deterministic synced state only (VoxelSkyLightMap + block emissives + the §11.1 freshwater scan), never from frame-dependent rendering state, to preserve cross-peer growth determinism.

### 19. Leaf decay only recognizes BranchwoodLog as support — stripping a living tree's trunk dissolves its canopy

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`
- **Impact:** HasNearbyLog checks only BlockRegistry.BranchwoodLog. Strip-log (Feller use) converts trunk blocks to SmoothBranchwood in place; CreativeWorldManager then marks the surrounding leaves as decay candidates (previous == BranchwoodLog), and on the next decay interval the entire canopy of a fully stripped tree decays away. If intended (stripped wood is dead wood) this is fine, but it is an easy player surprise worth a design confirmation since strip-log is presented as a cosmetic conversion.
- **Evidence:** VegetationService.HasNearbyLog (lines 427-445) matches only BlockRegistry.BranchwoodLog; CreativeWorldManager.OnBlockChanged lines 530-531 mark decay candidates when a BranchwoodLog becomes anything else, which includes the SmoothBranchwood strip conversion (MultiplayerSurvivalSync.ProcessHostStripLog).
- **Recommended fix:** If unintended, include SmoothBranchwood as canopy support in HasNearbyLog; if intended, document it in the wiki's survival rules page.

## What Looks Good (7)

- Host-authority enforcement is genuinely structural: BlockMutationAuthority.TryCommit gates on ChunkAuthorityBoundary, survival worlds reject the raw creative channel for clients (GameModeForbidsDirectMutation, MultiplayerChunkAuthoritySync.cs:277-289), and the host re-resolves the equipped tool from its own copy of the client inventory (ResolveAuthoritativeTool) so clients cannot fabricate tools.
- CraftingService.TryCraft is fully transactional — snapshot/rollback covers ingredients, output, and byproducts, and timed recipes are explicitly rejected from instant crafting to close the fuel/time bypass (CraftingService.cs:55-96).
- Deterministic simulation discipline is consistently applied where designed: FluidFlowService and FarmingService.TickGrowth advance on absolute world ticks with seed+position+tick hashed rolls, weather RNG state and tick counts travel in environment snapshots, and structure/container loot regenerates identically from the seed on late-join clients.
- Save robustness is well above average: atomic .tmp→replace writes with .bak recovery (WorldSaveService.ReplaceWithTempFile, regions dir swap with orphaned-.tmp cleanup), fail-fast corrupt-save detection (region palette/bounds validation, HasCompleteTopLevelJsonObject, IsValidInventory), and registry-hash mismatch warnings.
- The wire layer defends the message pump: malformed payloads degrade instead of throwing (TryReadMutationRequest negative-id drop, ReadItemStack empty-id guard, ApplyInventorySnapshot shape check), duplicate commands are suppressed by a bounded per-client window (ProcessedRequestWindow), and out-of-order chunk deltas are buffered and replayed by sequence (TryApplyChunkDelta/ApplyBufferedChunkDeltas).
- Thoughtful edge handling in the sim layer: AdvanceSapling restores a mature sapling and retries instead of destroying it when the tree is blocked (VegetationService.cs:199-207); MaxDropCountForTool keeps harvest capacity pre-checks tight to avoid spurious InventoryFull (ResourceHarvestService.cs:169-183); PlayerVitals.RestoreHealth clamps to ≥1 so a save can never restore into a dead state.
- Test coverage of the engine-free model layer is broad and behavior-focused (FarmingServiceEditModeTests, FluidFlowServiceEditModeTests, SmeltingStationModelEditModeTests, WorldSaveEditModeTests, SurvivalCreativeModeSwitchEditModeTests, plus real Netcode host/client PlayMode sessions exercising harvest/durability/crate sync).

## Could Not Review (5)

- Runtime/on-device behavior: per the binding method I ran no Unity sessions, so findings flagged needsManualVerification (SP-save overwrite flow, fluid level divergence, late-join regrowth divergence) are traced statically but not reproduced live.
- Boot scene / prefab serialized wiring and the 4-5K-line BlockiverseProjectBootstrapper — I verified the WorldTimeClock is bootstrapper-created but did not audit scene YAML or component wiring (outside the game-logic scope and presumably covered by a scene/bootstrap reviewer).
- Netcode for GameObjects delivery semantics (reliability/ordering of CustomMessagingManager named messages) — the delta-sequencing code suggests reordering is handled, but I could not statically confirm the transport guarantees the survival command channel implicitly relies on (e.g. inventory snapshot vs command result ordering).
- Rendering/lighting subsystems (VoxelWorldRenderer, ChunkMeshBuilder, EnvironmentLightingSolver) beyond their interaction with sky-light state — reviewed only where they intersect game logic.
- docs/rulesets cross-checking of every balance constant (hardness tables, fuel values, recipe costs) against the canonical ruleset documents — I verified internal consistency and formula structure, not every number against the markdown specs.

## Inspected (56)

- `Assets/Blockiverse/Scripts/Voxel/VoxelWorld.cs`
- `Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs`
- `Assets/Blockiverse/Scripts/Voxel/ChunkAuthority.cs`
- `Assets/Blockiverse/Scripts/Voxel/ChunkDelta.cs`
- `Assets/Blockiverse/Scripts/Voxel/FluidBlocks.cs`
- `Assets/Blockiverse/Scripts/Voxel/DeterministicHash.cs (via callers)`
- `Assets/Blockiverse/Scripts/WorldGen/FluidFlowService.cs`
- `Assets/Blockiverse/Scripts/WorldGen/WeatherService.cs`
- `Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs`
- `Assets/Blockiverse/Scripts/WorldGen/StructureService.cs`
- `Assets/Blockiverse/Scripts/WorldGen/SurvivalLiteWorldPreset.cs (partial)`
- `Assets/Blockiverse/Scripts/WorldGen/SurvivalBiomeResolver.cs`
- `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs`
- `Assets/Blockiverse/Scripts/Survival/FarmingService.cs`
- `Assets/Blockiverse/Scripts/Survival/Inventory.cs`
- `Assets/Blockiverse/Scripts/Survival/ItemStack.cs`
- `Assets/Blockiverse/Scripts/Survival/ItemId.cs`
- `Assets/Blockiverse/Scripts/Survival/ItemDefinition.cs`
- `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`
- `Assets/Blockiverse/Scripts/Survival/CraftingService.cs`
- `Assets/Blockiverse/Scripts/Survival/CraftingRecipe.cs`
- `Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs`
- `Assets/Blockiverse/Scripts/Survival/SmeltingModel.cs`
- `Assets/Blockiverse/Scripts/Survival/SmeltingStationModel.cs`
- `Assets/Blockiverse/Scripts/Survival/ResourceHarvestService.cs`
- `Assets/Blockiverse/Scripts/Survival/BlockHarvestRules.cs`
- `Assets/Blockiverse/Scripts/Survival/DropTable.cs`
- `Assets/Blockiverse/Scripts/Survival/MiningFormula.cs`
- `Assets/Blockiverse/Scripts/Survival/MendBenchRepair.cs`
- `Assets/Blockiverse/Scripts/Survival/StationProximity.cs`
- `Assets/Blockiverse/Scripts/Survival/ConsumableEffects.cs`
- `Assets/Blockiverse/Scripts/Survival/ContainerInventoryStore.cs`
- `Assets/Blockiverse/Scripts/SurvivalHealth/PlayerVitals.cs`
- `Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs`
- `Assets/Blockiverse/Scripts/SurvivalHealth/BlockHazards.cs`
- `Assets/Blockiverse/Scripts/SurvivalHealth/HazardDamage.cs`
- `Assets/Blockiverse/Scripts/SurvivalHealth/RecoveryWrap.cs`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`
- `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`
- `Assets/Blockiverse/Scripts/Gameplay/SurvivalCreativeModeSwitch.cs`
- `Assets/Blockiverse/Scripts/Gameplay/GameModeConstants.cs`
- `Assets/Blockiverse/Scripts/Gameplay/WorldTimeClock.cs`
- `Assets/Blockiverse/Scripts/Gameplay/EnvironmentDynamicsController.cs`
- `Assets/Blockiverse/Scripts/Gameplay/WorldEditService.cs`
- `Assets/Blockiverse/Scripts/Gameplay/WorldSaveStateMapper.cs`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeInteractionController.cs`
- `Assets/Blockiverse/Scripts/Gameplay/VoxelSkyLightMap.cs`
- `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- `Assets/Blockiverse/Scripts/Persistence/WorldSaveDirectorySchema.cs (grep)`
- `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseCreativeToolsPanel.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs`
- `Assets/Blockiverse/Tests/EditMode and PlayMode test inventory (file list + targeted greps)`
