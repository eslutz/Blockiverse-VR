#!/usr/bin/env python3
"""Extract per-agent structured outputs from the exhaustive-codebase-review workflow
transcripts and write them to the project root so completed work is never lost.

Idempotent: safe to re-run; overwrites output files with the latest state.
"""
import json
import glob
import os
import re

TDIR = "/Users/ericslutz/.claude/projects/-Users-ericslutz-Developer-Code-Blockiverse-Blockiverse-VR/c69bfede-e148-434b-978f-fac774d712b1/subagents/workflows/wf_53b36881-009"
ROOT = "/Users/ericslutz/Developer/Code/Blockiverse/Blockiverse-VR"

ROLE_TO_KEY = {
    "Game Design Expert": "game-design",
    "Game Logic Expert": "game-logic",
    "Security Expert": "security",
    "Anti-Pattern and Code Smell Expert": "anti-pattern",
    "VR / 3D Interaction Expert": "vr-interaction",
    "Performance and Optimization Expert": "performance",
    "Unity / C# Expert": "unity-csharp",
    "Dead Code and Unused Code Expert": "dead-code",
    "Test Coverage Expert": "test-coverage",
    "Asset Integration Expert": "asset-integration",
    "Runtime Logic Integration Expert": "runtime-wiring",
    "UI / Menu Flow Expert": "ui-menu-flow",
    "Localization Expert": "localization",
    "Accessibility Expert": "accessibility",
    "LAN Multiplayer Networking Expert": "lan-multiplayer",
}


def first_prompt(lines):
    try:
        d = json.loads(lines[0])
        content = d.get("message", {}).get("content")
        if isinstance(content, str):
            return content
        if isinstance(content, list):
            for c in content:
                if c.get("type") == "text":
                    return c.get("text", "")
    except Exception:
        pass
    return ""


def last_structured_output(lines):
    result = None
    for line in lines:
        try:
            d = json.loads(line)
        except Exception:
            continue
        if d.get("type") != "assistant":
            continue
        content = d.get("message", {}).get("content")
        if not isinstance(content, list):
            continue
        for c in content:
            if c.get("type") == "tool_use" and c.get("name") == "StructuredOutput":
                result = c.get("input")
    return result


def classify(prompt):
    if "You are deduplicating code-review findings" in prompt:
        return ("dedup", None, None)
    if "You are an adversarial verifier" in prompt:
        lens = "impact" if "YOUR LENS — IMPACT" in prompt else "evidence"
        m = re.search(r'"title":\s*"((?:[^"\\]|\\.)*)"', prompt)
        title = json.loads('"' + m.group(1) + '"') if m else "(unknown finding)"
        return ("verify", lens, title)
    m = re.search(r"YOUR ROLE: (.+?) \(", prompt)
    if m:
        role = m.group(1).strip()
        key = ROLE_TO_KEY.get(role)
        if key:
            return ("expert", key, role)
    return (None, None, None)


def render_expert_md(key, role, data, agent_id):
    out = []
    out.append(f"# Codebase Review — {role}")
    out.append("")
    out.append(f"> Workflow run `wf_53b36881-009`, agent `{agent_id}`. Raw expert output, pre-verification.")
    out.append("")
    out.append("## Area Reviewed")
    out.append("")
    out.append(data.get("areaReviewed", "(missing)"))
    out.append("")
    findings = data.get("findings", [])
    out.append(f"## Findings ({len(findings)})")
    out.append("")
    for i, f in enumerate(findings, 1):
        out.append(f"### {i}. {f.get('title', '(untitled)')}")
        out.append("")
        out.append(f"- **Severity:** {f.get('severity')}  |  **Confidence:** {f.get('confidence')}  |  **Needs manual verification:** {f.get('needsManualVerification')}")
        files = f.get("files", [])
        if files:
            out.append(f"- **Files:** {', '.join('`' + p + '`' for p in files)}")
        out.append(f"- **Impact:** {f.get('impact', '')}")
        out.append(f"- **Evidence:** {f.get('evidence', '')}")
        out.append(f"- **Recommended fix:** {f.get('recommendedFix', '')}")
        out.append("")
    positives = data.get("positives", [])
    out.append(f"## What Looks Good ({len(positives)})")
    out.append("")
    for p in positives:
        out.append(f"- {p}")
    out.append("")
    cnr = data.get("couldNotReview", [])
    out.append(f"## Could Not Review ({len(cnr)})")
    out.append("")
    for c in cnr:
        out.append(f"- {c}")
    out.append("")
    inspected = data.get("inspected", [])
    out.append(f"## Inspected ({len(inspected)})")
    out.append("")
    for p in inspected:
        out.append(f"- `{p}`")
    out.append("")
    return "\n".join(out)


def main():
    experts = {}       # key -> (role, data, agent_id, mtime) keep latest mtime
    dedup = None       # (data, agent_id)
    verifies = []      # (title, lens, data, agent_id, mtime)

    for path in sorted(glob.glob(os.path.join(TDIR, "agent-*.jsonl"))):
        agent_id = os.path.basename(path)[len("agent-"):-len(".jsonl")]
        try:
            with open(path) as fh:
                lines = fh.readlines()
        except Exception:
            continue
        if not lines:
            continue
        prompt = first_prompt(lines)
        kind, a, b = classify(prompt)
        if kind is None:
            continue
        data = last_structured_output(lines)
        if data is None:
            continue  # agent not finished (or died before output)
        mtime = os.path.getmtime(path)
        if kind == "expert":
            key, role = a, b
            if key not in experts or mtime > experts[key][3]:
                experts[key] = (role, data, agent_id, mtime)
        elif kind == "dedup":
            dedup = (data, agent_id)
        elif kind == "verify":
            verifies.append((b, a, data, agent_id, mtime))

    written = []
    for key, (role, data, agent_id, _) in sorted(experts.items()):
        out_path = os.path.join(ROOT, f"codebase-review-{key}.md")
        with open(out_path, "w") as fh:
            fh.write(render_expert_md(key, role, data, agent_id))
        written.append(f"{key}: {len(data.get('findings', []))} findings")

    if dedup is not None:
        data, agent_id = dedup
        out_path = os.path.join(ROOT, "codebase-review-dedup.md")
        clusters = data.get("clusters", [])
        body = [
            "# Codebase Review — Dedup Pass",
            "",
            f"> Workflow run `wf_53b36881-009`, agent `{agent_id}`.",
            "",
            f"{len(clusters)} duplicate clusters (indexes refer to the raw combined finding list, in expert order).",
            "",
            "```json",
            json.dumps(data, indent=2),
            "```",
            "",
        ]
        with open(out_path, "w") as fh:
            fh.write("\n".join(body))
        written.append(f"dedup: {len(clusters)} clusters")

    if verifies:
        # newest verdict per (title, lens)
        latest = {}
        for title, lens, data, agent_id, mtime in verifies:
            k = (title, lens)
            if k not in latest or mtime > latest[k][2]:
                latest[k] = (data, agent_id, mtime)
        by_title = {}
        for (title, lens), (data, agent_id, _) in latest.items():
            by_title.setdefault(title, []).append((lens, data, agent_id))
        body = [
            "# Codebase Review — Adversarial Verification Verdicts",
            "",
            "> Workflow run `wf_53b36881-009`. One section per finding; evidence/impact lens verdicts.",
            "",
            f"{len(latest)} verdicts across {len(by_title)} findings so far.",
            "",
        ]
        for title in sorted(by_title):
            body.append(f"## {title}")
            body.append("")
            for lens, data, agent_id in sorted(by_title[title]):
                body.append(f"- **Lens:** {lens}  |  **isReal:** {data.get('isReal')}  |  **Adjusted severity:** {data.get('adjustedSeverity')}  |  agent `{agent_id}`")
                body.append(f"  - {data.get('reason', '')}")
            body.append("")
        with open(os.path.join(ROOT, "codebase-review-verification.md"), "w") as fh:
            fh.write("\n".join(body))
        written.append(f"verification: {len(latest)} verdicts / {len(by_title)} findings")

    print("Wrote:" if written else "Nothing complete yet.")
    for w in written:
        print("  -", w)


if __name__ == "__main__":
    main()
