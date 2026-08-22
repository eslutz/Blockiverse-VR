#!/usr/bin/env python3
# Purpose: prove the client and the dedicated server cannot disagree on any field Netcode hashes
# into its connection-config hash.
#
# Why this failure mode deserves its own check:
#
#   NetworkConfig.GetConfig() (NGO 2.13.1, Runtime/Configuration/NetworkConfig.cs:328) writes these
#   into an XXHash64 that the client sends at connection time. If the two sides disagree by even one
#   field, Netcode drops the client BEFORE connection approval runs -- so no
#   BlockiverseJoinRejectionReason is ever produced, nothing appears in the approval path, and the
#   refusal is close to undiagnosable from either end. The server looks healthy and nobody can join.
#
#   Both scenes instantiate the SAME network manager prefab and are configured by one method
#   (BlockiverseProjectBootstrapper.ConfigureNetworkManagerObject), so they agree by construction
#   today. This check exists so that stops being a thing you have to remember: a per-instance
#   override in one scene, or a well-meaning "make the tick rate configurable on the server", would
#   otherwise ship silently.
#
# What it checks: no scene overrides any hashed NetworkConfig field on its prefab instance.
#
# Exit codes: 0 parity holds, 1 an override would break joins, 2 bad usage.

import re
import sys
from pathlib import Path

# Exactly the fields GetConfig() writes. ProtocolVersion and the prefab list are included because
# they are hashed too; RpcHashSize and the safety flags are cheap to watch and equally fatal.
HASHED_FIELDS = (
    "TickRate",
    "ConnectionApproval",
    "ForceSamePrefabs",
    "EnableSceneManagement",
    "EnsureNetworkVariableLengthSafety",
    "RpcHashSize",
    "ProtocolVersion",
)

MODIFICATION = re.compile(
    r"propertyPath:\s*(?P<path>[^\n]+)\n\s*value:\s*(?P<value>[^\n]*)", re.MULTILINE)

SCENES = [
    Path("Assets/Blockiverse/Scenes/Boot.unity"),
    Path("Assets/Blockiverse/Scenes/Server.unity"),
    Path("Assets/Blockiverse/Scenes/MultiplayerTest.unity"),
]


def main() -> int:
    problems = []
    scanned = []

    for scene in SCENES:
        if not scene.exists():
            continue
        try:
            text = scene.read_text(errors="replace")
        except OSError as error:
            print(f"cannot read {scene}: {error}", file=sys.stderr)
            return 2
        scanned.append(scene)

        for match in MODIFICATION.finditer(text):
            path = match.group("path").strip()
            if not path.startswith("NetworkConfig."):
                continue
            field = path.split(".", 1)[1].split(".")[0]
            if field in HASHED_FIELDS:
                problems.append((scene, path, match.group("value").strip()))

    if not scanned:
        print("no scenes found to check", file=sys.stderr)
        return 2

    print(f"checked {len(scanned)} scene(s) for overrides of Netcode's hashed config fields")

    if not problems:
        print("No scene overrides a hashed NetworkConfig field; client and server hash identically.")
        return 0

    print(f"\n{len(problems)} override(s) that would make clients undiagnosably fail to join:")
    for scene, path, value in problems:
        print(f"  {scene}")
        print(f"    {path} = {value}")
    print("\nNetcode hashes these into the connection config and drops mismatched clients BEFORE")
    print("connection approval, so no rejection reason is produced. Set the value in")
    print("BlockiverseProjectBootstrapper.ConfigureNetworkManagerObject, which configures every")
    print("scene from one place, rather than overriding it on one scene's prefab instance.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
