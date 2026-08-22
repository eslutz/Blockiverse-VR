# Audio Asset Manifest

**Generated file — do not edit by hand.** Regenerate with `python3 scripts/audio/make-audio-docs.py`.

Every sound in Blockiverse VR, what triggers it, where it came from, and under what license. This is the provenance record required by [`voxel_audio_vfx_ruleset.md`](../rulesets/voxel_audio_vfx_ruleset.md) §7 — a committed cue with no row here fails `scripts/audio/validate-audio-assets.py`.

Music is original to the project. Sound effects are edited from licensed third-party recordings by `scripts/audio/build-audio-assets.py`, driven by `scripts/audio/audio-manifest.json`. Raw source bundles are staged outside the repository and are never committed.

## Sources

| Pack | License | Commercial use | Modification | Attribution |
|---|---|---|---|---|
| Kenney — Impact Sounds | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | Yes | Yes | Not required |
| Kenney — Interface Sounds | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | Yes | Yes | Not required |
| OpenGameArt — Different steps on wood, stone, leaves, gravel and mud (TinyWorlds) | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | Yes | Yes | Not required |
| Sonniss GDC 2019 Game Audio Bundle | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | Yes | Yes | Not required |
| Sonniss GDC 2020 Game Audio Bundle | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | Yes | Yes | Not required |
| Sonniss GDC 2026 Game Audio Bundle | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | Yes | Yes | Not required |
| Blockiverse VR (original) | All Rights Reserved | — | — | — |

### License restrictions

- **Creative Commons Zero 1.0 Universal** — None.
- **Sonniss #GameAudioGDC Bundle Licensing Agreement** — No resale as-is; no authorship claim on the original recordings; no use for training artificial intelligence technologies.

Terms were verified at each original provider rather than from search results or a mirror. Attribution is not required by any source used; this record exists for provenance.

## Original project audio

| Cue | Trigger | Provenance | Length | Channels |
|---|---|---|---|---|
| `music_menu` | Title and pause menus | Original — `scripts/audio/generate-audio.py` | 36.00s | mono |
| `music_day` | Daytime music bed | Original — `scripts/audio/generate-audio.py` | 32.40s | mono |
| `music_night` | Nighttime music bed | Original — `scripts/audio/generate-audio.py` | 35.20s | mono |
| `music_cave` | Underground music bed | Original — `scripts/audio/generate-audio.py` | 35.00s | mono |
| `classic_block_break` | Block break, Classic Block Sounds enabled | Original — `scripts/audio/generate-audio.py` | 0.28s | mono |
| `classic_block_place` | Block place, Classic Block Sounds enabled | Original — `scripts/audio/generate-audio.py` | 0.20s | mono |

## Sound effects

### Block break and place

| Cue | Source pack | Source file | License | Length | Channels |
|---|---|---|---|---|---|
| `block_break` | Kenney — Impact Sounds | `impactMining_000.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.34s | mono |
| `block_break_crystal` | Sonniss GDC 2019 Game Audio Bundle | `Impact Soundworks - Super FX 8 and 16-bit Video Game SFX / Misc_Glass_Crystal_Shatter.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.45s | mono |
| `block_break_glass` | Sonniss GDC 2019 Game Audio Bundle | `Airborne Sound - Elements Glass / Glass,Plate Glass,Thick,Break,Topple,Schoeps.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.45s | mono |
| `block_break_gravelsand` | Sonniss GDC 2020 Game Audio Bundle | `PMSFX - Shattering Bricks / PM_SB_SOURCE_16 Impact brick rock dirt gravel single hit.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.40s | mono |
| `block_break_leaf` | Sonniss GDC 2020 Game Audio Bundle | `PMSFX - STEPS Dry Grass & Shrubs / PM_SDGS_14 Footstep Step Dry Grass Shrubs Pine Needles Meadow .flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.30s | mono |
| `block_break_oremetal` | Sonniss GDC 2026 Game Audio Bundle | `Epic Stock Media - HD Game Materials / METLImpt_Metal Old File Impact Tap Against Tire Iron Metallic Hit 01_ESM_HDGM.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.42s | mono |
| `block_break_snow` | Sonniss GDC 2020 Game Audio Bundle | `Bluezone - Snow Footsteps / Bluezone_BC0264_snow_footsteps_walk_deep_008.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.36s | mono |
| `block_break_soil` | Sonniss GDC 2019 Game Audio Bundle | `Matt Script - You Me & Debris / impact_gritty_grit_sandy_dirt_dig_shovel_shingle_roof_02.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.34s | mono |
| `block_break_stone` | Sonniss GDC 2020 Game Audio Bundle | `PMSFX - Rocky Impacts / PM_RI_Source_53 Rocks Impact Hit Single Stone.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.42s | mono |
| `block_break_wood` | Sonniss GDC 2019 Game Audio Bundle | `Matt Script - You Me & Debris / wood_breaking_cracking_snapping_breaking_peeling_bones_break_snap_crack_13.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.38s | mono |
| `block_place` | Kenney — Impact Sounds | `impactGeneric_light_000.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.16s | mono |
| `block_place_crystal` | Kenney — Impact Sounds | `impactBell_heavy_002.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.28s | mono |
| `block_place_glass` | Kenney — Impact Sounds | `impactGlass_light_000.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.23s | mono |
| `block_place_gravelsand` | Kenney — Impact Sounds | `impactSoft_heavy_001.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.24s | mono |
| `block_place_leaf` | Kenney — Impact Sounds | `impactSoft_medium_002.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.13s | mono |
| `block_place_oremetal` | Kenney — Impact Sounds | `impactMetal_light_000.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.26s | mono |
| `block_place_snow` | Kenney — Impact Sounds | `footstep_snow_004.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.26s | mono |
| `block_place_soil` | Kenney — Impact Sounds | `impactSoft_medium_000.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.12s | mono |
| `block_place_stone` | Kenney — Impact Sounds | `impactPlate_light_000.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.24s | mono |
| `block_place_wood` | Kenney — Impact Sounds | `impactWood_light_000.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.24s | mono |

### Footsteps

| Cue | Source pack | Source file | License | Length | Channels |
|---|---|---|---|---|---|
| `footstep_gravelsand_01` | Sonniss GDC 2020 Game Audio Bundle | `PMSFX - STEPS Dry Grass & Shrubs / PM_SDGS_113 Footstep Step Dry Grass Shrubs Pine Needles Meadow .flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.28s | mono |
| `footstep_gravelsand_02` | Sonniss GDC 2020 Game Audio Bundle | `PMSFX - STEPS Dry Grass & Shrubs / PM_SDGS_14 Footstep Step Dry Grass Shrubs Pine Needles Meadow .flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.28s | mono |
| `footstep_gravelsand_03` | Sonniss GDC 2020 Game Audio Bundle | `PMSFX - STEPS Dry Grass & Shrubs / PM_SDGS_186 Footstep Step Dry Grass Shrubs Pine Needles Meadow .flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.26s | mono |
| `footstep_gravelsand_04` | OpenGameArt — Different steps on wood, stone, leaves, gravel and mud (TinyWorlds) | `gravel.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.30s | mono |
| `footstep_leaf_01` | OpenGameArt — Different steps on wood, stone, leaves, gravel and mud (TinyWorlds) | `leaves01.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.32s | mono |
| `footstep_leaf_02` | OpenGameArt — Different steps on wood, stone, leaves, gravel and mud (TinyWorlds) | `leaves02.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.32s | mono |
| `footstep_leaf_03` | OpenGameArt — Different steps on wood, stone, leaves, gravel and mud (TinyWorlds) | `mud02.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.26s | mono |
| `footstep_leaf_04` | Kenney — Impact Sounds | `footstep_grass_004.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.30s | mono |
| `footstep_snow_01` | Kenney — Impact Sounds | `footstep_snow_000.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.32s | mono |
| `footstep_snow_02` | Kenney — Impact Sounds | `footstep_snow_001.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.32s | mono |
| `footstep_snow_03` | Kenney — Impact Sounds | `footstep_snow_002.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.32s | mono |
| `footstep_snow_04` | Kenney — Impact Sounds | `footstep_snow_003.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.32s | mono |
| `footstep_soil_01` | Kenney — Impact Sounds | `footstep_grass_000.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.30s | mono |
| `footstep_soil_02` | Kenney — Impact Sounds | `footstep_grass_001.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.30s | mono |
| `footstep_soil_03` | Kenney — Impact Sounds | `footstep_grass_002.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.30s | mono |
| `footstep_soil_04` | Kenney — Impact Sounds | `footstep_grass_003.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.30s | mono |
| `footstep_stone_01` | Kenney — Impact Sounds | `footstep_concrete_000.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.10s | mono |
| `footstep_stone_02` | Kenney — Impact Sounds | `footstep_concrete_001.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.10s | mono |
| `footstep_stone_03` | Kenney — Impact Sounds | `footstep_concrete_002.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.11s | mono |
| `footstep_stone_04` | Kenney — Impact Sounds | `footstep_concrete_003.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.11s | mono |
| `footstep_water_01` | Sonniss GDC 2020 Game Audio Bundle | `Wav Junction Sound Effects - Footsteps / 0014_Footsteps_water_puddle_single_splashes.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.34s | mono |
| `footstep_water_02` | Sonniss GDC 2020 Game Audio Bundle | `Wav Junction Sound Effects - Footsteps / 0014_Footsteps_water_puddle_single_splashes.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.34s | mono |
| `footstep_water_03` | Sonniss GDC 2020 Game Audio Bundle | `Wav Junction Sound Effects - Footsteps / 0014_Footsteps_water_puddle_single_splashes.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.34s | mono |
| `footstep_water_04` | Sonniss GDC 2020 Game Audio Bundle | `Wav Junction Sound Effects - Footsteps / 0014_Footsteps_water_puddle_single_splashes.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.34s | mono |
| `footstep_wood_01` | Kenney — Impact Sounds | `footstep_wood_000.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.27s | mono |
| `footstep_wood_02` | Kenney — Impact Sounds | `footstep_wood_001.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.25s | mono |
| `footstep_wood_03` | Kenney — Impact Sounds | `footstep_wood_002.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.25s | mono |
| `footstep_wood_04` | Kenney — Impact Sounds | `footstep_wood_003.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.25s | mono |

### Tool contact

| Cue | Source pack | Source file | License | Length | Channels |
|---|---|---|---|---|---|
| `tool_hit_soft_01` | Sonniss GDC 2020 Game Audio Bundle | `PMSFX - STEPS Dry Grass & Shrubs / PM_SDGS_113 Footstep Step Dry Grass Shrubs Pine Needles Meadow .flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.26s | mono |
| `tool_hit_soft_02` | Kenney — Impact Sounds | `impactSoft_medium_003.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.14s | mono |
| `tool_hit_soft_03` | Kenney — Impact Sounds | `impactSoft_heavy_003.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.26s | mono |
| `tool_hit_stone_01` | Kenney — Impact Sounds | `impactMining_001.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.30s | mono |
| `tool_hit_stone_02` | Kenney — Impact Sounds | `impactMining_002.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.30s | mono |
| `tool_hit_stone_03` | Kenney — Impact Sounds | `impactMining_003.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.30s | mono |
| `tool_wrong` | Sonniss GDC 2026 Game Audio Bundle | `Cinematic Sound Design - System & UI Feedback Elements / Interface Deny Low Fat Dark.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.34s | mono |

### Interface, inventory, and crafting

| Cue | Source pack | Source file | License | Length | Channels |
|---|---|---|---|---|---|
| `craft_fail` | Sonniss GDC 2026 Game Audio Bundle | `Cinematic Sound Design - UI Interaction Elements / Deny Muted.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.45s | mono |
| `craft_success` | Sonniss GDC 2026 Game Audio Bundle | `Cinematic Sound Design - UI Interaction Elements / Accept Boing Crunch.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.55s | mono |
| `inventory_close` | Kenney — Interface Sounds | `close_002.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.31s | mono |
| `inventory_open` | Kenney — Interface Sounds | `open_001.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.15s | mono |
| `pickup_item` | Sonniss GDC 2026 Game Audio Bundle | `Cinematic Sound Design - UI Interaction Elements / Ting Coins.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.40s | mono |
| `ui_cancel` | Kenney — Interface Sounds | `back_002.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.09s | mono |
| `ui_confirm` | Kenney — Interface Sounds | `confirmation_002.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.34s | mono |
| `ui_select` | Kenney — Interface Sounds | `select_004.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.16s | mono |

### Multiplayer

| Cue | Source pack | Source file | License | Length | Channels |
|---|---|---|---|---|---|
| `multiplayer_join` | Kenney — Interface Sounds | `maximize_006.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.38s | mono |
| `multiplayer_leave` | Kenney — Interface Sounds | `minimize_006.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.38s | mono |

### Containers

| Cue | Source pack | Source file | License | Length | Channels |
|---|---|---|---|---|---|
| `container_close` | Sonniss GDC 2019 Game Audio Bundle | `Soundopolis - Household Doors HD / Nightstand_Modern_Drawers_Wood_Close_x6_Fienup_001.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.45s | mono |
| `container_open` | Sonniss GDC 2019 Game Audio Bundle | `Soundopolis - Household Doors HD / Nightstand_Old_Drawers_Wood_Open_x11_Fienup_001.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.50s | mono |

### Fire and light

| Cue | Source pack | Source file | License | Length | Channels |
|---|---|---|---|---|---|
| `campfire_loop` | Sonniss GDC 2026 Game Audio Bundle | `Ivo Vicic - Campfire - Bonfire FX / 24 Campfire, Dropping Fresh Pine Branches in Fire, Crackling, Sizzling Strong, Close 02.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 14.00s | stereo |
| `emberflow_loop` | Sonniss GDC 2026 Game Audio Bundle | `Epic Stock Media - Synthesized Nature Loops and Sounds / FIREBurn_Loop Elements Fire Crackling Crunchy Flame Burn 03_ESM_SNLS.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 7.99s | mono |
| `torch_ignite` | Sonniss GDC 2026 Game Audio Bundle | `Ivo Vicic - Campfire - Bonfire FX / 24 Campfire, Dropping Fresh Pine Branches in Fire, Crackling, Sizzling Strong, Close 02.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.55s | mono |
| `torch_loop` | Sonniss GDC 2026 Game Audio Bundle | `Epic Stock Media - Synthesized Nature Loops and Sounds / FIREBurn_Loop Elements Fire Crackling Crunchy Flame Burn 03_ESM_SNLS.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 9.00s | mono |

### Weather

| Cue | Source pack | Source file | License | Length | Channels |
|---|---|---|---|---|---|
| `rain_heavy_loop` | Sonniss GDC 2020 Game Audio Bundle | `Articulated Sounds - Moody Rain Loops / WEATHER Rain, Large, Camping Tent near Sea, Isle-au-Coudre, Canada, LOOP.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 20.00s | stereo |
| `rain_light_loop` | Sonniss GDC 2020 Game Audio Bundle | `Soundholder - Rain & Thunder / ambient rain light washed out stereo MS.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 20.00s | stereo |
| `snow_wind_loop` | Sonniss GDC 2026 Game Audio Bundle | `Epic Stock Media - Synthesized Nature Loops and Sounds / WINDInt_Loop Weather Wind Whipping Constricted Flow Turbulent 01_ESM_SNLS.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 6.00s | stereo |
| `thunder_far` | Sonniss GDC 2026 Game Audio Bundle | `Jake Fielding - Interior Wind Rain and Storms / THUN_Interior Thunder Rumble_JF_INT Storm_01.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 3.60s | stereo |
| `thunder_near` | Sonniss GDC 2026 Game Audio Bundle | `Epic Stock Media - Public Spaces - Storms Lakes Parks and Rural Nature Exteriors / STORM_Texas Rain Thunder Initial Crash Boom Storm 01 Clap Lightning_ESM_CPS.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 3.20s | stereo |

### Ambience

| Cue | Source pack | Source file | License | Length | Channels |
|---|---|---|---|---|---|
| `cave_ambience_loop` | Sonniss GDC 2019 Game Audio Bundle | `Articulated Sounds - Ghosts Return / ATMO EERIE Cave, Water Drips, Emptyness, Howling Interior Wind, Oppressive, LOOP.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 22.00s | stereo |
| `day_ambience_loop` | Sonniss GDC 2026 Game Audio Bundle | `Just Sound Effects - Highlands of Norway / AMBSwmp_Meadow Pipits calling many Insects humming Wind blowing through Grass_JSE_HoN_Stereo.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 24.00s | stereo |
| `night_ambience_loop` | Sonniss GDC 2026 Game Audio Bundle | `Epic Stock Media - Synthesized Nature Loops and Sounds / AMBTrop_Loop Ambience Jungle Night Humid Birds Bug Chirps 01_ESM_SNLS.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 24.00s | stereo |

### Player vitals

| Cue | Source pack | Source file | License | Length | Channels |
|---|---|---|---|---|---|
| `low_health` | Sonniss GDC 2020 Game Audio Bundle | `Articulated Sounds - Human Male Breathe / HUMAN BREATH Male_Deep Mouth Inhale and Powerful Strong Exhale with Acceleration_A.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 1.60s | mono |
| `player_death` | Sonniss GDC 2020 Game Audio Bundle | `Articulated Sounds - Human Male Breathe / HUMAN BREATH Male_Deep Mouth Inhale and Powerful Strong Exhale with Acceleration_A.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 2.20s | mono |
| `player_hurt` | Sonniss GDC 2026 Game Audio Bundle | `SoundBits - Vox Hominis - Human Effort Voices / HMNBrth_Panting Male 02 04_SNDBTS_VH.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.55s | mono |

### Water and fluids

| Cue | Source pack | Source file | License | Length | Channels |
|---|---|---|---|---|---|
| `submerged_loop` | Sonniss GDC 2020 Game Audio Bundle | `Systematic-Sound - Sounds Of Nature – Flowing Water 01 / AMB WATERFLOW Stream Splash 2m Close M.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 12.01s | mono |
| `swim_stroke` | Sonniss GDC 2020 Game Audio Bundle | `Bluezone - Splash / Bluezone_BC0256_water_splash_small_008.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.70s | mono |
| `water_scoop` | Sonniss GDC 2020 Game Audio Bundle | `Wav Junction Sound Effects - Glassware / 0026_Pouring_Liquid_in_to_bowl_5.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.80s | mono |
| `water_splash` | Sonniss GDC 2020 Game Audio Bundle | `Bluezone - Splash / Bluezone_BC0256_water_splash_008.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 1.10s | mono |

### Consumption and movement

| Cue | Source pack | Source file | License | Length | Channels |
|---|---|---|---|---|---|
| `drink` | Sonniss GDC 2019 Game Audio Bundle | `Eiravaein Works - WakeyWakey / WakeyWakey,tea,gaiwan,ceramic,glazed,thick,3piece,cup,full,drink,sipping,slurp,swallow,mouthclicks,alt5.flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.90s | mono |
| `eat` | Sonniss GDC 2020 Game Audio Bundle | `PMSFX - STEPS Dry Grass & Shrubs / PM_SDGS_186 Footstep Step Dry Grass Shrubs Pine Needles Meadow .flac` | [Sonniss #GameAudioGDC Bundle Licensing Agreement](https://sonniss.com/gdc-bundle-license/) | 0.44s | mono |
| `landing` | Kenney — Impact Sounds | `impactSoft_heavy_000.ogg` | [Creative Commons Zero 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/) | 0.34s | mono |

## Rebuilding

```sh
# 1. Stage the source packs outside the repo (see the staging SOURCES.md)
# 2. Regenerate the build manifest if the cue set changed
python3 scripts/audio/make-audio-manifest.py
# 3. Confirm every source resolves before writing anything
python3 scripts/audio/build-audio-assets.py --check
# 4. Build the cues
python3 scripts/audio/build-audio-assets.py
# 5. Regenerate the original music and classic block cues
python3 scripts/audio/generate-audio.py
# 6. Validate what landed, and refresh this document
python3 scripts/audio/validate-audio-assets.py
python3 scripts/audio/make-audio-docs.py
```
