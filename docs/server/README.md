# Blockiverse Dedicated Server

A self-hosted dedicated server for Blockiverse VR: a headless process that owns the authoritative
world, so it stays up when nobody is wearing a headset and people outside your LAN can join.

It runs the same simulation the in-headset host runs, with no local player, no renderer, and no XR.

Architecture and rationale: [ADR 0007](../adr/0007-self-hosted-dedicated-server.md).

## Before you start

**The server version must match the clients it serves.** A client on a different version is refused
with `GameVersionMismatch`. This is deliberate — it stops a stale server serving new clients — but it
means one server build serves exactly one client version.

**Read [security-posture.md](security-posture.md) before exposing a port.** It is specific about what
the server protects against and, more usefully, what it does not.

**Four players is the supported ceiling.** `server.max_players` is honoured as you set it, but above
four is unmeasured and unsupported.

## Running the container

The quickest path. Uses the image published to `ghcr.io`.

```sh
mkdir -p world
docker run -d --name blockiverse-server \
  -p 7777:7777/udp \
  -v "$PWD/world:/data" \
  -e BLOCKIVERSE_SERVER_NAME="My Server" \
  ghcr.io/eslutz/blockiverse-server:latest
```

Or with the committed [docker-compose.yml](../../docker-compose.yml):

```sh
docker compose up -d
docker compose logs -f
```

Stop it cleanly — this saves the world and records an orderly shutdown:

```sh
docker compose exec server /bin/sh -c 'printf stop | nc -U /data/admin.sock'
```

`docker compose stop` also works and the server still saves, but the admin `stop` is preferred.

## Running the archive

Download `blockiverse-server-<version>-linux-x86_64.tar.gz` from the releases page, verify it, and
run it:

```sh
sha256sum -c blockiverse-server-<version>-linux-x86_64.tar.gz.sha256
tar -xzf blockiverse-server-<version>-linux-x86_64.tar.gz
cd blockiverse-server
./BlockiverseServer -batchmode -nographics --world-dir ./world
```

`-batchmode -nographics` are required: there is no display, and without them the player looks for
one. Everything after those reaches the server's own option parser.

Create `blockiverse-server.properties` beside the binary to avoid repeating flags:

```ini
server.name = My Server
server.port = 7777
world.dir = ./world
persistence.autosave_seconds = 60
```

Full settings reference: [configuration.md](configuration.md).

## Connecting from a headset

In the multiplayer menu, enter the server's address. LAN discovery finds servers on your own
subnet automatically; for anything else, type it:

```
192.168.1.20          uses the default port 7777
play.example.com      a hostname
10.0.0.5:7788         a non-default port
```

Servers you join successfully are remembered, most recent first, so you only type an address once.

### Reaching a server over the internet

The server listens on **UDP 7777**. Forward that port on your router to the machine running the
server, then give players your public address.

A VPN is the better option where practical. A mesh VPN such as Tailscale or WireGuard gives you real
per-device identity, which the game protocol does not have — see the security page for why that
matters more than it might sound.

## Backing up the world

The world directory is the only thing you cannot rebuild. Copy it while the server is stopped, or
immediately after an admin `save`:

```sh
docker compose exec server /bin/sh -c 'printf save | nc -U /data/admin.sock'
tar -czf "blockiverse-world-$(date +%Y%m%d).tar.gz" world
```

Restore by stopping the server, replacing the directory, and starting it again.

If the server logs a warning about a missing clean-shutdown marker at boot, the previous run ended
without saving and up to one autosave interval of progress may be gone. That is what the marker is
for — an unclean stop should be visible, not silent.

## Administering a running server

Commands go to standard input (with `docker run -it`, or a foreground shell) or to a Unix socket at
`<world.dir>/admin.sock`.

```
help    status    list    save    stop
kick <clientId>   ban <playerId>   unban <playerId>
```

The socket is **not** a network port. Its file permissions are its access control, so do not put the
world directory on a share other users can write to.

## Reporting problems

Include the server version, your configuration file with the secret removed, and the relevant log
output. Performance reports above four players are expected rather than actionable.
