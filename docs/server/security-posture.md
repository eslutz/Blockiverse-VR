# Blockiverse Dedicated Server — Security Posture

**Read this before exposing a server to the public internet.**

This page states plainly what the server does and does not protect against. It is deliberately
specific about the gaps, because a server operator cannot make a sensible risk decision from
reassurance. Architecture rationale lives in [ADR 0007](../adr/0007-self-hosted-dedicated-server.md).

## Summary

| | |
|---|---|
| **Safe** | A server on your LAN, or reachable only over a VPN such as Tailscale or WireGuard. |
| **Acceptable with care** | A public server for people you know, running the server secret, with backups and a bounded blast radius (container, unprivileged user, dedicated world directory). |
| **Not recommended** | A public, unrestricted server open to strangers, or a server whose world you cannot afford to lose. |

Blockiverse's multiplayer protocol was designed for LAN co-op among people who trust each other. The
dedicated server does not change that: it makes the same protocol reachable from further away. The
hardening in the server release closes the flaws that are trivially exploitable; it does not turn the
protocol into one designed for hostile clients.

## What the server does protect

**The world is host-authoritative.** Clients send intent, never state. The server validates and
commits every block mutation through a single gate, and clients only mirror what the server
broadcasts. A modified client cannot write directly to the world.

**Survival actions are validated server-side.** Harvesting, placing, crafting, station use, and
shared-crate transfers are resolved against the *server's* copy of the player's inventory, not
against what the client claims to be holding. Item counts, slot indices, and recipe availability are
re-checked server-side.

**Actions are reach-limited.** The server rejects actions targeting blocks beyond interaction range
of the acting player's position. This check fails **closed**: if the server cannot resolve where a
player is, the action is refused rather than allowed.

**Request rate is limited per client.** Block mutations, survival commands, identity handshakes,
crouch updates, and avatar streams are each capped per client. Sustained violations escalate to a
disconnect rather than being silently counted.

**Reconnect state is bounded.** The store of inventories kept for disconnected players is capped and
aged out, so a client cannot grow the server's memory or its save files without limit by
reconnecting repeatedly.

**The server secret gates joining.** With `security.require_secret = true`, a client must present the
correct shared secret to be approved. Without it, anyone with a Blockiverse client can join.

## What the server does NOT protect

These are known, documented limitations. None of them are bugs to report.

**The join secret is a shared password, not per-player authentication.** Everyone on your server uses
the same secret. There is no account system. If you give the secret to ten people, you have ten
people who can each give it to ten more, and revoking it means changing it for everyone.

**The join handshake is replayable and brute-forceable offline.** The approval payload's contents are
predictable, so anyone who can capture one join can attempt an offline dictionary attack against your
secret, and can replay a captured payload. **Use a long, random secret** — treat it like a password,
not like a room name.

**Traffic is not encrypted by default.** Transport encryption exists but is off unless configured
with certificates. Assume anything on the wire — including the identity token that grants inventory
ownership on reconnect — is visible to anyone who can observe the network path.

**Player identity is a bearer token, not a login.** A returning player is recognised by an identifier
their client stores locally and sends on connect. Anyone who obtains that value can claim that
player's inventory. There is no password and no ownership proof.

**Ordinary container contents are not server-authoritative.** Chests and similar containers are
resolved on each client against its own local state; only the shared crate and death-drop crates go
through the server. Do not treat a chest as a secure store on a server with untrusted players.

**The server has no authoritative view of player death.** Vitals are simulated locally by each peer by
design. Inventory-drop-on-death is therefore constrained with cooldowns and a notification window
rather than genuinely validated.

**Movement is client-authoritative.** The server trusts each client's reported position for reach
checks. There is no speed or teleport plausibility checking, so a modified client can move
implausibly and act anywhere it claims to be. Reach limits are relative to a claimed position.

**There is no in-game moderation surface.** Kick and ban exist as operator commands on the admin
console. There is no reporting, no chat filtering, and no automatic abuse detection.

## Recommended deployment

1. **Prefer a VPN over a public port.** A mesh VPN gives you real per-device identity, which the
   game protocol does not have. This is the single largest improvement available to you.
2. **If you do expose a port**, forward only the game's UDP port. Do not expose the admin socket —
   it is a filesystem socket precisely so it cannot be reached from the network.
3. **Set a long random secret** and `security.require_secret = true`.
4. **Run unprivileged and contained.** Use the container image, or a dedicated non-root user. Give
   the server its own world directory and nothing else.
5. **Back up the world directory.** Copy it while the server is stopped, or immediately after an
   admin `save`. Save data is the thing you cannot re-create.
6. **Keep the player count small and known.** Counts above 4 are unsupported and unmeasured; see the
   configuration reference.
7. **Update when server releases ship.** Security fixes will arrive as new builds.

## Reporting a vulnerability

Report suspected vulnerabilities privately to the project owner rather than filing a public issue.
Please include the server version, what you observed, and how to reproduce it.
