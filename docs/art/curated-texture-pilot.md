# Curated block texture pilot

Blockiverse VR is evaluating permissively licensed hand-authored textures as replacements for the generated/procedural block art. The pilot deliberately preserves the existing 32×32 runtime tile contract and 10×10 padded atlas so art quality can be judged independently of renderer or UV changes.

## Safety model

- `enhanced` remains the shipping/default texture set.
- `curated_v1` is a known experimental set ID but is not exposed in `BlockTextureSetIds.MenuOptions` until a committed atlas has passed visual and Quest validation.
- Raw third-party source packs live outside the repository in `Blockiverse-VR-texture-staging`, mirroring the audio staging workflow.
- Only processed 32×32 output tiles and their provenance are committed.
- A texture does not affect the normal `curated_v1` output until its manifest status is `adopted`.
- `--include-candidates` exists only for audition builds; it does not change manifest state or the shipping default.
- Every non-selected texture falls back byte-for-byte to the current `enhanced` source tile.
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

The tools search for a sibling directory named `Blockiverse-VR-texture-staging`. Override it with `BLOCKIVERSE_TEXTURE_STAGING` or `--staging`.

```text
Blockiverse-VR-texture-staging/
├── raw/
│   └── <pack-id>.zip
├── SOURCES.json
└── extract/
    ├── cethiel_grass_1/
    │   ├── _pack/
    │   └── grass_01.png
    ├── cethiel_dirt/
    │   ├── _pack/
    │   └── dirt_01.png
    └── ancient_civ/
        ├── rocks-bricks-dirt-concrete-plaster-tiles/
        │   ├── _pack/
        │   └── cave-rock.png
        ├── plants-grass-wood/
        │   ├── _pack/
        │   ├── jungle-wood.png
        │   └── jungle-hanging-leaves.png
        └── snow-ice-crystals/
            └── _pack/
```

The manifest records original-provider download endpoints. The fetcher keeps the upstream ZIP intact under `raw/`, extracts it safely, and normalizes only concretely referenced files to the pack staging root. `SOURCES.json` records the SHA-256 of every downloaded ZIP.

## Workflow

Validate the committed provenance/state machine without requiring raw sources:

```bash
python3 scripts/art/validate-curated-textures.py
```

Fetch the verified CC0 packs into external staging:

```bash
python3 scripts/art/fetch-curated-texture-sources.py
```

Verify the currently adopted sources only:

```bash
python3 scripts/art/build-curated-textures.py --check
```

Verify the direct candidates as well:

```bash
python3 scripts/art/build-curated-textures.py --check --include-candidates
```

Build an audition atlas containing adopted plus candidate direct textures, with all other tiles falling back to `enhanced`:

```bash
python3 scripts/art/build-curated-textures.py --include-candidates
```

Build the normal experimental set using adopted entries only:

```bash
python3 scripts/art/build-curated-textures.py
```

The builder writes:

- `Assets/Blockiverse/Art/Textures/Blocks/TextureSets/curated_v1/Source/*.png`
- `Assets/Blockiverse/Art/Textures/Blocks/TextureSets/curated_v1/blockiverse_block_atlas.png`
- `Assets/Blockiverse/Art/Textures/Blocks/TextureSets/curated_v1/curated-status.json`
- deterministic Unity `.meta` files using the existing art-generator GUID convention.

The `Curated Texture Preview` GitHub Actions workflow performs the fetch, candidate-resolution check, audition build, and artifact upload on this pilot PR so the source URLs and transform pipeline are exercised outside a developer workstation.

## Adoption gate

A candidate should be promoted to `adopted` only after all of the following:

1. License and source file are verified on the original provider page.
2. The source pack is fetched and its exact ZIP SHA-256 from external staging is copied into that pack's committed manifest entry as `sha256`. Adopted packs without a 64-character pinned hash fail validation and the builder rechecks the staged archive against that hash.
3. The staged original is available to the deterministic builder.
4. The 32×32 result tiles cleanly in a 3×3 repeat preview without an obvious seam or dominant repeating landmark.
5. The block reads correctly next to its neighboring material family rather than only in isolation.
6. It is legible at near, middle, and far viewing distances in Quest 3/3S without objectionable shimmer or high-frequency noise.
7. The change is preferred over `enhanced` in the existing comparison scene/screenshot workflow.

After a complete pilot atlas is committed and passes those checks, add `CuratedV1` to `BlockTextureSetIds.MenuOptions`. Do not change `BlockTextureSetIds.Default` until a broader production review approves the curated family.
