# ADR 0007: Water Surface Rendering

## Status

Accepted

## Context

Fluids shipped as opaque voxel geometry drawn with the terrain material. [PR #326]
gave water its own physics layer so the player falls through it instead of walking
on it, but the player still could not see into it: a lake read as a solid blue
floor, and the seabed the player now sinks toward was invisible.

Making water transparent on a Quest tile GPU is not a material toggle. It moves
water out of the opaque queue, which forfeits the early-Z rejection that currently
hides the entire submerged seabed, and it has to coexist with constraints this
project has already committed to elsewhere:

- Exactly one voxel shader is listed in `m_AlwaysIncludedShaders`
  (`ProjectSettings/GraphicsSettings.asset`), pinned by
  `M4ArtAssetValidationEditModeTests.VoxelLitShaderIsAlwaysIncludedForPlayerBuilds`.
- The URP asset ships `m_RequireDepthTexture: 0` and `m_RequireOpaqueTexture: 0`
  with MSAA 4x, asserted by `AndroidUrpAssetUsesQuestMobileRenderDefaults`.
- Vertex `COLOR` is fully spent by the night-lighting work: R is sky exposure,
  G is emitter reach, B is self emission.
- All routed menus are world-space canvases on the interaction layer, rendered by
  the same camera as the world.

## Decision

### 1. One shader, one keyword, material-driven surface state

`BlockiverseVoxelLit.shader` gains `_SrcBlend`/`_DstBlend`/`_ZWrite`/`_Cull` float
properties driving the ForwardLit pass's `Blend`/`ZWrite`/`Cull` directives, plus a
`_BLOCKIVERSE_WATER` local keyword. Defaults are opaque, so terrain renders exactly
as before. `BlockVisualAtlas` produces two runtime materials from the same authored
atlas: the opaque one, and a transparent clone with the keyword enabled.

A second `.shader` file is rejected. Nothing in the build references these
materials as assets — `BlockVisualAtlas` swaps the shader in at runtime — so a
second shader reached only through `Shader.Find` would be stripped from the Android
player. The failure would be invisible in the editor and in CI, and would appear on
device as magenta water. For the same reason the keyword is `multi_compile_local`,
never `shader_feature_local`.

### 2. Water attributes live in a second UV channel, never in vertex COLOR

Fluid meshes carry `UV1` = `(surfaceMask, familyIndex)`, written by
`ChunkMeshBuilder.AddFluidFace`. Vertex `COLOR` is not reused: water needs baked
sky exposure, emitter reach, and self emission exactly as much as stone does — a
torch-lit pool and a glowing emberflow both depend on channels the wave data would
have displaced. `COLOR.a` stays a literal `1.0`, because it is an opacity
multiplier the moment the material blends.

Water is therefore lit by the *same* full lighting solve as terrain. Only the
surface treatment — tint, alpha, wave displacement, wave normal, and highlight —
differs.

### 3. The surface mask is "an emitted +Y face", plus the wall feet standing on one

`surfaceMask` is set from the face index, not from a vertex height test or a
sky-access query, so by construction it marks exactly the top faces the mesh
builder actually emitted. A water cell with a sapling, a different fluid, or a
placed block above still emits a full-height top face and still gets masked.

One exception, and it exists to close a seam rather than open one: where a fluid
side wall's foot stands on a *lower* same-family surface — the step pattern flowing
water makes constantly — that foot is masked too, so it rides the neighbouring
surface down. Both sets of vertices share an x/z edge and the wave is a pure
function of x and z, so they dip by exactly the same amount and the seam closes
exactly rather than approximately. Without it the wave opens a 5 cm see-through
slit under every step. The rule tests the cell *below* the neighbour, so a
freestanding wall of water with open air beside it all the way down keeps its foot
planted, and a step against a different fluid family is left alone.

The wave is strictly downward: `dy = A·(s − 1)` with `s ∈ [−1, 1]`, so a crest can
never rise above the voxel face plane and a wall never moves away from what it
meets. No shoreline crack is expressible. The accepted artefact is the inverse: at
an exposed water edge, up to 5 cm of side wall can stand proud of a wave trough.
That is opaque geometry, never a hole.

An earlier design dropped the whole water surface by 0.125 blocks. It was rejected:
it opened a 12.5 cm slit on all four sides of any block placed on a lake, and the
shoreline step it was meant to create already exists, because `PlaceFluids` fills
to `SeaLevel - 1`.

### 4. `ZWrite On` for transparent water

Water writes depth, so a farther fluid fragment can never paint over a nearer one.
That is the sort flip that reads as a far bank sliding on top of the near surface,
and it happens both between chunks as the head turns and *inside a single fluid
mesh*, where a top face and a far wall are submitted in voxel-traversal order
rather than depth order. The cost is that a submerged far bank is mostly no longer
visible through the near surface; for a stylised voxel lake that layering is not
worth paying sorting artefacts for. The fallback is a single material float, not a
shader edit.

**What this does not buy: order independence.** With blending enabled, `ZWrite`
decides which fragments survive, not how many blend. When the farther fragment is
submitted first it blends and writes depth and the nearer one blends over it, so
that patch carries two layers of tint and reads slightly denser; submitted the
other way round the far fragment is depth-rejected and one layer lands. Triangle
order is fixed by the voxel scan rather than by the camera, so which of the two a
given patch shows depends on where the player is standing — visible where a
submerged wall sits behind a surface, as a density difference rather than as
flicker. Making it genuinely order-independent needs a depth-primed second pass
over fluid geometry (`ColorMask 0` + `ZWrite`, then `ZTest Equal` with `ZWrite`
off) or per-frame sorted geometry. Both cost an extra pass over water, which is the
one budget this feature has not measured yet, so both wait on the device capture in
Consequences.

### 5. Underwater is fog plus a camera clear — no tint quad

`BlockiverseWaterView` samples the eye cell, cross-fades a submersion blend over
0.25 s, and swaps the camera clear from Skybox to a solid fog colour.
`BlockiverseLightingCycleController` — the project's only writer of
`RenderSettings.fog` — folds that blend into its per-frame fog write, above its
clock/sun guard, and forces fog on regardless of weather while submerged.

A camera-attached tint quad is rejected. Every routed menu is a world-space canvas
rendered by this camera, so a near-clip quad would tint the pause and quit routes
at emberflow's density — taking the escape hatch away from the player in exactly
the situation that is hurting them. Canvas UI does not consume fog, so menus and
the survival HUD stay legible underwater. That is a feature, not an oversight.

### 6. No depth texture, no opaque texture, no renderer features

Enabling either flips `RequiresIntermediateAttachments` and forfeits fixed foveated
rendering across the whole frame. **Consequence accepted: no depth-fade shorelines,
no depth-driven colour ramp, no screen-space refraction.**

## Consequences

- Water is see-through and animated, and the seabed the player sinks toward is
  visible, which is what makes the fluid physics from [PR #326] legible.
- Fill rate is the open risk. Moving water to the transparent queue removes the
  early-Z rejection that hides the submerged seabed, roughly doubling shaded
  fragments over the water's screen area at MSAA 4x. Three staged `ovrgpuprofiler`
  captures on one seed and pose — today, queue-move-only, and the full feature —
  are a required merge gate; the queue-move-only number is the one that decides
  affordability. Levers, in order: raise alpha, then MSAA 4x → 2x, then keep water
  in the opaque queue as a last resort.
- The wave is presentation-only. The fluid `MeshCollider` is cooked from the
  undisplaced mesh and every gameplay query reads voxel data, so nothing crosses
  the determinism boundary and no per-frame `Shader.SetGlobalFloat` is needed.
  Waves keep moving while a menu is open.
- Residual transparency artefact, deliberately left: the number of blended water
  layers over a pixel can differ by viewpoint (see 4). The depth-primed pass that
  would remove it is specified above and is a candidate for the same device session
  that prices the queue move — it caps blended layers at one per pixel, so it may
  well pay for its own extra geometry pass.
- Mesh bounds for fluid meshes are padded downward by
  `VoxelWorldRenderer.MaxWaveDipMeters`; an EditMode test pins the shader's wave
  amplitudes to that padding so a look-dev change cannot silently start popping
  troughs at the edge of vision.

## Alternatives considered

- **A separate water shader.** Rejected: stripped from the player build (see 1).
- **Flags packed into `COLOR.a`.** Rejected: it is the blend's opacity multiplier.
- **Alpha-to-coverage.** Rejected: MSAA 4x quantises alpha to five levels, giving
  dithered, stereo-noisy edges.
- **Queue 2450.** Rejected: front-to-back opaque ordering blends water against the
  clear colour before the seabed is drawn.
- **An independent high-frequency sparkle sine.** Rejected: it aliases past ~20 m,
  and aliases *differently* in each eye — binocular rivalry, invisible in the flat
  game view and a comfort defect in the headset. The highlight is taken off the
  wave normal instead, which the one-metre vertex spacing band-limits by
  construction.

[PR #326]: https://github.com/eslutz/Blockiverse-VR/pull/326
