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

## GPU counters from the device (`ovrgpuprofiler`)

Fill-rate questions cannot be answered from the editor: the Quest GPU is a tiler and
the desktop numbers do not transfer. Horizon OS ships `ovrgpuprofiler` at
`/system_ext/bin/ovrgpuprofiler`, reachable through `hzdb`:

```sh
hzdb device list                                   # confirm the headset is attached
hzdb app install <path-to.apk>                     # must print "Installation successful"
hzdb app launch dev.ericslutz.blockiversevr
hzdb app foreground                                # must report our package, not vrshell
hzdb shell "timeout 10 ovrgpuprofiler -r"          # one metrics block per second
```

Notes that cost time to rediscover:

- The `timeout` must run **on the device** — macOS has no `timeout` binary.
- The app has to be **foregrounded and the headset worn**. With the headset off the
  system shell owns the compositor and the counters describe the dashboard, not the
  game. `hzdb app foreground` is the check that the reading is about our app at all.
- `ovrgpuprofiler -m` lists the available metric IDs; `-r "1,3"` restricts the stream.
- For frame timing and CPU/GPU overlap, use `hzdb perf capture` (Perfetto) and
  `hzdb perf compare` between two traces rather than the counter stream.

For a comparison to mean anything, both captures must be taken on the **same world
seed and the same head pose**. Stand still, look at the subject of the measurement,
and take the readings back to back; a rebuild-and-reinstall between captures keeps
the pose if the player does not move between them.

Counters worth reading first for a fill-rate change: `% Shaders Busy`,
`% Time ALUs Working`, `GPU % Bus Busy`, `% Texture Fetch Stall`, and
`Write Total (Bytes/sec)`.

## Recording a capture

1. Copy `report-template.md` to `report-YYYY-MM-DD.md`.
2. Fill in the build, device, targets table, and per-scenario observations.
3. Commit the report alongside any supporting screenshots.
