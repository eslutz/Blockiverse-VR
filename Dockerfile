# Blockiverse Dedicated Server
#
# This image does NOT build the server. It packages an already-built Linux player, so the archive
# on the release page and the image on ghcr.io are cut from one artifact and cannot drift. CI
# builds once, uploads, then feeds that output here as the build context.
#
# Build locally with:
#   scripts/unity/build-linux-server.sh
#   docker build --build-arg SERVER_DIR=Builds/LinuxServer -t blockiverse-server .
FROM debian:bookworm-slim

# The Unity Linux dedicated-server player links only against the glibc family -- verified with the
# ELF NEEDED entries: libc, libm, libgcc_s, libanl and the loader. No X11 and no GL, because the
# server subtarget has no graphics. So the slim base needs nothing added for the player itself.
#
# netcat-openbsd IS needed, and not as a convenience: the admin surface is a Unix domain socket,
# this base image ships no client that can speak to one, and under `docker run -d` there is no
# stdin either. Without it the container has NO way to run `save` or a clean `stop` -- so every
# shutdown would be a hard kill and the documented admin commands would fail with "nc: not found".
# It is ~30 KB; python3 would do the same job for a thousand times the size.
RUN apt-get update \
 && apt-get install --no-install-recommends --assume-yes ca-certificates netcat-openbsd \
 && rm -rf /var/lib/apt/lists/*

# Unprivileged by construction. The world directory is the only thing the server writes, and a
# server reachable from the internet should not be running as root over it.
RUN groupadd --gid 10001 blockiverse \
 && useradd --uid 10001 --gid 10001 --home-dir /opt/blockiverse --no-create-home blockiverse

ARG SERVER_DIR=Builds/LinuxServer

WORKDIR /opt/blockiverse

# Copy the known contents rather than the whole directory: a Unity build also emits
# BurstDebugInformation_DoNotShip, and OVRDumpBuildInfo drops RuntimeActionBindings.json in
# because it treats a Standalone target as "PC". Neither belongs in a shipped image.
COPY ${SERVER_DIR}/BlockiverseServer        ./BlockiverseServer
COPY ${SERVER_DIR}/UnityPlayer.so           ./UnityPlayer.so
COPY ${SERVER_DIR}/BlockiverseServer_Data   ./BlockiverseServer_Data

# The same admin helper the systemd install uses, so `docker exec` and a native install are
# administered with one command instead of two different incantations.
#
# Copied from source, NOT from ${SERVER_DIR}: the build context is the repo root either way, and
# sourcing it from the staged output would mean a local
# `docker build --build-arg SERVER_DIR=Builds/LinuxServer` fails, because a Unity build does not
# produce this file. One source, both paths.
COPY scripts/server/blockiverse-server-admin.sh /usr/local/bin/blockiverse-server-admin
RUN chmod +x /usr/local/bin/blockiverse-server-admin

RUN chmod +x ./BlockiverseServer \
 && mkdir -p /data \
 && chown -R blockiverse:blockiverse /opt/blockiverse /data

# The world. Mount something durable here or you lose it with the container.
VOLUME ["/data"]

# The game port. UDP -- Unity Transport does not use TCP.
EXPOSE 7777/udp

USER blockiverse

ENV BLOCKIVERSE_WORLD_DIR=/data \
    BLOCKIVERSE_SERVER_LISTEN_ADDRESS=0.0.0.0 \
    BLOCKIVERSE_LOG_FORMAT=text \
    BLOCKIVERSE_ADMIN_SOCKET=/data/admin.sock

# A UDP game port has nothing to probe and the admin socket needs a client, so the server touches
# a heartbeat file every 10s and the check reads its age. Stale means the main loop has stopped
# even if the process is technically alive, which is the failure a PID check would miss.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
  CMD test -f /data/.heartbeat \
   && [ $(( $(date +%s) - $(date -r /data/.heartbeat +%s) )) -lt 60 ] || exit 1

# -batchmode -nographics: there is no display, and without them the player looks for one.
# Arguments after these reach the server's own option parser, so `docker run ... --server-port 7788`
# works as an operator expects.
ENTRYPOINT ["./BlockiverseServer", "-batchmode", "-nographics"]
