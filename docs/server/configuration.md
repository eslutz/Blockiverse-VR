# Blockiverse Dedicated Server — Configuration Reference

Every setting can be supplied three ways. Later sources win:

```text
built-in defaults  →  config file  →  environment variables  →  command-line arguments
```

That order is chosen for containers: the image ships a baseline file, a deployment overrides it with
environment variables, and a one-off run overrides that with an argument on the command line.

**Naming.** A setting written `world.dir` in the config file is `BLOCKIVERSE_WORLD_DIR` as an
environment variable and `--world-dir` as an argument. The mapping is mechanical: uppercase, dots and
dots-to-underscores with a `BLOCKIVERSE_` prefix for the environment; lowercase with dashes for the
command line.

**Unknown keys are fatal.** A misspelled setting stops the server at startup with a message naming
every problem it found, and exits `78`. It does not start with a silently defaulted value.

## Config file

Plain `KEY=VALUE`, one per line. `#` begins a comment. Blank lines are ignored. Values are not
quoted.

```ini
# blockiverse-server.properties
server.port = 7777
server.name = Eric's Server
server.max_players = 4

world.dir = /data/world
world.name = Home
world.gamemode = survival

persistence.autosave_seconds = 60
log.format = json
```

## Network

| Setting | Default | Notes |
|---|---|---|
| `server.port` | `7777` | UDP. This is the port to forward. |
| `server.listen_address` | `0.0.0.0` | Bind address. `0.0.0.0` accepts on all interfaces. |
| `server.advertised_address` | *(empty)* | Display only — logged and shown to operators. Does not affect binding. |
| `server.max_players` | `4` | **Not enforced above 4, and unsupported above 4.** See below. |
| `server.name` | `Blockiverse Server` | Shown to operators and in logs. |
| `server.frame_rate` | `60` | Process frame rate. The simulation tick is frame-rate independent; this caps CPU burn. |

### On `server.max_players`

The ceiling was deliberately removed so operators are not blocked by a hardcoded number, but **above
four players you are in unmeasured territory and the project cannot support you there.** Two things
scale badly and have not been profiled beyond four:

- Every joining player receives a whole-world delta snapshot in one message.
- Inventory snapshots broadcast per client at 4 KB, reliable-fragmented.

The server logs a warning at startup when configured above four. Please do not file performance
issues for large player counts; they are expected.

## World

| Setting | Default | Notes |
|---|---|---|
| `world.dir` | `./world` | Directory holding the save. In the container this is `/data`. Back this up. |
| `world.name` | `Blockiverse World` | Display name recorded in the save manifest. |
| `world.seed` | *(unset)* | Unset means random on first creation. Once a world exists, the seed is read from its manifest and this is ignored. |
| `world.preset` | `survival_terrain` | One of `survival_terrain`, `flat_builder`, `void_builder`. Applies only when creating a new world. |
| `world.gamemode` | `survival` | `survival` or `creative`. |

Changing `world.preset` or `world.seed` does **not** regenerate an existing world. To start fresh,
move the old world directory aside.

## Persistence

| Setting | Default | Notes |
|---|---|---|
| `persistence.autosave_seconds` | `60` | Minimum 30. The server defaults lower than the in-game host because an unattended server should lose less on an unclean stop. |
| `persistence.save_on_stop` | `true` | Save during shutdown preparation. Turning this off risks losing everything since the last autosave. |
| `persistence.max_stashed_players` | `64` | How many disconnected players' inventories are held for reconnect. Bounded on purpose — see [security posture](security-posture.md). |

## Security

| Setting | Default | Notes |
|---|---|---|
| `security.require_secret` | `false` | When true, the server refuses to start without a `server.secret`. |
| `security.identity` | `none` | `none` or `meta`. With `meta`, every joining player must prove a Meta account and the server verifies the proof with Meta. See below. |
| `security.meta.app_id` | *(empty)* | Your Meta app id. Required when `security.identity = meta`. |
| `security.meta.app_secret_path` | *(empty)* | Path to a file holding the Meta app secret. A path on purpose — the secret must not sit in a config file you might paste into a bug report. Keep the file readable by the server user only. |
| `security.allowlist_path` | *(empty)* | File of permitted player identifiers, one per line. When set, only listed players may join. |
| `security.banlist_path` | *(empty)* | File of banned player identifiers, one per line. `meta:<userId>` entries ban a verified Meta account. |
| `security.tls.enabled` | `false` | Enables DTLS transport encryption. Requires cert and key. |
| `security.tls.cert_path` | *(empty)* | PEM certificate **chain** (leaf plus intermediates — for Let's Encrypt, `fullchain.pem`). |
| `security.tls.key_path` | *(empty)* | PEM private key. Keep it unreadable by other users. |
| `security.tls.server_name` | `blockiverse-server` | Name clients validate against. Must match the DNS name on your certificate. |

### The join secret

`server.secret` is the server's shared password. Players type it once into the multiplayer panel's
password field; it is remembered per server after a successful join. It is verified with a
per-connection challenge — the server sends a random nonce and the client answers with an HMAC over
it — so **the secret itself never crosses the wire**, a captured join cannot be replayed, and one
observation is not enough for an offline dictionary attack. A client that fails or ignores the
challenge is disconnected within ten seconds and receives no world data.

Use a long random value. It is still one password shared by everyone you invite; revoking it means
changing it for all of them. For per-person control, use `security.identity = meta`.

Setting `security.require_secret = true` without a `server.secret` is a fatal startup error: an
operator asking for a private server must not get an open one.

### Meta identity (`security.identity = meta`)

With identity on, every joining headset proves which Meta account it is signed into: the client
fetches a one-shot proof from the platform (`Users.GetUserProof`), and the server verifies it
directly with Meta's `user_nonce_validate` endpoint using your app credentials. This is real,
revocable, per-account identity:

- The server log names the verified account on every join.
- Ban an account by adding `meta:<userId>` to the ban-list file — the ban survives reinstalls and
  cannot be dodged by wiping local data, unlike GUID bans.
- If Meta's endpoint cannot be reached, the join is **refused**, not waved through.

Requires outbound HTTPS from the server to `graph.oculus.com`, and both `security.meta.app_id` and
`security.meta.app_secret_path`. Missing either is a fatal startup error, as is `security.meta.*`
set while `security.identity` is `none`.

### TLS

With a DNS name pointed at your server and a certificate from any ACME provider (Let's Encrypt),
players need **zero setup**: the client ships the ISRG root certificates and validates your
certificate chain against them, exactly like a browser. Players tick "Encrypted" in the multiplayer
panel when joining; tell them to, since the client cannot detect it. Set `security.tls.server_name`
to the DNS name on the certificate, and renew certificates the same way you would for a web server
(the server reads the files at startup, so restart after renewal).

A self-signed certificate also works but pushes trust distribution onto you: each player's
`servers.json` bookmark must carry your CA certificate in its `tlsPinnedCaPem` field, which today
means editing a file on the headset. Prefer the ACME path.

`security.tls.cert_path`, `security.tls.key_path`, and `security.tls.server_name` are rejected when
set without `security.tls.enabled` — material nothing reads is much more likely to be a mistake
than an intention.

## Logging and administration

| Setting | Default | Notes |
|---|---|---|
| `log.level` | `info` | `error`, `warn`, `info`, or `debug`. |
| `log.format` | `text` | `text` for humans, `json` for log shipping. |
| `admin.stdin_enabled` | `true` | Read admin commands from standard input. Useful with `docker run -it`. |
| `admin.socket_path` | `<world.dir>/admin.sock` | Unix domain socket for admin commands. Its permissions are its authorization — there is no token and no network port. |

The admin socket is intentionally **not** a TCP or HTTP port. Do not expose it, and do not place the
world directory on a share other users can write to.

### Admin commands

```text
help                  list commands
status                uptime, world, player count, last save
list                  connected players
save                  save the world now
stop                  save, disconnect everyone, and exit cleanly
kick <clientId>       disconnect one player
ban <playerId>        add to the ban list and disconnect
unban <playerId>      remove from the ban list
```

`stop` is the correct way to shut down. It waits for any in-flight save, writes the world, and exits.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Clean shutdown. |
| `78` | Configuration error. The message lists every problem found. |

## Settings that deliberately do not exist

**There is no view distance or simulation distance.** Blockiverse worlds are bounded and fully
resident — the server always simulates the whole world. A knob here would not do anything, so there
isn't one.

**There is no `server.tick_rate`.** Netcode hashes the tick rate into the handshake that gates
every connection, and the client's value is fixed at 30 in the shipped prefab. Any other value on
the server produces a different hash, so Netcode drops every joining client *before* Blockiverse's
own validation runs — no rejection reason, nothing useful in the log, and a server that looks
healthy while nobody can join. A setting whose only valid value is the default is not a setting.

**There is no `server.motd` and no `log.dir`.** Both were drafted and then removed rather than
shipped inert. A message of the day needs a wire message the protocol does not have yet, and a log
directory needs diagnostic file routing that is currently gated to development builds. A documented
setting that silently does nothing is worse than an absent one, because you would believe it worked.
Logs go to stdout, which is where a container log driver and `journalctl` both expect them.
