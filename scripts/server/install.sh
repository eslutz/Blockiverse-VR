#!/usr/bin/env bash
# Install the Blockiverse dedicated server as a systemd service on Linux x86-64.
#
# Run this from inside the extracted release archive:
#
#   tar -xzf blockiverse-server-<version>-linux-x86_64.tar.gz
#   cd blockiverse-server
#   sudo ./install.sh
#
# What it does, and deliberately does not do:
#   - installs the player to /opt/blockiverse-server (replaced wholesale on upgrade)
#   - creates the unprivileged system user `blockiverse`
#   - creates the world directory /var/lib/blockiverse-server and NEVER touches it again, so
#     re-running this to upgrade is safe
#   - writes /etc/blockiverse-server/blockiverse-server.properties only if it does not exist,
#     so your settings survive an upgrade
#   - installs the unit but does NOT start the server: read the config and the security posture
#     first, then start it yourself
#
# Override paths with INSTALL_DIR, WORLD_DIR, CONFIG_DIR, SERVER_USER.
#
# Exit codes: 1 not root, 2 wrong directory / missing files, 3 unsupported system.
set -euo pipefail

INSTALL_DIR="${INSTALL_DIR:-/opt/blockiverse-server}"
WORLD_DIR="${WORLD_DIR:-/var/lib/blockiverse-server}"
CONFIG_DIR="${CONFIG_DIR:-/etc/blockiverse-server}"
SERVER_USER="${SERVER_USER:-blockiverse}"
SERVER_GROUP="$SERVER_USER"
UNIT_PATH="/etc/systemd/system/blockiverse-server.service"
ADMIN_PATH="/usr/local/bin/blockiverse-server-admin"
SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

die() { echo "$@" >&2; exit "${EXIT_CODE:-2}"; }

if [ "$(id -u)" -ne 0 ]; then
  EXIT_CODE=1 die "This installer must run as root. Try: sudo ./install.sh"
fi

# Guard the destructive step below against a mistyped override.
case "$INSTALL_DIR" in
  ""|"/"|"/usr"|"/etc"|"/var"|"/opt"|"/home"|"/root"|"/bin"|"/sbin"|"/lib")
    EXIT_CODE=2 die "Refusing to use '$INSTALL_DIR' as INSTALL_DIR." ;;
esac

for required in BlockiverseServer UnityPlayer.so BlockiverseServer_Data; do
  [ -e "$SOURCE_DIR/$required" ] || die \
    "Missing '$required' beside this script. Run it from inside the extracted archive directory."
done

command -v systemctl >/dev/null 2>&1 || EXIT_CODE=3 die \
  "systemctl not found. This installer targets systemd Linux; run the binary directly instead:
  $SOURCE_DIR/BlockiverseServer -batchmode -nographics --world-dir <dir>"

if [ "$(uname -m)" != "x86_64" ]; then
  EXIT_CODE=3 die "This build is Linux x86-64 only; this machine reports $(uname -m)."
fi

echo "Installing the Blockiverse dedicated server"
echo "  program: $INSTALL_DIR"
echo "  world:   $WORLD_DIR"
echo "  config:  $CONFIG_DIR"

if ! getent group "$SERVER_GROUP" >/dev/null; then
  groupadd --system "$SERVER_GROUP"
  echo "  created group $SERVER_GROUP"
fi

if ! getent passwd "$SERVER_USER" >/dev/null; then
  useradd --system --gid "$SERVER_GROUP" --home-dir "$WORLD_DIR" \
          --no-create-home --shell /usr/sbin/nologin "$SERVER_USER"
  echo "  created user $SERVER_USER"
fi

# Replace the program directory wholesale so an upgrade cannot leave stale managed assemblies
# behind. The world directory is separate and is never touched here.
rm -rf "${INSTALL_DIR:?}"
mkdir -p "$INSTALL_DIR"
cp -r "$SOURCE_DIR/BlockiverseServer" \
      "$SOURCE_DIR/UnityPlayer.so" \
      "$SOURCE_DIR/BlockiverseServer_Data" \
      "$INSTALL_DIR/"
for doc in README.md configuration.md security-posture.md SERVER-EULA.md; do
  [ -f "$SOURCE_DIR/$doc" ] && cp "$SOURCE_DIR/$doc" "$INSTALL_DIR/"
done
chmod +x "$INSTALL_DIR/BlockiverseServer"

mkdir -p "$WORLD_DIR" "$CONFIG_DIR"
chown -R "$SERVER_USER:$SERVER_GROUP" "$INSTALL_DIR" "$WORLD_DIR"

CONFIG_FILE="$CONFIG_DIR/blockiverse-server.properties"
if [ -f "$CONFIG_FILE" ]; then
  echo "  kept existing $CONFIG_FILE"
else
  cat > "$CONFIG_FILE" <<EOF
# Blockiverse dedicated server configuration.
# Full reference: $INSTALL_DIR/configuration.md
#
# An unknown key here is FATAL and the server exits 78 rather than starting with a setting you
# thought you had set. A typo is reported by name, with a suggestion.

server.name = Blockiverse Server
server.port = 7777
server.listen_address = 0.0.0.0

# Four players is the supported ceiling. Higher is honoured, unmeasured, and your risk.
server.max_players = 4

# The shared secret clients must present. Leaving this empty uses the built-in default key, which
# every client build already knows -- fine on a trusted LAN, not fine on a forwarded port.
# Set security.require_secret = true to refuse to start without a real one.
server.secret =
security.require_secret = false

world.dir = $WORLD_DIR

# 60s bounds how much progress a crash can cost.
persistence.autosave_seconds = 60
persistence.save_on_stop = true

log.level = info
log.format = text
EOF
  chmod 640 "$CONFIG_FILE"
  chown root:"$SERVER_GROUP" "$CONFIG_FILE"
  echo "  wrote $CONFIG_FILE"
fi

install -m 755 "$SOURCE_DIR/blockiverse-server-admin.sh" "$ADMIN_PATH"

# Rewrite the unit's paths so non-default overrides actually work.
sed -e "s#^WorkingDirectory=.*#WorkingDirectory=$INSTALL_DIR#" \
    -e "s#^ExecStart=.*#ExecStart=$INSTALL_DIR/BlockiverseServer -batchmode -nographics --config $CONFIG_FILE#" \
    -e "s#^ReadWritePaths=.*#ReadWritePaths=$WORLD_DIR#" \
    -e "s#^ExecStop=.*#ExecStop=-$ADMIN_PATH --socket $WORLD_DIR/admin.sock stop#" \
    -e "s#^User=.*#User=$SERVER_USER#" \
    -e "s#^Group=.*#Group=$SERVER_GROUP#" \
    "$SOURCE_DIR/blockiverse-server.service" > "$UNIT_PATH"
chmod 644 "$UNIT_PATH"

systemctl daemon-reload

cat <<EOF

Installed. The server is NOT running yet, on purpose.

1. Read the security posture before exposing a port:
     less $INSTALL_DIR/security-posture.md

2. Review the configuration, and set a secret if this server will be reachable from the internet:
     sudoedit $CONFIG_FILE

3. Start it, and have it come back after a reboot:
     sudo systemctl enable --now blockiverse-server
     sudo systemctl status blockiverse-server
     sudo journalctl -u blockiverse-server -f

4. Administer it:
     sudo blockiverse-server-admin status
     sudo blockiverse-server-admin list
     sudo blockiverse-server-admin save

The world lives in $WORLD_DIR and is never modified by this installer, so re-running it to
upgrade is safe. Back that directory up; it is the only thing you cannot rebuild.

Clients must be on the SAME version as this server or every join is refused.
EOF
