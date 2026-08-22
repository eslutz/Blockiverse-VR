# ADR 0007: Self-Hosted Dedicated Server

## Status

Accepted

## Date

2026-08-21

## Decision

Blockiverse ships a **self-hosted dedicated server**: a headless Unity Dedicated Server build for
Linux x86-64, distributed both as a downloadable archive and as a container image on
`ghcr.io`. It runs the same authoritative simulation the LAN host runs today, with no local player,
no renderer, and no XR.

Specific decisions:

1. **True dedicated server, not a headless host.** `NetworkSessionMode.Server` and
   `BlockiverseNetworkSession.StartServer()` join `Host` and `Client`. The server process owns no
   player object and consumes no player seat.
2. **`Blockiverse.Gameplay` splits into `Blockiverse.WorldRuntime` + `Blockiverse.Gameplay`.**
   World simulation orchestration (`CreativeWorldManager`, `EnvironmentDynamicsController`, and the
   pure-C# lighting/edit helpers) moves to `Blockiverse.WorldRuntime`, which references no XR
   Interaction Toolkit, TextMeshPro, or uGUI. Both assemblies keep `rootNamespace:
   Blockiverse.Gameplay` — the same technique `Blockiverse.Survival.Health` already uses — so the
   move itself forces no `using` or type-name change anywhere. The asmdef boundary, not the
   namespace, is what matters.

   **This is not a pure file move, and an early draft of this ADR wrongly implied it was.**
   Verification established that `CreativeWorldManager` names eight types that stay in
   `Blockiverse.Gameplay` (`VoxelWorldRenderer`, `GlowwickLightManager`, `BlockiverseLightingRuntime`,
   `BlockiverseVoidSafetyFloor`, `CreativeInteractionController`, `CreativeHotbar`,
   `PlacementPreview`, `BlockiverseComfortTransition`) across roughly 25 sites, including serialized
   fields, a public `Renderer` property, public `Configure` parameters, and static calls. Moving the
   file without first inverting those dependencies produces a **circular assembly reference that
   Unity rejects outright**. Decision 4 is therefore a prerequisite of decision 2, not a
   complement to it, and re-homing the presentation-typed public API does change call sites.
3. **Presentation assemblies are excluded with `excludePlatforms: ["LinuxStandalone64"]`, never
   with `defineConstraints: ["!UNITY_SERVER"]`.** See Consequences for why this distinction is
   load-bearing. The exclusion applies to exactly three assemblies — `Blockiverse.Gameplay`,
   `Blockiverse.UI`, and `Blockiverse.VR`. It is *not* needed on `Blockiverse.Editor`
   (`includePlatforms: ["Editor"]`) or on any test assembly (`defineConstraints:
   ["UNITY_INCLUDE_TESTS"]`); none of those is compiled into a player build.

   **`Blockiverse.MetaAvatars` and `Blockiverse.MetaPlatform` ship in the server build**, despite
   being useless to it. An earlier draft excluded them for size. That is wrong: the shared network
   player prefab carries three `Blockiverse.MetaAvatars` components, and `MetaAvatarStreamRelay` is
   a `NetworkBehaviour`. Excluding the assembly changes the prefab's NetworkBehaviour set, so server
   and client would disagree on the spawn contract and player spawning would fail. The native Oculus
   calls are already `#if UNITY_ANDROID && !UNITY_EDITOR` guarded and the assembly compiles for the
   server target, so the cost is dead weight rather than breakage. Do not "optimise" it out.
4. **The renderer is cut with an `IWorldPresentation` seam**, resolved by `GetComponent` + `is`,
   and simply absent on the server. `VoxelSkyLightMap` moves to `CreativeWorldManager` ownership
   because sky occlusion is a simulation input (crop growth, cave detection), not a render artifact.
5. **Server configuration is `KEY=VALUE`**, resolved defaults → config file → environment →
   CLI arguments. Unknown keys and unparsable values are fatal.
6. **The admin surface is stdin plus a Unix domain socket**, not a TCP or HTTP port.
7. **Linux x86-64 only.** No Windows or macOS server builds in this scope.
8. **The compiled server is distributed under a binary-only grant** in `LICENSE.md` and
   `SERVER-EULA.md`. Repository source remains All Rights Reserved.

## Context

Multiplayer today is LAN host-authoritative: a player's headset is the host, and the world exists
only while that headset is running the game. There is no persistent world and no way to invite
someone off the local subnet.

The architecture was already most of the way to a dedicated server without anyone planning it that
way. `Blockiverse.Voxel`, `WorldGen`, `Survival`, and `Survival.Health` are `noEngineReferences:
true`. `Blockiverse.Networking` holds the entire authority model and does not reference
`Blockiverse.Gameplay` — it discovers Gameplay through interfaces. Every authority gate is already
`IsServer`, not `IsHost`. `ChunkAuthorityBoundary` already grants a server all host powers.

Three things blocked a headless process:

- `CreativeWorldManager`, the only `IMultiplayerWorldContext` implementation, lives in an assembly
  that references XRI, TextMeshPro, and uGUI, and unconditionally constructs a `VoxelWorldRenderer`
  that throws without a texture atlas material.
- `ConfigureEnvironmentServices` silently returned early when no `WorldTimeClock` existed in the
  scene, and the clock was created as a side effect of scene *lighting* setup. A headless scene
  therefore produced a world with no weather, no crop growth, no sapling growth, no leaf decay, and
  no fluid flow — with no error of any kind.
- Avatar poses reach `HeadAnchor` only through a `ClientRpc`, which does not execute in a
  server-only process. Every server-side reach check would have read each player's spawn transform,
  and `TryRejectOutOfReach` fails open when position resolution fails.

Exposing a listener to the internet also changes the threat model. The connection-approval payload
is a version handshake, not authentication: its HMAC key is a compile-time constant. Player identity
is a `PlayerPrefs` GUID pair sent in cleartext. `stashedInventoriesByIdentityKey` is unbounded and
keyed by a client-chosen value, and is written into every save. Rate-limit violations are counted
and never acted on.

## Consequences

### The `excludePlatforms` vs `defineConstraints` distinction is not stylistic

`UNITY_SERVER` is defined for **Editor** scripts whenever the active build target is Dedicated
Server. `Blockiverse.Editor` references `Blockiverse.Gameplay`, `UI`, `VR`, and the Oculus
assemblies. A `defineConstraints: ["!UNITY_SERVER"]` on those assemblies therefore makes
`Blockiverse.Editor` fail to compile while the server target is active — and `-executeMethod` then
cannot run at all, including the method doing the build. `excludePlatforms` never names `Editor`, so
editor and test compilation are unaffected regardless of active target.

`#if !UNITY_SERVER` on individual files does not solve the problem either: asmdef *references* are
not preprocessor-conditional, so XRI and uGUI would still link into the server player.

**Do not "simplify" the exclusions into define constraints.**

### Silent failure is designed out, not merely detected

`CreativeWorldManager` now self-heals a missing `WorldTimeClock` by adding one, rather than
returning early. An exception was rejected because many EditMode tests legitimately construct a bare
manager in an empty scene. The guard is instead a test asserting that a bare manager in an empty
scene still produces a non-null clock and weather state — a test that fails against the previous
behaviour.

The config resolver takes the same posture: an unknown or unparsable key exits `78` rather than
silently defaulting.

### Server-side security posture is honest but not complete

The server secret replaces the compile-time HMAC key and is backward-compatible by construction:
it changes only the HMAC *key*, leaving the approval protocol version, payload body, and default
join code untouched, so LAN hosts and unconfigured clients stay byte-identical.

Note on sequencing: concurrent LAN-multiplayer work raises `ApprovalPayloadProtocolVersion` to **2**
and adds game version, world-save schema, and registry hashes to the payload, along with a
`BlockiverseJoinRejectionReason` result. That change lands first and the dedicated server builds on
top of it — the server must produce a v2 payload, and headless needs a deliberate answer for what
`Application.version` compares against. The secret-as-HMAC-key decision is unaffected either way,
because it is orthogonal to the payload body.

`security.require_secret` prevents an operator running an open server while believing it is
protected.

What this does **not** buy: the payload body is fully predictable, so one captured payload permits
an offline dictionary attack on the secret, and the payload is replayable. Fixing either requires a
nonce or timestamp in the body, which is an approval-protocol version bump. Deferred deliberately.

Two further limitations are documented rather than fixed:

- **Ordinary container contents are not host-authoritative.** `ContainerOpen` operates on the
  client's own local `ContainerInventoryStore`; only the shared crate and death-drop crates go
  through the host. A dedicated server makes this more visible than LAN play did.
- **The server has no authoritative death state.** Vitals are per-peer local simulation by design,
  so `DeathDropInventory` can only be mitigated with a notification window and cooldowns, not
  properly validated.

### Known losses on the server platform

`BlockiverseWorldGenProfilingBridge` carries `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` and
installs WorldGen's profiler-marker callback. It stays in `Blockiverse.Gameplay`, which is excluded
from the server platform, so **WorldGen profiling silently no-ops on the dedicated server** — the
one platform where long-running generation cost matters most. The call is null-safe so nothing
crashes; the loss is invisible rather than loud. If server-side generation profiling is ever wanted,
that bridge has to move to an assembly the server compiles.

### Operational shape

The tick source is `Update`-driven off `Time.deltaTime`, so the server pins
`Application.targetFrameRate` and disables vsync; `WorldTimeClock` accumulates and emits whole ticks,
so tick counts stay frame-rate independent.

Shutdown does not rely on SIGTERM: `PosixSignalRegistration` is unavailable at this project's API
compatibility level and Unity's Linux player signal handling is unreliable. Durability comes from a
shorter default autosave interval, a bounded join on the in-flight save task, an admin `stop`
command as the recommended shutdown path, and a clean-shutdown marker file that makes a missed
signal observable at next boot instead of mysterious.

`MaxSupportedPlayers` is removed rather than raised. Above four players two things are unmeasured and
will be the first to break: late-join sends a whole-world delta snapshot per joiner, and per-client
inventory snapshots are broadcast at 4 KB reliable-fragmented. The server warns loudly above four and
the operator documentation marks it unsupported. Raising it properly is a measured change with a
ruleset amendment.

### Process and policy

- `docs/rulesets/voxel_multiplayer_networking_ruleset.md` gains the `Server` session mode and the
  `StartServer` contract; the ruleset remains canonical for the protocol.
- The roadmap item "Cloud private worlds" is partially promoted: self-hosting ships, paid hosting
  stays a future feature.
- A new release lane (`.github/workflows/server-release.yml`) builds the server and publishes to
  ghcr. It must never receive Meta credentials, matching the existing PR-lane rule.
- `BlockiverseProjectBootstrapper.Run()` must stay byte-identically idempotent. The server scene gets
  its own entry point that deliberately does **not** call `Run()`, because `ConfigureAndroidPlayer`
  unconditionally switches the active build target to Android.
- `Assets/Blockiverse/Scenes/Boot.unity` is no longer literally the whole game: a generated server
  scene exists alongside it. It remains the whole *client* game — there is still no scene switching
  at runtime.
