#!/usr/bin/env python3
# Purpose: assert that every expected test assembly was DISCOVERED by the Unity test run, and
# optionally that none of them shrank.
#
# Why this exists, beyond the pass/fail the runner already reports:
#
#   An asmdef whose reference fails to resolve drops out of test discovery ENTIRELY rather than
#   failing to compile loudly. The assembly simply is not there, every test that ran passed, and
#   the run reports green with a silently smaller suite. Totals do not catch it reliably either,
#   because a change that adds cases can net out an assembly that vanished.
#
#   Three EditMode assemblies are small enough (MetaPlatform 8, MetaAvatars 16, SurvivalHealth 28)
#   to disappear without visibly moving an ~880 total. The assembly NAME set is the invariant that
#   survives that; per-assembly counts catch the narrower case of one test class failing to compile
#   while its assembly survives.
#
# Assembly names are stable across branches and are asserted by default. Case COUNTS are
# branch-specific (a feature branch legitimately adds tests), so they are compared only against a
# locally recorded baseline via --record / --against.
#
# Usage:
#   scripts/unity/check-test-suites.py                    # assert the assembly set
#   scripts/unity/check-test-suites.py --record           # record current counts as a baseline
#   scripts/unity/check-test-suites.py --against FILE     # also assert counts did not shrink
#   scripts/unity/check-test-suites.py --against FILE --exact   # counts must match exactly
#
# Exit codes: 0 ok, 1 discovery/size failure, 2 bad usage, 66 results missing.

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# Every assembly the Unity test runner is expected to discover. Names, not counts: these are stable
# across branches, and a missing name is the failure this script exists to catch.
# Unity.Addressables.DocExampleCode.Editor.Tests is a package test that rides along, not noise.
EXPECTED_ASSEMBLIES = {
    "PlayMode": {
        "Blockiverse.Tests.PlayMode.dll",
        "Blockiverse.Tests.Networking.PlayMode.dll",
    },
    "EditMode": {
        "Blockiverse.Tests.EditMode.dll",
        "Blockiverse.Tests.Survival.EditMode.dll",
        "Blockiverse.Tests.Networking.EditMode.dll",
        "Blockiverse.Tests.EditMode.SurvivalHealth.dll",
        "Blockiverse.Tests.MetaAvatars.EditMode.dll",
        "Blockiverse.Tests.MetaPlatform.EditMode.dll",
        "Blockiverse.Tests.Server.EditMode.dll",
        "Unity.Addressables.DocExampleCode.Editor.Tests.dll",
    },
}


def read_suites(results_dir: Path, platform: str):
    path = results_dir / f"{platform}.xml"
    if not path.exists():
        return None, path
    root = ET.parse(path).getroot()
    suites = {
        s.get("name"): int(s.get("testcasecount") or 0)
        for s in root.iter("test-suite")
        if s.get("type") == "Assembly"
    }
    totals = {k: root.get(k) for k in ("total", "passed", "failed")}
    return (suites, totals), path


def main() -> int:
    parser = argparse.ArgumentParser(add_help=True)
    parser.add_argument("--results-dir", default="TestResults/Unity")
    parser.add_argument("--record", action="store_true", help="write current counts as a baseline")
    parser.add_argument("--against", metavar="FILE", help="baseline file to compare counts against")
    parser.add_argument("--exact", action="store_true", help="counts must match the baseline exactly")
    args = parser.parse_args()

    if args.exact and not args.against:
        print("--exact requires --against FILE", file=sys.stderr)
        return 2

    results_dir = Path(args.results_dir)
    observed, failures = {}, []

    for platform in ("EditMode", "PlayMode"):
        result, path = read_suites(results_dir, platform)
        if result is None:
            print(f"{platform}: {path} not found — did the test run produce results?", file=sys.stderr)
            return 66

        suites, totals = result
        observed[platform] = suites
        expected = EXPECTED_ASSEMBLIES[platform]

        print(f"\n{platform}  total={totals['total']} passed={totals['passed']} "
              f"failed={totals['failed']}  assemblies={len(suites)}")

        for name in sorted(expected | set(suites)):
            if name not in suites:
                failures.append(f"{platform}: MISSING assembly {name} — dropped from test discovery")
                print(f"  !!  ----  {name}   MISSING")
            elif name not in expected:
                print(f"  +   {suites[name]:>4}  {name}   (not in the expected set)")
            else:
                print(f"      {suites[name]:>4}  {name}")

    if args.record:
        target = Path(args.against) if args.against else results_dir / "suite-baseline.json"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(observed, indent=2, sort_keys=True) + "\n")
        print(f"\nRecorded baseline -> {target}")

    elif args.against:
        baseline_path = Path(args.against)
        if not baseline_path.exists():
            print(f"\nbaseline {baseline_path} not found; record one with --record --against FILE",
                  file=sys.stderr)
            return 2
        baseline = json.loads(baseline_path.read_text())
        for platform, expected_counts in baseline.items():
            for name, want in expected_counts.items():
                got = observed.get(platform, {}).get(name)
                if got is None:
                    continue  # already reported as missing above
                if got < want:
                    failures.append(
                        f"{platform}: {name} has {got} cases, baseline {want} "
                        f"({want - got} lost — a test class likely stopped compiling)")
                elif args.exact and got != want:
                    failures.append(
                        f"{platform}: {name} has {got} cases, baseline {want} (+{got - want}; "
                        f"--exact was requested)")

    print()
    if failures:
        print("SUITE DISCOVERY FAILURES:")
        for f in failures:
            print("  -", f)
        return 1

    print("All expected test assemblies were discovered.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
