# Curated block texture pilot

Blockiverse VR is evaluating permissively licensed hand-authored textures as replacements for the generated/procedural block art. The pilot deliberately preserves the existing 32×32 runtime tile contract and 10×10 padded atlas so art quality can be judged independently of renderer or UV changes.

## Safety model

- `enhanced` remains the shipping/default texture set.
- `curated_v1` is a known experimental set ID but is not exposed in `BlockTextureSetIds.MenuOptions` until a committed atlas has passed visual and Quest validation.
- Raw third-party source packs live outside the repository in `Blockiverse-VR-texture-staging`, mirroring the audio staging workflow.
- Only processed 32×32 output tiles and their provenance are committed.
- A texture does not affect `curated_v1` output until its manifest status is `adopted`.
- Every non-adopted texture falls back byte-for-byte to the current `enhanced` source tile.
- During this first implementation slice, adoption is limited to direct CC0 sources. Composite ore/station work stays in `researching` until deterministic compositing is implemented and validated.

## Pilot textures

The pilot intentionally reuses the existing 12-texture comparison group:

1. `meadow_turf`
2. `loose_loam`
3. `graystone`
4. `branchwood_log`
5. `leafmoss`
6. `lumen_quartz_cluster`
7. `embercoal_seam`
8. `rosycopper_bloom`
9. `rustcore_ore`
10. `build_table`
11. `glowwick`
12. `storage_crate`

The current candidate and research state is committed in `scripts/art/curated-texture-manifest.json`.

## Verified candidate sources

The initial direct auditions use CC0 assets from original OpenGameArt provider pages:

| Block | Candidate | Source pack | Current state |
| --- | --- | --- | --- |
| `meadow_turf` | `grass_01.png` | Cethiel — Tileable Grass Textures - Set 1 | candidate |
| `loose_loam` | `dirt_01.png` | Cethiel — Tileable Dirt Textures | candidate |
| `graystone` | `cave-rock.png` | DevEarley — Ancient Civ. in the Jungle | candidate |
| `branchwood_log` | `jungle-wood.png` | DevEarley — Ancient Civ. in the Jungle | candidate |
| `leafmoss` | `jungle-hanging-leaves.png` | DevEarley — Ancient Civ. in the Jungle | candidate |

Fantasy ores and workstations are intentionally not assigned a fake one-to-one replacement. They should share licensed material bases where useful, then receive controlled Blockiverse-authored overlays/details so the family reads coherently without repeating the current large procedural motifs.

## Staging layout

The builder searches for a sibling directory named `Blockiverse-VR-texture-staging`. Override it with `BLOCKIVERSE_TEXTURE_STAGING` or `--staging`.

```text
Blockiverse-VR-texture-staging/
└── extract/
    ├── cethiel_grass_1/
    │   └── grass_01.png
    ├── cethiel_dirt/
    │   └── dirt_01.png
    └── ancient_civ/
        ├── rocks-bricks-dirt-concrete-plaster-tiles/
        │   └── cave-rock.png
        ├── plants-grass-wood/
        │   ├── jungle-wood.png
        │   └── jungle-hanging-leaves.png
        └── snow-ice-crystals/
```

The exact staging paths are defined by the manifest rather than by whatever directory layout a downloaded ZIP happens to contain.

## Workflow

Validate the committed provenance/state machine without requiring raw sources:

```bash
python3 scripts/art/validate-curated-textures.py
```

Verify that every **adopted** source resolves in the external staging directory:

```bash
python3 scripts/art/build-curated-textures.py --check
```

Build the complete experimental set:

```bash
python3 scripts/art/build-curated-textures.py
```

The builder writes:

- `Assets/Blockiverse/Art/Textures/Blocks/TextureSets/curated_v1/Source/*.png`
- `Assets/Blockiverse/Art/Textures/Blocks/TextureSets/curated_v1/blockiverse_block_atlas.png`
- `Assets/Blockiverse/Art/Textures/Blocks/TextureSets/curated_v1/curated-status.json`
- deterministic Unity `.meta` files using the existing art-generator GUID convention.

## Adoption gate

A candidate should be promoted to `adopted` only after all of the following:

1. License and source file are verified on the original provider page.
2. The staged original is available to the deterministic builder.
3. The 32×32 result tiles cleanly in a 3×3 repeat preview without an obvious seam or dominant repeating landmark.
4. The block reads correctly next to its neighboring material family rather than only in isolation.
5. It is legible at near, middle, and far viewing distances in Quest 3/3S without objectionable shimmer or high-frequency noise.
6. The change is preferred over `enhanced` in the existing comparison scene/screenshot workflow.

After a complete pilot atlas is committed and passes those checks, add `CuratedV1` to `BlockTextureSetIds.MenuOptions`. Do not change `BlockTextureSetIds.Default` until a broader production review approves the curated family.
