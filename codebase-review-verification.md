# Codebase Review Verification

Static adversarial verification of `codebase-review-dedup.md`. Unity/tests were not run; Low and Informational findings are pass-through by instruction.

## Summary Counts

| Verdict | Count |
|---|---:|
| confirmed | 107 |
| disputed | 2 |
| refuted | 0 |
| downgraded | 5 |
| pass-through | 70 |
| Total | 184 |

## Breakdown By Original Severity

| Original severity | confirmed | disputed | refuted | downgraded | pass-through | Total |
|---|---:|---:|---:|---:|---:|---:|
| Critical | 6 | 0 | 0 | 0 | 0 | 6 |
| High | 25 | 1 | 0 | 5 | 0 | 31 |
| Medium | 76 | 1 | 0 | 0 | 0 | 77 |
| Low | 0 | 0 | 0 | 0 | 52 | 52 |
| Informational | 0 | 0 | 0 | 0 | 18 | 18 |

## 1. clay_lump has no source — Clay Kiln chain and the entire tier 3-7 metal progression are unreachable

- Original severity | Verdict | Adjusted severity: Critical | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the progression stop: ItemRegistry.cs:35 maps Claybed to the Claybed block item while
ItemRegistry.cs:104 registers ClayLump separately; CraftingRecipeBook.cs:55-56 consumes clay_lump for Clay Kiln;
BlockHarvestRules.cs:106-107 registers Claybed/RiverSilt without a DropTable; StructureService.cs:505-526 has no clay
loot table.
- Impact lens result: Confirmed. The Clay Kiln and fired-brick recipes are survival progression gates, and no host-authority/tick/save
invariant can create the missing acquisition path.
- Final recommendation: keep at severity

## 2. Late-join world snapshot exceeds NGO's non-fragmented named-message cap (~1264 bytes) after ~77 changed blocks, breaking late join in any real session

- Original severity | Verdict | Adjusted severity: Critical | confirmed | n/a
- Evidence lens result: Confirmed. MultiplayerChunkAuthoritySync.cs:214-230 sends the late-join world snapshot on client connect;
SendLateJoinSnapshot at MultiplayerChunkAuthoritySync.cs:676-702 serializes every world.GetChangedBlocks() entry into
one named message; VoxelWorld.cs:48-67 tracks changed blocks without automatic reconciliation.
- Impact lens result: Confirmed. Any edited hosted world can accumulate enough changed blocks to exceed the non-fragmented NGO named-message
path before a late join.
- Final recommendation: keep at severity

## 3. Menu system wiring lives in non-serialized fields set at editor time — entire menu UI is inert at runtime

- Original severity | Verdict | Adjusted severity: Critical | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseMenuController.cs:16-28 keeps menu/input refs in plain private fields and only stationPanel is
serialized; Configure/ConfigurePresenters populate them at lines 57-116. BlockiverseProjectBootstrapper.cs:3873-3881
calls those methods editor-time only; the prefab serializes only stationPanel for this component.
- Impact lens result: Confirmed. The project uses a generated single Boot scene, so runtime cannot rely on editor-only Configure assignments
unless they are serialized or rewired on Start.
- Final recommendation: keep at severity

## 4. New World and Load World panels serialize zero control references — panels are non-functional even if the menu controller is fixed

- Original severity | Verdict | Adjusted severity: Critical | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseNewWorldPanel.cs:13-20 and BlockiverseLoadWorldPanel.cs:15-20 keep UI refs as non-serialized
private fields; Configure assigns them at BlockiverseNewWorldPanel.cs:52-70 and BlockiverseLoadWorldPanel.cs:27-42,
with bootstrapper-only editor-time callers.
- Impact lens result: Confirmed. New/Load screens are shipped as generated prefab/scene state; if the fields are not serialized or rebuilt
at runtime, button/list behavior is unreachable.
- Final recommendation: keep at severity

## 5. Single-player save slot silently overwritten with multiplayer/host world after joining or hosting a LAN session

- Original severity | Verdict | Adjusted severity: Critical | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseWorldSessionController.cs:34 stores currentSavePath; SaveCurrentWorld at lines 167-197 writes
worldManager.World to that path. MultiplayerChunkAuthoritySync.cs:490-493 replaces worldManager.World from a host
snapshot, while multiplayer start/join UI at BlockiverseMultiplayerSessionMenu.cs:78-110 does not clear or retarget
currentSavePath.
- Impact lens result: Confirmed. The path is reachable after single-player load/create followed by LAN join/host, and atomic save semantics
do not prevent writing the wrong world to the old slot.
- Final recommendation: keep at severity

## 6. Survival crafting UI exposes only the first 5 of ~60 recipes — no tool or station is craftable in-game

- Original severity | Verdict | Adjusted severity: Critical | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseProjectBootstrapper.cs:3024 creates exactly five recipe labels/buttons,
SurvivalCraftingPanel.cs:218-231 refreshes only recipeLabels.Length entries, and CraftingRecipeBook.cs:35-50 places
BuildTable after the first five instant recipes.
- Impact lens result: Confirmed. Survival crafting UI has no paging/scrolling fallback, so later recipes cannot be selected through the
generated VR panel.
- Final recommendation: keep at severity

## 7. Art generator script is two milestones stale; regenerating destroys the committed block atlas

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. scripts/art/generate-art-assets.py:14-15 declares ATLAS_COLUMNS=8 and ATLAS_ROWS=7, while
BlockVisualAtlas.cs:11-12 requires 8x10. The committed atlas is 128 x 160 and its .meta maxTextureSize is 256, but
generate-art-assets.py:631-647 would emit a 128x112 atlas and maxTextureSize 128.
- Impact lens result: Confirmed. The documented regeneration path can replace runtime-required atlas dimensions and make BlockVisualAtlas
reject the authored texture.
- Final recommendation: keep at severity

## 8. Attacker-controlled string length in named-message handlers triggers multi-GB host allocation (deserialization DoS)

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. Host handlers read attacker-controlled strings at MultiplayerSurvivalSync.cs:2153, 3048, and 2698. NGO
source Library/PackageCache/com.unity.netcode.gameobjects@beeaefb722f7/Runtime/Serialization/FastBufferReader.cs:618
reads an unchecked int length, line 620 multiplies it before the bounds check, and line 624 pads a string to that
length.
- Impact lens result: Confirmed. The client-to-host named-message path is reachable before semantic validation and connection approval is
disabled, so a LAN peer can hit the vulnerable reader in the host message pump.
- Final recommendation: keep at severity

## 9. Blockiverse/Voxel Lit shader is referenced by nothing and will be stripped from Quest builds, disabling all voxel lighting visuals

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. BlockVisualAtlas.cs:212-225 uses Shader.Find("Blockiverse/Voxel Lit") at runtime;
Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader:1 defines that shader, but
ProjectSettings/GraphicsSettings.asset:29-37 always-included/preloaded shader lists do not include the Blockiverse
shader asset.
- Impact lens result: Confirmed. The renderer depends on a runtime Shader.Find path; if stripped in player builds, blocks fall back to a
different shader/material behavior.
- Final recommendation: keep at severity

## 10. Custom spawn position is a world-generation input but is never persisted or transmitted — loaded worlds and late-join clients regenerate different terrain

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. Creation passes spawn into WorldGenerationSettings at BlockiverseWorldSessionController.cs:319-320; reload
reconstructs settings without spawn at BlockiverseWorldSessionController.cs:675-676.
MultiplayerChunkAuthoritySync.cs:1072-1088 writes snapshot dimensions/seed/groundHeight but no spawn;
WorldGenerationSettings.cs:46 defaults spawn to world center when omitted.
- Impact lens result: Confirmed. Save/load and late-join regeneration are reachable and deterministic only over persisted/transmitted
inputs; omitting spawn changes protected terrain columns.
- Final recommendation: keep at severity

## 11. Damage, low health, and death have no audio or haptic channel — HUD text is the only feedback

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. SurvivalVitalsRuntime.cs:188-195 applies hazard damage and lines 269-271 only raise death;
BlockiverseAudioCuePlayer.cs:8-39 has no hurt/low-health/death cue; BlockiverseInteractionHaptics.cs:101-117 maps only
accepted interaction commands to haptics.
- Impact lens result: Confirmed. Vitals damage is a runtime survival path and no upstream HUD/audio/haptic bridge covers it before death.
- Final recommendation: keep at severity

## 12. Dying while any non-HUD screen is open never shows the death screen and can permanently save a dead player

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseMenuController.OnLocalPlayerDied at lines 260-267 closes the station screen but calls
ShowDeathScreen only if the active screen is GameplayHudScreen; ShowDeathScreen itself is lines 148-151.
SurvivalVitalsRuntime.cs:269-271 emits the death event.
- Impact lens result: Confirmed. Death can occur while pause/settings/modal screens are active because pause does not stop world ticks, so
the missing routing is reachable.
- Final recommendation: keep at severity

## 13. Every surface block edit synchronously rebuilds up to ~63 chunk meshes due to the ±13-block lighting invalidation halo

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. ChunkMeshBuilder.cs:248-280 marks changed and lighting-affected chunks after block edits, with lighting
padding from VoxelLightSampler.DefaultProbeDistance+1 at line 205 and MarkLightingAffectedChunks looping chunk ranges
at lines 283-298. VoxelWorldRenderer.cs:109-122 drains dirty chunks and rebuilds meshes in Update.
- Impact lens result: Confirmed. Surface/top-block edits can fan out into many chunk rebuilds in one frame; collider rebuilds are throttled,
but mesh rebuild work remains reachable.
- Final recommendation: keep at severity

## 14. Farming loop is dead: Tiller unobtainable, three of four seeds have no source, and crops return no seeds

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. FarmingService.cs:99-105 maps seed items to crops, but grep shows seed sources are limited to item
registration and one meadow_seed structure loot. MultiplayerSurvivalSync.cs:1390-1395 requires a tiller, while
CraftingRecipeBook.cs:137-152 puts metal tool recipes behind BellowsForge and no early tiller recipe exists.
- Impact lens result: Confirmed. The survival farming loop is gated by both missing seed acquisition and missing obtainable early tiller
path.
- Final recommendation: keep at severity

## 15. Fluid spread re-triggers the chunk rebuild storm 4×/second and cooks fluid MeshColliders synchronously every rebuild

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. FluidFlowService.cs:299-315 steps fluid families on world ticks and calls world.SetBlock on spread/retract
paths; VoxelWorldRenderer.cs:180-216 assigns fluid MeshCollider.sharedMesh synchronously when rebuilding fluid meshes.
- Impact lens result: Confirmed. Fluid updates happen during normal world ticks and can enqueue renderer/collider work during gameplay.
- Final recommendation: keep at severity

## 16. Hand roles are hard-coded; no left-handed mode, and the game is unplayable one-handed

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseInputRig.cs:581-587 wires only left-hand move and explicitly creates an unused right-hand move
reader; lines 596-607 wire only right-hand turn. RefreshCachedActions at lines 305-308 binds quick menu to left
activate and break/place to right select/activate; BlockiverseProjectBootstrapper.cs:1060-1061 binds Menu to left menu
and Jump to right primary.
- Impact lens result: Confirmed. No handedness setting exists in BlockiverseComfortSettings.cs:16-24 or settings persistence, so runtime
cannot swap these bindings.
- Final recommendation: keep at severity

## 17. Hardware Menu button is double-bound: persistent comfort-panel toggle fights the pause/back routing

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseProjectBootstrapper.cs:2198-2207 removes then adds a persistent inputRig.MenuPressed listener
for the comfort presenter ToggleVisible; BlockiverseMenuController.cs:198-199 also subscribes OnMenuPressed to the
same event at runtime; SettingsOpenComfort calls comfortMenu.Show at lines 483-485.
- Impact lens result: Confirmed. The same hardware menu event reaches both pause routing and comfort panel toggling in the generated rig.
- Final recommendation: keep at severity

## 18. Hosting or joining a LAN session never transitions the menu state to gameplay — pause menu and station screens unreachable in multiplayer

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseMultiplayerSessionMenu.cs:78-110 starts host/client sessions but never calls
BlockiverseMenuController.EnterGameplay. Single-player create/load are the only session-controller paths calling
EnterGameplay at BlockiverseWorldSessionController.cs:283 and 639.
- Impact lens result: Confirmed. LAN host/join is a runtime menu action and remains on menu screens unless another path enters gameplay.
- Final recommendation: keep at severity

## 19. Inventory UI shows 6 of 44 slots with no item management; hotbar slots 7-10 unselectable

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseProjectBootstrapper.cs:2948-2950 generates six inventory slot labels/buttons/icons;
Inventory.cs:7-8 defaults to 44 slots and 10 hotbar slots; SurvivalInventoryPanel.cs:80-109 refreshes only the
generated label count.
- Impact lens result: Confirmed. The UI is the runtime inventory surface and cannot expose the remaining slots/hotbar buttons.
- Final recommendation: keep at severity

## 20. Motion vignette is a no-op at default settings and the 'Strength' slider is inverted

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseComfortSettings.cs:22-24 defaults vignette enabled with strength 1.0 and lines 62-73 map
strength 1.0 to aperture 1.0; the comfort slider writes the raw value back to VignetteStrength via
BlockiverseComfortMenu, so max strength means no vignette.
- Impact lens result: Confirmed. Smooth locomotion is enabled through the rig and the default comfort setting presents protection while
mapping to a fully open aperture.
- Final recommendation: keep at severity

## 21. Multiplayer world saves never persist any player inventory: the host's inventory is saved as empty and never restored, and remote clients' inventories are not saved at all

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. MultiplayerWorldPersistence.cs:213 and 266 call WorldSaveService.Save without an Inventory argument;
WorldSaveService.cs:214-217 routes that overload through new Inventory(itemRegistry). RestoreSavedWorldBeforeHostStart
restores world/stations/vitals at MultiplayerWorldPersistence.cs:159-170 but never calls RestoreLocalInventory.
- Impact lens result: Confirmed. Host saves/autosaves are runtime multiplayer paths, and remote inventory dictionaries at
MultiplayerSurvivalSync.cs:194-199 are not serialized.
- Final recommendation: keep at severity

## 22. No stick deadzone on Move/Turn actions; XRI SnapTurnProvider fires on any non-zero vector

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseProjectBootstrapper.cs:1051-1052 creates Move/Turn thumbstick actions without processors, and rg
found no StickDeadzone processor in the generated input path. BlockiverseInputRig.cs:581-599 feeds those raw Vector2
actions to XRI continuous move/snap turn readers.
- Impact lens result: Confirmed. Runtime movement/turn providers consume the raw stick vectors; no project-level deadzone or menu gate
mitigates drift.
- Final recommendation: keep at severity

## 23. No world save on OnApplicationPause — Quest suspend/kill loses up to 5 minutes of progress

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. rg for OnApplicationPause/OnApplicationQuit/OnApplicationFocus/wantsToQuit finds only
BlockiverseSettingsPersistence.cs:38-44 for settings; BlockiverseWorldSessionController.Update saves only every
WorldSaveService.AutoSaveIntervalSeconds at lines 98-107 and menu actions at lines 153-161.
- Impact lens result: Confirmed. Quest Home/suspend is a normal runtime path and is not covered by the explicit world-save handlers.
- Final recommendation: keep at severity

## 24. P0: Death→respawn→save ordering invariant in BlockiverseMenuController is unprotected by tests

- Original severity | Verdict | Adjusted severity: High | downgraded | Medium
- Evidence lens result: Confirmed coverage gap. rg for BlockiverseMenuController under Assets/Blockiverse/Tests returns no controller tests;
the ordering dependency is visible in BlockiverseWorldSessionController.cs:154-158 and death actions in
BlockiverseMenuController.cs:406-414.
- Impact lens result: Downgraded. The code path is important, but this finding proves missing tests rather than a current runtime defect;
High/P0 severity is inflated under the impact lens.
- Final recommendation: downgrade to Medium

## 25. P0: FillBucket/PourBucket survival commands are completely untested (offline and networked)

- Original severity | Verdict | Adjusted severity: High | downgraded | Medium
- Evidence lens result: Confirmed coverage gap. MultiplayerSurvivalSync dispatches FillBucket/PourBucket at lines 2133-2136 and implements
host paths around lines 1612 and 1680; rg under Assets/Blockiverse/Tests finds no FillBucket/PourBucket coverage.
- Impact lens result: Downgraded. Missing coverage is real, but no concrete failing runtime path is demonstrated by static evidence alone.
- Final recommendation: downgrade to Medium

## 26. P0: Reconnect-identity inventory stash/reclaim (PlayerHello GUID) has no test

- Original severity | Verdict | Adjusted severity: High | downgraded | Medium
- Evidence lens result: Confirmed coverage gap. MultiplayerSurvivalSync.cs:2641-2655 stashes disconnect inventory by GUID and lines 2693-2709
reclaim it from PlayerHello; rg under Assets/Blockiverse/Tests finds no PlayerHello/playerGuid tests.
- Impact lens result: Downgraded. This is a high-value test gap, not a confirmed live data-loss defect in the reviewed code path.
- Final recommendation: downgrade to Medium

## 27. P0: Single-player session lifecycle (BlockiverseWorldSessionController) has zero test coverage

- Original severity | Verdict | Adjusted severity: High | downgraded | Medium
- Evidence lens result: Confirmed coverage gap. rg for BlockiverseWorldSessionController under Assets/Blockiverse/Tests returns no tests,
while the class owns SaveCurrentWorld at lines 167-210, world creation at 256-324, and load/continue/save-slot flows.
- Impact lens result: Downgraded. Broad untested surface is real, but the finding does not prove a present runtime failure.
- Final recommendation: downgrade to Medium

## 28. Player inventory and shared-crate snapshots exceed the same 1264-byte message cap once inventories fill, hanging client commands mid-session

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. Inventory.cs:7 declares 44 default slots; MultiplayerSurvivalSync.WriteInventorySnapshot at lines 3005-3012
serializes every slot string/count/durability; SendInventorySnapshot at lines 2439-2470 uses a 4096-byte writer and
default SendNamedMessage delivery.
- Impact lens result: Confirmed. Filled inventories/shared crates are reachable in survival play and can exceed the non-fragmented named-
message cap used by the command response path.
- Final recommendation: keep at severity

## 29. Release APK may ship without android.permission.INTERNET: ForceInternetPermission is 0 and the custom AndroidManifest declares no INTERNET permission

- Original severity | Verdict | Adjusted severity: High | downgraded | Medium
- Evidence lens result: Confirmed static configuration. ProjectSettings/ProjectSettings.asset:187 has ForceInternetPermission: 0; rg over
Assets/Plugins/Android/AndroidManifest.xml finds no android.permission.INTERNET;
BlockiverseProjectBootstrapper.cs:373-375 invokes the OVR manifest preprocessor but no code sets PlayerSettings
internet permission.
- Impact lens result: Downgraded. Static evidence proves the release manifest relies on Unity Auto, but without a release manifest build the
failure remains conditional; High severity is not fully established by static review.
- Final recommendation: downgrade to Medium

## 30. Remote players' Meta avatars and fallback bodies render at world origin: nothing ever moves the player NetworkObject root, and the remote avatar entity ignores the synced pose

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseProjectBootstrapper.cs:1176-1184 creates the network player at Vector3.zero;
BlockiverseNetworkAvatarRig.cs:185-192 applies synchronized root pose to the NetworkObject transform, while owner pose
publishing derives from transform.position, not the XR rig world position.
- Impact lens result: Confirmed. Spawned remote player objects and avatar fallback bodies are runtime-visible; no parent/position bridge
moves the NetworkObject root to the local XR rig.
- Final recommendation: keep at severity

## 31. Router pause/world-input contract is never consumed: 'paused' screens do not pause and world input is not gated

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. UiScreenRouter.cs:61 and 64 expose IsGamePaused/AllowWorldInput, but rg finds no consumers outside
UiScreenRouter. WorldTimeClock.cs:92-105 ticks unconditionally, and SurvivalVitalsRuntime damage/tick paths have no
router/pause guard.
- Impact lens result: Confirmed. Pause/settings screens are runtime screens and do not stop world ticks, vitals, or world input contracts.
- Final recommendation: keep at severity

## 32. Sapling-grown trees (and creative-spawned trees/ruins) are written with trackChange:false — lost from saves and late-join snapshots

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. VegetationService.cs:484-488 writes generated tree blocks with world.SetBlock(..., trackChange:false);
WorldSaveService.cs:452 saves only world.GetChangedBlocks(); MultiplayerChunkAuthoritySync.cs:676-703 also snapshots
changed blocks for late join.
- Impact lens result: Confirmed. Runtime sapling growth and creative tree/ruin spawning can create world mutations outside the save/snapshot
delta set.
- Final recommendation: keep at severity

## 33. Shared crate contents are not persisted by any save path

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. WorldSaveExtras at WorldSaveService.cs:73-82 and WorldSaveData at lines 85-109 have no shared-crate field;
MultiplayerSurvivalSync.cs:416 creates the shared crate inventory fresh and line 2562 clears it on client start.
- Impact lens result: Confirmed. Shared-crate state is a runtime multiplayer inventory surface and is absent from both save paths.
- Final recommendation: keep at severity

## 34. Shared-crate (and station) transfers strip tool durability, yielding never-breaking tools

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. MultiplayerSurvivalSync.cs:1858 creates a new ItemStack(itemId,count) for crate transfers, losing
durability; station deposit follows the same count/id transfer pattern. ApplyToolDurability returns early for slots
with Durability <= 0 at lines 1749-1762.
- Impact lens result: Confirmed. Moving tools through shared crates/stations is reachable and can erase wear state before later tool use.
- Final recommendation: keep at severity

## 35. Station (smelting) panel serializes only survivalSync — labels, progress bar, close and transfer buttons all unwired at runtime

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseStationPanel.cs:18-28 keeps labels/progress/buttons as non-serialized fields and serializes only
survivalSync; Configure/ConfigureTransferControls assign refs at lines 42-70; Awake at lines 91-94 wires null buttons
if runtime Configure did not run.
- Impact lens result: Confirmed. The station panel is generated/editor-wired and has no runtime rewire path for its un-serialized controls.
- Final recommendation: keep at severity

## 36. World create/load/host-start/late-join freeze the main thread for seconds (full 4.2M-block generation + 1024-chunk re-mesh + collider cook in one frame)

- Original severity | Verdict | Adjusted severity: High | disputed | n/a
- Evidence lens result: Partially confirmed and partially refuted. Main-thread create/load evidence is real:
BlockiverseWorldSessionController.cs:301-323 and 675-679 call Generate synchronously. The late-join full-generation
part is refuted: MultiplayerChunkAuthoritySync.cs:395-396 runs GenerateSnapshotWorld via Task.Run, then
FinalizeSnapshot at lines 490-503 applies the generated world and deltas on the main thread.
- Impact lens result: Disputed. High impact remains plausible for boot/create/load/host-start, but the title overstates late-join generation
as a full main-thread generate; keep the performance finding only after narrowing that scope.
- Final recommendation: keep at severity after narrowing the late-join claim

## 37. World Details action menu has no action ids at runtime — Play/Rename/Duplicate/Delete/Back can never fire

- Original severity | Verdict | Adjusted severity: High | confirmed | n/a
- Evidence lens result: Confirmed. BlockiverseActionMenu.cs:42 stores actionIds in a non-serialized readonly List populated by SetMenu;
BlockiverseProjectBootstrapper.cs:4714 calls worldDetails actionMenu.SetMenu editor-time;
BlockiverseMenuController.Start at lines 205-209 repopulates other menus but not worldDetailsMenu.
- Impact lens result: Confirmed. World Details buttons call InvokeActionAt against an empty runtime actionIds list, so save-management
actions cannot fire.
- Final recommendation: keep at severity

## 38. "Lockstep" environmental simulation (crops, fluids, vegetation, day/night) has no resync mechanism and diverges under clock drift, app pause, and delta latency

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
WorldTimeClock.Update (lines 92-107) accumulates ticks from Time.deltaTime per peer; RestoreElapsedTicks is invoked
only from the one-shot join snapshot and save load. CreativeWorldManager.ConfigureEnvironmentServices comments the
design: 'environmental mutations are never broadcast, so host and clients simulate in lockstep' (lines 473-475) and
OnWorldTick (lines 644-660) runs vegetation/farming/fluid ticks locally on every peer. SendEnvironmentSnapshot is only
called from HandleClientConnected. FarmingService.TickGrowth anchors per-crop intervals at first local tick of the
crop (TrackCrop lines 176-181), which is latency-dependent on clients receiving the plant delta.
- Final recommendation: keep at severity

## 39. 'Engine-free simulation core' invariant is unenforced (noEngineReferences:false) and already has a UnityEngine-module dependency in WorldGen

- Original severity | Verdict | Adjusted severity: Medium | disputed | n/a
- Evidence lens result: Disputed. The unenforced guard is real: Blockiverse.Voxel.asmdef:15, Blockiverse.Survival.asmdef:16,
Blockiverse.Survival.Health.asmdef:15, and Blockiverse.WorldGen.asmdef:16 all set noEngineReferences:false. The
stronger claim that WorldGen already has a UnityEngine dependency was not reproduced: rg for UnityEngine/using
UnityEngine in Voxel/Survival/SurvivalHealth/WorldGen source found only asmdef settings and a comment, not a runtime
source dependency.
- Final recommendation: remove the UnityEngine-dependency claim; keep a narrower asmdef-hardening finding

## 40. 'Reset Height' is a no-op under Floor tracking origin, and there is no seated-play height calibration

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseHeightReset.ResetHeight (lines 19–27) only sets `origin.CameraYOffset`. BlockiverseProjectBootstrapper.cs
lines 1745 and 1815 set `origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor`; Unity's XROrigin
moves the camera offset to 0 in Floor mode, so CameraYOffset writes are ignored. The comfort menu
(EnsureXrRigComfortMenu lines 2127–2188) has no eye-height slider even though voxel_survival_menus.md §6.19 lists
'standing eye height' as a Comfort setting. EnsureXrRigSurvivalHud line 2781 fixes the HUD at localPosition (0, 1.38,
1.15) on the camera offset.
- Final recommendation: keep at severity

## 41. All player-facing UI text is hardcoded English: ~50 runtime strings across UI scripts plus 204 m_text fields baked into the generated XR rig prefab

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
MenuActions.cs:82–146 hardcodes every menu button label ("Continue", "New World", "LAN Multiplayer", "Respawn at
Bedroll", "Switch Survival/Creative", …). BlockiverseMenuController.cs:150/206–209 sets screen titles "You Died",
"Paused", "Confirm?", "Settings", "OK"; line 469 builds the delete prompt $"Delete \"{worldName}\"? This cannot be
undone.". BlockiverseWorldSessionController.cs:291,445,479–555,600,648 emits user-facing errors ("Failed to create the
world.", "Save not found.", "Enter a world name first.", …). BlockiverseMultiplayerSessionMenu.cs:82–276 contains ~20
connection status sentences. BlockiverseCreativeToolsPanel.cs:123–394 contains ~25 status strings.
SurvivalHealthPanel.cs:135–137 returns "Down"/"Critical"/"Stable". Bootstrapper makes 115
EnsureLabel/EnsureButtonControl calls with English literals (e.g. lines 2652 "Sur...
- Final recommendation: keep at severity

## 42. App boot always generates and meshes a throwaway default world before the player picks a session

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
CreativeWorldManager.cs:728-732 (`void Awake() { if (World == null) InitializeDefaultWorld(); }`) →
CreateDefaultGeneratedWorld (719-726, SurvivalTerrainPreset.Generate) → ConfigureWorldRuntime → Renderer.Configure →
RebuildAll; VoxelWorldRenderer.Configure (53-73) destroys and rebuilds all generated chunk content on the subsequent
real-world initialization.
- Final recommendation: keep at severity

## 43. Autosave serializes the whole save to flash synchronously on the main thread every 5 minutes (both single-player and host)

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
MultiplayerWorldPersistence.cs:248-275 (Update → `new WorldSaveService().Save(...)` inline, cadence
WorldSaveService.AutoSaveIntervalSeconds); BlockiverseWorldSessionController.cs:98-107 (Update → SaveCurrentWorld on
the same cadence) and 167+ (SaveCurrentWorld synchronous); WorldSaveService.cs:197 `AutoSaveIntervalSeconds = 300f`,
214-335 (Save: CreateDefault registries, hash computation, WriteJsonAtomic ×6+, WriteRegionFiles with nested
Dictionary/List allocation per save at 448-510, regions dir delete/recreate/swap at 483-492).
- Final recommendation: keep at severity

## 44. Block atlas imported with mipmaps disabled and point filtering — minification shimmer and texture-cache thrash in VR

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
blockiverse_block_atlas.png.meta: `enableMipMap: 0`, `filterMode: 0`, `aniso: 1`, Android platform override
`textureCompression: 0, overridden: 1`; atlas geometry constants BlockVisualAtlas.cs:11-13 (8×10 tiles of 16px,
UvInset 0.001). Item icons sampled (berry_cluster.png.meta): enableMipMap: 0, maxTextureSize: 64, textureCompression:
0 (138/142 metas carry the override).
- Final recommendation: keep at severity

## 45. Block interaction reach is the XRI default 30 m with no host-side reach validation

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
Grep maxRaycastDistance: zero hits in Assets/Blockiverse/Scripts; prefab lines 29846, 48869, 49783 all read
m_MaxRaycastDistance: 30 (XRI default per XRRayInteractor.cs line 204). EnsureControllerInteractors in the
bootstrapper sets lineType/raycastMask/inputs but never the distance. MultiplayerSurvivalSync grep for reach/distance
shows only tilling-water and station-proximity checks — no harvest/place reach check against the requesting player's
position.
- Final recommendation: keep at severity

## 46. BlockiverseNetworkAvatarRig falls back to scanning every Transform in the scene, potentially every LateUpdate

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseNetworkAvatarRig.cs:78–94 (LateUpdate → ApplyTrackingSources), 162–164 (ApplyTrackingSources →
ResolveTrackingSources), 270–291 (ResolveTrackingSources / FindNamedTransform iterating
FindObjectsByType<Transform>(FindObjectsInactive.Include)). Names originate in
BlockiverseProjectBootstrapper.cs:1750/1756/1819/1825. Contrast: SurvivalVitalsRuntime throttles its equivalent
fallback scan with nextClockSearchTime (SurvivalVitalsRuntime.cs:88–101).
- Final recommendation: keep at severity

## 47. BlockiverseProjectBootstrapper is a 4,952-line single static class mixing project config, asset generation, and pixel-level UI construction

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
wc -l = 4,952; method outline shows 141 method definitions in one static class; Run() at lines 197–219 sequences 15
Ensure*/Configure* phases; UI panel builders run from ~3846 to 4952.
- Final recommendation: keep at severity

## 48. BlockiverseWorldSessionController (UI assembly) owns world generation, persistence orchestration, and file management — application logic in the presentation layer

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
850-line MonoBehaviour in Blockiverse.UI: GenerateWorld/RegenerateBaseWorld (301–324, 653–680), FindSpawnForBiome
(344–384), FoldSeed (404), AllocateSavePath/SanitizeFileName (804–848), autosave Update (98–107), full LoadSave
restore pipeline (590–651). CLAUDE.md describes UI as 'menu router/panels' yet this class is the de facto application
service.
- Final recommendation: keep at severity

## 49. Breaking a timed station mid-craft destroys its inputs/fuel/output; deposited items can never be withdrawn

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
TickStations (lines 1006-1042): mismatch adds to staleStationPositions and `stationModels.Remove(stale)` with no
content handling. SurvivalCommandKind (lines 13-32) contains no withdraw-input/fuel command; ProcessHostStationCommand
switch (lines 1955-2016) handles only Open/DepositInput/DepositFuel/CollectOutput. ProcessHostHarvest grants only the
rolled block drop.
- Final recommendation: keep at severity

## 50. Canonical tick-rate and day-length constants defined independently in three assemblies

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
WorldConstants.cs:10–11 (TicksPerSecond=20, TicksPerDay=24000); SmeltingModel.cs:14 (`public const int TicksPerSecond
= 20;`); MiningFormula.cs:11 (same); FarmingService.cs:108 (`public const int TicksPerGameDay = 24000;`);
VegetationService.cs:245 (`const long WildRegrowthRetryDelayTicks = 24000;`) and :343 (raw `return 24000;`). Root
cause: WorldConstants lives in Blockiverse.WorldGen, which Blockiverse.Survival cannot reference (Survival's asmdef
references only Voxel and Survival.Health).
- Final recommendation: keep at severity

## 51. Client weather sync is discarded: the environment snapshot is applied to the pre-join world's WeatherService, which is then replaced when the host world snapshot finishes regenerating

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. HandleEnvironmentSnapshotMessage applies weather immediately at MultiplayerChunkAuthoritySync.cs:743-750.
If the boot/default world already has weatherService, CreativeWorldManager.RestoreWeatherSyncState at lines 173-180
applies it to that service; later InitializeGeneratedWorld resets services at CreativeWorldManager.cs:433-470 and only
pendingWeatherSync at lines 490-495 would survive ordering.
- Final recommendation: keep at severity

## 52. Comfort menu omits move-speed, eye-height, and UI-scale controls promised by design

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseComfortMenu.ConfigureControls only accepts glide/teleport/smoothTurn/snapTurn/vignette controls, and
EnsureXrRigComfortMenu builds no move-speed, eye-height, UI-scale, or smooth-turn-speed controls.
BlockiverseComfortSettings exposes ContinuousMoveSpeed and StandingEyeHeight and BlockiverseSettingsPersistence
persists them, but they are UI-orphaned. The ruleset requires configurable text size/UI scale; generated panels use
fixed GameMenuScale and small labels. vr-interaction also notes the quick-menu block catalog is unconditional on mode.
- Final recommendation: keep at severity

## 53. Confirm modal does not block input to the screen beneath it

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
ApplyRouterState (BlockiverseMenuController.cs:500-519) only toggles presenter visibility — the underlying screen
stays visible (visible = screenId == activeId) and nothing disables its interactivity; every panel gets its own
TrackedDeviceGraphicRaycaster (bootstrapper EnsureTrackedDeviceRaycaster at e.g. 4048, 4141, 4290, 4669) and no
CanvasGroup/blocking layer is created. The confirm dialog is 440x320 (ConfirmDialogSize, line 98) vs World Details
560x620, both presented at the same head-relative position (presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, ...)
at 4103/4720), so the larger panel's lower buttons protrude past the dialog. ConfirmAccept/ConfirmCancel are the only
paths that clear confirmCallback (lines 419-430); UiScreenRouter.ClearToRoot (UiScreenRouter.cs:90-96) silently
discards modals. OnMenuPressed's first branch (...
- Final recommendation: keep at severity

## 54. Connection approval disabled with no join gate, player cap, or encryption

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseProjectBootstrapper.cs:1250 sets `networkManager.NetworkConfig.ConnectionApproval = false;` and no
ConnectionApprovalCallback is assigned anywhere (grep across Assets/Blockiverse returns none). The prefab sets
`m_UseEncryption: 0`. BlockiverseNetworkSession.StartHost/StartClient (lines 69-104) call SetConnectionData with no
SetServerSecrets/DTLS and never register approval. There is therefore no authentication, no max-connection
enforcement, and no integrity protection on the LAN session. Each new client triggers HandleClientConnected ->
SendLateJoinSnapshot + SendEnvironmentSnapshot (MultiplayerChunkAuthoritySync.cs:214-231) and a fresh inventory.
- Final recommendation: keep at severity

## 55. Creative undo/redo histories and clipboard survive world switches and can corrupt a newly loaded world

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
WorldEditService.Undo (lines 154-169) writes changes[i].PreviousBlock unconditionally; the service instance is a
readonly field of the persistent panel (BlockiverseCreativeToolsPanel.cs:29) and is never recreated on world load.
CreativeInteractionController.Configure (lines 32-50) replaces `world` but does not clear undoStack/redoStack
(declared lines 10-12). CreativeWorldManager.ConfigureInteractionController re-Configures the same controller instance
for every loaded world.
- Final recommendation: keep at severity

## 56. Creative-tools time-of-day and time-scale sliders are not gated during LAN sessions, letting any peer desynchronize the shared world clock

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseCreativeToolsPanel.OnTimeScaleChanged (lines 282-285) and the time-of-day handler (line 279,
SetNormalizedTime) have no NetworkSessionActive() guard, while CycleWeather (lines 293-297) and CanEdit (lines
344-348) explicitly block during sessions ('Weather control is host/offline only.', 'Region tools are unavailable
during a LAN session.'). WorldTimeClock.SetTimeScale/SetNormalizedTime mutate only the local clock; nothing replicates
them.
- Final recommendation: keep at severity

## 57. Crop growth stages do not affect yield — harvesting a just-planted crop equals a mature one

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
ItemRegistry.cs:158-168 RegisterDropAlias maps GrainStalk through GrainStalk_S4 and Berrybush through Berrybush_S5 all
to the same single-item drop; BlockHarvestRules.cs:154-164 registers each stage with no DropTable (fixed Drop count
1). FarmingService.OnBlockHarvested (line 385-389) queues regrowth for any berrybush stage. Ruleset §11.2 crop table:
'Meadow Grain ... Harvest grain_bundle ×1–3', 'Berrybush ... berry_cluster ×2–4'.
- Final recommendation: keep at severity

## 58. Dead bootstrapper scene path: EnsureBootSceneInteractionTestBlock is never called, leaving BlockiverseHighlightTarget and BlockiverseHighlight.mat orphaned

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseProjectBootstrapper.cs:1599 defines EnsureBootSceneInteractionTestBlock; grep shows no call site, and
EnsureBootScene at line 1127 calls `RemoveRootGameObject(scene, InteractionTestBlockName)` instead.
BlockiverseHighlightTarget guid c618760c5a164cc090aa34ceaf345005 appears in no .unity/.prefab/.asset; its only
references are the dead bootstrapper method (line 1628) and BlockiverseInteractionPlayModeTests.cs:34
(`firstObject.AddComponent<BlockiverseHighlightTarget>()`). BlockiverseHighlight.mat guid
d1257df724dcf4ea3a018ab73b6f2fdd has zero scene/prefab references but is recreated by EnsureMaterial at
BlockiverseProjectBootstrapper.cs:717 and loaded only at the dead line 1623.
- Final recommendation: keep at severity

## 59. Death has no consequences and no bedroll recovery path; fail-state loop is toothless

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
SurvivalVitalsRuntime.cs:46-48 'Bedroll respawn anchors are not implemented yet (no bedroll block exists)...
HasBedrollSpawn => false'; Respawn() (lines 275-281) restores all vitals and repositions the rig, never touching the
inventory. Grep for bedroll/wayflag blocks in BlockRegistry/ItemId: absent. voxel_survival_menus.md §6.21 mockup
includes 'Dropped items at: X 118, Y 44, Z -39' and a bedroll respawn action; ruleset §9.2 lists the bedroll recipe
(leafmoss ×6, reed_fiber ×8, fiber_cord ×4).
- Final recommendation: keep at severity

## 60. Difficulty setting (easy/normal/hard) is purely cosmetic

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
Grep for 'Difficulty' across Scripts/: hits only NewWorldConfig (option list), BlockiverseWorldSessionController
(stores currentDifficulty into the save manifest), SaveListModel/WorldDetailsPanel (display),
MultiplayerWorldPersistence and WorldSaveService (persistence). Zero references in Survival, SurvivalHealth, WorldGen,
or Gameplay simulation code. SurvivalVitals.cs:16-22 and BlockHazards.cs:37-42 are hard constants.
- Final recommendation: keep at severity

## 61. Falling off the world edge strands the player on the invisible void floor with no recovery path

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseVoidSafetyFloor.Configure (lines 32-61): topY = -fallAllowanceMeters (-8), a plain BoxCollider with 8 m
horizontal margin — no trigger, no respawn callback. Grep shows no consumer that rescues the player (no kill-Y, no
return-to-spawn on contact). MenuActions.Pause (lines 97-106) has no respawn/return-to-spawn entry.
SurvivalVitalsRuntime.BuildPlayerSaveState (lines 212-230) saves the raw rig position, RestorePlayerSaveState (lines
243-245) restores it verbatim. CreativeInteractionController.CanPlaceBlock (line 120) rejects out-of-bounds placement,
so the player cannot build stairs back.
- Final recommendation: keep at severity

## 62. FarmingService.ScanAndTrackCrops does a full per-position world scan (4.2M GetBlock + HashSet lookups) on every world init

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
FarmingService.cs:149-173 (ScanAndTrackCrops): `for (int y...) for (int z...) for (int x...) { var pos = new
BlockPosition(x, y, z); if (CropBlocks.Contains(world.GetBlock(pos)) ...` over full Bounds. Contrast
VegetationService.cs:126-131 comment + CollectBlockPositions usage; VoxelWorld.CollectBlockPositions
(VoxelWorld.cs:81-97) is the linear flat-array sweep designed for this. Call site: CreativeWorldManager.cs:487.
- Final recommendation: keep at severity

## 63. FluidFlowService reconstructed flow levels can disagree with the live host's levels, diverging spread/retraction

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
Configure BFS (lines 129-153) calls TryImproveLevel which raises existing cells to the best path budget (lines
175-193); the live path in ProcessCell only sets flowLevels when writing into Air (lines 379-411) and OnBlockChanged
never recomputes levels for surviving cells. HasSupport (lines 418-450) compares neighbor levels, so level
disagreements change retraction outcomes.
- Final recommendation: keep at severity

## 64. Food and cooking content reduced to two raw items; campfire/prep-board food loops missing

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
ConsumableEffects.TryApply (lines 17-46) handles exactly FieldBandage, CleanWaterFlask, BerryCluster, GrainBundle.
CraftingRecipeBook: PrepBoard appears once (FieldBandage, line 119-120); Campfire recipes are CleanWaterFlask and
Brightsalt only (lines 89-94). Grep for berry_mash/flatbread/trail_ration/cooked_morsel in ItemId.cs: absent. Ruleset
§12.2 campfire table and §9.6 trail_ration ×2 / berry_mash ×1 recipes.
- Final recommendation: keep at severity

## 65. Forced 'session ended' LAN panel bypasses the router and its Close button does nothing

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
EnsureSessionEndedMenuAvailable (BlockiverseMultiplayerSessionMenu.cs:314-330) enables all parent canvases/raycasters
directly when IsShowingSessionEndedMessage, without informing the router. The Close button is persistently wired to
BlockiverseMenuController.CloseLanMultiplayerScreen (bootstrapper 3892-3897) → HandleAction(LanMultiplayerClose),
which pops only 'if (router.ActiveScreen.ScreenId == MenuActions.LanMultiplayerScreen)'
(BlockiverseMenuController.cs:359-361) — false in the forced-show case (active is GameplayHud/Title). ApplyRouterState
will hide the panel on the next router change because its presenter reports IsVisible (canvas enabled).
- Final recommendation: keep at severity

## 66. Four registered items (worldroot, deepmantle, snowpack, frostglass) have unloadable icons due to spriteMode: 2 with an empty sprite sheet

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
All four .png.meta files have `spriteMode: 2` + `sprites: []` + `internalIDToNameTable: []` (every other icon has
spriteMode: 1); BlockiverseProjectBootstrapper.cs:2898-2907 only fixes `importer.textureType !=
TextureImporterType.Sprite` then `if (sprite == null) continue;` with no warning; parsed BlockiverseXRRig.prefab icon
library block: 138 ids/138 sprite refs, and the diff against the Items folder is exactly
['deepmantle','frostglass','snowpack','worldroot']; all four ids are registered in ItemRegistry.cs (Resource items
with block refs); SurvivalInventoryPanel.cs:148-149 `slotIcons[slotIndex].enabled = icon != null`. No test references
BlockiverseItemIconLibrary.
- Final recommendation: keep at severity

## 67. Foveated rendering feature enabled but no FFR level is ever set — GPU savings never realized

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
OpenXR Package Settings.asset: 'MetaXRFoveationFeature Android' block (around line 1946) has m_enabled: 1 but the
feature block carries no level configuration; Unity's 'FoveatedRenderingFeature Android' (line ~1463) is m_enabled: 0
and 'MetaXRSubsampledLayout Android' (line ~456) m_enabled: 0. Repo-wide grep for foveat* in
Assets/Blockiverse/Scripts matches only BlockiverseProjectBootstrapper.cs:77 (feature id string
'com.meta.openxr.feature.foveation'); no runtime call sets a level.
- Final recommendation: keep at severity

## 68. Generated UI sprites (8) and VFX particle sprites (16 assets total) are produced and test-enforced but wired into nothing

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
GUID search across all .unity/.prefab/.mat/.asset: all 16 sprite guids (e.g. hotbar_frame 9227a740…,
rain_splash_particle 72a22845…) are UNREFERENCED; bootstrapper has zero occurrences of any UI/VFX sprite name and
EnsureXrRigFeedback only does EnsureComponent<BlockiverseVfxPool>(rig) without assigning particleMaterial;
BlockiverseVfxPool.cs:75-77 fallback to Sprites/Default with no texture; panel construction uses GetRoundedSprite()
(bootstrapper lines 1530, 2108, 2478); M4ArtAssetValidationEditModeTests.cs:25-47 enforces the files exist.
- Final recommendation: keep at severity

## 69. Harvest gating deviates from §6.2: wrong tool or low tier blocks the break entirely instead of slow-break-with-reduced-drops

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
ResourceHarvestService.TryPreviewHarvest line 250-251: 'if (rule.HarvestTierMin > 0 && (usedTool != rule.EffectiveTool
|| toolTier < rule.HarvestTierMin)) return ... InsufficientTool'. Ruleset §6.2 table: 'Wrong tool but sufficient tier
| Block breaks slowly; terrain drops normally; resource nodes drop nothing' and 'Correct tool but insufficient tier |
Block breaks very slowly, resource nodes drop nothing'.
- Final recommendation: keep at severity

## 70. Hold-to-mine is the only mining input; spec-required toggle-to-mine alternative is missing

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseCreativeInputBridge.cs line 35: 'Hold-to-mine (§7.3): survival break is a timed hold on a fixed target',
line 255: 'Survival mode: hold-to-mine — the press starts a timed hold'; BlockiverseInputRig.IsBreakHeld (line 95) is
polled as a release safety net. voxel_survival_menus.md §10.3 requires both hold-to-mine and toggle-to-mine. No toggle
setting exists in BlockiverseComfortSettings/BlockiverseFeedbackSettings.
- Final recommendation: keep at severity

## 71. Host never validates mining time or rate for survival harvest commands

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseCreativeInputBridge.cs:97-124 TickMining accumulates `miningElapsedSeconds += Time.deltaTime` and submits
at the threshold; ProcessHostHarvest (MultiplayerSurvivalSync.cs:1138-1243) contains no timing, cadence, or death
validation — harvest.WorkRequired (mine ticks) is computed but never compared against anything on the host.
- Final recommendation: keep at severity

## 72. Hosting is coupled to whichever world happens to be loaded: metadata mismatch blocks hosting after single-player play, and a missing multiplayer save silently adopts the current world

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
RestoreSavedWorldBeforeHostStart (lines 94-184): when no save exists it returns true keeping the current world (lines
103-109); when a save exists, SavedWorldMatchesInitializedWorld (lines 393-403) requires
width/height/depth/chunkSize/seed of the save to equal the currently initialized worldManager.World — which is
whatever the player last loaded, or the Awake() default (CreativeWorldManager.cs:728-732, seed 6401 via
CreateDefaultGeneratedWorld line 719). Nothing re-initializes the world to match the multiplayer save before the
comparison.
- Final recommendation: keep at severity

## 73. Lightning player-safety exclusion checks the origin-pinned PlayerObject root, and strike feedback (LightningStruck) fires host-only

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
IsNearAnyPlayerHead (lines 251-270) uses 'client.PlayerObject.transform.position' — the player NetworkObject root that
nothing ever moves from (0,0,0) (see the avatar-root finding; PublishPose serializes an unmoved root,
BlockiverseNetworkAvatarRig). The correct synced position is available via the rig's HeadAnchor, which
MultiplayerSurvivalSync.TryResolveClientBlockPosition already uses (MultiplayerSurvivalSync.cs:2879-2901).
LightningStruck is declared 'Fired on the host when a strike lands... clients get the block change via deltas' (lines
38-40) and TryApplyLightningStrike early-returns on non-owners (line 167), so no client-side flash/thunder event
exists.
- Final recommendation: keep at severity

## 74. Load failures are reported to the hidden title-screen status label

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
LoadSelectedSave error path: menuController?.SetTitleStatus("Save not found.")
(BlockiverseWorldSessionController.cs:445); LoadSave failure paths: SetTitleStatus($"Failed to load: {result.Error}")
(line 600) and SetTitleStatus("Failed to load the world.") (line 648). SetTitleStatus writes only to titleMenu's
status label (BlockiverseMenuController.cs:225-228). These actions fire while router.ActiveScreen is LoadWorldScreen
(pushed at line 354) — ApplyRouterState hides the title panel whenever it is not the active screen, so the message is
invisible until the user cancels back. By contrast the World Details flows correctly use the visible ShowError modal
(e.g. PlayDetailsSave, line 479).
- Final recommendation: keep at severity

## 75. Load World screen is hard-capped at 6 saves with no scrolling, paging, sort, or search

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseLoadWorldPanel.cs:13 'const int MaxEntries = 6'; RefreshList (lines 74-90) only binds entryButtons.Length
rows; the bootstrapper builds exactly 6 fixed rows with no ScrollRect (EnsureLoadWorldMenuPanel,
BlockiverseProjectBootstrapper.cs:4269, 4317-4328). SaveListModel.SetSort/SetSearch (lines 69-79) have no UI callers
(grep confirms only tests use them).
- Final recommendation: keep at severity

## 76. Loading a save instantly rotates the player's view (rig yaw) with no comfort fade anywhere

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
CreativeWorldManager.PositionRig (lines 746-753): rigObject.transform.SetPositionAndRotation(position,
Quaternion.Euler(0f, yawDegrees, 0f)); called from SurvivalVitalsRuntime.RestorePlayerSaveState (lines 243-245).
BuildPlayerSaveState (line 224) saves rig.transform.eulerAngles.y, not camera heading. Grep -i fade across
Assets/Blockiverse/Scripts matches only BlockiverseMusicController. PositionRigAtSpawn (lines 736-743) hard-teleports
on respawn (Respawn(), line 280) with no transition.
- Final recommendation: keep at severity

## 77. Menu backdrop blockiverse_launch_landscape.png ships uncompressed (1672x941 RGBA32, ~6.3 MB) with point filtering and no mips

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
PNG header: 1672x941. Meta: enableMipMap: 0 (line 9), maxTextureSize: 2048 (line 33), filterMode: 0 (line 36), Android
platform block textureCompression: 0 with overridden settings (lines 71-86). Used at BlockiverseXRRig.prefab:52532
`m_Texture: {fileID: 2800000, guid: 6e4cf5e26f6546f0b967f7ea13af3f7c}` on a UnityEngine.UI.RawImage (line 52524).
- Final recommendation: keep at severity

## 78. Mining and HUD feedback gaps include silent inventory-full harvest rejection

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseCreativeInputBridge.TickMining (lines 97-124): elapsed/required tracked privately, never surfaced to UI;
CancelMining on any release/aim change (line 105-108). SurvivalFeedbackBridge.OnCommandFeedback (lines 105-117):
harvest failure cues only for InsufficientTool. Bootstrapper HUD sections (lines 2823-2826) build
Health/Inventory/Crafting/Shared Crate only — no clock/target elements. Menus §6.6 HUD elements table.
- Final recommendation: keep at severity

## 79. Multiplayer host load bypasses RestoreWorldTimeTicks, leaving FluidFlowService's tick anchor stale — duplicated restore path causes an unbounded catch-up loop

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Single-player restore goes through CreativeWorldManager.RestoreWorldTimeTicks, which calls
fluidFlowService.SyncToWorldTick at CreativeWorldManager.cs:195-209. MultiplayerWorldPersistence.cs:160-161 instead
calls RestoreSimulationState then worldManager.WorldTimeClock?.RestoreElapsedTicks directly, bypassing
SyncToWorldTick.
- Final recommendation: keep at severity

## 80. MultiplayerSurvivalSync is a ~2,900-line god MonoBehaviour owning the entire survival economy plus its wire protocol

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
File is 3,073 lines; the MonoBehaviour spans lines 168–3073. Method map confirms the mixed concerns: TrySubmit* client
API (507–1136), ProcessHost* host logic (1138–2031), wire codec (2968–3072), network callback wiring (2620–2780),
station persistence (925–1004), inventory snapshots (3005–3056).
- Final recommendation: keep at severity

## 81. New-world creation never resets the WorldTimeClock — new worlds inherit elapsed ticks from boot/previous worlds

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
CreateNewWorld (BlockiverseWorldSessionController.cs:256-293) calls InitializeGeneratedWorld + SetGameMode +
SaveCurrentWorld with no clock reset; grep shows RestoreElapsedTicks is only called from
CreativeWorldManager.RestoreWorldTimeTicks (load/late-join paths) and MultiplayerWorldPersistence.
WorldTimeClock.Update (lines 92-107) accumulates totalElapsedTicks continuously; the clock is created once by the
bootstrapper (BlockiverseProjectBootstrapper.cs:1361) and never recreated per world.
- Final recommendation: keep at severity

## 82. No LAN discovery and no host-IP display: joining requires typing an IP the game never shows, with a useless 127.0.0.1 default

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
DescribeSessionState Hosting branch (BlockiverseMultiplayerSessionMenu.cs:223-225) interpolates
session.Config.ListenAddress (default '0.0.0.0', BlockiverseNetworkConfig.cs:10). ApplyDefaultAddressText (lines
209-213) defaults the join field to DefaultAddress '127.0.0.1'. No discovery code exists in Assets/Blockiverse/Scripts
(searched for Discovery/UdpClient/Socket/GetHostAddresses/NetworkInterface). The menus ruleset specifies manual
address entry only, so this matches spec but the spec itself omits any way to learn the host address.
- Final recommendation: keep at severity

## 83. No per-client rate limiting on the host command/mutation channels (flood + broadcast amplification)

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
HandleCommandRequestMessage (MultiplayerSurvivalSync.cs:2094-2186) dispatches every client command with no throttling;
the only bound is the per-client ProcessedRequestWindow (lines 2947-2966) which merely de-duplicates by requestId, and
a flooder simply increments requestId each call. Accepted crate transfers call BroadcastSharedCrateSnapshot (line
1905) and accepted station commands call BroadcastStationSnapshot (line 2025); HandleMutationRequestMessage commits
then BroadcastDelta to all remote clients (MultiplayerChunkAuthoritySync.cs:290-296, SendToRemoteClients 860-873). No
token-bucket or per-tick cap exists on any path.
- Final recommendation: keep at severity

## 84. Only Latin-coverage Liberation Sans fonts are bundled; CJK/Arabic/Thai/Indic text typed via the Quest system keyboard cannot render

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
The only font assets in the project are the stock TMP essentials: LiberationSans SDF.asset has m_AtlasPopulationMode:
0 (static), m_SourceFontFile: {fileID: 0}, and exactly 250 m_Unicode entries (ASCII + Latin-1). Its
m_FallbackFontAssetTable (line 7715) points only to LiberationSans SDF - Fallback.asset, which is dynamic
(m_AtlasPopulationMode: 1) but sources the same LiberationSans.ttf — Liberation Sans covers Latin/Greek/Cyrillic only,
no CJK/Arabic/Thai/Devanagari. TMP Settings.asset has m_fallbackFontAssets: [] (no global fallback) and
m_ClearDynamicDataOnBuild: 1. BlockiverseSystemKeyboardField.cs:51 opens TouchScreenKeyboard.Open(...,
TouchScreenKeyboardType.Default) and streams keyboard.text straight into TMP_InputField (lines 59–71), so any script
the OS keyboard emits reaches TMP.
- Final recommendation: keep at severity

## 85. P1: ConsumableEffects and BlockHazards tables are engine-free and completely untested

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
grep 'ConsumableEffects' under Assets/Blockiverse/Tests/ → zero hits; grep 'BlockHazards|HazardDamage' under Tests →
only PlayerVitalsEditModeTests constructing a HazardDamage directly (lines 128-167). ConsumableEffects.cs:17-46;
BlockHazards.cs:37-75.
- Final recommendation: keep at severity

## 86. P1: CreativeWorldManager save-extras round trip and container loot flow untested

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
grep
'FillSaveExtras|RestoreSimulationState|TryLootContainerInto|RestoreContainerStore|SuppressContainerAutoLoot|ContainerLooted'
under Assets/Blockiverse/Tests/ → zero hits. CreativeWorldManager.cs:217 (FillSaveExtras), 281
(RestoreSimulationState), 619-630 (TryLootContainerInto), 634-642 (RestoreContainerStore, with the 'saved state is
authoritative over regenerated loot' comment).
- Final recommendation: keep at severity

## 87. P1: DeterministicHash has no golden-value regression tests — save compatibility is unguarded

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
DeterministicHash.cs:9-39 (Hash/UnitRoll, FNV-1a-style Mix at 41-49, Avalanche at 51-62). grep 'DeterministicHash' in
Assets/Blockiverse/Tests/ shows only consumers using it to predict rolls
(CreativeWorldManagerCropTrackingEditModeTests.cs:26, FarmingServiceEditModeTests.cs:275) — no test pins concrete
output values.
- Final recommendation: keep at severity

## 88. P1: Environment snapshot (weather RNG + world tick) late-join sync and cross-peer world-sim lockstep are untested

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
MultiplayerChunkAuthoritySync.cs:229-230 (SendLateJoinSnapshot + SendEnvironmentSnapshot on HandleClientConnected),
counters at lines 56-57; CreativeWorldManager.cs:644-660 (OnWorldTick ticks all sim services unconditionally), 504-507
(fluid sim configured against the synced absolute tick for late joiners). grep 'EnvironmentSnapshot|Weather' in
MultiplayerSessionPlayModeTests.cs returns nothing.
WeatherServiceEditModeTests.RestoringFullStateKeepsTwoServicesInLockstep covers the model only, not the wire.
- Final recommendation: keep at severity

## 89. P1: Request-replay dedup and out-of-order delta guards are never exercised

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
TryRejectDuplicate called from every ProcessHost* (MultiplayerSurvivalSync.cs:1148, 1254, 1317, 1379, 1464, 1531,
1571, 1621, 1689, 1785); SurvivalCommandResult.DuplicateResult at line 129; grep 'Duplicate' in
MultiplayerSessionPlayModeTests.cs → zero hits. MultiplayerChunkAuthoritySync.cs:54 and 782
(IgnoredOutOfOrderChunkDeltaCount) — no test references.
- Final recommendation: keep at severity

## 90. P1: Station panel UI and station persistence glue (Export/RestoreStationStates) untested

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
grep 'BlockiverseStationPanel' under Assets/Blockiverse/Tests/ → zero hits; grep
'ExportStationStates|RestoreStationStates' under Tests → zero hits (RestoreStationStates declared at
MultiplayerSurvivalSync.cs:982). SmeltingStationModelEditModeTests covers the model (incl.
ApplyHostSnapshotMirrorsHostState) but not the sync-level export/restore or the panel.
- Final recommendation: keep at severity

## 91. P1: SurvivalVitalsRuntime (hazard scan, death event, respawn resolution, world drink, player save state) is untested

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
grep 'SurvivalVitalsRuntime' under Assets/Blockiverse/Tests/ → zero hits. Untested members:
TickHazards/CheckHazardCell/TryApplyHazard (147-195), InSurvivalMode gate (132-133), OnWorldTick starvation wiring
(135-143), TryDrinkFromWorldSource cooldown (259-267), BuildPlayerSaveState (212-230), RestorePlayerSaveState
null→ResetVitalsToFull (235-248), Respawn + ResolveSpawnPosition FindSurfaceY fallback (275-298).
- Final recommendation: keep at severity

## 92. P1: Tested autosave helper is dead code; both live autosave loops are untested

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
grep -rn 'ShouldAutoSave' in Assets/ excluding Tests matches only its declaration (WorldSaveService.cs:207).
BlockiverseWorldSessionController.cs:98-107 and MultiplayerWorldPersistence.cs:248-256 both inline `Time.unscaledTime
- last... < WorldSaveService.AutoSaveIntervalSeconds` instead.
- Final recommendation: keep at severity

## 93. Pause-menu Save Game and autosave give no user feedback on success or failure

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
HandleAction case PauseSaveGame only invokes ActionRequested (BlockiverseMenuController.cs:375-377);
BlockiverseWorldSessionController.SaveCurrentWorld returns bool and logs on exception (lines 167-210) but no caller
surfaces the result — HandleAction (line 153-161) ignores it. The pause BlockiverseActionMenu has a status label built
by the bootstrapper (statusLabel, BlockiverseProjectBootstrapper.cs:4093-4097, wired via Configure at 4100) and
BlockiverseActionMenu.SetStatus exists (lines 93-97), but the only SetStatus caller is the title path
(SetTitleStatus).
- Final recommendation: keep at severity

## 94. Pervasive English sentence-fragment composition: concatenated/interpolated UI strings with fixed word order and no pluralization support

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
SurvivalCraftingPanel.cs:309–328: FormatRecipe = $"{FormatStack(recipe.Output)} - {FormatIngredients(recipe)}" plus
appended " [needs {station}]"; FormatStack = $"{definition.Name} x{stack.Count}".
SurvivalInventoryPanel.cs:94/108/158: $"x{stack.Count}", $"Hotbar {n} / {total}", "{Name} x{Count}".
SurvivalCratePanel.cs:74/100: $"Deposited {FormatStack(held)}" / $"Withdrew {FormatStack(stack)}".
SurvivalHealthPanel.cs:105: $"{baseState} · Hunger {hunger} · Thirst {thirst} · Stamina {stamina}".
BlockiverseLoadWorldPanel.cs:86: $"{Name} · Day {DayCount}". BlockiverseWorldDetailsPanel.cs:51–54 fuses three lines
of "Label: value" pairs into one format string. BlockiverseMultiplayerSessionMenu.cs:260/270 appends sentences:
$"{reconnectMessage} Last disconnect: {reason}".
- Final recommendation: keep at severity

## 95. Pervasive FindFirstObjectByType/GameObject.Find service-locator idiom (~79 runtime call sites in 36 files) hides the scene wiring contract

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
grep across Assets/Blockiverse/Scripts (excluding Editor) shows 79
FindFirstObjectByType/FindObjectsByType/GameObject.Find call sites in 36 runtime files. Representative:
BlockiverseWorldSessionController.cs:81–93, BlockiverseMenuController.cs:212/245/275/332/379,
CreativeWorldManager.cs:464/679/682, MultiplayerWorldPersistence.cs:349–359, SurvivalVitalsRuntime.cs:92–108,
WeatherFeedbackController.cs:59–65.
- Final recommendation: keep at severity

## 96. Placed storage containers cannot be opened; container menu and §10.5 special rules missing

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
MultiplayerSurvivalSync.cs:188 'const int SharedCrateSlotCount = 12'; TrySubmitUse (lines 583-641) routes station
blocks to StationOpenRequested but has no container-open branch — placed crates fall through to the generic paths.
ProcessHostHarvest (lines 1183-1191) is the only container access (TryLootContainerInto on break).
ContainerInventoryStore.DefaultContainerSlotCount = 12 vs ruleset §10.5 'Storage Crate | 24'. ItemRegistry registers
ReedBasket/ToolRack/PantryJar/DeepLocker (lines 73-76) but CraftingRecipeBook has no recipes for them.
- Final recommendation: keep at severity

## 97. Raw canonical IDs and C# enum identifiers are rendered directly as UI text (untranslatable by construction, and rough even in English)

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseNewWorldPanel.cs:40–47/128: ValueGetters return Config.GameMode/WorldPreset/StartingBiome (canonical
strings from NewWorldConfig.cs:12–19, e.g. "survival_terrain") straight into cycleValueLabels[idx].text.
BlockiverseCatalogBrowserPanel.cs:172: categoryLabel.text = Categories[categoryIndex].ToString()
(CreativeCatalogCategory members include DeepRock). CraftingRecipe.cs:21–33: CraftingStationNames.DisplayName builds
UI text by inserting spaces into the enum identifier ("ClayKiln" → "Clay Kiln"). BlockiverseStationPanel.cs:170/184:
$"Cannot deposit: {result.FailureReason}" prints enum member names. SurvivalCraftingPanel.cs:142/164 same pattern with
CraftingFailureReason. BlockiverseWorldDetailsPanel.cs:48–49 Capitalize(save.GameMode) re-cases the canonical id for
display. BlockiverseCreativeToolsPanel.cs:123 shows the WeatherSt...
- Final recommendation: keep at severity

## 98. Reconnect inventory stash and GUID bindings leak across sessions and worlds, and PlayerHello identity is unauthenticated

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
ClearSessionState (lines 2598-2606) clears inventoriesByClientId, processedRequestsByClientId, stationModels, and
pending commands but not playerGuidsByClientId (line 198) or stashedInventoriesByGuid (line 199).
HandleClientDisconnected (lines 2641-2655) stashes by GUID; HandlePlayerHelloMessage (lines 2693-2709) binds
senderClientId to any received GUID string and immediately hands over any stash with no verification and no per-world
scoping.
- Final recommendation: keep at severity

## 99. Ruleset drop tables, yield bonuses, and ground-item rules are almost entirely unimplemented

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
Repo grep for 'new DropTable' in runtime code returns exactly two sites (BlockHarvestRules.cs:124, 153).
ResourceHarvestService.RollDrop (lines 185-204) handles only Sickle double-roll and Carver full-yield; no tier-based
bonus branch exists. Grep for GroundItem/DroppedItem/despawn/pickupRadius across Scripts/ returns nothing.
ResourceHarvestService.TryPreviewHarvest (lines 254-263) returns InventoryFull failure, blocking the break.
ItemRegistry.cs:83 comment admits 'block mapping used for harvest drop lookup until M6 drop tables'.
- Final recommendation: keep at severity

## 100. Save/load orchestration duplicated between BlockiverseWorldSessionController (UI) and MultiplayerWorldPersistence (Gameplay), and the copies have already diverged

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BuildSavedContainers: UI lines 226–252 vs Gameplay lines 278–310 (verbatim duplicate, modulo loop variable).
BuildSaveExtras: UI 214–224 vs Gameplay 234–244 (identical). RestoreContainers: UI 682–698 (no slot validation) vs
Gameplay 312–341 (validates and logs invalid slots). Autosave Update loop duplicated: UI 98–107 vs Gameplay 248–275.
- Final recommendation: keep at severity

## 101. Spec-mandated feedback-toast/subtitle layer is absent; several events are audio-only with no visual channel

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
voxel_audio_vfx_ruleset.md line 81 lists `BlockiverseSubtitleToastPanel // optional accessibility layer`, line 115
'Subtitles/Feedback Toasts toggle', and §15 requires 'Feedback Toasts: Shows text/icon confirmation for important
audio-only events'. Grep for 'Subtitle|Toast' across Assets/Blockiverse/Scripts finds no runtime implementation.
SurvivalFeedbackBridge.cs lines 113–116 plays only `BlockiverseAudioCue.ToolWrong` for InsufficientTool rejections (no
status text), and lines 152–166 play MultiplayerJoin/Leave cues with no visual counterpart. voxel_survival_menus.md
§10.4 specifies toast strings ('This tool is not strong enough.', 'Game saved.') that have no UI surface.
- Final recommendation: keep at severity

## 102. Structure catalog 8/22 implemented; all underground loot-tier structures missing and the watchpost cannot spawn on small worlds

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
StructureService definitions (lines 82-93) list exactly: pathmark_stones, forager_lean_to, resin_tap_grove,
frost_shelter, drybrush_niter_pit, weathered_watchpost, cave_shrine, bridge_segment. Ruleset §12.1-12.4 lists 22
structures incl. 6 underground. Loot tables: only CommonSupply and ForagerFood (lines 505-526). weathered_watchpost
minDistanceFromSpawn: 128 (line 87); BlockiverseWorldSessionController.SizeFor default returns (128,128) with spawn at
width/2 (WorldGenerationSettings.cs:46) — max distance to a corner is sqrt(64²+64²) ≈ 90.5.
- Final recommendation: keep at severity

## 103. Sun light forces Hard realtime shadows every frame while URP main-light shadows are enabled at 2048 — contradicting the project's own no-shadow design

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseLightingCycleController.cs:41-44 (LateUpdate → ApplyCurrentLighting) and 70-71 (`sunLight.shadows =
LightShadows.Hard; sunLight.shadowStrength = 0.85f;` every frame); BlockiverseAndroidURPAsset.asset:
`m_MainLightShadowsSupported: 1`, `m_MainLightShadowmapResolution: 2048`, `m_ShadowDistance: 50`;
BlockiverseVoxelLit.shader:26 `#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE`, :73
GetShadowCoord, :81 GetMainLight(input.shadowCoord); VoxelWorldRenderer.cs:298-301 (shadowCastingMode Off + Meta VRC
comment), :229 fluid `receiveShadows = true`.
- Final recommendation: keep at severity

## 104. Survival HUD and launch panels stay visible on title/non-gameplay screens

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
EnsureXrRigSurvivalHud parents the HUD under Camera Offset, enables the Canvas, and registers no presenter or router
visibility hook. BlockiverseMenuController.ConfigurePresenters registers no GameplayHudScreen presenter for the HUD,
so ApplyRouterState never hides it. The controller-mapping popup is configured showWhenStarted true with no first-run
PlayerPrefs gate, placing three panels in front of the player at launch.
- Final recommendation: keep at severity

## 105. Survival threat profile is near zero: three contact hazards, 1 HP/min starvation, no fall damage, no night/cold pressure

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockHazards.HazardForBlock (lines 53-75) contains exactly thornbrush, campfire, emberflow(+flow). SurvivalVitals
constants (lines 16-22): HungerTicksPerPoint 240 (20 min full drain), ThirstTicksPerPoint 180 (15 min),
StarvationDamageIntervalTicks 1200 = 1 HP/min. No code path applies fall damage (no caller of ApplyDamage tied to
velocity/height); HazardDamageKind.Cold/Void/Suffocation/Toxic enum values (HazardDamage.cs:5-13) have no producers.
- Final recommendation: keep at severity

## 106. Survival/creative mode switch has no permission model: one pause-menu click grants vitals immunity, and the host can place free blocks in survival worlds

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
MenuActions.cs:101 adds PauseToggleMode to the pause menu unconditionally; BlockiverseMenuController.cs:378-380 routes
it to ToggleSurvivalCreativeMode with no world-mode or permission check. SurvivalVitalsRuntime.InSurvivalMode (lines
132-133) gates all vitals decay and hazards on the local mode. MultiplayerChunkAuthoritySync.cs:274-287 rejects direct
mutations only when '!senderIsHost && ... GameMode == Survival'. Creative ruleset line 925: 'Pause | Adds mode switch
if permission allows' and §2 permission schema.
- Final recommendation: keep at severity

## 107. Test-only scenes and Unity Test Runner artifacts are committed or enabled in build settings

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. ProjectSettings/EditorBuildSettings.asset:12 lists Assets/Blockiverse/Scenes/MultiplayerTest.unity as an
enabled build scene, and Assets/InitTestScene8a89a79c-5099-46ed-bbb3-77ff7893a809.unity is present under Assets.
- Final recommendation: keep at severity

## 108. UI assembly hardcodes WorldGen's internal biome enum order as magic indices

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseWorldSessionController.cs:386–401: comment 'TerrainBiome is internal to WorldGen; the resolver exposes
biome indexes, whose order is canonical' followed by hardcoded `case "meadow": return 0; … case "highlands": return
6;`. SurvivalLiteWorldPreset.cs:8: `enum TerrainBiome { Meadow, Pinewild, Wetland, Drybrush, Dunes, Tundra, Highlands
}` (internal, so UI cannot reference it).
- Final recommendation: keep at severity

## 109. Up to 24 realtime per-pixel point lights from torch blocks under Forward rendering

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
TorchbudLightManager.cs:11-13 (MaxActiveLights = 24, LightRange = 6.0f, LightIntensity = 2.2f), 111-122 (AddLight
creates LightType.Point, LightRenderMode.Auto); BlockiverseAndroidURPAsset.asset: m_AdditionalLightsRenderingMode: 1,
m_AdditionalLightsPerObjectLimit: 4, m_RenderingMode: 0 in BlockiverseAndroidUniversalRenderer.asset;
BlockiverseVoxelLit.shader:86-94 per-pixel additional light loop.
- Final recommendation: keep at severity

## 110. VoxelWorld changed-block delta set grows without reconciliation — bloats memory, saves, and the late-join snapshot

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
VoxelWorld.cs:23 `readonly Dictionary<BlockPosition, BlockChange> changedBlocks = new();` and 53-70 SetBlock
(`changedBlocks[position] = change;` — no removal path other than ClearChangedBlocks);
MultiplayerChunkAuthoritySync.cs:676-703 SendLateJoinSnapshot (`int writerSize = SnapshotHeaderBytes +
changedBlocks.Count * SnapshotBlockBytes;` single buffer/message); WorldSaveService.cs:452 `foreach (BlockChange
change in world.GetChangedBlocks())`.
- Final recommendation: keep at severity

## 111. World load path runs the full 1024-chunk RebuildAll twice (and host-start can too)

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseWorldSessionController.cs LoadSave: line ~609 `worldManager.InitializeGeneratedWorld(generated,
chunkAuthoritySync)` (triggers VoxelWorldRenderer.Configure → RebuildAll at VoxelWorldRenderer.cs:72), then line ~629
`worldManager.Renderer?.RebuildAll();` after result.ApplyTo. MultiplayerWorldPersistence.cs:178
`worldManager.Renderer?.RebuildAll();` after a load whose world may have just been initialized (line 377
InitializeDefaultWorld inside TryResolveWorldManager).
- Final recommendation: keep at severity

## 112. World-name sanitizer flattens every non-ASCII character to underscore in the player-visible name, not just the directory name

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseWorldSessionController.cs:828–848: SanitizeFileName replaces any char failing IsSafeFileNameChar (ASCII
letters/digits/space/_/-/./()) with '_'. AllocateSavePath (lines 804–818) builds candidateName from the sanitized
baseName and returns it as the worldName. CreateNewWorld line 278 then assigns `(currentSavePath, currentWorldName) =
AllocateSavePath(config.Name.Trim())` — so the sanitized string becomes the manifest WorldName shown in Load World,
World Details, and rename. The comment at 824–827 explains the sanitizer exists because Path.GetInvalidFileNameChars()
is empty on Android, but the allowlist is far stricter than filesystem safety requires.
- Final recommendation: keep at severity

## 113. World-preset construction/dispatch logic implemented four times across three assemblies with magic preset strings

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
BlockiverseWorldSessionController.cs:301–324 and 653–680 (two near-identical switches in the same file);
MultiplayerChunkAuthoritySync.cs:416–452 (third copy, switching on the enum); CreativeWorldManager.cs:719–726 (fourth,
survival-only). Preset string literals: NewWorldConfig.cs:15, BlockiverseWorldSessionController.cs:37/305/312/657/666,
MultiplayerWorldPersistence.cs:19, WorldSaveService.cs:214/219/252/428.
- Final recommendation: keep at severity

## 114. Worldroot is breakable and collectible, contradicting its 'unbreakable bottom crust' role

- Original severity | Verdict | Adjusted severity: Medium | confirmed | n/a
- Evidence lens result: Confirmed. Reproduced the cited static evidence or found no contradictory guard in the cited files. Key cited proof:
VoxelTypes.cs:273 registers Worldroot with hardnessClass VeryHard (= 6.0f per HardnessFromClass line 115) and
harvestTierMin 3 — not infinite. ItemRegistry.cs:44 registers a collectible Worldroot item; BlockHarvestRules.cs:114
registers a Delver harvest rule for it. Ruleset §2: 'Worldroot | worldroot | Unbreakable bottom crust. Prevents
falling out of the world. | ∞ | — | None'.
- Final recommendation: keep at severity

## 115. 'Infinite' world size silently creates a 256x256 bounded world

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 116. Avatar pose and Meta-avatar streams use only reliable delivery with no remote interpolation; streams are echoed back to their sender and unbounded to 64 KB

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 117. Berry/crop ripeness and ore identification may rely on hue contrasts weak for red-green colorblindness; no colorblind options

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 118. Block/item display names are constructor literals inside engine-free assemblies, and ItemRegistry keys a lookup on the English display name

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 119. BlockiverseInputRig recreates InputActionReference ScriptableObjects and reader objects on every wiring pass without destroying the old ones

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 120. Bootstrapper invokes a private Netcode settings method via reflection with a silently-swallowed failure

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 121. ChunkMeshBuilder allocates four new fluid lists on every chunk rebuild while the solid lists are pooled

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 122. Comfort panel has no Close button and is not router-managed

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 123. Container auto-loot during harvest can consume the capacity the harvest preview validated, silently destroying the crate drop

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 124. Controller Undo binding deliberately removed but dead undo plumbing remains; per-block undo/redo unreachable in VR

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 125. Creative mode lacks flight despite the creative ruleset defaulting it on

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 126. Cross-class temporal couplings documented only in comments (death-respawn-before-save, station-tick clock split)

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 127. Dates and numbers are displayed without regional formatting (invariant dates, English decimal conventions); no current parsing risk

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 128. Definitely-dead public methods: ConfigureWorld, InitializeAuthoritativeWorldSnapshot, TrackChangedBlock, ResetToFullHealth, CanStackWith, SelectPrevious, ApplyRemoteStreamForTests

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 129. Eleven orphan item icons have no registered item id (legacy ids and unimplemented ruleset foods)

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 130. Experimental Rules toggle from the menus ruleset is unimplemented — NewWorldConfig.ToggleExperimentalRules/ExperimentalRulesEnabled are test-only vestiges

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 131. Fixed-size world-space panels with Truncate overflow and no auto-sizing — zero tolerance for translated-text expansion

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 132. Gameplay layer reaches the XR rig via GameObject.Find string lookups to work around the assembly layering

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 133. Host cannot stop the session while the shutdown save keeps failing (deliberate abort with no override)

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 134. Implemented menus deviate from voxel_survival_menus.md in several documented capabilities

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 135. Meta avatar streaming allocates fresh byte[] payloads at 15 Hz per peer

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 136. Naming drift across the three preset identifier systems, including a vestigial wrapper class and a mis-named file

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 137. No quick 180° turn-around option in snap turning

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 138. Null-conditional operator used on UnityEngine.Object-derived references, bypassing Unity lifetime checks

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 139. P2: No item-icon coverage validation test (blocks have one; items do not)

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 140. P2: Offline host-rejection matrix for survival commands is only 2 cases deep

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 141. P2: PlayerVitals.RestoreHealth / SurvivalVitals.RestoreFrom clamp contracts untested

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 142. Pause menu always offers 'Quit Game' on Quest while the title menu deliberately hides Quit, and quit paths skip the spec'd confirmation

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 143. Per-tick and per-step micro-allocations in the world simulation loop

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 144. PerformanceStatsOverlay and FrameStatisticsSampler have no runtime or scene wiring — manual-attach dev tooling only

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 145. PerformanceStatsOverlay ships an OnGUI handler (IMGUI loop) in player builds

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 146. Reduced Flash only attenuates lightning to 35% instead of offering full suppression

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 147. Registry-hash ID ordering uses the culture-sensitive default string comparer

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 148. Right-stick UI scrolling triggers snap turns simultaneously; falls bypass the comfort vignette

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 149. Seven per-assembly marker classes are unreferenced placeholders

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 150. SmeltingStationModel.TryDepositInput accepts stacks exceeding MaxStackSize into empty slots

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 151. Stale Source/fired_brick.png block tile duplicates fired_brick_block.png under the old name

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 152. Stamina is a dead stat: displayed and restored but never consumed

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 153. Station and container save slots drop item durability

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 154. Station panel is not closed (or refreshed) when its station block is destroyed or becomes stale

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 155. StructureTerrainFit enum is declared and never used anywhere

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 156. Survival command channel accepts any inventory slot as 'equipped' and other minor validation gaps

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 157. Survival command channel is not gated by world game mode: clients can run the survival economy in creative multiplayer worlds

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 158. Survival HUD rebuilds slot/recipe label strings on every inventory change

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 159. Survival mode is selectable with void_builder and flat_builder presets that cannot sustain vitals

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 160. SurvivalHudController never unsubscribes craftingPanel.CraftingChanged and cratePanel.CrateChanged

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 161. UI feedback discovery (audio cue + haptics resolution) copy-pasted across at least 8 panel classes

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 162. Unobtainable registered content and stack/vocabulary deviations from canon

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 163. World Details operations resolve saves by display name while the Load path uses collision-safe keys — destructive ops can target the wrong slot

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 164. WorldSaveService: 1,273-line file with 15 types, a 13-parameter Save overload, and a 240-line WriteRegionFiles method

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 165. ~28 test-only telemetry counters baked into MultiplayerSurvivalSync's public API

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 166. ~35 scattered BlockRegistry/ItemRegistry/CraftingRecipeBook CreateDefault() construction sites with no shared canonical instance

- Original severity | Verdict | Adjusted severity: Low | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 167. 46 item icons have no matching ItemId, and BlockiverseHighlight.mat plus TunnelingVignette.prefab are referenced by nothing

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 168. Android adaptive/round launcher icon slots are empty — only the legacy 192px icon is set

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 169. Assets/CompositionLayers/UserSettings (per-user preference assets) is committed to the repo

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 170. Crafting panel lacks per-recipe availability symbols specified by the accessibility rules

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 171. Crop growth conditions are permanently 'favorable' — §11.2 light/moisture gating is unwired

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 172. EditMode tests pin exact English UI strings, coupling the test suite to the unlocalized copy

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 173. EnsureOvrAvatarManager's creation branch is dead code (UNITY_ANDROID && !UNITY_EDITOR inside an editor-only assembly)

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 174. Informational: production sync classes carry large test-only counter surfaces; exact-value counter assertions over-couple tests to internals

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 175. Informational: registry-hash-mismatch load path only logs a warning and that branch is untested

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 176. Leaf decay only recognizes BranchwoodLog as support — stripping a living tree's trunk dissolves its canopy

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 177. Minor feedback inconsistencies: stone mining sounds soft, controls text omits left-hand teleport, no snap-turn/teleport haptic

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 178. No hand-tracking input path; controllers are mandatory

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 179. No host migration; host disconnect ends the session for everyone (by design, with progress bounded by the host's last save)

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 180. No localization system exists: no package, no string tables, no language setting, no locale detection

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 181. Production chunk material is named 'BlockiverseTestBlock'

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 182. Sapling blocks have no harvest rule — placed saplings cannot be removed in survival

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 183. Test-only public API surface across runtime assemblies (12 members) plus unused constant WorldConstants.WorldMinY

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through

## 184. XR rig prefab carries a NetworkBehaviour (BlockiverseNetworkAvatarRig) with no NetworkObject

- Original severity | Verdict | Adjusted severity: Informational | pass-through | n/a
- Evidence lens result: Pass-through. Low/Informational findings were not verified by instruction; original evidence was carried forward
without an evidence-lens verdict.
- Final recommendation: keep as pass-through
