# Codebase Review — Asset Integration Expert (first run)

> Workflow run `wf_53b36881-009`, agent `a6f98eca4d7f7d9fd`. Raw expert output, pre-verification.

## Area Reviewed

Asset integration for the full Blockiverse-VR project: registry-vs-asset coverage (BlockRegistry 79 blocks, ItemRegistry 96 items, CreativeCatalog vs Art/Textures and the committed 8x10 block atlas), the generated-asset pipeline (scripts/art/generate-art-assets.py, scripts/audio/generate-audio.py) against what the code expects, serialized reference integrity across every .unity/.prefab/.mat/.asset (18,785 known GUIDs, all references resolved), import settings (.meta) for Quest, audio wiring (34 clips), runtime asset-loading patterns (Shader.Find, icon library, Resources, StreamingAssets), and build inclusion (EditorBuildSettings vs BlockiverseBuildSmoke). Reference integrity, audio, and block/item texture coverage are in excellent shape — no broken GUIDs, no missing scripts, every canonical id has its texture, and the committed atlas is pixel-identical to the committed source tiles. The serious problems are pipeline-level: the custom voxel-lighting shader is not referenced by any build-included asset (so device builds silently lose all voxel lighting), and the committed art generator script is several milestones out of date — rerunning it per the documented workflow would produce a 128x112, 50-tile atlas that the runtime rejects outright. Secondary issues: the committed XR rig prefab's item-icon library is stale (4 registered items missing icons), and all 16 generated UI/VFX sprites are wired to nothing.

## Findings (11)

### 1. Blockiverse/Voxel Lit shader is not included in Android builds — all voxel lighting silently lost on device

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader`, `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`, `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`, `Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs`, `ProjectSettings/GraphicsSettings.asset`, `Assets/Blockiverse/Materials/BlockiverseTestBlock.mat`
- **Impact:** On Quest, Shader.Find("Blockiverse/Voxel Lit") returns null because the shader is referenced by no build-included asset and is not in Always Included Shaders. BlockVisualAtlas.CreateBaseMaterial then falls back to the source material's shader (URP/Lit), which ignores vertex colors — but the entire voxel light model (sky-light darkness in caves, emissive glow from glowwick/lumen lamp/spark flare/emberflow, smooth per-face light) is baked into vertex colors by ChunkMeshBuilder. The world also has shadowCastingMode Off/receiveShadows false because the code assumes vertex-color lighting. Result: in the shipped APK, caves render fully lit, glow blocks don't glow, and day/night surface light shading is reduced to the directional sun — while the Editor (where Shader.Find always works) looks correct, hiding the regression from desktop testing.
- **Evidence:** BlockVisualAtlas.cs:212 `Shader voxelShader = Shader.Find(VoxelLitShaderName);` with fallback to `sourceMaterial.shader` (lines 213–225). BlockiverseVoxelLit.shader:79 multiplies by `input.color` (the voxel light). ChunkMeshBuilder.cs:180 `Color vertexColor = VoxelLightSampler.ToVertexColor(light);` → VoxelWorldRenderer.cs:176 `mesh.SetColors(data.Colors)`. Repo-wide grep for the shader's GUID 0447044b0998a413fa33570b9cb06621 finds zero references in any .mat/.unity/.prefab/.asset; GraphicsSettings.asset m_AlwaysIncludedShaders (lines 29–36) contains only built-ins (guid 0000000000000000f000000000000000); BlockiverseTestBlock.mat (the chunk source material wired in Boot.unity:168) uses URP/Lit (guid 933532a4fcc9baf4fa0491de14d08ed7). No editor/build script touches AlwaysIncludedShaders (grep across Assets/Blockiverse/Scripts/Editor).
- **Recommended fix:** In BlockiverseProjectBootstrapper (so it survives regeneration), either (a) add BlockiverseVoxelLit.shader to GraphicsSettings Always Included Shaders, or (b) create/commit a material that uses the Voxel Lit shader, assign it as the chunk source material referenced by Boot.unity, and have BlockVisualAtlas use the source material's shader directly instead of Shader.Find. Add an EditMode test asserting the shader is reachable from build content (e.g., present in GraphicsSettings.GetGraphicsSettings always-included list).

### 2. scripts/art/generate-art-assets.py is milestones out of date — regenerating per documented workflow produces an atlas the runtime rejects

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `scripts/art/generate-art-assets.py`, `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`, `Assets/Blockiverse/Tests/EditMode/M4ArtAssetValidationEditModeTests.cs`
- **Impact:** CLAUDE.md instructs 'never hand-author; regenerate instead' via this script, but running it overwrites the correct committed 128x160 (8x10, 76-tile) atlas with a 128x112 (8x7) atlas containing only 50 tiles. BlockVisualAtlas.IsAuthoredAtlasTexture requires exactly 128x160, so VoxelWorldRenderer.Configure → BlockVisualAtlas.CreateMaterial throws InvalidOperationException and the world never renders. 26 mapped tiles would be lost (lumen_lamp, spark_flare, tended_soil, all 15 crop/sapling growth stages, smooth_branchwood, reed_basket, tool_rack, pantry_jar, deep_locker, and the 3 fluid families), tile 31 would be written under the stale name fired_brick instead of fired_brick_block, and the rewritten atlas .meta would set maxTextureSize 128 (committed: 256; test requires >= 160). PR CI never compiles Unity, so only the local-only EditMode test gate would catch this.
- **Evidence:** generate-art-assets.py:15 `ATLAS_ROWS = 7` vs BlockVisualAtlas.cs:12 `Rows = 10`; BLOCKS list (script lines 18–69) ends at tile 49 ("mend_bench") while BlockVisualAtlas.TileIndexByBlockId (lines 20–102) maps indices 0–75; script line 50 names tile 31 "fired_brick" but the canonical block id is "fired_brick_block" (VoxelTypes.cs registration); script line 647 writes atlas meta max_size = max(8,7)*16 = 128 while the committed meta has maxTextureSize 256 and M4ArtAssetValidationEditModeTests.cs:68-70 requires >= 160. Verified the committed atlas is currently 128x160 and pixel-identical to all 76 mapped Source tiles. ITEMS list (script lines 72–134, 61 entries) also cannot reproduce ~80 of the 142 committed item icons (bronze/ironroot/rosycopper/deepsteel/starforged tool sets, foods, buckets, seeds, bars), though regeneration would not delete or GUID-break them (verified all script-covered files already use the script's crc32-derived GUIDs, so references stay stable).
- **Recommended fix:** Bring generate-art-assets.py back in sync with the shipped assets: ATLAS_ROWS = 10, extend BLOCKS to all 76 mapped tiles using canonical ids from BlockRegistry (fix fired_brick → fired_brick_block), extend ITEMS to all 96 registry ids plus committed extras, and write the atlas meta with maxTextureSize 256. Better: derive both lists from a single manifest (or export from the registries) so the script cannot drift again, and add a CI-light check comparing script tile names/count against the committed Source directory.

### 3. Committed XR rig prefab's item icon library is stale — 4 registered items (worldroot, deepmantle, snowpack, frostglass) have no icon entry

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab`, `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- **Impact:** The serialized BlockiverseItemIconLibrary arrays in BlockiverseXRRig.prefab contain 138 entries while Assets/Blockiverse/Art/Textures/Items has 142 icons; the four missing ids are canonical, registered, harvestable items (ItemRegistry.cs:44-47; BlockHarvestRules.cs:114-117). In Editor play mode and PlayMode tests (which use the committed prefab), inventory and crafting slots for these items show no icon (TryGetIcon fails, Image stays hidden). APK builds self-heal because BlockiverseBuildSmoke runs BlockiverseProjectBootstrapper.Run() before building, which rebuilds the library from the folder — but that also proves the committed generated prefab was not regenerated after the icons were added, violating the project's 'scenes/prefabs are generated' invariant and meaning any other prefab drift would also ship from a stale base in-editor.
- **Evidence:** Prefab itemIds block (line 43079 onward) has 138 entries / 138 sprite refs (0 null); diff vs directory listing yields exactly ['deepmantle','frostglass','snowpack','worldroot']. All four PNGs exist with textureType: 8 (Sprite) metas and Unity-random GUIDs (added after the last bootstrap). EnsureItemIconLibrary (BlockiverseProjectBootstrapper.cs:2885-2918) enumerates the folder at bootstrap time; BlockiverseBuildSmoke.cs:25/54 call Run() before each build.
- **Recommended fix:** Rerun Blockiverse → Bootstrap Unity Quest Project and commit the regenerated BlockiverseXRRig.prefab. Consider an EditMode test asserting the committed prefab's icon library covers every ItemRegistry id (and matches the Items folder) so prefab drift fails the test gate.

### 4. All 16 generated UI and VFX sprites are wired to nothing — players see flat-color quads instead of the authored art

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Art/Sprites/UI/`, `Assets/Blockiverse/Art/Sprites/VFX/`, `Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxPool.cs`, `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- **Impact:** Every sprite in Art/Sprites/UI (hotbar_frame, selected_slot, health_pip, inventory_panel, crafting_panel, multiplayer_status_badge, settings_panel, feedback_toast) and Art/Sprites/VFX (8 particle textures) has zero serialized references anywhere in scenes/prefabs and zero code references. The HUD the bootstrapper builds uses plain colored Image components, and BlockiverseVfxPool's particleMaterial is serialized null, so every particle effect renders with an untextured `new Material(Shader.Find("Sprites/Default"))` default quad. The generated art pipeline produces and test-validates these assets, but the game never shows them; they are dead weight that misrepresents the shipped visual state (and M4ArtAssetValidationEditModeTests only checks file existence, not usage).
- **Evidence:** GUID reference scan: 0 references for each of the 16 sprite GUIDs across all .unity/.prefab/.asset/.mat (vs 1 reference each for the two Branding sprites). BlockiverseXRRig.prefab:43035 `particleMaterial: {fileID: 0}`. BlockiverseVfxPool.cs:75-77 falls back to untextured Sprites/Default. Bootstrapper's EnsureSurvivalHud/EnsureHudSection build Image components with palette colors only — no LoadAssetAtPath<Sprite> for UI/VFX anywhere in BlockiverseProjectBootstrapper.cs (only the Items folder at line 2905).
- **Recommended fix:** Wire them in via the bootstrapper: assign the VFX sprites to a particle material (set BlockiverseVfxPool.particleMaterial, with per-cue sprite selection if desired) and set the HUD panel/slot/pip Images' sprites from Art/Sprites/UI. If the flat-color look is the intended final art direction instead, delete the sprites and their generator entries (and the existence test) to stop maintaining dead assets.

### 5. Menu backdrop blockiverse_launch_landscape.png ships uncompressed (1672x941 RGBA32, ~6.3 MB) with point filtering and no mips

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Art/Sprites/Branding/blockiverse_launch_landscape.png`, `Assets/Blockiverse/Art/Sprites/Branding/blockiverse_launch_landscape.png.meta`, `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab`
- **Impact:** The image is used as a full-screen RawImage on the world-space menu canvas, so it is in view at title/pause. The Android platform override forces uncompressed import (textureCompression: 0, overridden: 1), costing ~6.3 MB of APK size and GPU memory on Quest for one menu texture, and filterMode 0 (Point) with enableMipMap 0 on a non-pixel-art, NPOT 1672x941 image produces visible aliasing/shimmer when the world-space panel is viewed at any angle or distance in the headset. This is the single largest texture in the project and the only one where the project-wide 'uncompressed pixel art' import template is clearly the wrong choice.
- **Evidence:** PNG header: 1672x941. Meta: enableMipMap: 0 (line 9), maxTextureSize: 2048 (line 33), filterMode: 0 (line 36), Android platform block textureCompression: 0 with overridden settings (lines 71-86). Used at BlockiverseXRRig.prefab:52532 `m_Texture: {fileID: 2800000, guid: 6e4cf5e26f6546f0b967f7ea13af3f7c}` on a UnityEngine.UI.RawImage (line 52524).
- **Recommended fix:** Import this texture ASTC-compressed (e.g., ASTC 6x6) with mipmaps enabled and bilinear/trilinear filtering; optionally resize to a power-of-two-friendly 1024-wide variant for the menu. If the meta is script-generated, add a 'large smooth image' template to the generator distinct from the pixel-art template.

### 6. EditorBuildSettings includes MultiplayerTest.unity while the build scripts ship Boot only — two sources of truth for build content

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `ProjectSettings/EditorBuildSettings.asset`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs`
- **Impact:** BlockiverseProjectBootstrapper.EnsureBuildScenes deliberately keeps the dev/test scene MultiplayerTest.unity enabled in EditorBuildSettings, but BlockiverseBuildSmoke overrides scenes to Boot.unity only for both dev and release APKs. Anyone building through the Unity Build Settings UI (or any future pipeline that trusts EditorBuildSettings) ships the test scene and its duplicate NetworkManager environment in the APK; conversely the divergence makes it unclear which list is authoritative. No runtime code loads MultiplayerTest (it is referenced only by tests).
- **Evidence:** EditorBuildSettings.asset lines 8-13 list both scenes enabled. EnsureBuildScenes (BlockiverseProjectBootstrapper.cs:1290-1311) adds BootScenePath and MultiplayerTestScenePath. BlockiverseBuildSmoke.cs:29 and :60 both use `scenes = new[] { BlockiverseProject.BootScenePath }`. grep shows no runtime LoadScene of MultiplayerTest; only Tests/PlayMode/Networking/MultiplayerSessionPlayModeTests.cs and an EditMode test reference it.
- **Recommended fix:** Make EnsureBuildScenes include only Boot.unity (tests load MultiplayerTest by path, which does not require build-settings membership for editor PlayMode), or add an explicit comment plus a guard so release builds can never pick up the test scene from EditorBuildSettings.

### 7. Stale Unity Test Runner scene InitTestScene8a89a79c-... committed at Assets root and not caught by CI

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity`, `Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity.meta`, `scripts/ci/forbidden-files.sh`
- **Impact:** A generated 'Code-based tests runner' scene (a Test Runner bootstrap artifact) is tracked in git at the Assets root. It is not in the build scene list so it does not ship, but it pollutes the project (imported by Unity on every refresh, visible in the Project window, violates the 'scenes are generated by the bootstrapper' convention) and scripts/ci/forbidden-files.sh has no pattern for it, so it will never be flagged.
- **Evidence:** `git ls-files` shows both the scene and its .meta tracked; scene content includes the Test Runner's `m_Name: Code-based tests runner` camera object (line 133). forbidden-files.sh regex `^(Library|Temp|Logs|UserSettings)/|...` does not match Assets/InitTestScene*.
- **Recommended fix:** Delete Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity(.meta) and add `(^|/)InitTestScene[0-9a-f-]*\.unity(\.meta)?$` to the forbidden-files regex so Test Runner artifacts cannot be committed again.

### 8. Stale Source/fired_brick.png block tile duplicates fired_brick_block.png under the old name

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Art/Textures/Blocks/Source/fired_brick.png`, `Assets/Blockiverse/Art/Textures/Blocks/Source/fired_brick_block.png`, `scripts/art/generate-art-assets.py`
- **Impact:** Assets/Blockiverse/Art/Textures/Blocks/Source contains both fired_brick.png (old generator name, crc32-scheme GUID c095bd46...) and fired_brick_block.png (current canonical id, different pixel content, Unity-random GUID). Only fired_brick_block matches a BlockRegistry canonical id; the stale file is dead but will be resurrected by the current generator script (which still uses the old name), keeping the confusion alive and leaving the atlas tile 31 content ambiguous across regenerations.
- **Evidence:** Registry diff: Source contains exactly one file not matching any canonical block id — fired_brick (canonical id is fired_brick_block, VoxelTypes.cs FiredBrickBlock registration). `cmp` shows the two PNGs differ. generate-art-assets.py:50 still emits ("fired_brick", 31, ...).
- **Recommended fix:** Delete Source/fired_brick.png(.meta) and rename the generator entry to fired_brick_block as part of the script resync (see the generator-drift finding). Note Items/fired_brick.png is a different, legitimate asset (ItemId.FiredBrick).

### 9. 46 item icons have no matching ItemId, and BlockiverseHighlight.mat plus TunnelingVignette.prefab are referenced by nothing

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Art/Textures/Items/`, `Assets/Blockiverse/Materials/BlockiverseHighlight.mat`, `Assets/Blockiverse/VR/TunnelingVignette/TunnelingVignette.prefab`, `Assets/Blockiverse/Scripts/Survival/ItemId.cs`
- **Impact:** Unused-but-committed content: 46 of 142 item icons match no registered item (full bronze/ironroot/rosycopper/deepsteel/starforged tool sets = 35 icons for tiers 3-7 that exist in the ruleset but not yet in ItemId; foods flatbread/trail_ration/berry_mash; block-named icons berrybush/grain_stalk/reedgrass/niterstone/paletin_thread/staropal_geode/sunmetal_fleck/umbralite_node). All of them are loaded into the rig's icon library at bootstrap (file name = id), which is harmless (a few hundred KB) but means the library carries dead entries. BlockiverseHighlight.mat is generated by the bootstrapper yet referenced by no scene/prefab (its only consumer, the 'Interaction Test Block' wiring, is absent from the current scenes), and TunnelingVignette.prefab is unused (the rig embeds the vignette mesh/material directly).
- **Evidence:** comm diff of 142 icon basenames vs 96 ItemId canonical ids leaves 46 extras; ItemId.cs defines only Reedwood and Flint tool tiers (lines 113-129). GUID scans: BlockiverseHighlight.mat guid d1257df724dcf4ea3a018ab73b6f2fdd → 0 references; TunnelingVignette.prefab guid → 0 references in BlockiverseXRRig.prefab (the .mat and .fbx are referenced directly instead). EnsureItemIconLibrary (bootstrapper:2885) loads every texture in the Items folder unconditionally.
- **Recommended fix:** Nothing urgent. When tiers 3-7 / cooked foods land, the icons are ready. If you want a clean tree now: have EnsureItemIconLibrary filter to ItemRegistry ids, delete BlockiverseHighlight.mat or restore its consumer, and remove the unused TunnelingVignette.prefab. Keep the icons if they are intentional pre-work for the roadmap.

### 10. Android adaptive/round launcher icon slots are empty — only the legacy 192px icon is set

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `ProjectSettings/ProjectSettings.asset`, `Assets/Blockiverse/Art/Sprites/Branding/blockiverse_app_icon.png`
- **Impact:** ProjectSettings assigns blockiverse_app_icon.png only to the default icon and the Android Legacy (Kind 0) 192px slot; all Adaptive (Kind 2) and Round (Kind 1) slots have empty texture arrays. Quest's launcher uses store-uploaded art for installed channel builds, so impact there is minimal, but sideloaded/Unknown-Sources listings and any non-Quest Android target fall back to the legacy icon rendered without adaptive masking (can appear letterboxed/cropped in round masks).
- **Evidence:** ProjectSettings.asset m_BuildTargetPlatformIcons Android section: all Kind 2 (432/324/216/162/108/81) and Kind 1 (192...36) entries have `m_Textures: []`; only the Kind 0 192px entry references guid a7184a73e67f4e688ee627ea6a34c6d9 (blockiverse_app_icon.png, 512x512).
- **Recommended fix:** Generate foreground/background layers for the adaptive icon (the art generator could emit them) and assign them via the bootstrapper's ConfigureAppBranding so all Android icon kinds are populated.

### 11. Project-wide point filtering with mipmaps disabled on the block atlas will shimmer at distance in VR

- **Severity:** Informational  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png.meta`, `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`, `scripts/art/generate-art-assets.py`
- **Impact:** The block atlas (and all generated textures) import with filterMode Point and enableMipMap 0. For a head-tracked stereo display, minified pixel-art textures without mips alias heavily on distant terrain — a known VR comfort/quality concern Meta's guidance flags. The 0.001 UV inset in BlockVisualAtlas.GetTileRect protects tile edges at base level, but without mips there is nothing to protect at minification. This is plausibly an intentional aesthetic, so recording as an observation rather than a defect.
- **Evidence:** Atlas meta lines 9/36: enableMipMap: 0, filterMode: 0; generator template writes the same for every texture (generate-art-assets.py:510-541). BlockVisualAtlas.cs:18 `UvInset = 0.001f` only insets base-level UVs. M4ArtAssetValidationEditModeTests.cs:59-61 asserts Point/no-mips, so the choice is codified.
- **Recommended fix:** If shimmer is observed on device, enable mipmaps with Point filtering plus a padded atlas (duplicate tile borders into gutters, or move to a Texture2DArray) so distance rendering stabilizes without softening the pixel-art look; update the test accordingly.

## What Looks Good (7)

- Reference integrity is flawless: a full scan of every .unity/.prefab/.mat/.asset against 18,785 known GUIDs (Assets + Packages + PackageCache) found zero unresolved references, zero m_Script {fileID: 0} slots, and zero duplicate GUIDs.
- Complete texture coverage: all 79 non-air BlockRegistry canonical ids have Source tiles, all 96 ItemRegistry ids have icons (Assets/Blockiverse/Art/Textures), every renderable block has an atlas mapping (enforced by BlockVisualAtlas.ValidateRenderableBlockCoverage and M4ArtAssetValidationEditModeTests), and the committed 128x160 atlas is pixel-identical to all 76 mapped committed source tiles (verified by decode-and-compare).
- Audio pipeline is fully consistent and reference-stable: the 34 committed wavs exactly match generate-audio.py's CLIPS dict, every .meta GUID matches the script's md5(path) scheme (regeneration cannot break references), music uses Streaming/no-preload while SFX use DecompressOnLoad (correct for Quest memory), and all 34 clips plus the 28-cue table and footsteps/music are wired by GUID into BlockiverseXRRig.prefab (Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs:164-194, 2347-2397).
- The art generator's GUID scheme (crc32-derived, pinned atlas GUID) means regenerating script-covered textures never breaks serialized references — verified by recomputing every script-covered path's GUID against committed metas (0 drift).
- Build scripts self-heal generated wiring: BlockiverseBuildSmoke.cs runs BlockiverseProjectBootstrapper.Run() before both dev and release builds and ships exactly Boot.unity, so APKs always get freshly regenerated scene/prefab wiring.
- Heavy SDK content stays out of the build: nothing in the build scenes/prefabs references the 242 MB Assets/Oculus tree (verified by GUID reachability scan); StreamingAssets carries only the 7.3 MB Meta Avatar runtime zips.
- M4ArtAssetValidationEditModeTests is a genuinely good asset gate: it checks atlas dimensions against the code constants, per-id texture existence for every block and item, import settings (point/clamp/no-mips, Android override bounds), fail-fast material validation, and even forbidden trademark tokens in asset paths.

## Could Not Review (5)

- On-device behavior of the Shader.Find fallback (whether Unity's build truly strips BlockiverseVoxelLit and how the world looks under URP/Lit without vertex colors) — static evidence is strong but only a Quest build/device test or a build report confirms it.
- Actual APK contents (which assets the Unity build pipeline pulls in transitively) — inferred from serialized references and build script scene lists, not from a build log, since running builds was out of bounds.
- Visual quality judgments (atlas shimmer at distance, launch-image aliasing on the world-space menu) — flagged from import settings; require headset verification.
- Package-internal GUID resolution relied on the local Library/PackageCache matching Packages/manifest.json; a clean clone could theoretically resolve differently.
- The Meta Avatars runtime's asset loading from StreamingAssets zips (correct zip versions for the pinned SDK packages) — no static cross-check is possible against the zip contents.

## Inspected (31)

- `Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png (+ .meta, pixel-decoded and tile-compared)`
- `Assets/Blockiverse/Art/Textures/Blocks/Source/ (80 PNGs + metas)`
- `Assets/Blockiverse/Art/Textures/Items/ (142 PNGs + metas)`
- `Assets/Blockiverse/Art/Sprites/UI/, Assets/Blockiverse/Art/Sprites/VFX/, Assets/Blockiverse/Art/Sprites/Branding/`
- `Assets/Blockiverse/Audio/ (34 wavs + metas, GUID/loadType validated against generator)`
- `Assets/Blockiverse/Materials/*.mat`
- `Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader`
- `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab (58k-line YAML: icon library arrays, audio clip GUIDs, particleMaterial, RawImage texture)`
- `Assets/Blockiverse/Prefabs/Networking/*.prefab`
- `Assets/Blockiverse/Scenes/Boot.unity (full read)`
- `Assets/Blockiverse/Scenes/MultiplayerTest.unity (reference scan)`
- `Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity`
- `Assets/Blockiverse/Settings/ (URP assets, input actions)`
- `Assets/Resources/, Assets/XR/, Assets/XRI/, Assets/StreamingAssets/, Assets/Oculus/ (reference reachability)`
- `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseItemIconLibrary.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseMusicController.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxPool.cs`
- `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`
- `Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs (vertex-color light path)`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeCatalog.cs`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs (shader fallbacks)`
- `Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs (BlockRegistry)`
- `Assets/Blockiverse/Scripts/Survival/ItemId.cs, ItemRegistry.cs, BlockHarvestRules.cs`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs (icon library, audio wiring, materials, build scenes)`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs`
- `Assets/Blockiverse/Tests/EditMode/M4ArtAssetValidationEditModeTests.cs`
- `scripts/art/generate-art-assets.py`
- `scripts/audio/generate-audio.py`
- `scripts/ci/forbidden-files.sh`
- `ProjectSettings/GraphicsSettings.asset, EditorBuildSettings.asset, ProjectSettings.asset, QualitySettings.asset`
