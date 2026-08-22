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
# The container runs unprivileged as uid 10001. A bind-mounted directory keeps the HOST's
# ownership -- the image's own chown only applies to named volumes -- so without this the server
# can see /data and still not be able to write its world.
sudo chown -R 10001:10001 world
docker run -d --name blockiverse-server \
  -p 7777:7777/udp \
  -v "$PWD/world:/data" \
  -e BLOCKIVERSE_SERVER_NAME="My Server" \
  ghcr.io/eslutz/blockiverse-server:latest
```

The server checks this at startup and refuses to run with an explicit message rather than failing
at the first autosave. If you would rather not chown, use a named volume (`-v blockiverse-world:/data`),
which Docker seeds with the image's ownership.

Or with the committed [docker-compose.yml](../../docker-compose.yml):

```sh
docker compose up -d
docker compose logs -f
```

Stop it cleanly — this saves the world and records an orderly shutdown:

```sh
docker compose exec server blockiverse-server-admin stop
```

`docker compose stop` also works and the server still saves, but the admin `stop` is preferred: it
drives the save-then-mark-clean path deliberately rather than depending on signal delivery.

## Installing on Linux

Download `blockiverse-server-<version>-linux-x86_64.tar.gz` from the releases page and **verify the
checksum before extracting** — this is a binary you are about to run as a service:

```sh
sha256sum -c blockiverse-server-<version>-linux-x86_64.tar.gz.sha256
tar -xzf blockiverse-server-<version>-linux-x86_64.tar.gz
cd blockiverse-server
sudo ./install.sh
```

`install.sh` installs the player to `/opt/blockiverse-server`, creates the unprivileged
`blockiverse` system user, puts the world in `/var/lib/blockiverse-server`, writes a starter
config to `/etc/blockiverse-server/blockiverse-server.properties`, installs the
`blockiverse-server-admin` helper, and registers a systemd unit.

**It does not start the server.** Read the config and [security-posture.md](security-posture.md)
first, then:

```sh
sudo systemctl enable --now blockiverse-server
```

`enable` is what makes it come back after a reboot. Watch it with
`journalctl -u blockiverse-server -f`.

Override the locations with environment variables if the defaults do not suit you:

```sh
sudo INSTALL_DIR=/srv/blockiverse WORLD_DIR=/srv/blockiverse-world ./install.sh
```

**Upgrading** is the same command against a newer archive. The program directory is replaced
wholesale, but the world directory and your config file are never touched. Stop the service first
so the world is saved cleanly:

```sh
sudo systemctl stop blockiverse-server
sudo ./install.sh
sudo systemctl start blockiverse-server
```

Remember that clients must be on the same version, so upgrading the server means shipping the
matching client build too.

### Running it without installing

For a quick trial, or on a machine without systemd, run the binary directly:

```sh
./BlockiverseServer -batchmode -nographics --world-dir ./world
```

`-batchmode -nographics` are required: there is no display, and without them the player looks for
one. Everything after those reaches the server's own option parser. Nothing restarts it if it
exits, which is the whole reason the systemd path exists.

Create `blockiverse-server.properties` beside the binary to avoid repeating flags:

```ini
server.name = My Server
server.port = 7777
world.dir = ./world
persistence.autosave_seconds = 60
```

An unknown key in that file is fatal — the server exits `78` naming the key rather than starting
with a setting you believed you had applied. Full settings reference:
[configuration.md](configuration.md).

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
docker compose exec server blockiverse-server-admin save
tar -czf "blockiverse-world-$(date +%Y%m%d).tar.gz" world
```

Restore by stopping the server, replacing the directory, and starting it again.

If the server logs a warning about a missing clean-shutdown marker at boot, the previous run ended
without saving and up to one autosave interval of progress may be gone. That is what the marker is
for — an unclean stop should be visible, not silent.

## Administering a running server

```
help    status    list    save    stop
kick <clientId>   ban <playerId>   unban <playerId>
```

Commands reach the server two ways: standard input (under `docker run -it` or a foreground shell)
or a Unix socket at `<world.dir>/admin.sock`. Under systemd and `docker run -d` there is no stdin,
so the socket is the only route.

After a systemd install, use the helper:

```sh
sudo blockiverse-server-admin status
sudo blockiverse-server-admin list
sudo blockiverse-server-admin save
```

`list` prints both ids — the numeric client id that `kick` takes, and the player id that `ban`
takes. Banning also disconnects the player if they are currently connected.

In a container, the same helper is on the PATH:

```sh
docker compose exec server blockiverse-server-admin status
```

The socket is **not** a network port. Its file permissions are its access control, so do not put the
world directory on a share other users can write to. One consequence of the socket living inside the
world directory: a very deep `world.dir` can exceed the operating system's ~104-byte limit on Unix
socket paths, and the server logs a warning naming `admin.socket_path` as the fix.

## Reporting problems

Include the server version, your configuration file with the secret removed, and the relevant log
output. Performance reports above four players are expected rather than actionable.
