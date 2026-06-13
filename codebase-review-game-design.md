# Codebase Review — Game Design Expert

> Workflow run `wf_53b36881-009`, agent `af2a317401eaf4bed`. Raw expert output, pre-verification.

## Area Reviewed

Game-design review of the Blockiverse VR survival/creative loops: canonical rulesets (voxel_survival_ruleset.md, voxel_creative_ruleset.md, voxel_survival_menus.md, structure/environment/multiplayer rulesets) diffed against the actual Survival/SurvivalHealth/WorldGen/Gameplay/UI/VR code and the generated Boot scene + XR rig prefab. The simulation-layer foundations are genuinely strong — the mining formula, tool stats, recipe data, station/fuel model, deterministic crop growth, and the host-authoritative command channel all faithfully implement the ruleset math with consistent container-return invariants. But the assembled player experience is broken at the seams: the shipped crafting UI exposes only the first 5 of ~60 recipes (no tool or station is craftable in-game), clay_lump has no source so the entire kiln→forge→metal progression (tiers 3–7, steps 4–13 of the canonical progression path) is unreachable even if the UI were fixed, farming is dead (no obtainable Tiller, seeds essentially unobtainable and non-renewable), the inventory UI shows 6 of 44 slots, difficulty is cosmetic, pause doesn't pause, and death has no consequences and no bedroll recovery path. Survival mode today is effectively a gathering demo: the data layer promises a 7-tier progression that the player-facing layer cannot deliver.

## Findings (23)

### 1. Survival crafting UI exposes only the first 5 of ~60 recipes — no tool or station is craftable in-game

- **Severity:** Critical  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs`, `Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs`, `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab`
- **Impact:** The survival core loop dead-ends minutes in. The 5 visible recipes (registration order) are Work Plank, Stout Pole, Fiber Cord, Stone Rubble, and Glowwick. Campfire (index 5), Flint Carver (6), Build Table (7), every tool, and every station are beyond the fixed 5 buttons, and the panel has no paging or scrolling. Without the Build Table no tool exists; without a tier-1 Delver even graystone (harvestTierMin 1) is unharvestable. Tests pass because they call TryCraftByOutput directly, which no UI path uses.
- **Evidence:** BlockiverseProjectBootstrapper.cs:3024 'TMP_Text[] recipeLabels = new TMP_Text[5]'; the generated prefab contains exactly 'Recipe 1'..'Recipe 5' objects (grep of BlockiverseXRRig.prefab). SurvivalCraftingPanel.Refresh() (lines 218-232) fills only recipeLabels.Length rows from the head of GetSortedRecipes(); TryCraftAtIndex (104-118) maps the 5 buttons to indices 0-4; comment at lines 276-277 acknowledges 'limited recipe slots'. TryCraftByOutput callers: only Assets/Blockiverse/Tests (EditMode SurvivalUiEditModeTests.cs:79, PlayMode MultiplayerSessionPlayModeTests.cs:1755). CraftingRecipeBook.CreateDefault registers BuildTable as the 8th recipe (line 49).
- **Recommended fix:** In BlockiverseProjectBootstrapper.EnsureSurvivalCraftingSection, generate a scrollable/paged recipe list (or category tabs per voxel_survival_menus.md §6.10) sized to the full recipe book, and add paging support to SurvivalCraftingPanel (e.g. page offset + next/prev buttons feeding TryCraftAtIndex(pageOffset+index)). Add a PlayMode test that crafts a Build Table through the actual panel buttons.

### 2. clay_lump has no source — Clay Kiln chain and the entire tier 3-7 metal progression are unreachable

- **Severity:** Critical  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`, `Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs`, `Assets/Blockiverse/Scripts/Survival/BlockHarvestRules.cs`, `Assets/Blockiverse/Scripts/WorldGen/StructureService.cs`, `docs/rulesets/voxel_survival_ruleset.md`
- **Impact:** Independent of the UI cap, survival progression hard-stops at tier 2 (flint). clay_lump is consumed by the Clay Kiln recipe (x12) and fired_brick (x2) but is dropped by nothing, crafted by nothing, and absent from both structure loot tables. Without a kiln: no bars, no glass, no water flask, no bucket (needs rosycopper_bar), no Bellows Forge, no bronze/ironroot/deepsteel/starforged tools, no Tiller/Mallet/Sickle of any obtainable kind. Ruleset §13 progression steps 4-13 and the roadmap's 'Initial progression' ('Build clay kiln and bellows forge') cannot be played. Ores gated at tier 3+ (rustcore_ore, umbralite, staropal) can never be mined.
- **Evidence:** Ruleset §2: Claybed drops 'clay_lump ×2–4'; River Silt 20% clay_lump. Implementation: ItemRegistry.cs:35 maps BlockRegistry.Claybed to ItemId.Claybed (drops itself); ItemRegistry.cs:104 registers ClayLump with no block mapping; repo-wide grep for ClayLump in runtime code finds only the two consuming recipes (CraftingRecipeBook.cs:56, 72) and the registration. The only DropTables in the codebase are reedgrass fiber and resin knot (BlockHarvestRules.cs:124, 152-153). Structure loot tables (StructureService.cs:505-526) contain no clay. Structures place only Campfires (StructureService.cs:227), never a kiln. Tests inject ClayLump directly into inventories (SmeltingStationModelEditModeTests.cs:18) so no test covers acquiring it.
- **Recommended fix:** Implement the §2 drop column for claybed (clay_lump ×2-4) and river_silt (20% clay_lump ×1) via DropTable entries in BlockHarvestRuleSet.CreateDefault, and add an integration test that walks the full §13 progression (gather → kiln → forge → ironroot) using only obtainable items.

### 3. Farming loop is dead: Tiller unobtainable, three of four seeds have no source, and crops return no seeds

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Survival/FarmingService.cs`, `Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs`, `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`, `Assets/Blockiverse/Scripts/WorldGen/StructureService.cs`, `docs/rulesets/voxel_survival_ruleset.md`
- **Impact:** The §11 farming system (tended soil, deterministic growth, four crops) is fully built in FarmingService but no player can ever use it: tilling requires a Tiller, and the only Tiller recipes are metal-tier at the Bellows Forge (blocked by the clay break); berry_seed, drygrass_seed, and reed_cutting are dropped by nothing and appear in no loot table; meadow_seed exists only as rare structure-crate loot (weight 7/45 in loot_forager_food) and harvested crops drop no seeds back, so farming could never be self-sustaining even with a tiller. Ruleset §2 specifies renewable seed sources (meadow_turf 15%, leafmoss 20% meadow_seed, dry_turf 10% drygrass_seed).
- **Evidence:** FarmingService.CropForSeed (lines 99-105) maps four seed items to crops; PlantSeed requires TendedSoil which only Till() creates; MultiplayerSurvivalSync.ProcessHostTill (line 1390-1392) requires HarvestToolKind.Tiller. CraftingRecipeBook.RegisterToolRecipes (lines 129-153) registers tillers only for rosycopper/bronze/ironroot/deepsteel/starforged at CraftingStation.BellowsForge. Grep for MeadowSeed/BerrySeed/DrygrassSeed/ReedCutting sources: only ItemRegistry registration and the single meadow_seed loot entry (StructureService.cs:523). ItemRegistry drop aliases (lines 158-172) give crops GrainBundle/BerryCluster/ReedFiber — never seeds.
- **Recommended fix:** Add the ruleset's secondary seed drops to turf/leafmoss harvest rules; make mature crop harvests return 1-2 seeds (standard sandbox-farming convention, consistent with §11.2 yields); and give the Tiller an early-game recipe (e.g. reedwood/flint tiller at the Build Table) or move tended-soil creation to an obtainable tool.

### 4. Inventory UI shows 6 of 44 slots with no item management; hotbar slots 7-10 unselectable

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs`, `Assets/Blockiverse/Scripts/Survival/Inventory.cs`, `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab`, `docs/rulesets/voxel_survival_menus.md`
- **Impact:** The inventory model has 10 hotbar + 34 backpack slots, but the generated HUD shows only slots 1-6. Items auto-stack into the first free slot, so anything landing in slot 7+ becomes invisible and unusable; the player cannot equip a tool that lands there, cannot see why harvests fail with InventoryFull, and has none of the menus-spec §6.8 actions (move, swap, split, drop, sort, quick-transfer, equipment). Clicking a slot only changes hotbar selection. This makes inventory pressure — a core survival tension — read as random unexplained failure.
- **Evidence:** BlockiverseProjectBootstrapper.cs:2948 'TMP_Text[] slotLabels = new TMP_Text[6]'; prefab contains exactly 'Slot 1'..'Slot 6'. Inventory.cs:7-8 DefaultSlotCount 44, DefaultHotbarSlotCount 10. SurvivalInventoryPanel.WireSlotButtons (lines 169-192): a click only calls SetSelectedHotbarSlotIndex; no move/split/drop API exists in the panel. voxel_survival_menus.md §6.8 specifies 13 slot actions (pickup_or_place_stack, split, quick_transfer, sort, drop, lock, equip...), none implemented.
- **Recommended fix:** Generate the full 10-slot hotbar plus a backpack grid in the bootstrapper HUD, and implement at minimum slot-to-slot move/swap and drop-to-destroy in SurvivalInventoryPanel; surface an 'Inventory full' toast on InventoryFull command failures.

### 5. Crop growth stages do not affect yield — harvesting a just-planted crop equals a mature one

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`, `Assets/Blockiverse/Scripts/Survival/BlockHarvestRules.cs`, `Assets/Blockiverse/Scripts/Survival/FarmingService.cs`, `docs/rulesets/voxel_survival_ruleset.md`
- **Impact:** All grain/berry stages (S0-S5) alias to the same fixed 1x drop, so the entire deterministic growth system (1200-tick intervals, light/moisture conditions, stage chains) produces zero gameplay value: there is no reward for waiting and no penalty for early harvest. Ruleset §11.2 specifies mature harvests of grain_bundle ×1-3 and berry_cluster ×2-4. Berrybush regrow (2 game days) fires on harvesting any stage, making instant re-harvest the optimal strategy.
- **Evidence:** ItemRegistry.cs:158-168 RegisterDropAlias maps GrainStalk through GrainStalk_S4 and Berrybush through Berrybush_S5 all to the same single-item drop; BlockHarvestRules.cs:154-164 registers each stage with no DropTable (fixed Drop count 1). FarmingService.OnBlockHarvested (line 385-389) queues regrowth for any berrybush stage. Ruleset §11.2 crop table: 'Meadow Grain ... Harvest grain_bundle ×1–3', 'Berrybush ... berry_cluster ×2–4'.
- **Recommended fix:** Give only the mature stage (GrainStalk_S4, Berrybush_S5, Reedgrass_S3) the full DropTable yield (1-3 / 2-4 / 2-5) and make immature stages drop nothing or a seed only, restoring the §11.2 wait-for-maturity incentive.

### 6. Ruleset drop tables, yield bonuses, and ground-item rules are almost entirely unimplemented

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Survival/BlockHarvestRules.cs`, `Assets/Blockiverse/Scripts/Survival/ResourceHarvestService.cs`, `Assets/Blockiverse/Scripts/Survival/DropTable.cs`, `docs/rulesets/voxel_survival_ruleset.md`
- **Impact:** Of the ~20 variable/secondary drops in ruleset §2/§3 (snowpack 1-3 clumps, frostglass 40% shard, shingle_gravel 25% flint, leafmoss 20% seed, clearpane 50% shards, ore nodes 1-3, niter 2-5, etc.) only reedgrass and resin_knot have tables — everything else drops itself ×1 with 100% chance. The §6.4 tier 4-7 yield bonus (10-25% extra ore) is absent. There are no ground-item entities, so §10.4 pickup rules (2.5-block radius, despawn, merge, 3s protection) and the death-screen 'Dropped items at' concept are unimplementable; a full inventory rejects the harvest outright instead of dropping overflow. Mining variance and loot excitement — a core reward texture of the genre — are flattened.
- **Evidence:** Repo grep for 'new DropTable' in runtime code returns exactly two sites (BlockHarvestRules.cs:124, 153). ResourceHarvestService.RollDrop (lines 185-204) handles only Sickle double-roll and Carver full-yield; no tier-based bonus branch exists. Grep for GroundItem/DroppedItem/despawn/pickupRadius across Scripts/ returns nothing. ResourceHarvestService.TryPreviewHarvest (lines 254-263) returns InventoryFull failure, blocking the break. ItemRegistry.cs:83 comment admits 'block mapping used for harvest drop lookup until M6 drop tables'.
- **Recommended fix:** Extend BlockHarvestRuleSet.CreateDefault with the §2/§3 drop columns as DropTable entries (the DropTable type already supports chance and ranges and multi-entry rolls), add the §6.4 tier bonus to RollDrop, and either add a minimal dropped-item entity or explicitly amend the ruleset to the direct-to-inventory model.

### 7. Harvest gating deviates from §6.2: wrong tool or low tier blocks the break entirely instead of slow-break-with-reduced-drops

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Survival/ResourceHarvestService.cs`, `Assets/Blockiverse/Scripts/Survival/MiningFormula.cs`, `docs/rulesets/voxel_survival_ruleset.md`
- **Impact:** Ruleset §6.2 lets any block break slowly with the wrong tool (terrain still drops; resource nodes drop nothing), preserving player agency. The implementation rejects the action outright for any block with harvestTierMin > 0 unless the exact preferred tool class AND tier are held — e.g. a tier-3 Spade cannot break graystone at all. The MineTicks penalties for wrong tool (×0.25) and low tier (×0.15) are implemented but unreachable for tiered blocks because the preview rejects first. This changes the intended difficulty texture from 'inefficient but possible' to hard walls.
- **Evidence:** ResourceHarvestService.TryPreviewHarvest line 250-251: 'if (rule.HarvestTierMin > 0 && (usedTool != rule.EffectiveTool || toolTier < rule.HarvestTierMin)) return ... InsufficientTool'. Ruleset §6.2 table: 'Wrong tool but sufficient tier | Block breaks slowly; terrain drops normally; resource nodes drop nothing' and 'Correct tool but insufficient tier | Block breaks very slowly, resource nodes drop nothing'.
- **Recommended fix:** Allow the harvest when the tool mismatches but suppress drops per the §6.2 matrix (empty drop for resource nodes, normal drop for terrain with wrong tool), letting the existing MineTicks penalties express the cost; keep the hard reject only for hardness-infinite blocks.

### 8. Pause never pauses: UiScreenRouter.IsGamePaused has no consumers, so vitals and stations run during menus

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/UiScreenRouter.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`, `Assets/Blockiverse/Scripts/Gameplay/WorldTimeClock.cs`, `docs/rulesets/voxel_survival_menus.md`
- **Impact:** Menus ruleset §10.1 requires the pause menu and death screen to freeze the world update loop in single player. Every ScreenRoute is pushed with pauseGame: true, but nothing reads IsGamePaused — WorldTimeClock keeps ticking, hunger/thirst keep depleting, kilns keep smelting, and crops keep growing while the player browses settings or stands at the death screen. A player who opens Settings for 10 minutes loses ~66 thirst. The 'Paused' label is false feedback.
- **Evidence:** UiScreenRouter.cs:61 'public bool IsGamePaused => ActiveScreen.PauseGame;' — repo-wide grep finds zero consumers outside the router itself. WorldTimeClock has only a timeScale field (driven by the creative tools slider, BlockiverseCreativeToolsPanel.cs:98), no pause hook. voxel_survival_menus.md §10.1: 'Pause Menu | Yes | Freezes world update loop', 'Death Screen | Yes | World waits for respawn choice'.
- **Recommended fix:** In offline/single-player sessions, have the menu controller (or world session controller) gate WorldTimeClock ticking and SurvivalVitalsRuntime on router.IsGamePaused; in multiplayer keep simulation running per the multiplayer ruleset but drop the pauseGame flag from the route so the UI doesn't claim otherwise.

### 9. Difficulty setting (easy/normal/hard) is purely cosmetic

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs`, `Assets/Blockiverse/Scripts/SurvivalHealth/BlockHazards.cs`
- **Impact:** The New World menu offers a difficulty selector (per menus §6.3), the value is saved in the manifest, shown in world details and load lists — but no gameplay system reads it. Hazard damage, vitals decay, starvation cadence, drop rates, and resource tuning are all constants. Players choosing 'hard' get an identical game; the option is a broken promise and any future change will silently rebalance existing saves.
- **Evidence:** Grep for 'Difficulty' across Scripts/: hits only NewWorldConfig (option list), BlockiverseWorldSessionController (stores currentDifficulty into the save manifest), SaveListModel/WorldDetailsPanel (display), MultiplayerWorldPersistence and WorldSaveService (persistence). Zero references in Survival, SurvivalHealth, WorldGen, or Gameplay simulation code. SurvivalVitals.cs:16-22 and BlockHazards.cs:37-42 are hard constants.
- **Recommended fix:** Define a small DifficultyProfile (vitals decay multiplier, hazard damage multiplier, starvation interval) resolved from the manifest at world load and injected into SurvivalVitalsRuntime/BlockHazards lookups; or remove the selector until it does something.

### 10. Death has no consequences and no bedroll recovery path; fail-state loop is toothless

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`, `Assets/Blockiverse/Scripts/UI/MenuActions.cs`, `docs/rulesets/voxel_survival_menus.md`, `docs/rulesets/voxel_survival_ruleset.md`
- **Impact:** On death the player respawns at world spawn with full health/hunger/thirst and their complete inventory — no item drop, no durability cost, no corpse run. The bedroll (§9.2 'Sets respawn point') does not exist as an item or block, so HasBedrollSpawn is hard-coded false and the death menu can never offer the §6.21 'Respawn at Bedroll' option; dying far from base means a flavorless teleport home that can even be exploited as free fast-travel. The death screen also cannot show the spec's 'Dropped items at: X..' line. Death is currently a reward (full vitals restore + teleport), inverting the survival risk model.
- **Evidence:** SurvivalVitalsRuntime.cs:46-48 'Bedroll respawn anchors are not implemented yet (no bedroll block exists)... HasBedrollSpawn => false'; Respawn() (lines 275-281) restores all vitals and repositions the rig, never touching the inventory. Grep for bedroll/wayflag blocks in BlockRegistry/ItemId: absent. voxel_survival_menus.md §6.21 mockup includes 'Dropped items at: X 118, Y 44, Z -39' and a bedroll respawn action; ruleset §9.2 lists the bedroll recipe (leafmoss ×6, reed_fiber ×8, fiber_cord ×4).
- **Recommended fix:** Implement the bedroll block + recipe and a per-player spawn anchor (persisted in player save state), and pick a death penalty consistent with the menus mockup — at minimum drop the inventory into a recoverable container at the death position (ContainerInventoryStore already supports position-keyed inventories).

### 11. Survival threat profile is near zero: three contact hazards, 1 HP/min starvation, no fall damage, no night/cold pressure

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/SurvivalHealth/BlockHazards.cs`, `Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs`, `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`
- **Impact:** The only damage sources are thornbrush (1 HP/0.5s), campfire (2), and emberflow (3) — all trivially avoided static blocks — plus starvation/dehydration at 1 HP per 60s per empty vital (death takes ~50-100 minutes of total neglect after the 15-27 minute drain). There is no fall damage, no hostile entities (none specced — but also no compensating pressure), no temperature/night vitals effect, and hunger/thirst do not gate stamina or speed. Combined with free respawns, survival mode has effectively no fail-state pressure, making tools, bandages, food prep, and shelter (the systems the rulesets invest most in) strategically unnecessary.
- **Evidence:** BlockHazards.HazardForBlock (lines 53-75) contains exactly thornbrush, campfire, emberflow(+flow). SurvivalVitals constants (lines 16-22): HungerTicksPerPoint 240 (20 min full drain), ThirstTicksPerPoint 180 (15 min), StarvationDamageIntervalTicks 1200 = 1 HP/min. No code path applies fall damage (no caller of ApplyDamage tied to velocity/height); HazardDamageKind.Cold/Void/Suffocation/Toxic enum values (HazardDamage.cs:5-13) have no producers.
- **Recommended fix:** Tune a real pressure curve: scale starvation damage over time, add environmental pressure already specced in the environment ruleset (cold in tundra/night via the existing temperature model), and consider modest fall damage with a VR comfort toggle. Tie stamina/mining speed to hunger so food matters before the death spiral.

### 12. No damage feedback: taking damage produces no audio, haptic, or visual cue

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/BlockiverseAudioCuePlayer.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseInteractionHaptics.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs`, `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`, `docs/rulesets/voxel_survival_menus.md`
- **Impact:** When a player stands in thornbrush or emberflow or starves, the only signal is the health number on a world-space panel refreshed every 0.5s — no hurt sound, no controller haptic, no vignette/flash, no low-health warning. In VR, where the HUD panel may be out of view, players can drain to death without ever knowing they were taking damage. The menus ruleset §6.6 explicitly specs 'Damage indicators | hud.damage_feedback | Shows directional hit/heat/fall feedback'.
- **Evidence:** BlockiverseAudioCue enum (BlockiverseAudioCuePlayer.cs:8-39) has no hurt/damage/heartbeat cue. BlockiverseInteractionHaptics subscribes only to block-mutation and survival command feedback (lines 73-118) — nothing subscribes to PlayerVitals.HealthChanged for presentation except SurvivalHealthPanel's text refresh. SurvivalVitalsRuntime.TryApplyHazard (line 188-195) applies damage with no cue emission.
- **Recommended fix:** Add a PlayerHurt audio cue + haptic pattern triggered from PlayerVitals.HealthChanged (damage kind-aware: heat vs starvation), and a low-health pulse (reuse the existing vignette infrastructure from BlockiverseVignetteSettingsDriver, gated by the ReducedFlash accessibility setting).

### 13. Placed storage containers cannot be opened; container menu and §10.5 special rules missing

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Survival/ContainerInventoryStore.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalCratePanel.cs`, `docs/rulesets/voxel_survival_menus.md`, `docs/rulesets/voxel_survival_ruleset.md`
- **Impact:** A player can craft and place a Storage Crate, but there is no interact-to-open flow — world containers only yield their contents when broken (break-to-loot). The menus §6.11 Container Menu (move items both ways, sort, take-all) is unimplemented, and all §10.5 container behaviors (24-slot crate, carryable filled reed basket, pantry jar food preservation, tool rack, deep locker gating) are absent; ReedBasket/ToolRack/PantryJar/DeepLocker are registered placeables with no recipes and no behavior. The only player storage is one global 12-slot 'shared crate' panel not tied to any block (canon container rules imply 24). Base-building — the genre's long-term motivation loop — has essentially no storage gameplay.
- **Evidence:** MultiplayerSurvivalSync.cs:188 'const int SharedCrateSlotCount = 12'; TrySubmitUse (lines 583-641) routes station blocks to StationOpenRequested but has no container-open branch — placed crates fall through to the generic paths. ProcessHostHarvest (lines 1183-1191) is the only container access (TryLootContainerInto on break). ContainerInventoryStore.DefaultContainerSlotCount = 12 vs ruleset §10.5 'Storage Crate | 24'. ItemRegistry registers ReedBasket/ToolRack/PantryJar/DeepLocker (lines 73-76) but CraftingRecipeBook has no recipes for them.
- **Recommended fix:** Add a StationOpen-style ContainerOpen command for blocks present in ContainerInventoryStore, bind a per-position container panel (the SurvivalCratePanel transfer pattern generalizes), use 24 slots for storage_crate per canon, and add the reed_basket recipe (§9.1).

### 14. Structure catalog 8/22 implemented; all underground loot-tier structures missing and the watchpost cannot spawn on small worlds

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/WorldGen/StructureService.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs`, `docs/rulesets/voxel_structure_generation_ruleset.md`
- **Impact:** Exploration rewards flatten sharply: of the ruleset's 22 structures, only 8 exist, and none of the six underground/cave structures (stoneburrow_cellar, lumen_hollow, ember_vent_outpost, deep_locker_room, staropal_pocket_shrine — loot tiers 2-5) are implemented, so deep mining offers no landmark discoveries. Loot draws from just two tables (common supply, forager food), both early-game. Additionally, weathered_watchpost has minDistanceFromSpawn 128, but the default 'small' world is 128×128 with spawn at the center — the maximum possible distance is ~90 blocks, so the largest implemented loot structure can never generate on default worlds.
- **Evidence:** StructureService definitions (lines 82-93) list exactly: pathmark_stones, forager_lean_to, resin_tap_grove, frost_shelter, drybrush_niter_pit, weathered_watchpost, cave_shrine, bridge_segment. Ruleset §12.1-12.4 lists 22 structures incl. 6 underground. Loot tables: only CommonSupply and ForagerFood (lines 505-526). weathered_watchpost minDistanceFromSpawn: 128 (line 87); BlockiverseWorldSessionController.SizeFor default returns (128,128) with spawn at width/2 (WorldGenerationSettings.cs:46) — max distance to a corner is sqrt(64²+64²) ≈ 90.5.
- **Recommended fix:** Scale minDistanceFromSpawn to world size (or cap at ~40% of the half-diagonal), and prioritize implementing the underground structure set with tier 2-5 loot tables so the mid/late mining game has discovery rewards.

### 15. Survival/creative mode switch has no permission model: one pause-menu click grants vitals immunity, and the host can place free blocks in survival worlds

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/MenuActions.cs`, `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`, `Assets/Blockiverse/Scripts/Gameplay/SurvivalCreativeModeSwitch.cs`, `docs/rulesets/voxel_creative_ruleset.md`
- **Impact:** The creative ruleset (§2 PlayerModeState/WorldModeState: canSwitchOwnMode, allowModeSwitching) requires mode switching to be permissioned; the implementation exposes 'Switch Survival/Creative' unconditionally in the pause menu for every peer. Because vitals only tick while CurrentMode == Survival, any player — including a client in a co-op survival world — can toggle to creative to stop hunger/thirst/hazard damage, then toggle back. The host additionally bypasses the survival-economy entirely: client raw mutations are rejected in survival worlds, but the host's creative-path placements are not, letting the host conjure unlimited blocks into a survival world (which other players can then harvest).
- **Evidence:** MenuActions.cs:101 adds PauseToggleMode to the pause menu unconditionally; BlockiverseMenuController.cs:378-380 routes it to ToggleSurvivalCreativeMode with no world-mode or permission check. SurvivalVitalsRuntime.InSurvivalMode (lines 132-133) gates all vitals decay and hazards on the local mode. MultiplayerChunkAuthoritySync.cs:274-287 rejects direct mutations only when '!senderIsHost && ... GameMode == Survival'. Creative ruleset line 925: 'Pause | Adds mode switch if permission allows' and §2 permission schema.
- **Recommended fix:** Gate PauseToggleMode on the world's game mode plus a host-controlled permission (hide it in survival worlds by default), and validate the host's own creative placements against the world GameMode the same way client mutations are.

### 16. Worldroot is breakable and collectible, contradicting its 'unbreakable bottom crust' role

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs`, `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`, `Assets/Blockiverse/Scripts/Survival/BlockHarvestRules.cs`, `docs/rulesets/voxel_survival_ruleset.md`
- **Impact:** Ruleset §2 defines worldroot as hardness ∞, no drops, existing to prevent falling out of the world. The implementation gives it finite hardness 6.0, tier 3, and a self-drop — any rosycopper+ Delver mines through the world floor in ~2.7s, opening holes into the void beneath the bounded world (mitigated only by the safety-floor catcher) and adding a non-canonical 'worldroot' building block to inventories.
- **Evidence:** VoxelTypes.cs:273 registers Worldroot with hardnessClass VeryHard (= 6.0f per HardnessFromClass line 115) and harvestTierMin 3 — not infinite. ItemRegistry.cs:44 registers a collectible Worldroot item; BlockHarvestRules.cs:114 registers a Delver harvest rule for it. Ruleset §2: 'Worldroot | worldroot | Unbreakable bottom crust. Prevents falling out of the world. | ∞ | — | None'.
- **Recommended fix:** Give worldroot infinite hardness (MiningFormula.MineTicks already returns int.MaxValue for IsPositiveInfinity) and remove its item mapping/harvest rule; keep it in the creative catalog only if desired.

### 17. Food and cooking content reduced to two raw items; campfire/prep-board food loops missing

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Survival/ConsumableEffects.cs`, `Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs`, `Assets/Blockiverse/Scripts/Survival/ItemId.cs`, `docs/rulesets/voxel_survival_ruleset.md`
- **Impact:** The only edibles are raw Berry Cluster (+12 hunger) and raw Grain Bundle (+25). The campfire cooking table (§12.2: berry_mash, flatbread, cooked_morsel) and prep-board foods (§9.6: trail_ration, berry_mash) are unimplemented, and the items (berry_mash, flatbread, trail_ration, raw/cooked_morsel) do not exist in ItemId/ItemRegistry. The Prep Board station exists but offers exactly one recipe (field bandage), and the Campfire's crafts are two fluid conversions. The eat-to-survive loop is maximally repetitive and the cooking-station fantasy the rulesets build (3 food stations, pantry preservation) has no payoff.
- **Evidence:** ConsumableEffects.TryApply (lines 17-46) handles exactly FieldBandage, CleanWaterFlask, BerryCluster, GrainBundle. CraftingRecipeBook: PrepBoard appears once (FieldBandage, line 119-120); Campfire recipes are CleanWaterFlask and Brightsalt only (lines 89-94). Grep for berry_mash/flatbread/trail_ration/cooked_morsel in ItemId.cs: absent. Ruleset §12.2 campfire table and §9.6 trail_ration ×2 / berry_mash ×1 recipes.
- **Recommended fix:** Add the §12.2/§9.6 food items and recipes (berry_mash, flatbread, trail_ration at minimum) with hunger values that beat raw equivalents, so the campfire/prep-board stations earn their place in the progression.

### 18. Mining and HUD feedback gaps: no progress indicator, no target prompt, silent inventory-full failure, no day/time display

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs`, `Assets/Blockiverse/Scripts/Gameplay/SurvivalFeedbackBridge.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `docs/rulesets/voxel_survival_menus.md`
- **Impact:** Hold-to-mine plays a strike cue every 0.4s but gives no progress bar, crack overlay, or target/mine-time readout (menus §6.6 specs 'Target: ... Tool: ... Mine Time: 2.0s'), so players cannot tell a 2s dig from a 30s one or whether progress is happening at all. With a full inventory the break input does nothing with zero feedback (preview fails, host rejects InventoryFull, and SurvivalFeedbackBridge only cues InsufficientTool). The HUD has no day counter or clock despite a full day/night cycle driving crops and ambience. Release-to-cancel is also stricter than §6.2's 1.25s interruption grace.
- **Evidence:** BlockiverseCreativeInputBridge.TickMining (lines 97-124): elapsed/required tracked privately, never surfaced to UI; CancelMining on any release/aim change (line 105-108). SurvivalFeedbackBridge.OnCommandFeedback (lines 105-117): harvest failure cues only for InsufficientTool. Bootstrapper HUD sections (lines 2823-2826) build Health/Inventory/Crafting/Shared Crate only — no clock/target elements. Menus §6.6 HUD elements table.
- **Recommended fix:** Surface mining progress (radial near the target or controller-anchored bar) fed from TickMining, add an InventoryFull toast/cue in SurvivalFeedbackBridge, and add the day/time line to the HUD from WorldTimeClock (data already exists: TotalElapsedTicks / NormalizedTime).

### 19. 'Infinite' world size silently creates a 256x256 bounded world

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs`
- **Impact:** The New World menu offers small/medium/large/infinite, but 'infinite' falls through to the same 256×256 bounded world as 'large'. Players selecting infinite hit a hard invisible world edge ~96 blocks from spawn in any direction with no warning; the option label is dishonest and will generate store-review complaints.
- **Evidence:** NewWorldConfig.cs:14 WorldSizeOptions includes "infinite"; BlockiverseWorldSessionController.SizeFor (lines 328-335): 'case "large": case "infinite": return (256, 256);'.
- **Recommended fix:** Remove 'infinite' from WorldSizeOptions until streaming/unbounded worlds exist, or relabel it (e.g. 'huge') with the actual dimensions shown in the selector.

### 20. Stamina is a dead stat: displayed and restored but never consumed

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs`, `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs`
- **Impact:** Stamina is shown on the HUD, regenerates 1/s, is restored by clean water, and is reset on respawn — but SurvivalVitals.TrySpendStamina has zero callers in runtime code: no locomotion, mining, or jumping costs stamina. Players see a bar that never moves below full, which reads as broken UI and removes the intended exertion economy (clean water's stamina restore is also worthless).
- **Evidence:** Grep for TrySpendStamina across Assets/Blockiverse/Scripts: only the definition (SurvivalVitals.cs:96); no Gameplay/VR caller. SurvivalHealthPanel.cs:104-106 renders 'Stamina {n}' on the HUD.
- **Recommended fix:** Charge stamina for hold-to-mine ticks and/or sprint locomotion (with depletion slowing mine speed), or hide the stat from the HUD until it participates in gameplay.

### 21. Unobtainable registered content and stack/vocabulary deviations from canon

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`, `Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs`, `Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs`, `docs/rulesets/voxel_survival_ruleset.md`
- **Impact:** Multiple registered items have no acquisition path even after the clay fix: reedwood/flint Sickle, Mallet, Tiller (and reedwood Carver) have ItemRegistry entries but no recipes (so no Mallet of any tier is reachable before bronze, making §7.4 salvage and crafted-block reclaim — fired_brick_block/clearpane_glass require Mallet tier 1 — impossible mid-game); embercoal_block (the 720s fuel), reed_basket, tool_rack, pantry_jar, deep_locker have no recipes; rope_ladder, doorleaf, trap_hatch, wayflag, bedroll (§9.2) don't exist at all. Stack rules deviate: stations and storage containers stack 99 vs canon 10 / 1-filled. Player-facing vocabulary deviates from §0 canon: the items players collect are 'Flinty Shingle', 'Resin Knot', 'Surface Pebbles' where canon names flint_shard, resin_blob, stone_pebble as the item vocabulary.
- **Evidence:** ItemRegistry.cs:117-132 registers all 7 reedwood/flint tool classes; CraftingRecipeBook.cs:129-135 registers recipes only for delver/spade/feller (+handcraft flint_carver). EmbercoalBlock registered (ItemRegistry.cs:107), consumed nowhere, produced nowhere. ItemRegistry station/crate stack sizes use BlockStackSize 99 (lines 68-81) vs ruleset §10.2 'Stations | 10', 'Storage containers | 1 if filled, 10 if empty'. CraftingRecipeBook.cs:29-32 comment: 'flint_shard → flinty_shingle, resin_blob → resin_knot, stone_pebble → surface_pebbles' vs ruleset §0.1 'Player-facing UI and new saves should use the canonical IDs'. VoxelTypes.cs:298-299 fired_brick_block/clearpane_glass harvestTierMin 1 with Mallet effective tool.
- **Recommended fix:** Either add the missing §9.1/§9.2 recipes (reed basket, embercoal block, door/ladder/hatch/wayflag, early mallet) or remove the orphan registrations; align station/container stack sizes with §10.2; rename the harvested items to the canonical flint_shard/resin_blob/stone_pebble vocabulary with node blocks dropping them.

### 22. Survival mode is selectable with void_builder and flat_builder presets that cannot sustain vitals

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs`, `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs`, `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`
- **Impact:** The New World menu lets players combine GameMode=survival with the void_builder preset (a 16x16 cutstone platform, nothing else) or flat_builder (turf/loam/graystone only). In these worlds there is no water, no food, and no resources, so thirst/hunger drain to an inevitable death-respawn loop with no possible counterplay. No validation or warning exists at world creation.
- **Evidence:** NewWorldConfig.cs:12-15: GameModeOptions and WorldPresetOptions cycle independently; IsValid (lines 63-73) checks only the name. VoidBuilderPreset.Generate (WorldGenerationSettings.cs:132-154) places only a cutstone platform; vitals tick whenever CurrentMode == Survival regardless of preset (SurvivalVitalsRuntime.OnWorldTick).
- **Recommended fix:** Warn or disable survival vitals for builder presets (treat flat/void builder worlds as creative-rule worlds per the creative ruleset's preset intent), or validate the mode+preset combination in NewWorldConfig.IsValid.

### 23. Creative mode lacks flight despite the creative ruleset defaulting it on

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs`, `docs/rulesets/voxel_creative_ruleset.md`
- **Impact:** voxel_creative_ruleset §1 lists 'Movement | Flight enabled by default' as a core creative-mode difference, and §2 models canFly per player. The implementation explicitly disables fly on the continuous move provider and offers no vertical locomotion beyond teleporting onto existing geometry, so creative builders cannot work at height without scaffolding — a major friction for the build-anything fantasy the mode promises. (A VR-comfort-conscious implementation may be intentional, but no comfort-gated alternative exists either.)
- **Evidence:** BlockiverseProjectBootstrapper.cs:3811 'continuousMove.enableFly = false'; BlockiverseInputRig.cs:534 same; repo grep for fly/flight finds no other locomotion path. Creative ruleset §1 table: 'Movement | Grounded exploration | Flight enabled by default'; §2 'canFly: boolean'.
- **Recommended fix:** Add a comfort-gated fly mode for creative (smooth vertical move on the off-hand stick with vignette, or a teleport-to-air 'platform' tool), defaulting per the creative ruleset with a comfort setting to disable.

## What Looks Good (10)

- The engine-free simulation layer faithfully encodes the ruleset math: MiningFormula (Assets/Blockiverse/Scripts/Survival/MiningFormula.cs) matches §6.1/§6.3 exactly (×0.25/×0.15 penalties, durability matrix), and ItemRegistry tool durabilities are material base × class multiplier per §7.1/§7.2 (verified: flint mallet 108 = 90×1.2).
- The host-authoritative survival command channel (Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs) is consistent and cheat-resistant by design: server-side tool resolution from host-owned slots (ResolveAuthoritativeTool), station-proximity revalidation (ResolveValidatedStationClaim), duplicate-request windows, and identical durability/drop logic on local and remote paths.
- Container-return invariants are honored everywhere the ruleset requires (§9.6/§10.6): drinking a flask returns the empty flask (ProcessHostUseConsumable:1597), tilling without nearby water consumes a flask and returns it (ProcessHostTill:1436-1441, FarmingService.Till), campfire boiling returns empty buckets as recipe byproducts (CraftingRecipeBook.cs:89-94), and bucket fill/pour conserve the fluid source (ProcessHostFillBucket/PourBucket).
- Deterministic crop growth is genuinely multiplayer-safe: every roll is Hash(worldSeed, position, stage, intervalIndex) so late joiners advance crops identically with zero growth traffic (FarmingService.TickGrowth, lines 280-365), and berrybush regrowth persists across saves (ExportBerrybushRegrowth).
- Hold-to-mine converts canonical mine ticks into a timed VR hold with strike-cadence audio/VFX and a ToolWrong cue on tier failure (BlockiverseCreativeInputBridge.TickMining; SurvivalFeedbackBridge.OnCommandFeedback) — the right interaction shape for VR mining.
- The feedback architecture is cleanly decoupled and settings-aware: one audio cue player, VFX cue player, and haptics bridge all scale through BlockiverseFeedbackSettings (haptic intensity, reduced flash/particles, per-category volumes), matching the menus ruleset's accessibility table (§1081).
- Crafting recipe data closely mirrors §9.1-§9.5 including station gating, kiln/forge timings, fuel values with the forge 2× rule (SmeltingModel), and bar-count-per-class metal tool recipes (CraftingRecipeBook.MetalToolClassParts).
- Mode switching stashes and restores the survival inventory so creative scratch items can never leak into survival saves (SurvivalCreativeModeSwitch; persistence saves the stashed snapshot as the real inventory).
- Survival worlds reject client-side raw creative mutations, forcing all economy-relevant edits through the validated command channel (MultiplayerChunkAuthoritySync.cs:274-287).
- The creative catalog covers nearly the full block registry (77 entries vs 80 blocks, CreativeCatalog.cs) and creative undo/redo with authority-aware validation is implemented per the creative ruleset §12.1 (CreativeInteractionController).

## Could Not Review (5)

- On-device VR experience: hold-to-mine ergonomics, haptic strength, world-space panel legibility/reach, and comfort of the locomotion options can only be judged on a Quest headset — several tuning-level findings (challenge balance, missing damage feedback severity) deserve playtest confirmation.
- Generated audio/VFX asset quality (scripts/audio/generate-audio.py, scripts/art/generate-art-assets.py outputs) — static review can confirm cues are wired but not whether they read well in-game.
- Multiplayer session feel (latency of host round-trips on harvest/craft, late-join snapshot duration) — the code paths are sound but pacing impact needs live LAN testing.
- Runtime behavior of the generated Boot scene beyond what the bootstrapper source and prefab YAML show (I verified the 5-recipe/6-slot HUD in both the bootstrapper code and the committed BlockiverseXRRig.prefab, but did not execute Unity).
- Whether PlayMode tests pass as committed — per instructions I did not run scripts/unity/run-tests.sh; test code was read statically only.

## Inspected (53)

- `docs/rulesets/voxel_survival_ruleset.md`
- `docs/rulesets/voxel_survival_menus.md`
- `docs/rulesets/voxel_creative_ruleset.md`
- `docs/rulesets/voxel_structure_generation_ruleset.md`
- `docs/rulesets/voxel_world_environment_effects.md`
- `docs/rulesets/voxel_multiplayer_networking_ruleset.md`
- `docs/rulesets/voxel_implementation_alignment_matrix.md`
- `docs/roadmap/blockiverse_vr_execution_plan.md`
- `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`
- `Assets/Blockiverse/Scripts/Survival/ItemId.cs`
- `Assets/Blockiverse/Scripts/Survival/MiningFormula.cs`
- `Assets/Blockiverse/Scripts/Survival/BlockHarvestRules.cs`
- `Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs`
- `Assets/Blockiverse/Scripts/Survival/ResourceHarvestService.cs`
- `Assets/Blockiverse/Scripts/Survival/DropTable.cs`
- `Assets/Blockiverse/Scripts/Survival/FarmingService.cs`
- `Assets/Blockiverse/Scripts/Survival/ConsumableEffects.cs`
- `Assets/Blockiverse/Scripts/Survival/SmeltingModel.cs`
- `Assets/Blockiverse/Scripts/Survival/StationProximity.cs`
- `Assets/Blockiverse/Scripts/Survival/ContainerInventoryStore.cs`
- `Assets/Blockiverse/Scripts/Survival/Inventory.cs`
- `Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs`
- `Assets/Blockiverse/Scripts/SurvivalHealth/PlayerVitals.cs`
- `Assets/Blockiverse/Scripts/SurvivalHealth/BlockHazards.cs`
- `Assets/Blockiverse/Scripts/SurvivalHealth/HazardDamage.cs`
- `Assets/Blockiverse/Scripts/SurvivalHealth/RecoveryWrap.cs`
- `Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs`
- `Assets/Blockiverse/Scripts/WorldGen/StructureService.cs`
- `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`
- `Assets/Blockiverse/Scripts/Gameplay/SurvivalFeedbackBridge.cs`
- `Assets/Blockiverse/Scripts/Gameplay/SurvivalCreativeModeSwitch.cs`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeInteractionController.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseFeedbackSettings.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseAudioCuePlayer.cs`
- `Assets/Blockiverse/Scripts/Gameplay/WorldTimeClock.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs (mode gating)`
- `Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalCratePanel.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`
- `Assets/Blockiverse/Scripts/UI/MenuActions.cs`
- `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs (SizeFor)`
- `Assets/Blockiverse/Scripts/UI/UiScreenRouter.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseInteractionHaptics.cs`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs (HUD sections, lines 2823-3137)`
- `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab (Recipe/Slot object counts)`
- `Assets/Blockiverse/Scenes/Boot.unity (HUD absence check)`
