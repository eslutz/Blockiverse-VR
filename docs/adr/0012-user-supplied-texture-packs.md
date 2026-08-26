# ADR 0012 — User-supplied texture packs

**Status:** Accepted
**Date:** 2026-08-26

## Context

Generating and curating block textures is slow, and it does not get faster with scale. Opening the game to community art is a way out, but the obvious route — reading another game's resource packs — carries risks that are easy to underestimate.

This is cross-cutting: it touches Core, Persistence, Networking, WorldRuntime, Gameplay and UI, and it changes the atlas UV contract. Hence an ADR rather than a note in a ruleset.

## Decision

Ship a **first-party pack format** keyed on Blockiverse's own canonical texture names, loaded from the player's device at runtime, and never transmitted between players.

---

## 1. Our own format, not another game's

No Minecraft asset, namespace, file layout, or trademark enters the shipped app. Packs are named for Blockiverse textures (`meadow_turf.png`), not another game's.

The reasoning is worth recording because "we'd only be reading the format, not shipping the art" sounds sufficient and is not:

- **Most resource packs are all-rights-reserved by default.** Uploading to a hosting site does not grant downstream games a licence, and many packs are themselves derivative of the original game's art — so the pack author often cannot grant rights they do not hold.
- **The sharper risk is trademark, not copyright.** Marketing compatibility with a named game is trademark use. The realistic failure is not a lawsuit but a store complaint: a rights-holder needs only to file one, and a Quest Store takedown costs us a release.
- **A format reader is defensible; a substitute is not.** Ecosystem tooling has historically been tolerated where things that substitute for the original have not, and a VR voxel sandbox advertising another game's packs sits on the wrong side of that line.

Nothing stops a third party writing a converter against our documented format. That conversion runs on their machine, against art they hold rights to, and yields an ordinary pack. **We publish the format; we do not build the bridge.**

The practical point is that this was never a shortcut anyway: every block still needs a shipped default for packs to fall back to, and most Blockiverse blocks (Glowwick, Staropal Geode, Bellows Forge) have no counterpart to map from.

## 2. Never on the wire

A texture selection is local to each peer. It is not in the connection-approval payload, the world-snapshot header, any RPC, or any `NetworkVariable`, and pack files are never transmitted.

- **Technically it buys nothing.** A client cannot use a token for a pack it does not have.
- **The obvious fix is the dangerous one.** "Then send the pack too" turns a local render of somebody's art into redistribution of it, from a store-submitted app.
- **Rendering is not simulation.** Nothing about which texture a peer draws can affect world state, so there is nothing that needs to agree.

This was already true by accident — nothing in `Networking/` carried the value — but a client rendered with whatever its world manager's field happened to hold, which was leftover state rather than a decision. Clients now apply their own preference explicitly, and a file-set guard plus a reflection guard fail if a texture value is ever threaded into a wire format.

## 3. Requested vs effective selection

A resolution carries **both** what was asked for and what can be drawn. Callers persist the *requested* token and render the *effective* one.

Collapsing them is a data-loss bug, not an abstraction nicety. `BlockTextureSetIds.Normalize` coerces anything unrecognised to the default — correct for a built-in id, where there are four legal values and anything else is corruption. Applied to a pack token it means: the load renders the default **and the next autosave writes that default back**. A pack the player had merely moved or not yet reinstalled is then permanently forgotten, and they discover it only after reinstalling and finding the world ignores it.

The `pack:` prefix is what makes the two cases distinguishable without touching the filesystem, which is what lets Core own the rule while Persistence and Networking use it.

## 4. Directory, not archive

v1 accepts a directory only. The project has **zero** runtime archive code; adding `System.IO.Compression` means IL2CPP stripping verification and zip-slip validation for no v1 benefit. On Quest a directory is also easier to hand-tweak over MTP than a repacked archive.

## 5. Composite at runtime over a shipped atlas

A pack is composited over one of the four built-in atlases rather than replacing it, so a partial pack blends rather than leaving holes. Three consequences:

- **GPU readback, not readable imports.** The shipped atlases are `isReadable: 0`. Making them readable would cost every player ~4 MB permanently, including the majority who never install a pack, because all four load at scene load via a serialized array. A readback costs ~1.4 MB once, only when a pack is applied.
- **`RenderTextureReadWrite.sRGB` is load-bearing.** The project renders Linear and the atlas imports as sRGB. A Linear render target stores linear values verbatim and yields a washed-out atlas that looks entirely plausible in a screenshot and wrong on device.
- **CPU compositing, not GPU.** `Graphics.CopyTexture` cannot stretch, so replicating a 1-texel edge into an 8-texel padding band would need a blit per band or a custom shader — and a shader reached via `Shader.Find` is exactly the stripping trap `BlockVisualAtlas` already documents. CPU keeps the padding byte-identical to the offline generator and unit-testable.

Mip chains are generated by hand because `mipMapsPreserveCoverage` is an **import-time** setting that `Apply(updateMipmaps: true)` does not reproduce; without it foliage cutouts thin and vanish with distance while built-in tiles stay solid.

## 6. `UvInsetPixels` expressed at `MaxAtlasScale`

`BuildTileRect` applied a half-texel inset in *authored* pixels. Normalized UVs are scale-free, so on an atlas composited at scale S that becomes `0.5 × S` target texels — at scale 2 the outermost texel of every edge of a 64 px pack tile would never be sampled, silently cropping the author's art.

Dividing by `MaxAtlasScale` makes `GetTileRect` independent of which atlas is bound. That is also precisely what lets a texture change skip the chunk re-mesh: if UVs depended on the atlas, every swap would be a full world rebuild.

At scale 1 the inset is 1/8 of a texel, safe because the padding is edge-clamp **replication** — the texel just outside the tile is a byte-identical copy of the edge texel.

## 7. No save schema bump

The manifest's `textureSet` field already existed and was unvalidated; the vocabulary widens within it. The schema is frozen at v1 for alpha regardless ([voxel_save_versioning_schema.md §2.0](../rulesets/voxel_save_versioning_schema.md)).

---

## Consequences

**Good**
- Community art without shipping or transmitting anyone's assets.
- Texture changes are live: rebinding four materials repaints the world with no re-mesh, where previously the only path destroyed and rebuilt every chunk.
- Three copies of the tile mapping are now pinned to each other by test, closing a drift `BlockVisualAtlas` had flagged for a long time.

**Costs**
- A composited atlas is a runtime-owned `Texture2D` with no asset behind it, so ownership and disposal are manual (the same discipline `skyMaterial` already needs).
- Hand-built mip chains are more code than `Apply(updateMipmaps: true)`, and their failure mode is visual and distance-dependent rather than a test failure.
- 64 px packs double atlas memory.

**Accepted risks**
- **Symlinks.** .NET on Android follows them, so a symlinked `blocks/` could point outside the pack root. Accepted for v1: the pack root is the app's own sandbox, every read is length-capped, and tile names are restricted to `[a-z0-9_]` so no traversal is expressible through a name.
- **Device-only rendering faults.** The sRGB round trip and mip coverage both fail by looking slightly wrong at distance rather than by failing a test. Mitigated by an empty-pack byte-identity check on mip 0, and a headset pass is still required.

## Alternatives rejected

| Alternative | Why not |
|---|---|
| Read another game's resource packs | §1 |
| Replace the atlas instead of compositing | Partial packs would leave holes; every pack would have to be complete |
| Ship packs to other players | §2 — redistribution, from a store-submitted app |
| Flip the shipped atlases to readable | ~4 MB permanent cost for every player to benefit the few with packs |
| Generate the tile-name table from Python at build time | Adds a generated-source step to a repo whose generated-artifact rules already cause accidental commits, and buys nothing a test does not |
