#!/usr/bin/env python3
"""Build Blockiverse VR's shipping sound effects from licensed source recordings.

    python3 scripts/audio/build-audio-assets.py            # build everything
    python3 scripts/audio/build-audio-assets.py --check    # verify sources only
    python3 scripts/audio/build-audio-assets.py block_break footstep_soil_01

Reads `scripts/audio/audio-manifest.json`, which is the single source of truth
for what every cue is made of: source pack, source file, in-point, length, gain,
and license. Raw source bundles live OUTSIDE the repository (see SOURCES.md in
the staging directory) and are never committed — only the processed cues are.

Deterministic and idempotent: same sources plus same manifest gives byte-identical
output, and `.meta` GUIDs are derived from the asset path so rebuilding never
breaks a serialized prefab reference.

Requires ffmpeg. No third-party Python packages, matching generate-audio.py.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from audio_asset_common import (  # noqa: E402
    ROOT,
    AUDIO_DIR,
    SAMPLE_RATE,
    TARGET_PEAK,
    write_audio_meta,
)

MANIFEST_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "audio-manifest.json")
STAGING_DIR_NAME = "Blockiverse-VR-audio-staging"


def default_staging():
    """Locate the staging directory that holds the extracted source packs.

    Staging lives beside the repository, never inside it. `ROOT/..` is wrong when
    the build runs from a git worktree (which nests several levels under
    `.claude/worktrees/`), so walk up until the sibling directory turns up.
    """
    override = os.environ.get("BLOCKIVERSE_AUDIO_STAGING")
    if override:
        return os.path.abspath(override)

    current = ROOT
    for _ in range(6):
        candidate = os.path.join(os.path.dirname(current), STAGING_DIR_NAME)
        if os.path.isdir(candidate):
            return candidate
        parent = os.path.dirname(current)
        if parent == current:
            break
        current = parent
    return os.path.abspath(os.path.join(ROOT, "..", STAGING_DIR_NAME))


DEFAULT_STAGING = default_staging()

# Peak in dBFS that TARGET_PEAK (0.82) corresponds to, ~-1.72 dBFS.
TARGET_PEAK_DB = 20.0 * __import__("math").log10(TARGET_PEAK)

# Beds are levelled to a common integrated loudness so ambience, weather, and
# fire sit at comparable perceived volume before the mixer's category sliders
# touch them. -23 LUFS is the EBU R128 reference; the exact value matters less
# than every bed sharing it. The ceiling keeps a loud transient inside the bed
# (a thunder crack, a log settling) from clipping after the gain is applied.
TARGET_BED_LUFS = -23.0
BED_CEILING_DB = -1.5


def run(cmd):
    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"command failed: {' '.join(cmd[:3])}...\n{proc.stderr[-2000:]}")
    return proc


def measure_peak_db(path):
    """Max sample peak in dBFS, via ffmpeg's volumedetect."""
    proc = subprocess.run(
        ["ffmpeg", "-hide_banner", "-i", path, "-af", "volumedetect", "-f", "null", "-"],
        capture_output=True, text=True,
    )
    match = re.search(r"max_volume:\s*(-?\d+(?:\.\d+)?) dB", proc.stderr)
    if not match:
        raise RuntimeError(f"could not measure peak for {path}")
    return float(match.group(1))


def measure_loudness_lufs(path):
    """Integrated loudness (EBU R128) in LUFS.

    Beds are levelled by loudness rather than by peak. Peak-normalizing ambience
    is meaningless — one distant bird chirp sets the peak and the bed underneath
    it lands wherever it happens to land. Measured across these sources that gave
    a 29 dB spread between the day and night beds, which in the headset means one
    is inaudible while the other dominates.
    """
    proc = subprocess.run(
        ["ffmpeg", "-hide_banner", "-i", path, "-af", "ebur128", "-f", "null", "-"],
        capture_output=True, text=True,
    )
    # The trailing Summary block holds the integrated figure.
    matches = re.findall(r"^\s*I:\s*(-?\d+(?:\.\d+)?)\s*LUFS", proc.stderr, re.MULTILINE)
    if not matches:
        raise RuntimeError(f"could not measure loudness for {path}")
    return float(matches[-1])


def probe_duration(path):
    proc = run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
                "-of", "csv=p=0", path])
    return float(proc.stdout.strip())


def pitch_filter(semitones):
    """Shift pitch by `semitones` while preserving duration.

    Resampling moves pitch and speed together; the compensating atempo puts the
    speed back. atempo only accepts 0.5-2.0 per instance, so large shifts are
    split across stages.

    The leading aresample is essential, not tidiness: `asetrate` sets an ABSOLUTE
    rate, so applying it to a 96 kHz or 192 kHz Sonniss master while computing the
    target from 44.1 kHz stretches the clip by input_rate/44100 — a -2 semitone
    shift on 192 kHz source came out 4.36x too long before this was pinned.
    """
    if not semitones:
        return []
    ratio = 2.0 ** (semitones / 12.0)
    stages, remaining = [], 1.0 / ratio
    while remaining > 2.0:
        stages.append("atempo=2.0")
        remaining /= 2.0
    while remaining < 0.5:
        stages.append("atempo=0.5")
        remaining /= 0.5
    stages.append(f"atempo={remaining:.6f}")
    return [f"aresample={SAMPLE_RATE}",
            f"asetrate={int(SAMPLE_RATE * ratio)}",
            f"aresample={SAMPLE_RATE}"] + stages


def build_oneshot(source, dest, start, duration, channels, gain_db, fade_in, fade_out,
                  semitones=0):
    """Trim a one-shot, level it to the project peak target, write 16-bit PCM.

    One-shots are folded to mono because they play through spatialized sources,
    where a stereo clip would not pan. Short edge fades keep the trim from
    clicking; the generator applies the same treatment to synthesized cues.
    """
    # Pass one does everything that changes the waveform's shape: pitch, then an
    # explicit resample to the final rate, and only then the edge fades.
    #
    # The resample must precede the fades. Applied at the source rate instead, the
    # rate conversion afterwards rings at the boundary and lifts the first sample
    # back off zero — reintroducing exactly the click the fade exists to prevent.
    filters = pitch_filter(semitones) + [f"aresample={SAMPLE_RATE}"]
    if fade_in > 0:
        filters.append(f"afade=t=in:st=0:d={fade_in:.4f}")
    if fade_out > 0:
        filters.append(f"afade=t=out:st={max(duration - fade_out, 0):.4f}:d={fade_out:.4f}")

    with tempfile.TemporaryDirectory() as tmp:
        staged = os.path.join(tmp, "staged.wav")
        run(["ffmpeg", "-hide_banner", "-y", "-ss", f"{start}", "-t", f"{duration}",
             "-i", source, "-af", ",".join(filters), "-ac", str(channels),
             "-ar", str(SAMPLE_RATE), "-c:a", "pcm_s16le", staged])

        # Pass two is pure gain, and it comes last for a reason. Measuring the peak
        # before the fade-in overstates it for percussive cues whose transient sits
        # in the first few milliseconds: the fade then eats the attack and the clip
        # lands far under target. Footsteps came out at 0.15 peak that way, against
        # an 0.82 target. Scaling a clip that already starts near zero cannot
        # reintroduce a click, so nothing is lost by deferring it.
        peak_db = measure_peak_db(staged)
        correction = TARGET_PEAK_DB - peak_db + gain_db
        run(["ffmpeg", "-hide_banner", "-y", "-i", staged,
             "-af", f"volume={correction:.4f}dB", "-ac", str(channels),
             "-ar", str(SAMPLE_RATE), "-c:a", "pcm_s16le", dest])


def build_bed(source, dest, start, duration, channels, gain_db, crossfade, semitones=0):
    """Trim a looping bed and crossfade its tail over its head.

    A bed that simply cuts at the loop point ticks audibly every time it wraps.
    Taking `crossfade` seconds of material from just past the loop end and
    mixing it over the opening — each side faded equal-power — makes the wrap
    continuous. The result is exactly `duration` long.

    Beds keep their source channel layout and are NOT peak-normalized: ambience
    lives on its quiet-to-loud relationship, and flattening that is what makes a
    weather loop sound like a texture instead of a place.
    """
    body = max(duration - crossfade, 0.01)
    tail_filters = pitch_filter(semitones)
    gain = ("," + ",".join(tail_filters)) if tail_filters else ""
    filter_complex = (
        # head: the first `crossfade` seconds, fading in
        f"[0:a]atrim=start={start}:duration={crossfade},asetpts=N/SR/TB,"
        f"afade=t=in:st=0:d={crossfade}[head];"
        # tail: `crossfade` seconds taken from past the loop end, fading out
        f"[0:a]atrim=start={start + duration}:duration={crossfade},asetpts=N/SR/TB,"
        f"afade=t=out:st=0:d={crossfade}[tail];"
        # the two summed become the seamless opening
        f"[head][tail]amix=inputs=2:normalize=0[open];"
        # the rest of the loop, untouched
        f"[0:a]atrim=start={start + crossfade}:duration={body},asetpts=N/SR/TB[body];"
        f"[open][body]concat=n=2:v=0:a=1{gain}[out]"
    )
    with tempfile.TemporaryDirectory() as tmp:
        staged = os.path.join(tmp, "bed.wav")
        run(["ffmpeg", "-hide_banner", "-y", "-i", source,
             "-filter_complex", filter_complex, "-map", "[out]",
             "-ac", str(channels), "-ar", str(SAMPLE_RATE), "-c:a", "pcm_s16le", staged])

        # Level by integrated loudness, then apply the cue's artistic offset. This
        # is a single gain change, so the bed's internal dynamics survive intact —
        # which is the whole reason beds are not peak-normalized or run through
        # Unity's import normalize.
        loudness = measure_loudness_lufs(staged)
        correction = TARGET_BED_LUFS - loudness + gain_db
        # Never let the correction push the bed into the ceiling.
        headroom = BED_CEILING_DB - (measure_peak_db(staged) + correction)
        if headroom < 0:
            correction += headroom
            # Worth surfacing: a transient-heavy window (a log settling, a branch
            # dropped in the fire) drags the whole bed down to stay under the
            # ceiling, and it lands quieter than every other bed. Usually the fix
            # is a steadier in-point, not a different gain.
            print(f"  note: {os.path.basename(dest)} clamped {abs(headroom):.1f} dB for "
                  f"headroom — consider a steadier in-point", file=sys.stderr)
        run(["ffmpeg", "-hide_banner", "-y", "-i", staged,
             "-af", f"volume={correction:.4f}dB", "-ac", str(channels),
             "-ar", str(SAMPLE_RATE), "-c:a", "pcm_s16le", dest])


def resolve_source(entry, staging):
    path = os.path.join(staging, entry["source"])
    return path if os.path.exists(path) else None


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("cues", nargs="*", help="Build only these cues (default: all).")
    parser.add_argument("--staging", default=DEFAULT_STAGING,
                        help="Directory holding the extracted source packs.")
    parser.add_argument("--check", action="store_true",
                        help="Verify every source file resolves, then exit.")
    args = parser.parse_args()

    if not shutil.which("ffmpeg") or not shutil.which("ffprobe"):
        parser.error("ffmpeg and ffprobe are required (brew install ffmpeg).")

    with open(MANIFEST_PATH) as handle:
        manifest = json.load(handle)

    entries = manifest["cues"]
    if args.cues:
        wanted = set(args.cues)
        entries = [e for e in entries if e["cue"] in wanted]
        missing = wanted - {e["cue"] for e in entries}
        if missing:
            parser.error(f"unknown cue(s): {', '.join(sorted(missing))}")

    # Fail before writing anything rather than leaving a half-built audio folder.
    unresolved = [e for e in entries if resolve_source(e, args.staging) is None]
    if unresolved:
        print(f"ERROR: {len(unresolved)} source file(s) missing under {args.staging}:",
              file=sys.stderr)
        for entry in unresolved:
            print(f"  {entry['cue']:32s} <- {entry['source']}", file=sys.stderr)
        return 1

    if args.check:
        print(f"OK: all {len(entries)} source files resolve under {args.staging}")
        return 0

    audio_dir = os.path.join(ROOT, AUDIO_DIR)
    os.makedirs(audio_dir, exist_ok=True)

    for entry in entries:
        source = resolve_source(entry, args.staging)
        relative_path = f"{AUDIO_DIR}/{entry['cue']}.wav"
        dest = os.path.join(ROOT, relative_path)
        kind = entry.get("kind", "oneshot")
        channels = entry.get("channels", 1 if kind == "oneshot" else 2)
        gain_db = entry.get("gain_db", 0)

        semitones = entry.get("semitones", 0)
        if kind == "bed":
            build_bed(source, dest, entry["start"], entry["duration"], channels,
                      gain_db, entry.get("crossfade", 1.5), semitones)
        else:
            build_oneshot(source, dest, entry["start"], entry["duration"], channels,
                          gain_db, entry.get("fade_in", 0.004), entry.get("fade_out", 0.018),
                          semitones)

        write_audio_meta(
            relative_path,
            streaming=(kind == "bed"),
            force_mono=(kind == "oneshot"),
            normalize=(kind == "oneshot"),
        )
        print(f"wrote {relative_path}  ({probe_duration(dest):.3f}s, {channels}ch, {kind})")

    print(f"\nbuilt {len(entries)} cue(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
