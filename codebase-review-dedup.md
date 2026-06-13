# Blockiverse VR Codebase Review Deduplicated Findings

**Summary**

| Metric | Count |
|---|---:|
| Total raw findings in | 245 |
| Total merged findings out | 184 |
| Merged findings with MergedCount > 1 | 36 |
| Raw findings represented by merged findings | 97 |

| Severity | Merged Findings |
|---|---:|
| Critical | 6 |
| High | 31 |
| Medium | 77 |
| Low | 52 |
| Informational | 18 |


## 1. clay_lump has no source — Clay Kiln chain and the entire tier 3-7 metal progression are unreachable

- **Severity:** Critical
- **Impact:** Independent of the UI cap, survival progression hard-stops at tier 2 (flint). clay_lump is consumed by the Clay Kiln recipe (x12) and fired_brick (x2) but is dropped by nothing, crafted by nothing, and absent from both structure loot tables. Without a kiln: no bars, no glass, no water flask, no bucket (needs rosycopper_bar), no Bellows Forge, no bronze/ironroot/deepsteel/starforged tools, no Tiller/Mallet/Sickle of any obtainable kind. Ruleset §13 progression steps 4-13 and the roadmap's 'Initial progression' ('Build clay kiln and bellows forge') cannot be played. Ores gated at tier 3+ (rustcore_ore, umbralite, staropal) can never be mined.
- **Files:** ["Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs", "Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs", "Assets/Blockiverse/Scripts/Survival/BlockHarvestRules.cs", "Assets/Blockiverse/Scripts/WorldGen/StructureService.cs", "docs/rulesets/voxel_survival_ruleset.md"]
- **Evidence:** Ruleset §2: Claybed drops 'clay_lump ×2–4'; River Silt 20% clay_lump. Implementation: ItemRegistry.cs:35 maps BlockRegistry.Claybed to ItemId.Claybed (drops itself); ItemRegistry.cs:104 registers ClayLump with no block mapping; repo-wide grep for ClayLump in runtime code finds only the two consuming recipes (CraftingRecipeBook.cs:56, 72) and the registration. The only DropTables in the codebase are reedgrass fiber and resin knot (BlockHarvestRules.cs:124, 152-153). Structure loot tables (StructureService.cs:505-526) contain no clay. Structures place only Campfires (StructureService.cs:227), never a kiln. Tests inject ClayLump directly into inventories (SmeltingStationModelEditModeTests.cs:18) so no test covers acquiring it.
- **RecommendedFix:** Implement the §2 drop column for claybed (clay_lump ×2-4) and river_silt (20% clay_lump ×1) via DropTable entries in BlockHarvestRuleSet.CreateDefault, and add an integration test that walks the full §13 progression (gather → kiln → forge → ironroot) using only obtainable items.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 2. Late-join world snapshot exceeds NGO's non-fragmented named-message cap (~1264 bytes) after ~77 changed blocks, breaking late join in any real session

- **Severity:** Critical
- **Impact:** After roughly 77 changed blocks accumulate in the hosted world (player edits, fluid flow, crop growth, snow accumulation — fluid and growth mutations are tracked changes, so this threshold is crossed within minutes even without heavy building), SendLateJoinSnapshot throws an OverflowException on the host inside OnClientConnectedCallback. The joining client never receives the world snapshot, hasHostGenerationSnapshotForSession stays false, and every interaction is rejected with 'waiting for the host-owned world generation snapshot' forever. Late join — the core co-op entry flow — is effectively broken for any world that has been played.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Library/PackageCache/com.unity.netcode.gameobjects@beeaefb722f7/Runtime/Messaging/CustomMessageManager.cs", "Library/PackageCache/com.unity.netcode.gameobjects@beeaefb722f7/Runtime/Messaging/NetworkMessageManager.cs"]
- **Evidence:** MultiplayerChunkAuthoritySync.SendLateJoinSnapshot (lines 676-702) writes a 36-byte header plus 16 bytes per changed block (WriteWorldSnapshotHeader lines 1072-1089 writes 8 ints + 1 uint; per block 3 ints position + 1 int blockId) and sends via CustomMessagingManager.SendNamedMessage with the default delivery. NGO 2.11.2: SendNamedMessage default is NetworkDelivery.ReliableSequenced (CustomMessageManager.cs:300) and ValidateMessageSize (CustomMessageManager.cs:435-451) throws OverflowException for non-fragmented deliveries above NonFragmentedMessageMaxSize = 1296 minus headers (NetworkMessageManager.cs:113). (1264-36)/16 ≈ 76 blocks. In release builds the dev-only validation is compiled out but NetworkMessageManager.SendMessage line 623-625 caps the serializer at NonFragmentedMessageMaxSize for non-fragmented delivery, so the oversized write still fails. World-sim mutations are tracked: VoxelWorld.SetBlock defaults trackChange:true and FluidFlowService/FarmingService use that default. PlayMode tests use 8x8x8 worlds with 1-3 edits, far below the limit.
- **RecommendedFix:** Send the late-join snapshot with NetworkDelivery.ReliableFragmentedSequenced AND split it into bounded chunk messages (UnityTransport's default MaxPayloadSize is 6144 — UnityTransport.cs:59 — so even fragmented delivery caps near 6 KB unless raised). A robust shape: a snapshot-header message (settings, seed, delta sequence, total block count) followed by N block-batch messages of <= ~1 KB each, applied when all batches arrive. Add a PlayMode test that mutates 200+ blocks before a late join.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["lan-multiplayer"]
- **MergedCount:** 1

## 3. Menu system wiring lives in non-serialized fields set at editor time — entire menu UI is inert at runtime

- **Severity:** Critical
- **Impact:** In any fresh scene load (device build, or pressing Play after a domain reload) the title menu never appears, no screen presenter is ever shown or hidden, no button-list menu has an ActionInvoked subscriber, the hardware Menu button has no pause listener, and the New World, Load World, and Station panels have null control references. The core single-player flow (title → new world → gameplay → pause → save) is unreachable.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseNewWorldPanel.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseLoadWorldPanel.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab"]
- **Evidence:** BlockiverseMenuController.cs:16-32 declares inputRig, titleMenu, pauseMenu, deathMenu, confirmMenu, settingsMenu, newWorldPanel, loadWorldPanel, worldDetailsPanel, worldDetailsMenu as plain private fields (only stationPanel at line 28 has [SerializeField]); screenPresenters (line 32) is a runtime List. They are populated only by the editor-time bootstrapper: BlockiverseProjectBootstrapper.cs:3873-3881 (controller.Configure / ConfigurePresenters / ConfigureStationPanel inside EnsureXrRigGameMenus, called from CreateXrRigInstance:1771 before PrefabUtility.SaveAsPrefabAsset:1096/1107). Unity does not serialize private fields without [SerializeField] — confirmed in the prefab: BlockiverseXRRig.prefab:43425-43429 shows the BlockiverseMenuController component serializing ONLY 'stationPanel: {fileID: 505366546020636366}'; the BlockiverseNewWorldPanel block (prefab line 53286-53288) and BlockiverseLoadWorldPanel block (line 33990-33992) serialize zero fields; BlockiverseStationPanel (line 15583-15586) serializes only survivalSync. Boot.unity instantiates this prefab with no added components or relevant overrides. At runtime, BlockiverseMenuController.Start() (lines 193-215) therefore runs with all menu references null and an empty screenPresenters list — ApplyRouterState() shows nothing (all panel canvases are bootstrapped with canvas.enabled=false, e.g. BlockiverseProjectBootstrapper.cs:4041,4134,4283), WireMenus() subscribes to nothing, and inputRig is null so MenuPressed is never handled (the bootstrapper even scrubs the persistent OnMenuPressed listener at lines 3919-3926). BlockiverseNewWorldPanel.WireControls/ResetForNewWorld, BlockiverseLoadWorldPanel.WireControls, and BlockiverseStationPanel.Configure/ConfigureTransferControls (runtime onClick.AddListener calls) all depend on the same lost references. By contrast, correctly-built siblings (BlockiverseMultiplayerSessionMenu.cs:14-21, BlockiverseAudioSettingsPanel.cs:13-23, BlockiverseWorldDetailsPanel.cs:14-15, BlockiverseActionMenu.cs:35-39) use [SerializeField] and survive — their data is visible in the prefab YAML (e.g. lines 18701-18711, 44914-44927). No code outside the editor bootstrapper ever calls these Configure methods (verified by grep), and no test exercises BlockiverseMenuController (zero references under Assets/Blockiverse/Tests).
- **RecommendedFix:** Mark every bootstrapper-wired view reference [SerializeField] (BlockiverseMenuController menu/panel/inputRig fields; all control fields of BlockiverseNewWorldPanel, BlockiverseLoadWorldPanel, BlockiverseStationPanel) and replace the screenPresenters tuple list with a serializable struct list (string screenId + presenter reference) that ConfigurePresenters fills, then rerun the bootstrapper to bake the references into the prefab. Alternatively add a runtime wiring component that re-discovers the named panels under Camera Offset in Awake. Add a PlayMode regression test that loads Boot.unity and asserts the title-menu canvas becomes visible and that clicking its first button raises an action.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["ui-menu-flow", "unity-csharp", "runtime-wiring"]
- **MergedCount:** 3

## 4. New World and Load World panels serialize zero control references — panels are non-functional even if the menu controller is fixed

- **Severity:** Critical
- **Impact:** The New World screen (name/seed inputs, mode/difficulty/size/preset/biome cycle buttons, Create/Cancel) and Load World screen (save rows, Load/Details/Cancel buttons, selection label) have no references to their own UI controls at runtime: WireControls() runs over null/empty arrays, ResetForNewWorld()/SetSaves() update nothing, and ActionRequested (NewWorldCreate/LoadWorldLoad) can never fire. Single-player world creation and loading are impossible through UI regardless of the controller fix, making this a second independent break in the core session flow.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseNewWorldPanel.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseLoadWorldPanel.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab"]
- **Evidence:** BlockiverseNewWorldPanel.cs lines 13-20: `TMP_InputField nameInput; TMP_InputField seedInput; Button[] cycleBackButtons; ... Button createButton; Button cancelButton; TMP_Text errorLabel;` — none are [SerializeField]; they are assigned only by Configure(...) which the bootstrapper calls at editor time (BlockiverseProjectBootstrapper.cs:4248 `panel.Configure(nameInput, seedInput, backButtons, nextButtons, valueLabels, ...)`). Same for BlockiverseLoadWorldPanel.cs lines 15-21 (entryButtons/entryLabels/loadButton/detailsButton/cancelButton/selectionLabel) wired at bootstrapper line 4347. Ground truth: in the committed BlockiverseXRRig.prefab, the MonoBehaviour blocks for both BlockiverseNewWorldPanel (guid 537d089e18dc6450b9b259f6be1a7f57) and BlockiverseLoadWorldPanel (guid 42f1808d583684ac3a0f3aa4511422ef) contain NO serialized fields after m_EditorClassIdentifier — empty. Contrast with sibling panels done correctly (BlockiverseAudioSettingsPanel, BlockiverseCatalogBrowserPanel, SurvivalInventoryPanel all serialize full control arrays in the same prefab).
- **RecommendedFix:** Add [SerializeField] to all control-reference fields in BlockiverseNewWorldPanel and BlockiverseLoadWorldPanel (matching the pattern in BlockiverseAudioSettingsPanel/SurvivalInventoryPanel), keep Configure() for tests, move the WireControls() listener attachment into Awake() (idempotent), and rerun the bootstrapper to regenerate the rig prefab with the references persisted.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["runtime-wiring"]
- **MergedCount:** 1

## 5. Single-player save slot silently overwritten with multiplayer/host world after joining or hosting a LAN session

- **Severity:** Critical
- **Impact:** A player who plays a single-player world and later joins (or hosts) a multiplayer session in the same app run gets their single-player save irreversibly replaced. BlockiverseWorldSessionController never clears currentSavePath when a network session starts; its autosave (Update, 300 s cadence) and the PauseSaveGame/PauseReturnToTitle/TitleQuit handlers keep calling SaveCurrentWorld(), which writes whatever world CreativeWorldManager currently holds. After a client join, FinalizeSnapshot (MultiplayerChunkAuthoritySync.cs:478-513) replaces worldManager.World with the host's regenerated world, so the next autosave writes the host's seed/dimensions/deltas into the player's single-player .vxlworld directory — the original world is gone.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs"]
- **Evidence:** BlockiverseWorldSessionController.cs:44 `HasActiveSession => !string.IsNullOrEmpty(currentSavePath)`; Update() lines 98-107 autosaves while HasActiveSession; HandleAction lines 153-161 saves on TitleQuit/ReturnToTitle. currentSavePath is only ever cleared in DeleteDetailsSave (line 562) — grep confirms no clear on host start/client join. MultiplayerChunkAuthoritySync.FinalizeSnapshot lines 490-493 calls worldManager.InitializeGeneratedWorld with the host's world, replacing the world the SP autosave then persists to currentSavePath.
- **RecommendedFix:** Clear the single-player session (currentSavePath/currentWorldName) — or at minimum suspend SaveCurrentWorld — whenever a network session starts (subscribe to BlockiverseNetworkSession host/client start events or check NetworkManager.IsListening inside SaveCurrentWorld/Update). Also guard SaveCurrentWorld against saving when worldManager.World's seed/dimensions no longer match the manifest at currentSavePath.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["game-logic", "lan-multiplayer", "ui-menu-flow", "anti-pattern"]
- **MergedCount:** 4

## 6. Survival crafting UI exposes only the first 5 of ~60 recipes — no tool or station is craftable in-game

- **Severity:** Critical
- **Impact:** The survival core loop dead-ends minutes in. The 5 visible recipes (registration order) are Work Plank, Stout Pole, Fiber Cord, Stone Rubble, and Glowwick. Campfire (index 5), Flint Carver (6), Build Table (7), every tool, and every station are beyond the fixed 5 buttons, and the panel has no paging or scrolling. Without the Build Table no tool exists; without a tier-1 Delver even graystone (harvestTierMin 1) is unharvestable. Tests pass because they call TryCraftByOutput directly, which no UI path uses.
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs", "Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab"]
- **Evidence:** BlockiverseProjectBootstrapper.cs:3024 'TMP_Text[] recipeLabels = new TMP_Text[5]'; the generated prefab contains exactly 'Recipe 1'..'Recipe 5' objects (grep of BlockiverseXRRig.prefab). SurvivalCraftingPanel.Refresh() (lines 218-232) fills only recipeLabels.Length rows from the head of GetSortedRecipes(); TryCraftAtIndex (104-118) maps the 5 buttons to indices 0-4; comment at lines 276-277 acknowledges 'limited recipe slots'. TryCraftByOutput callers: only Assets/Blockiverse/Tests (EditMode SurvivalUiEditModeTests.cs:79, PlayMode MultiplayerSessionPlayModeTests.cs:1755). CraftingRecipeBook.CreateDefault registers BuildTable as the 8th recipe (line 49).
- **RecommendedFix:** In BlockiverseProjectBootstrapper.EnsureSurvivalCraftingSection, generate a scrollable/paged recipe list (or category tabs per voxel_survival_menus.md §6.10) sized to the full recipe book, and add paging support to SurvivalCraftingPanel (e.g. page offset + next/prev buttons feeding TryCraftAtIndex(pageOffset+index)). Add a PlayMode test that crafts a Build Table through the actual panel buttons.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 7. Art generator script is two milestones stale; regenerating destroys the committed block atlas

- **Severity:** High
- **Impact:** CLAUDE.md instructs "never hand-author; regenerate instead" via python3 scripts/art/generate-art-assets.py, but the committed script (last touched in PR #304) predates the art shipped in PRs #305/#306/#311. Running it overwrites the committed 128x160 (8x10, 76-tile) atlas with a 128x112 (8x7, 50-tile) one — deleting tiles 50–75 (lumen_lamp, spark_flare, tended_soil, all crop/sapling growth stages, smooth_branchwood, reed_basket, tool_rack, pantry_jar, deep_locker, freshwater/brine/emberflow), rewrites blockiverse_block_atlas.png.meta with maxTextureSize 128 (committed: 256, atlas is 160 tall so Unity would downscale it), and reverts the corrected fired_brick item icon (verified pixel-different from script output). BlockVisualAtlas.IsAuthoredAtlasTexture requires exactly 8x10x16, so the world renderer would throw at runtime. The script also cannot produce 79 of the 142 committed item icons and 23 of the 79 block source tiles (tool tiers 3–7, foods, buckets, growth stages, etc.) — there is no committed generator for the current art, so the documented pipeline is unusable for any future art change.
- **Files:** ["scripts/art/generate-art-assets.py", "scripts/art/test_generate_art_assets.py", "Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png", "Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png.meta", "Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs", "Assets/Blockiverse/Tests/EditMode/M4ArtAssetValidationEditModeTests.cs"]
- **Evidence:** scripts/art/generate-art-assets.py:14-15 `ATLAS_COLUMNS = 8 / ATLAS_ROWS = 7` and BLOCKS list ends at tile 49 (mend_bench), ITEMS list has 63 entries; BlockVisualAtlas.cs:11-12 `Columns = 8; Rows = 10` with TileIndexByBlockId mapping up to index 75; committed atlas PNG header is 128x160; committed atlas .meta maxTextureSize: 256 vs script write_texture_meta max_size=max(8,7)*16=128 (generate-art-assets.py:647); pixel-diff run: 'item icons script would change: 1 [fired_brick DIFFERENT]'; scripts/art/test_generate_art_assets.py:78 asserts ATLAS_ROWS == 7, codifying the stale state. git log: script last changed in #304; atlas changed in #305/#306/#311.
- **RecommendedFix:** Recreate the missing generator coverage: extend BLOCKS to all 76 atlas tiles (matching BlockVisualAtlas.TileIndexByBlockId), set ATLAS_ROWS = 10, extend ITEMS to all 131 registered item ids (reuse the tier/class tool composition from ItemRegistry), write atlas meta with max_size >= 160, and update scripts/art/test_generate_art_assets.py to the new dimensions. Verify regeneration is byte/pixel-stable against the committed art (the committed sources for the 50 known blocks and 62/63 known items already match the script's pixel functions exactly, so only the new entries need porting). Until then, add a guard or README warning that the script must not be run.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["asset-integration", "asset-integration-run1", "accessibility"]
- **MergedCount:** 3

## 8. Attacker-controlled string length in named-message handlers triggers multi-GB host allocation (deserialization DoS)

- **Severity:** High
- **Impact:** Any unauthenticated LAN peer can crash or severely stall the host (and thus drop the entire co-op session for all players) by sending a single ~16-byte malformed command. Because the host owns all world generation, mutation validation and the survival economy, killing the host process ends the game for everyone.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Library/PackageCache/com.unity.netcode.gameobjects@beeaefb722f7/Runtime/Serialization/FastBufferReader.cs"]
- **Evidence:** Host-side handlers read attacker-controlled strings with no length sanity check: HandleCommandRequestMessage reads `reader.ReadValueSafe(out string outputItemId)` (MultiplayerSurvivalSync.cs:2153), ReadItemStack reads `reader.ReadValueSafe(out string itemId)` (line 3048) for crate/station commands, and HandlePlayerHelloMessage reads `reader.ReadValueSafe(out string guid)` (line 2698). In NGO 2.11.2, FastBufferReader.ReadValueSafe(out string) (FastBufferReader.cs:603-641) reads the length with the NON-range-checked `ReadLength(out int length)` (line 618 -> 664-668, casts uint->int with no bound), then bounds-checks via `TryBeginReadInternal(length * sizeof(char))` (line 620). With a declared length of 0x40000000 the `length * 2` multiply overflows signed int to a negative value, so TryBeginReadInternal (line 439: `Position + bytes > Length`) returns true and the guard is bypassed; line 624 then executes `s = "".PadRight(length)`, attempting a ~2 GB string allocation from a tiny packet. NGO's HandleMessage wraps handlers in try/catch (NetworkMessageManager.cs:420-427) so an eventual OutOfMemoryException is caught, but the oversized allocation attempt itself is the DoS on the 8 GB shared-memory Quest.
- **RecommendedFix:** Do not feed attacker-controlled buffers to ReadValueSafe(out string) unbounded. Before each string read, validate a maximum length against the remaining buffer (e.g. read a uint length, reject if it exceeds a small per-field cap like 64 and/or `reader.Length - reader.Position`), or read canonical ids as a fixed-cap byte span (mirroring the 64 KiB clamp already used in MetaAvatarStreamMessage.NetworkSerialize). Apply the same guard to every ReadValueSafe(out string) reachable from a client message (outputItemId, itemId in ReadItemStack, guid, and the host->client activeOutputItemId at line 2378).
- **Confidence:** Medium
- **NeedsManualVerification:** true
- **Sources:** ["security"]
- **MergedCount:** 1

## 9. Blockiverse/Voxel Lit shader is referenced by nothing and will be stripped from Quest builds, disabling all voxel lighting visuals

- **Severity:** High
- **Impact:** The entire voxel lighting feature (VoxelSkyLightMap sky light, cave darkness, emissive blocks like glowwick/lumen_lamp/spark_flare/campfire, per-face light baked by ChunkMeshBuilder) is rendered by multiplying vertex color in the custom shader. The shader is resolved only via Shader.Find at runtime; since no material, scene, prefab, or the Always Included Shaders list references it, Unity strips it from APK builds. On device, CreateBaseMaterial silently falls back to the chunk material's URP/Lit shader, which ignores vertex colors — so caves render fully lit, emissive blocks cast no visible light, and day/night face shading is lost. Editor and PlayMode tests are unaffected (Shader.Find works there), so the regression is invisible to the only test gate.
- **Files:** ["Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader", "Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs", "Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs", "ProjectSettings/GraphicsSettings.asset", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs", "Assets/Blockiverse/Materials/BlockiverseTestBlock.mat"]
- **Evidence:** BlockVisualAtlas.cs:212 `Shader voxelShader = Shader.Find(VoxelLitShaderName);` with silent fallback to sourceMaterial's shader (lines 213-225); shader guid 0447044b0998a413fa33570b9cb06621 appears in zero .unity/.prefab/.mat/.asset files (repo-wide grep); GraphicsSettings.asset m_AlwaysIncludedShaders contains only built-in shaders (lines 29-36); all three committed materials use URP Lit/Unlit guids (933532a4…, 650dd952…); the bootstrapper contains no occurrence of 'VoxelLit'/'Voxel Lit'/'AlwaysIncluded'; ChunkMeshBuilder.cs:186 adds per-vertex light colors that only the custom shader's `* input.color` (BlockiverseVoxelLit.shader frag) consumes; BlockiverseBuildSmoke.cs does not touch GraphicsSettings.
- **RecommendedFix:** Make the shader reachable in builds: either (a) have the bootstrapper assign Blockiverse/Voxel Lit as the shader of BlockiverseTestBlock.mat (the serialized chunk material in Boot.unity), which both includes the shader and removes the runtime swap, or (b) have the bootstrapper add the shader to GraphicsSettings m_AlwaysIncludedShaders. Also change CreateBaseMaterial to log a warning (BlockiverseLog) when the voxel shader cannot be found so device logs reveal the fallback.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["asset-integration", "asset-integration-run1", "dead-code"]
- **MergedCount:** 3

## 10. Custom spawn position is a world-generation input but is never persisted or transmitted — loaded worlds and late-join clients regenerate different terrain

- **Severity:** High
- **Impact:** The new-world flow picks a custom spawn via FindSpawnForBiome (also triggered for 'balanced' worlds whose center is under water). SurvivalTerrainPreset uses settings.SpawnPosition to flatten the spawn area and to exclude caves/fluids/structures/vegetation/resources from spawn-protected columns. But the manifest stores no spawn position, RegenerateBaseWorld reconstructs settings without it, and the chunk-snapshot header omits it. Result: (1) on every load of such a world the baseline terrain differs from the original — the spawn clearing reverts to raw terrain (player structures float/clip) and a spurious flattened circle appears at the world center; (2) a late-joining client regenerates terrain that differs from the host's around both spawn areas, causing visible divergence and ExpectedBlockMismatch rejections.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/WorldGen/SurvivalLiteWorldPreset.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Assets/Blockiverse/Scripts/Persistence/WorldSaveDirectorySchema.cs"]
- **Evidence:** Creation: BlockiverseWorldSessionController.GenerateWorld lines 319-320 passes `spawn` into WorldGenerationSettings. Generation consumes it: SurvivalLiteWorldPreset.cs FlattenSpawnSurface (lines 126-147) and IsInsideSpawnProtectedColumn used at lines 269, 391, 403, 525, 543, 615, 696. Load: RegenerateBaseWorld lines 675-676 builds `new WorldGenerationSettings(data.Width, ..., WorldConstants.SeaLevel)` with no spawnPosition. Wire: WriteWorldSnapshotHeader (MultiplayerChunkAuthoritySync.cs:1072-1089) writes width/height/depth/chunkSize/seed/groundHeight only. WorldSaveDirectorySchema.cs has no spawn field (grep 'Spawn' returns nothing).
- **RecommendedFix:** Persist SpawnPosition in the manifest (schema bump) and include it in the chunk-snapshot header; reconstruct WorldGenerationSettings with it in RegenerateBaseWorld and HandleChunkSnapshotMessage. Alternatively, derive the spawn deterministically from the seed inside the preset so it is never an external input.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 11. Damage, low health, and death have no audio or haptic channel — HUD text is the only feedback

- **Severity:** High
- **Impact:** Taking hazard damage (campfire, thornbrush), starvation/dehydration damage, reaching critical health, and dying produce no sound, no haptic pulse, and no in-view flash; the only signal is a number/bar changing on the body-locked Survival HUD panel, which refreshes on a 0.5 s cadence and may not be in the player's view. Players with low vision, attention differences, or simply looking elsewhere die without warning. This violates the project's own multi-channel rule (voxel_audio_vfx_ruleset §15: important events need at least two feedback channels).
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs", "Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs", "Assets/Blockiverse/Scripts/Gameplay/BlockiverseAudioCuePlayer.cs", "scripts/audio/generate-audio.py", "Assets/Blockiverse/Scripts/VR/BlockiverseInteractionHaptics.cs", "docs/rulesets/voxel_survival_menus.md", "Assets/Blockiverse/Scripts/Gameplay/SurvivalFeedbackBridge.cs"]
- **Evidence:** SurvivalVitalsRuntime applies damage (line 142 `Vitals.ApplyDamage(starvationDamage)`, lines 188–195 hazard ApplyTick) with no feedback call. The only consumers of PlayerVitals.HealthChanged/Died are SurvivalHealthPanel.Refresh (text/slider, lines 127–130) and the death-screen route in BlockiverseMenuController.HandleLocalPlayerDied. The BlockiverseAudioCue enum (BlockiverseAudioCuePlayer.cs lines 8–39) contains no damage/hurt/low-health/death cue, and Assets/Blockiverse/Audio/ contains no hurt clip (generate-audio.py has no such generator). SurvivalHealthPanel's 'Critical' state (line 137) is plain text with no color, sound, or haptic escalation.
- **RecommendedFix:** Add DamageTaken / LowHealthWarning / PlayerDied cues to BlockiverseAudioCue, generate clips in scripts/audio/generate-audio.py, assign them in the bootstrapper's ConfigureGeneratedAudioClips, and have SurvivalVitalsRuntime (or a small vitals feedback bridge like SurvivalFeedbackBridge) subscribe to Vitals.HealthChanged/Died to play the cue plus a haptic pattern (scaled by BlockiverseFeedbackSettings.ResolveHapticIntensity, respecting ReducedFlash for any visual pulse). Add a periodic low-health/low-hunger/low-thirst warning when values cross thresholds.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["accessibility", "game-design", "vr-interaction"]
- **MergedCount:** 3

## 12. Dying while any non-HUD screen is open never shows the death screen and can permanently save a dead player

- **Severity:** High
- **Impact:** Because simulation continues during menus (see pause finding), a player can reach 0 health with the pause/settings/creative-tools/LAN screen open. HandleLocalPlayerDied only shows the death screen when the active screen is the gameplay HUD, and LocalPlayerDied fires exactly once, so the player resumes into gameplay dead with no respawn UI. Choosing Return to Title then saves Health=0 via BuildPlayerSaveState; reloading restores the dead vitals without re-raising the death event, leaving that save permanently stuck with a dead, unrespawnable player.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs"]
- **Evidence:** BlockiverseMenuController.HandleLocalPlayerDied (lines 257-268) returns without showing the death screen unless router.ActiveScreen is GameplayHudScreen (it closes only an open station panel first). SurvivalVitalsRuntime raises LocalPlayerDied solely from Vitals.Died (lines 269-272) — a one-shot transition; nothing re-checks IsDead when returning to the HUD. PauseReturnToTitle (BlockiverseMenuController.cs:396-399) does not respawn (only DeathReturnToTitle does, line 412-417), and BlockiverseWorldSessionController.HandleAction saves on PauseReturnToTitle (lines 153-161) with BuildPlayerSaveState persisting Vitals.CurrentHealth verbatim (SurvivalVitalsRuntime.cs:212-230); RestorePlayerSaveState (lines 235-248) restores that 0 health on load.
- **RecommendedFix:** In HandleLocalPlayerDied, clear the screen stack to the HUD (or pop to it) before pushing the death screen regardless of the active screen, and/or re-check vitalsRuntime.Vitals.IsDead whenever the router returns to GameplayHudScreen and show the death screen then. Clamp restored health to a minimum of 1 (or trigger the death flow) in RestorePlayerSaveState when a save contains a dead player.
- **Confidence:** Medium
- **NeedsManualVerification:** true
- **Sources:** ["ui-menu-flow"]
- **MergedCount:** 1

## 13. Every surface block edit synchronously rebuilds up to ~63 chunk meshes due to the ±13-block lighting invalidation halo

- **Severity:** High
- **Impact:** Placing or breaking a block that changes a column's sky profile marks every chunk in a 27×27-column halo (LightingProbeInvalidationPadding = DefaultProbeDistance 12 + 1 = 13) from y=0 up to the surface (~y 96-110) dirty: 3×3 chunk columns × 7 vertical chunks ≈ up to 63 chunks. RebuildDirty then rebuilds them all in the same frame (called synchronously from the interaction handler and from the world tick). Each rebuild is a 4096-block walk with per-rendered-face cave-light probes (up to 5 directions × 12 steps = 60 block reads per face) plus Mesh.RecalculateNormals and a queued collider rebake — a guaranteed multi-millisecond to tens-of-milliseconds CPU spike per edit on Quest, i.e. dropped frames on the core build/mine interaction. Underground edits still mark up to 27 chunks (±13 in y too). The code's own warning threshold (LargeDirtyRebuildWarningThreshold = 8 drained chunks) confirms this fires in normal play.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs", "Assets/Blockiverse/Scripts/Gameplay/VoxelLightSampler.cs", "Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeInteractionController.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs"]
- **Evidence:** ChunkMeshBuilder.cs:205 `LightingProbeInvalidationPadding = VoxelLightSampler.DefaultProbeDistance + 1` with VoxelLightSampler.cs:11 `DefaultProbeDistance = 12`; ChunkRebuildQueue.OnBlockChanged (ChunkMeshBuilder.cs:245-281) — sky-profile change branch calls MarkLightingAffectedChunks(x, z, 0, maxY) marking chunk range minX=x-13..maxX=x+13 etc. (lines 283-301); VoxelWorldRenderer.RebuildDirty (109-133) rebuilds all drained chunks synchronously and warns at ≥8 (line 13, 126-132); called per edit from CreativeInteractionController.RebuildChangedChunks (`worldRenderer?.RebuildDirty()`) and per tick from CreativeWorldManager.cs:658. Per-face probe cost: VoxelLightSampler.SampleAirLight lines 41-67.
- **RecommendedFix:** Decouple lighting refresh from immediate remesh: (1) budget RebuildDirty (N chunks per frame, nearest-first) the same way collider rebakes are budgeted — visuals of distant lighting halo chunks can trail by a few frames without being noticeable; (2) shrink the invalidation set — only chunks whose visible faces actually border the changed column within probe range need a lighting repaint (a per-chunk 'light dirty only' rebuild could skip geometry regeneration and just rewrite vertex colors); (3) consider caching per-cell light values per chunk so an unchanged-geometry rebuild is a color-array update instead of a full probe walk.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["performance"]
- **MergedCount:** 1

## 14. Farming loop is dead: Tiller unobtainable, three of four seeds have no source, and crops return no seeds

- **Severity:** High
- **Impact:** The §11 farming system (tended soil, deterministic growth, four crops) is fully built in FarmingService but no player can ever use it: tilling requires a Tiller, and the only Tiller recipes are metal-tier at the Bellows Forge (blocked by the clay break); berry_seed, drygrass_seed, and reed_cutting are dropped by nothing and appear in no loot table; meadow_seed exists only as rare structure-crate loot (weight 7/45 in loot_forager_food) and harvested crops drop no seeds back, so farming could never be self-sustaining even with a tiller. Ruleset §2 specifies renewable seed sources (meadow_turf 15%, leafmoss 20% meadow_seed, dry_turf 10% drygrass_seed).
- **Files:** ["Assets/Blockiverse/Scripts/Survival/FarmingService.cs", "Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs", "Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs", "Assets/Blockiverse/Scripts/WorldGen/StructureService.cs", "docs/rulesets/voxel_survival_ruleset.md"]
- **Evidence:** FarmingService.CropForSeed (lines 99-105) maps four seed items to crops; PlantSeed requires TendedSoil which only Till() creates; MultiplayerSurvivalSync.ProcessHostTill (line 1390-1392) requires HarvestToolKind.Tiller. CraftingRecipeBook.RegisterToolRecipes (lines 129-153) registers tillers only for rosycopper/bronze/ironroot/deepsteel/starforged at CraftingStation.BellowsForge. Grep for MeadowSeed/BerrySeed/DrygrassSeed/ReedCutting sources: only ItemRegistry registration and the single meadow_seed loot entry (StructureService.cs:523). ItemRegistry drop aliases (lines 158-172) give crops GrainBundle/BerryCluster/ReedFiber — never seeds.
- **RecommendedFix:** Add the ruleset's secondary seed drops to turf/leafmoss harvest rules; make mature crop harvests return 1-2 seeds (standard sandbox-farming convention, consistent with §11.2 yields); and give the Tiller an early-game recipe (e.g. reedwood/flint tiller at the Build Table) or move tended-soil creation to an obtainable tool.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 15. Fluid spread re-triggers the chunk rebuild storm 4×/second and cooks fluid MeshColliders synchronously every rebuild

- **Severity:** High
- **Impact:** Freshwater steps every 5 world ticks (0.25 s; emberflow every 12). Every cell a flow writes (SetBlock) fires ChunkRebuildQueue.OnBlockChanged, which marks the ±13-block halo (up to 27 chunks per cell, coalesced by HashSet) — so an active water front keeps dozens of chunks dirty continuously, and CreativeWorldManager.OnWorldTick → Renderer.RebuildDirty() rebuilds them all synchronously every tick batch. On top of the solid-mesh rebuild cost, UpdateFluidChunkMesh re-cooks the fluid child's MeshCollider synchronously on every rebuild of a fluid-bearing chunk (unlike solid colliders, which are budgeted at 4/frame). Pouring a bucket, breaching a lake, or an emberflow burn means sustained dropped frames for the duration of the spread — exactly the 'mesh rebuild storm when fluids spread' scenario, on every peer simultaneously (the sim is deterministic lockstep on all clients).
- **Files:** ["Assets/Blockiverse/Scripts/WorldGen/FluidFlowService.cs", "Assets/Blockiverse/Scripts/Voxel/FluidBlocks.cs", "Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs"]
- **Evidence:** FluidBlocks.cs:19 `TickCadenceByFamily = { 5, 6, 12 }` and :18 flow distances {8,6,4}; FluidFlowService.ProcessCell (342-416) calls world.SetBlock for falls/spreads/retractions; CreativeWorldManager.OnWorldTick (644-660): `fluidFlowService?.Tick(...)` then `Renderer?.RebuildDirty()` every tick batch; VoxelWorldRenderer.UpdateFluidChunkMesh lines 213-215: `collider.sharedMesh = null; collider.sharedMesh = mesh;` — synchronous PhysX cook per rebuild, bypassing the ColliderRebuildBudget (DefaultColliderRebuildBudget = 4, line 21); halo marking via ChunkRebuildQueue.OnBlockChanged (ChunkMeshBuilder.cs:245-281).
- **RecommendedFix:** Same budgeted RebuildDirty as the edit-storm fix, plus: (1) route fluid collider rebakes through the existing throttled pendingColliderRebuilds queue instead of cooking inline (rays against a frame-stale water surface are acceptable); (2) exempt fluid-only block changes from the full lighting halo — a water cell does not block light (IsLightBlocking requires IsSolid), so marking only the cell's own chunk + face neighbors would cut the dirty set by an order of magnitude; (3) optionally cap fluid sim mutations per step and carry the remainder to the next step.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["performance"]
- **MergedCount:** 1

## 16. Hand roles are hard-coded; no left-handed mode, and the game is unplayable one-handed

- **Severity:** High
- **Impact:** Left-handed players cannot swap movement to the right stick or break/place to the left hand. Players with use of only one hand are locked out entirely: a right-hand-only player cannot move in glide mode (left stick only), open the pause menu (left controller menu button), or open the blocks menu (left grip); a left-hand-only player cannot turn (right stick only), break/place (right trigger/grip), or jump (right A).
- **Files:** ["Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseComfortSettings.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseSettingsPersistence.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab", "Assets/Blockiverse/Scripts/VR/BlockiverseXrUiInputConfigurator.cs"]
- **Evidence:** BlockiverseInputRig.ConfigureXriProviderInputs (lines 577–618): `continuousMoveProvider.rightHandMoveInput = CreateUnusedVector2Reader("Right Hand Move")` and `snapTurnProvider.leftHandTurnInput = CreateUnusedVector2Reader("Left Hand Snap Turn")` — the off-hand inputs are explicitly Unused. RefreshCachedActions (lines 305–310) binds break/place to RightHandMap Select/Activate and quick menu to LeftHandMap Activate. Bootstrapper line 1060 binds Menu to `<XRController>{LeftHand}/menuButton`, line 1061 Jump to `{RightHand}/primaryButton`. docs/rulesets/voxel_survival_menus.md §6.20 declares 'Bindings are fixed to the Quest controller layout (no remapping)'. No handedness field exists in BlockiverseComfortSettings or BlockiverseSettingsPersistence.
- **RecommendedFix:** Add a `DominantHand` (or `SwapHands`) setting to BlockiverseComfortSettings, persist it in BlockiverseSettingsPersistence, expose a toggle in the comfort menu (bootstrapper EnsureXrRigComfortMenu), and honor it in BlockiverseInputRig.ConfigureXriProviderInputs / RefreshCachedActions by swapping which hand map feeds move/turn/break/place/jump, and in EnsureXrRigFeedback's FindControllerHaptics(rig, Right) so 'dominant hand' haptics follow the setting. Longer term, consider a one-handed preset (move+turn on one stick via mode toggle, teleport already works from either stick). Update the Controls reference text to reflect the active mapping.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["accessibility", "vr-interaction"]
- **MergedCount:** 2

## 17. Hardware Menu button is double-bound: persistent comfort-panel toggle fights the pause/back routing

- **Severity:** High
- **Impact:** Every press of the Menu button (the documented pause button) also toggles the Comfort Settings panel via a persistent UnityEvent listener. With the menu controller wired as intended, opening the pause menu simultaneously pops a comfort panel in front of it (1.3 m vs 1.1 m), and dismissing pause re-shows/hides comfort out of phase. In the current broken build the Menu button does nothing except toggle comfort. The comfort panel also has no Close button of its own, so the conflicting binding is its only dismissal path.
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseComfortMenu.cs"]
- **Evidence:** EnsureXrRigComfortMenu adds 'UnityEventTools.AddPersistentListener(inputRig.MenuPressed, presenter.ToggleVisible)' (BlockiverseProjectBootstrapper.cs:2207, after removing prior copies at 2198-2206). Confirmed serialized in BlockiverseXRRig.prefab:42390-42404 (menuPressed → BlockiverseWorldSpacePanelPresenter.ToggleVisible on fileID 3940017637420611929, which lives on the 'Comfort Settings Menu' GameObject, prefab line 51444-51462). Meanwhile BlockiverseMenuController.Start() subscribes OnMenuPressed to the same inputRig.MenuPressed event (BlockiverseMenuController.cs:198-199) to push/pop pause and screens, and the Settings hub separately opens the same comfort panel via comfortMenu.Show() (line 483-485). The comfort panel builder (bootstrapper 2068-2215) creates toggles/sliders and a Height Reset button but no Close button — BlockiverseComfortMenu.Hide() has no UI trigger.
- **RecommendedFix:** Remove the persistent MenuPressed→ToggleVisible listener from the comfort presenter in EnsureXrRigComfortMenu (the Settings → Comfort entry is the canonical entry point), add a Close button on the comfort panel wired to BlockiverseComfortMenu.Hide, and have the menu controller hide the comfort panel in ApplyRouterState when leaving the Settings screen.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["ui-menu-flow", "vr-interaction", "runtime-wiring", "accessibility"]
- **MergedCount:** 4

## 18. Hosting or joining a LAN session never transitions the menu state to gameplay — pause menu and station screens unreachable in multiplayer

- **Severity:** High
- **Impact:** The spec flow LanMenu → HUD (voxel_survival_menus §4, §5) is unimplemented: after Host/Join the router stays on [Title, LanMultiplayer]. Closing the LAN panel lands on the title menu floating over the live session. OnMenuPressed does nothing on the title screen, so the pause menu (and Save Game) is unreachable for the whole multiplayer session; HandleStationOpenRequested requires the active screen to be GameplayHudScreen (it is Title), so kiln/forge panels silently fail to open for both host and clients. Multiplayer block interaction only works at all because world input is ungated (separate finding).
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs"]
- **Evidence:** EnterGameplay() is called only from BlockiverseWorldSessionController.CreateNewWorld (line 283) and LoadSave (line 639) — grep shows no other callers; BlockiverseMultiplayerSessionMenu.StartLanHost/JoinLanSession (lines 78-111) start the Netcode session but never touch the router or menu controller. OnMenuPressed (BlockiverseMenuController.cs:127-139) only pushes the pause screen when active == GameplayHudScreen and explicitly excludes TitleScreen from the generic pop. HandleStationOpenRequested (lines 296-309) early-returns when ActiveScreen != GameplayHudScreen.
- **RecommendedFix:** When the network session reaches Hosting or ConnectedClient (and the client's late-join world snapshot has been applied), call menuController.EnterGameplay() — e.g. the menu controller subscribes to BlockiverseNetworkSession state changes, or BlockiverseMultiplayerSessionMenu raises an event the controller consumes. On disconnect, route back to the title root explicitly.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["ui-menu-flow"]
- **MergedCount:** 1

## 19. Inventory UI shows 6 of 44 slots with no item management; hotbar slots 7-10 unselectable

- **Severity:** High
- **Impact:** The inventory model has 10 hotbar + 34 backpack slots, but the generated HUD shows only slots 1-6. Items auto-stack into the first free slot, so anything landing in slot 7+ becomes invisible and unusable; the player cannot equip a tool that lands there, cannot see why harvests fail with InventoryFull, and has none of the menus-spec §6.8 actions (move, swap, split, drop, sort, quick-transfer, equipment). Clicking a slot only changes hotbar selection. This makes inventory pressure — a core survival tension — read as random unexplained failure.
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs", "Assets/Blockiverse/Scripts/Survival/Inventory.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab", "docs/rulesets/voxel_survival_menus.md"]
- **Evidence:** BlockiverseProjectBootstrapper.cs:2948 'TMP_Text[] slotLabels = new TMP_Text[6]'; prefab contains exactly 'Slot 1'..'Slot 6'. Inventory.cs:7-8 DefaultSlotCount 44, DefaultHotbarSlotCount 10. SurvivalInventoryPanel.WireSlotButtons (lines 169-192): a click only calls SetSelectedHotbarSlotIndex; no move/split/drop API exists in the panel. voxel_survival_menus.md §6.8 specifies 13 slot actions (pickup_or_place_stack, split, quick_transfer, sort, drop, lock, equip...), none implemented.
- **RecommendedFix:** Generate the full 10-slot hotbar plus a backpack grid in the bootstrapper HUD, and implement at minimum slot-to-slot move/swap and drop-to-destroy in SurvivalInventoryPanel; surface an 'Inventory full' toast on InventoryFull command failures.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 20. Motion vignette is a no-op at default settings and the 'Strength' slider is inverted

- **Severity:** High
- **Impact:** The comfort tunneling vignette — the game's only protection against smooth-locomotion vection on a glide-by-default rig — does nothing out of the box: default VignetteStrength=1.0 maps to apertureSize 1.0 (fully open, no visible vignette) even though the comfort menu shows 'Motion Vignette' toggled ON. Players prone to motion sickness get zero protection and have no reason to suspect the enabled toggle is inert. Worse, the slider labeled 'Strength' is inverted: dragging it to maximum removes the vignette, dragging to minimum gives the strongest effect — the opposite of every user expectation.
- **Files:** ["Assets/Blockiverse/Scripts/VR/BlockiverseComfortSettings.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab", "Assets/Blockiverse/Scripts/VR/BlockiverseVignetteSettingsDriver.cs"]
- **Evidence:** BlockiverseComfortSettings.cs line 24: '[SerializeField] float vignetteStrength = 1.0f' with comment 'Normalized 0–1: 1 = widest aperture (subtle), 0 = fully closed (strong)'; line 73: VignetteAperture => 0.6f + strength*0.4f → 1.0 at default. BlockiverseXRRig.prefab line ~8320: TunnelingVignetteController m_DefaultParameters m_ApertureSize: 1 (no vignette; XRI's own comfort default is 0.7). Bootstrapper EnsureVignetteSlider (line 3360-3377): label 'Strength', LeftToRight 0–1, value = settings.VignetteStrength, so max slider = no vignette. BlockiverseComfortMenu.ApplyOtherControlsWithFeedback (line 178-179) writes slider.value straight into VignetteStrength.
- **RecommendedFix:** Invert the semantic so higher strength = stronger vignette (aperture = 1.0 - strength*0.4, or relabel/remap the slider as 1-value), and ship a sensible default (e.g. strength giving aperture ~0.7-0.8 when vignetteEnabled). Add a PlayerPrefs migration in BlockiverseSettingsPersistence so existing saved values are remapped once. Rerun the bootstrapper so the prefab default aperture is no longer 1.0.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["vr-interaction", "accessibility"]
- **MergedCount:** 2

## 21. Multiplayer world saves never persist any player inventory: the host's inventory is saved as empty and never restored, and remote clients' inventories are not saved at all

- **Severity:** High
- **Impact:** Every time the multiplayer host session ends (shutdown save or autosave then app exit), all players lose every item they carried, even though the world, containers, stations, and even the host's vitals persist. The reconnect stash (stashedInventoriesByGuid) is in-memory only, so a player who disconnects and never reconnects before the host stops loses their items, and all clients start empty on the next hosting of the same world. This contradicts the persistence story players will expect from the save system.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs", "Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs"]
- **Evidence:** MultiplayerWorldPersistence.SaveWorldBeforeHostShutdown (line 213) and the autosave (line 266) call the WorldSaveService.Save overload without an inventory parameter; that overload (WorldSaveService.cs:214-217) substitutes 'new Inventory(itemRegistry)' — an empty inventory — into players/local_player.json. The load path RestoreSavedWorldBeforeHostStart (lines 94-184) restores world, containers, stations, and vitals (vitalsRuntime.RestorePlayerSaveState line 170) but never calls survivalSync.RestoreLocalInventory — the only callers of RestoreLocalInventory are in the single-player controller (BlockiverseWorldSessionController.cs:707). Remote inventories exist only in MultiplayerSurvivalSync.inventoriesByClientId / stashedInventoriesByGuid (lines 194-199), which BuildSaveExtras never serializes.
- **RecommendedFix:** Persist the host's inventory in the multiplayer save (pass survivalSync.BuildPersistedInventory() + SelectedHotbarSlotIndex into Save, and call RestoreLocalInventory on load, mirroring the single-player controller). Persist remote players' inventories keyed by their persistent player GUID (the PlayerHello identity already exists) in a players/<guid>.json section, and rehydrate stashedInventoriesByGuid from it on host start so returning players reclaim items across host restarts.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["lan-multiplayer", "game-logic"]
- **MergedCount:** 2

## 22. No stick deadzone on Move/Turn actions; XRI SnapTurnProvider fires on any non-zero vector

- **Severity:** High
- **Impact:** Thumbstick drift or sensor noise translates directly into unintended locomotion: slow continuous glide drift (constant low-grade vection, a classic motion-sickness trigger that also keeps the tunneling vignette flickering) and, worse, spurious snap turns — XRI's SnapTurnProvider has no magnitude threshold, so any reading down to Vector2 epsilon (~1e-5) on the right stick picks a cardinal and rotates the camera 45° every 0.5 s debounce window. Quest controllers with worn sticks (a common hardware complaint) would make the game unplayable. The committed action asset and the bootstrapper generator both omit the StickDeadzone processors that Unity's own XRI sample bindings include.
- **Files:** ["Assets/Blockiverse/Settings/BlockiverseInputActions.inputactions", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** Every binding in BlockiverseInputActions.inputactions has "processors": "" (e.g. Move binding id ...024 → <XRController>{LeftHand}/thumbstick, Turn binding id ...052); grep StickDeadzone returns 0 hits in the asset and the bootstrapper (AddControllerMap lines 1051-1052 add Move/Turn with no processors). Verified in packages: SnapTurnProvider.GetTurnAmount (XRI 3.x, SnapTurnProvider.cs lines 193-221) returns m_TurnAmount for ANY input != Vector2.zero via CardinalUtility.GetNearestCardinal — no deadzone; ContinuousMoveProvider reads the raw Vector2. Reading a whole-stick Vector2 binding does not apply the child-axis axisDeadzone processors (Vector2Control.ReadUnprocessedValueFromState reads raw state), and OculusTouchControllerProfile's thumbstick (StickControl via USE_STICK_CONTROL_THUMBSTICKS in ProjectSettings.asset lines 700-701) declares no stick-level deadzone processor.
- **RecommendedFix:** Add a 'stickDeadzone' processor (e.g. min=0.15) to the Move, Turn, and UI Scroll bindings in EnsureInputActionSchema/AddControllerMap so the bootstrapper writes it into the asset, and regenerate BlockiverseInputActions.inputactions. Alternatively give SnapTurnProvider input a magnitude threshold by processing in a custom reader.
- **Confidence:** Medium
- **NeedsManualVerification:** true
- **Sources:** ["vr-interaction"]
- **MergedCount:** 1

## 23. No world save on OnApplicationPause — Quest suspend/kill loses up to 5 minutes of progress

- **Severity:** High
- **Impact:** On Quest the normal way to leave a game is the system Home button; the app is suspended and may be killed by the OS at any time afterwards. Single-player world state is saved only by the 300 s autosave timer and explicit menu actions, so a player who exits via Home (or whose headset sleeps and the app is later killed) silently loses up to AutoSaveIntervalSeconds of building/inventory/vitals progress. Comfort settings DO save on pause, proving the pattern exists but was not applied to world saves.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseSettingsPersistence.cs", "Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs"]
- **Evidence:** Repo-wide grep for OnApplicationPause/OnApplicationQuit/OnApplicationFocus/wantsToQuit matches only BlockiverseSettingsPersistence.cs lines 38-44 (comfort/feedback settings). BlockiverseWorldSessionController saves only in Update() on the WorldSaveService.AutoSaveIntervalSeconds = 300f cadence (WorldSaveService.cs line 197; session controller Update lines 98-107) and on menu actions (HandleAction lines 153-161: PauseSaveGame/PauseReturnToTitle/DeathReturnToTitle/TitleQuit). BlockiverseMenuController.CanQuit() (lines 557-562) is false on device ('Quest apps exit via the system Home button'), acknowledging Home is the exit path — yet nothing saves there.
- **RecommendedFix:** Add OnApplicationPause(bool paused) to BlockiverseWorldSessionController (and an equivalent host-save trigger in MultiplayerWorldPersistence) that calls SaveCurrentWorld() when paused && HasActiveSession. The save path is already atomic (.tmp → move with .bak recovery), so a pause-time save is safe even if the process is killed mid-write.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["unity-csharp"]
- **MergedCount:** 1

## 24. P0: Death→respawn→save ordering invariant in BlockiverseMenuController is unprotected by tests

- **Severity:** High
- **Impact:** BlockiverseWorldSessionController.cs lines 154–158 document an order dependency: the menu controller must call vitalsRuntime.Respawn() BEFORE raising DeathReturnToTitle so the save written by that action sees post-respawn state. BlockiverseMenuController has zero tests, so reordering HandleAction (lines 406–414) silently writes a dead/death-position player state into the save. PlayerVitals.RestoreHealth clamps health to ≥1 on load, but the rig position and zeroed hunger/thirst would still persist wrongly. The death-screen routing (ShowDeathScreen, station-panel close on death, death screen not dismissible by the generic back action, lines 133–151, 262–267) is also untested.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs"]
- **Evidence:** grep 'BlockiverseMenuController' under Assets/Blockiverse/Tests/ returns nothing. BlockiverseMenuController.cs:406-414 (DeathRespawnBedroll/DeathRespawnWorldSpawn/DeathReturnToTitle → vitalsRuntime?.Respawn()); BlockiverseWorldSessionController.cs:154-158 comment stating the ordering contract; ActionMenuEditModeTests only covers the BlockiverseActionMenu widget (menu labels/clicks) and MenuActions.Death list contents, not the controller routing.
- **RecommendedFix:** Add MenuControllerEditModeTests (EditMode, construct the MonoBehaviour with stub presenters like the existing UI tests do): (1) DeathReturnToTitleRespawnsVitalsBeforeRaisingActionRequested — subscribe to ActionRequested, assert vitalsRuntime.Vitals.IsDead is already false (and rig moved to spawn) when the DeathReturnToTitle action fires; (2) LocalPlayerDiedShowsDeathScreenAndClosesStationPanel; (3) DeathScreenIsNotDismissedByGenericBackAction (router top stays DeathScreen); (4) DeathRespawnWorldSpawnPopsDeathScreenAndResumesGameplay.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 25. P0: FillBucket/PourBucket survival commands are completely untested (offline and networked)

- **Severity:** High
- **Impact:** The bucket↔fluid economy loop shipped in M8.5 (fill a bucket from a freshwater/brine/emberflow source, pour it back into the world, which also feeds the host-side FluidFlowService via the placed source) has zero coverage: no EditMode test, no offline host-path test, and no networked test. A regression could delete fluid sources without granting the bucket item, duplicate fluids, or desync host/client worlds, and run-tests.sh would pass.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Tests/PlayMode/Networking/MultiplayerSessionPlayModeTests.cs", "Assets/Blockiverse/Tests/EditMode/Survival/"]
- **Evidence:** MultiplayerSurvivalSync.cs: TrySubmitFillBucket (~740-760), TrySubmitPourBucket (~770-794), ProcessHostFillBucket (1612), ProcessHostPourBucket (1680), wire dispatch cases at 2133-2136. grep -rn 'FillBucket|PourBucket' under Assets/Blockiverse/Tests/ returns zero hits (the only 'bucket' matches are crafting-recipe assertions for bucket items in CraftingModelEditModeTests.cs:181-227).
- **RecommendedFix:** Add to MultiplayerSessionPlayModeTests: NetworkedBucketFillAndPourStayHostAuthoritative — seed client inventory on host with empty_bucket, place a Freshwater source in the host world, TrySubmitFillBucket → assert source removed on host AND client mirrors, empty_bucket→freshwater_bucket swap in the host-owned inventory; then TrySubmitPourBucket at an air cell → assert source placed on both worlds and bucket reverts to empty. Add offline [Test] cases in the same file (pattern of HostRejectsNonConsumableUseWithoutThrowing): HostRejectsFillBucketWithoutEmptyBucketHeld, HostRejectsFillBucketOnNonSourceCell, HostRejectsPourBucketIntoOccupiedCell — asserting FailureReason and that neither world nor inventory changed.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 26. P0: Reconnect-identity inventory stash/reclaim (PlayerHello GUID) has no test

- **Severity:** High
- **Impact:** MultiplayerSurvivalSync stashes a disconnecting client's inventory under its persistent player GUID and re-binds it when the same player rejoins with a new client id. This is the only thing standing between a mid-session disconnect and total inventory loss for that player — a survival-economy data-loss scenario — and neither the stash (HandleClientDisconnected) nor the reclaim (HandlePlayerHelloMessage) nor SendPlayerHello is exercised by any test. The PlayMode suite tests host restarts and menu UX on disconnect, but never disconnects and reconnects a survival-sync client.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Tests/PlayMode/Networking/MultiplayerSessionPlayModeTests.cs"]
- **Evidence:** MultiplayerSurvivalSync.cs:2641-2655 (HandleClientDisconnected stashes inventoriesByClientId into stashedInventoriesByGuid), 2693-2709 (HandlePlayerHelloMessage reclaims and re-sends the snapshot), 2658-2668 (ResolveLocalPlayerGuid via PlayerPrefs). grep for 'PlayerHello|playerGuid' under Assets/Blockiverse/Tests/ returns zero hits; ClientMenuShowsSessionEndedAndReconnectsAfterLanHostRestarts (MultiplayerSessionPlayModeTests.cs:270) only reconnects at the session/menu level with no survival sync attached.
- **RecommendedFix:** Add PlayMode test ReconnectingClientReclaimsStashedInventoryByPlayerGuid: host + client with survival syncs; seed the client inventory on the host and confirm the client mirror; clientSession.StopSession(); wait for host HandleClientDisconnected; clientSession.StartClient() again (same process → same PlayerPrefs GUID); assert hostSurvivalSync.GetInventory(newClientId) contains the pre-disconnect items and the client's LocalInventory mirror matches after the snapshot. Add a companion EditMode-style [Test] HostStashesDisconnectedClientInventoryOnlyWhenGuidKnown covering the no-hello-received branch (inventory dropped, no stash). Note the tests must isolate/restore the 'Blockiverse.PlayerGuid' PlayerPrefs key.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 27. P0: Single-player session lifecycle (BlockiverseWorldSessionController) has zero test coverage

- **Severity:** High
- **Impact:** The 850-line controller implementing every shipped single-player session verb — create world from New World config, save (incl. containers/stations/player extras assembly), load, continue-latest, rename/duplicate/delete save slots, save-on-quit/pause/death, and the live autosave Update loop — is completely untested. A regression in any verb (e.g. ContinueLatestSave picking the wrong slot by ModifiedAtUtc, BuildSavedContainers dropping emptied crates, FoldSeed changing) ships undetected because run-tests.sh is the only gate and nothing references this class.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Tests/PlayMode/", "Assets/Blockiverse/Tests/EditMode/"]
- **Evidence:** grep for 'BlockiverseWorldSessionController' across Assets/ matches only the prefab, the bootstrapper, CreativeWorldManager, MultiplayerWorldPersistence and the class itself — zero hits under Assets/Blockiverse/Tests/. Untested logic includes HandleAction switch (lines 124–163), SaveCurrentWorld (167–210), BuildSaveExtras (214–224), BuildSavedContainers (226–252), CreateNewWorld (256–293), GenerateWorld preset dispatch (301–324), SizeFor (328–337), FindSpawnForBiome (344–384), BiomeIndexFor (388–401), FoldSeed (404), ContinueLatestSave (408+).
- **RecommendedFix:** Add WorldSessionControllerPlayModeTests (PlayMode, can reuse the Boot-scene-free pattern of CreateCreativeWorldManager from MultiplayerSessionPlayModeTests): (1) CreateNewWorldWritesSaveSlotAndEntersGameplay — drive MenuActions.NewWorldCreate with a NewWorldConfig, assert a .vxlworld directory exists under the controller's saves root, manifest world name matches, and worldManager.World matches the preset/size mapping; (2) ContinueLatestSaveLoadsMostRecentlyModifiedSlot — write two saves with distinct ModifiedAtUtc, assert the newer loads; (3) SaveOnPauseAndDeathReturnToTitlePersistsContainersStationsAndPlayerState — assert the saved extras round-trip into a reloaded session; (4) RenameDuplicateDeleteKeepSaveListAndPathsConsistent. Extract FoldSeed/SizeFor/BiomeIndexFor/FindSpawnForBiome into a public engine-free helper (UI asmdef is testable from EditMode) and add plain NUnit cases: FoldSeedXorFoldsHighAndLowWords, SizeForMapsMediumLargeInfinite, FindSpawnForBiomeReturnsDryColumnOfRequestedBiomeOrNull.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 28. Player inventory and shared-crate snapshots exceed the same 1264-byte message cap once inventories fill, hanging client commands mid-session

- **Severity:** High
- **Impact:** A remote client whose 44-slot inventory has roughly 30+ occupied slots makes every subsequent host->client inventory snapshot throw on the host. Because SendInventorySnapshot is called inside ProcessHost* before SendCommandResult, the exception (caught by NGO's receive loop, logged, message processing aborted) means the client never receives the command result: the pending request hangs forever, the client's inventory mirror freezes stale, and retrying hits the duplicate window which also tries to send the oversized snapshot and fails again. Survival co-op degrades permanently for that client once their inventory fills.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs"]
- **Evidence:** Inventory.DefaultSlotCount = 44 (Inventory.cs:7). WriteInventorySnapshot (MultiplayerSurvivalSync.cs:3005-3012) writes 2 ints + per slot a string ItemId (FastBufferWriter string = 4-byte length + 2 bytes/char; canonical ids run 11-17 chars, e.g. 'clean_water_flask'), int count, int durability ≈ 36-46 bytes per occupied slot vs 12 per empty. ~30 occupied slots exceeds the ~1264-byte non-fragmented cap. SendInventorySnapshot (lines 2439-2470, FastBufferWriter sized 4096) uses SendNamedMessage default ReliableSequenced. Every ProcessHost* path calls SendInventorySnapshot(clientId) before SendCommandResult (e.g. ProcessHostHarvest lines 1238-1239); NGO catches handler exceptions and ignores the rest of the handler (NetworkMessageManager.cs:420-427), so the result message is never sent and TryRejectDuplicate (lines 2058-2081) re-sends the same oversized snapshot on retry.
- **RecommendedFix:** Send inventory and shared-crate snapshots with NetworkDelivery.ReliableFragmentedSequenced (payload fits comfortably under UnityTransport's 6144-byte fragmentation cap), or encode item ids compactly (oneByteChars / registry index). Also reorder so SendCommandResult is sent before the snapshot, and wrap snapshot sends in try/catch so a snapshot failure can never swallow the command result.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["lan-multiplayer"]
- **MergedCount:** 1

## 29. Release APK may ship without android.permission.INTERNET: ForceInternetPermission is 0 and the custom AndroidManifest declares no INTERNET permission

- **Severity:** High
- **Impact:** If Unity's 'Internet Access: Auto' heuristic does not inject the INTERNET permission for Unity Transport's native sockets, the store/release APK cannot open sockets at all: hosting and joining both fail on device while development builds (which always add INTERNET for the profiler) work — a classic works-in-dev, broken-in-release trap for the entire LAN co-op feature.
- **Files:** ["ProjectSettings/ProjectSettings.asset", "Assets/Plugins/Android/AndroidManifest.xml", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** ProjectSettings.asset line 187: 'ForceInternetPermission: 0' (Internet Access = Auto). Assets/Plugins/Android/AndroidManifest.xml contains no <uses-permission android:name="android.permission.INTERNET"/> (only VR features/metadata). No editor/build script sets PlayerSettings internet permission (grep for Internet/Permission across Editor scripts and scripts/unity/*.sh is empty). The OVRManifestPreprocessor call (bootstrapper line 375) manages Meta-specific entries, not INTERNET.
- **RecommendedFix:** Set Internet Access to Require (ForceInternetPermission: 1 via the bootstrapper so it is generated, per project policy) or add <uses-permission android:name="android.permission.INTERNET"/> to Assets/Plugins/Android/AndroidManifest.xml. Verify by inspecting the merged manifest of a release build (aapt dump permissions) and testing LAN join on-device with a non-development signed build.
- **Confidence:** Medium
- **NeedsManualVerification:** true
- **Sources:** ["lan-multiplayer"]
- **MergedCount:** 1

## 30. Remote players' Meta avatars and fallback bodies render at world origin: nothing ever moves the player NetworkObject root, and the remote avatar entity ignores the synced pose

- **Severity:** High
- **Impact:** On Quest, once a remote player's Meta-avatar stream arrives, the fallback head/hand cubes are hidden (SetMetaAvatarAvailable(true)) and the Meta avatar becomes the only representation — but it is parented to the player NetworkObject, which is spawned at (0,0,0) and never moved, with locomotion offsets absent from the recorded stream (the local entity tracks the camera, and rig-root locomotion is outside entity space). Remote players therefore appear at/near the world corner — under terrain (surface is ~y96+), i.e. effectively invisible. Even without Meta avatars, the fallback 'body' capsule sits at world origin while only the head/hand cubes track correctly (they work only because head/hand local offsets are computed relative to an origin-pinned root, making them world coordinates).
- **Files:** ["Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkAvatarRig.cs", "Assets/Blockiverse/Scripts/MetaAvatars/MetaHorizonAvatarProvider.cs", "Assets/Blockiverse/Scripts/MetaAvatars/BlockiverseMetaAvatarEntity.cs", "Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamRelay.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** No code positions or parents the spawned player object: grep for PlayerObject/ConfigureTrackingSources finds only EnvironmentDynamicsController reads and the rig's own methods. BlockiverseNetworkAvatarRig owner path (LateUpdate -> ApplyTrackingSources -> PublishPose, lines 78-160) only writes head/hand anchors local to 'transform' and serializes transform.position — which stays (0,0,0); remote ApplyPose (lines 185-192) sets the root to that origin. The fallback body is created at local (0,0.85,0) under fallbackRoot (EnsureFallbackProxy lines 217-223). The player prefab presenter is configured RemoteThirdPerson with null tracking sources (BlockiverseProjectBootstrapper.cs:1188-1194); MetaHorizonAvatarProvider parents the entity to the player object (line 142) and BlockiverseMetaAvatarEntity.SetTrackingSourcesFromTransforms (lines 118-122) no-ops with a null head, so the remote entity never adopts the synced HeadAnchor pose. MetaAvatarStreamRelay.HideOwnerNetworkFallbackWhenLocalAvatarIsReady / RefreshAvatarState hide the fallback proxy once a stream applies (relay lines 83-90, rig RefreshAvatarMode lines 138-145). The PlayMode pose test manually moves the owner root (test line 225-226), masking the gap.
- **RecommendedFix:** Drive the player NetworkObject root from the XR rig on the owner (set transform to the rig root each LateUpdate before PublishPose), so RootPosition replicates locomotion; and position the remote Meta avatar entity from the synced pose (e.g. SetTrackingSources(avatarRig.HeadAnchor, leftHandAnchor, rightHandAnchor) for RemoteThirdPerson, or parent the entity under the synced root once the root is driven). Verify on two headsets that remote bodies/avatars track.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["lan-multiplayer"]
- **MergedCount:** 1

## 31. Router pause/world-input contract is never consumed: 'paused' screens do not pause and world input is not gated

- **Severity:** High
- **Impact:** Opening the pause menu (or any pauseGame:true screen — title, settings, load world) does not stop the simulation: world time, weather, crops, station jobs, hunger/thirst drain, and hazard damage all continue, so a player can starve or burn to death while reading the settings menu, contradicting voxel_survival_menus §1/§10.1 ('Pauses Game? Yes'). Likewise AllowWorldInput is ignored: the only world-input gate is 'ray over UI', so pressing the trigger while aiming beside any open menu (including at the title screen, where a default world already exists) breaks blocks in the world behind the menu.
- **Files:** ["Assets/Blockiverse/Scripts/UI/UiScreenRouter.cs", "Assets/Blockiverse/Scripts/Gameplay/WorldTimeClock.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "docs/rulesets/voxel_survival_menus.md"]
- **Evidence:** UiScreenRouter.IsGamePaused (line 61) and AllowWorldInput (line 64) are referenced nowhere outside UiScreenRouter.cs — a repo-wide grep for 'IsGamePaused|AllowWorldInput' returns only the router itself. WorldTimeClock ticks unconditionally from its own accumulator and nothing sets Time.timeScale (grep 'timeScale' hits only the clock's own day-speed field and the creative tools slider). SurvivalVitalsRuntime.OnWorldTick/TickHazards (lines 135-195) deplete vitals whenever survival mode is active, with no menu/pause check. BlockiverseCreativeInputBridge.CanInteract (lines 354-359) gates block edits only on interactionRay.IsOverUIGameObject(); CreativeInteractionController contains no menu/router/EventSystem references at all (grep verified). ScreenRoute.PauseGame values set throughout BlockiverseMenuController (e.g. lines 130, 350, 364) therefore have no effect.
- **RecommendedFix:** Have BlockiverseMenuController publish router state on Changed (e.g. a static or injected IMenuStateProvider): WorldTimeClock skips accumulation while IsGamePaused (single-player only; keep host clock running in LAN sessions per host-authority rules), SurvivalVitalsRuntime suspends ticks/hazards while paused, and BlockiverseCreativeInputBridge additionally requires router.AllowWorldInput before TryGetTarget succeeds. Add EditMode tests asserting vitals do not tick while a pauseGame route is active.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["ui-menu-flow", "vr-interaction", "game-design"]
- **MergedCount:** 3

## 32. Sapling-grown trees (and creative-spawned trees/ruins) are written with trackChange:false — lost from saves and late-join snapshots

- **Severity:** High
- **Impact:** When a runtime sapling matures, VegetationService places the trunk/canopy via TrySetBlock(..., trackChange:false). Saves persist only world.GetChangedBlocks() (WorldSaveService.WriteRegionFiles), and late-join sync sends only the same changed-block set (SendLateJoinSnapshot), so every player-grown tree vanishes on save/load (the tracked Sapling→Air change is saved, so not even the sapling remains) and is invisible to late-joining clients (permanent host/client world divergence, ExpectedBlockMismatch on edits). The creative-tools SpawnTree/SpawnRuin buttons hit the same untracked paths, so spawned trees/ruins also disappear on reload.
- **Files:** ["Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs", "Assets/Blockiverse/Scripts/WorldGen/StructureService.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseCreativeToolsPanel.cs", "Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs"]
- **Evidence:** VegetationService.cs:484-488 `TrySetBlock(... world.SetBlock(pos, block, trackChange: false))` is used by PlaceTrunk/PlaceCanopyRound/PlaceCanopySquare, which AdvanceSapling (lines 193-209) calls at runtime via PlaceBiomeTree. WorldSaveService.cs:452 `foreach (BlockChange change in world.GetChangedBlocks())` is the only source of saved block deltas; MultiplayerChunkAuthoritySync.cs:678 SendLateJoinSnapshot also iterates GetChangedBlocks(). StructureService.PlaceStructureAt (line 162) → PlaceRuin uses trackChange:false throughout; BlockiverseCreativeToolsPanel.SpawnTree (line 243) and SpawnRuin (line 253) call these at runtime.
- **RecommendedFix:** Split the placement paths: keep trackChange:false for generation-time placement, but make runtime growth and creative spawners use tracked SetBlock (e.g. add a `trackChanges` flag to VegetationService/PlaceBiomeTree and to StructureService.PlaceStructureAt, defaulting generation to false and runtime to true). Add an EditMode test asserting a matured sapling's trunk blocks appear in world.GetChangedBlocks().
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic", "lan-multiplayer"]
- **MergedCount:** 2

## 33. Shared crate contents are not persisted by any save path

- **Severity:** High
- **Impact:** The shared crate (SharedCrateInventory, the multiplayer-economy shared storage) exists in memory only. Neither WorldSaveData/WorldSaveExtras nor either save path (single-player BlockiverseWorldSessionController or MultiplayerWorldPersistence) serializes it, and nothing restores it on load. Anything players deposit in the shared crate is silently destroyed on save/load, host shutdown, or app exit — item loss in a shipped core flow.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs"]
- **Evidence:** WorldSaveData (WorldSaveService.cs:85-109) and WorldSaveExtras (lines 73-82) contain no shared-crate field. Repo-wide grep for 'SharedCrate' outside MultiplayerSurvivalSync.cs hits only UI panels (SurvivalHudController, SurvivalCratePanel) — no persistence references. SharedCrateInventory is created fresh in Configure/CreateSharedCrateInventory (MultiplayerSurvivalSync.cs:416, 2927-2931) and cleared on client start (line 2562).
- **RecommendedFix:** Add the shared crate slots to WorldSaveExtras (canonical id + count + durability), export from MultiplayerSurvivalSync in both BuildSaveExtras call sites, and restore it on load/host-start alongside RestoreStationStates.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 34. Shared-crate (and station) transfers strip tool durability, yielding never-breaking tools

- **Severity:** High
- **Impact:** Depositing a tool into the shared crate and withdrawing it returns the tool with Durability 0, because ProcessHostCrateTransfer rebuilds the stack as `new ItemStack(itemId, count)`. Durability 0 means 'not tracking wear' (per MendBenchRepair), and ApplyToolDurability skips slots with Durability <= 0, so the withdrawn tool never loses durability again while keeping its full ToolTier/ToolClass power — an infinite-durability exploit reachable through the normal crate UI, and an accidental wear-state loss for players legitimately sharing tools. Station input deposits have the same stripping (tools deposited there are also unrecoverable, see separate finding).
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Scripts/Survival/MendBenchRepair.cs", "Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs"]
- **Evidence:** MultiplayerSurvivalSync.cs:1858 `var stack = new ItemStack(itemId, count);` then lines 1879-1899 Remove/TryAddAll move that durability-less stack both directions; station deposit does the same at line 1971. ApplyToolDurability (lines 1749-1762) returns early when `slot.Durability <= 0`. Tools are crafted with full durability via ItemRegistry.CreateItemStack (ItemRegistry.cs:268-273), and MendBenchRepair.cs:90-93 documents 'durability 0 means the slot is not tracking wear'. PlayMode tests only exercise resource (timber) crate transfers, not tools.
- **RecommendedFix:** Carry durability through crate/station transfers: either transfer the actual ItemStack (slot-based transfer preserving Durability, as ContainerInventoryStore.TransferAllInto already does) or reject ItemKind.Tool / MaxDurability>0 items in IsValidTransferItem for count-based transfers. Add tests for tool round-trips through the crate.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 35. Station (smelting) panel serializes only survivalSync — labels, progress bar, close and transfer buttons all unwired at runtime

- **Severity:** High
- **Impact:** When a player opens a smelting station (HandleStationOpenRequested → stationPanel.Open), the panel has no titleLabel, slot labels, fuel/output/status labels, progress slider, close button, or deposit/collect buttons: nothing displays or updates, the deposit-input/deposit-fuel/collect-output requests can never be sent, and CloseRequested can never fire from the panel's own close button (only the menu-button escape hatch in OnMenuPressed could close it — which is itself dead per the Critical finding). The entire smelting-station interaction loop is unusable.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab"]
- **Evidence:** BlockiverseStationPanel.cs lines 18-28: `TMP_Text titleLabel; TMP_Text[] inputSlotLabels; TMP_Text fuelLabel; TMP_Text outputLabel; TMP_Text statusLabel; Slider progressSlider; Button closeButton; Button depositInputButton; Button depositFuelButton; Button collectOutputButton;` — only `[SerializeField] MultiplayerSurvivalSync survivalSync` (line 28) is serialized. They are assigned only via Configure(...) (lines 42-59) and ConfigureTransferControls(...) (lines 61-70), called from the bootstrapper at editor time (BlockiverseProjectBootstrapper.cs:4453-4459 area). Committed prefab ground truth: the BlockiverseStationPanel block in BlockiverseXRRig.prefab serializes exactly `survivalSync: {fileID: 0}`. Awake() (line 92-95) calls WireTransferButtons() against null buttons, a no-op.
- **RecommendedFix:** Add [SerializeField] to all ten control fields in BlockiverseStationPanel, keep Configure/ConfigureTransferControls for tests, ensure Awake's WireTransferButtons plus a close-button wiring run idempotently against serialized references, and rerun the bootstrapper to persist the references into BlockiverseXRRig.prefab.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["runtime-wiring"]
- **MergedCount:** 1

## 36. World create/load/host-start/late-join freeze the main thread for seconds (full 4.2M-block generation + 1024-chunk re-mesh + collider cook in one frame)

- **Severity:** High
- **Impact:** Every entry into a world — app boot (CreativeWorldManager.Awake), New World, Load World, multiplayer host start, and client late-join finalize — runs as a single synchronous main-thread burst. In the headset this is a multi-second frozen frame (head-locked image / compositor stall), the single worst comfort failure on Quest, and risks the OS app-not-responding watchdog. The burst comprises: SurvivalTerrainPreset.Generate over 128×256×128 = 4,194,304 blocks (terrain+fluids+caves+veins+structures+vegetation); VoxelWorldRenderer.RebuildAll over 8×16×8 = 1024 chunks each doing a 4096-block walk with per-face light probes (up to 5 dirs × 12 steps) plus Mesh.RecalculateNormals, then ProcessPendingColliderRebuilds(int.MaxValue) which PhysX-cooks every non-empty chunk MeshCollider in the same frame; VoxelSkyLightMap constructor full column scan; TorchbudLightManager.RebuildAllLights full 4.2M-block scan; FluidFlowService.Configure full-world scan + BFS; FarmingService.ScanAndTrackCrops full per-position scan. Only the late-join path moves generation off-thread (Task.Run) — its FinalizeSnapshot still does all meshing/baking in one frame.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs", "Assets/Blockiverse/Scripts/Gameplay/TorchbudLightManager.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs"]
- **Evidence:** BlockiverseWorldSessionController.cs:256-322 (CreateNewWorld → SurvivalTerrainPreset.Generate() on main thread, then InitializeGeneratedWorld) and 590-680 (LoadSave → RegenerateBaseWorld → preset.Generate()); CreativeWorldManager.cs:728-732 (Awake → InitializeDefaultWorld) and 393-420 (ConfigureWorldRuntime → Renderer.Configure); VoxelWorldRenderer.cs:72 (Configure → RebuildAll), 75-107 (RebuildAll loops all chunks), 100 (ProcessPendingColliderRebuilds(int.MaxValue) — comment says 'flush every queued collider rebuild rather than throttling'); VoxelSkyLightMap.cs:21-33 (constructor Rebuild scans all columns); TorchbudLightManager.cs:60-76 (RebuildAllLights triple loop over Bounds); FluidFlowService.cs:88-173 (Configure full-world triple loop + BFS); MultiplayerChunkAuthoritySync.cs:478-513 (FinalizeSnapshot → InitializeGeneratedWorld + RebuildDirty on main thread after off-thread Generate). WorldGenerationSettings.cs:19-28 (width 128, height 256, depth 128).
- **RecommendedFix:** Amortize world entry across frames: (1) run generation off the main thread everywhere (the Task.Run pattern in MultiplayerChunkAuthoritySync.StartSnapshotGeneration already proves the sim core is thread-safe for this); (2) time-slice RebuildAll — mesh N chunks per frame behind the existing startup overlay / a loading screen, prioritizing chunks near spawn; (3) keep the collider flush budgeted (cook spawn-area colliders first, drain the rest via the existing per-frame pump); (4) replace the FarmingService/Torchbud full per-position scans with VoxelWorld.CollectBlockPositions flat-array sweeps (VegetationService already does this). Display a real loading state during the slice so the compositor keeps rendering.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["performance", "unity-csharp", "ui-menu-flow"]
- **MergedCount:** 3

## 37. World Details action menu has no action ids at runtime — Play/Rename/Duplicate/Delete/Back can never fire

- **Severity:** High
- **Impact:** Even after the serialization defect is fixed, every button on the World Details screen is a no-op: the §6.5 save-management flow (play, rename, duplicate, delete) is dead because the menu's action-id list only exists in editor memory.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseActionMenu.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** BlockiverseActionMenu.actionIds is a non-serialized 'readonly List<string>' (BlockiverseActionMenu.cs:42) populated only by SetMenu. The bootstrapper calls actionMenu.SetMenu("World Details", MenuActions.WorldDetails) once at editor time (BlockiverseProjectBootstrapper.cs:4714) — grep shows this is the only call site. BlockiverseMenuController.Start() (lines 205-209) re-populates the title, pause, death, confirm, and settings menus at runtime but never calls worldDetailsMenu.SetMenu(...). At runtime InvokeActionAt (BlockiverseActionMenu.cs:122-133) returns early because index >= actionIds.Count (the list is empty), so no WorldDetails* action is ever emitted; the labels look correct because TMP text is serialized.
- **RecommendedFix:** In BlockiverseMenuController.Start(), add worldDetailsMenu?.SetMenu("World Details", MenuActions.WorldDetails) alongside the other runtime SetMenu calls; consider making BlockiverseActionMenu serialize its actionIds (List<string> with [SerializeField]) so editor-baked menus also survive.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["ui-menu-flow"]
- **MergedCount:** 1

## 38. "Lockstep" environmental simulation (crops, fluids, vegetation, day/night) has no resync mechanism and diverges under clock drift, app pause, and delta latency

- **Severity:** Medium
- **Impact:** Host and client worlds gradually disagree on simulated state that is never broadcast: WorldTimeClock advances from each peer's local Time.deltaTime, so a Quest headset doff (app pause; Unity's clamped deltaTime cannot catch up) permanently offsets the client's tick count from the host's — shifting day/night phase, crop growth intervals, and fluid-flow steps with no repair path (the environment snapshot is sent only once at connect, MultiplayerChunkAuthoritySync.cs:229-230). Independently, block deltas arrive at latency-shifted local ticks, so tick-anchored growth/flow decisions that depend on world content (water proximity, trunk clearance, flow obstacles) can resolve differently per peer; the resulting environmental mutations are deliberately not broadcast, so divergence persists until the client rejoins. Players see different fluids/plants; client-side harvest attempts on divergent blocks fail with host corrections.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/WorldTimeClock.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Assets/Blockiverse/Scripts/WorldGen/FluidFlowService.cs", "Assets/Blockiverse/Scripts/Survival/FarmingService.cs", "Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs"]
- **Evidence:** WorldTimeClock.Update (lines 92-107) accumulates ticks from Time.deltaTime per peer; RestoreElapsedTicks is invoked only from the one-shot join snapshot and save load. CreativeWorldManager.ConfigureEnvironmentServices comments the design: 'environmental mutations are never broadcast, so host and clients simulate in lockstep' (lines 473-475) and OnWorldTick (lines 644-660) runs vegetation/farming/fluid ticks locally on every peer. SendEnvironmentSnapshot is only called from HandleClientConnected. FarmingService.TickGrowth anchors per-crop intervals at first local tick of the crop (TrackCrop lines 176-181), which is latency-dependent on clients receiving the plant delta.
- **RecommendedFix:** Add a periodic host->client environment heartbeat (world tick count + weather sync state, tiny payload) that clients snap/slew to, and treat host-side environmental mutations as authoritative broadcasts (they already flow through tracked SetBlock; routing them through the existing delta channel as host-originated deltas would eliminate the divergence class at modest bandwidth) — or at minimum re-send the late-join snapshot/environment snapshot on a 'resync' request when a client detects large tick skew after pause/resume.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["lan-multiplayer", "game-logic"]
- **MergedCount:** 2

## 39. 'Engine-free simulation core' invariant is unenforced (noEngineReferences:false) and already has a UnityEngine-module dependency in WorldGen

- **Severity:** Medium
- **Impact:** The project's core architectural invariant — Voxel/Survival/Survival.Health/WorldGen are engine-free so generation can run on Task.Run and tests are plain NUnit — is enforced only by convention: all four asmdefs set "noEngineReferences": false, so nothing stops UnityEngine APIs from creeping in. WorldGen already imports Unity.Profiling (a UnityEngine.CoreModule type) in SurvivalTerrainPreset. ProfilerMarker happens to be thread-safe, but the open door means the next convenience import (Debug.Log, Mathf, Random) silently breaks thread-safety or NUnit isolation with no gate to catch it.
- **Files:** ["Assets/Blockiverse/Scripts/WorldGen/SurvivalLiteWorldPreset.cs", "Assets/Blockiverse/Scripts/WorldGen/Blockiverse.WorldGen.asmdef", "Assets/Blockiverse/Scripts/Voxel/Blockiverse.Voxel.asmdef", "Assets/Blockiverse/Scripts/Survival/Blockiverse.Survival.asmdef", "Assets/Blockiverse/Scripts/SurvivalHealth/Blockiverse.Survival.Health.asmdef"]
- **Evidence:** SurvivalLiteWorldPreset.cs:4 `using Unity.Profiling;` and :12/:41 ProfilerMarker usage inside Generate(). All four 'engine-free' asmdefs contain "noEngineReferences": false (verified by reading each file). Grep confirms this is currently the only engine using across the four assemblies.
- **RecommendedFix:** Either set noEngineReferences:true on the four asmdefs (and move the ProfilerMarker into the Gameplay-side callers, which already have markers in VoxelWorldRenderer), or if the profiling hook is wanted in-place, document the single sanctioned exception and add a CI grep (scripts/ci) rejecting `using UnityEngine` in those folders.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern", "unity-csharp"]
- **MergedCount:** 2

## 40. 'Reset Height' is a no-op under Floor tracking origin, and there is no seated-play height calibration

- **Severity:** Medium
- **Impact:** Seated players (including wheelchair users) cannot raise their in-world eye height: the rig uses Floor tracking origin, where XROrigin ignores CameraYOffset, so the comfort menu's only height control does nothing on Quest. The StandingEyeHeight setting (1.0–2.2 m) is persisted but has no UI slider and no effect in Floor mode. The body-locked Survival HUD is also generated at a fixed local height of 1.38 m, which sits at or above a seated player's eye level.
- **Files:** ["Assets/Blockiverse/Scripts/VR/BlockiverseHeightReset.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseComfortSettings.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab"]
- **Evidence:** BlockiverseHeightReset.ResetHeight (lines 19–27) only sets `origin.CameraYOffset`. BlockiverseProjectBootstrapper.cs lines 1745 and 1815 set `origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor`; Unity's XROrigin moves the camera offset to 0 in Floor mode, so CameraYOffset writes are ignored. The comfort menu (EnsureXrRigComfortMenu lines 2127–2188) has no eye-height slider even though voxel_survival_menus.md §6.19 lists 'standing eye height' as a Comfort setting. EnsureXrRigSurvivalHud line 2781 fixes the HUD at localPosition (0, 1.38, 1.15) on the camera offset.
- **RecommendedFix:** Implement a real seated-mode offset: add a 'Seated mode / height offset' setting in BlockiverseComfortSettings, apply it as an additional Y translation on the XROrigin CameraFloorOffsetObject (works regardless of tracking origin mode), wire 'Reset Height' to capture current head height and compute the offset against StandingEyeHeight, expose the eye-height slider in EnsureXrRigComfortMenu, and position the Survival HUD relative to the current head height (or give it a BlockiverseWorldSpacePanelPresenter that recenters like the menus do).
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["accessibility", "vr-interaction"]
- **MergedCount:** 2

## 41. All player-facing UI text is hardcoded English: ~50 runtime strings across UI scripts plus 204 m_text fields baked into the generated XR rig prefab

- **Severity:** Medium
- **Impact:** Localizing later requires touching every UI panel script, MenuActions, the session controller, and the 5K-line bootstrapper; there is no single extraction point. Until then the entire menu system, HUD, errors, and the controls-reference popup are English regardless of device language. This is the dominant cost driver for any future translation.
- **Files:** ["Assets/Blockiverse/Scripts/UI/MenuActions.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseCreativeToolsPanel.cs", "Assets/Blockiverse/Scripts/UI/SurvivalCratePanel.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs", "Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs", "Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeHotbar.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab"]
- **Evidence:** MenuActions.cs:82–146 hardcodes every menu button label ("Continue", "New World", "LAN Multiplayer", "Respawn at Bedroll", "Switch Survival/Creative", …). BlockiverseMenuController.cs:150/206–209 sets screen titles "You Died", "Paused", "Confirm?", "Settings", "OK"; line 469 builds the delete prompt $"Delete \"{worldName}\"? This cannot be undone.". BlockiverseWorldSessionController.cs:291,445,479–555,600,648 emits user-facing errors ("Failed to create the world.", "Save not found.", "Enter a world name first.", …). BlockiverseMultiplayerSessionMenu.cs:82–276 contains ~20 connection status sentences. BlockiverseCreativeToolsPanel.cs:123–394 contains ~25 status strings. SurvivalHealthPanel.cs:135–137 returns "Down"/"Critical"/"Stable". Bootstrapper makes 115 EnsureLabel/EnsureButtonControl calls with English literals (e.g. lines 2652 "Survive, craft, and shape the world.", 2722 "Controller Map", 4164 "New World", 4185 row labels, 4382–4392 ControllerMappingText), producing 204 m_text fields in BlockiverseXRRig.prefab (verified by grep; values include "Load World", "World Details", "Reduced Flash", "Meadow Turf").
- **RecommendedFix:** Introduce a string-id-based lookup before the UI surface grows further: give every literal a key (e.g. menu.title.continue, error.save_not_found), move English text into a default table, and have MenuActions, the panels, and EnsureLabel resolve keys through it. Because scenes/prefabs are bootstrapper-generated, baked text can instead become a runtime localizer component (set text from key on Awake), keeping the bootstrapper writing keys, not copy.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["localization"]
- **MergedCount:** 1

## 42. App boot always generates and meshes a throwaway default world before the player picks a session

- **Severity:** Medium
- **Impact:** CreativeWorldManager.Awake unconditionally runs InitializeDefaultWorld — full 128×256×128 survival generation, sky-light build, all-chunk meshing, full collider cook, fluid sim configure, and the farming/vegetation scans — during scene load at app start, before the title menu is usable. If the player then chooses New World, Load, or Join, all of that work is discarded and repeated for the real world (Configure tears down every chunk object/mesh and rebuilds). This roughly doubles effective time-to-interactive for the most common flows and lengthens cold app start (a Quest store review metric), beyond serving as a backdrop world for the title menu.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs"]
- **Evidence:** CreativeWorldManager.cs:728-732 (`void Awake() { if (World == null) InitializeDefaultWorld(); }`) → CreateDefaultGeneratedWorld (719-726, SurvivalTerrainPreset.Generate) → ConfigureWorldRuntime → Renderer.Configure → RebuildAll; VoxelWorldRenderer.Configure (53-73) destroys and rebuilds all generated chunk content on the subsequent real-world initialization.
- **RecommendedFix:** Defer world creation until a session verb runs (title menu over a skybox/static backdrop), or generate a much smaller decorative backdrop world for the title screen (e.g. 64×128×64 around spawn), or reuse the boot world when the player continues into the same seed/preset. Combined with off-thread generation (finding #1) this directly cuts cold-start time roughly in half for load/join flows.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["performance", "anti-pattern"]
- **MergedCount:** 2

## 43. Autosave serializes the whole save to flash synchronously on the main thread every 5 minutes (both single-player and host)

- **Severity:** Medium
- **Impact:** WorldSaveService.Save runs inline in Update on the autosave cadence (AutoSaveIntervalSeconds = 300) and on pause/quit/menu actions: it builds BlockRegistry.CreateDefault() and ItemRegistry.CreateDefault() fresh (a new WorldSaveService per call), computes registry hashes, walks the full changed-block dictionary building a nested region/chunk/section map of new collections, JSON-serializes manifest/dimension/environment/containers/simulation/stations/player, and performs atomic .tmp writes plus a recursive directory delete/swap — all on the render thread against Quest flash storage. With a mature world (mining + structures + fluid floods inflating the delta set), this is a perceptible hitch (likely tens to hundreds of ms) every 5 minutes during play, a recurring comfort/frame-pacing defect.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs"]
- **Evidence:** MultiplayerWorldPersistence.cs:248-275 (Update → `new WorldSaveService().Save(...)` inline, cadence WorldSaveService.AutoSaveIntervalSeconds); BlockiverseWorldSessionController.cs:98-107 (Update → SaveCurrentWorld on the same cadence) and 167+ (SaveCurrentWorld synchronous); WorldSaveService.cs:197 `AutoSaveIntervalSeconds = 300f`, 214-335 (Save: CreateDefault registries, hash computation, WriteJsonAtomic ×6+, WriteRegionFiles with nested Dictionary/List allocation per save at 448-510, regions dir delete/recreate/swap at 483-492).
- **RecommendedFix:** Move serialization and IO off the main thread: snapshot the mutable state cheaply on the main thread (the changed-block dictionary values can be copied to an array, plus small extras), then run region/JSON encoding and file writes on a worker (Task.Run) with a single in-flight save guard; keep only the final directory swap fast. Also hoist the per-save `new WorldSaveService()` / registry construction and hash computation into cached statics — the registries and their hashes never change at runtime.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["performance", "unity-csharp"]
- **MergedCount:** 2

## 44. Block atlas imported with mipmaps disabled and point filtering — minification shimmer and texture-cache thrash in VR

- **Severity:** Medium
- **Impact:** The single terrain texture (blockiverse_block_atlas.png, 128×160, 16px tiles) is imported with enableMipMap: 0 and filterMode: 0 (Point), with an Android override forcing uncompressed. Every distant voxel face samples the base level far below 1:1, producing severe specular-free shimmer/crawling in stereo VR (a comfort problem the Quest store review notices) and incoherent texture fetch patterns that waste GPU cache/bandwidth on the tiled GPU. Memory is irrelevant at this size — the issue is sampling quality and bandwidth, magnified because this texture covers most of every frame. The 142 item icons (64×64, mips off, uncompressed) matter less since they're UI-scale.
- **Files:** ["Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png.meta", "Assets/Blockiverse/Art/Textures/Items", "scripts/art/generate-art-assets.py", "Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs"]
- **Evidence:** blockiverse_block_atlas.png.meta: `enableMipMap: 0`, `filterMode: 0`, `aniso: 1`, Android platform override `textureCompression: 0, overridden: 1`; atlas geometry constants BlockVisualAtlas.cs:11-13 (8×10 tiles of 16px, UvInset 0.001). Item icons sampled (berry_cluster.png.meta): enableMipMap: 0, maxTextureSize: 64, textureCompression: 0 (138/142 metas carry the override).
- **RecommendedFix:** Enable mipmaps on the atlas and use trilinear-between-mips with point-within-mip if the pixel-art look must be kept at close range (or pad/duplicate tile borders and increase UvInset to prevent mip bleed across the 16px tiles — the standard Minecraft-style approach; alternatively bake a small mip chain per tile offline in generate-art-assets.py). Re-evaluate the uncompressed Android override: ASTC 4x4/6x6 on a padded atlas is fine for this style and halves bandwidth.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["performance", "asset-integration", "asset-integration-run1"]
- **MergedCount:** 3

## 45. Block interaction reach is the XRI default 30 m with no host-side reach validation

- **Severity:** Medium
- **Impact:** The interaction ray's maxRaycastDistance is never configured, so it keeps XRRayInteractor's default of 30 m (serialized as 30 in the prefab for all three rays). Players can break and place blocks, open stations, and start hold-to-mine on targets 30 m away — roughly 6x a Minecraft-like reach — which trivializes survival mining/building balance, and at that distance one block subtends well under half a degree, making straight-ray targeting jittery and frustrating. MultiplayerSurvivalSync performs no player-to-target distance validation on harvest/use commands, so the host accepts any in-bounds target a (possibly modified) client submits.
- **Files:** ["Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs"]
- **Evidence:** Grep maxRaycastDistance: zero hits in Assets/Blockiverse/Scripts; prefab lines 29846, 48869, 49783 all read m_MaxRaycastDistance: 30 (XRI default per XRRayInteractor.cs line 204). EnsureControllerInteractors in the bootstrapper sets lineType/raycastMask/inputs but never the distance. MultiplayerSurvivalSync grep for reach/distance shows only tilling-water and station-proximity checks — no harvest/place reach check against the requesting player's position.
- **RecommendedFix:** Set interactionRay.maxRaycastDistance to a gameplay-tuned reach (~5-6 m) in BlockiverseProjectBootstrapper.EnsureControllerInteractors and in BlockiverseInputRig.EnsureRayInteractorInputs (runtime repair path), then rerun the bootstrapper. Add a host-side max-reach check (player rig position vs target block) to harvest/use/place command validation in MultiplayerSurvivalSync, mirroring the existing station-proximity pattern.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["vr-interaction"]
- **MergedCount:** 1

## 46. BlockiverseNetworkAvatarRig falls back to scanning every Transform in the scene, potentially every LateUpdate

- **Severity:** Medium
- **Impact:** FindNamedTransform iterates FindObjectsByType<Transform>(FindObjectsInactive.Include) — every transform in the loaded scene — to find controllers by the string names 'Left Controller'/'Right Controller'. ResolveTrackingSources runs from ApplyTrackingSources, which is called every LateUpdate for the owner (and every frame pre-spawn via RefreshLocalTrackingPose). If either hand transform is absent or renamed (headless host, MultiplayerTest scene, bootstrapper rename), this becomes two full-scene scans per frame in a scene that also contains hundreds of generated chunk objects — a sustained CPU drain on Quest. It also couples Networking-layer code to GameObject names authored by the Editor-layer bootstrapper.
- **Files:** ["Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkAvatarRig.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** BlockiverseNetworkAvatarRig.cs:78–94 (LateUpdate → ApplyTrackingSources), 162–164 (ApplyTrackingSources → ResolveTrackingSources), 270–291 (ResolveTrackingSources / FindNamedTransform iterating FindObjectsByType<Transform>(FindObjectsInactive.Include)). Names originate in BlockiverseProjectBootstrapper.cs:1750/1756/1819/1825. Contrast: SurvivalVitalsRuntime throttles its equivalent fallback scan with nextClockSearchTime (SurvivalVitalsRuntime.cs:88–101).
- **RecommendedFix:** Throttle the fallback search (copy the SurvivalVitalsRuntime nextClockSearchTime pattern), resolve via XROrigin/camera-offset child lookup (as BlockiverseMetaAvatarPresenter.cs:157–158 already does with cameraOffset.Find) instead of a global Transform scan, and hoist the controller names into BlockiverseProject constants.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 47. BlockiverseProjectBootstrapper is a 4,952-line single static class mixing project config, asset generation, and pixel-level UI construction

- **Severity:** Medium
- **Impact:** The bootstrapper is the canonical source of all scene/prefab wiring (per project policy), so its health matters more than a normal editor script. One file/class holds ~141 methods spanning Android/OpenXR/URP player settings, input-action schema surgery, texture/atlas pixel painting (FreshwaterTilePixel etc.), XR rig assembly, and hand-built world-space UI for ~15 panels with hardcoded layout vectors (several methods ~150 lines, e.g. EnsureXrRigComfortMenu 2068–2216, EnsureBlockMenuPlaceholder 2440–2588, EnsureNewWorldMenuPanel 4115–4263). Navigating, reviewing, and safely editing it is slow, and merge conflicts concentrate here since every wiring change must land in this file.
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** wc -l = 4,952; method outline shows 141 method definitions in one static class; Run() at lines 197–219 sequences 15 Ensure*/Configure* phases; UI panel builders run from ~3846 to 4952.
- **RecommendedFix:** Split into partial classes or focused static classes by phase, preserving the single Run() entry point: BootstrapperPlayerSettings, BootstrapperAssets (materials/atlas/icons/audio), BootstrapperInputActions, BootstrapperXrRig, BootstrapperMenusUi, BootstrapperNetwork. Pure mechanical extraction; no behavior change, and BlockiverseBootstrapEditModeTests keeps it honest.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 48. BlockiverseWorldSessionController (UI assembly) owns world generation, persistence orchestration, and file management — application logic in the presentation layer

- **Severity:** Medium
- **Impact:** The UI assembly's session controller implements seed folding, biome spawn-search math (FindSpawnForBiome, an O(r²) ring scan over SurvivalBiomeResolver), preset construction, save-slot allocation/sanitization, autosave scheduling, and full load/restore sequencing — none of which is UI. Because this logic is trapped at the top of the dependency stack, lower layers cannot reuse it (directly causing the MultiplayerWorldPersistence duplication and its divergences) and it can only be exercised through a MonoBehaviour wired to menu events.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs"]
- **Evidence:** 850-line MonoBehaviour in Blockiverse.UI: GenerateWorld/RegenerateBaseWorld (301–324, 653–680), FindSpawnForBiome (344–384), FoldSeed (404), AllocateSavePath/SanitizeFileName (804–848), autosave Update (98–107), full LoadSave restore pipeline (590–651). CLAUDE.md describes UI as 'menu router/panels' yet this class is the de facto application service.
- **RecommendedFix:** Move the world-session service (generation, save/load orchestration, slot management) into Gameplay (which already references Persistence and WorldGen) as a plain class or MonoBehaviour; keep a thin UI adapter that translates MenuActions ids into service calls. This also dissolves the UI/Gameplay persistence duplication finding.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 49. Breaking a timed station mid-craft destroys its inputs/fuel/output; deposited items can never be withdrawn

- **Severity:** Medium
- **Impact:** TickStations prunes a station model the moment its world block no longer matches (harvested or replaced) without returning or dropping its contents — the harvester receives only the kiln/forge block item while all deposited inputs, fuel, and uncollected output vanish. Compounding this, the command channel offers no input/fuel withdrawal (only StationOpen/DepositInput/DepositFuel/CollectOutput), so any item deposited by mistake (including tools, which IsValidTransferItem accepts) is permanently stuck until the station is broken — at which point it is destroyed.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs"]
- **Evidence:** TickStations (lines 1006-1042): mismatch adds to staleStationPositions and `stationModels.Remove(stale)` with no content handling. SurvivalCommandKind (lines 13-32) contains no withdraw-input/fuel command; ProcessHostStationCommand switch (lines 1955-2016) handles only Open/DepositInput/DepositFuel/CollectOutput. ProcessHostHarvest grants only the rolled block drop.
- **RecommendedFix:** On station prune (and/or in ProcessHostHarvest when the harvested block is a timed station), transfer the model's inputs/fuel/output into the breaking player's inventory best-effort (mirroring container auto-loot), and add StationWithdrawInput/StationWithdrawFuel commands for the panel.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 50. Canonical tick-rate and day-length constants defined independently in three assemblies

- **Severity:** Medium
- **Impact:** TicksPerSecond = 20 is declared three times (WorldConstants, SmeltingModel, MiningFormula) and the 24000-tick day twice plus two raw literals. A future tick-rate or day-length change requires edits in Survival, WorldGen, and callers; missing one silently desynchronizes smelting/mining timing from the world clock and from values persisted in saves. VegetationService even hardcodes 24000 twice in the same assembly that owns WorldConstants.TicksPerDay.
- **Files:** ["Assets/Blockiverse/Scripts/WorldGen/WorldConstants.cs", "Assets/Blockiverse/Scripts/Survival/SmeltingModel.cs", "Assets/Blockiverse/Scripts/Survival/MiningFormula.cs", "Assets/Blockiverse/Scripts/Survival/FarmingService.cs", "Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs"]
- **Evidence:** WorldConstants.cs:10–11 (TicksPerSecond=20, TicksPerDay=24000); SmeltingModel.cs:14 (`public const int TicksPerSecond = 20;`); MiningFormula.cs:11 (same); FarmingService.cs:108 (`public const int TicksPerGameDay = 24000;`); VegetationService.cs:245 (`const long WildRegrowthRetryDelayTicks = 24000;`) and :343 (raw `return 24000;`). Root cause: WorldConstants lives in Blockiverse.WorldGen, which Blockiverse.Survival cannot reference (Survival's asmdef references only Voxel and Survival.Health).
- **RecommendedFix:** Move the simulation-time constants (TicksPerSecond, TicksPerDay) down to Blockiverse.Voxel (referenced by both Survival and WorldGen) or Core, then redefine SmeltingModel/MiningFormula/FarmingService constants as aliases of it and replace the raw 24000 literals in VegetationService with WorldConstants.TicksPerDay.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 51. Client weather sync is discarded: the environment snapshot is applied to the pre-join world's WeatherService, which is then replaced when the host world snapshot finishes regenerating

- **Severity:** Medium
- **Impact:** Every joining client's weather (state, Markov RNG position, tick accumulator) diverges from the host for the whole session: the host can be mid-thunderstorm while the client shows clear skies, and all future weather transitions follow different timelines. This silently defeats the entire weather-sync design (RNG state shipped in snapshots 'so late-joiners stay in lockstep') and makes weather-coupled presentation (fog, precipitation, lightning ambience) inconsistent between players.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs"]
- **Evidence:** Host sends ChunkSnapshot then EnvironmentSnapshot on connect (MultiplayerChunkAuthoritySync.HandleClientConnected lines 229-230). The client handles EnvironmentSnapshot immediately (HandleEnvironmentSnapshotMessage lines 733-754) -> CreativeWorldManager.RestoreWeatherSyncState (lines 173-181) applies it directly because weatherService is non-null — Boot's CreativeWorldManager.Awake() already initialized a default world (lines 728-732), so the pendingWeatherSync buffer never engages. Seconds later the background world regeneration completes and FinalizeSnapshot -> InitializeGeneratedWorld -> ConfigureEnvironmentServices creates 'weatherService = new WeatherService(seed)' (line 469) with fresh state; pendingWeatherSync is null so nothing is re-applied. (World time ticks survive because the scene WorldTimeClock object persists.)
- **RecommendedFix:** Buffer the received WeatherSyncState in CreativeWorldManager regardless of whether a weatherService currently exists (keep pendingWeatherSync until consumed by the next ConfigureEnvironmentServices), or have MultiplayerChunkAuthoritySync re-apply the stored environment snapshot after FinalizeSnapshot. Also consider a periodic (e.g. once-per-minute) environment re-sync broadcast to heal drift.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["lan-multiplayer"]
- **MergedCount:** 1

## 52. Comfort menu omits move-speed, eye-height, and UI-scale controls promised by design

- **Severity:** Medium
- **Impact:** Players cannot adjust several comfort and accessibility controls that either already exist in settings or are required by the menus ruleset. ContinuousMoveSpeed and StandingEyeHeight are persisted settings but have no in-game controls; UI text/panel scale is not configurable even though several world-space labels are near the VR legibility floor; smooth-turn speed is fixed; and the left-grip block menu remains available in survival even though catalog selection is ignored there. The result is reduced comfort for motion-sensitive players and weak support for low-vision users.
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseComfortMenu.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseComfortSettings.cs", "docs/rulesets/voxel_survival_menus.md", "Assets/Blockiverse/Scripts/VR/BlockiverseWorldSpacePanelPresenter.cs"]
- **Evidence:** BlockiverseComfortMenu.ConfigureControls only accepts glide/teleport/smoothTurn/snapTurn/vignette controls, and EnsureXrRigComfortMenu builds no move-speed, eye-height, UI-scale, or smooth-turn-speed controls. BlockiverseComfortSettings exposes ContinuousMoveSpeed and StandingEyeHeight and BlockiverseSettingsPersistence persists them, but they are UI-orphaned. The ruleset requires configurable text size/UI scale; generated panels use fixed GameMenuScale and small labels. vr-interaction also notes the quick-menu block catalog is unconditional on mode.
- **RecommendedFix:** Add Move Speed and Standing Eye Height sliders to the comfort menu using existing settings, add a persisted UI Scale/Text Size control consumed by BlockiverseWorldSpacePanelPresenter, optionally add smooth-turn speed, and gate the block catalog quick-menu on creative mode. Rerun the bootstrapper and raise minimum generated body text sizes while adding the scale multiplier.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["accessibility", "vr-interaction"]
- **MergedCount:** 3

## 53. Confirm modal does not block input to the screen beneath it

- **Severity:** Medium
- **Impact:** While the delete-world confirmation (or any error dialog) is open, the buttons of the underlying screen remain raycastable wherever they extend beyond the smaller dialog — e.g. the user can press Play, Rename, or Back on the World Details panel behind the delete confirmation. Pressing Play loads the world and ClearToRoot clears the modal stack without invoking the pending confirmCallback, leaving a stale callback; pressing Back pops the screen the confirmation was about. This violates §2.2 modal priority. Related: OnMenuPressed pushes the pause screen underneath an open modal when the active screen is the gameplay HUD (no HasModal check on that branch).
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** ApplyRouterState (BlockiverseMenuController.cs:500-519) only toggles presenter visibility — the underlying screen stays visible (visible = screenId == activeId) and nothing disables its interactivity; every panel gets its own TrackedDeviceGraphicRaycaster (bootstrapper EnsureTrackedDeviceRaycaster at e.g. 4048, 4141, 4290, 4669) and no CanvasGroup/blocking layer is created. The confirm dialog is 440x320 (ConfirmDialogSize, line 98) vs World Details 560x620, both presented at the same head-relative position (presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, ...) at 4103/4720), so the larger panel's lower buttons protrude past the dialog. ConfirmAccept/ConfirmCancel are the only paths that clear confirmCallback (lines 419-430); UiScreenRouter.ClearToRoot (UiScreenRouter.cs:90-96) silently discards modals. OnMenuPressed's first branch (line 129-130) lacks a HasModal guard.
- **RecommendedFix:** While router.HasModal, disable interaction on non-modal panels — e.g. ApplyRouterState sets a CanvasGroup.interactable/blocksRaycasts=false (or disables the TrackedDeviceGraphicRaycaster) on every visible non-modal presenter, and add a full-size dim/blocker image behind the confirm dialog. Invoke or explicitly cancel confirmCallback when the stack is cleared, and add !router.HasModal to OnMenuPressed's gameplay branch.
- **Confidence:** Medium
- **NeedsManualVerification:** true
- **Sources:** ["ui-menu-flow"]
- **MergedCount:** 1

## 54. Connection approval disabled with no join gate, player cap, or encryption

- **Severity:** Medium
- **Impact:** Any device that can reach UDP 7777 on the host can join an in-progress game with no password or approval, and there is no cap on concurrent clients. An attacker can open unlimited connections (each spawns a player NetworkObject, allocates a host inventory, and triggers a full late-join world snapshot regeneration), exhausting host CPU/memory.
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Prefabs/Networking/BlockiverseNetworkManager.prefab", "Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkSession.cs", "Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkConfig.cs"]
- **Evidence:** BlockiverseProjectBootstrapper.cs:1250 sets `networkManager.NetworkConfig.ConnectionApproval = false;` and no ConnectionApprovalCallback is assigned anywhere (grep across Assets/Blockiverse returns none). The prefab sets `m_UseEncryption: 0`. BlockiverseNetworkSession.StartHost/StartClient (lines 69-104) call SetConnectionData with no SetServerSecrets/DTLS and never register approval. There is therefore no authentication, no max-connection enforcement, and no integrity protection on the LAN session. Each new client triggers HandleClientConnected -> SendLateJoinSnapshot + SendEnvironmentSnapshot (MultiplayerChunkAuthoritySync.cs:214-231) and a fresh inventory.
- **RecommendedFix:** Enable ConnectionApproval with a callback that (a) enforces a maximum player count, (b) optionally validates a host-set session password/token carried in the connection payload, and (c) rate-limits new connections per source. Enable UnityTransport encryption (SetServerSecrets / DTLS) for the session so payloads and the reconnect GUID are not exposed on the LAN.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["security", "lan-multiplayer"]
- **MergedCount:** 2

## 55. Creative undo/redo histories and clipboard survive world switches and can corrupt a newly loaded world

- **Severity:** Medium
- **Impact:** Neither CreativeInteractionController.Configure nor BlockiverseCreativeToolsPanel's WorldEditService clears undo/redo state when a different world is initialized. WorldEditService.Undo/Redo apply the recorded change lists blindly via world.SetBlock with no expected-block validation, so after loading world B a single Undo press replays world A's region edit inverse into world B at the same coordinates (silent block corruption that then gets autosaved). CreativeInteractionController's per-block undo is partially protected by its expectedCurrentBlock check but still rejects/coincidentally applies stale entries.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/WorldEditService.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseCreativeToolsPanel.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeInteractionController.cs"]
- **Evidence:** WorldEditService.Undo (lines 154-169) writes changes[i].PreviousBlock unconditionally; the service instance is a readonly field of the persistent panel (BlockiverseCreativeToolsPanel.cs:29) and is never recreated on world load. CreativeInteractionController.Configure (lines 32-50) replaces `world` but does not clear undoStack/redoStack (declared lines 10-12). CreativeWorldManager.ConfigureInteractionController re-Configures the same controller instance for every loaded world.
- **RecommendedFix:** Clear undo/redo stacks and the clipboard whenever the bound VoxelWorld instance changes (in CreativeInteractionController.Configure and via a world-changed hook for the panel's WorldEditService), and/or record the world instance alongside history entries and refuse to apply entries from another world.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 56. Creative-tools time-of-day and time-scale sliders are not gated during LAN sessions, letting any peer desynchronize the shared world clock

- **Severity:** Medium
- **Impact:** Unlike region edits and weather cycling (both blocked while a session is listening), the time-scale and time-of-day sliders write straight to the local WorldTimeClock. A host dragging time-scale makes its tick stream race ahead of all clients (and the save records host ticks); a client dragging it desyncs only itself. Either way the deterministic-lockstep contract for crops/fluids/day-night breaks permanently for the session, and peers see different lighting.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseCreativeToolsPanel.cs", "Assets/Blockiverse/Scripts/Gameplay/WorldTimeClock.cs"]
- **Evidence:** BlockiverseCreativeToolsPanel.OnTimeScaleChanged (lines 282-285) and the time-of-day handler (line 279, SetNormalizedTime) have no NetworkSessionActive() guard, while CycleWeather (lines 293-297) and CanEdit (lines 344-348) explicitly block during sessions ('Weather control is host/offline only.', 'Region tools are unavailable during a LAN session.'). WorldTimeClock.SetTimeScale/SetNormalizedTime mutate only the local clock; nothing replicates them.
- **RecommendedFix:** Apply the same NetworkSessionActive() gate to the time-of-day and time-scale handlers (status message + SetValueWithoutNotify revert), or, if host-side time control is desired, replicate the change by having the host broadcast a fresh environment snapshot after any clock adjustment.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["lan-multiplayer"]
- **MergedCount:** 1

## 57. Crop growth stages do not affect yield — harvesting a just-planted crop equals a mature one

- **Severity:** Medium
- **Impact:** All grain/berry stages (S0-S5) alias to the same fixed 1x drop, so the entire deterministic growth system (1200-tick intervals, light/moisture conditions, stage chains) produces zero gameplay value: there is no reward for waiting and no penalty for early harvest. Ruleset §11.2 specifies mature harvests of grain_bundle ×1-3 and berry_cluster ×2-4. Berrybush regrow (2 game days) fires on harvesting any stage, making instant re-harvest the optimal strategy.
- **Files:** ["Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs", "Assets/Blockiverse/Scripts/Survival/BlockHarvestRules.cs", "Assets/Blockiverse/Scripts/Survival/FarmingService.cs", "docs/rulesets/voxel_survival_ruleset.md"]
- **Evidence:** ItemRegistry.cs:158-168 RegisterDropAlias maps GrainStalk through GrainStalk_S4 and Berrybush through Berrybush_S5 all to the same single-item drop; BlockHarvestRules.cs:154-164 registers each stage with no DropTable (fixed Drop count 1). FarmingService.OnBlockHarvested (line 385-389) queues regrowth for any berrybush stage. Ruleset §11.2 crop table: 'Meadow Grain ... Harvest grain_bundle ×1–3', 'Berrybush ... berry_cluster ×2–4'.
- **RecommendedFix:** Give only the mature stage (GrainStalk_S4, Berrybush_S5, Reedgrass_S3) the full DropTable yield (1-3 / 2-4 / 2-5) and make immature stages drop nothing or a seed only, restoring the §11.2 wait-for-maturity incentive.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 58. Dead bootstrapper scene path: EnsureBootSceneInteractionTestBlock is never called, leaving BlockiverseHighlightTarget and BlockiverseHighlight.mat orphaned

- **Severity:** Medium
- **Impact:** A whole feature chain is dead: the bootstrapper method that created the 'Interaction Test Block' is uncalled (the bootstrapper now actively removes that object from the Boot scene), the BlockiverseHighlightTarget MonoBehaviour it wired appears in no scene or prefab and has no runtime caller, and BlockiverseHighlight.mat is still regenerated on every bootstrap run but referenced by nothing. A PlayMode test still exercises the component by adding it to a self-created object, testing behavior the shipped game never uses. ~80 lines of editor code, a runtime component, a generated material, and a test are all removable. Classification: EnsureBootSceneInteractionTestBlock = definitely unused; BlockiverseHighlightTarget = only used in tests; BlockiverseHighlight.mat = definitely unused (but kept alive by editor tooling).
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseHighlightTarget.cs", "Assets/Blockiverse/Materials/BlockiverseHighlight.mat", "Assets/Blockiverse/Tests/PlayMode/BlockiverseInteractionPlayModeTests.cs"]
- **Evidence:** BlockiverseProjectBootstrapper.cs:1599 defines EnsureBootSceneInteractionTestBlock; grep shows no call site, and EnsureBootScene at line 1127 calls `RemoveRootGameObject(scene, InteractionTestBlockName)` instead. BlockiverseHighlightTarget guid c618760c5a164cc090aa34ceaf345005 appears in no .unity/.prefab/.asset; its only references are the dead bootstrapper method (line 1628) and BlockiverseInteractionPlayModeTests.cs:34 (`firstObject.AddComponent<BlockiverseHighlightTarget>()`). BlockiverseHighlight.mat guid d1257df724dcf4ea3a018ab73b6f2fdd has zero scene/prefab references but is recreated by EnsureMaterial at BlockiverseProjectBootstrapper.cs:717 and loaded only at the dead line 1623.
- **RecommendedFix:** Delete EnsureBootSceneInteractionTestBlock, the EnsureMaterial(HighlightMaterialPath, …) call at line 717, BlockiverseHighlightTarget.cs, BlockiverseHighlight.mat, the HighlightMaterialPath/HighlightColor constants, and the highlight-target PlayMode test case. Keep the RemoveRootGameObject cleanup call for one release so existing generated scenes are scrubbed.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["dead-code"]
- **MergedCount:** 1

## 59. Death has no consequences and no bedroll recovery path; fail-state loop is toothless

- **Severity:** Medium
- **Impact:** On death the player respawns at world spawn with full health/hunger/thirst and their complete inventory — no item drop, no durability cost, no corpse run. The bedroll (§9.2 'Sets respawn point') does not exist as an item or block, so HasBedrollSpawn is hard-coded false and the death menu can never offer the §6.21 'Respawn at Bedroll' option; dying far from base means a flavorless teleport home that can even be exploited as free fast-travel. The death screen also cannot show the spec's 'Dropped items at: X..' line. Death is currently a reward (full vitals restore + teleport), inverting the survival risk model.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/UI/MenuActions.cs", "docs/rulesets/voxel_survival_menus.md", "docs/rulesets/voxel_survival_ruleset.md"]
- **Evidence:** SurvivalVitalsRuntime.cs:46-48 'Bedroll respawn anchors are not implemented yet (no bedroll block exists)... HasBedrollSpawn => false'; Respawn() (lines 275-281) restores all vitals and repositions the rig, never touching the inventory. Grep for bedroll/wayflag blocks in BlockRegistry/ItemId: absent. voxel_survival_menus.md §6.21 mockup includes 'Dropped items at: X 118, Y 44, Z -39' and a bedroll respawn action; ruleset §9.2 lists the bedroll recipe (leafmoss ×6, reed_fiber ×8, fiber_cord ×4).
- **RecommendedFix:** Implement the bedroll block + recipe and a per-player spawn anchor (persisted in player save state), and pick a death penalty consistent with the menus mockup — at minimum drop the inventory into a recoverable container at the death position (ContainerInventoryStore already supports position-keyed inventories).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design", "ui-menu-flow"]
- **MergedCount:** 2

## 60. Difficulty setting (easy/normal/hard) is purely cosmetic

- **Severity:** Medium
- **Impact:** The New World menu offers a difficulty selector (per menus §6.3), the value is saved in the manifest, shown in world details and load lists — but no gameplay system reads it. Hazard damage, vitals decay, starvation cadence, drop rates, and resource tuning are all constants. Players choosing 'hard' get an identical game; the option is a broken promise and any future change will silently rebalance existing saves.
- **Files:** ["Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs", "Assets/Blockiverse/Scripts/SurvivalHealth/BlockHazards.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs", "Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs"]
- **Evidence:** Grep for 'Difficulty' across Scripts/: hits only NewWorldConfig (option list), BlockiverseWorldSessionController (stores currentDifficulty into the save manifest), SaveListModel/WorldDetailsPanel (display), MultiplayerWorldPersistence and WorldSaveService (persistence). Zero references in Survival, SurvivalHealth, WorldGen, or Gameplay simulation code. SurvivalVitals.cs:16-22 and BlockHazards.cs:37-42 are hard constants.
- **RecommendedFix:** Define a small DifficultyProfile (vitals decay multiplier, hazard damage multiplier, starvation interval) resolved from the manifest at world load and injected into SurvivalVitalsRuntime/BlockHazards lookups; or remove the selector until it does something.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design", "accessibility"]
- **MergedCount:** 2

## 61. Falling off the world edge strands the player on the invisible void floor with no recovery path

- **Severity:** Medium
- **Impact:** BlockiverseVoidSafetyFloor catches players 8 m below the world so they don't fall forever, but nothing returns them: the world surface is a sheer ~100 m wall above, jump height is 1.3 m, blocks cannot be placed outside world bounds, and the pause menu has no 'return to spawn' action. In creative mode (no vitals, no death) the player is stuck permanently; in survival they must wait to starve. Worse, saving while stranded persists the rig position, so Continue/Load restores them onto the void floor. The teleport arc can only target chunk TeleportationAreas, which from 8 m below the world edge are usually unreachable by the projectile curve.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/BlockiverseVoidSafetyFloor.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/UI/MenuActions.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs"]
- **Evidence:** BlockiverseVoidSafetyFloor.Configure (lines 32-61): topY = -fallAllowanceMeters (-8), a plain BoxCollider with 8 m horizontal margin — no trigger, no respawn callback. Grep shows no consumer that rescues the player (no kill-Y, no return-to-spawn on contact). MenuActions.Pause (lines 97-106) has no respawn/return-to-spawn entry. SurvivalVitalsRuntime.BuildPlayerSaveState (lines 212-230) saves the raw rig position, RestorePlayerSaveState (lines 243-245) restores it verbatim. CreativeInteractionController.CanPlaceBlock (line 120) rejects out-of-bounds placement, so the player cannot build stairs back.
- **RecommendedFix:** Treat the void floor as a rescue trigger: when the rig contacts it (or rig Y < topY for N seconds), teleport the player back to the world spawn (with a fade), or add a 'Return to Spawn' action to the pause menu. Also clamp BuildPlayerSaveState's saved position to within world bounds or re-spawn on load when the saved position is below the world.
- **Confidence:** Medium
- **NeedsManualVerification:** true
- **Sources:** ["vr-interaction"]
- **MergedCount:** 1

## 62. FarmingService.ScanAndTrackCrops does a full per-position world scan (4.2M GetBlock + HashSet lookups) on every world init

- **Severity:** Medium
- **Impact:** ConfigureEnvironmentServices calls farmingService.ScanAndTrackCrops(World) at every world initialization (boot, new world, load, host start, and the late-join client finalize). Its triple nested loop calls world.GetBlock(new BlockPosition(...)) — paying bounds-check + index math — plus a CropBlocks HashSet.Contains for each of the 4,194,304 positions. VegetationService had the identical problem and was fixed to use VoxelWorld.CollectBlockPositions, with a comment explicitly warning that 'a per-position GetBlock scan over a full-size world (4M+ blocks) stalls the main thread on world init, including the late-join client finalize path' — FarmingService was left on the slow path. This adds an estimated hundreds of milliseconds to every world-entry hitch on Quest-class CPUs.
- **Files:** ["Assets/Blockiverse/Scripts/Survival/FarmingService.cs", "Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/Voxel/VoxelWorld.cs"]
- **Evidence:** FarmingService.cs:149-173 (ScanAndTrackCrops): `for (int y...) for (int z...) for (int x...) { var pos = new BlockPosition(x, y, z); if (CropBlocks.Contains(world.GetBlock(pos)) ...` over full Bounds. Contrast VegetationService.cs:126-131 comment + CollectBlockPositions usage; VoxelWorld.CollectBlockPositions (VoxelWorld.cs:81-97) is the linear flat-array sweep designed for this. Call site: CreativeWorldManager.cs:487.
- **RecommendedFix:** Mirror the VegetationService fix: collect candidate positions with world.CollectBlockPositions for each crop block id (CropBlocks is a known finite set) into a reused scratch list, then reconcile trackedCrops from that. This turns ~4.2M dictionary-checked GetBlock calls into one linear array pass per crop block id.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["performance"]
- **MergedCount:** 1

## 63. FluidFlowService reconstructed flow levels can disagree with the live host's levels, diverging spread/retraction

- **Severity:** Medium
- **Impact:** Configure() rebuilds flowing-cell budgets with a multi-source BFS that takes the best budget over all current paths, while the live simulation assigns a cell's level only once when it spreads into air and never improves it when a shorter source path later opens (ProcessCell writes only into Air cells). After terrain edits near flowing fluid, a host's stored level for a cell can be lower than the BFS-derived level a late joiner or a reloaded save computes for the same geometry. HasSupport (neighborLevel > level) and the level>=2 spread gate then make different retraction/spread decisions on different peers, so fluid extents drift apart between the host and late joiners despite the deterministic-lockstep design.
- **Files:** ["Assets/Blockiverse/Scripts/WorldGen/FluidFlowService.cs"]
- **Evidence:** Configure BFS (lines 129-153) calls TryImproveLevel which raises existing cells to the best path budget (lines 175-193); the live path in ProcessCell only sets flowLevels when writing into Air (lines 379-411) and OnBlockChanged never recomputes levels for surviving cells. HasSupport (lines 418-450) compares neighbor levels, so level disagreements change retraction outcomes.
- **RecommendedFix:** Make the live sim re-improve levels when geometry changes (e.g. on OnBlockChanged, re-run a localized TryImproveLevel relaxation around the edit), or have HasSupport/spread derive levels from a periodic local recomputation instead of sticky assignments, so continuous and reconstructed states converge to the same fixpoint.
- **Confidence:** Medium
- **NeedsManualVerification:** true
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 64. Food and cooking content reduced to two raw items; campfire/prep-board food loops missing

- **Severity:** Medium
- **Impact:** The only edibles are raw Berry Cluster (+12 hunger) and raw Grain Bundle (+25). The campfire cooking table (§12.2: berry_mash, flatbread, cooked_morsel) and prep-board foods (§9.6: trail_ration, berry_mash) are unimplemented, and the items (berry_mash, flatbread, trail_ration, raw/cooked_morsel) do not exist in ItemId/ItemRegistry. The Prep Board station exists but offers exactly one recipe (field bandage), and the Campfire's crafts are two fluid conversions. The eat-to-survive loop is maximally repetitive and the cooking-station fantasy the rulesets build (3 food stations, pantry preservation) has no payoff.
- **Files:** ["Assets/Blockiverse/Scripts/Survival/ConsumableEffects.cs", "Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs", "Assets/Blockiverse/Scripts/Survival/ItemId.cs", "docs/rulesets/voxel_survival_ruleset.md"]
- **Evidence:** ConsumableEffects.TryApply (lines 17-46) handles exactly FieldBandage, CleanWaterFlask, BerryCluster, GrainBundle. CraftingRecipeBook: PrepBoard appears once (FieldBandage, line 119-120); Campfire recipes are CleanWaterFlask and Brightsalt only (lines 89-94). Grep for berry_mash/flatbread/trail_ration/cooked_morsel in ItemId.cs: absent. Ruleset §12.2 campfire table and §9.6 trail_ration ×2 / berry_mash ×1 recipes.
- **RecommendedFix:** Add the §12.2/§9.6 food items and recipes (berry_mash, flatbread, trail_ration at minimum) with hunger values that beat raw equivalents, so the campfire/prep-board stations earn their place in the progression.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 65. Forced 'session ended' LAN panel bypasses the router and its Close button does nothing

- **Severity:** Medium
- **Impact:** When a client's host disconnects, the LAN panel force-enables its own canvas over whatever the player is doing, but because the router never pushed the LAN screen, the panel's Close button (routed through LanMultiplayerClose) is a no-op — the player cannot dismiss the panel with the button it presents; it only disappears as a side effect of an unrelated router transition (e.g. pressing the Menu button to open pause). The spec's reconnect flow (§6.18) also lacks the dedicated Reconnect action (multiplayer.reconnect); users are told via status text to reuse Join.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs"]
- **Evidence:** EnsureSessionEndedMenuAvailable (BlockiverseMultiplayerSessionMenu.cs:314-330) enables all parent canvases/raycasters directly when IsShowingSessionEndedMessage, without informing the router. The Close button is persistently wired to BlockiverseMenuController.CloseLanMultiplayerScreen (bootstrapper 3892-3897) → HandleAction(LanMultiplayerClose), which pops only 'if (router.ActiveScreen.ScreenId == MenuActions.LanMultiplayerScreen)' (BlockiverseMenuController.cs:359-361) — false in the forced-show case (active is GameplayHud/Title). ApplyRouterState will hide the panel on the next router change because its presenter reports IsVisible (canvas enabled).
- **RecommendedFix:** Instead of enabling canvases directly, raise an event the menu controller handles by pushing the LanMultiplayerScreen route (so visibility, input target, and Close all stay consistent); alternatively make CloseLanMultiplayerScreen fall back to hiding the panel's presenter when the LAN screen is not the active route. Add a dedicated Reconnect button bound to the previous join address.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["ui-menu-flow"]
- **MergedCount:** 1

## 66. Four registered items (worldroot, deepmantle, snowpack, frostglass) have unloadable icons due to spriteMode: 2 with an empty sprite sheet

- **Severity:** Medium
- **Impact:** These four blocks are harvestable/placeable survival resources, but their inventory/crafting icons never load: the textures are imported as Sprite Mode = Multiple with zero sprite rects defined, so no Sprite sub-asset exists. EnsureItemIconLibrary silently skips null sprites, so the icon library ships with 138 of 142 icons — confirmed in the committed XR rig prefab, which is missing exactly these four ids. Re-running the bootstrapper does NOT heal this (it only forces textureType, not spriteImportMode), so fresh builds also ship with blank icon slots: SurvivalInventoryPanel.SetSlotIcon disables the icon Image when the sprite is null, leaving empty squares for these items.
- **Files:** ["Assets/Blockiverse/Art/Textures/Items/worldroot.png.meta", "Assets/Blockiverse/Art/Textures/Items/deepmantle.png.meta", "Assets/Blockiverse/Art/Textures/Items/snowpack.png.meta", "Assets/Blockiverse/Art/Textures/Items/frostglass.png.meta", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab", "Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs", "Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs", "Assets/Blockiverse/Art/Textures/Items/deepmantle.png", "Assets/Blockiverse/Art/Textures/Items/frostglass.png", "Assets/Blockiverse/Art/Textures/Items/snowpack.png", "Assets/Blockiverse/Art/Textures/Items/worldroot.png"]
- **Evidence:** All four .png.meta files have `spriteMode: 2` + `sprites: []` + `internalIDToNameTable: []` (every other icon has spriteMode: 1); BlockiverseProjectBootstrapper.cs:2898-2907 only fixes `importer.textureType != TextureImporterType.Sprite` then `if (sprite == null) continue;` with no warning; parsed BlockiverseXRRig.prefab icon library block: 138 ids/138 sprite refs, and the diff against the Items folder is exactly ['deepmantle','frostglass','snowpack','worldroot']; all four ids are registered in ItemRegistry.cs (Resource items with block refs); SurvivalInventoryPanel.cs:148-149 `slotIcons[slotIndex].enabled = icon != null`. No test references BlockiverseItemIconLibrary.
- **RecommendedFix:** Fix the four .meta files to `spriteMode: 1` (matching every other item icon). Harden EnsureItemIconLibrary to force `importer.spriteImportMode = SpriteImportMode.Single` when no sprite loads, and log a warning naming any Items/*.png that yields no Sprite. Add an EditMode test asserting every ItemRegistry-registered id loads a non-null Sprite from Assets/Blockiverse/Art/Textures/Items/<id>.png (M4ArtAssetValidationEditModeTests only checks file existence, which is why this passed).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["asset-integration", "asset-integration-run1", "dead-code"]
- **MergedCount:** 3

## 67. Foveated rendering feature enabled but no FFR level is ever set — GPU savings never realized

- **Severity:** Medium
- **Impact:** The OpenXR 'Meta XR Foveation' feature is enabled for Android, but nothing in the codebase configures a foveation level (no OVRManager in the generated scene, no OVRPlugin.foveatedRenderingLevel / XRDisplaySubsystem foveation calls; the only repo hit for 'foveation' outside SDK code is the bootstrapper's feature-id string). With the runtime default level (off), the app renders full resolution across the entire view. For a fragment-bound app on Quest (4x MSAA, full-screen voxel geometry, per-pixel additional lights), enabling FFR level 2-3 (or dynamic) is one of the largest free GPU wins available (commonly 10-25% GPU savings), directly improving thermals and frame-rate headroom.
- **Files:** ["Assets/XR/Settings/OpenXR Package Settings.asset", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** OpenXR Package Settings.asset: 'MetaXRFoveationFeature Android' block (around line 1946) has m_enabled: 1 but the feature block carries no level configuration; Unity's 'FoveatedRenderingFeature Android' (line ~1463) is m_enabled: 0 and 'MetaXRSubsampledLayout Android' (line ~456) m_enabled: 0. Repo-wide grep for foveat* in Assets/Blockiverse/Scripts matches only BlockiverseProjectBootstrapper.cs:77 (feature id string 'com.meta.openxr.feature.foveation'); no runtime call sets a level.
- **RecommendedFix:** Set a fixed foveation level at startup (e.g. via OVRPlugin.foveatedRenderingLevel = High with dynamic enabled, or Unity's XRDisplaySubsystem foveatedRenderingLevel/flags when using the Unity feature), ideally raising it during known-heavy moments (world load, fluid storms). Verify on-device that the periphery quality is acceptable with the flat-shaded art style — it almost always is. Consider also enabling Subsampled Layout (Vulkan) to reduce FFR fringe artifacts and save more bandwidth.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["performance"]
- **MergedCount:** 1

## 68. Generated UI sprites (8) and VFX particle sprites (16 assets total) are produced and test-enforced but wired into nothing

- **Severity:** Medium
- **Impact:** The art pipeline generates and the M4 test mandates hotbar_frame, selected_slot, health_pip, inventory_panel, crafting_panel, multiplayer_status_badge, settings_panel, feedback_toast plus 8 particle sprites (rain splash, snowflake, ember, dust, puff, sparks, fog wisp) — but no scene, prefab, material, or bootstrapper code references any of them. The HUD/menus are built from Unity's built-in rounded sprite + flat colors, and BlockiverseVfxPool renders every particle with a fallback `new Material(Shader.Find("Sprites/Default"))` because its serialized particleMaterial is never assigned — so all block-break/craft/weather VFX appear as untextured squares instead of the authored particles. Either the integration was never finished (visual-quality gap on device) or 16 assets plus their test entries are dead weight.
- **Files:** ["Assets/Blockiverse/Art/Sprites/UI/", "Assets/Blockiverse/Art/Sprites/VFX/", "Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxPool.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Tests/EditMode/M4ArtAssetValidationEditModeTests.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab", "scripts/art/generate-art-assets.py"]
- **Evidence:** GUID search across all .unity/.prefab/.mat/.asset: all 16 sprite guids (e.g. hotbar_frame 9227a740…, rain_splash_particle 72a22845…) are UNREFERENCED; bootstrapper has zero occurrences of any UI/VFX sprite name and EnsureXrRigFeedback only does EnsureComponent<BlockiverseVfxPool>(rig) without assigning particleMaterial; BlockiverseVfxPool.cs:75-77 fallback to Sprites/Default with no texture; panel construction uses GetRoundedSprite() (bootstrapper lines 1530, 2108, 2478); M4ArtAssetValidationEditModeTests.cs:25-47 enforces the files exist.
- **RecommendedFix:** Wire them in via the bootstrapper: create a particle material per VFX cue (or a single material using the appropriate particle sprite) and assign BlockiverseVfxPool.particleMaterial; use the UI sprites on the HUD sections they were drawn for (hotbar frame, selected slot, health pips, panel backgrounds, status badge, toast). If the flat-color UI is the intended final look, delete the unused sprites and their entries in the generator and M4 test instead of carrying dead assets.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["asset-integration", "asset-integration-run1", "dead-code"]
- **MergedCount:** 3

## 69. Harvest gating deviates from §6.2: wrong tool or low tier blocks the break entirely instead of slow-break-with-reduced-drops

- **Severity:** Medium
- **Impact:** Ruleset §6.2 lets any block break slowly with the wrong tool (terrain still drops; resource nodes drop nothing), preserving player agency. The implementation rejects the action outright for any block with harvestTierMin > 0 unless the exact preferred tool class AND tier are held — e.g. a tier-3 Spade cannot break graystone at all. The MineTicks penalties for wrong tool (×0.25) and low tier (×0.15) are implemented but unreachable for tiered blocks because the preview rejects first. This changes the intended difficulty texture from 'inefficient but possible' to hard walls.
- **Files:** ["Assets/Blockiverse/Scripts/Survival/ResourceHarvestService.cs", "Assets/Blockiverse/Scripts/Survival/MiningFormula.cs", "docs/rulesets/voxel_survival_ruleset.md"]
- **Evidence:** ResourceHarvestService.TryPreviewHarvest line 250-251: 'if (rule.HarvestTierMin > 0 && (usedTool != rule.EffectiveTool || toolTier < rule.HarvestTierMin)) return ... InsufficientTool'. Ruleset §6.2 table: 'Wrong tool but sufficient tier | Block breaks slowly; terrain drops normally; resource nodes drop nothing' and 'Correct tool but insufficient tier | Block breaks very slowly, resource nodes drop nothing'.
- **RecommendedFix:** Allow the harvest when the tool mismatches but suppress drops per the §6.2 matrix (empty drop for resource nodes, normal drop for terrain with wrong tool), letting the existing MineTicks penalties express the cost; keep the hard reject only for hardness-infinite blocks.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 70. Hold-to-mine is the only mining input; spec-required toggle-to-mine alternative is missing

- **Severity:** Medium
- **Impact:** Survival mining requires holding the right trigger continuously for the full break duration while keeping aim on the block. Players with limited grip strength, tremor, or fatigue conditions cannot sustain trigger holds; the project's own accessibility rules (§10.3 'Hold-to-mine and toggle-to-mine should both be supported') require an alternative that does not exist.
- **Files:** ["Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs", "docs/rulesets/voxel_survival_menus.md", "Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs"]
- **Evidence:** BlockiverseCreativeInputBridge.cs line 35: 'Hold-to-mine (§7.3): survival break is a timed hold on a fixed target', line 255: 'Survival mode: hold-to-mine — the press starts a timed hold'; BlockiverseInputRig.IsBreakHeld (line 95) is polled as a release safety net. voxel_survival_menus.md §10.3 requires both hold-to-mine and toggle-to-mine. No toggle setting exists in BlockiverseComfortSettings/BlockiverseFeedbackSettings.
- **RecommendedFix:** Add a 'Toggle to mine' option (BlockiverseComfortSettings + persistence + comfort menu toggle in the bootstrapper). In BlockiverseCreativeInputBridge, when enabled, treat the first break press as starting the hold and cancel on a second press or on aim leaving the block, instead of requiring IsBreakHeld.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["accessibility"]
- **MergedCount:** 1

## 71. Host never validates mining time or rate for survival harvest commands

- **Severity:** Medium
- **Impact:** Hold-to-mine (§7.3) is enforced purely client-side: BlockiverseCreativeInputBridge.TickMining accumulates Time.deltaTime locally and only then calls TrySubmitHarvest. ProcessHostHarvest validates tool/tier/capacity/world state but has no notion of elapsed mining time or per-client rate limiting, and no alive-check. A modified or buggy client can instant-mine arbitrary blocks (including VeryHard tier-gated blocks, as long as it owns a qualifying tool) and continue acting while dead, undermining the survival economy in LAN co-op.
- **Files:** ["Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs"]
- **Evidence:** BlockiverseCreativeInputBridge.cs:97-124 TickMining accumulates `miningElapsedSeconds += Time.deltaTime` and submits at the threshold; ProcessHostHarvest (MultiplayerSurvivalSync.cs:1138-1243) contains no timing, cadence, or death validation — harvest.WorkRequired (mine ticks) is computed but never compared against anything on the host.
- **RecommendedFix:** Track per-client last-harvest timestamps on the host and reject (or queue) harvests arriving faster than the block's MineTicks for the authoritative tool; optionally also gate commands from clients whose vitals report dead (would require vitals reporting, or accept as a documented trust boundary for LAN play).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 72. Hosting is coupled to whichever world happens to be loaded: metadata mismatch blocks hosting after single-player play, and a missing multiplayer save silently adopts the current world

- **Severity:** Medium
- **Impact:** A player who previously hosted (creating multiplayer-world.vxlworld from world X) and later plays a single-player world Y — or simply reboots into the default seed-6401 sandbox when X wasn't the default — gets 'Unable to load saved multiplayer world because the save metadata does not match the initialized host world' on every Host attempt, with no in-UI way to load the matching world into the host slot. Conversely, with no multiplayer save on disk, pressing Host silently turns the currently loaded single-player world into the LAN world and saves it as multiplayer-world on shutdown, surprising players and interacting badly with the dual-autosave issue.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs"]
- **Evidence:** RestoreSavedWorldBeforeHostStart (lines 94-184): when no save exists it returns true keeping the current world (lines 103-109); when a save exists, SavedWorldMatchesInitializedWorld (lines 393-403) requires width/height/depth/chunkSize/seed of the save to equal the currently initialized worldManager.World — which is whatever the player last loaded, or the Awake() default (CreativeWorldManager.cs:728-732, seed 6401 via CreateDefaultGeneratedWorld line 719). Nothing re-initializes the world to match the multiplayer save before the comparison.
- **RecommendedFix:** When the multiplayer save exists but mismatches, regenerate the host world from the save's own settings (registry hash, dims, seed are all in the manifest) before applying deltas, instead of requiring the live world to already match — i.e. make hosting load the multiplayer world outright. The menus ruleset (voxel_survival_menus.md line 1019: 'Loads or initializes canonical world state, then starts host session') already describes this behavior.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["lan-multiplayer"]
- **MergedCount:** 1

## 73. Lightning player-safety exclusion checks the origin-pinned PlayerObject root, and strike feedback (LightningStruck) fires host-only

- **Severity:** Medium
- **Impact:** The 8-block 'no point-blank strikes' comfort rule protects remote clients only at world origin — never at their actual position — so clients can receive jump-scare point-blank lightning the design explicitly forbids; conversely an 8-block dead zone sits uselessly at the world corner. Separately, because LightningStruck is raised only on the host (mutation-owning peer), clients in the same thunderstorm never see the flash or hear thunder — they only see a turf block silently change via the delta — a clear cross-peer presentation divergence.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/EnvironmentDynamicsController.cs", "Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkAvatarRig.cs"]
- **Evidence:** IsNearAnyPlayerHead (lines 251-270) uses 'client.PlayerObject.transform.position' — the player NetworkObject root that nothing ever moves from (0,0,0) (see the avatar-root finding; PublishPose serializes an unmoved root, BlockiverseNetworkAvatarRig). The correct synced position is available via the rig's HeadAnchor, which MultiplayerSurvivalSync.TryResolveClientBlockPosition already uses (MultiplayerSurvivalSync.cs:2879-2901). LightningStruck is declared 'Fired on the host when a strike lands... clients get the block change via deltas' (lines 38-40) and TryApplyLightningStrike early-returns on non-owners (line 167), so no client-side flash/thunder event exists.
- **RecommendedFix:** Use the avatar rig HeadAnchor world position for the exclusion check (mirror TryResolveClientBlockPosition), and broadcast a small 'lightning struck at position' named message (or piggyback on the scorch delta with a flag) so clients can play the flash/thunder/VFX locally.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["lan-multiplayer"]
- **MergedCount:** 1

## 74. Load failures are reported to the hidden title-screen status label

- **Severity:** Medium
- **Impact:** When loading a save fails (corrupt save, schema mismatch, 'Save not found'), the user is on the Load World screen but the error is written to the title menu's status label, which is hidden behind it — the Load button appears to silently do nothing, with no visible explanation or recovery hint.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs"]
- **Evidence:** LoadSelectedSave error path: menuController?.SetTitleStatus("Save not found.") (BlockiverseWorldSessionController.cs:445); LoadSave failure paths: SetTitleStatus($"Failed to load: {result.Error}") (line 600) and SetTitleStatus("Failed to load the world.") (line 648). SetTitleStatus writes only to titleMenu's status label (BlockiverseMenuController.cs:225-228). These actions fire while router.ActiveScreen is LoadWorldScreen (pushed at line 354) — ApplyRouterState hides the title panel whenever it is not the active screen, so the message is invisible until the user cancels back. By contrast the World Details flows correctly use the visible ShowError modal (e.g. PlayDetailsSave, line 479).
- **RecommendedFix:** Use menuController.ShowError(...) for load failures triggered from the Load World and Details screens (it renders as a modal over the current screen), or add a status label to the Load World panel and route failures there; keep SetTitleStatus for failures that occur while the title screen is actually active (Continue).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["ui-menu-flow"]
- **MergedCount:** 1

## 75. Load World screen is hard-capped at 6 saves with no scrolling, paging, sort, or search

- **Severity:** Medium
- **Impact:** Players with more than six worlds cannot see, load, rename, duplicate, or delete the seventh and later saves (sorted by last-played) from inside the game — those worlds become unreachable through the UI even though they exist on disk. The SaveListModel's sort and search capabilities (spec §6.4 'Sort selector', 'Search field') are implemented but never exposed.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseLoadWorldPanel.cs", "Assets/Blockiverse/Scripts/UI/SaveListModel.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** BlockiverseLoadWorldPanel.cs:13 'const int MaxEntries = 6'; RefreshList (lines 74-90) only binds entryButtons.Length rows; the bootstrapper builds exactly 6 fixed rows with no ScrollRect (EnsureLoadWorldMenuPanel, BlockiverseProjectBootstrapper.cs:4269, 4317-4328). SaveListModel.SetSort/SetSearch (lines 69-79) have no UI callers (grep confirms only tests use them).
- **RecommendedFix:** Add paging (Prev/Next buttons cycling the visible window over model.VisibleSaves) or a ScrollRect list to the Load World panel, and wire at least the sort selector; until then, surface a 'N more saves not shown' label so users know saves are hidden.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["ui-menu-flow"]
- **MergedCount:** 1

## 76. Loading a save instantly rotates the player's view (rig yaw) with no comfort fade anywhere

- **Severity:** Medium
- **Impact:** CreativeWorldManager.PositionRig sets the rig's world rotation to the saved yaw when a world loads, rotating the camera without user input — the single most disorienting camera operation in VR — and there is no fade-to-black anywhere in the project (the only 'fade' code is music volume), so world load, death respawn, and void-floor landings are all hard visual cuts. The restored heading is also wrong by construction: BuildPlayerSaveState records rig.eulerAngles.y, but the player's actual facing is rig yaw + headset local yaw, so the restored view direction differs from what was saved by however the player was physically turned at save and load time.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs"]
- **Evidence:** CreativeWorldManager.PositionRig (lines 746-753): rigObject.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f)); called from SurvivalVitalsRuntime.RestorePlayerSaveState (lines 243-245). BuildPlayerSaveState (line 224) saves rig.transform.eulerAngles.y, not camera heading. Grep -i fade across Assets/Blockiverse/Scripts matches only BlockiverseMusicController. PositionRigAtSpawn (lines 736-743) hard-teleports on respawn (Respawn(), line 280) with no transition.
- **RecommendedFix:** Use XROrigin.MatchOriginUpCameraForward (or rotate the rig by savedYaw minus the camera's current local yaw) so the player's actual view matches the saved heading, and add a brief HMD fade (a simple full-screen quad on the camera or compositor fade) around world load, respawn teleports, and the initial spawn placement.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["vr-interaction"]
- **MergedCount:** 1

## 77. Menu backdrop blockiverse_launch_landscape.png ships uncompressed (1672x941 RGBA32, ~6.3 MB) with point filtering and no mips

- **Severity:** Medium
- **Impact:** The image is used as a full-screen RawImage on the world-space menu canvas, so it is in view at title/pause. The Android platform override forces uncompressed import (textureCompression: 0, overridden: 1), costing ~6.3 MB of APK size and GPU memory on Quest for one menu texture, and filterMode 0 (Point) with enableMipMap 0 on a non-pixel-art, NPOT 1672x941 image produces visible aliasing/shimmer when the world-space panel is viewed at any angle or distance in the headset. This is the single largest texture in the project and the only one where the project-wide 'uncompressed pixel art' import template is clearly the wrong choice.
- **Files:** ["Assets/Blockiverse/Art/Sprites/Branding/blockiverse_launch_landscape.png", "Assets/Blockiverse/Art/Sprites/Branding/blockiverse_launch_landscape.png.meta", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab"]
- **Evidence:** PNG header: 1672x941. Meta: enableMipMap: 0 (line 9), maxTextureSize: 2048 (line 33), filterMode: 0 (line 36), Android platform block textureCompression: 0 with overridden settings (lines 71-86). Used at BlockiverseXRRig.prefab:52532 `m_Texture: {fileID: 2800000, guid: 6e4cf5e26f6546f0b967f7ea13af3f7c}` on a UnityEngine.UI.RawImage (line 52524).
- **RecommendedFix:** Import this texture ASTC-compressed (e.g., ASTC 6x6) with mipmaps enabled and bilinear/trilinear filtering; optionally resize to a power-of-two-friendly 1024-wide variant for the menu. If the meta is script-generated, add a 'large smooth image' template to the generator distinct from the pixel-art template.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["asset-integration-run1"]
- **MergedCount:** 1

## 78. Mining and HUD feedback gaps include silent inventory-full harvest rejection

- **Severity:** Medium
- **Impact:** Hold-to-mine plays a strike cue every 0.4s but gives no progress bar, crack overlay, or target/mine-time readout (menus §6.6 specs 'Target: ... Tool: ... Mine Time: 2.0s'), so players cannot tell a 2s dig from a 30s one or whether progress is happening at all. With a full inventory the break input does nothing with zero feedback (preview fails, host rejects InventoryFull, and SurvivalFeedbackBridge only cues InsufficientTool). The HUD has no day counter or clock despite a full day/night cycle driving crops and ambience. Release-to-cancel is also stricter than §6.2's 1.25s interruption grace.
- **Files:** ["Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalFeedbackBridge.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "docs/rulesets/voxel_survival_menus.md", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs"]
- **Evidence:** BlockiverseCreativeInputBridge.TickMining (lines 97-124): elapsed/required tracked privately, never surfaced to UI; CancelMining on any release/aim change (line 105-108). SurvivalFeedbackBridge.OnCommandFeedback (lines 105-117): harvest failure cues only for InsufficientTool. Bootstrapper HUD sections (lines 2823-2826) build Health/Inventory/Crafting/Shared Crate only — no clock/target elements. Menus §6.6 HUD elements table.
- **RecommendedFix:** Surface mining progress (radial near the target or controller-anchored bar) fed from TickMining, add an InventoryFull toast/cue in SurvivalFeedbackBridge, and add the day/time line to the HUD from WorldTimeClock (data already exists: TotalElapsedTicks / NormalizedTime).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design", "accessibility"]
- **MergedCount:** 2

## 79. Multiplayer host load bypasses RestoreWorldTimeTicks, leaving FluidFlowService's tick anchor stale — duplicated restore path causes an unbounded catch-up loop

- **Severity:** Medium
- **Impact:** When a host loads multiplayer-world.vxlworld, MultiplayerWorldPersistence calls WorldTimeClock.RestoreElapsedTicks directly instead of CreativeWorldManager.RestoreWorldTimeTicks, so fluidFlowService.SyncToWorldTick is never called. The fluid sim's lastWorldTick stays at the value it was configured with at boot (0 for the default world), while the clock jumps to the saved absolute tick. The next world tick then runs FluidFlowService.Tick's catch-up loop from tick 1 to the saved tick — for a world with hours of play time that is hundreds of thousands of loop iterations on the main thread in one frame, and any fluid cells activated by applying the saved block deltas get re-stepped through that entire replay (frame hitch on Quest plus fluid state that differs from the same save loaded in single-player).
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/WorldGen/FluidFlowService.cs"]
- **Evidence:** MultiplayerWorldPersistence.cs:161 `worldManager.WorldTimeClock?.RestoreElapsedTicks(result.Data.WorldTimeTicks);` — contrast with the single-player path BlockiverseWorldSessionController.cs:619 which calls worldManager.RestoreWorldTimeTicks. CreativeWorldManager.cs:195–210 documents exactly why the wrapper exists ('the next Tick must not replay every elapsed tick') and calls fluidFlowService?.SyncToWorldTick(totalElapsedTicks). FluidFlowService.cs:299–315: `for (long tick = lastWorldTick + 1; tick <= worldTick; tick++)` with no cap.
- **RecommendedFix:** Change MultiplayerWorldPersistence.cs:161 to call worldManager.RestoreWorldTimeTicks(result.Data.WorldTimeTicks) (the wrapper already buffers when the clock is missing). Defensively, also cap or fast-forward the catch-up loop in FluidFlowService.Tick when the gap exceeds one day of ticks. Add an EditMode test asserting lastWorldTick equals the restored tick after a host load.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 80. MultiplayerSurvivalSync is a ~2,900-line god MonoBehaviour owning the entire survival economy plus its wire protocol

- **Severity:** Medium
- **Impact:** The single most change-prone class in the codebase concentrates at least nine responsibilities: client command submission (12+ TrySubmit* methods), host-side command resolution (11 ProcessHost* methods, several 85–117 lines long, e.g. ProcessHostStationCommand lines 1915–2031, ProcessHostHarvest 1138–1244), a ~100-line wire dispatcher (HandleCommandRequestMessage 2094–2192), FastBuffer serialization helpers (2968–3072), per-client inventory registry and reconnect-GUID stash (lines 190–200), station model ownership + real-time ticking (Update, 447–462), station persistence export/restore (925–1004), request dedup windows (ProcessedRequestWindow 2947), mode switching/hotbar state (273–308), and presentation feedback events. Every new survival verb touches this one file across 4–5 places, making regressions in the co-op economy channel likely and code review of any change expensive.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs"]
- **Evidence:** File is 3,073 lines; the MonoBehaviour spans lines 168–3073. Method map confirms the mixed concerns: TrySubmit* client API (507–1136), ProcessHost* host logic (1138–2031), wire codec (2968–3072), network callback wiring (2620–2780), station persistence (925–1004), inventory snapshots (3005–3056).
- **RecommendedFix:** Split along the seams that already exist in the code: (1) a plain-C# SurvivalCommandCodec for the FastBuffer read/write helpers, (2) a SurvivalHostResolver (engine-free where possible) holding the ProcessHost* logic, inventories, dedup windows and station models, (3) a thin MonoBehaviour transport adapter owning NetworkManager callbacks and message registration, (4) move StationPersistentState export/restore next to the persistence mapper. The existing Configure() seam and PlayMode tests make this refactor mechanically safe.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 81. New-world creation never resets the WorldTimeClock — new worlds inherit elapsed ticks from boot/previous worlds

- **Severity:** Medium
- **Impact:** The WorldTimeClock lives on a Boot-scene object and ticks from app start (including at the title screen and across world switches). LoadSave restores the clock from the save, but CreateNewWorld never calls RestoreElapsedTicks(0)/Configure, so a freshly created world starts with whatever TotalElapsedTicks accumulated so far — including a previously loaded world's restored ticks. The save list day counter ((ticks / TicksPerDay) + 1) shows e.g. 'Day 37' for a brand-new world, the starting time-of-day is arbitrary rather than the canonical morning start, and hunger/thirst interval anchoring begins at a random phase. The wrong tick count is then persisted into the new save.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/Gameplay/WorldTimeClock.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs"]
- **Evidence:** CreateNewWorld (BlockiverseWorldSessionController.cs:256-293) calls InitializeGeneratedWorld + SetGameMode + SaveCurrentWorld with no clock reset; grep shows RestoreElapsedTicks is only called from CreativeWorldManager.RestoreWorldTimeTicks (load/late-join paths) and MultiplayerWorldPersistence. WorldTimeClock.Update (lines 92-107) accumulates totalElapsedTicks continuously; the clock is created once by the bootstrapper (BlockiverseProjectBootstrapper.cs:1361) and never recreated per world.
- **RecommendedFix:** In CreateNewWorld, call worldManager.RestoreWorldTimeTicks(0) (and reset normalized time to DefaultStartNormalizedTime via Configure) after InitializeGeneratedWorld and before the initial SaveCurrentWorld.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 82. No LAN discovery and no host-IP display: joining requires typing an IP the game never shows, with a useless 127.0.0.1 default

- **Severity:** Medium
- **Impact:** The shipped join flow is barely usable on device: the host's status line reads 'Hosting LAN session on 0.0.0.0:7777' (the listen address, not the device's LAN IP), so neither player can learn the address to type from inside the game — they must dig through Quest Wi-Fi settings on the host headset. The join field defaults to 127.0.0.1, which can never reach another headset. There is no UDP broadcast/mDNS discovery anywhere in the codebase (grep confirms no Socket/UdpClient/NetworkInterface usage).
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs", "Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkConfig.cs"]
- **Evidence:** DescribeSessionState Hosting branch (BlockiverseMultiplayerSessionMenu.cs:223-225) interpolates session.Config.ListenAddress (default '0.0.0.0', BlockiverseNetworkConfig.cs:10). ApplyDefaultAddressText (lines 209-213) defaults the join field to DefaultAddress '127.0.0.1'. No discovery code exists in Assets/Blockiverse/Scripts (searched for Discovery/UdpClient/Socket/GetHostAddresses/NetworkInterface). The menus ruleset specifies manual address entry only, so this matches spec but the spec itself omits any way to learn the host address.
- **RecommendedFix:** At minimum, resolve and display the host's LAN IPv4 (System.Net.NetworkInterface unicast addresses, ignoring loopback) in the Hosting status line. Better: add a lightweight UDP LAN-broadcast discovery (host beacons name+port; join menu lists discovered hosts), which removes manual IP entry entirely — a major usability win for the Quest system keyboard.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["lan-multiplayer"]
- **MergedCount:** 1

## 83. No per-client rate limiting on the host command/mutation channels (flood + broadcast amplification)

- **Severity:** Medium
- **Impact:** A connected client can flood the host with survival commands or (in creative worlds) raw block mutations as fast as it can send. Each accepted operation makes the host broadcast inventory/crate/station/delta snapshots to all clients, so one malicious peer can amplify its traffic to grief or stall the whole session.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs"]
- **Evidence:** HandleCommandRequestMessage (MultiplayerSurvivalSync.cs:2094-2186) dispatches every client command with no throttling; the only bound is the per-client ProcessedRequestWindow (lines 2947-2966) which merely de-duplicates by requestId, and a flooder simply increments requestId each call. Accepted crate transfers call BroadcastSharedCrateSnapshot (line 1905) and accepted station commands call BroadcastStationSnapshot (line 2025); HandleMutationRequestMessage commits then BroadcastDelta to all remote clients (MultiplayerChunkAuthoritySync.cs:290-296, SendToRemoteClients 860-873). No token-bucket or per-tick cap exists on any path.
- **RecommendedFix:** Add a per-client rate limiter (e.g. token bucket per connection per command kind) in the host dispatchers, dropping or disconnecting clients that exceed a sane cadence, and coalesce snapshot broadcasts so a burst of commands cannot fan out one snapshot per command to every peer.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["security"]
- **MergedCount:** 1

## 84. Only Latin-coverage Liberation Sans fonts are bundled; CJK/Arabic/Thai/Indic text typed via the Quest system keyboard cannot render

- **Severity:** Medium
- **Impact:** A player whose Quest is set to Japanese/Korean/Chinese/Arabic etc. can open the native system keyboard (BlockiverseSystemKeyboardField) and type a world name or seed in their script; TMP has no glyphs for it, so the input field, Load World rows, World Details, and the rename field show missing-glyph boxes. It also means no future localized UI can ship for those languages without new font assets, and RTL scripts additionally need shaping TMP does not provide natively.
- **Files:** ["Assets/TextMesh Pro/Resources/TMP Settings.asset", "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset", "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset", "Assets/TextMesh Pro/Fonts/LiberationSans.ttf", "Assets/Blockiverse/Scripts/VR/BlockiverseSystemKeyboardField.cs"]
- **Evidence:** The only font assets in the project are the stock TMP essentials: LiberationSans SDF.asset has m_AtlasPopulationMode: 0 (static), m_SourceFontFile: {fileID: 0}, and exactly 250 m_Unicode entries (ASCII + Latin-1). Its m_FallbackFontAssetTable (line 7715) points only to LiberationSans SDF - Fallback.asset, which is dynamic (m_AtlasPopulationMode: 1) but sources the same LiberationSans.ttf — Liberation Sans covers Latin/Greek/Cyrillic only, no CJK/Arabic/Thai/Devanagari. TMP Settings.asset has m_fallbackFontAssets: [] (no global fallback) and m_ClearDynamicDataOnBuild: 1. BlockiverseSystemKeyboardField.cs:51 opens TouchScreenKeyboard.Open(..., TouchScreenKeyboardType.Default) and streams keyboard.text straight into TMP_InputField (lines 59–71), so any script the OS keyboard emits reaches TMP.
- **RecommendedFix:** Add a CJK-capable fallback (e.g. Noto Sans CJK subsets or Noto Sans dynamic font assets) to TMP Settings' global fallback list so player-typed names render even pre-localization; budget atlas memory for Quest (use dynamic atlases with multi-atlas enabled). Alternatively, until fonts are added, restrict the world-name input's content to the supported character set and message it, rather than rendering boxes. Verify keyboard behavior per-language on device.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["localization"]
- **MergedCount:** 1

## 85. P1: ConsumableEffects and BlockHazards tables are engine-free and completely untested

- **Severity:** Medium
- **Impact:** ConsumableEffects.TryApply is the canonical item→vitals mapping (field_bandage heals via RecoveryWrap; clean_water_flask +40 thirst/+20 stamina; berry_cluster +12 hunger/+4 thirst; grain_bundle +25 hunger; unknown → false). BlockHazards is the canonical hazard table (thornbrush Feet|Head env-damage 1 @0.5s; campfire Feet|GroundBelow heat 2; emberflow source+flow sharing ONE hazard id so wading between cells never double-applies). Both are plain NUnit-testable in seconds, both encode ruleset §13/§6 behavior, and neither has any test — the networked consumable test only asserts the inventory decrement, never the vitals effect.
- **Files:** ["Assets/Blockiverse/Scripts/Survival/ConsumableEffects.cs", "Assets/Blockiverse/Scripts/SurvivalHealth/BlockHazards.cs"]
- **Evidence:** grep 'ConsumableEffects' under Assets/Blockiverse/Tests/ → zero hits; grep 'BlockHazards|HazardDamage' under Tests → only PlayerVitalsEditModeTests constructing a HazardDamage directly (lines 128-167). ConsumableEffects.cs:17-46; BlockHazards.cs:37-75.
- **RecommendedFix:** Add ConsumableEffectsEditModeTests: FieldBandageAppliesRecoveryWrap (damaged vitals heal exactly 25), CleanWaterFlaskRestoresThirstAndStamina (+40/+20 clamped), BerryClusterRestoresHungerAndThirst, GrainBundleRestoresHungerOnly, UnknownItemReturnsFalseAndChangesNothing. Add BlockHazardsEditModeTests: ThornbrushTriggersOnFeetAndHeadOnly, CampfireTriggersOnFeetAndGroundBelowOnly, EmberflowSourceAndFlowShareOneHazardId (Assert.AreSame / id equality so the throttle dedupes), HazardAmountsAndKindsMatchTable, NonHazardBlocksReturnFalse.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 86. P1: CreativeWorldManager save-extras round trip and container loot flow untested

- **Severity:** Medium
- **Impact:** FillSaveExtras/RestoreSimulationState — the actual glue the session controller uses to persist and restore weather machine state, world time, sapling progress, wild regrowth, berrybush regrowth and crop tracking — is never round-tripped at the manager level (each sub-service has its own export/restore tests, but a wiring regression in the manager, e.g. forgetting to restore one collection or restoring before the clock, is uncovered; the crop-tracking test at CreativeWorldManagerCropTrackingEditModeTests covers exactly one such historical bug). RestoreContainerStore (emptied crates stay empty on reload) and TryLootContainerInto/ContainerLooted/SuppressContainerAutoLoot (the structure-loot pickup flow) also have no tests.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs"]
- **Evidence:** grep 'FillSaveExtras|RestoreSimulationState|TryLootContainerInto|RestoreContainerStore|SuppressContainerAutoLoot|ContainerLooted' under Assets/Blockiverse/Tests/ → zero hits. CreativeWorldManager.cs:217 (FillSaveExtras), 281 (RestoreSimulationState), 619-630 (TryLootContainerInto), 634-642 (RestoreContainerStore, with the 'saved state is authoritative over regenerated loot' comment).
- **RecommendedFix:** Add CreativeWorldManagerSaveExtrasPlayModeTests: (1) FillThenRestoreSimulationStateRoundTripsAllCollections — configure a survival world, advance weather/clock, plant a sapling and queue regrowth, FillSaveExtras → fresh manager + RestoreSimulationState → FillSaveExtras again, assert the two extras are deeply equal; (2) RestoreContainerStoreKeepsEmptiedCratesEmpty — restore a save with an empty container entry over a freshly generated world whose loot table would repopulate it, assert it stays empty; (3) TryLootContainerIntoTransfersAllAndRaisesContainerLooted, including the full-inventory remainder case (store keeps the rest, event behavior asserted).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 87. P1: DeterministicHash has no golden-value regression tests — save compatibility is unguarded

- **Severity:** Medium
- **Impact:** Saves store only changed-block deltas against terrain regenerated from the seed, and all seed-derived rolls (terrain, biomes, structures, farming) flow through DeterministicHash. Every existing determinism test (SurvivalTerrainPresetIsDeterministicForSameSeed, BiomeIndexIsDeterministicForSameSeedAndPosition, GeneratedWorldIsReproducibleForSameSeed) only checks self-consistency within one build. If anyone tweaks Mix/Avalanche, every test still passes while every existing player save loads against different regenerated terrain (effective world corruption) and peers built from different versions desync.
- **Files:** ["Assets/Blockiverse/Scripts/Voxel/DeterministicHash.cs", "Assets/Blockiverse/Tests/EditMode/VoxelCoreEditModeTests.cs"]
- **Evidence:** DeterministicHash.cs:9-39 (Hash/UnitRoll, FNV-1a-style Mix at 41-49, Avalanche at 51-62). grep 'DeterministicHash' in Assets/Blockiverse/Tests/ shows only consumers using it to predict rolls (CreativeWorldManagerCropTrackingEditModeTests.cs:26, FarmingServiceEditModeTests.cs:275) — no test pins concrete output values.
- **RecommendedFix:** Add DeterministicHashEditModeTests with golden values: HashProducesPinnedValuesForKnownInputs (e.g. Assert.That(DeterministicHash.Hash(seed:6401, 1, 2, 3, salt:7), Is.EqualTo(<current value>)) for ~6 input tuples incl. negative coords and int.MaxValue) and UnitRollProducesPinnedValuesAndStaysInUnitInterval (pin 3–4 doubles, plus extra-low/high 32-bit fold cases). Add a comment that these constants are a save-format compatibility contract. Optionally add a world-level golden: hash the full block array of SurvivalTerrainPreset for one pinned seed and assert the digest.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 88. P1: Environment snapshot (weather RNG + world tick) late-join sync and cross-peer world-sim lockstep are untested

- **Severity:** Medium
- **Impact:** Every peer ticks the full world simulation locally (CreativeWorldManager.OnWorldTick runs weather, vegetation, farming, fluids with no authority gate), relying on the environment snapshot to put late joiners on the same tick/RNG state. The wire path — SendEnvironmentSnapshot on client connect and its apply handler — is never asserted (AppliedEnvironmentSnapshotCount/SentEnvironmentSnapshotCount counters exist but appear in no test), and no integration test ever advances world time on a connected host+client and compares world state. A silent regression here causes gradual host/client divergence of crops, fluids and weather that no current test can catch.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Tests/PlayMode/Networking/MultiplayerSessionPlayModeTests.cs"]
- **Evidence:** MultiplayerChunkAuthoritySync.cs:229-230 (SendLateJoinSnapshot + SendEnvironmentSnapshot on HandleClientConnected), counters at lines 56-57; CreativeWorldManager.cs:644-660 (OnWorldTick ticks all sim services unconditionally), 504-507 (fluid sim configured against the synced absolute tick for late joiners). grep 'EnvironmentSnapshot|Weather' in MultiplayerSessionPlayModeTests.cs returns nothing. WeatherServiceEditModeTests.RestoringFullStateKeepsTwoServicesInLockstep covers the model only, not the wire.
- **RecommendedFix:** Add PlayMode test LateJoinClientReceivesEnvironmentSnapshotAndSimsInLockstep: host with weather/clock running, connect a client, assert clientSync.AppliedEnvironmentSnapshotCount >= 1 and the client's CreativeWorldManager.GetWeatherSyncState()/CurrentWorldTick equal the host's; then place a Freshwater source via the authoritative mutation channel, advance both WorldTimeClocks by the same tick count, and assert a sampled region of fluid/crop cells is byte-identical between hostWorldManager.World and clientWorldManager.World (cross-peer lockstep regression guard).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 89. P1: Request-replay dedup and out-of-order delta guards are never exercised

- **Severity:** Medium
- **Impact:** Two defensive multiplayer mechanisms have zero coverage: (1) MultiplayerSurvivalSync.TryRejectDuplicate, the idempotency guard preventing a duplicated/replayed survival command (packet duplication, client retry) from double-granting drops or double-consuming items — an economy-duplication bug if it regresses; (2) MultiplayerChunkAuthoritySync's out-of-order chunk delta rejection (IgnoredOutOfOrderChunkDeltaCount is incremented at line 782 but asserted nowhere — the packet-loss test runs over reliable delivery so the branch never fires).
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs"]
- **Evidence:** TryRejectDuplicate called from every ProcessHost* (MultiplayerSurvivalSync.cs:1148, 1254, 1317, 1379, 1464, 1531, 1571, 1621, 1689, 1785); SurvivalCommandResult.DuplicateResult at line 129; grep 'Duplicate' in MultiplayerSessionPlayModeTests.cs → zero hits. MultiplayerChunkAuthoritySync.cs:54 and 782 (IgnoredOutOfOrderChunkDeltaCount) — no test references.
- **RecommendedFix:** Both are testable without a transport: (1) offline [Test] HostRejectsReplayedCommandRequestIdWithoutDoubleApplying — call ProcessHostHarvest twice via the public TrySubmit path with a forced duplicate (or expose a test seam), assert the world block is removed once, the drop granted once, and the second result is the Duplicate failure; (2) [Test] ClientIgnoresChunkDeltaWithStaleSequence — drive the client's delta apply with sequence N then N-1 (via the message handler seam or by refactoring the sequence check into a pure method) and assert IgnoredOutOfOrderChunkDeltaCount == 1 and the world state matches sequence N.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 90. P1: Station panel UI and station persistence glue (Export/RestoreStationStates) untested

- **Severity:** Medium
- **Impact:** BlockiverseStationPanel (the kiln/forge interaction UI: deposit input, deposit fuel, collect output, progress display) has zero tests — unlike the crafting/crate panels which have routing tests. MultiplayerSurvivalSync.ExportStationStates/RestoreStationStates, the glue persisting in-progress smelts across save/load (WorldSaveService round-trips VxlwStation, but nothing tests that a half-finished smelt survives the runtime export→restore cycle with progress, input, fuel and output intact).
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs"]
- **Evidence:** grep 'BlockiverseStationPanel' under Assets/Blockiverse/Tests/ → zero hits; grep 'ExportStationStates|RestoreStationStates' under Tests → zero hits (RestoreStationStates declared at MultiplayerSurvivalSync.cs:982). SmeltingStationModelEditModeTests covers the model (incl. ApplyHostSnapshotMirrorsHostState) but not the sync-level export/restore or the panel.
- **RecommendedFix:** (1) Add StationStatePersistenceEditModeTests: deposit input+fuel into a kiln model via the sync, tick halfway through a craft, ExportStationStates → RestoreStationStates into a fresh sync, tick the remaining duration, assert the output completes exactly on schedule (progress was preserved, not reset). (2) Add StationPanelRoutesDepositAndCollectThroughAuthoritativeSync to the PlayMode suite, mirroring SurvivalHudPanelsRouteCraftAndCrateThroughAuthoritativeSync: bind BlockiverseStationPanel to a host sync, click deposit-input/deposit-fuel/collect, assert the station model and inventory mutate and that depositing an item the player does not hold is rejected without state change.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 91. P1: SurvivalVitalsRuntime (hazard scan, death event, respawn resolution, world drink, player save state) is untested

- **Severity:** Medium
- **Impact:** The runtime that connects vitals to the world — contact-cell hazard scanning (head/feet/ground), per-hazard damage throttling, creative-mode immunity, starvation ticking off the world clock, LocalPlayerDied event (the trigger for the entire death flow), spawn resolution fallback when no generation settings exist (snapshot clients), world-drink cooldown, and BuildPlayerSaveState/RestorePlayerSaveState (null state → reset-to-full contract) — has zero tests. Several of these are documented contracts (e.g. a save without player state must not leak previous-session vitals).
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs"]
- **Evidence:** grep 'SurvivalVitalsRuntime' under Assets/Blockiverse/Tests/ → zero hits. Untested members: TickHazards/CheckHazardCell/TryApplyHazard (147-195), InSurvivalMode gate (132-133), OnWorldTick starvation wiring (135-143), TryDrinkFromWorldSource cooldown (259-267), BuildPlayerSaveState (212-230), RestorePlayerSaveState null→ResetVitalsToFull (235-248), Respawn + ResolveSpawnPosition FindSurfaceY fallback (275-298).
- **RecommendedFix:** Add SurvivalVitalsRuntimePlayModeTests (component on a GameObject with a Camera tagged MainCamera and a configured CreativeWorldManager): (1) HazardCellContactAppliesThrottledDamage — place Thornbrush at the head cell, run two scan intervals, assert exactly the expected damage count; (2) CreativeModeIsImmuneToHazardsAndVitalsDecay; (3) WorldDrinkRestoresThirstOnceWithinCooldown; (4) RestorePlayerSaveStateWithNullResetsVitalsToFull and round-trips position/yaw/vitals otherwise; (5) RespawnWithoutSettingsStandsOnSurfaceColumnCenter (snapshot-client fallback); (6) VitalsDeathRaisesLocalPlayerDiedExactlyOnce. Longer term, extract the contact-cell scan into an engine-free helper (world + 3 cells in, hazard hits out) so it joins the plain NUnit suite.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 92. P1: Tested autosave helper is dead code; both live autosave loops are untested

- **Severity:** Medium
- **Impact:** WorldSaveEditModeTests.ShouldAutoSaveReturnsTrueAfterIntervalAndFalseBeforeIt green-lights WorldSaveService.ShouldAutoSave — but that method has zero production callers. The two real autosave paths (single-player BlockiverseWorldSessionController.Update and multiplayer-host MultiplayerWorldPersistence.Update) re-implement the interval check inline against Time.unscaledTime and are untestable as written. The suite therefore certifies autosave logic that does not run, while the logic that runs can silently break (e.g. the 'stamp before attempt' once-per-interval failure-logging contract at BlockiverseWorldSessionController.cs:174-176).
- **Files:** ["Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs", "Assets/Blockiverse/Tests/EditMode/WorldSaveEditModeTests.cs"]
- **Evidence:** grep -rn 'ShouldAutoSave' in Assets/ excluding Tests matches only its declaration (WorldSaveService.cs:207). BlockiverseWorldSessionController.cs:98-107 and MultiplayerWorldPersistence.cs:248-256 both inline `Time.unscaledTime - last... < WorldSaveService.AutoSaveIntervalSeconds` instead.
- **RecommendedFix:** Refactor both Update loops to call WorldSaveService.ShouldAutoSave (or a small injectable autosave gate taking 'now' as a parameter) so the tested code is the shipped code. Then add: (1) EditMode AutosaveGateFiresOncePerIntervalAndRestampsOnFailure (pure, time injected); (2) PlayMode SessionControllerAutosavesActiveWorldOnInterval using a temporarily reduced interval seam, asserting the manifest ModifiedAtUtc advances without any menu action. If no time seam is added, at minimum delete the dead method so the suite stops asserting unreachable code.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage", "dead-code"]
- **MergedCount:** 2

## 93. Pause-menu Save Game and autosave give no user feedback on success or failure

- **Severity:** Medium
- **Impact:** Pressing Save Game shows nothing — neither the spec'd 'Game saved.' toast nor 'Could not save game.' (§10.4). A failing save (disk full, IO error) is only written to the log, so a player can believe they saved when nothing was persisted; on Quest there is no visible log. Autosave failures are equally silent.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs"]
- **Evidence:** HandleAction case PauseSaveGame only invokes ActionRequested (BlockiverseMenuController.cs:375-377); BlockiverseWorldSessionController.SaveCurrentWorld returns bool and logs on exception (lines 167-210) but no caller surfaces the result — HandleAction (line 153-161) ignores it. The pause BlockiverseActionMenu has a status label built by the bootstrapper (statusLabel, BlockiverseProjectBootstrapper.cs:4093-4097, wired via Configure at 4100) and BlockiverseActionMenu.SetStatus exists (lines 93-97), but the only SetStatus caller is the title path (SetTitleStatus).
- **RecommendedFix:** Expose a SetPauseStatus (or generalize SetTitleStatus) on BlockiverseMenuController; have the session controller report 'Game saved.' / 'Could not save game.' on the pause menu's status label after PauseSaveGame, and show a one-shot error dialog (ShowError) the first time an autosave fails.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["ui-menu-flow"]
- **MergedCount:** 1

## 94. Pervasive English sentence-fragment composition: concatenated/interpolated UI strings with fixed word order and no pluralization support

- **Severity:** Medium
- **Impact:** Even after string extraction, these patterns break under translation: word order is fixed by code-side concatenation, counts use the English "x6" suffix convention, and label:value pairs are fused into single format strings. Languages with different word order, declension, or plural rules (Russian, Polish, German, Japanese counters) cannot be expressed; translators never see complete sentences.
- **Files:** ["Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs", "Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs", "Assets/Blockiverse/Scripts/UI/SurvivalCratePanel.cs", "Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseLoadWorldPanel.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldDetailsPanel.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs"]
- **Evidence:** SurvivalCraftingPanel.cs:309–328: FormatRecipe = $"{FormatStack(recipe.Output)} - {FormatIngredients(recipe)}" plus appended " [needs {station}]"; FormatStack = $"{definition.Name} x{stack.Count}". SurvivalInventoryPanel.cs:94/108/158: $"x{stack.Count}", $"Hotbar {n} / {total}", "{Name} x{Count}". SurvivalCratePanel.cs:74/100: $"Deposited {FormatStack(held)}" / $"Withdrew {FormatStack(stack)}". SurvivalHealthPanel.cs:105: $"{baseState} · Hunger {hunger} · Thirst {thirst} · Stamina {stamina}". BlockiverseLoadWorldPanel.cs:86: $"{Name}  ·  Day {DayCount}". BlockiverseWorldDetailsPanel.cs:51–54 fuses three lines of "Label: value" pairs into one format string. BlockiverseMultiplayerSessionMenu.cs:260/270 appends sentences: $"{reconnectMessage} Last disconnect: {reason}".
- **RecommendedFix:** When extracting strings, convert every composed message into a single parameterized template per message (e.g. craft.success = "Crafted {item} ×{count}") so translators control word order, and adopt a plural-aware formatter (Unity Localization Smart Strings or ICU-style select/plural) for count-bearing messages. Keep FormatStack-style helpers but have them resolve through templates instead of string interpolation.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["localization"]
- **MergedCount:** 1

## 95. Pervasive FindFirstObjectByType/GameObject.Find service-locator idiom (~79 runtime call sites in 36 files) hides the scene wiring contract

- **Severity:** Medium
- **Impact:** Nearly every component lazily resolves collaborators by scanning the scene when its serialized field is null (e.g. BlockiverseWorldSessionController.ResolveReferences resolves five systems, lines 77–94; CreativeWorldManager finds WorldTimeClock/CreativeHotbar/PlacementPreview at 464/679/682; BlockiverseMenuController even calls FindFirstObjectByType<BlockiverseCreativeInputBridge> inline inside a switch case at line 379). This silently masks bootstrapper wiring regressions (a missing serialized reference 'works' by finding whatever instance exists, including a wrong/inactive one), makes initialization order-dependent, and makes dependencies invisible to readers and tests. The code itself acknowledges the fragility: MultiplayerSurvivalSync maintains an inLifecycleResolve flag (lines 2769–2780) solely to warn when the fallback fires outside the expected Awake/OnEnable/Configure window.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalFeedbackBridge.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs", "Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseXRRigMarker.cs"]
- **Evidence:** grep across Assets/Blockiverse/Scripts (excluding Editor) shows 79 FindFirstObjectByType/FindObjectsByType/GameObject.Find call sites in 36 runtime files. Representative: BlockiverseWorldSessionController.cs:81–93, BlockiverseMenuController.cs:212/245/275/332/379, CreativeWorldManager.cs:464/679/682, MultiplayerWorldPersistence.cs:349–359, SurvivalVitalsRuntime.cs:92–108, WeatherFeedbackController.cs:59–65.
- **RecommendedFix:** Treat bootstrapper-assigned serialized references as the only sanctioned wiring: extend the MultiplayerSurvivalSync warning pattern (or a shared ResolveOrWarn helper) to all ResolveReferences fallbacks, and add an EditMode test over the generated Boot scene asserting every [SerializeField] dependency of the core systems is non-null so fallbacks become test failures instead of silent scans.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern", "unity-csharp"]
- **MergedCount:** 2

## 96. Placed storage containers cannot be opened; container menu and §10.5 special rules missing

- **Severity:** Medium
- **Impact:** A player can craft and place a Storage Crate, but there is no interact-to-open flow — world containers only yield their contents when broken (break-to-loot). The menus §6.11 Container Menu (move items both ways, sort, take-all) is unimplemented, and all §10.5 container behaviors (24-slot crate, carryable filled reed basket, pantry jar food preservation, tool rack, deep locker gating) are absent; ReedBasket/ToolRack/PantryJar/DeepLocker are registered placeables with no recipes and no behavior. The only player storage is one global 12-slot 'shared crate' panel not tied to any block (canon container rules imply 24). Base-building — the genre's long-term motivation loop — has essentially no storage gameplay.
- **Files:** ["Assets/Blockiverse/Scripts/Survival/ContainerInventoryStore.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Scripts/UI/SurvivalCratePanel.cs", "docs/rulesets/voxel_survival_menus.md", "docs/rulesets/voxel_survival_ruleset.md"]
- **Evidence:** MultiplayerSurvivalSync.cs:188 'const int SharedCrateSlotCount = 12'; TrySubmitUse (lines 583-641) routes station blocks to StationOpenRequested but has no container-open branch — placed crates fall through to the generic paths. ProcessHostHarvest (lines 1183-1191) is the only container access (TryLootContainerInto on break). ContainerInventoryStore.DefaultContainerSlotCount = 12 vs ruleset §10.5 'Storage Crate | 24'. ItemRegistry registers ReedBasket/ToolRack/PantryJar/DeepLocker (lines 73-76) but CraftingRecipeBook has no recipes for them.
- **RecommendedFix:** Add a StationOpen-style ContainerOpen command for blocks present in ContainerInventoryStore, bind a per-position container panel (the SurvivalCratePanel transfer pattern generalizes), use 24 slots for storage_crate per canon, and add the reed_basket recipe (§9.1).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 97. Raw canonical IDs and C# enum identifiers are rendered directly as UI text (untranslatable by construction, and rough even in English)

- **Severity:** Medium
- **Impact:** Players see machine identifiers: the New World screen displays "survival", "survival_terrain", "flat_builder", "pinewild" as the selector values; the creative catalog category label shows enum names like "DeepRock"; station names are derived by splitting PascalCase enum identifiers; and station/crafting failures render enum member names ("Cannot deposit: OutputBlocked", "Cannot craft …: MissingIngredient"). None of these can be localized without changing the mechanism, because the displayed text IS the identifier.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseNewWorldPanel.cs", "Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseCatalogBrowserPanel.cs", "Assets/Blockiverse/Scripts/Survival/CraftingRecipe.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs", "Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldDetailsPanel.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseCreativeToolsPanel.cs"]
- **Evidence:** BlockiverseNewWorldPanel.cs:40–47/128: ValueGetters return Config.GameMode/WorldPreset/StartingBiome (canonical strings from NewWorldConfig.cs:12–19, e.g. "survival_terrain") straight into cycleValueLabels[idx].text. BlockiverseCatalogBrowserPanel.cs:172: categoryLabel.text = Categories[categoryIndex].ToString() (CreativeCatalogCategory members include DeepRock). CraftingRecipe.cs:21–33: CraftingStationNames.DisplayName builds UI text by inserting spaces into the enum identifier ("ClayKiln" → "Clay Kiln"). BlockiverseStationPanel.cs:170/184: $"Cannot deposit: {result.FailureReason}" prints enum member names. SurvivalCraftingPanel.cs:142/164 same pattern with CraftingFailureReason. BlockiverseWorldDetailsPanel.cs:48–49 Capitalize(save.GameMode) re-cases the canonical id for display. BlockiverseCreativeToolsPanel.cs:123 shows the WeatherState enum raw.
- **RecommendedFix:** Add explicit display-name mappings for every identifier that reaches a label: a per-enum switch or dictionary returning a string-table key (failure reasons, stations, categories, weather states, game mode/difficulty/world size/preset/biome options). This is also an immediate English polish win — "Cannot deposit: output blocked" and "Survival Terrain" instead of identifier text.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["localization"]
- **MergedCount:** 1

## 98. Reconnect inventory stash and GUID bindings leak across sessions and worlds, and PlayerHello identity is unauthenticated

- **Severity:** Medium
- **Impact:** ClearSessionState (run on server/client stop) does not clear stashedInventoriesByGuid or playerGuidsByClientId. If the host stops a session and later hosts a different world in the same app run, a returning player's PlayerHello hands them the inventory stashed from the previous world — items teleport across worlds/sessions (potential progression break or item duplication when combined with the world reload). Stale clientId->GUID bindings from the previous session also survive into the next one. Additionally, any client can claim any GUID in PlayerHello at any time, so on a non-trusted LAN a peer can claim another player's GUID and, after that player disconnects, steal their stashed inventory.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Prefabs/Networking/BlockiverseNetworkManager.prefab"]
- **Evidence:** ClearSessionState (lines 2598-2606) clears inventoriesByClientId, processedRequestsByClientId, stationModels, and pending commands but not playerGuidsByClientId (line 198) or stashedInventoriesByGuid (line 199). HandleClientDisconnected (lines 2641-2655) stashes by GUID; HandlePlayerHelloMessage (lines 2693-2709) binds senderClientId to any received GUID string and immediately hands over any stash with no verification and no per-world scoping.
- **RecommendedFix:** Clear stashedInventoriesByGuid and playerGuidsByClientId in ClearSessionState (or key the stash by (world identity, guid)). For the trust gap, at minimum ignore PlayerHello GUID rebinds for GUIDs currently bound to a different live connection; longer-term, persist per-GUID inventories with the world save (see the inventory-persistence finding) so the in-memory stash stops being the only copy.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["lan-multiplayer", "security"]
- **MergedCount:** 3

## 99. Ruleset drop tables, yield bonuses, and ground-item rules are almost entirely unimplemented

- **Severity:** Medium
- **Impact:** Of the ~20 variable/secondary drops in ruleset §2/§3 (snowpack 1-3 clumps, frostglass 40% shard, shingle_gravel 25% flint, leafmoss 20% seed, clearpane 50% shards, ore nodes 1-3, niter 2-5, etc.) only reedgrass and resin_knot have tables — everything else drops itself ×1 with 100% chance. The §6.4 tier 4-7 yield bonus (10-25% extra ore) is absent. There are no ground-item entities, so §10.4 pickup rules (2.5-block radius, despawn, merge, 3s protection) and the death-screen 'Dropped items at' concept are unimplementable; a full inventory rejects the harvest outright instead of dropping overflow. Mining variance and loot excitement — a core reward texture of the genre — are flattened.
- **Files:** ["Assets/Blockiverse/Scripts/Survival/BlockHarvestRules.cs", "Assets/Blockiverse/Scripts/Survival/ResourceHarvestService.cs", "Assets/Blockiverse/Scripts/Survival/DropTable.cs", "docs/rulesets/voxel_survival_ruleset.md"]
- **Evidence:** Repo grep for 'new DropTable' in runtime code returns exactly two sites (BlockHarvestRules.cs:124, 153). ResourceHarvestService.RollDrop (lines 185-204) handles only Sickle double-roll and Carver full-yield; no tier-based bonus branch exists. Grep for GroundItem/DroppedItem/despawn/pickupRadius across Scripts/ returns nothing. ResourceHarvestService.TryPreviewHarvest (lines 254-263) returns InventoryFull failure, blocking the break. ItemRegistry.cs:83 comment admits 'block mapping used for harvest drop lookup until M6 drop tables'.
- **RecommendedFix:** Extend BlockHarvestRuleSet.CreateDefault with the §2/§3 drop columns as DropTable entries (the DropTable type already supports chance and ranges and multi-entry rolls), add the §6.4 tier bonus to RollDrop, and either add a minimal dropped-item entity or explicitly amend the ruleset to the direct-to-inventory model.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 100. Save/load orchestration duplicated between BlockiverseWorldSessionController (UI) and MultiplayerWorldPersistence (Gameplay), and the copies have already diverged

- **Severity:** Medium
- **Impact:** The same five-step save assembly (BuildSaveExtras, BuildSavedContainers) and restore sequence (suppress auto-loot → ApplyTo → RestoreSimulationState → restore clock → SetGameMode → RestoreContainers → RestoreStations) is implemented twice in different assemblies. Divergence #1 is already a defect (see separate fluid-sim finding). Divergence #2: the multiplayer RestoreContainers validates blank canonical ids and non-positive counts (MultiplayerWorldPersistence.cs:324–336) while the single-player copy does not (BlockiverseWorldSessionController.cs:682–698), so a corrupted slot that multiplayer load tolerates makes a single-player load throw inside the ItemId constructor and surface as a generic 'Failed to load the world.' Every future persistence fix must be applied twice or the formats drift.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs"]
- **Evidence:** BuildSavedContainers: UI lines 226–252 vs Gameplay lines 278–310 (verbatim duplicate, modulo loop variable). BuildSaveExtras: UI 214–224 vs Gameplay 234–244 (identical). RestoreContainers: UI 682–698 (no slot validation) vs Gameplay 312–341 (validates and logs invalid slots). Autosave Update loop duplicated: UI 98–107 vs Gameplay 248–275.
- **RecommendedFix:** Extract a single WorldSessionPersistenceCoordinator (Gameplay layer, since UI already references Gameplay) exposing SaveWorld(savePath, name, …) and RestoreLoadedWorld(WorldLoadResult), used by both the single-player session controller and the multiplayer host persistence. Port the slot-validation guard into the shared restore path.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 101. Spec-mandated feedback-toast/subtitle layer is absent; several events are audio-only with no visual channel

- **Severity:** Medium
- **Impact:** Deaf and hard-of-hearing players miss information that is only ever played as sound: wrong-tool harvest rejection (ToolWrong cue), multiplayer peer join/leave stingers, thunder (the lightning flash is suppressible via Reduced Flash, making storms nearly invisible-audio-only), and teleport confirmation. The project's own rulesets require a 'Subtitles/Feedback Toasts toggle' and a BlockiverseSubtitleToastPanel accessibility layer, plus toast messages like 'This tool is not strong enough.' — none are implemented.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/SurvivalFeedbackBridge.cs", "docs/rulesets/voxel_audio_vfx_ruleset.md", "docs/rulesets/voxel_survival_menus.md"]
- **Evidence:** voxel_audio_vfx_ruleset.md line 81 lists `BlockiverseSubtitleToastPanel  // optional accessibility layer`, line 115 'Subtitles/Feedback Toasts toggle', and §15 requires 'Feedback Toasts: Shows text/icon confirmation for important audio-only events'. Grep for 'Subtitle|Toast' across Assets/Blockiverse/Scripts finds no runtime implementation. SurvivalFeedbackBridge.cs lines 113–116 plays only `BlockiverseAudioCue.ToolWrong` for InsufficientTool rejections (no status text), and lines 152–166 play MultiplayerJoin/Leave cues with no visual counterpart. voxel_survival_menus.md §10.4 specifies toast strings ('This tool is not strong enough.', 'Game saved.') that have no UI surface.
- **RecommendedFix:** Implement a world-space toast panel (new UI script + bootstrapper generation alongside the Survival HUD) that subscribes to MultiplayerSurvivalSync.CommandFeedback, NetworkManager connect/disconnect, and save events, showing the §10.4 strings; add a 'Feedback Toasts' toggle to BlockiverseFeedbackSettings, persist it in BlockiverseSettingsPersistence, and expose it on the audio/feedback settings panel.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["accessibility"]
- **MergedCount:** 1

## 102. Structure catalog 8/22 implemented; all underground loot-tier structures missing and the watchpost cannot spawn on small worlds

- **Severity:** Medium
- **Impact:** Exploration rewards flatten sharply: of the ruleset's 22 structures, only 8 exist, and none of the six underground/cave structures (stoneburrow_cellar, lumen_hollow, ember_vent_outpost, deep_locker_room, staropal_pocket_shrine — loot tiers 2-5) are implemented, so deep mining offers no landmark discoveries. Loot draws from just two tables (common supply, forager food), both early-game. Additionally, weathered_watchpost has minDistanceFromSpawn 128, but the default 'small' world is 128×128 with spawn at the center — the maximum possible distance is ~90 blocks, so the largest implemented loot structure can never generate on default worlds.
- **Files:** ["Assets/Blockiverse/Scripts/WorldGen/StructureService.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs", "docs/rulesets/voxel_structure_generation_ruleset.md"]
- **Evidence:** StructureService definitions (lines 82-93) list exactly: pathmark_stones, forager_lean_to, resin_tap_grove, frost_shelter, drybrush_niter_pit, weathered_watchpost, cave_shrine, bridge_segment. Ruleset §12.1-12.4 lists 22 structures incl. 6 underground. Loot tables: only CommonSupply and ForagerFood (lines 505-526). weathered_watchpost minDistanceFromSpawn: 128 (line 87); BlockiverseWorldSessionController.SizeFor default returns (128,128) with spawn at width/2 (WorldGenerationSettings.cs:46) — max distance to a corner is sqrt(64²+64²) ≈ 90.5.
- **RecommendedFix:** Scale minDistanceFromSpawn to world size (or cap at ~40% of the half-diagonal), and prioritize implementing the underground structure set with tier 2-5 loot tables so the mid/late mining game has discovery rewards.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 103. Sun light forces Hard realtime shadows every frame while URP main-light shadows are enabled at 2048 — contradicting the project's own no-shadow design

- **Severity:** Medium
- **Impact:** BlockiverseLightingCycleController.LateUpdate sets sunLight.shadows = LightShadows.Hard with strength 0.85 every frame, and the Android URP asset has main-light shadows supported with a 2048×2048 shadow map and 50 m shadow distance. Voxel chunks neither cast nor receive (renderer flags off), and the custom voxel shader's comment-documented design is vertex-color lighting — VoxelWorldRenderer.cs:298-300 explicitly says shadow passes 'would only add an extra render pass per light on Quest (per Meta VRC guidance) for no visual gain.' Yet any shadow-casting renderer in range (Meta avatars, network fallback proxies, props) triggers a per-frame 2048 shadow map render pass plus the _MAIN_LIGHT_SHADOWS keyword, making every voxel fragment sample the shadow map (BlockiverseVoxelLit ForwardLit calls GetMainLight(shadowCoord)) — meaningful GPU bandwidth/ALU on a tile-based mobile GPU, in both eyes, for shadows the art style doesn't use. The fluid child even has receiveShadows = true (VoxelWorldRenderer.cs:229).
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/BlockiverseLightingCycleController.cs", "Assets/Blockiverse/Settings/BlockiverseAndroidURPAsset.asset", "Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader", "Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** BlockiverseLightingCycleController.cs:41-44 (LateUpdate → ApplyCurrentLighting) and 70-71 (`sunLight.shadows = LightShadows.Hard; sunLight.shadowStrength = 0.85f;` every frame); BlockiverseAndroidURPAsset.asset: `m_MainLightShadowsSupported: 1`, `m_MainLightShadowmapResolution: 2048`, `m_ShadowDistance: 50`; BlockiverseVoxelLit.shader:26 `#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE`, :73 GetShadowCoord, :81 GetMainLight(input.shadowCoord); VoxelWorldRenderer.cs:298-301 (shadowCastingMode Off + Meta VRC comment), :229 fluid `receiveShadows = true`.
- **RecommendedFix:** Set sunLight.shadows = LightShadows.None in ApplyCurrentLighting and disable main-light shadows in BlockiverseAndroidURPAsset (m_MainLightShadowsSupported: 0, which also strips the shadow keywords/variants). If soft character grounding is wanted later, use a cheap blob/projected shadow instead of a full shadow map. Also stop re-assigning sunLight.type/shadows/renderMode and RenderSettings.ambientMode every LateUpdate — set static values once in Configure and only update intensity/color/rotation/fog per frame.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["performance", "unity-csharp"]
- **MergedCount:** 2

## 104. Survival HUD and launch panels stay visible on title/non-gameplay screens

- **Severity:** Medium
- **Impact:** The survival HUD is permanently enabled in rig space and visible on title, pause, death, and creative screens; on boot it stacks behind the controller-mapping popup and title menu. This clutters the first-run/menu experience, presents empty or irrelevant survival state outside gameplay, and competes with the same central world-space panel area used by menus.
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab", "Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Tests/PlayMode/BootScenePlayModeTests.cs"]
- **Evidence:** EnsureXrRigSurvivalHud parents the HUD under Camera Offset, enables the Canvas, and registers no presenter or router visibility hook. BlockiverseMenuController.ConfigurePresenters registers no GameplayHudScreen presenter for the HUD, so ApplyRouterState never hides it. The controller-mapping popup is configured showWhenStarted true with no first-run PlayerPrefs gate, placing three panels in front of the player at launch.
- **RecommendedFix:** Register the survival HUD as a GameplayHudScreen presenter or toggle it from ApplyRouterState so it is hidden outside gameplay and minimized/hidden in creative mode. Gate the controller-mapping popup behind a first-run preference and keep it reachable from Settings -> Controls.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["vr-interaction", "ui-menu-flow"]
- **MergedCount:** 2

## 105. Survival threat profile is near zero: three contact hazards, 1 HP/min starvation, no fall damage, no night/cold pressure

- **Severity:** Medium
- **Impact:** The only damage sources are thornbrush (1 HP/0.5s), campfire (2), and emberflow (3) — all trivially avoided static blocks — plus starvation/dehydration at 1 HP per 60s per empty vital (death takes ~50-100 minutes of total neglect after the 15-27 minute drain). There is no fall damage, no hostile entities (none specced — but also no compensating pressure), no temperature/night vitals effect, and hunger/thirst do not gate stamina or speed. Combined with free respawns, survival mode has effectively no fail-state pressure, making tools, bandages, food prep, and shelter (the systems the rulesets invest most in) strategically unnecessary.
- **Files:** ["Assets/Blockiverse/Scripts/SurvivalHealth/BlockHazards.cs", "Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs"]
- **Evidence:** BlockHazards.HazardForBlock (lines 53-75) contains exactly thornbrush, campfire, emberflow(+flow). SurvivalVitals constants (lines 16-22): HungerTicksPerPoint 240 (20 min full drain), ThirstTicksPerPoint 180 (15 min), StarvationDamageIntervalTicks 1200 = 1 HP/min. No code path applies fall damage (no caller of ApplyDamage tied to velocity/height); HazardDamageKind.Cold/Void/Suffocation/Toxic enum values (HazardDamage.cs:5-13) have no producers.
- **RecommendedFix:** Tune a real pressure curve: scale starvation damage over time, add environmental pressure already specced in the environment ruleset (cold in tundra/night via the existing temperature model), and consider modest fall damage with a VR comfort toggle. Tie stamina/mining speed to hunger so food matters before the death spiral.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 106. Survival/creative mode switch has no permission model: one pause-menu click grants vitals immunity, and the host can place free blocks in survival worlds

- **Severity:** Medium
- **Impact:** The creative ruleset (§2 PlayerModeState/WorldModeState: canSwitchOwnMode, allowModeSwitching) requires mode switching to be permissioned; the implementation exposes 'Switch Survival/Creative' unconditionally in the pause menu for every peer. Because vitals only tick while CurrentMode == Survival, any player — including a client in a co-op survival world — can toggle to creative to stop hunger/thirst/hazard damage, then toggle back. The host additionally bypasses the survival-economy entirely: client raw mutations are rejected in survival worlds, but the host's creative-path placements are not, letting the host conjure unlimited blocks into a survival world (which other players can then harvest).
- **Files:** ["Assets/Blockiverse/Scripts/UI/MenuActions.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalCreativeModeSwitch.cs", "docs/rulesets/voxel_creative_ruleset.md"]
- **Evidence:** MenuActions.cs:101 adds PauseToggleMode to the pause menu unconditionally; BlockiverseMenuController.cs:378-380 routes it to ToggleSurvivalCreativeMode with no world-mode or permission check. SurvivalVitalsRuntime.InSurvivalMode (lines 132-133) gates all vitals decay and hazards on the local mode. MultiplayerChunkAuthoritySync.cs:274-287 rejects direct mutations only when '!senderIsHost && ... GameMode == Survival'. Creative ruleset line 925: 'Pause | Adds mode switch if permission allows' and §2 permission schema.
- **RecommendedFix:** Gate PauseToggleMode on the world's game mode plus a host-controlled permission (hide it in survival worlds by default), and validate the host's own creative placements against the world GameMode the same way client mutations are.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 107. Test-only scenes and Unity Test Runner artifacts are committed or enabled in build settings

- **Severity:** Medium
- **Impact:** Test-only content can leak into editor/default builds and keeps repository hygiene checks incomplete. MultiplayerTest.unity is enabled in ProjectSettings/EditorBuildSettings.asset even though the scripted build path overrides scenes to Boot only, while the generated InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity artifact is committed at Assets root and is not caught by forbidden-files CI. This creates two sources of truth for build content and leaves stale Unity Test Runner output in source control.
- **Files:** ["ProjectSettings/EditorBuildSettings.asset", "Assets/Blockiverse/Scenes/MultiplayerTest.unity", "Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs", "Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity.meta", "scripts/ci/forbidden-files.sh", "Assets/Blockiverse/Tests/PlayMode/Networking/MultiplayerSessionPlayModeTests.cs", "scripts/art/generate-first-launch-assets.py.meta"]
- **Evidence:** ProjectSettings/EditorBuildSettings.asset lists Assets/Blockiverse/Scenes/MultiplayerTest.unity as enabled; runtime-wiring and performance reports note this means default player builds can include it. Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity and its .meta are committed at Assets root; multiple reports note scripts/ci/forbidden-files.sh does not block that pattern. The official build scripts/BlockiverseBuildSmoke override scenes to Boot, which reduces shipped APK risk but does not fix the editor/default-build source of truth.
- **RecommendedFix:** Disable or remove MultiplayerTest.unity from EditorBuildSettings unless a test build explicitly opts into it; delete the InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity artifact and its .meta; expand scripts/ci/forbidden-files.sh to reject Unity Test Runner InitTestScene artifacts and stray Unity .meta files outside valid asset locations. Keep test scenes referenced only from test code or explicit test build paths.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["security", "unity-csharp", "dead-code", "test-coverage", "asset-integration", "asset-integration-run1", "runtime-wiring", "performance", "anti-pattern"]
- **MergedCount:** 12

## 108. UI assembly hardcodes WorldGen's internal biome enum order as magic indices

- **Severity:** Medium
- **Impact:** BlockiverseWorldSessionController.BiomeIndexFor maps menu biome strings to integers 0–6 that must match the declaration order of the internal TerrainBiome enum in WorldGen. Reordering or inserting a biome in TerrainBiome compiles cleanly but silently makes 'starting biome' spawn searches pick the wrong biome — there is no compile-time or test-time link between the two lists.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/WorldGen/SurvivalLiteWorldPreset.cs"]
- **Evidence:** BlockiverseWorldSessionController.cs:386–401: comment 'TerrainBiome is internal to WorldGen; the resolver exposes biome indexes, whose order is canonical' followed by hardcoded `case "meadow": return 0; … case "highlands": return 6;`. SurvivalLiteWorldPreset.cs:8: `enum TerrainBiome { Meadow, Pinewild, Wetland, Drybrush, Dunes, Tundra, Highlands }` (internal, so UI cannot reference it).
- **RecommendedFix:** Expose a public mapping from WorldGen — e.g. SurvivalBiomeResolver.TryGetBiomeIndex(string canonicalBiomeId) or a public BiomeIds class listing canonical ids in index order — and use it from the UI. At minimum add an EditMode test that asserts BiomeIndexFor agrees with the enum order.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 109. Up to 24 realtime per-pixel point lights from torch blocks under Forward rendering

- **Severity:** Medium
- **Impact:** TorchbudLightManager creates a realtime Point light (range 6, no shadows) for every emissive block up to MaxActiveLights = 24. The URP asset uses classic Forward (m_RenderingMode: 0) with per-pixel additional lights (m_AdditionalLightsRenderingMode: 1, per-object limit 4). Because chunk meshes are huge (a whole 16³ chunk is one renderer), a single torch makes URP evaluate up to 4 additional lights for every fragment of each affected chunk mesh — large screen areas pay per-pixel light loops in the custom shader's _ADDITIONAL_LIGHTS branch. A torch-lit base with many emitters measurably raises fragment cost and CPU light-culling/setup per frame on Quest, and per-object selection of 4 lights across whole-chunk renderers also produces visible light popping.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/TorchbudLightManager.cs", "Assets/Blockiverse/Settings/BlockiverseAndroidURPAsset.asset", "Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader"]
- **Evidence:** TorchbudLightManager.cs:11-13 (MaxActiveLights = 24, LightRange = 6.0f, LightIntensity = 2.2f), 111-122 (AddLight creates LightType.Point, LightRenderMode.Auto); BlockiverseAndroidURPAsset.asset: m_AdditionalLightsRenderingMode: 1, m_AdditionalLightsPerObjectLimit: 4, m_RenderingMode: 0 in BlockiverseAndroidUniversalRenderer.asset; BlockiverseVoxelLit.shader:86-94 per-pixel additional light loop.
- **RecommendedFix:** Since block lighting is already baked into vertex colors via the sky/probe system, consider folding emissive block light into the vertex-color bake (deterministic, zero runtime light cost) and keeping at most 1-3 realtime lights for the nearest emitters for dynamic pop. If realtime lights stay, lower MaxActiveLights, prioritize by distance to the player (currently first-come-first-served with a pending queue), and consider Forward+ (clustered) which decouples light count from per-object limits — though validate Forward+ cost on Quest first.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["performance"]
- **MergedCount:** 1

## 110. VoxelWorld changed-block delta set grows without reconciliation — bloats memory, saves, and the late-join snapshot

- **Severity:** Medium
- **Impact:** VoxelWorld.changedBlocks is a Dictionary<BlockPosition, BlockChange> that only ever grows (SetBlock upserts; entries are never removed when a cell returns to its generated value). Fluid dynamics make this acute: a flood that spreads and then retracts leaves a permanent Air-valued delta for every cell it ever touched; snow accumulation/melt cycles and emberflow burns do the same. Consequences scale together: (1) host memory (~48+ bytes/entry; tens of thousands of dead entries after a few floods); (2) save size and main-thread save time (WriteRegionFiles iterates every entry); (3) the late-join snapshot is sized 80 + 32 × changedBlocks.Count bytes and written into a single Allocator.Temp FastBufferWriter and one named message — a long-running host session can push this into multi-megabyte single-message territory, spiking the host's frame and stressing UTP reliable fragmentation; (4) load time (ApplyTo replays every delta).
- **Files:** ["Assets/Blockiverse/Scripts/Voxel/VoxelWorld.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs"]
- **Evidence:** VoxelWorld.cs:23 `readonly Dictionary<BlockPosition, BlockChange> changedBlocks = new();` and 53-70 SetBlock (`changedBlocks[position] = change;` — no removal path other than ClearChangedBlocks); MultiplayerChunkAuthoritySync.cs:676-703 SendLateJoinSnapshot (`int writerSize = SnapshotHeaderBytes + changedBlocks.Count * SnapshotBlockBytes;` single buffer/message); WorldSaveService.cs:452 `foreach (BlockChange change in world.GetChangedBlocks())`.
- **RecommendedFix:** Reconcile deltas against the generated baseline: either (a) have the world retain (or lazily recompute per-column) the generated block value so SetBlock can delete the entry when a cell returns to baseline, or (b) periodically compact: during save (which already regenerates nothing but knows the seed) or on a background thread, regenerate per-region baseline and drop no-op deltas. Also consider chunking the late-join snapshot into multiple messages to bound per-frame serialization.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["performance"]
- **MergedCount:** 1

## 111. World load path runs the full 1024-chunk RebuildAll twice (and host-start can too)

- **Severity:** Medium
- **Impact:** LoadSave first calls InitializeGeneratedWorld, whose ConfigureWorldRuntime → Renderer.Configure already executes a complete RebuildAll (all chunk meshes + full collider flush), then after applying saved deltas calls Renderer.RebuildAll() again — doubling the most expensive part of the already multi-second load hitch. MultiplayerWorldPersistence.RestoreSavedWorldBeforeHostStart has the same shape: TryResolveWorldManager may InitializeDefaultWorld (RebuildAll #1) and line 178 runs RebuildAll #2. The first full build is wasted work; only chunks touched by saved deltas need a second pass, and the delta application already marks exactly those chunks dirty via the rebuild queue.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs", "Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs"]
- **Evidence:** BlockiverseWorldSessionController.cs LoadSave: line ~609 `worldManager.InitializeGeneratedWorld(generated, chunkAuthoritySync)` (triggers VoxelWorldRenderer.Configure → RebuildAll at VoxelWorldRenderer.cs:72), then line ~629 `worldManager.Renderer?.RebuildAll();` after result.ApplyTo. MultiplayerWorldPersistence.cs:178 `worldManager.Renderer?.RebuildAll();` after a load whose world may have just been initialized (line 377 InitializeDefaultWorld inside TryResolveWorldManager).
- **RecommendedFix:** After applying saved block deltas, call Renderer.RebuildDirty() instead of RebuildAll() — block-delta application already marks affected chunks via ChunkRebuildQueue (the late-join FinalizeSnapshot path at MultiplayerChunkAuthoritySync.cs:495-507 does exactly this, with a comment explaining why). Alternatively defer the renderer's initial RebuildAll until after deltas are applied so only one full pass runs.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["performance"]
- **MergedCount:** 1

## 112. World-name sanitizer flattens every non-ASCII character to underscore in the player-visible name, not just the directory name

- **Severity:** Medium
- **Impact:** Any world name containing characters outside [a-zA-Z0-9 _-.()] is silently corrupted in the UI and the save manifest: "Café" becomes "Caf_", a Japanese or Cyrillic name becomes a row of underscores, and multiple such worlds collide into "___", "___ (2)", "___ (3)". This affects non-English players today (the Quest system keyboard lets them type these names) and destroys user-entered data with no warning.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs"]
- **Evidence:** BlockiverseWorldSessionController.cs:828–848: SanitizeFileName replaces any char failing IsSafeFileNameChar (ASCII letters/digits/space/_/-/./()) with '_'. AllocateSavePath (lines 804–818) builds candidateName from the sanitized baseName and returns it as the worldName. CreateNewWorld line 278 then assigns `(currentSavePath, currentWorldName) = AllocateSavePath(config.Name.Trim())` — so the sanitized string becomes the manifest WorldName shown in Load World, World Details, and rename. The comment at 824–827 explains the sanitizer exists because Path.GetInvalidFileNameChars() is empty on Android, but the allowlist is far stricter than filesystem safety requires.
- **RecommendedFix:** Decouple display name from directory name: store the player's original (trimmed) name in the manifest and UI, and derive the directory from the sanitized name plus a uniqueness suffix or short hash (the manifest already carries the authoritative name; SummaryKey already keys on name+seed+createdUtc). At minimum, widen the allowlist to permit all letters/digits via char.IsLetterOrDigit (which is Unicode-aware) while still excluding path separators and reserved characters.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["localization"]
- **MergedCount:** 1

## 113. World-preset construction/dispatch logic implemented four times across three assemblies with magic preset strings

- **Severity:** Medium
- **Impact:** The 'which preset class + which settings for this preset id' decision exists in four places: BlockiverseWorldSessionController.GenerateWorld (new world), BlockiverseWorldSessionController.RegenerateBaseWorld (load), MultiplayerChunkAuthoritySync.GenerateSnapshotWorld (late join), and CreativeWorldManager.CreateDefaultGeneratedWorld (boot). The string ids "flat_builder"/"void_builder"/"survival_terrain" are raw literals at 10+ sites with no shared constants, and the builder ground-height rules differ subtly between the copies (GenerateWorld uses FlatBuilderGroundHeight raw, RegenerateBaseWorld clamps with Math.Min(…, data.Height - 2)). A new preset or a tuning change must be replicated in all four sites or hosts, clients, loads, and new worlds silently generate different baselines — fatal for a delta-only save format and seed-regenerated late-join sync. It also forces the regeneration logic to live in the UI assembly, which is why MultiplayerWorldPersistence cannot regenerate a world itself and instead fails hosting on metadata mismatch (MultiplayerWorldPersistence.cs:142–151).
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs", "Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs"]
- **Evidence:** BlockiverseWorldSessionController.cs:301–324 and 653–680 (two near-identical switches in the same file); MultiplayerChunkAuthoritySync.cs:416–452 (third copy, switching on the enum); CreativeWorldManager.cs:719–726 (fourth, survival-only). Preset string literals: NewWorldConfig.cs:15, BlockiverseWorldSessionController.cs:37/305/312/657/666, MultiplayerWorldPersistence.cs:19, WorldSaveService.cs:214/219/252/428.
- **RecommendedFix:** Add a WorldPresetCatalog in WorldGen (or Gameplay): canonical preset-id constants plus a single Generate(presetId-or-enum, registry, settings) factory returning GeneratedCreativeWorld-shaped data. Route all four call sites through it and delete the duplicated switches.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 114. Worldroot is breakable and collectible, contradicting its 'unbreakable bottom crust' role

- **Severity:** Medium
- **Impact:** Ruleset §2 defines worldroot as hardness ∞, no drops, existing to prevent falling out of the world. The implementation gives it finite hardness 6.0, tier 3, and a self-drop — any rosycopper+ Delver mines through the world floor in ~2.7s, opening holes into the void beneath the bounded world (mitigated only by the safety-floor catcher) and adding a non-canonical 'worldroot' building block to inventories.
- **Files:** ["Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs", "Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs", "Assets/Blockiverse/Scripts/Survival/BlockHarvestRules.cs", "docs/rulesets/voxel_survival_ruleset.md"]
- **Evidence:** VoxelTypes.cs:273 registers Worldroot with hardnessClass VeryHard (= 6.0f per HardnessFromClass line 115) and harvestTierMin 3 — not infinite. ItemRegistry.cs:44 registers a collectible Worldroot item; BlockHarvestRules.cs:114 registers a Delver harvest rule for it. Ruleset §2: 'Worldroot | worldroot | Unbreakable bottom crust. Prevents falling out of the world. | ∞ | — | None'.
- **RecommendedFix:** Give worldroot infinite hardness (MiningFormula.MineTicks already returns int.MaxValue for IsPositiveInfinity) and remove its item mapping/harvest rule; keep it in the creative catalog only if desired.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 115. 'Infinite' world size silently creates a 256x256 bounded world

- **Severity:** Low
- **Impact:** The New World menu offers small/medium/large/infinite, but 'infinite' falls through to the same 256×256 bounded world as 'large'. Players selecting infinite hit a hard invisible world edge ~96 blocks from spawn in any direction with no warning; the option label is dishonest and will generate store-review complaints.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs"]
- **Evidence:** NewWorldConfig.cs:14 WorldSizeOptions includes "infinite"; BlockiverseWorldSessionController.SizeFor (lines 328-335): 'case "large": case "infinite": return (256, 256);'.
- **RecommendedFix:** Remove 'infinite' from WorldSizeOptions until streaming/unbounded worlds exist, or relabel it (e.g. 'huge') with the actual dimensions shown in the selector.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 116. Avatar pose and Meta-avatar streams use only reliable delivery with no remote interpolation; streams are echoed back to their sender and unbounded to 64 KB

- **Severity:** Low
- **Impact:** Under Wi-Fi packet loss, reliable-sequenced delivery head-of-line-blocks pose updates: remote avatars freeze then snap forward (no interpolation exists — ApplyPose sets transforms directly each LateUpdate), which is noticeably worse in VR than dropped unreliable updates would be. The Meta-avatar relay additionally ClientRpc-broadcasts each stream packet to every client including the original sender (filtered only after delivery), wasting up to 64 KB x 15 Hz of return bandwidth per player, and the relay forwards arbitrary client-supplied bytes into the native Avatar SDK parser on every peer.
- **Files:** ["Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkAvatarRig.cs", "Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamRelay.cs", "Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamMessage.cs", "Assets/Blockiverse/Scripts/MetaAvatars/BlockiverseMetaAvatarPresenter.cs", "Assets/Blockiverse/Scripts/MetaAvatars/MetaHorizonAvatarProvider.cs"]
- **Evidence:** Pose sync is a NetworkVariable<AvatarPose> (BlockiverseNetworkAvatarRig.cs:23-26, default reliable NetworkVariable delta path) written at up to 30 Hz (poseSendRateHz=30, line 30) and applied raw with no smoothing (ApplyPose lines 185-192). MetaAvatarStreamRelay.SubmitAvatarStreamServerRpc/ReceiveAvatarStreamClientRpc (lines 65-81) are default-delivery (reliable) RPCs; the ClientRpc goes to all clients and non-owners filter after receipt (line 76). MetaAvatarStreamMessage.MaxPayloadBytes = 64*1024 (line 9); ApplyStreamData passes bytes to OvrAvatarEntity.ApplyStreamData with no validation (MetaHorizonAvatarProvider.cs:108-120).
- **RecommendedFix:** Send pose/stream traffic unreliably (ClientRpcParams/ServerRpc Delivery = Unreliable for streams; consider NetworkTransform-style interpolation or a small lerp buffer for the fallback rig), target the ClientRpc to all-but-sender via ClientRpcParams, and clamp relayed stream payloads to the realistic Meta stream LOD size (a few KB) rather than 64 KB.
- **Confidence:** Medium
- **NeedsManualVerification:** true
- **Sources:** ["lan-multiplayer", "security"]
- **MergedCount:** 2

## 117. Berry/crop ripeness and ore identification may rely on hue contrasts weak for red-green colorblindness; no colorblind options

- **Severity:** Low
- **Impact:** The mature berrybush is signaled by red berry accents (203,56,91) on a green bush (50,112,54) — a classic deuteranopia/protanopia confusion pair — and several ores differ mainly by accent hue. There are no colorblind palettes or filters. Mitigating factors: growth stages use distinct atlas tiles (shape/density can differ, pixels not verified here), and harvesting feedback is text/audio based.
- **Files:** ["scripts/art/generate-art-assets.py", "Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs"]
- **Evidence:** generate-art-assets.py line 60: `("berrybush", 41, (50, 112, 54), (203, 56, 91), "berries", 283)` — red accent on green base; tool/ore entries (raw_rosycopper (241,135,70) vs sunmetal_fleck (255,196,74)) also differentiate by warm hues. No colorblind setting exists anywhere in Scripts/.
- **RecommendedFix:** When extending the art generator for the stage tiles (see atlas finding), encode growth stage primarily by silhouette/height/density rather than accent color, and lighten mature-stage accents for luminance contrast. A full colorblind filter is likely unnecessary if shape coding is done; document the approach in the art script.
- **Confidence:** Medium
- **NeedsManualVerification:** true
- **Sources:** ["accessibility"]
- **MergedCount:** 1

## 118. Block/item display names are constructor literals inside engine-free assemblies, and ItemRegistry keys a lookup on the English display name

- **Severity:** Low
- **Impact:** The ~100+ English names ("Meadow Turf", "Fired Brick Block", …) live in Voxel and Survival, which by project invariant cannot reference UnityEngine — and therefore cannot use the Unity Localization package directly. Worse, ItemRegistry maintains definitionsByName keyed by the display name, so naively replacing Name with translated text would change registry semantics (duplicate detection) and any future name-based lookups. Localization must instead happen at the UI layer keyed by canonical id, which no current code anticipates.
- **Files:** ["Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs", "Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs", "Assets/Blockiverse/Scripts/Survival/ItemDefinition.cs"]
- **Evidence:** VoxelTypes.cs:254 etc.: registry.Register(new BlockDefinition(MeadowTurf, "meadow_turf", "Meadow Turf", …)) — English name as the third positional literal for every block. ItemRegistry.cs:30–69: every item registered with an English name literal; ItemRegistry.cs:11/227/234: readonly Dictionary<string, ItemDefinition> definitionsByName = new(StringComparer.OrdinalIgnoreCase) keyed by definition.Name. UI reads definition.Name directly for display (SurvivalCraftingPanel.cs:327, BlockiverseCatalogBrowserPanel.cs:167, CreativeHotbar.cs:165) and BlockiverseCatalogBrowserPanel.cs:187 string-matches search input against Name with OrdinalIgnoreCase (which does not case-fold most non-ASCII).
- **RecommendedFix:** Treat in-registry Name as the English source/development name and add a UI-side display-name resolver keyed by CanonicalId/ItemId (a plain dictionary loaded from the string table works within the engine-free constraint if placed behind an interface). Switch catalog search to compare against the resolved display name with CultureInfo-aware IndexOf/CompareInfo once names can be non-ASCII. Keep definitionsByName keyed on the immutable English name or drop it in favor of canonical-id keys.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["localization"]
- **MergedCount:** 1

## 119. BlockiverseInputRig recreates InputActionReference ScriptableObjects and reader objects on every wiring pass without destroying the old ones

- **Severity:** Low
- **Impact:** Each of the (at least) three startup passes (Awake, Start, OnEnable all call RepairRuntimeTracking) and every subsequent enable/reconfigure allocates ~10 new InputActionReference ScriptableObject instances plus XRInputValueReader/XRInputButtonReader wrappers; the superseded ones are never destroyed. In the single-scene app they accumulate for the process lifetime — small but unbounded across repeated disable/enable cycles, and a latent confusion source when debugging input.
- **Files:** ["Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs"]
- **Evidence:** CreateVector2ActionReader/CreateButtonActionReader (BlockiverseInputRig.cs lines 833-864) call InputActionReference.Create(action) — a ScriptableObject.CreateInstance — and are invoked from ConfigureXriProviderInputs (lines 577-618) and EnsureRayInteractorInputs (lines 624-661); both run inside RepairRuntimeTracking, which is called from Awake (line 259), Start (line 266), and OnEnable (line 271). The class's own comment (lines 78-80) acknowledges the readers 'must only be rebuilt when a setting actually changes', yet the rebuild happens on every lifecycle pass regardless.
- **RecommendedFix:** Cache the created InputActionReference per action (e.g. Dictionary<InputAction, InputActionReference>) and reuse it across passes, or guard ConfigureXriProviderInputs/EnsureRayInteractorInputs with an 'alreadyWiredForThisAsset' check (mirroring RefreshCachedActions' cachedActionAsset gate) so the readers are built once per InputActionAsset instance.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["unity-csharp"]
- **MergedCount:** 1

## 120. Bootstrapper invokes a private Netcode settings method via reflection with a silently-swallowed failure

- **Severity:** Low
- **Impact:** DisableNetcodeDefaultNetworkPrefabs reflects NetcodeForGameObjectsProjectSettings.SaveSettings (private) and calls it through saveSettings?.Invoke — if a Netcode package update renames or removes the method, the null-conditional makes the persistence step a silent no-op and GenerateDefaultNetworkPrefabs quietly reverts on the next editor session, producing confusing prefab-list diffs rather than an error.
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** BlockiverseProjectBootstrapper.cs:244–254: GetMethod("SaveSettings", BindingFlags.Instance | BindingFlags.NonPublic) followed by `saveSettings?.Invoke(settings, null);` with no else/log branch.
- **RecommendedFix:** Log a warning (or throw, since this is editor tooling) when the MethodInfo is null so a Netcode upgrade surfaces immediately; check whether the current Netcode 2.x exposes a public save path (e.g. SaveSettings became public or settings.Save()) and use it.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 121. ChunkMeshBuilder allocates four new fluid lists on every chunk rebuild while the solid lists are pooled

- **Severity:** Low
- **Impact:** Build() pools the solid vertex/triangle/uv/color lists ([ThreadStatic], explicitly 'so chunk rebuilds do not allocate every call (GC hitches on Quest)') but allocates `new List<Vector3>() / List<int>() / List<Vector2>() / List<Color>()` for the fluid mesh on every call — including for the overwhelming majority of chunks that contain no fluid. During rebuild storms (63 chunks per surface edit; continuous 4 Hz fluid repaints; 1024 chunks at RebuildAll) this generates thousands of list allocations plus growth reallocation for fluid-bearing chunks, adding avoidable GC pressure on a platform where the same file's comments treat GC hitches as a design constraint.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs"]
- **Evidence:** ChunkMeshBuilder.cs:40-43 pooled [ThreadStatic] solid lists with the GC-hitch comment; lines 92-95: `var fluidVertices = new List<Vector3>(); var fluidTriangles = new List<int>(); var fluidUvs = new List<Vector2>(); var fluidColors = new List<Color>();` allocated unconditionally per Build call.
- **RecommendedFix:** Pool the four fluid lists the same way as the solid ones ([ThreadStatic], Clear() per call). The aliasing contract documented for ChunkMeshData already requires consumers to copy data out before the next Build, so pooling the fluid lists has identical semantics — VoxelWorldRenderer.RebuildChunk consumes both before the next Build.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["performance", "unity-csharp"]
- **MergedCount:** 2

## 122. Comfort panel has no Close button and is not router-managed

- **Severity:** Low
- **Impact:** Opening Settings → Comfort shows the panel via canvas.enabled only (no recenter, no router entry). The only dismissal is the Menu button, whose generic escape-hatch also pops the underlying Settings screen — so closing the comfort panel kicks the player out of Settings, and because Show() bypasses the presenter, the panel appears wherever it was last placed rather than recentering to the player's head.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseComfortMenu.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** BlockiverseMenuController.HandleAction lines 483–485: `case MenuActions.SettingsOpenComfort: comfortMenu?.Show();` with no router push and no presenter recenter (BlockiverseComfortMenu.Show, lines 71–78, just enables the canvas). EnsureXrRigComfortMenu (lines 2115–2188) generates title, toggles, sliders, and a Height Reset button but no Close button. OnMenuPressed (lines 133–138) pops the active screen as a generic escape hatch.
- **RecommendedFix:** Add a Close button to the comfort panel in EnsureXrRigComfortMenu wired to hide it (or, better, register the comfort panel as a router screen with `settings.open_comfort` pushing it like Audio/Controls), and call the panel's presenter.Show() instead of canvas-enable so it recenters to the head when opened.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["accessibility"]
- **MergedCount:** 1

## 123. Container auto-loot during harvest can consume the capacity the harvest preview validated, silently destroying the crate drop

- **Severity:** Low
- **Impact:** ProcessHostHarvest loots the container's contents into the harvester's inventory after TryPreviewHarvest validated capacity for the block drop but before the drop is added; the final `inventory.TryAddAll(drop)` return value is ignored. With a nearly full inventory, the crate contents can fill the last slots and the storage-crate item itself is then silently lost even though the block was already removed from the world.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs"]
- **Evidence:** MultiplayerSurvivalSync.cs:1160-1164 preview (capacity check) → lines 1184-1191 TryLootContainerInto(position, inventory) → line 1221-1222 `ItemStack drop = ...RollHarvestDrop(...); inventory.TryAddAll(drop);` with the bool result discarded (contrast ResourceHarvestService.ApplyHarvestToInventory lines 150-159, which treats the same failure as InventoryFull and aborts).
- **RecommendedFix:** Re-check capacity for the drop after looting (and before committing the mutation), or add the drop first and loot afterwards; at minimum log and surface InventoryFull when TryAddAll fails on the authoritative path.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 124. Controller Undo binding deliberately removed but dead undo plumbing remains; per-block undo/redo unreachable in VR

- **Severity:** Low
- **Impact:** BlockiverseInputRig still caches and polls an 'Undo' action and exposes UndoPressed, and BlockiverseCreativeInputBridge subscribes TryUndo to it — but the bootstrapper explicitly removes the Undo action from the Gameplay map, so the event can never fire. CreativeInteractionController.UndoLast is therefore unreachable from any input, and RedoLast has no caller at all outside tests. Players have no way to undo a single misplaced/mis-broken block (the Creative Tools panel's Undo/Redo buttons drive the separate WorldEditService region-edit stack, which does not contain single-block edits), despite the creative ruleset listing undo/redo as core verbs.
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeInteractionController.cs"]
- **Evidence:** Bootstrapper EnsureInputActionSchema line 573: RemoveAction(gameplayMap, BlockiverseInputActionNames.Undo); the committed BlockiverseInputActions.inputactions Gameplay map contains only Menu/Jump/Block Editing Toggle. BlockiverseInputRig.RefreshCachedActions line 309 still looks up Undo (always null) and UpdateCreativeBindings lines 336-337 poll it. BlockiverseCreativeInputBridge.Bind lines 154/159 subscribe TryUndo to UndoPressed. Grep UndoLast/RedoLast: only the bridge calls UndoLast; RedoLast has zero non-test callers. BlockiverseCreativeToolsPanel.UndoEdit (line 220) calls editService.Undo — a different stack (WorldEditService region edits).
- **RecommendedFix:** Either re-bind Undo to an available input (e.g. left controller B/Y button, currently unused) and add Redo, or delete the dead undoPressed/cachedUndoAction plumbing from BlockiverseInputRig and the bridge. Consider adding Undo/Redo buttons for single-block edits to the Creative Tools panel by routing them to CreativeInteractionController.UndoLast/RedoLast.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["vr-interaction"]
- **MergedCount:** 1

## 125. Creative mode lacks flight despite the creative ruleset defaulting it on

- **Severity:** Low
- **Impact:** voxel_creative_ruleset §1 lists 'Movement | Flight enabled by default' as a core creative-mode difference, and §2 models canFly per player. The implementation explicitly disables fly on the continuous move provider and offers no vertical locomotion beyond teleporting onto existing geometry, so creative builders cannot work at height without scaffolding — a major friction for the build-anything fantasy the mode promises. (A VR-comfort-conscious implementation may be intentional, but no comfort-gated alternative exists either.)
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs", "docs/rulesets/voxel_creative_ruleset.md"]
- **Evidence:** BlockiverseProjectBootstrapper.cs:3811 'continuousMove.enableFly = false'; BlockiverseInputRig.cs:534 same; repo grep for fly/flight finds no other locomotion path. Creative ruleset §1 table: 'Movement | Grounded exploration | Flight enabled by default'; §2 'canFly: boolean'.
- **RecommendedFix:** Add a comfort-gated fly mode for creative (smooth vertical move on the off-hand stick with vignette, or a teleport-to-air 'platform' tool), defaulting per the creative ruleset with a comfort setting to disable.
- **Confidence:** High
- **NeedsManualVerification:** true
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 126. Cross-class temporal couplings documented only in comments (death-respawn-before-save, station-tick clock split)

- **Severity:** Low
- **Impact:** Two fragile orderings are load-bearing but enforced by nothing: (1) saving on DeathReturnToTitle is only correct because BlockiverseMenuController.HandleAction calls vitalsRuntime.Respawn() on the statement line before raising ActionRequested — the session controller's comment explicitly depends on that statement order in another assembly's switch case; reordering compiles and silently saves dead-player state. (2) Simulation time is split across two clocks: vegetation/farming/fluids advance on WorldTimeClock ticks while smelting stations advance on raw Time.deltaTime in two places (host tick and panel extrapolation), so any future pause/timescale behavior must be fixed in both systems.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs"]
- **Evidence:** BlockiverseWorldSessionController.cs:154–158 ('Order dependency: BlockiverseMenuController respawns the vitals runtime *before* raising DeathReturnToTitle…') paired with BlockiverseMenuController.cs:412–416. Station real-time ticking: MultiplayerSurvivalSync.cs:447–462 (Update, stationTickRemainder += Time.deltaTime * SmeltingModel.TicksPerSecond) and the duplicate extrapolation in BlockiverseStationPanel.cs:95–124.
- **RecommendedFix:** (1) Make the save handler itself respawn-safe: have SaveCurrentWorld ask vitalsRuntime for post-respawn state (or have the death flow emit a dedicated 'SaveAfterRespawn' action) instead of relying on statement order in another class. (2) Drive station ticking from WorldTimeClock.Ticked like every other simulation, removing the second time base.
- **Confidence:** Medium
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 127. Dates and numbers are displayed without regional formatting (invariant dates, English decimal conventions); no current parsing risk

- **Severity:** Low
- **Impact:** World Details shows Created/Last Played as invariant "yyyy-MM-dd" regardless of the player's region — safe but not locale-appropriate (US users expect MM/DD/YYYY, Germans DD.MM.YYYY). The dev performance overlay formats numbers with the current culture including :n0 group separators, fine for a dev tool. Importantly, the inverse problem is absent: no float.Parse/ToString crosses the save or wire boundary as locale-formatted text, so there is no German-locale decimal-comma corruption path. The seed field's ulong.TryParse (NewWorldConfig.cs:86) uses current culture implicitly, but integer digit parsing is locale-stable in practice; non-numeric input deterministically falls through to the FNV-1a hash.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldDetailsPanel.cs", "Assets/Blockiverse/Scripts/Gameplay/PerformanceStatsOverlay.cs", "Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs", "Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs"]
- **Evidence:** BlockiverseWorldDetailsPanel.cs:61: utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) for display. PerformanceStatsOverlay.cs:86–89: $"…{Sampler.AverageFps:0.0}…{stats.TriangleCount:n0}…" with current culture. NewWorldConfig.cs:86: ulong.TryParse(trimmed, out ulong numeric) without NumberStyles/IFormatProvider. Counter-evidence of safety: WorldSaveService.cs:238/911 uses DateTime.UtcNow.ToString("o") (round-trip, invariant); all JSON goes through UnityEngine.JsonUtility (lines 374, 1190–1192) which is culture-invariant; BlockiverseWorldSessionController.cs:791–800 parses timestamps with CultureInfo.InvariantCulture; project-wide grep finds no float.Parse/double.Parse anywhere.
- **RecommendedFix:** For localized display, format save dates with the user's culture (e.g. utc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)) while keeping invariant "o" for storage — the split is already correctly in place. Optionally pass NumberStyles.None + CultureInfo.InvariantCulture to the seed TryParse to make cross-device seed-text determinism explicit.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["localization"]
- **MergedCount:** 1

## 128. Definitely-dead public methods: ConfigureWorld, InitializeAuthoritativeWorldSnapshot, TrackChangedBlock, ResetToFullHealth, CanStackWith, SelectPrevious, ApplyRemoteStreamForTests

- **Severity:** Low
- **Impact:** Seven public methods have zero callers anywhere (source, tests, editor tooling, UnityEvent YAML bindings) and are safe to delete; several also duplicate or contradict the live path, which misleads maintainers: MultiplayerChunkAuthoritySync.ConfigureWorld duplicates a subset of the live Configure(...); CreativeWorldManager.InitializeAuthoritativeWorldSnapshot looks like a superseded late-join world-install API; VoxelWorld.TrackChangedBlock duplicates the inline delta tracking inside SetBlock (changedBlocks[position] at line 67); PlayerVitals.ResetToFullHealth would respawn at BlockPosition(0,0,0) if anyone ever called it; ItemStack.CanStackWith is an unused query; CreativeHotbar.SelectPrevious has no caller (and SelectNext is test-only — runtime hotbar selection happens exclusively through SelectBlock from the catalog browser/pick-block); MetaAvatarStreamRelay.ApplyRemoteStreamForTests is a 'ForTests' seam that not even tests use. Classification: definitely unused.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/Voxel/VoxelWorld.cs", "Assets/Blockiverse/Scripts/SurvivalHealth/PlayerVitals.cs", "Assets/Blockiverse/Scripts/Survival/ItemStack.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeHotbar.cs", "Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamRelay.cs"]
- **Evidence:** Repo-wide `grep -rnw` for each name returns only the declaration: MultiplayerChunkAuthoritySync.cs:89 ConfigureWorld; CreativeWorldManager.cs:370 InitializeAuthoritativeWorldSnapshot; VoxelWorld.cs:109 TrackChangedBlock (inline tracking at VoxelWorld.cs:67 is the live path); PlayerVitals.cs:93 ResetToFullHealth (`RespawnAt(new BlockPosition(0, 0, 0))`); ItemStack.cs:34 CanStackWith; CreativeHotbar.cs:118 SelectPrevious (external consumers use only hotbar.SelectBlock/SelectedBlockId/SelectionChanged; no m_MethodName YAML binding to SelectNext/SelectPrevious exists); MetaAvatarStreamRelay.cs:59 ApplyRemoteStreamForTests.
- **RecommendedFix:** Delete all seven methods. If hotbar next/previous cycling is intended as a future controller binding (no input action exists for it in BlockiverseInputActionNames), keep SelectNext/SelectPrevious but track the missing input wiring as a feature task instead of leaving silent dead API.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["dead-code"]
- **MergedCount:** 1

## 129. Eleven orphan item icons have no registered item id (legacy ids and unimplemented ruleset foods)

- **Severity:** Low
- **Impact:** Assets/Blockiverse/Art/Textures/Items contains icons for ids that no longer (or do not yet) exist in ItemRegistry: 8 legacy block-named icons superseded by canonical drop items (berrybush, grain_stalk, reedgrass, niterstone, paletin_thread, staropal_geode, sunmetal_fleck, umbralite_node — drops are berry_cluster, grain_bundle, reed_fiber, etc.) and 3 food icons (berry_mash, flatbread, trail_ration) whose recipes exist in docs/rulesets/voxel_survival_ruleset.md (§ lines 637-638, 856-857) but were never registered as items. They are loaded into the icon library as dead entries and signal either missing game content (the Prep Board foods) or stale generator output that should be pruned.
- **Files:** ["Assets/Blockiverse/Art/Textures/Items/", "Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs", "docs/rulesets/voxel_survival_ruleset.md", "scripts/art/generate-art-assets.py"]
- **Evidence:** Programmatic diff of 131 registered ids (ItemId.cs constants + RegisterToolTier compositions) vs 142 icon filenames: icons with no registered item = [berry_mash, berrybush, flatbread, grain_stalk, niterstone, paletin_thread, reedgrass, staropal_geode, sunmetal_fleck, trail_ration, umbralite_node]; grep for BerryMash/Flatbread/TrailRation in Assets/Blockiverse/Scripts returns nothing; the legacy 8 are still in the stale generator's ITEMS list so regeneration recreates them.
- **RecommendedFix:** Either register the three ruleset foods (berry_mash, flatbread, trail_ration — their Prep Board recipes are canon) so the icons become live, and delete the 8 legacy block-named icons (also removing them from the generator's ITEMS list); or, if the foods are deferred, delete all 11 icons and regenerate them when the items land. Add the same registry-coverage test recommended above to flag future orphans.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["asset-integration"]
- **MergedCount:** 1

## 130. Experimental Rules toggle from the menus ruleset is unimplemented — NewWorldConfig.ToggleExperimentalRules/ExperimentalRulesEnabled are test-only vestiges

- **Severity:** Low
- **Impact:** docs/rulesets/voxel_survival_menus.md specifies a New World 'Experimental Rules' toggle (action id new_world.toggle_experimental), but MenuActions contains no such constant, BlockiverseNewWorldPanel exposes no control for it, no scene/prefab UnityEvent binds it, and ExperimentalRulesEnabled is never read by world creation. The model-level method exists solely so NewWorldConfigEditModeTests can flip it. Classification: only used in tests (unwired planned feature).
- **Files:** ["Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs", "Assets/Blockiverse/Scripts/UI/MenuActions.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseNewWorldPanel.cs", "docs/rulesets/voxel_survival_menus.md", "Assets/Blockiverse/Tests/EditMode/NewWorldConfigEditModeTests.cs"]
- **Evidence:** NewWorldConfig.cs:34 `public bool ExperimentalRulesEnabled { get; private set; }` and :60 `public void ToggleExperimentalRules() => …` — repo-wide grep finds only NewWorldConfigEditModeTests.cs:91/93 as callers and no reader of ExperimentalRulesEnabled. grep 'toggle_experimental' in Assets returns nothing; the ruleset names it at docs/rulesets/voxel_survival_menus.md:349.
- **RecommendedFix:** Either implement the toggle (MenuActions constant, panel control wired by the bootstrapper, consume the flag in world creation) to match the ruleset, or remove the property/method/test and annotate the ruleset row as not-yet-implemented so the canonical design and code agree.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["dead-code"]
- **MergedCount:** 1

## 131. Fixed-size world-space panels with Truncate overflow and no auto-sizing — zero tolerance for translated-text expansion

- **Severity:** Low
- **Impact:** Every generated label has a hard pixel size and silently truncates overflowing text; buttons are fixed 220×54 (down to 150×48 / 164×54) with 26pt labels. German/French/Russian translations typically run 20–35% longer than English, so localized labels like "Switch Survival/Creative" or "Respawn at World Spawn" would clip with no visual indication. Layout is fully code-defined in the bootstrapper, so fixing it later means revisiting all 115 call sites.
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** EnsureLabel (lines 3573–3604): labelRect.sizeDelta = size (fixed), tmp.fontSize = fontSize (fixed), tmp.overflowMode = TextOverflowModes.Truncate, no enableAutoSizing. EnsureButtonControl (3425–3481): fixed size with default 220×54 and label insets of 8/4 px; call sites use even smaller fixed sizes (3987–3991: Host/Join/Stop at 164×54; 4521–4525: 150×48). No LayoutGroup/ContentSizeFitter usage for these panels.
- **RecommendedFix:** When (or before) localizing, enable TMP auto-sizing with sane min/max (e.g. fontSizeMin 18) on button and row labels in EnsureLabel/EnsureButtonControl — a two-line change that propagates to every generated panel on the next bootstrap run — and spot-check the worst offenders (pause menu, controls popup) with pseudo-localized 30%-longer strings.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["localization"]
- **MergedCount:** 1

## 132. Gameplay layer reaches the XR rig via GameObject.Find string lookups to work around the assembly layering

- **Severity:** Low
- **Impact:** VR sits above Gameplay in the layering, so Gameplay code cannot reference rig types — instead CreativeWorldManager.PositionRigAtSpawn/PositionRig and SurvivalVitalsRuntime.BuildPlayerSaveState locate and mutate the player rig with GameObject.Find("BlockiverseXRRig"). The dependency on the rig is real but invisible to the type system: renaming the rig in the bootstrapper compiles fine and silently breaks spawn positioning, respawn, and player-state saves (they all no-op when Find returns null).
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs", "Assets/Blockiverse/Scripts/Core/BlockiverseProject.cs"]
- **Evidence:** CreativeWorldManager.cs:736–753 (two static methods doing GameObject.Find(BlockiverseProject.XrRigRootName), with a comment explaining they are public 'because no InternalsVisibleTo covers the UI assembly'); SurvivalVitalsRuntime.cs:214 (same Find in BuildPlayerSaveState, returns null silently when absent).
- **RecommendedFix:** Define a tiny IPlayerRigLocator (or a PlayerRigAnchor MonoBehaviour) in Gameplay that the VR rig registers itself into at OnEnable; Gameplay code then asks the registry instead of string-searching the hierarchy. Keeps the layering and makes the dependency explicit and testable.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 133. Host cannot stop the session while the shutdown save keeps failing (deliberate abort with no override)

- **Severity:** Low
- **Impact:** If the multiplayer shutdown save persistently fails (disk full, IO error on Quest), StopSession aborts every time and the host remains stuck hosting; the only escape is quitting the app (which force-kills the session without the orderly client notification). Clients stay connected to a host that is trying to leave.
- **Files:** ["Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkSession.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs"]
- **Evidence:** BlockiverseNetworkSession.StopSession (lines 120-128): when HostShutdownPreparing (wired to MultiplayerWorldPersistence.SaveWorldBeforeHostShutdown, line 447) returns false, the stop is aborted (LastStopRequestSucceeded=false, state restored to Hosting). The PlayMode test HostShutdownSaveFailureAbortsShutdownAndKeepsClientsConnected codifies this. There is no 'stop without saving' path.
- **RecommendedFix:** After N failed stop attempts (or via an explicit confirm modal: 'Saving failed — stop anyway and lose progress since the last autosave?'), allow shutdown to proceed without the save. Surface the failure reason prominently in the LAN menu.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["lan-multiplayer"]
- **MergedCount:** 1

## 134. Implemented menus deviate from voxel_survival_menus.md in several documented capabilities

- **Severity:** Low
- **Impact:** Users and store reviewers reading the ruleset/wiki will expect features the menus do not offer: no Multiplayer entry on the pause menu (§3/§5 list lan_multiplayer from Pause; in-game multiplayer access requires returning to title), no Credits screen (§6.2), New World lacks the Random Seed button and Experimental Rules toggle (§6.3 — NewWorldConfig.ToggleExperimentalRules exists but has no UI), Load World lacks the per-save Delete button (§6.4; delete lives only in Details), the Comfort section lacks move speed and standing eye height (§6.19), and the LAN menu has no port field or distinct Reconnect control (§6.18).
- **Files:** ["Assets/Blockiverse/Scripts/UI/MenuActions.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs", "docs/rulesets/voxel_survival_menus.md"]
- **Evidence:** MenuActions.Pause (MenuActions.cs:97-106) has no multiplayer entry vs §5 'pause_menu | Multiplayer | Always | lan_multiplayer'. EnsureNewWorldMenuPanel builds only name/seed inputs + 5 cycle rows + Create/Cancel (bootstrapper 4168-4245) — no randomize button (NewWorldConfig.RandomizeSeed only called from ResetForNewWorld, BlockiverseNewWorldPanel.cs:73-82) and no experimental toggle (NewWorldConfig.ToggleExperimentalRules, line 60, has zero UI callers). EnsureLoadWorldMenuPanel footer is Load/Details/Cancel (4339-4344) vs §6.4 Play/Details/Delete+Sort+Search. Comfort builder (2068-2215) has glide/teleport/smooth-turn/snap-degrees/vignette/height-reset only vs §6.19's 'move speed … standing eye height'. LAN panel has an address input but no port control (3978-3984) and no Reconnect button (only Host/Join/Stop/Close).
- **RecommendedFix:** Either implement the missing controls (pause-menu Multiplayer entry is the highest-value: add a PauseMultiplayer action pushing LanMultiplayerScreen) or amend docs/rulesets/voxel_survival_menus.md and the wiki to match shipped behavior, per the project's documentation-currency rule.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["ui-menu-flow"]
- **MergedCount:** 1

## 135. Meta avatar streaming allocates fresh byte[] payloads at 15 Hz per peer

- **Severity:** Low
- **Impact:** Every avatar stream record allocates a new byte[] (MetaHorizonAvatarProvider copies the SDK auto-buffer into `streamData = new byte[byteCount]`), and every receive allocates another (MetaAvatarStreamMessage deserialization `new byte[length]`). At 15 Hz per remote peer this is continuous small-array garbage on host and clients for the lifetime of a co-op session — modest (likely a few KB/s per peer) but永-running GC pressure that pooled buffers would eliminate.
- **Files:** ["Assets/Blockiverse/Scripts/MetaAvatars/MetaHorizonAvatarProvider.cs", "Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamMessage.cs", "Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamRelay.cs"]
- **Evidence:** MetaHorizonAvatarProvider.cs:99-103 (`RecordStreamData_AutoBuffer(streamLod, ref streamBuffer)` then `streamData = new byte[byteCount]` copy); MetaAvatarStreamMessage.cs:35 (`Payload = length == 0 ? Array.Empty<byte>() : new byte[length];` per received message); MetaAvatarStreamRelay.cs:26-57 (LateUpdate send at streamSendRateHz = 15).
- **RecommendedFix:** Reuse a persistent byte buffer per relay (record into the retained streamBuffer and pass an ArraySegment/length alongside, or pool payload arrays by size bucket) so the steady-state streaming path is allocation-free. The wire write already copies, so the buffer can be reused immediately after send.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["performance"]
- **MergedCount:** 1

## 136. Naming drift across the three preset identifier systems, including a vestigial wrapper class and a mis-named file

- **Severity:** Low
- **Impact:** The same concept carries inconsistent names per layer: enum CreativeWorldGenerationPreset.SurvivalLite ↔ class SurvivalTerrainPreset ↔ string "survival_terrain"; enum FlatCreative ↔ class FlatBuilderPreset ↔ string "flat_builder" — plus FlatCreativeWorldPreset, a class whose Generate() only delegates to FlatBuilderPreset, used at exactly one call site while every other site uses FlatBuilderPreset directly. SurvivalTerrainPreset lives in a file named SurvivalLiteWorldPreset.cs. 'CreativeWorldManager' actually owns worlds for both creative and survival modes. Each mismatch costs navigation time and invites wrong-symbol edits.
- **Files:** ["Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs", "Assets/Blockiverse/Scripts/WorldGen/SurvivalLiteWorldPreset.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs"]
- **Evidence:** WorldGenerationSettings.cs:98–113 (FlatCreativeWorldPreset.Generate() => new FlatBuilderPreset(...).Generate()); its only consumer is MultiplayerChunkAuthoritySync.cs:428, while BlockiverseWorldSessionController.cs:308/662 use FlatBuilderPreset. SurvivalLiteWorldPreset.cs:10 declares SurvivalTerrainPreset. CreativeWorldManager.cs:19–25 comment documents it owns survival worlds too. Also CreativeWorldManager.cs:55–60 infers the preset from a magic 'Height >= 32' heuristic.
- **RecommendedFix:** Delete FlatCreativeWorldPreset (point the one call site at FlatBuilderPreset), rename SurvivalLiteWorldPreset.cs to SurvivalTerrainPreset.cs, and align enum/class/string naming when introducing the WorldPresetCatalog recommended above. Replace the Height>=32 inference with an explicit preset argument (all real callers already pass one).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 137. No quick 180° turn-around option in snap turning

- **Severity:** Low
- **Impact:** Players who need to reverse direction without smooth rotation must chain multiple snap turns (up to 12 presses at the 15° minimum). XRI's built-in turn-around gesture is explicitly disabled with no setting to enable it.
- **Files:** ["Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** BlockiverseInputRig.ConfigureXriLocomotionProviders line 541: `snapTurnProvider.enableTurnAround = false;` with no comfort setting controlling it.
- **RecommendedFix:** Add a 'Turn Around (stick down)' toggle to BlockiverseComfortSettings + the comfort menu, and set snapTurnProvider.enableTurnAround from it in ApplyComfortSettingsToProviders.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["accessibility"]
- **MergedCount:** 1

## 138. Null-conditional operator used on UnityEngine.Object-derived references, bypassing Unity lifetime checks

- **Severity:** Low
- **Impact:** ?. and ?? on UnityEngine.Object compare against true null, not Unity's destroyed-object fake-null, so a destroyed-but-referenced component passes the check and throws MissingReferenceException (or silently operates on a dead object) when the call executes. In this single-scene game most targets live for the process lifetime, so practical risk concentrates in teardown ordering (OnDestroy chains) and world-reconfiguration paths where renderers/previews are destroyed and recreated.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs"]
- **Evidence:** Examples: 'worldManager.Renderer?.RebuildDirty()' (MultiplayerChunkAuthoritySync.cs lines 507, 765), 'Renderer?.RebuildDirty()' (CreativeWorldManager.cs line 658), 'worldManager?.ConfigureAuthoritySync(this)' (lines 84/92/949), 'avatarRig?.HeadAnchor' on a GetComponent result (MultiplayerSurvivalSync.cs line 2888), plus dozens of 'panel?.Refresh()' patterns in UI. The codebase mixes this with correct '!= null' checks (e.g. VoxelWorldRenderer.ProcessPendingColliderRebuilds line 261 properly checks 'chunkObject == null').
- **RecommendedFix:** Adopt a convention: explicit '!= null' (Unity-aware) comparisons for UnityEngine.Object-derived fields, reserving ?./?? for plain C# types (services, events). Lowest-risk targets first: teardown paths and anything touched during world reconfiguration. Consider enabling the UNT0008 Unity analyzer (null-propagation on Unity objects) to enforce it.
- **Confidence:** Medium
- **NeedsManualVerification:** false
- **Sources:** ["unity-csharp"]
- **MergedCount:** 1

## 139. P2: No item-icon coverage validation test (blocks have one; items do not)

- **Severity:** Low
- **Impact:** ChunkRenderingEditModeTests.VisualAtlasContainsDistinctTilesForEveryRenderableBlock and the M4 art-validation tests guarantee every renderable block has authored atlas art, but there is no equivalent test that every ItemId in ItemRegistry.CreateDefault() resolves an icon in the generated icon set / BlockiverseItemIconLibrary. A newly added item without a generated icon ships as a blank sprite in the inventory/crafting/station UI and nothing fails.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/BlockiverseItemIconLibrary.cs", "Assets/Blockiverse/Tests/EditMode/M4ArtAssetValidationEditModeTests.cs"]
- **Evidence:** grep 'ItemIconLibrary' under Assets/Blockiverse/Tests/ → zero hits. BlockiverseItemIconLibrary.cs:21-31 (Configure/TryGetIcon); consumers SurvivalInventoryPanel.cs:140 and SurvivalCraftingPanel.cs:242 silently render no icon when TryGetIcon fails. Bootstrapper wires icons at BlockiverseProjectBootstrapper.cs:719/2822/2885.
- **RecommendedFix:** Add to M4ArtAssetValidationEditModeTests (editor asmdef has AssetDatabase access): EveryRegisteredItemHasAGeneratedIconSprite — enumerate ItemRegistry.CreateDefault().All, load the generated icon sprites the bootstrapper binds (same asset path logic as EnsureBlockItemIcons), and assert each item id resolves a non-null sprite; plus IconLibraryConfigureRejectsMismatchedArrayLengths as a unit test on the component.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 140. P2: Offline host-rejection matrix for survival commands is only 2 cases deep

- **Severity:** Low
- **Impact:** Only two offline rejection tests exist (HostRejectsNonConsumableUseWithoutThrowing, HostRejectsUnknownCrateTransferItemWithoutThrowing). The remaining host validation branches — place with an empty/non-block slot, harvest out of bounds or with empty hand on a tier-gated block via the sync (covered in ResourceHarvestService unit tests but not through the sync's slot-resolution path), till on non-tillable, plant on non-tended soil, repair with no wear/material/bench via sync, crate withdraw exceeding the crate count, station deposit of a non-fuel item via sync — are unexercised through the command layer that actually resolves inventory slots and positions on the host.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Tests/PlayMode/Networking/MultiplayerSessionPlayModeTests.cs"]
- **Evidence:** MultiplayerSessionPlayModeTests.cs:1694-1722 and 1790-1813 are the only [Test] (non-networked) rejection cases. The ProcessHost* methods (lines 1138-2050) each contain 2-4 rejection branches keyed by SurvivalCommandFailureReason; the FailureReason enum at MultiplayerSurvivalSync.cs:34 has many members never asserted anywhere.
- **RecommendedFix:** Extend the existing offline [Test] pattern into a small matrix in MultiplayerSessionPlayModeTests (or a new SurvivalSyncHostValidationTests): HostRejectsPlaceFromEmptySlot, HostRejectsPlaceOfNonBlockItem, HostRejectsCrateWithdrawExceedingCount, HostRejectsTillOnNonTillableBlock, HostRejectsPlantOnUntendedSoil, HostRejectsRepairWithoutMendBenchViaSync, HostRejectsStationFuelDepositOfNonFuelViaSync — each asserting the specific SurvivalCommandFailureReason and zero world/inventory mutation.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 141. P2: PlayerVitals.RestoreHealth / SurvivalVitals.RestoreFrom clamp contracts untested

- **Severity:** Low
- **Impact:** RestoreHealth documents and implements the load-time contract 'a save can never restore into a dead state' (clamp to [1, MaxHealth]) and fires HealthChanged for HUD repaint; RestoreFrom clamps and resets the tick accumulators. Neither is covered, so a clamp regression would let a corrupted/edited save spawn the player dead with no death screen (the LocalPlayerDied event only fires on a damage transition).
- **Files:** ["Assets/Blockiverse/Scripts/SurvivalHealth/PlayerVitals.cs", "Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs", "Assets/Blockiverse/Tests/EditMode/SurvivalHealth/PlayerVitalsEditModeTests.cs"]
- **Evidence:** PlayerVitals.cs:98-113 (RestoreHealth clamp to [1, MaxHealth], HealthChanged publish); SurvivalVitals.cs:83-92 (RestoreFrom clamps and zeroes accumulators). Neither symbol appears in PlayerVitalsEditModeTests.cs or SurvivalVitalsEditModeTests.cs.
- **RecommendedFix:** Add to PlayerVitalsEditModeTests: RestoreHealthClampsZeroOrNegativeToOneAndNeverRestoresDead, RestoreHealthClampsAboveMaxAndRaisesHealthChanged. Add to SurvivalVitalsEditModeTests: RestoreFromClampsOutOfRangeValuesAndResetsAccumulators (assert a subsequent Tick of less than one interval does not immediately deplete, proving accumulators were reset).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 142. Pause menu always offers 'Quit Game' on Quest while the title menu deliberately hides Quit, and quit paths skip the spec'd confirmation

- **Severity:** Low
- **Impact:** Inconsistent platform behavior: the title screen hides Quit on device (per the 'Quest apps exit via Home' rationale) but the pause menu unconditionally shows Quit Game, which calls Application.Quit() immediately. Both TitleQuit and PauseQuit also skip the confirm dialog required by §5/§6.7 ('Confirms if unsaved changes exist') — mitigated by the automatic save-before-quit.
- **Files:** ["Assets/Blockiverse/Scripts/UI/MenuActions.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs"]
- **Evidence:** MenuActions.Pause is a static list always containing PauseQuit (MenuActions.cs:97-106) with no canQuit parameter, while Title(...) gates TitleQuit on canQuit (lines 92-93) and CanQuit() is false outside the editor (BlockiverseMenuController.cs:557-562). PauseQuit handler saves then quits with no confirm (lines 401-404); TitleQuit likewise (366-370).
- **RecommendedFix:** Give MenuActions.Pause a factory taking canQuit (mirroring Title) and rebuild the pause menu in Start() with CanQuit(); wrap both quit actions in RequestConfirm('Quit game?', ...) per §6.22.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["ui-menu-flow"]
- **MergedCount:** 1

## 143. Per-tick and per-step micro-allocations in the world simulation loop

- **Severity:** Low
- **Impact:** Small but steady GC pressure in the 20 Hz tick path: (1) ChunkRebuildQueue.DrainDirtyChunks allocates a new List per drain whenever anything is dirty (every tick during fluid activity); (2) FluidFlowService.StepFamily calls processScratch.Sort(ComparePositions) — under Unity's C# the static method group converts to a fresh Comparison<T> delegate per call (up to 20×/s across families), and the sort itself is O(n log n) over the active set each step; (3) FarmingService growth sweep snapshots keys per interval; (4) BlockiverseVfxPool.ConfigureParticleSystem allocates a Burst[] per Play (rain VFX every 0.6 s). Individually trivial, but they run forever and Quest GC pauses are user-visible; the codebase elsewhere goes to lengths (ThreadStatic pools, scratch lists) to avoid exactly this.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs", "Assets/Blockiverse/Scripts/WorldGen/FluidFlowService.cs", "Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxPool.cs", "Assets/Blockiverse/Scripts/Survival/FarmingService.cs"]
- **Evidence:** ChunkMeshBuilder.cs:240 `var drained = new List<ChunkCoordinate>(dirtyChunks);` per non-empty drain; FluidFlowService.cs:327 `processScratch.Sort(ComparePositions);` (method-group delegate allocation per call) inside StepFamily called per family per cadence tick; BlockiverseVfxPool.cs:96-99 `emission.SetBursts(new[] { new ParticleSystem.Burst(...) })` per Play; FarmingService SnapshotKeys per growth interval (line ~330).
- **RecommendedFix:** Cache a static Comparison<BlockPosition> (or IComparer<BlockPosition>) instance for the fluid sort; reuse a scratch list in DrainDirtyChunks (swap pattern: return the filled scratch and clear into a second set); cache a single-element Burst[] in the VFX pool and mutate it before SetBursts. All are two-line changes with zero semantic impact.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["performance"]
- **MergedCount:** 1

## 144. PerformanceStatsOverlay and FrameStatisticsSampler have no runtime or scene wiring — manual-attach dev tooling only

- **Severity:** Low
- **Impact:** PerformanceStatsOverlay (Gameplay) has zero C# references and appears in no scene/prefab; FrameStatisticsSampler (Core) is referenced only by that overlay and its own EditMode test. Both compile into the shipped player but are unreachable unless a developer hand-attaches the overlay in the editor — which is exactly what docs/testing/performance/ instructs, so this is intentional dev tooling rather than rot. Worth recording because the docs say 'enable PerformanceStatsOverlay' yet no menu item, debug flag, or scene object can enable it on a device build. Classification: definitely unused at runtime; documented manual editor tooling.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/PerformanceStatsOverlay.cs", "Assets/Blockiverse/Scripts/Core/FrameStatisticsSampler.cs", "docs/testing/performance/README.md", "docs/testing/performance/report-template.md", "Assets/Blockiverse/Scenes/Boot.unity", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab"]
- **Evidence:** PerformanceStatsOverlay guid 41c4cf1aef1d8e774d101b4493bfc1cb appears in no .unity/.prefab/.asset; repo-wide grep for the class name hits only docs and scripts/store/validate-store-submission-docs.sh (which greps the docs, not the code). FrameStatisticsSampler's only non-test reference is PerformanceStatsOverlay.cs. docs/testing/performance/README.md:14 describes it as the in-headset HUD.
- **RecommendedFix:** If in-headset perf HUD passes are still part of the release checklist, add a guarded way to enable it in development builds (e.g. the bootstrapper adds it disabled, or a debug menu action), since the current workflow only works in the editor. Otherwise move both classes' usage expectation out of the perf docs or accept them as editor-only tooling.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["dead-code", "runtime-wiring"]
- **MergedCount:** 2

## 145. PerformanceStatsOverlay ships an OnGUI handler (IMGUI loop) in player builds

- **Severity:** Low
- **Impact:** Any enabled MonoBehaviour implementing OnGUI forces Unity's IMGUI event/repaint pipeline to run every frame (multiple OnGUI invocations per frame plus GUI event allocation), even when the method early-outs. The visibility guard only suppresses drawing in non-debug builds — the component, its Update sampler, and the IMGUI hook remain active in release. IMGUI also renders to the (non-existent in VR) screen overlay; the cost is small but pure waste on Quest, and the 5-second log summary builds interpolated strings forever.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/PerformanceStatsOverlay.cs"]
- **Evidence:** PerformanceStatsOverlay.cs:79-92 OnGUI with guard `if (!visible || !Sampler.HasSamples || !(Debug.isDebugBuild || Application.isEditor)) return;` — the method still exists and is called by IMGUI each frame; Update (51-64) samples and logs every 5 s (interpolated string at 72-76).
- **RecommendedFix:** Compile the OnGUI path out of release builds (#if DEVELOPMENT_BUILD || UNITY_EDITOR around OnGUI, or disable the component entirely in release via the bootstrapper), keeping the FrameStatisticsSampler/log if the telemetry is wanted. This removes the IMGUI pipeline from shipping frames entirely.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["performance"]
- **MergedCount:** 1

## 146. Reduced Flash only attenuates lightning to 35% instead of offering full suppression

- **Severity:** Low
- **Impact:** Photosensitive players enabling 'Reduced Flash' still get lightning flashes at 35% intensity. The ruleset frames the setting as protection for intense pulses; for photosensitivity, an off option is the safe choice (thunder audio still conveys the storm).
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxCuePlayer.cs", "docs/rulesets/voxel_audio_vfx_ruleset.md"]
- **Evidence:** BlockiverseVfxCuePlayer.PlayCue lines 40–44: `if (cue == BlockiverseVfxCue.LightningFlash && feedbackSettings.ReducedFlash) intensity *= 0.35f;` — flash is reduced, never skipped.
- **RecommendedFix:** When ReducedFlash is enabled, skip the LightningFlash cue entirely (return before pool.Play) or add a separate 'No Flash' tier; keep the ThunderNear/Far audio so storms remain perceivable.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["accessibility"]
- **MergedCount:** 1

## 147. Registry-hash ID ordering uses the culture-sensitive default string comparer

- **Severity:** Low
- **Impact:** ComputeBlockRegistryHash/ComputeItemRegistryHash sort canonical IDs with LINQ OrderBy's default comparer, which is culture-sensitive. If collation ever orders two IDs differently across device locales (locale-tailored collation of sequences, or Mono/ICU behavior differences on Quest vs editor), the concatenated string and its MD5 differ, and WorldSaveService.cs:391–393 would reject a perfectly valid save with a registry-mismatch failure. The current all-lowercase-ASCII-with-underscores ID set makes divergence unlikely today, but the comparer is the wrong tool for a stable wire/persistence fingerprint.
- **Files:** ["Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs"]
- **Evidence:** WorldSaveService.cs:989: string.Join("|", registry.All.Select(d => d.CanonicalId).OrderBy(id => id)); line 995: same for item IDs. OrderBy with no comparer uses Comparer<string>.Default (culture-sensitive CompareTo), unlike the deliberate StringComparer.OrdinalIgnoreCase used elsewhere (e.g. SaveListModel.cs:123). The hashes are persisted in the manifest and compared on load (lines 391–393).
- **RecommendedFix:** Pass StringComparer.Ordinal to both OrderBy calls. Existing saves keep matching as long as ordinal order equals the order the current locale produced, which holds for the present ID set — verify by comparing the ordinal-sorted join against the current output before shipping.
- **Confidence:** Medium
- **NeedsManualVerification:** true
- **Sources:** ["localization"]
- **MergedCount:** 1

## 148. Right-stick UI scrolling triggers snap turns simultaneously; falls bypass the comfort vignette

- **Severity:** Low
- **Impact:** Two smaller comfort/input conflicts: (1) UI Scroll and Turn share the raw right thumbstick with no suppression while the ray hovers UI, so nudging the stick left/right to scroll a panel also snap-turns the player (and with smooth turn enabled, rotates continuously) — turning the world behind a menu the player is reading. (2) The tunneling vignette is wired to move/turn/teleport providers only; GravityProvider and JumpProvider are excluded, so long vertical falls (off cliffs, into caves) — among the strongest vection generators — get no vignette even with comfort fully enabled.
- **Files:** ["Assets/Blockiverse/Settings/BlockiverseInputActions.inputactions", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab"]
- **Evidence:** Bindings ...049 (UI Scroll) and ...052 (Turn) both map <XRController>{RightHand}/thumbstick in the action asset; BlockiverseInputRig.ConfigureXriProviderInputs (lines 594-608) feeds the same Turn action to snap/continuous turn with no UI-hover gating. Bootstrapper EnsureXrRigTunnelingVignette (lines 2267-2270) adds only ContinuousMoveProvider, ContinuousTurnProvider, TeleportationProvider; prefab m_LocomotionVignetteProviders (line 8330) lists exactly three providers — no GravityProvider/JumpProvider entries.
- **RecommendedFix:** Suppress turn input while the right ray reports IsOverUIGameObject (gate in a custom reader or disable the turn providers from the bridge while hovering UI). Add the GravityProvider (and optionally JumpProvider) to the vignette's locomotionVignetteProviders in EnsureXrRigTunnelingVignette.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["vr-interaction"]
- **MergedCount:** 1

## 149. Seven per-assembly marker classes are unreferenced placeholders

- **Severity:** Low
- **Impact:** GameplayAssembly, NetworkingAssembly, PersistenceAssembly, SurvivalHealthAssembly, UIAssembly, VoxelAssembly, and WorldGenAssembly are static classes holding (at most) a Name constant; none is referenced anywhere in source, tests, or tooling, and SurvivalHealthAssembly is an entirely empty internal class. Five of the twelve assemblies (Core, Survival, VR, MetaAvatars, Editor) have no such marker, so the pattern is also inconsistent — they appear to be leftover asmdef-seed placeholders. Classification: definitely unused.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/GameplayAssembly.cs", "Assets/Blockiverse/Scripts/Networking/NetworkingAssembly.cs", "Assets/Blockiverse/Scripts/Persistence/PersistenceAssembly.cs", "Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalHealthAssembly.cs", "Assets/Blockiverse/Scripts/UI/UIAssembly.cs", "Assets/Blockiverse/Scripts/Voxel/VoxelAssembly.cs", "Assets/Blockiverse/Scripts/WorldGen/WorldGenAssembly.cs"]
- **Evidence:** Each file contains only `public static class XAssembly { public const string Name = "…"; }` (SurvivalHealthAssembly.cs is `internal static class SurvivalHealthAssembly { }`). Repo-wide grep for each class name and for any `Assembly.Name` constant usage returns zero references outside the declaring files. Every assembly contains other compiled files, so deletion cannot empty an asmdef.
- **RecommendedFix:** Delete all seven marker files (each assembly has plenty of other sources, so the asmdefs stay valid).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["dead-code"]
- **MergedCount:** 1

## 150. SmeltingStationModel.TryDepositInput accepts stacks exceeding MaxStackSize into empty slots

- **Severity:** Low
- **Impact:** The merge path enforces `inputs[i].Count + stack.Count <= max`, but the empty-slot path assigns the whole incoming stack without clamping, so a host-processed deposit of e.g. 120 clay lumps lands as a single over-max stack in a station slot. This violates the stack-size invariant the Inventory class enforces elsewhere and can later surface as an oversized stack when contents round-trip through snapshots/saves.
- **Files:** ["Assets/Blockiverse/Scripts/Survival/SmeltingStationModel.cs"]
- **Evidence:** SmeltingStationModel.cs:83-110 — merge branch checks max (line 91) but the empty-slot branch (lines 99-107) does `inputs[i] = stack;` with no MaxStackSize check; the host passes a client-chosen count (MultiplayerSurvivalSync.cs:1964-1974) bounded only by CountOf.
- **RecommendedFix:** Clamp deposits to MaxStackSize in TryDepositInput (split or reject the remainder), mirroring TryDepositFuel's check.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 151. Stale Source/fired_brick.png block tile duplicates fired_brick_block.png under the old name

- **Severity:** Low
- **Impact:** Assets/Blockiverse/Art/Textures/Blocks/Source contains both fired_brick.png (old generator name, crc32-scheme GUID c095bd46...) and fired_brick_block.png (current canonical id, different pixel content, Unity-random GUID). Only fired_brick_block matches a BlockRegistry canonical id; the stale file is dead but will be resurrected by the current generator script (which still uses the old name), keeping the confusion alive and leaving the atlas tile 31 content ambiguous across regenerations.
- **Files:** ["Assets/Blockiverse/Art/Textures/Blocks/Source/fired_brick.png", "Assets/Blockiverse/Art/Textures/Blocks/Source/fired_brick_block.png", "scripts/art/generate-art-assets.py"]
- **Evidence:** Registry diff: Source contains exactly one file not matching any canonical block id — fired_brick (canonical id is fired_brick_block, VoxelTypes.cs FiredBrickBlock registration). `cmp` shows the two PNGs differ. generate-art-assets.py:50 still emits ("fired_brick", 31, ...).
- **RecommendedFix:** Delete Source/fired_brick.png(.meta) and rename the generator entry to fired_brick_block as part of the script resync (see the generator-drift finding). Note Items/fired_brick.png is a different, legitimate asset (ItemId.FiredBrick).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["asset-integration-run1"]
- **MergedCount:** 1

## 152. Stamina is a dead stat: displayed and restored but never consumed

- **Severity:** Low
- **Impact:** Stamina is shown on the HUD, regenerates 1/s, is restored by clean water, and is reset on respawn — but SurvivalVitals.TrySpendStamina has zero callers in runtime code: no locomotion, mining, or jumping costs stamina. Players see a bar that never moves below full, which reads as broken UI and removes the intended exertion economy (clean water's stamina restore is also worthless).
- **Files:** ["Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs", "Assets/Blockiverse/Scripts/UI/SurvivalHealthPanel.cs", "Assets/Blockiverse/Tests/EditMode/SurvivalHealth/SurvivalVitalsEditModeTests.cs"]
- **Evidence:** Grep for TrySpendStamina across Assets/Blockiverse/Scripts: only the definition (SurvivalVitals.cs:96); no Gameplay/VR caller. SurvivalHealthPanel.cs:104-106 renders 'Stamina {n}' on the HUD.
- **RecommendedFix:** Charge stamina for hold-to-mine ticks and/or sprint locomotion (with depletion slowing mine speed), or hide the stat from the HUD until it participates in gameplay.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design", "dead-code"]
- **MergedCount:** 2

## 153. Station and container save slots drop item durability

- **Severity:** Low
- **Impact:** WorldSaveStateMapper.ToSavedSlot and SavedContainerSlot serialize only CanonicalId + Count, so any durability-tracked tool sitting in a kiln/forge slot or a world container at save time comes back with Durability 0 after load — joining the same 'never wears out' family as the crate-transfer bug. Player inventory slots DO persist durability (SavedInventorySlot.Durability), making the omission inconsistent.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/WorldSaveStateMapper.cs", "Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs"]
- **Evidence:** WorldSaveStateMapper.cs:76-85 ToSavedSlot/FromSavedSlot carry no durability; SavedContainerSlot (WorldSaveService.cs:43-47) has no Durability field, while SavedInventorySlot (lines 24-30) does.
- **RecommendedFix:** Add a Durability field to SavedContainerSlot/VxlwContainerSlot and map it in ToSavedSlot/FromSavedSlot (schema-compatible additive change while pre-release).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 154. Station panel is not closed (or refreshed) when its station block is destroyed or becomes stale

- **Severity:** Low
- **Impact:** If the kiln/forge block backing an open station panel is broken (e.g. by another player in co-op) the panel stays open showing the removed station's contents; deposits/collects fail with host rejections but the UI keeps presenting the dead station until the user closes it manually.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs"]
- **Evidence:** Stale station models are silently dropped during the host tick ('stationModels.Remove(stale)', MultiplayerSurvivalSync.cs:1040-1041) with no event; the only station event is StationOpenRequested (line 900). BlockiverseMenuController subscribes only to StationOpenRequested (lines 272-285); nothing closes the panel when the model is removed, and BlockiverseStationPanel.Update keeps rendering its captured 'station' reference (lines 96-125).
- **RecommendedFix:** Add a StationRemoved(BlockPosition) event to MultiplayerSurvivalSync raised when a model is dropped (host) or a snapshot invalidates it (client); BlockiverseMenuController handles it by calling HandleStationCloseRequested when stationPanel.OpenPosition matches.
- **Confidence:** Medium
- **NeedsManualVerification:** false
- **Sources:** ["ui-menu-flow"]
- **MergedCount:** 1

## 155. StructureTerrainFit enum is declared and never used anywhere

- **Severity:** Low
- **Impact:** Dead public enum in the WorldGen assembly; readers will assume structures support SnapToSurface/Flatten terrain-fit modes when no such system exists. Safe to delete. Classification: definitely unused.
- **Files:** ["Assets/Blockiverse/Scripts/WorldGen/StructureService.cs"]
- **Evidence:** StructureService.cs:8 `public enum StructureTerrainFit  { SnapToSurface, Flatten }`; repo-wide `grep -rnw StructureTerrainFit Assets` returns only this declaration (its sibling StructureDegradation at line 7 is heavily used; this one is not).
- **RecommendedFix:** Delete the enum, or implement the terrain-fit behavior in StructureService.PlaceRuin if it was planned.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["dead-code"]
- **MergedCount:** 1

## 156. Survival command channel accepts any inventory slot as 'equipped' and other minor validation gaps

- **Severity:** Low
- **Impact:** The host bounds-checks the wire-supplied equippedSlotIndex against SlotCount, not HotbarSlotCount, so a modified client can harvest/place/strip/till/plant using items in backpack slots the local UI would never allow (EquippedItem getter restricts to hotbar). Survival placement also has no player-overlap check on the host (the creative path's playerBounds check is not mirrored), allowing blocks to be placed inside players, and the VR undo button calls CreativeInteractionController.UndoLast unconditionally even in survival mode (host-local only, since clients are rejected by GameModeForbidsDirectMutation).
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeInteractionController.cs"]
- **Evidence:** ResolveAuthoritativeTool (MultiplayerSurvivalSync.cs:1768-1774) and ProcessHostPlace/StripLog/Till/PlantSeed (e.g. lines 1261-1263) test `equippedSlotIndex < inventory.SlotCount` only; EquippedItem (lines 349-360) limits to HotbarSlotCount. ProcessHostPlace (1245-1306) has no player-position overlap validation. BlockiverseCreativeInputBridge.TryUndo (lines 304-307) has no SurvivalInteractionActive gate, unlike TryBreakTarget/TryPlaceTarget.
- **RecommendedFix:** Clamp the accepted slot index to the hotbar range on the host; reject placements intersecting any connected player's head/feet cells; gate TryUndo on creative mode.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 157. Survival command channel is not gated by world game mode: clients can run the survival economy in creative multiplayer worlds

- **Severity:** Low
- **Impact:** In a creative-mode LAN world, remote clients can still send harvest/craft/place/crate commands; the host processes them, mutating host-side per-client inventories and the world through survival rules that creative mode is not supposed to use. The inverse gate exists (survival worlds reject the raw creative mutation channel), but not this direction — an asymmetry that can produce odd hybrid state (e.g. survival drops harvested out of a creative build).
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs"]
- **Evidence:** HandleCommandRequestMessage (MultiplayerSurvivalSync.cs:2094-2186) checks only CanProcessHostRequests() (listening && IsServer) — no worldManager.GameMode check — while the creative channel's survival gate exists in MultiplayerChunkAuthoritySync.HandleMutationRequestMessage (lines 277-289, GameModeForbidsDirectMutation).
- **RecommendedFix:** Mirror the gate: in HandleCommandRequestMessage (or each ProcessHost*), reject survival commands with a dedicated failure reason when worldManager.GameMode == WorldGameMode.Creative, unless the design explicitly wants survival actions in creative co-op.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["lan-multiplayer"]
- **MergedCount:** 1

## 158. Survival HUD rebuilds slot/recipe label strings on every inventory change

- **Severity:** Low
- **Impact:** LocalInventoryChanged fires after every accepted harvest/place/craft (i.e., every mined block). SurvivalHudController.OnLocalInventoryChanged then refreshes both the inventory panel (per-slot `$"x{stack.Count}"` / FormatStack interpolation across all slots) and the crafting panel (FormatRecipe string per visible recipe row). During sustained mining this allocates dozens of short-lived strings per second and re-touches every TMP label (TMP early-outs on equal text, but the strings are still allocated). The station panel demonstrates the right pattern in the same codebase (ContentVersion-gated label rebuilds with an explicit comment that 'TMP label rebuilds and the string formatting are too costly per frame in VR').
- **Files:** ["Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs", "Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs", "Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs"]
- **Evidence:** SurvivalHudController.cs:152-170 (OnLocalInventoryChanged → inventoryPanel.Refresh() + craftingPanel.Refresh()); SurvivalInventoryPanel.cs:80-109 (Refresh: per-slot `slotLabels[i].text = $"x{stack.Count}"` / FormatSlot → FormatStack interpolation at 152-160); SurvivalCraftingPanel.cs:218-232 (Refresh: FormatRecipe per label). MultiplayerSurvivalSync raises LocalInventoryChanged from SendInventorySnapshot on every accepted command (MultiplayerSurvivalSync.cs:2443-2446).
- **RecommendedFix:** Only rewrite a slot label when that slot's (itemId, count, durability) actually changed — keep a small per-slot cache of last-rendered values, or version the Inventory and per-slot dirty bits. Pre-cache count strings for common stack sizes ("x1".."x99") to remove the interpolation entirely. Crafting rows only need re-formatting when craftability or the recipe list changes, not on every pickup.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["performance"]
- **MergedCount:** 1

## 159. Survival mode is selectable with void_builder and flat_builder presets that cannot sustain vitals

- **Severity:** Low
- **Impact:** The New World menu lets players combine GameMode=survival with the void_builder preset (a 16x16 cutstone platform, nothing else) or flat_builder (turf/loam/graystone only). In these worlds there is no water, no food, and no resources, so thirst/hunger drain to an inevitable death-respawn loop with no possible counterplay. No validation or warning exists at world creation.
- **Files:** ["Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs", "Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs"]
- **Evidence:** NewWorldConfig.cs:12-15: GameModeOptions and WorldPresetOptions cycle independently; IsValid (lines 63-73) checks only the name. VoidBuilderPreset.Generate (WorldGenerationSettings.cs:132-154) places only a cutstone platform; vitals tick whenever CurrentMode == Survival regardless of preset (SurvivalVitalsRuntime.OnWorldTick).
- **RecommendedFix:** Warn or disable survival vitals for builder presets (treat flat/void builder worlds as creative-rule worlds per the creative ruleset's preset intent), or validate the mode+preset combination in NewWorldConfig.IsValid.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 160. SurvivalHudController never unsubscribes craftingPanel.CraftingChanged and cratePanel.CrateChanged

- **Severity:** Low
- **Impact:** Asymmetric event cleanup: OnDestroy detaches the selection-changed and survival-sync handlers but not the two panel events. Today the panels are children of the HUD (destroyed together), so the leak is bounded; but if Configure() ever points the controller at external panels, the destroyed controller's RefreshPanels delegate would keep the controller alive and fire on dead UnityEngine.Objects.
- **Files:** ["Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs"]
- **Evidence:** Subscriptions at lines 106-116 (craftingPanel.CraftingChanged += RefreshPanels; cratePanel.CrateChanged += RefreshPanels); OnDestroy (lines 130-139) only unsubscribes selectionChangedSource and survivalSync handlers — no matching -= for the two panel events.
- **RecommendedFix:** Add 'if (craftingPanel != null) craftingPanel.CraftingChanged -= RefreshPanels;' and the cratePanel equivalent to OnDestroy, matching the existing pattern used for survivalSync.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["unity-csharp"]
- **MergedCount:** 1

## 161. UI feedback discovery (audio cue + haptics resolution) copy-pasted across at least 8 panel classes

- **Severity:** Low
- **Impact:** The identical DiscoverFeedback/PlayFeedback idiom — null-check field, FindFirstObjectByType<BlockiverseAudioCuePlayer>, FindFirstObjectByType<BlockiverseInteractionHaptics>, then PlayCue + PlayUiTick — is re-implemented in SurvivalInventoryPanel, SurvivalCratePanel, SurvivalCraftingPanel, BlockiverseComfortMenu, BlockiverseActionMenu, BlockiverseMultiplayerSessionMenu, BlockiverseWorldSpacePanelPresenter, and BlockiverseMenuController.PlayStationCue. Any change to feedback routing (e.g. a mute setting or pooled players) requires touching all copies.
- **Files:** ["Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseActionMenu.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseWorldSpacePanelPresenter.cs", "Assets/Blockiverse/Scripts/UI/SurvivalCratePanel.cs", "Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseComfortMenu.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs"]
- **Evidence:** Verbatim duplicates at SurvivalInventoryPanel.cs:218–228, BlockiverseActionMenu.cs:134–147, BlockiverseWorldSpacePanelPresenter.cs:227–238; same pattern at SurvivalCratePanel.cs:187–190, SurvivalCraftingPanel.cs:357–363, BlockiverseComfortMenu.cs:260–263, BlockiverseMultiplayerSessionMenu.cs:386–389, BlockiverseMenuController.cs:324–335.
- **RecommendedFix:** Extract a small UiFeedback helper (static or a scene component the bootstrapper wires once) exposing PlayUiCue(BlockiverseAudioCue) that owns discovery/caching; replace the eight copies.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 162. Unobtainable registered content and stack/vocabulary deviations from canon

- **Severity:** Low
- **Impact:** Multiple registered items have no acquisition path even after the clay fix: reedwood/flint Sickle, Mallet, Tiller (and reedwood Carver) have ItemRegistry entries but no recipes (so no Mallet of any tier is reachable before bronze, making §7.4 salvage and crafted-block reclaim — fired_brick_block/clearpane_glass require Mallet tier 1 — impossible mid-game); embercoal_block (the 720s fuel), reed_basket, tool_rack, pantry_jar, deep_locker have no recipes; rope_ladder, doorleaf, trap_hatch, wayflag, bedroll (§9.2) don't exist at all. Stack rules deviate: stations and storage containers stack 99 vs canon 10 / 1-filled. Player-facing vocabulary deviates from §0 canon: the items players collect are 'Flinty Shingle', 'Resin Knot', 'Surface Pebbles' where canon names flint_shard, resin_blob, stone_pebble as the item vocabulary.
- **Files:** ["Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs", "Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs", "Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs", "docs/rulesets/voxel_survival_ruleset.md"]
- **Evidence:** ItemRegistry.cs:117-132 registers all 7 reedwood/flint tool classes; CraftingRecipeBook.cs:129-135 registers recipes only for delver/spade/feller (+handcraft flint_carver). EmbercoalBlock registered (ItemRegistry.cs:107), consumed nowhere, produced nowhere. ItemRegistry station/crate stack sizes use BlockStackSize 99 (lines 68-81) vs ruleset §10.2 'Stations | 10', 'Storage containers | 1 if filled, 10 if empty'. CraftingRecipeBook.cs:29-32 comment: 'flint_shard → flinty_shingle, resin_blob → resin_knot, stone_pebble → surface_pebbles' vs ruleset §0.1 'Player-facing UI and new saves should use the canonical IDs'. VoxelTypes.cs:298-299 fired_brick_block/clearpane_glass harvestTierMin 1 with Mallet effective tool.
- **RecommendedFix:** Either add the missing §9.1/§9.2 recipes (reed basket, embercoal block, door/ladder/hatch/wayflag, early mallet) or remove the orphan registrations; align station/container stack sizes with §10.2; rename the harvested items to the canonical flint_shard/resin_blob/stone_pebble vocabulary with node blocks dropping them.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-design"]
- **MergedCount:** 1

## 163. World Details operations resolve saves by display name while the Load path uses collision-safe keys — destructive ops can target the wrong slot

- **Severity:** Low
- **Impact:** RefreshSaveList builds savePathsBySummaryKey (name+seed+createdUtc) explicitly because 'two saves share a display name', and LoadSelectedSave prefers it. But Play/Rename/Duplicate/Delete on the World Details screen resolve via TryResolveSavePathByName, which returns the first case-insensitive WorldName match. With colliding display names (saves copied in externally, or a manifest WorldName edited by hand), Delete/Rename can act on a different slot than the one shown — deletion is irreversible. The inconsistency between the two resolution strategies in the same file is the smell.
- **Files:** ["Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs"]
- **Evidence:** savePathsBySummaryKey + SummaryKey at lines 38–40 and 450–451 (comment: 'pins the exact slot even when two saves share a display name'); LoadSelectedSave uses it (424–446); but PlayDetailsSave (470–479), RenameDetailsSave (483–517), DuplicateDetailsSave (519–539), DeleteDetailsSave (541–568) all call TryResolveSavePathByName (453–466), first-match by name only.
- **RecommendedFix:** Route the details-screen operations through the same SummaryKey→path map (the WorldSaveSummary shown on the details panel already carries seed and createdUtc), falling back to name matching only when the map misses.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 164. WorldSaveService: 1,273-line file with 15 types, a 13-parameter Save overload, and a 240-line WriteRegionFiles method

- **Severity:** Low
- **Impact:** The persistence layer concentrates its whole data model and service in one file. Save(string, string, VoxelWorld, Inventory, int, Inventory, string, string, long, IReadOnlyList<SavedContainer>, string, string, WorldSaveExtras) has 13 parameters, which already produces unreadable single-line call sites (MultiplayerWorldPersistence.cs:213 and :266 pass 10 named arguments on one line) and makes adding a save field a signature-breaking change. WriteRegionFiles (lines 448–688) is a single method built around a Dictionary<(int,int), Dictionary<(int,int), Dictionary<int, List<(int,string)>>>> — hard to review and to extend when the region format evolves.
- **Files:** ["Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs"]
- **Evidence:** WorldSaveService.cs:219 (13-parameter Save signature), 448–688 (WriteRegionFiles, ~240 lines, triple-nested tuple dictionary at line 450), file contains SavedBlockDelta/SavedInventorySlot/…/WorldSaveService (15 top-level types, lines 15–191).
- **RecommendedFix:** Introduce a WorldSaveRequest DTO (world, inventory, environment, containers, extras, metadata) so Save takes (path, request); both save paths already build the same shape. Extract the region grouping into a RegionDeltaIndex helper class and split the DTOs into their own file(s).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 165. ~28 test-only telemetry counters baked into MultiplayerSurvivalSync's public API

- **Severity:** Low
- **Impact:** ReceivedHarvestRequestCount, AcceptedPlaceCount, RejectedCommandCount, etc. (lines 367–395) are public auto-properties incremented throughout the host paths but read only by MultiplayerSessionPlayModeTests. They inflate the god class's public surface, force every new command to remember to bump matching counters, and blur which members are production contract versus test instrumentation.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Tests/PlayMode/Networking/MultiplayerSessionPlayModeTests.cs"]
- **Evidence:** MultiplayerSurvivalSync.cs:367–395 declares 28 public counters; repo-wide grep shows the only consumers outside the two sync classes are Assets/Blockiverse/Tests/PlayMode/Networking/MultiplayerSessionPlayModeTests.cs. MultiplayerChunkAuthoritySync has the same pattern (AppliedSnapshotBlockCount, AppliedGenerationSnapshotCount).
- **RecommendedFix:** Collect the counters into one SurvivalSyncDiagnostics struct/class exposed as a single property (or compiled only in development builds), so command code increments diagnostics.X and the production API stays small.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern"]
- **MergedCount:** 1

## 166. ~35 scattered BlockRegistry/ItemRegistry/CraftingRecipeBook CreateDefault() construction sites with no shared canonical instance

- **Severity:** Low
- **Impact:** Each registry build registers ~80 definitions; UI panels, services, and sync classes each construct and hold private instances (some per-call, e.g. BlockiverseWorldSessionController.cs:182 builds an ItemRegistry inside the autosave path when survivalSync is null). Beyond redundant allocations, there is no single source of truth at runtime: if defaults ever become data-driven or moddable, instance divergence becomes a correctness bug, and reference-equality can never be used. The duplicated-instances pattern also obscures which registry a given Inventory validates against.
- **Files:** ["Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs", "Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs", "Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs", "Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs"]
- **Evidence:** Repo grep (excluding tests/editor) shows ~35 CreateDefault() call sites across UI, Gameplay, Persistence and Survival, e.g. SurvivalInventoryPanel.cs:13, SurvivalCratePanel.cs:17 (each a private static instance), MultiplayerSurvivalSync.cs:403–408 and 2793–2796, WorldSaveService.cs:141/168/204/235/390, BlockiverseWorldSessionController.cs:182/303/655.
- **RecommendedFix:** Introduce lazily-initialized shared defaults (e.g. BlockRegistry.Default / ItemRegistry.Default static readonly properties — the registries are immutable after construction) and route the ?? CreateDefault() fallbacks through them; keep explicit injection for tests.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["anti-pattern", "unity-csharp"]
- **MergedCount:** 2

## 167. 46 item icons have no matching ItemId, and BlockiverseHighlight.mat plus TunnelingVignette.prefab are referenced by nothing

- **Severity:** Informational
- **Impact:** Unused-but-committed content: 46 of 142 item icons match no registered item (full bronze/ironroot/rosycopper/deepsteel/starforged tool sets = 35 icons for tiers 3-7 that exist in the ruleset but not yet in ItemId; foods flatbread/trail_ration/berry_mash; block-named icons berrybush/grain_stalk/reedgrass/niterstone/paletin_thread/staropal_geode/sunmetal_fleck/umbralite_node). All of them are loaded into the rig's icon library at bootstrap (file name = id), which is harmless (a few hundred KB) but means the library carries dead entries. BlockiverseHighlight.mat is generated by the bootstrapper yet referenced by no scene/prefab (its only consumer, the 'Interaction Test Block' wiring, is absent from the current scenes), and TunnelingVignette.prefab is unused (the rig embeds the vignette mesh/material directly).
- **Files:** ["Assets/Blockiverse/Art/Textures/Items/", "Assets/Blockiverse/Materials/BlockiverseHighlight.mat", "Assets/Blockiverse/VR/TunnelingVignette/TunnelingVignette.prefab", "Assets/Blockiverse/Scripts/Survival/ItemId.cs"]
- **Evidence:** comm diff of 142 icon basenames vs 96 ItemId canonical ids leaves 46 extras; ItemId.cs defines only Reedwood and Flint tool tiers (lines 113-129). GUID scans: BlockiverseHighlight.mat guid d1257df724dcf4ea3a018ab73b6f2fdd → 0 references; TunnelingVignette.prefab guid → 0 references in BlockiverseXRRig.prefab (the .mat and .fbx are referenced directly instead). EnsureItemIconLibrary (bootstrapper:2885) loads every texture in the Items folder unconditionally.
- **RecommendedFix:** Nothing urgent. When tiers 3-7 / cooked foods land, the icons are ready. If you want a clean tree now: have EnsureItemIconLibrary filter to ItemRegistry ids, delete BlockiverseHighlight.mat or restore its consumer, and remove the unused TunnelingVignette.prefab. Keep the icons if they are intentional pre-work for the roadmap.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["asset-integration-run1"]
- **MergedCount:** 1

## 168. Android adaptive/round launcher icon slots are empty — only the legacy 192px icon is set

- **Severity:** Informational
- **Impact:** ProjectSettings assigns blockiverse_app_icon.png only to the default icon and the Android Legacy (Kind 0) 192px slot; all Adaptive (Kind 2) and Round (Kind 1) slots have empty texture arrays. Quest's launcher uses store-uploaded art for installed channel builds, so impact there is minimal, but sideloaded/Unknown-Sources listings and any non-Quest Android target fall back to the legacy icon rendered without adaptive masking (can appear letterboxed/cropped in round masks).
- **Files:** ["ProjectSettings/ProjectSettings.asset", "Assets/Blockiverse/Art/Sprites/Branding/blockiverse_app_icon.png"]
- **Evidence:** ProjectSettings.asset m_BuildTargetPlatformIcons Android section: all Kind 2 (432/324/216/162/108/81) and Kind 1 (192...36) entries have `m_Textures: []`; only the Kind 0 192px entry references guid a7184a73e67f4e688ee627ea6a34c6d9 (blockiverse_app_icon.png, 512x512).
- **RecommendedFix:** Generate foreground/background layers for the adaptive icon (the art generator could emit them) and assign them via the bootstrapper's ConfigureAppBranding so all Android icon kinds are populated.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["asset-integration-run1"]
- **MergedCount:** 1

## 169. Assets/CompositionLayers/UserSettings (per-user preference assets) is committed to the repo

- **Severity:** Informational
- **Impact:** CompositionLayersPreferences.asset and CompositionLayersRuntimeSettings.asset under an Assets-level 'UserSettings' folder are auto-generated by the Unity Composition Layers package. Committing per-user preference assets invites noisy diffs across machines; the CI forbidden-files gate blocks the top-level UserSettings/ directory but not this Assets-embedded one. Nothing in Blockiverse code references them.
- **Files:** ["Assets/CompositionLayers/UserSettings/CompositionLayersPreferences.asset", "Assets/CompositionLayers/UserSettings/Resources/CompositionLayersRuntimeSettings.asset", "scripts/ci/forbidden-files.sh"]
- **Evidence:** `git ls-files` tracks both assets and metas; forbidden_regex in scripts/ci/forbidden-files.sh matches `^(Library|Temp|Logs|UserSettings)/` only at repo root. No Blockiverse script references either asset; the package regenerates them on demand.
- **RecommendedFix:** Verify the package tolerates their absence (the Resources/ runtime-settings asset may be required in builds if composition layers are used at runtime); if it regenerates them, untrack the preferences asset at minimum and consider a forbidden-files pattern for Assets/**/UserSettings/. Mark as needs-verification before deleting the Resources asset.
- **Confidence:** Medium
- **NeedsManualVerification:** true
- **Sources:** ["dead-code"]
- **MergedCount:** 1

## 170. Crafting panel lacks per-recipe availability symbols specified by the accessibility rules

- **Severity:** Informational
- **Impact:** Recipes show ingredients and '[needs station]' as text (good — no color coding), but there is no ✓/✗/! availability marker per recipe as specified in §10.3, so players must attempt a craft (and parse the status line) to learn whether ingredients suffice. Minor friction, multi-channel failure feedback does exist (CraftFail audio + status text).
- **Files:** ["Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs", "docs/rulesets/voxel_survival_menus.md"]
- **Evidence:** SurvivalCraftingPanel.FormatRecipe (lines 307–313) renders output, ingredients, and '[needs {station}]' but never checks inventory.CanConsume-style availability; §10.3 specifies '✓ available / ✗ missing / ! wrong station' symbols.
- **RecommendedFix:** In FormatRecipe, prefix each recipe label with a symbol derived from ingredient availability against the bound inventory and EffectiveStationFor (text symbols keep it colorblind-safe), refreshing on inventory change events already routed through SurvivalHudController.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["accessibility"]
- **MergedCount:** 1

## 171. Crop growth conditions are permanently 'favorable' — §11.2 light/moisture gating is unwired

- **Severity:** Informational
- **Impact:** CreativeWorldManager.OnWorldTick calls farmingService.TickGrowth(World, CurrentWorldTick) without a conditions callback, so every crop always rolls its full base growth chance regardless of light, soil moisture, or biome. The FarmingService machinery (CropGrowthConditions, UnfavorableGrowthMultiplier, per-crop MinLight) exists but is dead at runtime. Note: wiring a real light source must use deterministic, synced inputs (e.g. the sky map + clock), or host/client growth lockstep breaks.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs", "Assets/Blockiverse/Scripts/Survival/FarmingService.cs"]
- **Evidence:** CreativeWorldManager.cs:652 `farmingService?.TickGrowth(World, CurrentWorldTick);` — conditions parameter omitted, defaulting to `_ => CropGrowthConditions.Favorable` (FarmingService.cs:299).
- **RecommendedFix:** When implementing, derive conditions from deterministic synced state only (VoxelSkyLightMap + block emissives + the §11.1 freshwater scan), never from frame-dependent rendering state, to preserve cross-peer growth determinism.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 172. EditMode tests pin exact English UI strings, coupling the test suite to the unlocalized copy

- **Severity:** Informational
- **Impact:** Any string extraction or copy change breaks these assertions, and they institutionalize English literals as the contract. When localization lands, these tests must be rewritten to assert string keys or table lookups; budgeting for that avoids surprise churn during extraction.
- **Files:** ["Assets/Blockiverse/Tests/EditMode/ActionMenuEditModeTests.cs", "Assets/Blockiverse/Tests/EditMode/SurvivalUiEditModeTests.cs"]
- **Evidence:** ActionMenuEditModeTests.cs:35–39 asserts title.text == "Paused" and labels equal "Resume", "Switch Survival/Creative", "Creative Tools", "Return to Title"; line 112–114 asserts confirm labels "Delete"/"Keep". SurvivalUiEditModeTests.cs:84/106 asserts statusLabel.text == "Crafted Work Plank x6".
- **RecommendedFix:** When introducing the string table, change these assertions to compare against the table lookup for the expected key (or assert the key resolution path) rather than literal English, so tests survive translation and copy edits.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["localization"]
- **MergedCount:** 1

## 173. EnsureOvrAvatarManager's creation branch is dead code (UNITY_ANDROID && !UNITY_EDITOR inside an editor-only assembly)

- **Severity:** Informational
- **Impact:** No functional impact — MetaHorizonAvatarProvider.cs:170-176 creates the OvrAvatarManager singleton at runtime on Quest, and the #else branch correctly deactivates any legacy scene manager — but the #if UNITY_ANDROID && !UNITY_EDITOR block in BlockiverseProjectBootstrapper.EnsureOvrAvatarManager (lines 1383-1398) can never compile true in an Editor assembly, so the OvrAvatarManager scene-object configuration code (loading budgets etc.) is unreachable and misleading to future maintainers.
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/MetaAvatars/MetaHorizonAvatarProvider.cs"]
- **Evidence:** BlockiverseProjectBootstrapper.cs:1377-1406: editor menu code guarded by `#if UNITY_ANDROID && !UNITY_EDITOR` — editor scripts always compile with UNITY_EDITOR defined, so only the #else (deactivate) branch ever exists. Boot.unity accordingly contains no OvrAvatarManager root; MultiplayerTest.unity contains one with m_IsActive: 0. Runtime creation confirmed at MetaHorizonAvatarProvider.cs:170-171 (`if (!OvrAvatarManager.hasInstance) OvrAvatarManager.Instantiate();`).
- **RecommendedFix:** Delete the unreachable #if branch and keep only the deactivation path with a comment pointing at MetaHorizonAvatarProvider's runtime creation (and move the MaxConcurrentAvatarsLoading tuning there if it is still wanted).
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["runtime-wiring"]
- **MergedCount:** 1

## 174. Informational: production sync classes carry large test-only counter surfaces; exact-value counter assertions over-couple tests to internals

- **Severity:** Informational
- **Impact:** MultiplayerChunkAuthoritySync exposes ~20 public counters (SentMutationRequestCount, AppliedSnapshotBlockCount, LastCompletedMutationRequestId, ...) that exist primarily as test observability, and the PlayMode tests assert exact values extensively (e.g. 25+ counter equality asserts in one test). This is deliberate and currently effective, but it couples tests to message-count implementation details — a benign change such as batching two deltas into one message or re-sending a snapshot would fail a dozen assertions that are not about player-observable behavior, raising maintenance cost.
- **Files:** ["Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs", "Assets/Blockiverse/Tests/PlayMode/Networking/MultiplayerSessionPlayModeTests.cs"]
- **Evidence:** MultiplayerChunkAuthoritySync.cs:47-70 (counter block); MultiplayerSessionPlayModeTests.cs:724-748 and 832-854 assert exact counter values alongside the world-state assertions that actually express the contract.
- **RecommendedFix:** Keep the counters, but when writing new tests prefer world/inventory-state and Is.GreaterThanOrEqualTo assertions for transport-level counts (the suite already does this for snapshot counts); reserve exact-equality for ordering-critical ids (sequence ids, request ids). No code change required now.
- **Confidence:** Medium
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 175. Informational: registry-hash-mismatch load path only logs a warning and that branch is untested

- **Severity:** Informational
- **Impact:** WorldSaveService.Load logs a warning and continues when the manifest's BlockRegistryHash differs from the current registry (relying on UnresolvedCanonicalIdDeltaIsSkippedOnApply for safety, which is tested). The warning branch itself, and the combined behavior 'mismatched-hash save still loads with unknown ids skipped', is not directly asserted, so the intended degradation mode is unpinned.
- **Files:** ["Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs", "Assets/Blockiverse/Tests/EditMode/WorldSaveEditModeTests.cs"]
- **Evidence:** WorldSaveService.cs:390-398 (hash compare → BlockiverseLog.Warning, no failure). WorldSaveEditModeTests covers hash storage (RegistryHashIsStoredInManifestAndMatchesCurrentRegistry) and unknown-id skipping, but no test writes a manifest with a foreign hash and asserts the load still succeeds with the warning.
- **RecommendedFix:** Add MismatchedRegistryHashLoadsWithWarningAndSkipsUnknownIds to WorldSaveEditModeTests: save normally, rewrite manifest.json's BlockRegistryHash to a bogus value, load, assert success, assert the captured log sink (the file already has a Log(BlockiverseLogEntry) sink helper) received the mismatch warning.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["test-coverage"]
- **MergedCount:** 1

## 176. Leaf decay only recognizes BranchwoodLog as support — stripping a living tree's trunk dissolves its canopy

- **Severity:** Informational
- **Impact:** HasNearbyLog checks only BlockRegistry.BranchwoodLog. Strip-log (Feller use) converts trunk blocks to SmoothBranchwood in place; CreativeWorldManager then marks the surrounding leaves as decay candidates (previous == BranchwoodLog), and on the next decay interval the entire canopy of a fully stripped tree decays away. If intended (stripped wood is dead wood) this is fine, but it is an easy player surprise worth a design confirmation since strip-log is presented as a cosmetic conversion.
- **Files:** ["Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs", "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs"]
- **Evidence:** VegetationService.HasNearbyLog (lines 427-445) matches only BlockRegistry.BranchwoodLog; CreativeWorldManager.OnBlockChanged lines 530-531 mark decay candidates when a BranchwoodLog becomes anything else, which includes the SmoothBranchwood strip conversion (MultiplayerSurvivalSync.ProcessHostStripLog).
- **RecommendedFix:** If unintended, include SmoothBranchwood as canopy support in HasNearbyLog; if intended, document it in the wiki's survival rules page.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 177. Minor feedback inconsistencies: stone mining sounds soft, controls text omits left-hand teleport, no snap-turn/teleport haptic

- **Severity:** Informational
- **Impact:** Polish-level observations: (1) hold-to-mine always plays ToolHitSoft regardless of material — the generated tool_hit_stone clip is loaded into the cue player but never played by any code path, so mining stone sounds like digging turf; (2) the Controller Map popup/controls screen says 'Right stick hold up: teleport aim' but both controllers have teleport rays and the left stick up also aims/teleports in Teleport mode; (3) snap turns and teleport landings have no haptic tick (teleport plays a footstep audio cue only), so the most significant discrete viewpoint changes have the least tactile confirmation; (4) BlockiverseControllerHaptics.SendImpulse allocates a new List and re-queries InputDevices on every impulse.
- **Files:** ["Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs", "Assets/Blockiverse/Scripts/Gameplay/SurvivalFeedbackBridge.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs", "Assets/Blockiverse/Scripts/VR/BlockiverseControllerHaptics.cs"]
- **Evidence:** BlockiverseCreativeInputBridge.PlayMineStrikeFeedback (line 139) hardcodes ToolHitSoft; SurvivalFeedbackBridge line 132 likewise; grep ToolHitStone shows only enum/clip-loading references. ControllerMappingText (bootstrapper lines 4382-4391) lists teleport under 'Right stick' only, while EnsureControllerInteractors gives both hands teleport rays and AddControllerMap adds Teleport Mode/Select composites to both maps. BlockiverseInputRig.PlayTeleportCue (lines 809-815) plays only an audio Footstep cue; no haptic call exists on snapTurnProvider/teleportationProvider events. BlockiverseControllerHaptics.SendImpulse lines 54-55 allocate per call.
- **RecommendedFix:** Pick the strike cue from the target block's material category (registry already distinguishes stone-like blocks). Update ControllerMappingText to mention both sticks teleport (or restrict teleport to the dominant hand). Add a light haptic on teleport end and optionally snap turn via BlockiverseControllerHaptics. Cache the InputDevice lookup in BlockiverseControllerHaptics and refresh on device change.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["vr-interaction"]
- **MergedCount:** 1

## 178. No hand-tracking input path; controllers are mandatory

- **Severity:** Informational
- **Impact:** Players who cannot hold or operate Touch controllers (limb difference, grip impairment) have no alternative input: all bindings are `<XRController>` paths and no OpenXR hand-tracking feature/subsystem is configured. Worth recording as a known limitation for store metadata and the roadmap rather than an immediate defect.
- **Files:** ["Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs", "Packages/manifest.json", "Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs"]
- **Evidence:** All generated input actions bind `<XRController>{LeftHand}/...` / `{RightHand}/...` (BlockiverseProjectBootstrapper.cs lines 1060–1062 and BlockiverseInputRig pose paths lines 31–40). Grep for hand-tracking configuration across ProjectSettings/ and Packages/manifest.json returns nothing.
- **RecommendedFix:** Document 'controllers required' in the wiki/store listing now. If hand tracking is ever added, the XRI ray interactors and the existing UI-first menu design would carry over; gameplay bindings would need pinch-gesture equivalents defined in the bootstrapper's input-action generation.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["accessibility"]
- **MergedCount:** 1

## 179. No host migration; host disconnect ends the session for everyone (by design, with progress bounded by the host's last save)

- **Severity:** Informational
- **Impact:** Confirmed absent: when the host stops or drops, all clients are disconnected, see the 'LAN session ended because the host disconnected' UX, and can only rejoin when the host returns. World progress survives only to the host's last autosave (300 s cadence) or orderly shutdown save; client-side progress since the last host inventory snapshot is host-side anyway. For 2-3 player LAN co-op this is a reasonable scope cut, but combined with the inventory-persistence gap it currently means total item loss on host crash.
- **Files:** ["Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkSession.cs", "Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs", "Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs"]
- **Evidence:** No migration code exists anywhere (no ownership transfer, no host-candidate logic). Client UX: IsShowingSessionEndedMessage / DescribeDisconnectedState (BlockiverseMultiplayerSessionMenu.cs:43-45, 250-261). Host autosave cadence: WorldSaveService.AutoSaveIntervalSeconds = 300 (WorldSaveService.cs:197) used by MultiplayerWorldPersistence.Update (line 253).
- **RecommendedFix:** No action required for the current scope; document the behavior in the wiki's multiplayer page (host owns the world; host crash loses up to 5 minutes). If host crashes prove common on Quest (battery/thermal), consider shortening the multiplayer autosave interval.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["lan-multiplayer"]
- **MergedCount:** 1

## 180. No localization system exists: no package, no string tables, no language setting, no locale detection

- **Severity:** Informational
- **Impact:** The game ships English-only with no seam to add a second language. The design docs already anticipate one (voxel_save_versioning_schema.md §18 reserves `language?: string` in LocalSessionSave) and the store listing has an unfilled placeholder, but nothing in code reads or stores a language. Every translated-release plan starts from zero infrastructure.
- **Files:** ["Packages/manifest.json", "docs/rulesets/voxel_save_versioning_schema.md", "docs/store-submission/store-listing.md"]
- **Evidence:** Packages/manifest.json contains no com.unity.localization (only Meta XR, XRI, Netcode, URP, etc.). Project-wide grep for `Localization|LocalizedString|LocaleIdentifier` over Assets/Blockiverse/Scripts and ProjectSettings returns zero hits; grep for `systemLanguage|CurrentCulture|SetCulture` returns zero hits. docs/rulesets/voxel_save_versioning_schema.md:876 defines `language?: string` in LocalSessionSave (unimplemented). docs/store-submission/store-listing.md:55: "Interface language(s): <English / others>".
- **RecommendedFix:** Decide the localization strategy explicitly. If localizing: add com.unity.localization (or, given the engine-free assembly constraint, a small plain-C# string-table service in Core keyed by string ids with per-locale tables loaded by UI), add a language entry to the Settings screen, and persist it via the already-specified LocalSessionSave.language field. If staying English-only for 1.0, fill in the store-listing Languages section accordingly and record the decision in docs/adr/.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["localization"]
- **MergedCount:** 1

## 181. Production chunk material is named 'BlockiverseTestBlock'

- **Severity:** Informational
- **Impact:** The material serialized into Boot.unity's VoxelWorldRenderer.chunkMaterial — the material that carries the authored atlas into the build and the runtime material pipeline — is Assets/Blockiverse/Materials/BlockiverseTestBlock.mat (constant BlockiverseProject.TestBlockMaterialPath). The 'Test' name misleadingly suggests a placeholder; it is also the asset a fix for the voxel-shader finding would touch, so the naming actively invites accidental deletion or exclusion.
- **Files:** ["Assets/Blockiverse/Materials/BlockiverseTestBlock.mat", "Assets/Blockiverse/Scenes/Boot.unity", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** Boot.unity line 168: `chunkMaterial: {fileID: 2100000, guid: 4d5cd674d4ec742bfa27f01c9b1f16fe...}` resolves to BlockiverseTestBlock.mat.meta; the material's _BaseMap is the authored atlas (guid 90a6fc9b496045d7ad07d8e02954ce10); bootstrapper line 929 EnsureMaterial(BlockiverseProject.TestBlockMaterialPath, ...).
- **RecommendedFix:** Rename the asset and constant to something like BlockiverseChunkAtlas.mat / ChunkMaterialPath via the bootstrapper (preserving the GUID by renaming the file+meta pair, not recreating it), ideally in the same change that binds the Voxel Lit shader to it.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["asset-integration"]
- **MergedCount:** 1

## 182. Sapling blocks have no harvest rule — placed saplings cannot be removed in survival

- **Severity:** Informational
- **Impact:** BlockHarvestRuleSet.CreateDefault registers rules for every crop stage and plant but none for Sapling/Sapling_S1/Sapling_S2, so TryPreviewHarvest returns NoHarvestRule and survival players cannot break a sapling (relevant for creative-built worlds later played in survival, or if a sapling item is added). There is also no sapling ItemDefinition, so saplings are currently creative-catalog-only.
- **Files:** ["Assets/Blockiverse/Scripts/Survival/BlockHarvestRules.cs", "Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs"]
- **Evidence:** BlockHarvestRuleSet.CreateDefault (BlockHarvestRules.cs:76-169) contains no BlockRegistry.Sapling* entries; ItemRegistry.CreateDefault has no sapling item or drop alias, so RegisterForBlock could not even resolve a drop today.
- **RecommendedFix:** Add a sapling item (or alias the drop to an existing resource) plus Hand/Sickle harvest rules for the three sapling stages when saplings become player-obtainable.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["game-logic"]
- **MergedCount:** 1

## 183. Test-only public API surface across runtime assemblies (12 members) plus unused constant WorldConstants.WorldMinY

- **Severity:** Informational
- **Impact:** The following public members are referenced exclusively by test code (no runtime, editor, or YAML consumer), so production behavior is not what the tests exercise and the members could be trimmed or annotated as intentional seams: BlockMutationAuthority.CreateClientProxy and ChunkAuthorityBoundary.OwnsChunkGeneration (Voxel — runtime clients never construct a client-proxy authority; MultiplayerChunkAuthoritySync nulls mutationAuthority for clients); WorldGenerationSettings.CreateDefaultCreative (runtime uses CreateDefaultSurvivalLite via CreativeWorldManager.CreateDefaultGeneratedWorld); CraftingRecipeBook.GetByOutput; SurvivalCraftingPanel.TryCraftByOutput; UiScreenRouter.ReplaceScreen; BlockiverseNetworkConfig.WithAddress; BlockiverseNetworkAvatarRig.SetLocalRigPose; TorchbudLightManager.TryGetLight; FarmingService.HasPendingRegrowth; BlockiverseAudioCuePlayer.HasClipForCue and IsLoopActive; CreativeHotbar.SelectNext (covered in the dead-methods finding). WorldConstants.WorldMinY has zero references anywhere. Explicitly-named seams (BlockiverseLog.SetSinkForTesting/ResetSinkForTesting, BlockiverseVfxPool.ConfigureForTests) are fine and excluded. Classification: only used in tests.
- **Files:** ["Assets/Blockiverse/Scripts/Voxel/ChunkAuthority.cs", "Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs", "Assets/Blockiverse/Scripts/WorldGen/WorldConstants.cs", "Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs", "Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs", "Assets/Blockiverse/Scripts/UI/UiScreenRouter.cs", "Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkConfig.cs", "Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkAvatarRig.cs", "Assets/Blockiverse/Scripts/Gameplay/TorchbudLightManager.cs", "Assets/Blockiverse/Scripts/Survival/FarmingService.cs", "Assets/Blockiverse/Scripts/Gameplay/BlockiverseAudioCuePlayer.cs"]
- **Evidence:** Method-level reference census (382 distinctive public methods, grep -rlw across Assets) shows src=0/test>=1 with no in-file caller for each listed member; e.g. ChunkAuthority.cs:239 CreateClientProxy (2 test files only), WorldGenerationSettings.cs:8 CreateDefaultCreative (WorldSaveEditModeTests only, runtime path is CreativeWorldManager.cs:719→CreateDefaultSurvivalLite), WorldConstants.cs:6 `public const int WorldMinY = 0;` with zero references repo-wide.
- **RecommendedFix:** Triage each member: delete those that exist only to make a test compile, or rename/mark intentional seams (the codebase already uses an explicit 'ForTests' naming convention — follow it). Delete WorldConstants.WorldMinY or use it where 0 is hard-coded as the world floor.
- **Confidence:** High
- **NeedsManualVerification:** false
- **Sources:** ["dead-code"]
- **MergedCount:** 1

## 184. XR rig prefab carries a NetworkBehaviour (BlockiverseNetworkAvatarRig) with no NetworkObject

- **Severity:** Informational
- **Impact:** Intentional-looking but worth recording: the local rig's BlockiverseNetworkAvatarRig is never spawnable (the rig prefab contains no NetworkObject — NGO's NetworkObject script GUID d5a57f767e5e46a458fc5d3c628d0cbb appears only in BlockiverseNetworkPlayer.prefab). The component guards all network paths behind IsSpawned (LateUpdate line 80, SetLocalRigPose line 128) and serves purely as the local fallback-proxy pose driver, so behavior is correct, but Netcode tooling/validation may flag the orphan NetworkBehaviour and future edits that touch its NetworkVariable while unspawned would throw.
- **Files:** ["Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab", "Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkAvatarRig.cs", "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs"]
- **Evidence:** BlockiverseXRRig.prefab contains guid 44b2c1b7d98f4a0a9f2a5e1b7c3d9e10 (BlockiverseNetworkAvatarRig, added by EnsureXrRigAvatar, bootstrapper line 2045) but zero occurrences of the NetworkObject script guid; BlockiverseNetworkAvatarRig.cs gates network access on IsSpawned (lines 80-95, 128).
- **RecommendedFix:** Either split the local pose-proxy behavior into a plain MonoBehaviour the rig uses (with the NetworkBehaviour only on the network player prefab), or document the unspawned-by-design contract on the class to prevent someone 'fixing' it by adding a NetworkObject to the rig.
- **Confidence:** Medium
- **NeedsManualVerification:** false
- **Sources:** ["runtime-wiring"]
- **MergedCount:** 1
