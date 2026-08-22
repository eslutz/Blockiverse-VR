# Blockiverse Dedicated Server

> **Status: in development.** The pages here describe the server as designed and agreed in
> [ADR 0007](../adr/0007-self-hosted-dedicated-server.md). The build is proven — a Linux x86-64
> Dedicated Server player compiles and links — but the server entry point, configuration, and admin
> console are not implemented yet. Treat this as the specification these pages are written against,
> not as instructions for something you can download today.

A self-hosted dedicated server for Blockiverse VR: a headless process that owns the authoritative
world so it stays up when nobody is wearing a headset, and so people outside your LAN can join.

It runs the same simulation the in-headset host runs today, with no local player, no renderer, and
no XR.

## The two ways to run it

**A downloadable Linux x86-64 build.** Unpack it, put a `blockiverse-server.properties` next to it,
run the binary. Your world lives in a directory you choose.

**A container image on `ghcr.io`.** Mount a volume at `/data`, publish the UDP port, run it. The
image contains the same build as the archive — they are cut from one CI artifact so they cannot
drift.

Both are covered by the distribution grant in [LICENSE.md](../../LICENSE.md) and the terms in
[SERVER-EULA.md](../../SERVER-EULA.md). The compiled server may be run and redistributed; the source
in this repository may not.

## Pages

| | |
|---|---|
| [configuration.md](configuration.md) | Every setting, the file/env/CLI precedence order, admin commands, exit codes |
| [security-posture.md](security-posture.md) | **Read before exposing a port.** What the server protects against and — more importantly — what it does not |

## What to know before you start

**It is bounded, and it simulates everything.** Blockiverse worlds are fixed-size and fully
resident, so there is no view distance or simulation distance to tune. The server always simulates
the whole world.

**Four players is the supported ceiling.** The limit is not enforced — `server.max_players` is
honoured as you set it — but above four you are in unmeasured territory the project cannot support.
See the configuration reference for what scales badly.

**The world directory is the thing to back up.** Everything else is reproducible from the build.

**It was designed for LAN co-op among people who trust each other.** The dedicated server makes that
same protocol reachable from further away; it does not turn it into one built for hostile clients.
A server on a VPN is meaningfully safer than a server on a forwarded port, and the security page is
specific about why.

## Reporting problems

Server issues belong in the project issue tracker with the server version, your configuration file
with the secret removed, and the relevant log output. Performance reports above four players are
expected rather than actionable.
