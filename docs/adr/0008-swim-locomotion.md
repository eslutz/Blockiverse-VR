# ADR 0008: Swim Locomotion

## Status

Accepted

## Context

[PR #326] gave fluids their own physics layer, so the player falls into water
instead of walking on it, and [PR #328] made that legible by rendering water
transparently. What happened next was not swimming: XRI's gravity dropped the
player to the seabed at terminal velocity, and nothing in the game distinguished
being in water from being in air.

The constraints this had to fit inside:

- **No controller input is free.** ADR 0005 routes new input actions through the
  deterministic bootstrapper catalog, and the Quest controllers are fully spoken for.
- **Gravity is owned by XRI.** `GravityProvider` re-asserts `useGravity` on every
  comfort change, so a swim implementation cannot simply switch it off.
- **Jump is gated by locomotion mode.** `jumpProvider.enabled = isGlide && ...`,
  while crouch is not mode-gated.
- **Meta's comfort guidance** is to default to the gentler option and let players opt
  into more intense ones.

## Decision

### 1. Negative buoyancy is the default, and the accommodation is the inverse setting

The player sinks whenever they are not actively swimming. Water should read as
something you work against, not a floor you bob on: the surface is not a resting
state, and treading water is an active act (Eric's ruling).

This deliberately inverts Meta's default-to-comfort guidance for one specific
motion, so the escape hatch is first-class rather than buried. **Sink When Not
Swimming**, off, restores *exact* neutral buoyancy — with no input the app moves the
player vertically by zero, so loading a save submerged, respawning underwater, and a
fluid flowing into the player's cell all produce no unrequested motion at all. It
sits in the Comfort menu beside the existing vection controls, and
`SwimVignetteBoost` engages the tunneling vignette during passive descent exactly as
it does for driven vertical motion.

The descent is a **velocity** target reached by `Mathf.MoveTowards`, never a spring
and never an acceleration: it cannot overshoot, bob, or accumulate into a fall, and
it is bounded by the seabed. 0.35 m/s is roughly one block every three seconds —
clearly readable as sinking, slow enough to stay a drift.

**0.35 m/s is reasoned, not validated. A headset comfort pass on this specific
default is a required deliverable, not a nice-to-have.**

### 2. Wading is not swimming

| State | Condition | Gravity | Vertical owner |
|---|---|---|---|
| Dry | no fluid sample | XRI | XRI |
| Wading | feet in fluid, body dry | **on** | XRI |
| Surfaced | body in fluid, head dry | locked off | swim provider |
| Swimming | body and head in fluid | locked off | swim provider |

Wading keeps gravity on so every puddle and the one-block shore step stays walkable.
Only the body sample crossing into fluid hands over control.

### 3. Gravity is suppressed with a lock *and* explicit registration

`TryLockGravity(GravityOverride.ForcedOff)` plus
`gravityProvider.gravityControllers.Add(this)`.

`gravityPaused` alone is not enough: `CanProcessGravity` consults a controller's
pause flag only when no forced lock is held. And the controller list
auto-populates exactly once, from components already under the mediator — so in any
runtime-built rig a provider added later is never consulted, the player sinks at
XRI's terminal velocity, and nothing says why. The provider tracks its own lock flag
because `TryLockGravity` refuses (and only warns) when the same provider is already
registered, so it cannot serve as its own idempotence check.

Every path that stops swimming releases the lock: leaving the water, locomotion
suppression, creative flight, the world going away, and `OnDisable`. A lock taken
and never released leaves the player hanging in mid-air on dry land, which is why
each of those paths has its own PlayMode test.

### 4. Swim-up reads the jump *action*, not the jump provider

Jump is gated by locomotion mode. A **Teleport**-mode player who ended up submerged
could swim down (crouch is not mode-gated) and not up, while passive sink pulled them
deeper: an inescapable underwater state in the *comfort* locomotion mode. The swim
provider therefore resolves the jump action directly. The jump provider itself stays
disabled while swimming — a real jump underwater is still meaningless — and stays
mode-gated on dry land, so ordinary jumping is unchanged. A source-text guard pins
the dependency out.

Swim-down reuses the existing crouch action; while swimming the rig suppresses
crouch's capsule shrink and camera drop, because crouch's only meaning underwater is
"descend" and dropping the view would move the camera for an input meant to move the
body. Horizontal swimming is the existing move stick with a speed factor.

### 5. The submersion query is engine-free voxel reads, not physics

`FluidSubmersion` (in `Blockiverse.Voxel`, `noEngineReferences`) samples three cells —
feet, mid-capsule, head — per frame. A raycast against the fluid collider would
contend with the throttled collider recook queue and would disagree with the GPU
wave, which is presentation-only; the voxel grid is the authority for where fluid is.
Sample points come from the CharacterController capsule rather than the head, because
crouch and Use My Real Height both change the head-to-ground distance.

A null world reads as dry and releases the lock. That is a real state — the title
screen, and the window while a world is swapped — and treating it as anything else
would leave gravity off forever after a reload.

## Consequences

- Nothing replicates and no save changes. Submersion is a pure function of
  (synced world blocks, own player position); the world is already host-authoritative
  and each peer's position is already client-trusted, so every peer computes its own
  answer with zero messages. Schema stays at v4.
- Emberflow goes through the same path, thicker in every direction: it sinks you more
  slowly (0.20 m/s) but it does sink you, and it still does not break a fall. Sinking
  simply increases exposure to the hazard that already exists.
- **No ledge-climb assist.** Hold-jump-to-rise already gets the player out, and an
  assist that moves you *up* when you asked to move *forward* is unrequested motion in
  a direction the player did not choose — a different and less defensible thing than a
  constant, predictable, physically motivated descent.
- **No breath or drowning.** Persisting a breath value forces a save-schema bump that
  would hard-refuse every existing save, and the pre-release policy has no migrations.
- A water column deep enough to submerge the body no longer produces a fall, which
  changed what the existing fall-damage PlayMode test could measure; its fluid columns
  are now wade-depth so the feet-in-fluid rule it pins is exercised without waiting
  out a slow descent.

[PR #326]: https://github.com/eslutz/Blockiverse-VR/pull/326
[PR #328]: https://github.com/eslutz/Blockiverse-VR/pull/328
