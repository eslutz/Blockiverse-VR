# Codebase Review — Anti-Pattern and Code Smell Expert

> Workflow run `wf_53b36881-009`, agent `a0a975448533d518a`. Raw expert output, pre-verification.

## Area Reviewed

Anti-pattern and code-smell review of the Blockiverse VR runtime (12 layered asmdefs under Assets/Blockiverse/Scripts) plus the editor bootstrapper and persistence layer. Overall the architecture is deliberately layered and unusually disciplined for a Unity project: the simulation core is engine-free and NUnit-testable, there are no mutable singletons, events are consistently unsubscribed, and MonoBehaviours expose Configure() seams for tests. The dominant smells are concentration ones: three god artifacts (MultiplayerSurvivalSync ~2,900-line MonoBehaviour, BlockiverseProjectBootstrapper 4,952-line static class, WorldSaveService 1,273-line file), a pervasive FindFirstObjectByType service-locator idiom (~79 runtime call sites), and — most consequentially — duplicated save/load/world-regeneration logic between the UI and Gameplay assemblies that has already begun to diverge behaviorally (fluid-sim tick sync and container-slot validation exist in one copy but not the other). Canonical constants (TicksPerSecond, TicksPerDay) are also defined independently in three assemblies because WorldConstants sits in WorldGen, which Survival cannot reference.

## Findings (23)

### 1. MultiplayerSurvivalSync is a ~2,900-line god MonoBehaviour owning the entire survival economy plus its wire protocol

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- **Impact:** The single most change-prone class in the codebase concentrates at least nine responsibilities: client command submission (12+ TrySubmit* methods), host-side command resolution (11 ProcessHost* methods, several 85–117 lines long, e.g. ProcessHostStationCommand lines 1915–2031, ProcessHostHarvest 1138–1244), a ~100-line wire dispatcher (HandleCommandRequestMessage 2094–2192), FastBuffer serialization helpers (2968–3072), per-client inventory registry and reconnect-GUID stash (lines 190–200), station model ownership + real-time ticking (Update, 447–462), station persistence export/restore (925–1004), request dedup windows (ProcessedRequestWindow 2947), mode switching/hotbar state (273–308), and presentation feedback events. Every new survival verb touches this one file across 4–5 places, making regressions in the co-op economy channel likely and code review of any change expensive.
- **Evidence:** File is 3,073 lines; the MonoBehaviour spans lines 168–3073. Method map confirms the mixed concerns: TrySubmit* client API (507–1136), ProcessHost* host logic (1138–2031), wire codec (2968–3072), network callback wiring (2620–2780), station persistence (925–1004), inventory snapshots (3005–3056).
- **Recommended fix:** Split along the seams that already exist in the code: (1) a plain-C# SurvivalCommandCodec for the FastBuffer read/write helpers, (2) a SurvivalHostResolver (engine-free where possible) holding the ProcessHost* logic, inventories, dedup windows and station models, (3) a thin MonoBehaviour transport adapter owning NetworkManager callbacks and message registration, (4) move StationPersistentState export/restore next to the persistence mapper. The existing Configure() seam and PlayMode tests make this refactor mechanically safe.

### 2. Save/load orchestration duplicated between BlockiverseWorldSessionController (UI) and MultiplayerWorldPersistence (Gameplay), and the copies have already diverged

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`
- **Impact:** The same five-step save assembly (BuildSaveExtras, BuildSavedContainers) and restore sequence (suppress auto-loot → ApplyTo → RestoreSimulationState → restore clock → SetGameMode → RestoreContainers → RestoreStations) is implemented twice in different assemblies. Divergence #1 is already a defect (see separate fluid-sim finding). Divergence #2: the multiplayer RestoreContainers validates blank canonical ids and non-positive counts (MultiplayerWorldPersistence.cs:324–336) while the single-player copy does not (BlockiverseWorldSessionController.cs:682–698), so a corrupted slot that multiplayer load tolerates makes a single-player load throw inside the ItemId constructor and surface as a generic 'Failed to load the world.' Every future persistence fix must be applied twice or the formats drift.
- **Evidence:** BuildSavedContainers: UI lines 226–252 vs Gameplay lines 278–310 (verbatim duplicate, modulo loop variable). BuildSaveExtras: UI 214–224 vs Gameplay 234–244 (identical). RestoreContainers: UI 682–698 (no slot validation) vs Gameplay 312–341 (validates and logs invalid slots). Autosave Update loop duplicated: UI 98–107 vs Gameplay 248–275.
- **Recommended fix:** Extract a single WorldSessionPersistenceCoordinator (Gameplay layer, since UI already references Gameplay) exposing SaveWorld(savePath, name, …) and RestoreLoadedWorld(WorldLoadResult), used by both the single-player session controller and the multiplayer host persistence. Port the slot-validation guard into the shared restore path.

### 3. Multiplayer host load bypasses RestoreWorldTimeTicks, leaving FluidFlowService's tick anchor stale — duplicated restore path causes an unbounded catch-up loop

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/WorldGen/FluidFlowService.cs`
- **Impact:** When a host loads multiplayer-world.vxlworld, MultiplayerWorldPersistence calls WorldTimeClock.RestoreElapsedTicks directly instead of CreativeWorldManager.RestoreWorldTimeTicks, so fluidFlowService.SyncToWorldTick is never called. The fluid sim's lastWorldTick stays at the value it was configured with at boot (0 for the default world), while the clock jumps to the saved absolute tick. The next world tick then runs FluidFlowService.Tick's catch-up loop from tick 1 to the saved tick — for a world with hours of play time that is hundreds of thousands of loop iterations on the main thread in one frame, and any fluid cells activated by applying the saved block deltas get re-stepped through that entire replay (frame hitch on Quest plus fluid state that differs from the same save loaded in single-player).
- **Evidence:** MultiplayerWorldPersistence.cs:161 `worldManager.WorldTimeClock?.RestoreElapsedTicks(result.Data.WorldTimeTicks);` — contrast with the single-player path BlockiverseWorldSessionController.cs:619 which calls worldManager.RestoreWorldTimeTicks. CreativeWorldManager.cs:195–210 documents exactly why the wrapper exists ('the next Tick must not replay every elapsed tick') and calls fluidFlowService?.SyncToWorldTick(totalElapsedTicks). FluidFlowService.cs:299–315: `for (long tick = lastWorldTick + 1; tick <= worldTick; tick++)` with no cap.
- **Recommended fix:** Change MultiplayerWorldPersistence.cs:161 to call worldManager.RestoreWorldTimeTicks(result.Data.WorldTimeTicks) (the wrapper already buffers when the clock is missing). Defensively, also cap or fast-forward the catch-up loop in FluidFlowService.Tick when the gap exceeds one day of ticks. Add an EditMode test asserting lastWorldTick equals the restored tick after a host load.

### 4. World-preset construction/dispatch logic implemented four times across three assemblies with magic preset strings

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs`, `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs`
- **Impact:** The 'which preset class + which settings for this preset id' decision exists in four places: BlockiverseWorldSessionController.GenerateWorld (new world), BlockiverseWorldSessionController.RegenerateBaseWorld (load), MultiplayerChunkAuthoritySync.GenerateSnapshotWorld (late join), and CreativeWorldManager.CreateDefaultGeneratedWorld (boot). The string ids "flat_builder"/"void_builder"/"survival_terrain" are raw literals at 10+ sites with no shared constants, and the builder ground-height rules differ subtly between the copies (GenerateWorld uses FlatBuilderGroundHeight raw, RegenerateBaseWorld clamps with Math.Min(…, data.Height - 2)). A new preset or a tuning change must be replicated in all four sites or hosts, clients, loads, and new worlds silently generate different baselines — fatal for a delta-only save format and seed-regenerated late-join sync. It also forces the regeneration logic to live in the UI assembly, which is why MultiplayerWorldPersistence cannot regenerate a world itself and instead fails hosting on metadata mismatch (MultiplayerWorldPersistence.cs:142–151).
- **Evidence:** BlockiverseWorldSessionController.cs:301–324 and 653–680 (two near-identical switches in the same file); MultiplayerChunkAuthoritySync.cs:416–452 (third copy, switching on the enum); CreativeWorldManager.cs:719–726 (fourth, survival-only). Preset string literals: NewWorldConfig.cs:15, BlockiverseWorldSessionController.cs:37/305/312/657/666, MultiplayerWorldPersistence.cs:19, WorldSaveService.cs:214/219/252/428.
- **Recommended fix:** Add a WorldPresetCatalog in WorldGen (or Gameplay): canonical preset-id constants plus a single Generate(presetId-or-enum, registry, settings) factory returning GeneratedCreativeWorld-shaped data. Route all four call sites through it and delete the duplicated switches.

### 5. Canonical tick-rate and day-length constants defined independently in three assemblies

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/WorldGen/WorldConstants.cs`, `Assets/Blockiverse/Scripts/Survival/SmeltingModel.cs`, `Assets/Blockiverse/Scripts/Survival/MiningFormula.cs`, `Assets/Blockiverse/Scripts/Survival/FarmingService.cs`, `Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs`
- **Impact:** TicksPerSecond = 20 is declared three times (WorldConstants, SmeltingModel, MiningFormula) and the 24000-tick day twice plus two raw literals. A future tick-rate or day-length change requires edits in Survival, WorldGen, and callers; missing one silently desynchronizes smelting/mining timing from the world clock and from values persisted in saves. VegetationService even hardcodes 24000 twice in the same assembly that owns WorldConstants.TicksPerDay.
- **Evidence:** WorldConstants.cs:10–11 (TicksPerSecond=20, TicksPerDay=24000); SmeltingModel.cs:14 (`public const int TicksPerSecond = 20;`); MiningFormula.cs:11 (same); FarmingService.cs:108 (`public const int TicksPerGameDay = 24000;`); VegetationService.cs:245 (`const long WildRegrowthRetryDelayTicks = 24000;`) and :343 (raw `return 24000;`). Root cause: WorldConstants lives in Blockiverse.WorldGen, which Blockiverse.Survival cannot reference (Survival's asmdef references only Voxel and Survival.Health).
- **Recommended fix:** Move the simulation-time constants (TicksPerSecond, TicksPerDay) down to Blockiverse.Voxel (referenced by both Survival and WorldGen) or Core, then redefine SmeltingModel/MiningFormula/FarmingService constants as aliases of it and replace the raw 24000 literals in VegetationService with WorldConstants.TicksPerDay.

### 6. UI assembly hardcodes WorldGen's internal biome enum order as magic indices

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/WorldGen/SurvivalLiteWorldPreset.cs`
- **Impact:** BlockiverseWorldSessionController.BiomeIndexFor maps menu biome strings to integers 0–6 that must match the declaration order of the internal TerrainBiome enum in WorldGen. Reordering or inserting a biome in TerrainBiome compiles cleanly but silently makes 'starting biome' spawn searches pick the wrong biome — there is no compile-time or test-time link between the two lists.
- **Evidence:** BlockiverseWorldSessionController.cs:386–401: comment 'TerrainBiome is internal to WorldGen; the resolver exposes biome indexes, whose order is canonical' followed by hardcoded `case "meadow": return 0; … case "highlands": return 6;`. SurvivalLiteWorldPreset.cs:8: `enum TerrainBiome { Meadow, Pinewild, Wetland, Drybrush, Dunes, Tundra, Highlands }` (internal, so UI cannot reference it).
- **Recommended fix:** Expose a public mapping from WorldGen — e.g. SurvivalBiomeResolver.TryGetBiomeIndex(string canonicalBiomeId) or a public BiomeIds class listing canonical ids in index order — and use it from the UI. At minimum add an EditMode test that asserts BiomeIndexFor agrees with the enum order.

### 7. Pervasive FindFirstObjectByType/GameObject.Find service-locator idiom (~79 runtime call sites in 36 files) hides the scene wiring contract

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Assets/Blockiverse/Scripts/Gameplay/SurvivalFeedbackBridge.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs`
- **Impact:** Nearly every component lazily resolves collaborators by scanning the scene when its serialized field is null (e.g. BlockiverseWorldSessionController.ResolveReferences resolves five systems, lines 77–94; CreativeWorldManager finds WorldTimeClock/CreativeHotbar/PlacementPreview at 464/679/682; BlockiverseMenuController even calls FindFirstObjectByType<BlockiverseCreativeInputBridge> inline inside a switch case at line 379). This silently masks bootstrapper wiring regressions (a missing serialized reference 'works' by finding whatever instance exists, including a wrong/inactive one), makes initialization order-dependent, and makes dependencies invisible to readers and tests. The code itself acknowledges the fragility: MultiplayerSurvivalSync maintains an inLifecycleResolve flag (lines 2769–2780) solely to warn when the fallback fires outside the expected Awake/OnEnable/Configure window.
- **Evidence:** grep across Assets/Blockiverse/Scripts (excluding Editor) shows 79 FindFirstObjectByType/FindObjectsByType/GameObject.Find call sites in 36 runtime files. Representative: BlockiverseWorldSessionController.cs:81–93, BlockiverseMenuController.cs:212/245/275/332/379, CreativeWorldManager.cs:464/679/682, MultiplayerWorldPersistence.cs:349–359, SurvivalVitalsRuntime.cs:92–108, WeatherFeedbackController.cs:59–65.
- **Recommended fix:** Treat bootstrapper-assigned serialized references as the only sanctioned wiring: extend the MultiplayerSurvivalSync warning pattern (or a shared ResolveOrWarn helper) to all ResolveReferences fallbacks, and add an EditMode test over the generated Boot scene asserting every [SerializeField] dependency of the core systems is non-null so fallbacks become test failures instead of silent scans.

### 8. BlockiverseNetworkAvatarRig falls back to scanning every Transform in the scene, potentially every LateUpdate

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkAvatarRig.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- **Impact:** FindNamedTransform iterates FindObjectsByType<Transform>(FindObjectsInactive.Include) — every transform in the loaded scene — to find controllers by the string names 'Left Controller'/'Right Controller'. ResolveTrackingSources runs from ApplyTrackingSources, which is called every LateUpdate for the owner (and every frame pre-spawn via RefreshLocalTrackingPose). If either hand transform is absent or renamed (headless host, MultiplayerTest scene, bootstrapper rename), this becomes two full-scene scans per frame in a scene that also contains hundreds of generated chunk objects — a sustained CPU drain on Quest. It also couples Networking-layer code to GameObject names authored by the Editor-layer bootstrapper.
- **Evidence:** BlockiverseNetworkAvatarRig.cs:78–94 (LateUpdate → ApplyTrackingSources), 162–164 (ApplyTrackingSources → ResolveTrackingSources), 270–291 (ResolveTrackingSources / FindNamedTransform iterating FindObjectsByType<Transform>(FindObjectsInactive.Include)). Names originate in BlockiverseProjectBootstrapper.cs:1750/1756/1819/1825. Contrast: SurvivalVitalsRuntime throttles its equivalent fallback scan with nextClockSearchTime (SurvivalVitalsRuntime.cs:88–101).
- **Recommended fix:** Throttle the fallback search (copy the SurvivalVitalsRuntime nextClockSearchTime pattern), resolve via XROrigin/camera-offset child lookup (as BlockiverseMetaAvatarPresenter.cs:157–158 already does with cameraOffset.Find) instead of a global Transform scan, and hoist the controller names into BlockiverseProject constants.

### 9. Single-player autosave and multiplayer host autosave are mutually unaware; hosting does not end the single-player session

- **Severity:** Medium  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs`
- **Impact:** BlockiverseWorldSessionController autosaves to currentSavePath whenever HasActiveSession is true, and nothing outside the class ever clears that state — returning to title (PauseReturnToTitle/DeathReturnToTitle) saves but does not end the session, and BlockiverseMultiplayerSessionMenu.StartHost (line 87) never notifies the session controller. If a player loads a single-player world, returns to title, and starts hosting, both autosave Update loops run concurrently against the same CreativeWorldManager world instance: multiplayer mutations from remote clients get autosaved into the player's single-player slot every interval, silently corrupting that slot's intent (and double-writing IO on Quest).
- **Evidence:** BlockiverseWorldSessionController.cs:98–107 (Update autosave gated only on HasActiveSession), 43–44 (HasActiveSession = currentSavePath non-empty); grep shows no external reader/writer of currentSavePath/HasActiveSession anywhere in the codebase. MenuController's PauseReturnToTitle case (BlockiverseMenuController.cs:396–400) raises the save action but never clears the session. MultiplayerWorldPersistence.cs:248–275 is an independent autosave loop. BlockiverseMultiplayerSessionMenu.cs:87 starts hosting with no reference to BlockiverseWorldSessionController.
- **Recommended fix:** Give session lifetime a single owner: have BlockiverseWorldSessionController subscribe to the network session (or expose EndSession()) and clear currentSavePath when hosting/joining starts and when returning to title; alternatively gate the single-player autosave on the chunk-authority boundary being offline.

### 10. BlockiverseProjectBootstrapper is a 4,952-line single static class mixing project config, asset generation, and pixel-level UI construction

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- **Impact:** The bootstrapper is the canonical source of all scene/prefab wiring (per project policy), so its health matters more than a normal editor script. One file/class holds ~141 methods spanning Android/OpenXR/URP player settings, input-action schema surgery, texture/atlas pixel painting (FreshwaterTilePixel etc.), XR rig assembly, and hand-built world-space UI for ~15 panels with hardcoded layout vectors (several methods ~150 lines, e.g. EnsureXrRigComfortMenu 2068–2216, EnsureBlockMenuPlaceholder 2440–2588, EnsureNewWorldMenuPanel 4115–4263). Navigating, reviewing, and safely editing it is slow, and merge conflicts concentrate here since every wiring change must land in this file.
- **Evidence:** wc -l = 4,952; method outline shows 141 method definitions in one static class; Run() at lines 197–219 sequences 15 Ensure*/Configure* phases; UI panel builders run from ~3846 to 4952.
- **Recommended fix:** Split into partial classes or focused static classes by phase, preserving the single Run() entry point: BootstrapperPlayerSettings, BootstrapperAssets (materials/atlas/icons/audio), BootstrapperInputActions, BootstrapperXrRig, BootstrapperMenusUi, BootstrapperNetwork. Pure mechanical extraction; no behavior change, and BlockiverseBootstrapEditModeTests keeps it honest.

### 11. 'Engine-free simulation core' invariant is unenforced (noEngineReferences:false) and already has a UnityEngine-module dependency in WorldGen

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/WorldGen/SurvivalLiteWorldPreset.cs`, `Assets/Blockiverse/Scripts/WorldGen/Blockiverse.WorldGen.asmdef`, `Assets/Blockiverse/Scripts/Voxel/Blockiverse.Voxel.asmdef`, `Assets/Blockiverse/Scripts/Survival/Blockiverse.Survival.asmdef`, `Assets/Blockiverse/Scripts/SurvivalHealth/Blockiverse.Survival.Health.asmdef`
- **Impact:** The project's core architectural invariant — Voxel/Survival/Survival.Health/WorldGen are engine-free so generation can run on Task.Run and tests are plain NUnit — is enforced only by convention: all four asmdefs set "noEngineReferences": false, so nothing stops UnityEngine APIs from creeping in. WorldGen already imports Unity.Profiling (a UnityEngine.CoreModule type) in SurvivalTerrainPreset. ProfilerMarker happens to be thread-safe, but the open door means the next convenience import (Debug.Log, Mathf, Random) silently breaks thread-safety or NUnit isolation with no gate to catch it.
- **Evidence:** SurvivalLiteWorldPreset.cs:4 `using Unity.Profiling;` and :12/:41 ProfilerMarker usage inside Generate(). All four 'engine-free' asmdefs contain "noEngineReferences": false (verified by reading each file). Grep confirms this is currently the only engine using across the four assemblies.
- **Recommended fix:** Either set noEngineReferences:true on the four asmdefs (and move the ProfilerMarker into the Gameplay-side callers, which already have markers in VoxelWorldRenderer), or if the profiling hook is wanted in-place, document the single sanctioned exception and add a CI grep (scripts/ci) rejecting `using UnityEngine` in those folders.

### 12. Gameplay layer reaches the XR rig via GameObject.Find string lookups to work around the assembly layering

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`, `Assets/Blockiverse/Scripts/Core/BlockiverseProject.cs`
- **Impact:** VR sits above Gameplay in the layering, so Gameplay code cannot reference rig types — instead CreativeWorldManager.PositionRigAtSpawn/PositionRig and SurvivalVitalsRuntime.BuildPlayerSaveState locate and mutate the player rig with GameObject.Find("BlockiverseXRRig"). The dependency on the rig is real but invisible to the type system: renaming the rig in the bootstrapper compiles fine and silently breaks spawn positioning, respawn, and player-state saves (they all no-op when Find returns null).
- **Evidence:** CreativeWorldManager.cs:736–753 (two static methods doing GameObject.Find(BlockiverseProject.XrRigRootName), with a comment explaining they are public 'because no InternalsVisibleTo covers the UI assembly'); SurvivalVitalsRuntime.cs:214 (same Find in BuildPlayerSaveState, returns null silently when absent).
- **Recommended fix:** Define a tiny IPlayerRigLocator (or a PlayerRigAnchor MonoBehaviour) in Gameplay that the VR rig registers itself into at OnEnable; Gameplay code then asks the registry instead of string-searching the hierarchy. Keeps the layering and makes the dependency explicit and testable.

### 13. BlockiverseWorldSessionController (UI assembly) owns world generation, persistence orchestration, and file management — application logic in the presentation layer

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`
- **Impact:** The UI assembly's session controller implements seed folding, biome spawn-search math (FindSpawnForBiome, an O(r²) ring scan over SurvivalBiomeResolver), preset construction, save-slot allocation/sanitization, autosave scheduling, and full load/restore sequencing — none of which is UI. Because this logic is trapped at the top of the dependency stack, lower layers cannot reuse it (directly causing the MultiplayerWorldPersistence duplication and its divergences) and it can only be exercised through a MonoBehaviour wired to menu events.
- **Evidence:** 850-line MonoBehaviour in Blockiverse.UI: GenerateWorld/RegenerateBaseWorld (301–324, 653–680), FindSpawnForBiome (344–384), FoldSeed (404), AllocateSavePath/SanitizeFileName (804–848), autosave Update (98–107), full LoadSave restore pipeline (590–651). CLAUDE.md describes UI as 'menu router/panels' yet this class is the de facto application service.
- **Recommended fix:** Move the world-session service (generation, save/load orchestration, slot management) into Gameplay (which already references Persistence and WorldGen) as a plain class or MonoBehaviour; keep a thin UI adapter that translates MenuActions ids into service calls. This also dissolves the UI/Gameplay persistence duplication finding.

### 14. Boot-time synchronous default-world generation in Awake is discarded on most real player paths

- **Severity:** Medium  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`
- **Impact:** CreativeWorldManager.Awake unconditionally generates a full SurvivalLite world (128×256×128 ≈ 4.2M blocks: terrain fill, caves, veins, structures, vegetation) plus a complete renderer rebuild on the main thread at app start. The moment the player picks New World, Load, Continue, or joins a host, that world is thrown away and a second full generation runs — also synchronously on the main thread (BlockiverseWorldSessionController.CreateNewWorld/LoadSave), unlike the late-join path which correctly generates on Task.Run. On Quest-class hardware this doubles time-to-gameplay and produces a long main-thread stall inside menu interactions; the existence of a generated startup loading overlay (bootstrapper EnsureXrRigStartupLoadingOverlay) confirms the stall is real.
- **Evidence:** CreativeWorldManager.cs:728–732 (Awake → InitializeDefaultWorld) and 719–726 (CreateDefaultGeneratedWorld runs SurvivalTerrainPreset.Generate synchronously); BlockiverseWorldSessionController.cs:268 (GenerateWorld on the UI thread inside CreateNewWorld) and 608 (RegenerateBaseWorld inside LoadSave). The async precedent exists at MultiplayerChunkAuthoritySync.cs:379–401 ('World generation is pure C# over engine-free types, safe off the main thread', Task.Run + coroutine completion). Bootstrapper EnsureXrRigStartupLoadingOverlay at line 2588.
- **Recommended fix:** Reuse the StartSnapshotGeneration pattern for all generation paths: generate worlds on Task.Run and initialize the manager when the task completes (the menus are already up, so a progress state is natural). Defer or eliminate the Awake default world — generate it lazily only when the bare sandbox is actually entered.

### 15. WorldSaveService: 1,273-line file with 15 types, a 13-parameter Save overload, and a 240-line WriteRegionFiles method

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`
- **Impact:** The persistence layer concentrates its whole data model and service in one file. Save(string, string, VoxelWorld, Inventory, int, Inventory, string, string, long, IReadOnlyList<SavedContainer>, string, string, WorldSaveExtras) has 13 parameters, which already produces unreadable single-line call sites (MultiplayerWorldPersistence.cs:213 and :266 pass 10 named arguments on one line) and makes adding a save field a signature-breaking change. WriteRegionFiles (lines 448–688) is a single method built around a Dictionary<(int,int), Dictionary<(int,int), Dictionary<int, List<(int,string)>>>> — hard to review and to extend when the region format evolves.
- **Evidence:** WorldSaveService.cs:219 (13-parameter Save signature), 448–688 (WriteRegionFiles, ~240 lines, triple-nested tuple dictionary at line 450), file contains SavedBlockDelta/SavedInventorySlot/…/WorldSaveService (15 top-level types, lines 15–191).
- **Recommended fix:** Introduce a WorldSaveRequest DTO (world, inventory, environment, containers, extras, metadata) so Save takes (path, request); both save paths already build the same shape. Extract the region grouping into a RegionDeltaIndex helper class and split the DTOs into their own file(s).

### 16. UI feedback discovery (audio cue + haptics resolution) copy-pasted across at least 8 panel classes

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseActionMenu.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseWorldSpacePanelPresenter.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalCratePanel.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseComfortMenu.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`
- **Impact:** The identical DiscoverFeedback/PlayFeedback idiom — null-check field, FindFirstObjectByType<BlockiverseAudioCuePlayer>, FindFirstObjectByType<BlockiverseInteractionHaptics>, then PlayCue + PlayUiTick — is re-implemented in SurvivalInventoryPanel, SurvivalCratePanel, SurvivalCraftingPanel, BlockiverseComfortMenu, BlockiverseActionMenu, BlockiverseMultiplayerSessionMenu, BlockiverseWorldSpacePanelPresenter, and BlockiverseMenuController.PlayStationCue. Any change to feedback routing (e.g. a mute setting or pooled players) requires touching all copies.
- **Evidence:** Verbatim duplicates at SurvivalInventoryPanel.cs:218–228, BlockiverseActionMenu.cs:134–147, BlockiverseWorldSpacePanelPresenter.cs:227–238; same pattern at SurvivalCratePanel.cs:187–190, SurvivalCraftingPanel.cs:357–363, BlockiverseComfortMenu.cs:260–263, BlockiverseMultiplayerSessionMenu.cs:386–389, BlockiverseMenuController.cs:324–335.
- **Recommended fix:** Extract a small UiFeedback helper (static or a scene component the bootstrapper wires once) exposing PlayUiCue(BlockiverseAudioCue) that owns discovery/caching; replace the eight copies.

### 17. ~28 test-only telemetry counters baked into MultiplayerSurvivalSync's public API

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Assets/Blockiverse/Tests/PlayMode/Networking/MultiplayerSessionPlayModeTests.cs`
- **Impact:** ReceivedHarvestRequestCount, AcceptedPlaceCount, RejectedCommandCount, etc. (lines 367–395) are public auto-properties incremented throughout the host paths but read only by MultiplayerSessionPlayModeTests. They inflate the god class's public surface, force every new command to remember to bump matching counters, and blur which members are production contract versus test instrumentation.
- **Evidence:** MultiplayerSurvivalSync.cs:367–395 declares 28 public counters; repo-wide grep shows the only consumers outside the two sync classes are Assets/Blockiverse/Tests/PlayMode/Networking/MultiplayerSessionPlayModeTests.cs. MultiplayerChunkAuthoritySync has the same pattern (AppliedSnapshotBlockCount, AppliedGenerationSnapshotCount).
- **Recommended fix:** Collect the counters into one SurvivalSyncDiagnostics struct/class exposed as a single property (or compiled only in development builds), so command code increments diagnostics.X and the production API stays small.

### 18. Naming drift across the three preset identifier systems, including a vestigial wrapper class and a mis-named file

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs`, `Assets/Blockiverse/Scripts/WorldGen/SurvivalLiteWorldPreset.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`
- **Impact:** The same concept carries inconsistent names per layer: enum CreativeWorldGenerationPreset.SurvivalLite ↔ class SurvivalTerrainPreset ↔ string "survival_terrain"; enum FlatCreative ↔ class FlatBuilderPreset ↔ string "flat_builder" — plus FlatCreativeWorldPreset, a class whose Generate() only delegates to FlatBuilderPreset, used at exactly one call site while every other site uses FlatBuilderPreset directly. SurvivalTerrainPreset lives in a file named SurvivalLiteWorldPreset.cs. 'CreativeWorldManager' actually owns worlds for both creative and survival modes. Each mismatch costs navigation time and invites wrong-symbol edits.
- **Evidence:** WorldGenerationSettings.cs:98–113 (FlatCreativeWorldPreset.Generate() => new FlatBuilderPreset(...).Generate()); its only consumer is MultiplayerChunkAuthoritySync.cs:428, while BlockiverseWorldSessionController.cs:308/662 use FlatBuilderPreset. SurvivalLiteWorldPreset.cs:10 declares SurvivalTerrainPreset. CreativeWorldManager.cs:19–25 comment documents it owns survival worlds too. Also CreativeWorldManager.cs:55–60 infers the preset from a magic 'Height >= 32' heuristic.
- **Recommended fix:** Delete FlatCreativeWorldPreset (point the one call site at FlatBuilderPreset), rename SurvivalLiteWorldPreset.cs to SurvivalTerrainPreset.cs, and align enum/class/string naming when introducing the WorldPresetCatalog recommended above. Replace the Height>=32 inference with an explicit preset argument (all real callers already pass one).

### 19. World Details operations resolve saves by display name while the Load path uses collision-safe keys — destructive ops can target the wrong slot

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`
- **Impact:** RefreshSaveList builds savePathsBySummaryKey (name+seed+createdUtc) explicitly because 'two saves share a display name', and LoadSelectedSave prefers it. But Play/Rename/Duplicate/Delete on the World Details screen resolve via TryResolveSavePathByName, which returns the first case-insensitive WorldName match. With colliding display names (saves copied in externally, or a manifest WorldName edited by hand), Delete/Rename can act on a different slot than the one shown — deletion is irreversible. The inconsistency between the two resolution strategies in the same file is the smell.
- **Evidence:** savePathsBySummaryKey + SummaryKey at lines 38–40 and 450–451 (comment: 'pins the exact slot even when two saves share a display name'); LoadSelectedSave uses it (424–446); but PlayDetailsSave (470–479), RenameDetailsSave (483–517), DuplicateDetailsSave (519–539), DeleteDetailsSave (541–568) all call TryResolveSavePathByName (453–466), first-match by name only.
- **Recommended fix:** Route the details-screen operations through the same SummaryKey→path map (the WorldSaveSummary shown on the details panel already carries seed and createdUtc), falling back to name matching only when the map misses.

### 20. Bootstrapper invokes a private Netcode settings method via reflection with a silently-swallowed failure

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- **Impact:** DisableNetcodeDefaultNetworkPrefabs reflects NetcodeForGameObjectsProjectSettings.SaveSettings (private) and calls it through saveSettings?.Invoke — if a Netcode package update renames or removes the method, the null-conditional makes the persistence step a silent no-op and GenerateDefaultNetworkPrefabs quietly reverts on the next editor session, producing confusing prefab-list diffs rather than an error.
- **Evidence:** BlockiverseProjectBootstrapper.cs:244–254: GetMethod("SaveSettings", BindingFlags.Instance | BindingFlags.NonPublic) followed by `saveSettings?.Invoke(settings, null);` with no else/log branch.
- **Recommended fix:** Log a warning (or throw, since this is editor tooling) when the MethodInfo is null so a Netcode upgrade surfaces immediately; check whether the current Netcode 2.x exposes a public save path (e.g. SaveSettings became public or settings.Save()) and use it.

### 21. Stray auto-generated InitTestScene committed at the Assets root and not covered by the forbidden-files CI check

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity`, `scripts/ci/forbidden-files.sh`
- **Impact:** Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity (+ .meta) is a Unity test-runner artifact committed at the Assets root (entered in commit be5ec24). It pollutes the project view next to the two real scenes, can confuse scene-related tooling/searches, and signals that the CI forbidden-files gate misses this whole class of generated artifact.
- **Evidence:** ls Assets/ shows the scene and meta at the root; git log confirms it was committed in be5ec24. scripts/ci/forbidden-files.sh's regex ('^(Library|Temp|Logs|UserSettings)/|…') does not match Assets/InitTestScene*.unity.
- **Recommended fix:** Delete the scene and its .meta, and add `(^|/)InitTestScene[0-9a-f-]*\.unity(\.meta)?$` to the forbidden_regex in scripts/ci/forbidden-files.sh so the test runner can never re-commit one.

### 22. ~35 scattered BlockRegistry/ItemRegistry/CraftingRecipeBook CreateDefault() construction sites with no shared canonical instance

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`, `Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs`, `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- **Impact:** Each registry build registers ~80 definitions; UI panels, services, and sync classes each construct and hold private instances (some per-call, e.g. BlockiverseWorldSessionController.cs:182 builds an ItemRegistry inside the autosave path when survivalSync is null). Beyond redundant allocations, there is no single source of truth at runtime: if defaults ever become data-driven or moddable, instance divergence becomes a correctness bug, and reference-equality can never be used. The duplicated-instances pattern also obscures which registry a given Inventory validates against.
- **Evidence:** Repo grep (excluding tests/editor) shows ~35 CreateDefault() call sites across UI, Gameplay, Persistence and Survival, e.g. SurvivalInventoryPanel.cs:13, SurvivalCratePanel.cs:17 (each a private static instance), MultiplayerSurvivalSync.cs:403–408 and 2793–2796, WorldSaveService.cs:141/168/204/235/390, BlockiverseWorldSessionController.cs:182/303/655.
- **Recommended fix:** Introduce lazily-initialized shared defaults (e.g. BlockRegistry.Default / ItemRegistry.Default static readonly properties — the registries are immutable after construction) and route the ?? CreateDefault() fallbacks through them; keep explicit injection for tests.

### 23. Cross-class temporal couplings documented only in comments (death-respawn-before-save, station-tick clock split)

- **Severity:** Low  |  **Confidence:** Medium  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs`
- **Impact:** Two fragile orderings are load-bearing but enforced by nothing: (1) saving on DeathReturnToTitle is only correct because BlockiverseMenuController.HandleAction calls vitalsRuntime.Respawn() on the statement line before raising ActionRequested — the session controller's comment explicitly depends on that statement order in another assembly's switch case; reordering compiles and silently saves dead-player state. (2) Simulation time is split across two clocks: vegetation/farming/fluids advance on WorldTimeClock ticks while smelting stations advance on raw Time.deltaTime in two places (host tick and panel extrapolation), so any future pause/timescale behavior must be fixed in both systems.
- **Evidence:** BlockiverseWorldSessionController.cs:154–158 ('Order dependency: BlockiverseMenuController respawns the vitals runtime *before* raising DeathReturnToTitle…') paired with BlockiverseMenuController.cs:412–416. Station real-time ticking: MultiplayerSurvivalSync.cs:447–462 (Update, stationTickRemainder += Time.deltaTime * SmeltingModel.TicksPerSecond) and the duplicate extrapolation in BlockiverseStationPanel.cs:95–124.
- **Recommended fix:** (1) Make the save handler itself respawn-safe: have SaveCurrentWorld ask vitalsRuntime for post-respawn state (or have the death flow emit a dedicated 'SaveAfterRespawn' action) instead of relying on statement order in another class. (2) Drive station ticking from WorldTimeClock.Ticked like every other simulation, removing the second time base.

## What Looks Good (8)

- Disciplined assembly layering with zero InternalsVisibleTo and an engine-free simulation core (Voxel/Survival/SurvivalHealth/WorldGen asmdefs reference only lower layers), enabling Task.Run world generation (MultiplayerChunkAuthoritySync.cs:379–401) and plain NUnit EditMode tests.
- Essentially no global mutable state: repo-wide scan found no singletons and only benign statics — a swappable log sink (Core/BlockiverseLog.cs:51), a cached saves root (BlockiverseWorldSessionController.cs:46–49), a cached layer mask (BlockiverseInputRig.cs:87), and [ThreadStatic] mesh pools (ChunkMeshBuilder.cs:40–43). Domain reload is not disabled (ProjectSettings/EditorSettings.asset m_EnterPlayModeOptions: 0), so even these reset between plays.
- MonoBehaviours consistently expose Configure(...) dependency-injection seams (CreativeWorldManager.Configure, MultiplayerSurvivalSync.Configure, BlockiverseWorldSessionController.Configure), which is why 55 test files including real Netcode host/client PlayMode sessions exist for code this integration-heavy.
- Careful event hygiene: subscriptions tracked against the exact instance subscribed (CreativeWorldManager.subscribedWorld, lines 78–80 and 443–450) and torn down in OnDestroy/OnDisable across the codebase; abandoned async snapshot tasks are observed to avoid UnobservedTaskException (MultiplayerChunkAuthoritySync.cs:403–414).
- Performance-conscious patterns appropriate to Quest: collider rebake budgeting (VoxelWorldRenderer.cs:19–53), batched renderer rebuilds after snapshots (MultiplayerChunkAuthoritySync.FinalizeSnapshot), allocation-free scratch buffers with explanatory comments (MultiplayerSurvivalSync.cs:207–211), ProfilerMarkers on hot paths, and per-frame TMP label rebuild avoidance (BlockiverseStationPanel.cs:118–121).
- Canonical names/paths centralized in Core/BlockiverseProject.cs and menu action ids in UI/MenuActions.cs rather than scattered literals.
- The service-locator fallback in MultiplayerSurvivalSync self-reports misuse via the inLifecycleResolve guard (lines 2769–2780) — a thoughtful mitigation other classes would benefit from copying.
- Comments throughout encode the *why* of ordering and invariants (e.g. CreativeWorldManager.cs:194–210 on clock/fluid restore ordering; MultiplayerSurvivalSync.cs:1150–1157 on server-authoritative tool resolution), which made this review's divergence findings possible at all.

## Could Not Review (5)

- Actual on-device cost of the boot-time synchronous default-world generation and of the FluidFlowService catch-up loop — both need Quest profiling; the static analysis establishes the code paths but not the millisecond budgets.
- End-to-end reachability of the dual-autosave overlap (load single-player world → return to title → host LAN) — traced statically through BlockiverseMultiplayerSessionMenu and the session controller, but the full menu routing was not executed.
- Generated Boot.unity/prefab YAML wiring (serialized-reference completeness) — I treated the bootstrapper as the source of truth per project policy and focused on code structure; scene-YAML GUID auditing was left to the wiring-focused review.
- Compilation of the Editor assembly's reference set (it omits direct Voxel/WorldGen/Persistence asmdef references) — usings suggest it only consumes re-exported types from referenced assemblies, and the local test gate reportedly passes, but I could not run a Unity compile to confirm.
- BlockiverseProjectBootstrapper's 141 methods were reviewed via outline and targeted reads (Run, network settings, panel builders), not line-by-line; additional intra-method duplication may exist beyond what is reported.

## Inspected (37)

- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`
- `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`
- `Assets/Blockiverse/Scripts/Gameplay/WorldTimeClock.cs`
- `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseActionMenu.cs`
- `Assets/Blockiverse/Scripts/UI/MenuActions.cs (size/location)`
- `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs (preset strings)`
- `Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs (host-start flow)`
- `Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseWorldSpacePanelPresenter.cs`
- `Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkAvatarRig.cs`
- `Assets/Blockiverse/Scripts/WorldGen/WorldConstants.cs`
- `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs`
- `Assets/Blockiverse/Scripts/WorldGen/SurvivalLiteWorldPreset.cs`
- `Assets/Blockiverse/Scripts/WorldGen/FluidFlowService.cs`
- `Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs (constants)`
- `Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs`
- `Assets/Blockiverse/Scripts/Voxel/ChunkAuthority.cs`
- `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`
- `Assets/Blockiverse/Scripts/Survival/FarmingService.cs (constants)`
- `Assets/Blockiverse/Scripts/Survival/SmeltingModel.cs / MiningFormula.cs (constants)`
- `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs (outline + Run + key methods)`
- `Assets/Blockiverse/Scripts/Core/BlockiverseProject.cs`
- `Assets/Blockiverse/Scripts/Core/BlockiverseLog.cs (static state)`
- `All 12 *.asmdef files under Assets/Blockiverse/Scripts/`
- `ProjectSettings/EditorSettings.asset (EnterPlayModeOptions)`
- `scripts/ci/forbidden-files.sh`
- `Assets/Blockiverse/Tests/EditMode + PlayMode directory listings`
- `Assets/ root listing (stray InitTestScene)`
