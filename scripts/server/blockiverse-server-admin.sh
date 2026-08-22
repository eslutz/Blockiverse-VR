#!/bin/sh
# Send one admin command to a running Blockiverse dedicated server.
#
#   blockiverse-server-admin status
#   blockiverse-server-admin save
#   blockiverse-server-admin stop
#   blockiverse-server-admin list
#   blockiverse-server-admin kick 3
#   blockiverse-server-admin ban <playerId>
#
# The server's admin surface is a Unix domain socket, NOT a network port -- its file permissions
# are its access control. That is a deliberate design choice (no token to design, no port to
# accidentally expose), but it means you cannot reach it with curl, and `nc` is not installed on a
# minimal Debian server. Hence this helper: it uses whichever of python3 / socat / nc -U exists.
#
# Override the socket with BLOCKIVERSE_ADMIN_SOCKET, or --socket <path>.
#
# Exit codes: 0 command sent and answered, 1 no command given, 2 no usable client tool,
#             3 socket missing or unreachable.
set -eu

SOCKET="${BLOCKIVERSE_ADMIN_SOCKET:-/var/lib/blockiverse-server/admin.sock}"

if [ "${1:-}" = "--socket" ]; then
  [ $# -ge 2 ] || { echo "--socket needs a path" >&2; exit 1; }
  SOCKET="$2"
  shift 2
fi

if [ $# -eq 0 ]; then
  echo "usage: $(basename "$0") [--socket <path>] <command> [args...]" >&2
  echo "commands: help status list save stop kick <clientId> ban <playerId> unban <playerId>" >&2
  exit 1
fi

COMMAND="$*"

if [ ! -S "$SOCKET" ]; then
  {
    echo "No admin socket at $SOCKET"
    echo "The server creates it inside its world directory once it is running."
    echo "If the world directory is elsewhere, pass --socket <path> or set BLOCKIVERSE_ADMIN_SOCKET."
  } >&2
  exit 3
fi

if command -v python3 >/dev/null 2>&1; then
  BLOCKIVERSE_ADMIN_COMMAND="$COMMAND" BLOCKIVERSE_ADMIN_SOCKET_PATH="$SOCKET" python3 - <<'PYEOF'
import os, socket, sys

path = os.environ["BLOCKIVERSE_ADMIN_SOCKET_PATH"]
command = os.environ["BLOCKIVERSE_ADMIN_COMMAND"]

try:
    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as client:
        client.settimeout(15)
        client.connect(path)
        client.sendall(command.encode("utf-8"))
        # The server answers one command per connection and then closes, so read to EOF.
        chunks = []
        while True:
            data = client.recv(4096)
            if not data:
                break
            chunks.append(data)
except OSError as error:
    print(f"cannot talk to the server on {path}: {error}", file=sys.stderr)
    sys.exit(3)

sys.stdout.write(b"".join(chunks).decode("utf-8", "replace"))
PYEOF
elif command -v socat >/dev/null 2>&1; then
  printf '%s' "$COMMAND" | socat - "UNIX-CONNECT:$SOCKET"
elif command -v nc >/dev/null 2>&1 && nc -h 2>&1 | grep -q -- '-U'; then
  printf '%s' "$COMMAND" | nc -U "$SOCKET"
else
  {
    echo "No way to talk to a Unix socket: install python3 (recommended), socat, or an nc with -U."
    echo "  apt-get install -y python3"
  } >&2
  exit 2
fi
