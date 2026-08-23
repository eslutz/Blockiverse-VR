#!/usr/bin/env python3
# Purpose: keep the server's configuration keys, its generated config file, and its documentation
# from drifting apart.
#
# Why this is worth a script:
#
#   Unknown keys are FATAL -- the server prints the offending key and exits 78 rather than starting
#   with a setting the operator believed they had applied. That is the right behaviour, but it turns
#   two ordinary kinds of drift into outages:
#
#     1. install.sh generates /etc/blockiverse-server/blockiverse-server.properties. One typo there
#        and EVERY fresh install fails to boot, on a file the operator did not write.
#     2. A key documented in configuration.md that no longer exists sends an operator to write a
#        config that refuses to start, with the docs insisting they are right.
#
#   Renaming a key is a one-line change in the resolver, and nothing else in the repo fails.
#
# Checks:
#   - every key install.sh writes is implemented
#   - every implemented key is documented
#   - keys named in the "deliberately do not exist" section are NOT implemented (if one gets
#     implemented later, that prose becomes a lie and must be removed)
#
# Exit codes: 0 consistent, 1 drift found, 2 a source file is missing.

import re
import sys
from pathlib import Path

RESOLVER = Path("Assets/Blockiverse/Scripts/Server/BlockiverseServerOptionsResolver.cs")
INSTALLER = Path("scripts/server/install.sh")
DOCS = Path("docs/server/configuration.md")

KEY = r"[a-z_]+\.[a-z_.]+"
NAMESPACES = {"server", "world", "persistence", "security", "log", "admin"}


def main() -> int:
    for path in (RESOLVER, INSTALLER, DOCS):
        if not path.is_file():
            print(f"missing: {path}", file=sys.stderr)
            return 2

    implemented = set(re.findall(rf'\["({KEY})"\]', RESOLVER.read_text()))
    if not implemented:
        print("parsed zero keys from the resolver; the dictionary shape must have changed",
              file=sys.stderr)
        return 2

    installer = INSTALLER.read_text()
    try:
        block = installer.split('cat > "$CONFIG_FILE" <<EOF', 1)[1].split("\nEOF\n", 1)[0]
    except IndexError:
        print("could not find the generated config heredoc in install.sh", file=sys.stderr)
        return 2

    generated = set()
    for line in block.splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            generated.add(line.split("=", 1)[0].strip())

    docs = DOCS.read_text()
    # The prose section that names settings which intentionally do not exist.
    absent = set(re.findall(rf"There is no `({KEY})`", docs))
    absent |= set(re.findall(rf"no `({KEY})`", docs))
    documented = {k for k in re.findall(rf"`({KEY})`", docs) if k.split(".")[0] in NAMESPACES}

    problems = []
    for key in sorted(generated - implemented):
        problems.append(f"install.sh writes '{key}', which the resolver does not implement. "
                        f"Every fresh install would exit 78.")
    for key in sorted(implemented - documented):
        problems.append(f"'{key}' is implemented but never appears in {DOCS}.")
    for key in sorted(absent & implemented):
        problems.append(f"{DOCS} says '{key}' does not exist, but the resolver implements it.")

    print(f"{len(implemented)} implemented, {len(generated)} written by install.sh, "
          f"{len(documented)} documented, {len(absent)} documented as absent")

    if problems:
        print("\nconfiguration drift:")
        for problem in problems:
            print(f"  - {problem}")
        return 1

    print("Configuration keys, the generated config, and the documentation agree.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
