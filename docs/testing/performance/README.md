# Performance Reports

Store Quest performance captures and summaries here.

Minimum internal target:

- Stable 72 FPS on Quest 3 and Quest 3S
- No runaway chunk mesh allocations
- No extended hitches during normal chunk rebuilds
- Stable two-player session performance

## Instrumentation

- **In-headset HUD:** `PerformanceStatsOverlay` (Gameplay) shows live FPS (avg/min/max),
  frame time, chunk count, triangle count, and the rebuild queue depth. It also logs a
  periodic `Performance` summary through `BlockiverseLog` for Quest log capture.
- **ProfilerMarkers:** generation and meshing are wrapped with named markers
  (`Blockiverse.SurvivalLiteWorldPreset.Generate`,
  `Blockiverse.VoxelWorldRenderer.RebuildAll` / `RebuildDirty` / `RebuildChunk`,
  `Blockiverse.ChunkMeshBuilder.Build`) for the Unity Profiler and OVR Metrics Tool.
- **CPU proxy tests:** `WorldGenerationStressEditModeTests` generates and meshes the full
  default survival-lite world (the largest world the game ships) and asserts the work is
  deterministic and bounded. Run it before each headset capture.

## Recording a capture

1. Copy `report-template.md` to `report-YYYY-MM-DD.md`.
2. Fill in the build, device, targets table, and per-scenario observations.
3. Commit the report alongside any supporting screenshots.
