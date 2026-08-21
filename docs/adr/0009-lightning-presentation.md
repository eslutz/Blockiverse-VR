# ADR 0009: Lightning Presentation

## Status

Accepted

## Context

Lightning had existed in the codebase since the weather machine landed, and an
audit found that in practice nobody had ever seen it:

- **Strikes were never witnessed.** `TryStrikeRandomColumn` drew a uniformly
  random column from the entire world, and the strike was then *rejected* within
  8 blocks of the player. Every strike therefore happened somewhere unobserved.
- **The "flash" was four fog puffs.** `BlockiverseVfxCue.LightningFlash` aliased
  the fog-wisp sprite: 4 particles at 8 cm, 0.35 s, ~27% opacity, 8–16 m
  overhead. At that distance it subtends about 0.4°, below the threshold of
  noticing.
- **There was no sky flash at all.** Nothing pulsed any light; storms only dimmed
  ambient continuously.
- **The ambient flash was a lie.** `TickThunder` fired a flash near the player
  every 6–14 s with no strike behind it, unrelated to `LightningStruck`.
- **Thunder ignored distance.** `OnLightningStruck` played `ThunderNear`
  immediately at full volume however far away the strike was; the ambient rumble
  picked near vs far on a coin flip. No distance was computed for thunder
  anywhere in the codebase.

The ruleset's §10 additionally specified a weighted target table, three impact
zones with damage values, and two new blocks — none of it implemented, and all of
it more complicated than what this game wants.

## Decision

### 1. Bias strikes into a wide ring around a player, and say plainly what that costs

Strikes are drawn from a ring of radius 10–96 blocks around a randomly chosen
player head, **uniform in radius** rather than uniform in area. Distance is the
entire point: consecutive strikes should differ, some close enough to fill the
view and some distant silhouettes, and both the flash and the thunder are scaled
from that distance. Area-uniform sampling was rejected because it piles strikes
against the outer edge where storm fog washes them out — close to the problem
being fixed.

The inner radius sits just outside the existing 8-block player exclusion, so the
comfort exclusion is an invariant of selection rather than a filter applied after
it. A test asserts that relationship directly, so a tuning pass cannot quietly
drop bolts on the player's head.

**This costs strict determinism of placement, and that is accepted.** The
cadence, the intensity roll and the RNG stream stay deterministic; the struck
column does not, because the anchor is a live head position. Placement already
depended on head proximity as a rejection input — ring bias makes the dependence
total. An invisible deterministic strike is worth less than a visible one.

### 2. Storms vary, between storms and across their own life

A flat 35% roll every 10 seconds meant every storm in the game struck at the same
rate from its first second to its last. It is replaced by a per-storm character
roll (12%–70% peak) shaped by a build-peak-taper arc. Both terms derive from the
world seed and state that already travels in the environment snapshot, so this
needs no new networking and no new persisted state, and every peer computes the
same storm.

The arc never reaches zero: a storm that stops striking entirely at its edges
reads as broken weather rather than as a storm tapering.

### 3. The bolt is geometry, not a sprite

Three approaches were rendered at 12/45/90 blocks at Quest 3 angular scale before
choosing. A stretched 32×32 point-filtered tile visibly stair-steps over tens of
blocks; more decisively, **any single tall billboard reads unmistakably as a flat
card in stereo**, swimming as the head translates. That is fatal for the one
effect in the game the player is meant to stop and look at. A sourced photo was
also rejected: it looks good in isolation but reads soft and photoreal against
hard-edged voxel terrain, and one photo means every strike in the world is the
same bolt.

Geometry is also the cheaper option on a tile GPU — a 0.76-block-wide ribbon
covers a fraction of the transparent fill a 4-block quad would.

Soft edges come from a 64×1 alpha ramp generated in C# at startup and sampled
across the ribbon's *width*, not from stacked layers. That means no PNG, no
`.meta`, no atlas entry, no art-pipeline validation to update — and it collapses
what would have been two draws into one. A full-height additive glow billboard
was rejected for the same reason the sprite was: a wide transparent quad spanning
the screen vertically is the most expensive thing you can hand a tile GPU. The
impact glow is a small localised quad in the same mesh instead.

Billboarding is **yaw only**. A full look-at rotation tilts the bolt when the
player looks up, which is exactly the flat-card tell that ruled out a sprite.

### 4. The sky flash modulates ambient and never the sun

`BlockiverseLightingCycleController.ApplyCurrentLighting()` rewrites sun
intensity/colour/shadows and `RenderSettings.ambientLight` **every LateUpdate**,
so any external component poking a light is erased within a frame. The flash is
folded into that method rather than bolted on.

It touches ambient only. At night the sun sits below
`MinimumShadowCastingIntensity`, so raising it for a flash would flip the entire
shadow pass on and off for two frames — a full shadow-caster sweep over every
loaded chunk, with every shadow in the scene snapping in and out. Ambient is
`AmbientMode.Flat`, so an additive term is free and lifts everything uniformly,
which is what a flash looks like anyway. A PlayMode test holds the sun's shadow
mode and intensity constant across the whole flash at night.

No point light is created: the runtime point-light budget is fully spent on
emitters with distance eviction, so a lightning light would evict a torch the
player is standing next to.

The flash falls off **quadratically** with distance and reaches exactly zero at
the ring's outer edge. The zero is exact rather than nearly-zero because the term
is re-added every frame, so a residual would bleed into ambient permanently.

### 5. Thunder plays 2D, delayed and attenuated by distance

The delay is `distance / 34` blocks per second — deliberately about ten times
slower than the real speed of sound. Honest propagation over a 96-block ring
peaks at 0.28 s, which no player perceives as a delay at all; at 34 the same
strike arrives 2.8 s behind its flash, and that gap is the strongest distance cue
the game has.

Thunder plays through the 2D cue path rather than positionally. The positional
path routes through an 8-source round-robin pool that *moves* whichever source it
picks, so a clip still ringing when the pool wraps is teleported mid-tail; those
sources also apply Unity's default logarithmic rolloff, which would attenuate a
second time on top of the distance curve. Thunder is a sky-filling sound, not a
point source.

The distance volume folds in **inside** `ResolveVolume`, so master volume, the
weather bus and Mute All remain the single gate a caller cannot scale past.

The clips themselves remain placeholders. A better recording drops in by editing
`thunder()` in `scripts/audio/generate-audio.py`, whose GUIDs are md5-of-path, so
no references churn and no logic changes.

### 6. Reduced Flash suppresses the visuals, not the storm

One predicate (`IsFlashCue` / `AllowFlashEffects`) is consulted by everything that
produces a flash, including the bolt and the sky flash, which do not route through
`PlayCue`. Thunder is queued **before** that gate: a player using Reduced Flash
should still hear the storm.

v1 suppresses both the bolt and the sky flash, preserving today's semantics and
the ruleset's unqualified "respect reduced-flash". The softer answer — keep a dim
static bolt, since a ~1%-of-screen ribbon is not a photosensitivity trigger, and
suppress only the full-field flash — is better accessibility but is its own
decision, recorded here as a follow-up rather than smuggled in.

### 7. Scope deliberately cut

Not shipped: player or entity damage (lightning is atmospheric only), the weighted
target table, the three impact zones, and the `charred_log` / `stormglass` blocks.
The last is not the cosmetic addition it looks like — a new `BlockId` changes the
registry hash the save format records. Ruleset §10 has been rewritten to describe
what ships rather than what was once imagined.

## Consequences

- Storms are now something a player watches rather than something the simulation
  does offscreen. Distance is legible three ways at once: the bolt's size, the
  flash's strength, and the thunder's delay.
- Strike placement is no longer reproducible from a seed alone. Recorded above
  and in the code, so it is not mistaken for a regression.
- The particle-tint fix that shipped alongside this changes the appearance of all
  eight existing VFX cues, which had been silently rendering untinted. It is a
  separate commit so it can be reverted independently.
- Two accessibility accommodations remain specified but unimplemented: a
  reduced-thunder audio option and a thunder haptic.

## Related

- [voxel_world_environment_effects.md](../rulesets/voxel_world_environment_effects.md) §10
- [voxel_audio_vfx_ruleset.md](../rulesets/voxel_audio_vfx_ruleset.md)
- [ADR 0003](0003-m6-performance-and-feedback.md) — the feedback/VFX budget this fits inside
