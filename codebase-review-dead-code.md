# Codebase Review — Dead Code and Unused Code Expert

> Workflow run `wf_53b36881-009`, agent `a56554e5df9255cc0`. Raw expert output, pre-verification.

## Area Reviewed

Dead/unused code review of the full Blockiverse-VR repo: all 254 public/internal types and 382 distinctive public methods under Assets/Blockiverse/Scripts/ were reference-traced against other source, tests, editor tooling, shell/CI scripts, and scene/prefab/asset YAML (via .cs.meta GUIDs and UnityEvent m_MethodName bindings); all generated art (241 PNGs) and audio (34 WAVs) assets were GUID-traced; build scene lists, stray scenes, and missing-script GUIDs were audited. Overall the codebase is unusually clean for its size — no commented-out code, no [Obsolete] members, no orphaned script GUIDs in committed scenes/prefabs, no parallel legacy systems, and almost all constants/audio assets are live. The real issues cluster in three areas: (1) a custom voxel shader that is reachable only via Shader.Find and referenced by no built asset, so it is likely stripped from device builds; (2) a stale generated XR-rig prefab missing four item icons that exist on disk; and (3) a tail of definitely-dead public methods, a dead bootstrapper scene path with its orphaned component and material, generated 2D art (UI/VFX sprites) that exists only to satisfy existence tests, and a stray committed Unity Test Runner scene.

## Findings (15)

### 1. Custom voxel shader is referenced only via Shader.Find and will be stripped from device builds

- **Severity:** High  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader`, `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`, `ProjectSettings/GraphicsSettings.asset`, `Assets/Blockiverse/Materials/BlockiverseTestBlock.mat`
- **Impact:** BlockVisualAtlas.CreateBaseMaterial prefers Shader.Find("Blockiverse/Voxel Lit") and silently falls back to the source material's shader when not found. The shader asset (Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader, guid 0447044b0998a413fa33570b9cb06621) is referenced by zero materials/scenes/prefabs (all committed materials use URP Lit guid 933532a4fcc9baf4fa0491de14d08ed7) and is not in GraphicsSettings m_AlwaysIncludedShaders. Unity strips shaders with no asset references from player builds, so on Quest the custom voxel lighting shader never runs — editor and device render with different shaders, and the entire shader file is effectively dead code in shipped builds.
- **Evidence:** Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs:16 `public const string VoxelLitShaderName = "Blockiverse/Voxel Lit";` and :212 `Shader voxelShader = Shader.Find(VoxelLitShaderName);` with fallback chain to sourceMaterial.shader / URP Lit. GUID grep of 0447044b0998a413fa33570b9cb06621 across all .mat/.unity/.prefab/.asset returns nothing; ProjectSettings/GraphicsSettings.asset m_AlwaysIncludedShaders (lines 29–36) contains only built-in shaders. Chunk material wired by the bootstrapper is BlockiverseTestBlock.mat whose m_Shader is URP Lit (933532a4fcc9baf4fa0491de14d08ed7).
- **Recommended fix:** Make the shader a real asset reference: either have the bootstrapper create/commit the chunk material with BlockiverseVoxelLit as its shader (so VoxelWorldRenderer's serialized material carries the reference), or add the shader to GraphicsSettings Always Included Shaders via the bootstrapper. Then verify on-device that the voxel shader path is taken. If the fallback rendering is actually the intended shipped look, delete the shader and the Shader.Find branch instead.

### 2. Committed XR rig prefab's item icon library is stale — missing 4 item icons that exist on disk

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab`, `Assets/Blockiverse/Art/Textures/Items/deepmantle.png`, `Assets/Blockiverse/Art/Textures/Items/frostglass.png`, `Assets/Blockiverse/Art/Textures/Items/snowpack.png`, `Assets/Blockiverse/Art/Textures/Items/worldroot.png`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- **Impact:** The items snowpack, frostglass, worldroot, and deepmantle (all registered in ItemId.cs lines 32–40) have icon PNGs in Assets/Blockiverse/Art/Textures/Items/, but the committed BlockiverseXRRig.prefab's BlockiverseItemIconLibrary holds only 138 of the 142 icons and lacks these four. SurvivalInventoryPanel falls back to text-only slots when an icon is missing (SurvivalInventoryPanel.cs:89), so these items render without icons in the inventory/crafting UI on shipped builds. Classification: used indirectly via Unity serialization — the wiring is stale because the bootstrapper was not rerun after the icons were generated.
- **Evidence:** Items folder contains 142 PNGs; parsing the BlockiverseItemIconLibrary component (script guid 2a6f8c4e1b9d45072e3a5d8f0c7b1964) inside BlockiverseXRRig.prefab yields itemIds count = 138 and 138 sprite guid refs, with 'deepmantle', 'frostglass', 'snowpack', 'worldroot' absent. EnsureItemIconLibrary (BlockiverseProjectBootstrapper.cs:2885) populates the library from every Texture2D in that folder, so a rerun would include them. The four ids are real items (Assets/Blockiverse/Scripts/Survival/ItemId.cs:32–40).
- **Recommended fix:** Rerun the bootstrapper (menu: Blockiverse → Bootstrap Unity Quest Project) and commit the regenerated BlockiverseXRRig.prefab so the icon library includes all 142 icons. Consider an EditMode test asserting the prefab's icon library covers every PNG in the Items folder (or every registry item with an icon on disk) to catch future staleness.

### 3. Dead bootstrapper scene path: EnsureBootSceneInteractionTestBlock is never called, leaving BlockiverseHighlightTarget and BlockiverseHighlight.mat orphaned

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseHighlightTarget.cs`, `Assets/Blockiverse/Materials/BlockiverseHighlight.mat`, `Assets/Blockiverse/Tests/PlayMode/BlockiverseInteractionPlayModeTests.cs`
- **Impact:** A whole feature chain is dead: the bootstrapper method that created the 'Interaction Test Block' is uncalled (the bootstrapper now actively removes that object from the Boot scene), the BlockiverseHighlightTarget MonoBehaviour it wired appears in no scene or prefab and has no runtime caller, and BlockiverseHighlight.mat is still regenerated on every bootstrap run but referenced by nothing. A PlayMode test still exercises the component by adding it to a self-created object, testing behavior the shipped game never uses. ~80 lines of editor code, a runtime component, a generated material, and a test are all removable. Classification: EnsureBootSceneInteractionTestBlock = definitely unused; BlockiverseHighlightTarget = only used in tests; BlockiverseHighlight.mat = definitely unused (but kept alive by editor tooling).
- **Evidence:** BlockiverseProjectBootstrapper.cs:1599 defines EnsureBootSceneInteractionTestBlock; grep shows no call site, and EnsureBootScene at line 1127 calls `RemoveRootGameObject(scene, InteractionTestBlockName)` instead. BlockiverseHighlightTarget guid c618760c5a164cc090aa34ceaf345005 appears in no .unity/.prefab/.asset; its only references are the dead bootstrapper method (line 1628) and BlockiverseInteractionPlayModeTests.cs:34 (`firstObject.AddComponent<BlockiverseHighlightTarget>()`). BlockiverseHighlight.mat guid d1257df724dcf4ea3a018ab73b6f2fdd has zero scene/prefab references but is recreated by EnsureMaterial at BlockiverseProjectBootstrapper.cs:717 and loaded only at the dead line 1623.
- **Recommended fix:** Delete EnsureBootSceneInteractionTestBlock, the EnsureMaterial(HighlightMaterialPath, …) call at line 717, BlockiverseHighlightTarget.cs, BlockiverseHighlight.mat, the HighlightMaterialPath/HighlightColor constants, and the highlight-target PlayMode test case. Keep the RemoveRootGameObject cleanup call for one release so existing generated scenes are scrubbed.

### 4. Generated UI and VFX sprite assets are produced and test-enforced but never wired into the game

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Art/Sprites/UI/`, `Assets/Blockiverse/Art/Sprites/VFX/`, `Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxPool.cs`, `Assets/Blockiverse/Tests/EditMode/M4ArtAssetValidationEditModeTests.cs`, `scripts/art/generate-art-assets.py`
- **Impact:** 16 generated 2D assets — 8 UI sprites (crafting_panel, health_pip, hotbar_frame, inventory_panel, multiplayer_status_badge, selected_slot, settings_panel, feedback_toast) and 8 VFX particle sprites (block_dust_particle, block_puff_particle, craft_spark_particle, ember_particle, fog_wisp_particle, rain_splash_particle, resource_spark_particle, snowflake_particle) — are referenced by zero scenes/prefabs/materials. Their only consumers are the art generator and M4ArtAssetValidationEditModeTests, which asserts they exist. The world-space UI is built from plain Image/TMP elements and BlockiverseVfxPool renders untextured 'Sprites/Default' particles, so either an intended art pass never landed (players see plainer UI/VFX than the art pipeline produces) or the assets and their validation tests are dead weight. Classification: only used in tests.
- **Evidence:** GUID grep of every PNG under Assets/Blockiverse/Art/Sprites/{UI,VFX} across all .unity/.prefab/.asset/.mat files returns nothing; name grep hits only scripts/art/generate-art-assets.py, scripts/art/test_generate_art_assets.py, and M4ArtAssetValidationEditModeTests.cs. BlockiverseVfxPool.cs:75–77 uses `particleMaterial != null ? particleMaterial : new Material(Shader.Find("Sprites/Default"))` with no texture from these sprites, and no committed material references them.
- **Recommended fix:** Decide per group: either wire the sprites in (assign UI sprites in the bootstrapper's panel construction, give BlockiverseVfxPool per-cue textured materials) or remove them from the generator, delete the assets, and drop the corresponding existence assertions from M4ArtAssetValidationEditModeTests. Today the tests guarantee art that ships unused.

### 5. Stray Unity Test Runner scene committed at Assets root (InitTestScene8a89a79c-…)

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity`, `Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity.meta`, `scripts/ci/forbidden-files.sh`
- **Impact:** Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity is the auto-generated scene the Unity Test Runner creates when entering play mode for tests; it was accidentally committed (in commit be5ec24) and serves no purpose. It is not in EditorBuildSettings so it does not ship, but it pollutes the Assets root, confuses the 'single Boot scene' architecture, and the forbidden-files CI gate does not catch this class of artifact. Classification: definitely unused — safe to delete.
- **Evidence:** `git ls-files` tracks the scene and its .meta; the scene's only script GUID (102e512f651ee834f951a2516c1ea3b8) resolves to com.unity.test-framework's PlaymodeTestsController. No reference to 'InitTestScene' exists anywhere in Assets, scripts, .github, or ProjectSettings; EditorBuildSettings.asset lists only Boot and MultiplayerTest. forbidden_regex in scripts/ci/forbidden-files.sh ('^(Library|Temp|Logs|UserSettings)/|…') does not match it.
- **Recommended fix:** Delete the scene and its .meta. Add a pattern such as `(^|/)InitTestScene[0-9a-f-]*\.unity(\.meta)?$` to forbidden_regex in scripts/ci/forbidden-files.sh so the test runner's transient scenes can never be committed again.

### 6. Definitely-dead public methods: ConfigureWorld, InitializeAuthoritativeWorldSnapshot, TrackChangedBlock, ResetToFullHealth, CanStackWith, SelectPrevious, ApplyRemoteStreamForTests

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Voxel/VoxelWorld.cs`, `Assets/Blockiverse/Scripts/SurvivalHealth/PlayerVitals.cs`, `Assets/Blockiverse/Scripts/Survival/ItemStack.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeHotbar.cs`, `Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamRelay.cs`
- **Impact:** Seven public methods have zero callers anywhere (source, tests, editor tooling, UnityEvent YAML bindings) and are safe to delete; several also duplicate or contradict the live path, which misleads maintainers: MultiplayerChunkAuthoritySync.ConfigureWorld duplicates a subset of the live Configure(...); CreativeWorldManager.InitializeAuthoritativeWorldSnapshot looks like a superseded late-join world-install API; VoxelWorld.TrackChangedBlock duplicates the inline delta tracking inside SetBlock (changedBlocks[position] at line 67); PlayerVitals.ResetToFullHealth would respawn at BlockPosition(0,0,0) if anyone ever called it; ItemStack.CanStackWith is an unused query; CreativeHotbar.SelectPrevious has no caller (and SelectNext is test-only — runtime hotbar selection happens exclusively through SelectBlock from the catalog browser/pick-block); MetaAvatarStreamRelay.ApplyRemoteStreamForTests is a 'ForTests' seam that not even tests use. Classification: definitely unused.
- **Evidence:** Repo-wide `grep -rnw` for each name returns only the declaration: MultiplayerChunkAuthoritySync.cs:89 ConfigureWorld; CreativeWorldManager.cs:370 InitializeAuthoritativeWorldSnapshot; VoxelWorld.cs:109 TrackChangedBlock (inline tracking at VoxelWorld.cs:67 is the live path); PlayerVitals.cs:93 ResetToFullHealth (`RespawnAt(new BlockPosition(0, 0, 0))`); ItemStack.cs:34 CanStackWith; CreativeHotbar.cs:118 SelectPrevious (external consumers use only hotbar.SelectBlock/SelectedBlockId/SelectionChanged; no m_MethodName YAML binding to SelectNext/SelectPrevious exists); MetaAvatarStreamRelay.cs:59 ApplyRemoteStreamForTests.
- **Recommended fix:** Delete all seven methods. If hotbar next/previous cycling is intended as a future controller binding (no input action exists for it in BlockiverseInputActionNames), keep SelectNext/SelectPrevious but track the missing input wiring as a feature task instead of leaving silent dead API.

### 7. WorldSaveService.ShouldAutoSave is test-only while the autosave check is duplicated inline at two call sites

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`, `Assets/Blockiverse/Tests/EditMode/WorldSaveEditModeTests.cs`
- **Impact:** The single API meant to encapsulate the autosave decision is called only by WorldSaveEditModeTests; both runtime autosave drivers re-implement the comparison inline against WorldSaveService.AutoSaveIntervalSeconds. The tests therefore verify a code path production does not execute, and a future change to autosave policy must be made in three places. Classification: only used in tests / duplicate implementation.
- **Evidence:** WorldSaveService.cs:207 declares ShouldAutoSave; only references are WorldSaveEditModeTests.cs:136–139. Runtime checks are inline: BlockiverseWorldSessionController.cs:103 `if (Time.unscaledTime - lastSaveTime < WorldSaveService.AutoSaveIntervalSeconds)` and MultiplayerWorldPersistence.cs:253 `if (Time.unscaledTime - lastAutoSaveTime < WorldSaveService.AutoSaveIntervalSeconds)`.
- **Recommended fix:** Route both runtime call sites through WorldSaveService.ShouldAutoSave(elapsedSeconds) (it is engine-free and takes elapsed seconds, so both can use it), or delete the method and its test if the inline checks are preferred.

### 8. SurvivalVitals.TrySpendStamina is test-only — no gameplay system ever spends stamina

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs`, `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`, `Assets/Blockiverse/Tests/EditMode/SurvivalHealth/SurvivalVitalsEditModeTests.cs`
- **Impact:** Stamina ticks, restores, is persisted, and is displayed in the HUD and health panel, but the only spend API has no runtime caller, so stamina can never decrease through player actions. Either a planned mechanic (sprint/jump/mining costs) is unimplemented and the HUD shows a stat that never moves downward, or the spend API plus its tests are dead. Classification: only used in tests.
- **Evidence:** SurvivalVitals.cs:96 declares TrySpendStamina; repo-wide grep shows callers only in SurvivalVitalsEditModeTests.cs:46–53. SurvivalVitalsRuntime reads/restores Stamina (lines 228, 247) and the HUD displays it (SurvivalHudController.cs:183, SurvivalHealthPanel.cs:95) but nothing invokes a spend.
- **Recommended fix:** Check the survival ruleset for intended stamina costs; if costs are specced, wire TrySpendStamina into the relevant actions (and flag the gap as a gameplay task). If stamina is display-only by design, remove TrySpendStamina and its tests.

### 9. Experimental Rules toggle from the menus ruleset is unimplemented — NewWorldConfig.ToggleExperimentalRules/ExperimentalRulesEnabled are test-only vestiges

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs`, `Assets/Blockiverse/Scripts/UI/MenuActions.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseNewWorldPanel.cs`, `docs/rulesets/voxel_survival_menus.md`, `Assets/Blockiverse/Tests/EditMode/NewWorldConfigEditModeTests.cs`
- **Impact:** docs/rulesets/voxel_survival_menus.md specifies a New World 'Experimental Rules' toggle (action id new_world.toggle_experimental), but MenuActions contains no such constant, BlockiverseNewWorldPanel exposes no control for it, no scene/prefab UnityEvent binds it, and ExperimentalRulesEnabled is never read by world creation. The model-level method exists solely so NewWorldConfigEditModeTests can flip it. Classification: only used in tests (unwired planned feature).
- **Evidence:** NewWorldConfig.cs:34 `public bool ExperimentalRulesEnabled { get; private set; }` and :60 `public void ToggleExperimentalRules() => …` — repo-wide grep finds only NewWorldConfigEditModeTests.cs:91/93 as callers and no reader of ExperimentalRulesEnabled. grep 'toggle_experimental' in Assets returns nothing; the ruleset names it at docs/rulesets/voxel_survival_menus.md:349.
- **Recommended fix:** Either implement the toggle (MenuActions constant, panel control wired by the bootstrapper, consume the flag in world creation) to match the ruleset, or remove the property/method/test and annotate the ruleset row as not-yet-implemented so the canonical design and code agree.

### 10. StructureTerrainFit enum is declared and never used anywhere

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/WorldGen/StructureService.cs`
- **Impact:** Dead public enum in the WorldGen assembly; readers will assume structures support SnapToSurface/Flatten terrain-fit modes when no such system exists. Safe to delete. Classification: definitely unused.
- **Evidence:** StructureService.cs:8 `public enum StructureTerrainFit  { SnapToSurface, Flatten }`; repo-wide `grep -rnw StructureTerrainFit Assets` returns only this declaration (its sibling StructureDegradation at line 7 is heavily used; this one is not).
- **Recommended fix:** Delete the enum, or implement the terrain-fit behavior in StructureService.PlaceRuin if it was planned.

### 11. Seven per-assembly marker classes are unreferenced placeholders

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/GameplayAssembly.cs`, `Assets/Blockiverse/Scripts/Networking/NetworkingAssembly.cs`, `Assets/Blockiverse/Scripts/Persistence/PersistenceAssembly.cs`, `Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalHealthAssembly.cs`, `Assets/Blockiverse/Scripts/UI/UIAssembly.cs`, `Assets/Blockiverse/Scripts/Voxel/VoxelAssembly.cs`, `Assets/Blockiverse/Scripts/WorldGen/WorldGenAssembly.cs`
- **Impact:** GameplayAssembly, NetworkingAssembly, PersistenceAssembly, SurvivalHealthAssembly, UIAssembly, VoxelAssembly, and WorldGenAssembly are static classes holding (at most) a Name constant; none is referenced anywhere in source, tests, or tooling, and SurvivalHealthAssembly is an entirely empty internal class. Five of the twelve assemblies (Core, Survival, VR, MetaAvatars, Editor) have no such marker, so the pattern is also inconsistent — they appear to be leftover asmdef-seed placeholders. Classification: definitely unused.
- **Evidence:** Each file contains only `public static class XAssembly { public const string Name = "…"; }` (SurvivalHealthAssembly.cs is `internal static class SurvivalHealthAssembly { }`). Repo-wide grep for each class name and for any `Assembly.Name` constant usage returns zero references outside the declaring files. Every assembly contains other compiled files, so deletion cannot empty an asmdef.
- **Recommended fix:** Delete all seven marker files (each assembly has plenty of other sources, so the asmdefs stay valid).

### 12. PerformanceStatsOverlay and FrameStatisticsSampler have no runtime or scene wiring — manual-attach dev tooling only

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/PerformanceStatsOverlay.cs`, `Assets/Blockiverse/Scripts/Core/FrameStatisticsSampler.cs`, `docs/testing/performance/README.md`, `docs/testing/performance/report-template.md`
- **Impact:** PerformanceStatsOverlay (Gameplay) has zero C# references and appears in no scene/prefab; FrameStatisticsSampler (Core) is referenced only by that overlay and its own EditMode test. Both compile into the shipped player but are unreachable unless a developer hand-attaches the overlay in the editor — which is exactly what docs/testing/performance/ instructs, so this is intentional dev tooling rather than rot. Worth recording because the docs say 'enable PerformanceStatsOverlay' yet no menu item, debug flag, or scene object can enable it on a device build. Classification: definitely unused at runtime; documented manual editor tooling.
- **Evidence:** PerformanceStatsOverlay guid 41c4cf1aef1d8e774d101b4493bfc1cb appears in no .unity/.prefab/.asset; repo-wide grep for the class name hits only docs and scripts/store/validate-store-submission-docs.sh (which greps the docs, not the code). FrameStatisticsSampler's only non-test reference is PerformanceStatsOverlay.cs. docs/testing/performance/README.md:14 describes it as the in-headset HUD.
- **Recommended fix:** If in-headset perf HUD passes are still part of the release checklist, add a guarded way to enable it in development builds (e.g. the bootstrapper adds it disabled, or a debug menu action), since the current workflow only works in the editor. Otherwise move both classes' usage expectation out of the perf docs or accept them as editor-only tooling.

### 13. Test-only public API surface across runtime assemblies (12 members) plus unused constant WorldConstants.WorldMinY

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Voxel/ChunkAuthority.cs`, `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs`, `Assets/Blockiverse/Scripts/WorldGen/WorldConstants.cs`, `Assets/Blockiverse/Scripts/Survival/CraftingRecipeBook.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs`, `Assets/Blockiverse/Scripts/UI/UiScreenRouter.cs`, `Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkConfig.cs`, `Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkAvatarRig.cs`, `Assets/Blockiverse/Scripts/Gameplay/TorchbudLightManager.cs`, `Assets/Blockiverse/Scripts/Survival/FarmingService.cs`, `Assets/Blockiverse/Scripts/Gameplay/BlockiverseAudioCuePlayer.cs`
- **Impact:** The following public members are referenced exclusively by test code (no runtime, editor, or YAML consumer), so production behavior is not what the tests exercise and the members could be trimmed or annotated as intentional seams: BlockMutationAuthority.CreateClientProxy and ChunkAuthorityBoundary.OwnsChunkGeneration (Voxel — runtime clients never construct a client-proxy authority; MultiplayerChunkAuthoritySync nulls mutationAuthority for clients); WorldGenerationSettings.CreateDefaultCreative (runtime uses CreateDefaultSurvivalLite via CreativeWorldManager.CreateDefaultGeneratedWorld); CraftingRecipeBook.GetByOutput; SurvivalCraftingPanel.TryCraftByOutput; UiScreenRouter.ReplaceScreen; BlockiverseNetworkConfig.WithAddress; BlockiverseNetworkAvatarRig.SetLocalRigPose; TorchbudLightManager.TryGetLight; FarmingService.HasPendingRegrowth; BlockiverseAudioCuePlayer.HasClipForCue and IsLoopActive; CreativeHotbar.SelectNext (covered in the dead-methods finding). WorldConstants.WorldMinY has zero references anywhere. Explicitly-named seams (BlockiverseLog.SetSinkForTesting/ResetSinkForTesting, BlockiverseVfxPool.ConfigureForTests) are fine and excluded. Classification: only used in tests.
- **Evidence:** Method-level reference census (382 distinctive public methods, grep -rlw across Assets) shows src=0/test>=1 with no in-file caller for each listed member; e.g. ChunkAuthority.cs:239 CreateClientProxy (2 test files only), WorldGenerationSettings.cs:8 CreateDefaultCreative (WorldSaveEditModeTests only, runtime path is CreativeWorldManager.cs:719→CreateDefaultSurvivalLite), WorldConstants.cs:6 `public const int WorldMinY = 0;` with zero references repo-wide.
- **Recommended fix:** Triage each member: delete those that exist only to make a test compile, or rename/mark intentional seams (the codebase already uses an explicit 'ForTests' naming convention — follow it). Delete WorldConstants.WorldMinY or use it where 0 is hard-coded as the world floor.

### 14. MultiplayerTest.unity is a test-only scene kept in EditorBuildSettings (does not ship via the build scripts)

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scenes/MultiplayerTest.unity`, `ProjectSettings/EditorBuildSettings.asset`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs`, `Assets/Blockiverse/Tests/PlayMode/Networking/MultiplayerSessionPlayModeTests.cs`
- **Impact:** The scene is loaded only by PlayMode tests (SceneManager.LoadSceneAsync requires build-settings membership in the editor), and the bootstrapper's EnsureBuildScenes deliberately re-adds it. BlockiverseBuildSmoke passes scenes = { Boot } for both dev and release APKs, so it does not ship through the sanctioned pipeline — but any build made from Unity's File→Build menu would include this test scene. Classification: only used in tests (by design), with a footgun for non-script builds.
- **Evidence:** No runtime LoadScene/SceneManager call exists anywhere under Assets/Blockiverse/Scripts outside Editor/. EditorBuildSettings.asset lists the scene enabled at index 1; EnsureBuildScenes (BlockiverseProjectBootstrapper.cs:1290) includes BlockiverseProject.MultiplayerTestScenePath; BlockiverseBuildSmoke.cs:29 and :60 set `scenes = new[] { BlockiverseProject.BootScenePath }`; MultiplayerSessionPlayModeTests.cs:1889 loads it via SceneManager.LoadSceneAsync.
- **Recommended fix:** No action strictly required. To remove the footgun, mark the scene disabled in build settings and have the PlayMode tests load it with UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode (path-based, no build-settings requirement), then drop it from EnsureBuildScenes.

### 15. Assets/CompositionLayers/UserSettings (per-user preference assets) is committed to the repo

- **Severity:** Informational  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/CompositionLayers/UserSettings/CompositionLayersPreferences.asset`, `Assets/CompositionLayers/UserSettings/Resources/CompositionLayersRuntimeSettings.asset`, `scripts/ci/forbidden-files.sh`
- **Impact:** CompositionLayersPreferences.asset and CompositionLayersRuntimeSettings.asset under an Assets-level 'UserSettings' folder are auto-generated by the Unity Composition Layers package. Committing per-user preference assets invites noisy diffs across machines; the CI forbidden-files gate blocks the top-level UserSettings/ directory but not this Assets-embedded one. Nothing in Blockiverse code references them.
- **Evidence:** `git ls-files` tracks both assets and metas; forbidden_regex in scripts/ci/forbidden-files.sh matches `^(Library|Temp|Logs|UserSettings)/` only at repo root. No Blockiverse script references either asset; the package regenerates them on demand.
- **Recommended fix:** Verify the package tolerates their absence (the Resources/ runtime-settings asset may be required in builds if composition layers are used at runtime); if it regenerates them, untrack the preferences asset at minimum and consider a forbidden-files pattern for Assets/**/UserSettings/. Mark as needs-verification before deleting the Resources asset.

## What Looks Good (9)

- No commented-out code blocks anywhere under Assets/Blockiverse/Scripts (heuristic scan for commented statements found only prose comments), and no [Obsolete] members or InternalsVisibleTo usage.
- No missing-script references in committed scenes/prefabs: every m_Script GUID in Boot.unity, MultiplayerTest.unity, and all three Blockiverse prefabs resolves to a project script or a PackageCache script (verified against Library/PackageCache).
- All 34 generated audio assets (Assets/Blockiverse/Audio/*.wav) are referenced by GUID from committed prefabs/scenes — zero orphaned SFX/music.
- Constants classes are almost perfectly live: every constant in MenuActions.cs, BlockiverseInputActionNames.cs, GameModeConstants.cs, and BlockiverseProject.cs has at least one consumer (only WorldConstants.WorldMinY is dead).
- No parallel/legacy duplicate systems found: SmeltingModel vs SmeltingStationModel are complementary (fuel math vs station runtime), and the lighting stack (EnvironmentLightComputer → EnvironmentLightingSolver → LightingCycleEvaluator → BlockiverseLightingCycleController/Runtime, VoxelSkyLightMap/VoxelLightSampler) is a single coherent live chain.
- Runtime-added components are correctly alive despite absent scene wiring: BlockiverseVoidSafetyFloor (CreativeWorldManager.cs:708 AddComponent), TorchbudLightManager (CreativeWorldManager.cs:428), PlacementPreview (CreativeInteractionController), and BlockiverseTrackedPoseDriverLifecycle (BlockiverseInputRig.cs:254).
- BlockiverseBuildSmoke pins shipped builds to exactly the Boot scene (BlockiverseBuildSmoke.cs:29/60), so the test scene in EditorBuildSettings cannot leak into script-built APKs.
- Editor tooling entry points are all reachable: BlockiverseBuildSmoke methods are invoked via -executeMethod from scripts/unity/build-*.sh, and bootstrapper entry points carry [MenuItem] attributes (e.g. ImportTmpEssentialResources at BlockiverseProjectBootstrapper.cs:237, EnsureNetworkFoundationAssets at :221).
- Block source tiles (Assets/Blockiverse/Art/Textures/Blocks/Source/) are intentional generator inputs consumed by the atlas pipeline and validated by M4 tests — not orphans despite having no scene references.

## Could Not Review (5)

- Actual shader stripping on a Quest device build for BlockiverseVoxelLit.shader — the static evidence (no asset references, not in Always Included Shaders, Shader.Find at runtime) strongly implies it is stripped, but only a device/APK inspection can confirm which shader the chunks actually use.
- Whether PerformanceStatsOverlay is genuinely exercised during manual performance passes (process question, not answerable from code).
- Unused private fields/properties at full repo scale — the member-level census covered public types, public methods with distinctive (>=10 char) names, constants classes, and YAML UnityEvent bindings; short-named public methods (e.g. 4–9 character names) and private members were not exhaustively swept because common-word grep noise makes static counting unreliable.
- Whether the Composition Layers package requires the committed Assets/CompositionLayers/UserSettings assets in player builds (the Resources/ asset could be load-bearing at runtime if composition layers are active).
- Reflection-only invocation paths other than Unity lifecycle/Netcode codegen — none were observed (no Type.GetMethod/Invoke usage in Blockiverse code), but dynamic invocation from third-party packages cannot be fully excluded statically.

## Inspected (45)

- `Assets/Blockiverse/Scripts/ (all 140 .cs files, type and method reference census)`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeHotbar.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseMusicController.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxPool.cs`
- `Assets/Blockiverse/Scripts/Gameplay/PerformanceStatsOverlay.cs`
- `Assets/Blockiverse/Scripts/Core/FrameStatisticsSampler.cs`
- `Assets/Blockiverse/Scripts/Voxel/VoxelWorld.cs`
- `Assets/Blockiverse/Scripts/Voxel/ChunkAuthority.cs`
- `Assets/Blockiverse/Scripts/WorldGen/StructureService.cs`
- `Assets/Blockiverse/Scripts/WorldGen/WorldConstants.cs`
- `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs`
- `Assets/Blockiverse/Scripts/Survival/ItemStack.cs`
- `Assets/Blockiverse/Scripts/Survival/SmeltingModel.cs`
- `Assets/Blockiverse/Scripts/Survival/SmeltingStationModel.cs`
- `Assets/Blockiverse/Scripts/Survival/FarmingService.cs`
- `Assets/Blockiverse/Scripts/SurvivalHealth/PlayerVitals.cs`
- `Assets/Blockiverse/Scripts/SurvivalHealth/SurvivalVitals.cs`
- `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- `Assets/Blockiverse/Scripts/UI/NewWorldConfig.cs`
- `Assets/Blockiverse/Scripts/UI/MenuActions.cs`
- `Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamRelay.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseHighlightTarget.cs`
- `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab (YAML, incl. icon-library component)`
- `Assets/Blockiverse/Prefabs/Networking/BlockiverseNetworkManager.prefab`
- `Assets/Blockiverse/Prefabs/Networking/BlockiverseNetworkPlayer.prefab`
- `Assets/Blockiverse/Scenes/Boot.unity (script GUID audit)`
- `Assets/Blockiverse/Scenes/MultiplayerTest.unity (script GUID audit)`
- `Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity`
- `Assets/Blockiverse/Materials/*.mat (GUID trace)`
- `Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader`
- `Assets/Blockiverse/Art/ (241 PNG GUID trace)`
- `Assets/Blockiverse/Audio/ (34 WAV GUID trace)`
- `Assets/Blockiverse/Tests/EditMode + PlayMode (reference tracing, M4ArtAssetValidationEditModeTests, MultiplayerSessionPlayModeTests)`
- `ProjectSettings/EditorBuildSettings.asset`
- `ProjectSettings/GraphicsSettings.asset (Always Included Shaders)`
- `scripts/ci/forbidden-files.sh`
- `scripts/unity/build-development-apk.sh`
- `scripts/unity/build-release-apk.sh`
- `docs/rulesets/voxel_survival_menus.md (experimental rules spec)`
- `docs/testing/performance/README.md`
