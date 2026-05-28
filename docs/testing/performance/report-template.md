# Performance Report — <date> / <build>

Copy this file to `report-YYYY-MM-DD.md` and fill in each section from a headset capture.

## Build under test

- Build: <development / release APK file name>
- Commit: <short SHA>
- Device(s): Quest 3 / Quest 3S
- OS / runtime version: <Horizon OS build>

## Targets

| Metric | Target | Result | Pass/Fail |
| --- | --- | --- | --- |
| Sustained frame rate | >= 72 FPS | | |
| Frame time | <= 13.9 ms avg | | |
| Stale frames / hitches | none extended | | |
| Two-player session frame rate | >= 72 FPS | | |

## Capture method

- In-headset HUD: enable `PerformanceStatsOverlay` (FPS avg/min/max, frame ms, chunk count, triangles, rebuild queue).
- ProfilerMarkers to watch in the Unity Profiler / OVR Metrics Tool:
  - `Blockiverse.SurvivalLiteWorldPreset.Generate`
  - `Blockiverse.VoxelWorldRenderer.RebuildAll`
  - `Blockiverse.VoxelWorldRenderer.RebuildDirty`
  - `Blockiverse.VoxelWorldRenderer.RebuildChunk`
  - `Blockiverse.ChunkMeshBuilder.Build`
- EditMode proxy: run `WorldGenerationStressEditModeTests` for the CPU-side generation/meshing budget before each capture.

## Scenarios

1. Cold load of the default survival-lite world (generation + first full mesh).
2. Sustained creative editing (rapid break/place near chunk borders to exercise dirty rebuilds).
3. Two-player host-authoritative session (host + one client editing concurrently).

## Observations

- Frame rate: 
- Notable hitches (cause, ProfilerMarker, duration): 
- Memory / allocation notes: 
- Thermal behavior over <N> minutes: 

## Follow-ups

- 
