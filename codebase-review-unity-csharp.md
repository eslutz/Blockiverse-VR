# Codebase Review — Unity / C# Expert

> Workflow run `wf_53b36881-009`, agent `a0dbaca8ca737cf09`. Raw expert output, pre-verification.

## Area Reviewed

Unity/C# engine-architecture review of the Blockiverse VR codebase: all 12 runtime/editor asmdefs, MonoBehaviour lifecycle usage across Gameplay/UI/VR/Networking, the Task.Run late-join world-generation path, event subscription symmetry, serialization correctness of bootstrapper-wired components (cross-checked against the generated Boot scene and XR rig prefab YAML), the 4,952-line editor bootstrapper, save/load main-thread behavior, Packages/manifest.json, and Quest/Android player, XR, URP, and quality configuration. Overall the codebase is unusually disciplined for its size — engine-free simulation core verified, careful event hygiene, defensive wire-format parsing, pooled meshes and throttled collider rebakes. However, I found one structural defect that statically breaks the entire menu system at runtime (BlockiverseMenuController is wired by the editor bootstrapper through non-serialized private fields, so every menu reference is lost when the scene/prefab is saved), plus a cluster of main-thread stall risks (synchronous world generation at boot and on load/create, synchronous autosave I/O) and a Quest-lifecycle data-loss gap (no world save on OnApplicationPause).

## Findings (13)

### 1. BlockiverseMenuController wiring is lost on serialization — entire menu system statically unwired at runtime

- **Severity:** Critical  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab`
- **Impact:** Title/pause/death/confirm/settings/new-world/load-world/world-details menus and all screen presenters have no controller references in a fresh play session or device build: the menu button does nothing (Start() only adds the MenuPressed listener when inputRig != null, and the bootstrapper deliberately scrubs the persistent listener), WireMenus() subscribes nothing, RefreshTitleMenu()/SetMenu() never run so action buttons have empty actionIds, and ApplyRouterState() iterates an empty screenPresenters list so screens never show/hide. New World, Load World, Continue, Pause, Settings, and LAN-multiplayer menu entry are all dead; only the bootstrapper-independent HUD and the default sandbox world (generated in CreativeWorldManager.Awake) function.
- **Evidence:** BlockiverseMenuController.cs lines 16-30 declare inputRig, titleMenu, pauseMenu, deathMenu, confirmMenu, settingsMenu, newWorldPanel, loadWorldPanel, worldDetailsPanel, worldDetailsMenu, comfortMenu as private fields WITHOUT [SerializeField] (only stationPanel has it, line 28); screenPresenters (line 32) is a non-serializable readonly List of tuples. These are populated only by Configure()/ConfigurePresenters(), whose sole callers are the editor-only bootstrapper (BlockiverseProjectBootstrapper.cs lines 3874-3881, inside EnsureXrRigGameMenus) — grep confirms no runtime caller exists. The saved prefab proves the loss: BlockiverseXRRig.prefab line 43425-43428 shows the serialized component carrying ONLY 'stationPanel: {fileID: 505366546020636366}'. The bootstrapper also removes the OnMenuPressed persistent listener (lines ~3925-3931: 'The controller subscribes to MenuPressed at runtime (Start → AddListener)…'), but Start() (BlockiverseMenuController.cs lines 198-199) guards that AddListener with 'if (inputRig != null)', which can never be true at runtime. ActionInvoked has no other subscriber in the codebase (grep: only WireMenus lines 529-534). BootScenePlayModeTests only asserts HUD panels and the controller-mapping popup, so tests cannot catch this.
- **Recommended fix:** Add [SerializeField] to inputRig, titleMenu, pauseMenu, deathMenu, confirmMenu, settingsMenu, newWorldPanel, loadWorldPanel, worldDetailsPanel, worldDetailsMenu (and comfortMenu), and replace screenPresenters with a serializable representation (e.g. a [Serializable] struct { string screenId; BlockiverseWorldSpacePanelPresenter presenter; } array that ConfigurePresenters fills), then rerun the bootstrapper so the prefab/scene persist the wiring. Alternatively (or additionally, as a safety net) add runtime fallback resolution in Start() that discovers the panels by component type/name under the rig, mirroring the existing fallbacks for comfortMenu/survivalSync/vitalsRuntime. Add a PlayMode test that loads Boot.unity and asserts the title menu's ActionIds are non-empty and that OnMenuPressed pushes the pause screen.

### 2. Synchronous full-world generation and mesh rebuild on the main thread at boot and on every world create/load

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`, `Assets/Blockiverse/Scripts/Gameplay/TorchbudLightManager.cs`
- **Impact:** Multi-second main-thread freezes in VR (compositor judder/black flicker, Meta VRC performance-requirement risk) at app launch and every time the player creates or loads a world. The work is also duplicated at boot: the Awake-generated default world is thrown away the moment a save is loaded or a new world is created.
- **Evidence:** CreativeWorldManager.Awake() (line 728-732) calls InitializeDefaultWorld() → CreateDefaultGeneratedWorld() which generates a 128×256×128 (4.2M-block) SurvivalTerrainPreset world (WorldGenerationSettings.CreateDefaultSurvivalTerrain, lines 19-28) synchronously, then ConfigureWorldRuntime → VoxelWorldRenderer.Configure → RebuildAll() meshes 1024 chunks and flushes ALL collider cooking ('ProcessPendingColliderRebuilds(int.MaxValue)', VoxelWorldRenderer.cs line 100), plus TorchbudLightManager.RebuildAllLights() does a per-position triple loop over all 4.2M blocks (TorchbudLightManager.cs lines 60-76). BlockiverseWorldSessionController.LoadSave (line 597-651, RegenerateBaseWorld → preset.Generate() then Renderer.RebuildAll at line 629) and CreateNewWorld (lines 256-293, GenerateWorld) repeat all of this synchronously on the main thread. The codebase itself documents the cost on the client path: MultiplayerChunkAuthoritySync.cs lines 331-334 — 'regenerating a full survival world synchronously would stall the VR main thread for seconds' — and moves the identical work to Task.Run there (line 396). The startup overlay hides on a fixed 2.25 s wall-clock timer (BlockiverseStartupOverlay.cs hideAfterSeconds), not on generation completing.
- **Recommended fix:** Reuse the proven background-generation pattern from MultiplayerChunkAuthoritySync (Task.Run pure generation + coroutine completion poll + superseded-task observation) for BlockiverseWorldSessionController.LoadSave/CreateNewWorld and for the boot default world. Defer InitializeDefaultWorld out of Awake (lazy-create only when entering the bare sandbox, or kick the generation Task in Awake and finalize on the main thread when complete), keep the loading overlay visible until the world is actually configured, and amortize the initial RebuildAll/collider flush over frames where spawn-proximity allows. Replace TorchbudLightManager.RebuildAllLights' per-position GetBlock loop with a linear scan over the backing array (the pattern VoxelWorld.CollectBlockPositions lines 78-81 prescribes for full-world sweeps).

### 3. No world save on OnApplicationPause — Quest suspend/kill loses up to 5 minutes of progress

- **Severity:** High  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseSettingsPersistence.cs`, `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- **Impact:** On Quest the normal way to leave a game is the system Home button; the app is suspended and may be killed by the OS at any time afterwards. Single-player world state is saved only by the 300 s autosave timer and explicit menu actions, so a player who exits via Home (or whose headset sleeps and the app is later killed) silently loses up to AutoSaveIntervalSeconds of building/inventory/vitals progress. Comfort settings DO save on pause, proving the pattern exists but was not applied to world saves.
- **Evidence:** Repo-wide grep for OnApplicationPause/OnApplicationQuit/OnApplicationFocus/wantsToQuit matches only BlockiverseSettingsPersistence.cs lines 38-44 (comfort/feedback settings). BlockiverseWorldSessionController saves only in Update() on the WorldSaveService.AutoSaveIntervalSeconds = 300f cadence (WorldSaveService.cs line 197; session controller Update lines 98-107) and on menu actions (HandleAction lines 153-161: PauseSaveGame/PauseReturnToTitle/DeathReturnToTitle/TitleQuit). BlockiverseMenuController.CanQuit() (lines 557-562) is false on device ('Quest apps exit via the system Home button'), acknowledging Home is the exit path — yet nothing saves there.
- **Recommended fix:** Add OnApplicationPause(bool paused) to BlockiverseWorldSessionController (and an equivalent host-save trigger in MultiplayerWorldPersistence) that calls SaveCurrentWorld() when paused && HasActiveSession. The save path is already atomic (.tmp → move with .bak recovery), so a pause-time save is safe even if the process is killed mid-write.

### 4. Synchronous save I/O plus full save-list re-enumeration on the main thread during gameplay autosave

- **Severity:** Medium  |  **Confidence:** High  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`, `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs`
- **Impact:** Every 300 s during play, the frame that triggers autosave performs full world-delta serialization, JSON writes, region-directory swap to flash storage, and then RefreshSaveList() re-reads every save manifest under persistentDataPath/Saves — all on the main thread. In VR this is a perceptible hitch that scales with edit count and number of save slots.
- **Evidence:** BlockiverseWorldSessionController.Update() (lines 98-107) → SaveCurrentWorld() (lines 167-210) constructs 'new WorldSaveService().Save(...)' synchronously, then calls RefreshSaveList() (line 199) which runs WorldSaveService.EnumerateSaves(SavesRoot) (lines 763-789), parsing every manifest on disk. WorldSaveService.Save writes manifest/dimension/containers/regions via File.WriteAllText + Directory.Move (WorldSaveService.cs lines 309, 486-574, 1193-1209).
- **Recommended fix:** Snapshot the serializable state on the main thread (changed-block list, inventory, extras are plain C# data) and run the file writes on a background Task with a completion callback, or at minimum drop the RefreshSaveList() call from the autosave path (the list only needs refreshing when the menu opens — HandleAction already refreshes on TitleLoadWorld). Keep the existing atomic .tmp/.bak protocol unchanged.

### 5. MultiplayerTest.unity enabled in EditorBuildSettings and stray InitTestScene committed at Assets root

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `ProjectSettings/EditorBuildSettings.asset`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity`, `Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs`
- **Impact:** Manual File→Build builds (anything not going through BlockiverseBuildSmoke, which overrides the scene list to Boot only) would package the MultiplayerTest scene, inflating the APK and shipping a test surface. The committed InitTestScene8a89a79c-…unity at the Assets root is a Unity test-runner artifact that adds repo noise and a phantom scene asset.
- **Evidence:** EditorBuildSettings.asset lists Boot.unity and MultiplayerTest.unity, both 'enabled: 1'; the bootstrapper enforces this (EnsureBuildScenes, BlockiverseProjectBootstrapper.cs lines 1290-1311, adds MultiplayerTestScenePath as a required enabled scene). BlockiverseBuildSmoke.BuildDevelopmentAndroid/BuildReleaseAndroid (lines 27-35, 60-68) override with 'scenes = new[] { BlockiverseProject.BootScenePath }', so script-driven builds are unaffected. InitTestScene8a89a79c-… and its .meta exist at the Assets root (ls Assets/).
- **Recommended fix:** In EnsureBuildScenes, add MultiplayerTest.unity as disabled (enabled: false) or omit it; delete Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity(.meta) and add the InitTestScene pattern to scripts/ci/forbidden-files.sh so test-runner artifacts cannot be committed again.

### 6. BlockiverseInputRig recreates InputActionReference ScriptableObjects and reader objects on every wiring pass without destroying the old ones

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs`
- **Impact:** Each of the (at least) three startup passes (Awake, Start, OnEnable all call RepairRuntimeTracking) and every subsequent enable/reconfigure allocates ~10 new InputActionReference ScriptableObject instances plus XRInputValueReader/XRInputButtonReader wrappers; the superseded ones are never destroyed. In the single-scene app they accumulate for the process lifetime — small but unbounded across repeated disable/enable cycles, and a latent confusion source when debugging input.
- **Evidence:** CreateVector2ActionReader/CreateButtonActionReader (BlockiverseInputRig.cs lines 833-864) call InputActionReference.Create(action) — a ScriptableObject.CreateInstance — and are invoked from ConfigureXriProviderInputs (lines 577-618) and EnsureRayInteractorInputs (lines 624-661); both run inside RepairRuntimeTracking, which is called from Awake (line 259), Start (line 266), and OnEnable (line 271). The class's own comment (lines 78-80) acknowledges the readers 'must only be rebuilt when a setting actually changes', yet the rebuild happens on every lifecycle pass regardless.
- **Recommended fix:** Cache the created InputActionReference per action (e.g. Dictionary<InputAction, InputActionReference>) and reuse it across passes, or guard ConfigureXriProviderInputs/EnsureRayInteractorInputs with an 'alreadyWiredForThisAsset' check (mirroring RefreshCachedActions' cachedActionAsset gate) so the readers are built once per InputActionAsset instance.

### 7. SurvivalHudController never unsubscribes craftingPanel.CraftingChanged and cratePanel.CrateChanged

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs`
- **Impact:** Asymmetric event cleanup: OnDestroy detaches the selection-changed and survival-sync handlers but not the two panel events. Today the panels are children of the HUD (destroyed together), so the leak is bounded; but if Configure() ever points the controller at external panels, the destroyed controller's RefreshPanels delegate would keep the controller alive and fire on dead UnityEngine.Objects.
- **Evidence:** Subscriptions at lines 106-116 (craftingPanel.CraftingChanged += RefreshPanels; cratePanel.CrateChanged += RefreshPanels); OnDestroy (lines 130-139) only unsubscribes selectionChangedSource and survivalSync handlers — no matching -= for the two panel events.
- **Recommended fix:** Add 'if (craftingPanel != null) craftingPanel.CraftingChanged -= RefreshPanels;' and the cratePanel equivalent to OnDestroy, matching the existing pattern used for survivalSync.

### 8. Engine-free assembly invariant is convention-only — noEngineReferences is false on Voxel/Survival/Survival.Health/WorldGen

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Voxel/Blockiverse.Voxel.asmdef`, `Assets/Blockiverse/Scripts/Survival/Blockiverse.Survival.asmdef`, `Assets/Blockiverse/Scripts/SurvivalHealth/Blockiverse.Survival.Health.asmdef`, `Assets/Blockiverse/Scripts/WorldGen/Blockiverse.WorldGen.asmdef`
- **Impact:** The project's core invariant (no UnityEngine in the simulation assemblies, enabling Task.Run world generation and plain NUnit tests) is currently enforced by nothing: any contributor can add 'using UnityEngine;' to Voxel/Survival/WorldGen and it will compile, silently breaking thread-safety assumptions of the late-join background generation path.
- **Evidence:** All four asmdefs declare "noEngineReferences": false. A repo-wide grep confirms the source currently contains no UnityEngine usage in these assemblies (the invariant holds today), so flipping the flag is safe; note Persistence also appears engine-free but its asmdef likewise leaves the flag off.
- **Recommended fix:** Set "noEngineReferences": true on Blockiverse.Voxel, Blockiverse.Survival, Blockiverse.Survival.Health, and Blockiverse.WorldGen (and Blockiverse.Persistence if it compiles cleanly) so the compiler enforces the engine-free contract. Verify Core's public API consumed by these assemblies exposes no UnityEngine types first.

### 9. Pervasive scene-scan service location (93 FindFirstObjectByType/GameObject.Find call sites) instead of serialized wiring or a composition root

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs`, `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`, `Assets/Blockiverse/Scripts/VR/BlockiverseXRRigMarker.cs`
- **Impact:** Cross-component references are resolved by scene scans in Awake/OnEnable/lazy getters (FindFirstObjectByType) and by name (GameObject.Find(BlockiverseProject.XrRigRootName) in CreativeWorldManager.PositionRigAtSpawn/PositionRig and SurvivalVitalsRuntime.BuildPlayerSaveState, despite a dedicated BlockiverseXRRigMarker component existing). This works in the single-scene architecture but makes initialization order-sensitive, costs scene walks at startup, silently no-ops when a name drifts, and was a contributing factor to the unnoticed menu-wiring break (runtime fallbacks exist for some fields and not others, masking which wiring actually functions).
- **Evidence:** grep counts 93 FindFirstObjectByType/FindObjectsByType/GameObject.Find/Camera.main sites in runtime scripts (excluding Editor). Examples: CreativeWorldManager.PositionRigAtSpawn line 738 'GameObject.Find(BlockiverseProject.XrRigRootName)'; SurvivalHudController.BindValidationState lines 72-83 (three scans per Awake); BlockiverseMenuController.HandleAction line 379 scans inside a menu-action switch. MultiplayerSurvivalSync at least logs when its fallback fires outside the lifecycle window (FindWorldManagerFallback, lines 2774-2780) — the only component that does.
- **Recommended fix:** Prefer bootstrapper-assigned [SerializeField] references (the project already generates the scene, so wiring is free), resolve the rig via the existing BlockiverseXRRigMarker instead of by name, and extend MultiplayerSurvivalSync's 'warn when the fallback fires late' pattern to the other components so silent mis-wiring surfaces in logs.

### 10. Null-conditional operator used on UnityEngine.Object-derived references, bypassing Unity lifetime checks

- **Severity:** Low  |  **Confidence:** Medium  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs`
- **Impact:** ?. and ?? on UnityEngine.Object compare against true null, not Unity's destroyed-object fake-null, so a destroyed-but-referenced component passes the check and throws MissingReferenceException (or silently operates on a dead object) when the call executes. In this single-scene game most targets live for the process lifetime, so practical risk concentrates in teardown ordering (OnDestroy chains) and world-reconfiguration paths where renderers/previews are destroyed and recreated.
- **Evidence:** Examples: 'worldManager.Renderer?.RebuildDirty()' (MultiplayerChunkAuthoritySync.cs lines 507, 765), 'Renderer?.RebuildDirty()' (CreativeWorldManager.cs line 658), 'worldManager?.ConfigureAuthoritySync(this)' (lines 84/92/949), 'avatarRig?.HeadAnchor' on a GetComponent result (MultiplayerSurvivalSync.cs line 2888), plus dozens of 'panel?.Refresh()' patterns in UI. The codebase mixes this with correct '!= null' checks (e.g. VoxelWorldRenderer.ProcessPendingColliderRebuilds line 261 properly checks 'chunkObject == null').
- **Recommended fix:** Adopt a convention: explicit '!= null' (Unity-aware) comparisons for UnityEngine.Object-derived fields, reserving ?./?? for plain C# types (services, events). Lowest-risk targets first: teardown paths and anything touched during world reconfiguration. Consider enabling the UNT0008 Unity analyzer (null-propagation on Unity objects) to enforce it.

### 11. ChunkMeshBuilder pools solid-mesh lists but allocates four fresh fluid lists on every chunk rebuild

- **Severity:** Low  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs`
- **Impact:** The stated goal of the [ThreadStatic] pooling — 'chunk rebuilds do not allocate every call (GC hitches on Quest)' — is undercut: every Build call allocates four List instances (plus growth) for fluid geometry even for the overwhelmingly common fluid-free chunk, generating steady GC pressure during edit-heavy play and the initial 1024-chunk RebuildAll.
- **Evidence:** Lines 40-43 pool pooledVertices/pooledTriangles/pooledUvs/pooledColors with a comment about avoiding GC hitches on Quest; lines 92-95 then do 'var fluidVertices = new List<Vector3>(); var fluidTriangles = new List<int>(); …' unconditionally inside Build.
- **Recommended fix:** Add a second [ThreadStatic] pooled set for the fluid lists, cleared at the top of Build exactly like the solid set (the existing ChunkMeshData aliasing contract already covers them — VoxelWorldRenderer.RebuildChunk copies both into Meshes before the next Build).

### 12. Main directional light renders hard shadows while all voxel geometry has shadow casting disabled

- **Severity:** Informational  |  **Confidence:** Medium  |  **Needs manual verification:** True
- **Files:** `Assets/Blockiverse/Settings/BlockiverseAndroidURPAsset.asset`, `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs`, `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`
- **Impact:** The URP asset enables main-light shadows (50 m shadow distance) and the bootstrapper sets the sun to LightShadows.Hard, but chunk renderers neither cast nor receive shadows by design ('voxel lighting is baked into vertex colors'). The GPU still pays for shadowmap setup/passes on Quest for at most a handful of dynamic casters (avatars), and the chunk meshes ignore the result anyway.
- **Evidence:** BlockiverseAndroidURPAsset.asset: m_MainLightShadowsSupported: 1, m_ShadowDistance: 50. Bootstrapper line 1356-1357: 'light.shadows = LightShadows.Hard; light.shadowStrength = 0.85f;'. VoxelWorldRenderer.GetOrCreateChunkObject lines 298-301: shadowCastingMode Off, receiveShadows false, with the comment citing Meta VRC guidance against extra shadow passes.
- **Recommended fix:** Either disable main-light shadows in the URP asset and set the sun to LightShadows.None in the bootstrapper (consistent with the vertex-color lighting design), or document the intentional cost if avatar/prop shadows are wanted. Profile on device to confirm the saving before/after.

### 13. Default registries and recipe books rebuilt at 43 call sites instead of shared

- **Severity:** Informational  |  **Confidence:** High  |  **Needs manual verification:** False
- **Files:** `Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs`, `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`, `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`, `Assets/Blockiverse/Scripts/Survival/ItemRegistry.cs`, `Assets/Blockiverse/Scripts/Voxel/VoxelTypes.cs`
- **Impact:** ItemRegistry.CreateDefault (127 registrations), BlockRegistry.CreateDefault (84 registrations), and CraftingRecipeBook.CreateDefault are constructed independently by HUD panels, sync components, world manager, and persistence — 43 non-test call sites. All are one-time Awake/lazy costs, so this is allocation noise rather than a hot path, but it also means components compare items across registry instances (safe today only because identity flows through canonical string ids) and a future mutable-registry change would silently diverge.
- **Evidence:** grep lists 43 CreateDefault call sites outside tests/editor, e.g. SurvivalHudController.cs:67, MultiplayerSurvivalSync.cs:403/406/2793/2796, CreativeWorldManager.cs:574/721, WorldSaveService.cs:141. Two UI panels already use the better pattern: 'static readonly ItemRegistry DefaultItemRegistry = ItemRegistry.CreateDefault();' (SurvivalInventoryPanel.cs:13, SurvivalCratePanel.cs:17). MultiplayerChunkAuthoritySync.ResolveRegistry (lines 921-934) even returns a brand-new BlockRegistry per call on clients awaiting a snapshot.
- **Recommended fix:** Expose cached singletons (e.g. ItemRegistry.Default / BlockRegistry.Default backed by Lazy<T>) since the default registries are immutable after construction, and route the 43 call sites through them; keep CreateDefault() for tests that need isolated instances.

## What Looks Good (9)

- Engine-free simulation core verified: zero UnityEngine references in Voxel/Survival/SurvivalHealth/WorldGen sources (repo-wide grep), enabling the Task.Run late-join generation and plain NUnit EditMode tests exactly as the architecture claims.
- Exemplary async pattern in Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs: background Task.Run for pure world generation, main-thread completion via coroutine poll, supersession handling, and ObserveAbandonedSnapshotTask (lines 403-414) attaching OnlyOnFaulted continuations so abandoned tasks can never surface as UnobservedTaskException.
- Disciplined event hygiene almost everywhere: idempotent '-= then +=' subscription, tracked-subscription unbind (CreativeWorldManager.subscribedWorld, SurvivalHudController.selectionChangedSource with stored handler delegate), and symmetric OnDisable/OnDestroy cleanup in BlockiverseNetworkSession, MultiplayerSurvivalSync, EnvironmentDynamicsController, TorchbudLightManager, SurvivalVitalsRuntime.
- VoxelWorldRenderer is well-engineered for Quest: pooled per-chunk Meshes (no allocate/destroy churn), throttled MeshCollider rebakes with a per-frame pump and an empty-mesh PhysX guard (lines 253-276), shadow passes disabled per Meta VRC guidance, and full teardown of generated content in OnDestroy/Configure swaps.
- Network message handlers are defensively written: every FastBufferWriter wrapped in try/finally Dispose, malformed payloads degraded instead of thrown inside the message pump (TryReadMutationRequest, ReadItemStack, ApplyInventorySnapshot shape check), and bounded duplicate-request windows (ProcessedRequestWindow, MultiplayerSurvivalSync.cs lines 2947-2966).
- Save format implementation honors the documented atomicity contract: .tmp → move/File.Replace with .bak recovery window (WorldSaveService.cs lines 309, 486-574, 1193-1209), and Android-safe filename sanitization with an explicit allowlist because Path.GetInvalidFileNameChars() is empty on Android (BlockiverseWorldSessionController.cs lines 824-848).
- Quest/Android player configuration is correct and bootstrapper-enforced: IL2CPP + ARM64-only + Vulkan-only + Linear color space + GameActivity entry + Input System only (BlockiverseProjectBootstrapper.ConfigureAndroidPlayer), URP asset with MSAA 4/no HDR, OpenXR loader assigned for Android in Assets/XR/XRGeneralSettingsPerBuildTarget.asset, and release builds explicitly restricted to the Boot scene in BlockiverseBuildSmoke.
- Lifecycle ordering subtleties are documented and handled in code: GravityProvider added before JumpProvider with the Awake self-disable reason (BlockiverseInputRig.cs lines 476-479), BlockiverseTrackedPoseDriverLifecycle (-10000 execution order) guaranteeing pose-driver disable on teardown, and the comment trail in CreativeWorldManager.ConfigureEnvironmentServices explaining the unsubscribe-from-the-right-world-instance pattern.
- Test asmdef layout is correct: UNITY_INCLUDE_TESTS defineConstraints, autoReferenced false, overrideReferences with nunit.framework.dll, properly nested per-area test assemblies (Networking/Survival/MetaAvatars), so test code can never leak into player builds.

## Could Not Review (5)

- Runtime/on-device behavior: the method forbids running Unity, so the Critical menu-wiring finding and all stall-magnitude estimates are static conclusions from serialization rules and scene/prefab YAML; a single Boot-scene play session (does the title menu respond?) would conclusively confirm finding 1.
- The full interior of the 4,952-line bootstrapper UI construction (EnsureActionMenuPanel/EnsureNewWorldMenuPanel etc. internals): I verified the menu-controller wiring path, build-scene logic, Android/Meta/URP configuration, and persistent-listener handling, but did not line-audit every generated panel hierarchy.
- Meta Avatars SDK integration depth (BlockiverseMetaAvatarPresenter against Oculus.Avatar2 internals, OVRManifestPreprocessor output): package source not inspected; only the project-side streaming relay and asmdef references were reviewed.
- Generated asset pipelines (scripts/art/generate-art-assets.py, scripts/audio/generate-audio.py) and the texture atlas contents — outside Unity/C# scope; only BlockVisualAtlas's C# consumption was touched.
- Boot.unity prefab-instance override completeness for every component (I spot-checked the menu controller, Creative World object, and rig prefab; a full GUID→script map of all 650 m_Script references in the rig prefab was not performed).

## Inspected (41)

- `Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs`
- `Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs`
- `Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs`
- `Assets/Blockiverse/Scripts/Gameplay/WorldTimeClock.cs`
- `Assets/Blockiverse/Scripts/Gameplay/SurvivalVitalsRuntime.cs`
- `Assets/Blockiverse/Scripts/Gameplay/EnvironmentDynamicsController.cs`
- `Assets/Blockiverse/Scripts/Gameplay/TorchbudLightManager.cs`
- `Assets/Blockiverse/Scripts/Gameplay/MultiplayerWorldPersistence.cs`
- `Assets/Blockiverse/Scripts/Gameplay/PerformanceStatsOverlay.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseMenuController.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseActionMenu.cs`
- `Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseMultiplayerSessionMenu.cs`
- `Assets/Blockiverse/Scripts/UI/BlockiverseStartupOverlay.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseTrackedPoseDriverLifecycle.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseCreativeInputBridge.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseLocomotionRayMediator.cs`
- `Assets/Blockiverse/Scripts/VR/BlockiverseWorldSpacePanelPresenter.cs`
- `Assets/Blockiverse/Scripts/Networking/BlockiverseNetworkSession.cs`
- `Assets/Blockiverse/Scripts/MetaAvatars/MetaAvatarStreamRelay.cs`
- `Assets/Blockiverse/Scripts/Voxel/VoxelWorld.cs`
- `Assets/Blockiverse/Scripts/Core/FrameStatisticsSampler.cs`
- `Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs (Save/atomic-write/autosave sections)`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs (Run, Android/Meta/URP config, EnsureXrRigGameMenus, EnsureBuildScenes, locomotion wiring)`
- `Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs`
- `All 12 Blockiverse asmdefs + Assets/Blockiverse/Tests/PlayMode and EditMode asmdefs`
- `Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab (BlockiverseMenuController serialized block)`
- `Assets/Blockiverse/Scenes/Boot.unity (Creative World object serialized fields)`
- `Assets/Blockiverse/Tests/PlayMode/BootScenePlayModeTests.cs`
- `Packages/manifest.json`
- `ProjectSettings/ProjectSettings.asset`
- `ProjectSettings/EditorBuildSettings.asset`
- `ProjectSettings/EditorSettings.asset`
- `ProjectSettings/QualitySettings.asset`
- `Assets/Blockiverse/Settings/BlockiverseAndroidURPAsset.asset`
- `Assets/XR/XRGeneralSettingsPerBuildTarget.asset`
- `Assets/Blockiverse/Scripts/WorldGen/WorldGenerationSettings.cs (defaults)`
