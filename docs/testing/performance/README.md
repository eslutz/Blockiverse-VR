# Performance Reports

Store Quest performance captures and summaries here.

Minimum internal target:

- Stable 72 FPS on Quest 3 and Quest 3S, with 90 FPS treated as an optimization goal when content allows
- No runaway chunk mesh allocations, no recurring per-frame managed allocations in gameplay/input/UI hot paths, and no synchronous Addressables waits during gameplay
- No extended hitches during normal chunk rebuilds, menu routing, save/load, or world streaming transitions
- Stable two-player session performance with Meta avatar or fallback-proxy pose traffic enabled

## Instrumentation

- **In-headset HUD:** the generated World object carries `PerformanceStatsOverlay`
  (Gameplay), which shows live FPS (avg/min/max), frame time, chunk count, triangle
  count, and the rebuild queue depth in development builds. It also logs a periodic
  `Performance` summary through `BlockiverseLog` for Quest log capture.
- **ProfilerMarkers:** generation, meshing, save/load, menu routing, world-session
  transitions, and host-authoritative networking are wrapped with named markers for
  the Unity Profiler and OVR Metrics Tool. Watch at least:
  `Blockiverse.SurvivalLiteWorldPreset.Generate`,
  `Blockiverse.VoxelWorldRenderer.RebuildAll` / `RebuildDirty` / `RebuildChunk`,
  `Blockiverse.ChunkMeshBuilder.Build`,
  `Blockiverse.WorldSaveService.Save` / `Load`,
  `Blockiverse.WorldSession.ApplyLoadedWorld`,
  `Blockiverse.UiScreenRouter.PushScreen` / `PopScreen`, and
  `Blockiverse.ChunkAuthority.HandleMutationRequest` / `ApplyBufferedChunkDeltas`.
- **CPU proxy tests:** `WorldGenerationStressEditModeTests` generates and meshes the full
  canonical `survival_terrain` world or the largest currently shipped canonical preset and
  asserts the work is deterministic and bounded. Run it before each headset capture.

## Recording a capture

1. Copy `report-template.md` to `report-YYYY-MM-DD.md`.
2. Fill in the build, device, targets table, and per-scenario observations.
3. Commit the report alongside any supporting screenshots.
