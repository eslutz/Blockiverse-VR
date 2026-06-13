# Codebase Review — Performance and Optimization Expert

> Workflow run `wf_53b36881-009`, agent `a93957a546ada0685`. Raw expert output, pre-verification.

## Area Reviewed

Performance and optimization review of the Blockiverse VR codebase for Meta Quest 3/3S: every MonoBehaviour Update/LateUpdate in Gameplay/UI/VR/MetaAvatars/Networking, the voxel rendering pipeline (VoxelWorldRenderer, ChunkMeshBuilder, VoxelSkyLightMap, VoxelLightSampler), world generation and load/save flows, fluid simulation integration, multiplayer sync hot paths, ProjectSettings/QualitySettings/URP/OpenXR configuration, the custom voxel shader, texture and audio import settings, and pooling/memory patterns. Overall the per-frame steady-state code is unusually disciplined for a project at this stage — change-gated UI repaints, throttled scans, pooled meshes/lists/audio/VFX, Allocator.Temp wire buffers, and a well-tuned URP asset (4x MSAA, HDR off, SRP batcher, Vulkan/IL2CPP/ARM64/graphics jobs). The serious problems are concentrated in burst work on the main thread: world create/load/host-start/late-join all run multi-second full-world generation plus full re-mesh synchronously (the load path even does it twice), every surface block edit synchronously rebuilds up to ~63 chunk meshes due to a ±13-block lighting invalidation halo, spreading fluids re-trigger that storm 4 times per second, and autosaves serialize the world to flash on the main thread every 5 minutes. A secondary cluster of config issues (main-light realtime shadows forced on every frame against the project's own guidance, foveated rendering enabled but never given a level, mip-less point-filtered block atlas) leaves GPU headroom and visual comfort on the table.

## Findings (18)

### 1. World create/load/host-start/late-join freeze the main thread for seconds (full 4.2M-block generation + 1024-chunk re-mesh + collider cook in one frame)

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`
- **Impact:** Every entry into a world — app boot (CreativeWorldManager.Awake), New World, Load World, multiplayer host start, and client late-join finalize — runs as a single synchronous main-thread burst. In the headset this is a multi-second frozen frame (head-locked image / compositor stall), the single worst comfort failure on Quest, and risks the OS app-not-responding watchdog. The burst comprises: SurvivalTerrainPreset.Generate over 128×256×128 = 4,194,304 blocks (terrain+fluids+caves+veins+structures+vegetation); VoxelWorldRenderer.RebuildAll over 8×16×8 = 1024 chunks each doing a 4096-block walk with per-face light probes (up to 5 dirs × 12 steps) plus Mesh.RecalculateNormals, then ProcessPendingColliderRebuilds(int.MaxValue) which PhysX-cooks every non-empty chunk MeshCollider in the same frame; VoxelSkyLightMap constructor full column scan; TorchbudLightManager.RebuildAllLights full 4.2M-block scan; FluidFlowService.Configure full-world scan + BFS; FarmingService.ScanAndTrackCrops full per-position scan. Only the late-join path moves generation off-thread (Task.Run) — its FinalizeSnapshot still does all meshing/baking in one frame.
- **Evidence:** BlockiverseWorldSessionController.cs:256-322 (CreateNewWorld → SurvivalTerrainPreset.Generate() on main thread, then InitializeGeneratedWorld) and 590-680 (LoadSave → RegenerateBaseWorld → preset.Generate()); CreativeWorldManager.cs:728-732 (Awake → InitializeDefaultWorld) and 393-420 (ConfigureWorldRuntime → Renderer.Configure); VoxelWorldRenderer.cs:72 (Configure → RebuildAll), 75-107 (RebuildAll loops all chunks), 100 (ProcessPendingColliderRebuilds(int.MaxValue) — comment says 'flush every queued collider rebuild rather than throttling'); VoxelSkyLightMap.cs:21-33 (constructor Rebuild scans all columns); TorchbudLightManager.cs:60-76 (RebuildAllLights triple loop over Bounds); FluidFlowService.cs:88-173 (Configure full-world triple loop + BFS); MultiplayerChunkAuthoritySync.cs:478-513 (FinalizeSnapshot → InitializeGeneratedWorld + RebuildDirty on main thread after off-thread Generate). WorldGenerationSettings.cs:19-28 (width 128, height 256, depth 128).
- **Recommended fix:** Amortize world entry across frames: (1) run generation off the main thread everywhere (the Task.Run pattern in MultiplayerChunkAuthoritySync.StartSnapshotGeneration already proves the sim core is thread-safe for this); (2) time-slice RebuildAll — mesh N chunks per frame behind the existing startup overlay / a loading screen, prioritizing chunks near spawn; (3) keep the collider flush budgeted (cook spawn-area colliders first, drain the rest via the existing per-frame pump); (4) replace the FarmingService/Torchbud full per-position scans with VoxelWorld.CollectBlockPositions flat-array sweeps (VegetationService already does this). Display a real loading state during the slice so the compositor keeps rendering.

### 2. World load path runs the full 1024-chunk RebuildAll twice (and host-start can too)

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`, `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`
- **Impact:** LoadSave first calls InitializeGeneratedWorld, whose ConfigureWorldRuntime → Renderer.Configure already executes a complete RebuildAll (all chunk meshes + full collider flush), then after applying saved deltas calls Renderer.RebuildAll() again — doubling the most expensive part of the already multi-second load hitch. MultiplayerWorldPersistence.RestoreSavedWorldBeforeHostStart has the same shape: TryResolveWorldManager may InitializeDefaultWorld (RebuildAll #1) and line 178 runs RebuildAll #2. The first full build is wasted work; only chunks touched by saved deltas need a second pass, and the delta application already marks exactly those chunks dirty via the rebuild queue.
- **Evidence:** BlockiverseWorldSessionController.cs LoadSave: line ~609 `worldManager.InitializeGeneratedWorld(generated, chunkAuthoritySync)` (triggers VoxelWorldRenderer.Configure → RebuildAll at VoxelWorldRenderer.cs:72), then line ~629 `worldManager.Renderer?.RebuildAll();` after result.ApplyTo. MultiplayerWorldPersistence.cs:178 `worldManager.Renderer?.RebuildAll();` after a load whose world may have just been initialized (line 377 InitializeDefaultWorld inside TryResolveWorldManager).
- **Recommended fix:** After applying saved block deltas, call Renderer.RebuildDirty() instead of RebuildAll() — block-delta application already marks affected chunks via ChunkRebuildQueue (the late-join FinalizeSnapshot path at MultiplayerChunkAuthoritySync.cs:495-507 does exactly this, with a comment explaining why). Alternatively defer the renderer's initial RebuildAll until after deltas are applied so only one full pass runs.

### 3. Every surface block edit synchronously rebuilds up to ~63 chunk meshes due to the ±13-block lighting invalidation halo

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs`, `Assets/Blockiverse/Scripts/Gameplay/VoxelLightSampler.cs`, `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeInteractionController.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`
- **Impact:** Placing or breaking a block that changes a column's sky profile marks every chunk in a 27×27-column halo (LightingProbeInvalidationPadding = DefaultProbeDistance 12 + 1 = 13) from y=0 up to the surface (~y 96-110) dirty: 3×3 chunk columns × 7 vertical chunks ≈ up to 63 chunks. RebuildDirty then rebuilds them all in the same frame (called synchronously from the interaction handler and from the world tick). Each rebuild is a 4096-block walk with per-rendered-face cave-light probes (up to 5 directions × 12 steps = 60 block reads per face) plus Mesh.RecalculateNormals and a queued collider rebake — a guaranteed multi-millisecond to tens-of-milliseconds CPU spike per edit on Quest, i.e. dropped frames on the core build/mine interaction. Underground edits still mark up to 27 chunks (±13 in y too). The code's own warning threshold (LargeDirtyRebuildWarningThreshold = 8 drained chunks) confirms this fires in normal play.
- **Evidence:** ChunkMeshBuilder.cs:205 `LightingProbeInvalidationPadding = VoxelLightSampler.DefaultProbeDistance + 1` with VoxelLightSampler.cs:11 `DefaultProbeDistance = 12`; ChunkRebuildQueue.OnBlockChanged (ChunkMeshBuilder.cs:245-281) — sky-profile change branch calls MarkLightingAffectedChunks(x, z, 0, maxY) marking chunk range minX=x-13..maxX=x+13 etc. (lines 283-301); VoxelWorldRenderer.RebuildDirty (109-133) rebuilds all drained chunks synchronously and warns at ≥8 (line 13, 126-132); called per edit from CreativeInteractionController.RebuildChangedChunks (`worldRenderer?.RebuildDirty()`) and per tick from CreativeWorldManager.cs:658. Per-face probe cost: VoxelLightSampler.SampleAirLight lines 41-67.
- **Recommended fix:** Decouple lighting refresh from immediate remesh: (1) budget RebuildDirty (N chunks per frame, nearest-first) the same way collider rebakes are budgeted — visuals of distant lighting halo chunks can trail by a few frames without being noticeable; (2) shrink the invalidation set — only chunks whose visible faces actually border the changed column within probe range need a lighting repaint (a per-chunk 'light dirty only' rebuild could skip geometry regeneration and just rewrite vertex colors); (3) consider caching per-cell light values per chunk so an unchanged-geometry rebuild is a color-array update instead of a full probe walk.

### 4. Fluid spread re-triggers the chunk rebuild storm 4×/second and cooks fluid MeshColliders synchronously every rebuild

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/WorldGen/FluidFlowService.cs`, `Assets/Blockiverse/Scripts/Voxel/FluidBlocks.cs`, `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs`
- **Impact:** Freshwater steps every 5 world ticks (0.25 s; emberflow every 12). Every cell a flow writes (SetBlock) fires ChunkRebuildQueue.OnBlockChanged, which marks the ±13-block halo (up to 27 chunks per cell, coalesced by HashSet) — so an active water front keeps dozens of chunks dirty continuously, and CreativeWorldManager.OnWorldTick → Renderer.RebuildDirty() rebuilds them all synchronously every tick batch. On top of the solid-mesh rebuild cost, UpdateFluidChunkMesh re-cooks the fluid child's MeshCollider synchronously on every rebuild of a fluid-bearing chunk (unlike solid colliders, which are budgeted at 4/frame). Pouring a bucket, breaching a lake, or an emberflow burn means sustained dropped frames for the duration of the spread — exactly the 'mesh rebuild storm when fluids spread' scenario, on every peer simultaneously (the sim is deterministic lockstep on all clients).
- **Evidence:** FluidBlocks.cs:19 `TickCadenceByFamily = { 5, 6, 12 }` and :18 flow distances {8,6,4}; FluidFlowService.ProcessCell (342-416) calls world.SetBlock for falls/spreads/retractions; CreativeWorldManager.OnWorldTick (644-660): `fluidFlowService?.Tick(...)` then `Renderer?.RebuildDirty()` every tick batch; VoxelWorldRenderer.UpdateFluidChunkMesh lines 213-215: `collider.sharedMesh = null; collider.sharedMesh = mesh;` — synchronous PhysX cook per rebuild, bypassing the ColliderRebuildBudget (DefaultColliderRebuildBudget = 4, line 21); halo marking via ChunkRebuildQueue.OnBlockChanged (ChunkMeshBuilder.cs:245-281).
- **Recommended fix:** Same budgeted RebuildDirty as the edit-storm fix, plus: (1) route fluid collider rebakes through the existing throttled pendingColliderRebuilds queue instead of cooking inline (rays against a frame-stale water surface are acceptable); (2) exempt fluid-only block changes from the full lighting halo — a water cell does not block light (IsLightBlocking requires IsSolid), so marking only the cell's own chunk + face neighbors would cut the dirty set by an order of magnitude; (3) optionally cap fluid sim mutations per step and carry the remainder to the next step.

### 5. Autosave serializes the whole save to flash synchronously on the main thread every 5 minutes (both single-player and host)

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- **Impact:** WorldSaveService.Save runs inline in Update on the autosave cadence (AutoSaveIntervalSeconds = 300) and on pause/quit/menu actions: it builds BlockRegistry.CreateDefault() and ItemRegistry.CreateDefault() fresh (a new WorldSaveService per call), computes registry hashes, walks the full changed-block dictionary building a nested region/chunk/section map of new collections, JSON-serializes manifest/dimension/environment/containers/simulation/stations/player, and performs atomic .tmp writes plus a recursive directory delete/swap — all on the render thread against Quest flash storage. With a mature world (mining + structures + fluid floods inflating the delta set), this is a perceptible hitch (likely tens to hundreds of ms) every 5 minutes during play, a recurring comfort/frame-pacing defect.
- **Evidence:** MultiplayerWorldPersistence.cs:248-275 (Update → `new WorldSaveService().Save(...)` inline, cadence WorldSaveService.AutoSaveIntervalSeconds); BlockiverseWorldSessionController.cs:98-107 (Update → SaveCurrentWorld on the same cadence) and 167+ (SaveCurrentWorld synchronous); WorldSaveService.cs:197 `AutoSaveIntervalSeconds = 300f`, 214-335 (Save: CreateDefault registries, hash computation, WriteJsonAtomic ×6+, WriteRegionFiles with nested Dictionary/List allocation per save at 448-510, regions dir delete/recreate/swap at 483-492).
- **Recommended fix:** Move serialization and IO off the main thread: snapshot the mutable state cheaply on the main thread (the changed-block dictionary values can be copied to an array, plus small extras), then run region/JSON encoding and file writes on a worker (Task.Run) with a single in-flight save guard; keep only the final directory swap fast. Also hoist the per-save `new WorldSaveService()` / registry construction and hash computation into cached statics — the registries and their hashes never change at runtime.

### 6. Sun light forces Hard realtime shadows every frame while URP main-light shadows are enabled at 2048 — contradicting the project's own no-shadow design

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/BlockiverseLightingCycleController.cs`, `Assets/Blockiverse/Settings/BlockiverseAndroidURPAsset.asset`, `Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader`, `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`
- **Impact:** BlockiverseLightingCycleController.LateUpdate sets sunLight.shadows = LightShadows.Hard with strength 0.85 every frame, and the Android URP asset has main-light shadows supported with a 2048×2048 shadow map and 50 m shadow distance. Voxel chunks neither cast nor receive (renderer flags off), and the custom voxel shader's comment-documented design is vertex-color lighting — VoxelWorldRenderer.cs:298-300 explicitly says shadow passes 'would only add an extra render pass per light on Quest (per Meta VRC guidance) for no visual gain.' Yet any shadow-casting renderer in range (Meta avatars, network fallback proxies, props) triggers a per-frame 2048 shadow map render pass plus the _MAIN_LIGHT_SHADOWS keyword, making every voxel fragment sample the shadow map (BlockiverseVoxelLit ForwardLit calls GetMainLight(shadowCoord)) — meaningful GPU bandwidth/ALU on a tile-based mobile GPU, in both eyes, for shadows the art style doesn't use. The fluid child even has receiveShadows = true (VoxelWorldRenderer.cs:229).
- **Evidence:** BlockiverseLightingCycleController.cs:41-44 (LateUpdate → ApplyCurrentLighting) and 70-71 (`sunLight.shadows = LightShadows.Hard; sunLight.shadowStrength = 0.85f;` every frame); BlockiverseAndroidURPAsset.asset: `m_MainLightShadowsSupported: 1`, `m_MainLightShadowmapResolution: 2048`, `m_ShadowDistance: 50`; BlockiverseVoxelLit.shader:26 `#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE`, :73 GetShadowCoord, :81 GetMainLight(input.shadowCoord); VoxelWorldRenderer.cs:298-301 (shadowCastingMode Off + Meta VRC comment), :229 fluid `receiveShadows = true`.
- **Recommended fix:** Set sunLight.shadows = LightShadows.None in ApplyCurrentLighting and disable main-light shadows in BlockiverseAndroidURPAsset (m_MainLightShadowsSupported: 0, which also strips the shadow keywords/variants). If soft character grounding is wanted later, use a cheap blob/projected shadow instead of a full shadow map. Also stop re-assigning sunLight.type/shadows/renderMode and RenderSettings.ambientMode every LateUpdate — set static values once in Configure and only update intensity/color/rotation/fog per frame.

### 7. Foveated rendering feature enabled but no FFR level is ever set — GPU savings never realized

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/XR/Settings/OpenXR Package Settings.asset`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`
- **Impact:** The OpenXR 'Meta XR Foveation' feature is enabled for Android, but nothing in the codebase configures a foveation level (no OVRManager in the generated scene, no OVRPlugin.foveatedRenderingLevel / XRDisplaySubsystem foveation calls; the only repo hit for 'foveation' outside SDK code is the bootstrapper's feature-id string). With the runtime default level (off), the app renders full resolution across the entire view. For a fragment-bound app on Quest (4x MSAA, full-screen voxel geometry, per-pixel additional lights), enabling FFR level 2-3 (or dynamic) is one of the largest free GPU wins available (commonly 10-25% GPU savings), directly improving thermals and frame-rate headroom.
- **Evidence:** OpenXR Package Settings.asset: 'MetaXRFoveationFeature Android' block (around line 1946) has m_enabled: 1 but the feature block carries no level configuration; Unity's 'FoveatedRenderingFeature Android' (line ~1463) is m_enabled: 0 and 'MetaXRSubsampledLayout Android' (line ~456) m_enabled: 0. Repo-wide grep for foveat* in Assets/Blockiverse/Scripts matches only BlockiverseProjectBootstrapper.cs:77 (feature id string 'com.meta.openxr.feature.foveation'); no runtime call sets a level.
- **Recommended fix:** Set a fixed foveation level at startup (e.g. via OVRPlugin.foveatedRenderingLevel = High with dynamic enabled, or Unity's XRDisplaySubsystem foveatedRenderingLevel/flags when using the Unity feature), ideally raising it during known-heavy moments (world load, fluid storms). Verify on-device that the periphery quality is acceptable with the flat-shaded art style — it almost always is. Consider also enabling Subsampled Layout (Vulkan) to reduce FFR fringe artifacts and save more bandwidth.

### 8. FarmingService.ScanAndTrackCrops does a full per-position world scan (4.2M GetBlock + HashSet lookups) on every world init

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Survival/FarmingService.cs`, `Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Voxel/VoxelWorld.cs`
- **Impact:** ConfigureEnvironmentServices calls farmingService.ScanAndTrackCrops(World) at every world initialization (boot, new world, load, host start, and the late-join client finalize). Its triple nested loop calls world.GetBlock(new BlockPosition(...)) — paying bounds-check + index math — plus a CropBlocks HashSet.Contains for each of the 4,194,304 positions. VegetationService had the identical problem and was fixed to use VoxelWorld.CollectBlockPositions, with a comment explicitly warning that 'a per-position GetBlock scan over a full-size world (4M+ blocks) stalls the main thread on world init, including the late-join client finalize path' — FarmingService was left on the slow path. This adds an estimated hundreds of milliseconds to every world-entry hitch on Quest-class CPUs.
- **Evidence:** FarmingService.cs:149-173 (ScanAndTrackCrops): `for (int y...) for (int z...) for (int x...) { var pos = new BlockPosition(x, y, z); if (CropBlocks.Contains(world.GetBlock(pos)) ...` over full Bounds. Contrast VegetationService.cs:126-131 comment + CollectBlockPositions usage; VoxelWorld.CollectBlockPositions (VoxelWorld.cs:81-97) is the linear flat-array sweep designed for this. Call site: CreativeWorldManager.cs:487.
- **Recommended fix:** Mirror the VegetationService fix: collect candidate positions with world.CollectBlockPositions for each crop block id (CropBlocks is a known finite set) into a reused scratch list, then reconcile trackedCrops from that. This turns ~4.2M dictionary-checked GetBlock calls into one linear array pass per crop block id.

### 9. ChunkMeshBuilder allocates four new fluid lists on every chunk rebuild while the solid lists are pooled

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs`
- **Impact:** Build() pools the solid vertex/triangle/uv/color lists ([ThreadStatic], explicitly 'so chunk rebuilds do not allocate every call (GC hitches on Quest)') but allocates `new List<Vector3>() / List<int>() / List<Vector2>() / List<Color>()` for the fluid mesh on every call — including for the overwhelming majority of chunks that contain no fluid. During rebuild storms (63 chunks per surface edit; continuous 4 Hz fluid repaints; 1024 chunks at RebuildAll) this generates thousands of list allocations plus growth reallocation for fluid-bearing chunks, adding avoidable GC pressure on a platform where the same file's comments treat GC hitches as a design constraint.
- **Evidence:** ChunkMeshBuilder.cs:40-43 pooled [ThreadStatic] solid lists with the GC-hitch comment; lines 92-95: `var fluidVertices = new List<Vector3>(); var fluidTriangles = new List<int>(); var fluidUvs = new List<Vector2>(); var fluidColors = new List<Color>();` allocated unconditionally per Build call.
- **Recommended fix:** Pool the four fluid lists the same way as the solid ones ([ThreadStatic], Clear() per call). The aliasing contract documented for ChunkMeshData already requires consumers to copy data out before the next Build, so pooling the fluid lists has identical semantics — VoxelWorldRenderer.RebuildChunk consumes both before the next Build.

### 10. Up to 24 realtime per-pixel point lights from torch blocks under Forward rendering

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/TorchbudLightManager.cs`, `Assets/Blockiverse/Settings/BlockiverseAndroidURPAsset.asset`, `Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader`
- **Impact:** TorchbudLightManager creates a realtime Point light (range 6, no shadows) for every emissive block up to MaxActiveLights = 24. The URP asset uses classic Forward (m_RenderingMode: 0) with per-pixel additional lights (m_AdditionalLightsRenderingMode: 1, per-object limit 4). Because chunk meshes are huge (a whole 16³ chunk is one renderer), a single torch makes URP evaluate up to 4 additional lights for every fragment of each affected chunk mesh — large screen areas pay per-pixel light loops in the custom shader's _ADDITIONAL_LIGHTS branch. A torch-lit base with many emitters measurably raises fragment cost and CPU light-culling/setup per frame on Quest, and per-object selection of 4 lights across whole-chunk renderers also produces visible light popping.
- **Evidence:** TorchbudLightManager.cs:11-13 (MaxActiveLights = 24, LightRange = 6.0f, LightIntensity = 2.2f), 111-122 (AddLight creates LightType.Point, LightRenderMode.Auto); BlockiverseAndroidURPAsset.asset: m_AdditionalLightsRenderingMode: 1, m_AdditionalLightsPerObjectLimit: 4, m_RenderingMode: 0 in BlockiverseAndroidUniversalRenderer.asset; BlockiverseVoxelLit.shader:86-94 per-pixel additional light loop.
- **Recommended fix:** Since block lighting is already baked into vertex colors via the sky/probe system, consider folding emissive block light into the vertex-color bake (deterministic, zero runtime light cost) and keeping at most 1-3 realtime lights for the nearest emitters for dynamic pop. If realtime lights stay, lower MaxActiveLights, prioritize by distance to the player (currently first-come-first-served with a pending queue), and consider Forward+ (clustered) which decouples light count from per-object limits — though validate Forward+ cost on Quest first.

### 11. Block atlas imported with mipmaps disabled and point filtering — minification shimmer and texture-cache thrash in VR

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png.meta`, `Assets/Blockiverse/Art/Textures/Items`
- **Impact:** The single terrain texture (blockiverse_block_atlas.png, 128×160, 16px tiles) is imported with enableMipMap: 0 and filterMode: 0 (Point), with an Android override forcing uncompressed. Every distant voxel face samples the base level far below 1:1, producing severe specular-free shimmer/crawling in stereo VR (a comfort problem the Quest store review notices) and incoherent texture fetch patterns that waste GPU cache/bandwidth on the tiled GPU. Memory is irrelevant at this size — the issue is sampling quality and bandwidth, magnified because this texture covers most of every frame. The 142 item icons (64×64, mips off, uncompressed) matter less since they're UI-scale.
- **Evidence:** blockiverse_block_atlas.png.meta: `enableMipMap: 0`, `filterMode: 0`, `aniso: 1`, Android platform override `textureCompression: 0, overridden: 1`; atlas geometry constants BlockVisualAtlas.cs:11-13 (8×10 tiles of 16px, UvInset 0.001). Item icons sampled (berry_cluster.png.meta): enableMipMap: 0, maxTextureSize: 64, textureCompression: 0 (138/142 metas carry the override).
- **Recommended fix:** Enable mipmaps on the atlas and use trilinear-between-mips with point-within-mip if the pixel-art look must be kept at close range (or pad/duplicate tile borders and increase UvInset to prevent mip bleed across the 16px tiles — the standard Minecraft-style approach; alternatively bake a small mip chain per tile offline in generate-art-assets.py). Re-evaluate the uncompressed Android override: ASTC 4x4/6x6 on a padded atlas is fine for this style and halves bandwidth.

### 12. VoxelWorld changed-block delta set grows without reconciliation — bloats memory, saves, and the late-join snapshot

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Voxel/VoxelWorld.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`, `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- **Impact:** VoxelWorld.changedBlocks is a Dictionary<BlockPosition, BlockChange> that only ever grows (SetBlock upserts; entries are never removed when a cell returns to its generated value). Fluid dynamics make this acute: a flood that spreads and then retracts leaves a permanent Air-valued delta for every cell it ever touched; snow accumulation/melt cycles and emberflow burns do the same. Consequences scale together: (1) host memory (~48+ bytes/entry; tens of thousands of dead entries after a few floods); (2) save size and main-thread save time (WriteRegionFiles iterates every entry); (3) the late-join snapshot is sized 80 + 32 × changedBlocks.Count bytes and written into a single Allocator.Temp FastBufferWriter and one named message — a long-running host session can push this into multi-megabyte single-message territory, spiking the host's frame and stressing UTP reliable fragmentation; (4) load time (ApplyTo replays every delta).
- **Evidence:** VoxelWorld.cs:23 `readonly Dictionary<BlockPosition, BlockChange> changedBlocks = new();` and 53-70 SetBlock (`changedBlocks[position] = change;` — no removal path other than ClearChangedBlocks); MultiplayerChunkAuthoritySync.cs:676-703 SendLateJoinSnapshot (`int writerSize = SnapshotHeaderBytes + changedBlocks.Count * SnapshotBlockBytes;` single buffer/message); WorldSaveService.cs:452 `foreach (BlockChange change in world.GetChangedBlocks())`.
- **Recommended fix:** Reconcile deltas against the generated baseline: either (a) have the world retain (or lazily recompute per-column) the generated block value so SetBlock can delete the entry when a cell returns to baseline, or (b) periodically compact: during save (which already regenerates nothing but knows the seed) or on a background thread, regenerate per-region baseline and drop no-op deltas. Also consider chunking the late-join snapshot into multiple messages to bound per-frame serialization.

### 13. Per-tick and per-step micro-allocations in the world simulation loop

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs`, `Assets/Blockiverse/Scripts/WorldGen/FluidFlowService.cs`, `Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxPool.cs`, `Assets/Blockiverse/Scripts/Survival/FarmingService.cs`
- **Impact:** Small but steady GC pressure in the 20 Hz tick path: (1) ChunkRebuildQueue.DrainDirtyChunks allocates a new List per drain whenever anything is dirty (every tick during fluid activity); (2) FluidFlowService.StepFamily calls processScratch.Sort(ComparePositions) — under Unity's C# the static method group converts to a fresh Comparison<T> delegate per call (up to 20×/s across families), and the sort itself is O(n log n) over the active set each step; (3) FarmingService growth sweep snapshots keys per interval; (4) BlockiverseVfxPool.ConfigureParticleSystem allocates a Burst[] per Play (rain VFX every 0.6 s). Individually trivial, but they run forever and Quest GC pauses are user-visible; the codebase elsewhere goes to lengths (ThreadStatic pools, scratch lists) to avoid exactly this.
- **Evidence:** ChunkMeshBuilder.cs:240 `var drained = new List<ChunkCoordinate>(dirtyChunks);` per non-empty drain; FluidFlowService.cs:327 `processScratch.Sort(ComparePositions);` (method-group delegate allocation per call) inside StepFamily called per family per cadence tick; BlockiverseVfxPool.cs:96-99 `emission.SetBursts(new[] { new ParticleSystem.Burst(...) })` per Play; FarmingService SnapshotKeys per growth interval (line ~330).
- **Recommended fix:** Cache a static Comparison<BlockPosition> (or IComparer<BlockPosition>) instance for the fluid sort; reuse a scratch list in DrainDirtyChunks (swap pattern: return the filled scratch and clear into a second set); cache a single-element Burst[] in the VFX pool and mutate it before SetBursts. All are two-line changes with zero semantic impact.

### 14. PerformanceStatsOverlay ships an OnGUI handler (IMGUI loop) in player builds

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/PerformanceStatsOverlay.cs`
- **Impact:** Any enabled MonoBehaviour implementing OnGUI forces Unity's IMGUI event/repaint pipeline to run every frame (multiple OnGUI invocations per frame plus GUI event allocation), even when the method early-outs. The visibility guard only suppresses drawing in non-debug builds — the component, its Update sampler, and the IMGUI hook remain active in release. IMGUI also renders to the (non-existent in VR) screen overlay; the cost is small but pure waste on Quest, and the 5-second log summary builds interpolated strings forever.
- **Evidence:** PerformanceStatsOverlay.cs:79-92 OnGUI with guard `if (!visible || !Sampler.HasSamples || !(Debug.isDebugBuild || Application.isEditor)) return;` — the method still exists and is called by IMGUI each frame; Update (51-64) samples and logs every 5 s (interpolated string at 72-76).
- **Recommended fix:** Compile the OnGUI path out of release builds (#if DEVELOPMENT_BUILD || UNITY_EDITOR around OnGUI, or disable the component entirely in release via the bootstrapper), keeping the FrameStatisticsSampler/log if the telemetry is wanted. This removes the IMGUI pipeline from shipping frames entirely.

### 15. Meta avatar streaming allocates fresh byte[] payloads at 15 Hz per peer

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/MetaAvatars/MetaHorizonAvatarProvider.cs`, `Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamMessage.cs`, `Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamRelay.cs`
- **Impact:** Every avatar stream record allocates a new byte[] (MetaHorizonAvatarProvider copies the SDK auto-buffer into `streamData = new byte[byteCount]`), and every receive allocates another (MetaAvatarStreamMessage deserialization `new byte[length]`). At 15 Hz per remote peer this is continuous small-array garbage on host and clients for the lifetime of a co-op session — modest (likely a few KB/s per peer) but永-running GC pressure that pooled buffers would eliminate.
- **Evidence:** MetaHorizonAvatarProvider.cs:99-103 (`RecordStreamData_AutoBuffer(streamLod, ref streamBuffer)` then `streamData = new byte[byteCount]` copy); MetaAvatarStreamMessage.cs:35 (`Payload = length == 0 ? Array.Empty<byte>() : new byte[length];` per received message); MetaAvatarStreamRelay.cs:26-57 (LateUpdate send at streamSendRateHz = 15).
- **Recommended fix:** Reuse a persistent byte buffer per relay (record into the retained streamBuffer and pass an ArraySegment/length alongside, or pool payload arrays by size bucket) so the steady-state streaming path is allocation-free. The wire write already copies, so the buffer can be reused immediately after send.

### 16. Survival HUD rebuilds slot/recipe label strings on every inventory change

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs`
- **Impact:** LocalInventoryChanged fires after every accepted harvest/place/craft (i.e., every mined block). SurvivalHudController.OnLocalInventoryChanged then refreshes both the inventory panel (per-slot `$"x{stack.Count}"` / FormatStack interpolation across all slots) and the crafting panel (FormatRecipe string per visible recipe row). During sustained mining this allocates dozens of short-lived strings per second and re-touches every TMP label (TMP early-outs on equal text, but the strings are still allocated). The station panel demonstrates the right pattern in the same codebase (ContentVersion-gated label rebuilds with an explicit comment that 'TMP label rebuilds and the string formatting are too costly per frame in VR').
- **Evidence:** SurvivalHudController.cs:152-170 (OnLocalInventoryChanged → inventoryPanel.Refresh() + craftingPanel.Refresh()); SurvivalInventoryPanel.cs:80-109 (Refresh: per-slot `slotLabels[i].text = $"x{stack.Count}"` / FormatSlot → FormatStack interpolation at 152-160); SurvivalCraftingPanel.cs:218-232 (Refresh: FormatRecipe per label). MultiplayerSurvivalSync raises LocalInventoryChanged from SendInventorySnapshot on every accepted command (MultiplayerSurvivalSync.cs:2443-2446).
- **Recommended fix:** Only rewrite a slot label when that slot's (itemId, count, durability) actually changed — keep a small per-slot cache of last-rendered values, or version the Inventory and per-slot dirty bits. Pre-cache count strings for common stack sizes ("x1".."x99") to remove the interpolation entirely. Crafting rows only need re-formatting when craftability or the recipe list changes, not on every pickup.

### 17. MultiplayerTest scene is included in the shipping build scene list

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `ProjectSettings/EditorBuildSettings.asset`, `Assets/Blockiverse/Scenes/MultiplayerTest.unity`
- **Impact:** EditorBuildSettings includes Assets/Blockiverse/Scenes/MultiplayerTest.unity (73 KB scene plus whatever it uniquely references) as an enabled build scene alongside Boot.unity. The game never scene-switches (single-scene design), so this adds dead weight to the APK and pulls its referenced assets into the build; it also risks shipping test-only objects to store review.
- **Evidence:** EditorBuildSettings.asset m_Scenes: Boot.unity (enabled: 1) and `- enabled: 1, path: Assets/Blockiverse/Scenes/MultiplayerTest.unity`.
- **Recommended fix:** Disable or remove MultiplayerTest.unity from the build scene list (and have BlockiverseBuildSmoke/the bootstrapper assert only Boot.unity is enabled so it cannot regress). The stray Assets/InitTestScene8a89a79c-*.unity at the Assets root is not in the build list but should be deleted as repo hygiene.

### 18. App boot always generates and meshes a throwaway default world before the player picks a session

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`
- **Impact:** CreativeWorldManager.Awake unconditionally runs InitializeDefaultWorld — full 128×256×128 survival generation, sky-light build, all-chunk meshing, full collider cook, fluid sim configure, and the farming/vegetation scans — during scene load at app start, before the title menu is usable. If the player then chooses New World, Load, or Join, all of that work is discarded and repeated for the real world (Configure tears down every chunk object/mesh and rebuilds). This roughly doubles effective time-to-interactive for the most common flows and lengthens cold app start (a Quest store review metric), beyond serving as a backdrop world for the title menu.
- **Evidence:** CreativeWorldManager.cs:728-732 (`void Awake() { if (World == null) InitializeDefaultWorld(); }`) → CreateDefaultGeneratedWorld (719-726, SurvivalTerrainPreset.Generate) → ConfigureWorldRuntime → Renderer.Configure → RebuildAll; VoxelWorldRenderer.Configure (53-73) destroys and rebuilds all generated chunk content on the subsequent real-world initialization.
- **Recommended fix:** Defer world creation until a session verb runs (title menu over a skybox/static backdrop), or generate a much smaller decorative backdrop world for the title screen (e.g. 64×128×64 around spawn), or reuse the boot world when the player continues into the same seed/preset. Combined with off-thread generation (finding #1) this directly cuts cold-start time roughly in half for load/join flows.

## What Looks Good (12)

- Collider rebake throttling is well designed: MeshCollider cooking (the expensive part on Quest) is budgeted at 4 per frame with a per-frame pump and an explicit full flush only for initial world build (Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs:19-21, 245-283).
- Chunk meshes are pooled per chunk and refilled in place (no per-rebuild Mesh allocate/destroy churn), and ChunkMeshBuilder pools its solid output lists [ThreadStatic] specifically to avoid GC hitches (VoxelWorldRenderer.cs:144-153, ChunkMeshBuilder.cs:36-43).
- VoxelSkyLightMap converts what was a per-probe full column walk ('millions of reads per rebuilt chunk') into O(1) lookups maintained incrementally from block changes — a genuine algorithmic win the renderer, music, ambience, and hazard systems all share (Assets/Blockiverse/Scripts/Gameplay/VoxelSkyLightMap.cs).
- Late-join world reconstruction runs generation off the main thread via Task.Run over the engine-free sim core, with superseded-task observation and a coroutine completion gate (Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs:379-513).
- Steady-state per-frame discipline is strong across UI and presentation: change-gated repaints everywhere (BlockiverseStationPanel ContentVersion gating with an explicit VR-cost comment; BlockiverseMultiplayerSessionMenu derived-state gating; BlockiverseInputRig comfort push only on change at lines 712-741; vignette driver aperture gate), throttled polls (station scan 0.5 s, vitals 0.5 s, weather/music 1 s poll, throttled FindFirstObjectByType searches), and cached input actions.
- Networking hot paths avoid managed garbage: all wire writes use FastBufferWriter with Allocator.Temp, snapshots are sent only on state change, and the station snapshot receive path reuses a grown scratch ItemStack[] (Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs:209-212, 2199-2496).
- Object pooling exists and is correct where it matters: round-robin ParticleSystem pool (BlockiverseVfxPool), dedicated loop AudioSources per cue plus a rotating world-space one-shot source pool (BlockiverseAudioCuePlayer), and a single shared chunk material for SRP-batcher-friendly chunk rendering.
- Quest-appropriate rendering configuration: URP asset with 4x MSAA, HDR off, render scale 1, SRP batcher on, no depth/opaque texture, additional-light shadows off (Assets/Blockiverse/Settings/BlockiverseAndroidURPAsset.asset); chunk renderers cast no shadows with vertex-color lighting per Meta VRC guidance (VoxelWorldRenderer.cs:298-301); the custom voxel shader is a lean single-pass forward shader with minimal varyings (Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader).
- Player settings are correct for Quest: IL2CPP (scriptingBackend Android: 1), ARM64-only (AndroidTargetArchitectures: 2), Vulkan-only (m_APIs: 0x15), graphics jobs enabled, multithreaded rendering on, linear color space (ProjectSettings/ProjectSettings.asset).
- Audio import settings are right-sized: ~3 MB music tracks are Streaming + Vorbis (loadType: 2), short SFX/ambience loops decompress-on-load; DSP buffer 1024 ('best performance') (Assets/Blockiverse/Audio/*.meta, ProjectSettings/AudioManager.asset).
- Profiler instrumentation is in place on the exact hot paths a device profiling session needs (ProfilerMarkers on RebuildAll/RebuildDirty/RebuildChunk/ChunkMeshBuilder.Build/world Generate), plus a frame-stats overlay and periodic performance logging (PerformanceStatsOverlay, FrameStatisticsSampler).
- VegetationService's init scans were already converted to flat-array sweeps with a comment documenting the main-thread-stall rationale, and its tick paths reuse scratch lists (Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs:111-138, 146-160).

## Could Not Review (7)

- Actual on-device timings (frame rate, GPU/CPU ms, thermal throttling, GC pause durations) — static review can rank costs but not measure them; the chunk-rebuild, world-init, and autosave findings should be confirmed with the existing ProfilerMarkers on a Quest 3 via a profiling build.
- Whether URP actually executes the main-light shadow pass each frame in practice — it depends on shadow-casting renderers (Meta avatars, fallback proxies) being visible within the 50 m shadow distance; the misconfiguration is verified, the realized per-frame GPU cost is not.
- Runtime default state of Meta XR foveation when the feature is enabled but no level is set — I found no code setting a level, but the SDK/runtime default behavior needs on-device confirmation (e.g. via OVR Metrics Tool).
- Total draw call / vertex counts per frame (chunk visibility, 37 world-space canvases in the XR rig prefab, 18 TrackedDeviceGraphicRaycasters, TMP text meshes) — requires a frame capture (RenderDoc for Oculus / OVRGPUProfiler); the prefab is 1.7 MB of YAML and per-canvas enablement at runtime is driven by menu state I could not execute.
- Meta Avatars SDK internal cost (skinning, texture memory, per-avatar GPU) — third-party package internals were out of scope; only the project's streaming wrapper code was reviewed.
- Unity license/packages internals (XRI interactor per-frame raycast cost against the per-chunk TeleportationArea registry of up to 1024 interactables) — plausible but unquantifiable statically; flagging for a profiling pass rather than as a finding.
- PlayMode/EditMode test execution (forbidden by method constraints) — test code was read for coverage signals only.

## Inspected (68)

- `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`
- `Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs`
- `Assets/Blockiverse/Scripts/Gameplay/VoxelSkyLightMap.cs`
- `Assets/Blockiverse/Scripts/Gameplay/VoxelLightSampler.cs`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`
- `Assets/Blockiverse/Scripts/Gameplay/CreativeInteractionController.cs (edit path)`
- `Assets/Blockiverse/Scripts/Gameplay/WorldTimeClock.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`
- `Assets/Blockiverse/Scripts/Gameplay/TorchbudLightManager.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseLightingCycleController.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseLightingRuntime.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockVisualAtlas.cs`
- `Assets/Blockiverse/Scripts/Gameplay/EnvironmentDynamicsController.cs`
- `Assets/Blockiverse/Scripts/Gameplay/WeatherFeedbackController.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseMusicController.cs`
- `Assets/Blockiverse/Scripts/Gameplay/PerformanceStatsOverlay.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxPool.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseAudioCuePlayer.cs (scan)`
- `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`
- `Assets/Blockiverse/Scripts/Gameplay/BlockiverseLocomotionFeedback.cs`
- `Assets/Blockiverse/Scripts/Gameplay/PlacementPreview.cs (scan)`
- `Assets/Blockiverse/Scripts/WorldGen/FluidFlowService.cs`
- `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs`
- `Assets/Blockiverse/Scripts/WorldGen/WorldConstants.cs`
- `Assets/Blockiverse/Scripts/WorldGen/SurvivalLiteWorldPreset.cs (Generate)`
- `Assets/Blockiverse/Scripts/WorldGen/VegetationService.cs (tick/scan paths)`
- `Assets/Blockiverse/Scripts/Survival/FarmingService.cs (scan/tick paths)`
- `Assets/Blockiverse/Scripts/Survival/StationProximity.cs`
- `Assets/Blockiverse/Scripts/Voxel/VoxelWorld.cs`
- `Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs (BlockId)`
- `Assets/Blockiverse/Scripts/Voxel/FluidBlocks.cs (cadence/distance)`
- `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalInventoryPanel.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalCraftingPanel.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseStationPanel.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`
- `Assets/Blockiverse/Scripts/UI/UiScreenRouter.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseLocomotionRayMediator.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseSettingsPersistence.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseSystemKeyboardField.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseVignetteSettingsDriver.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseWorldSpacePanelPresenter.cs`
- `Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamRelay.cs`
- `Assets/Blockiverse/Scripts/MetaAvatars/BlockiverseMetaAvatarPresenter.cs`
- `Assets/Blockiverse/Scripts/MetaAvatars/MetaHorizonAvatarProvider.cs (record path)`
- `Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkAvatarRig.cs`
- `Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader`
- `Assets/Blockiverse/Settings/BlockiverseAndroidURPAsset.asset`
- `Assets/Blockiverse/Settings/BlockiverseAndroidUniversalRenderer.asset`
- `Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png.meta`
- `Assets/Blockiverse/Art/Textures/Items/*.meta (sampled)`
- `Assets/Blockiverse/Audio/*.wav.meta (music + ambience sampled) and file sizes`
- `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab (canvas/TMP/raycaster counts)`
- `Assets/Blockiverse/Scenes/Boot.unity (composition)`
- `Assets/XR/Settings/OpenXR Package Settings.asset (per-feature m_enabled)`
- `ProjectSettings/ProjectSettings.asset`
- `ProjectSettings/QualitySettings.asset`
- `ProjectSettings/GraphicsSettings.asset`
- `ProjectSettings/TimeManager.asset`
- `ProjectSettings/DynamicsManager.asset`
- `ProjectSettings/AudioManager.asset`
- `ProjectSettings/EditorBuildSettings.asset`
