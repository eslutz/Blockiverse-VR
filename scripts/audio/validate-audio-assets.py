#!/usr/bin/env python3
"""Validate the audio actually committed under Assets/Blockiverse/Audio.

    python3 scripts/audio/validate-audio-assets.py

This checks the FILES, not the generator. That distinction matters: the older
`validate-generated-audio.py` inspects `generate-audio.py`'s in-memory output, so
it would happily pass while the shipped audio was something else entirely. Since
the sound effects now come from licensed source recordings rather than synthesis,
this is the check that guards what players hear.

Enforces, per `docs/rulesets/voxel_audio_vfx_ruleset.md` §7:
  - every manifest cue exists on disk, and every committed cue is accounted for
  - 44.1 kHz, 16-bit PCM
  - mono one-shots (so they spatialize), stereo beds where declared
  - one-shot peaks near the project target; nothing clipped or near-silent
  - loop beds are continuous across the wrap point
  - every cue traces to a source file and a license
  - `.meta` import settings match the cue's kind
"""
import json
import math
import os
import struct
import sys
import unittest
import wave

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
AUDIO_DIR = os.path.join(ROOT, "Assets/Blockiverse/Audio")
MANIFEST = os.path.join(HERE, "audio-manifest.json")

SAMPLE_RATE = 44100
TARGET_PEAK = 0.82
# One-shots are peak-normalized to TARGET_PEAK then offset by the cue's gain, so
# the band has to be wide enough for a deliberately quiet cue (footsteps sit a
# few dB down) while still catching a clipped or near-silent build.
PEAK_MIN, PEAK_MAX = 0.30, 0.999
BED_PEAK_MAX = 0.999
# Beds are levelled by loudness, not peak. Checking RMS spread rather than an
# absolute floor is what catches the real failure — one bed inaudible next to
# another — which a peak check misses entirely, since a single bird chirp can
# set a quiet bed's peak.
BED_RMS_SPREAD_DB = 14.0

# Files that are generated originals rather than processed third-party source.
GENERATED = {
    "music_menu": "streaming",
    "music_day": "streaming",
    "music_night": "streaming",
    "music_cave": "streaming",
    "classic_block_break": "oneshot",
    "classic_block_place": "oneshot",
}


def read_wav(path):
    with wave.open(path, "rb") as handle:
        channels = handle.getnchannels()
        width = handle.getsampwidth()
        rate = handle.getframerate()
        frames = handle.getnframes()
        raw = handle.readframes(frames)
    samples = struct.unpack(f"<{len(raw) // 2}h", raw)
    return channels, width, rate, frames, samples


def peak_of(samples):
    return max((abs(s) for s in samples), default=0) / 32767.0


def load_manifest():
    with open(MANIFEST) as handle:
        return json.load(handle)


class AudioAssetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = load_manifest()
        cls.cues = {c["cue"]: c for c in cls.manifest["cues"]}
        cls.wavs = sorted(
            os.path.splitext(f)[0] for f in os.listdir(AUDIO_DIR) if f.endswith(".wav")
        )
        cls.cache = {}
        for name in cls.wavs:
            cls.cache[name] = read_wav(os.path.join(AUDIO_DIR, f"{name}.wav"))

    def test_every_committed_wav_is_accounted_for(self):
        """No orphans: a cue with no manifest row has no traceable license."""
        known = set(self.cues) | set(GENERATED)
        orphans = sorted(set(self.wavs) - known)
        self.assertEqual([], orphans,
                         f"committed audio with no manifest row or generator entry: {orphans}")

    def test_every_manifest_cue_exists(self):
        missing = sorted(set(self.cues) - set(self.wavs))
        self.assertEqual([], missing, f"manifest cues with no committed file: {missing}")

    def test_every_generated_clip_exists(self):
        missing = sorted(set(GENERATED) - set(self.wavs))
        self.assertEqual([], missing, f"generated clips missing: {missing}")

    def test_format_is_uniform(self):
        for name in self.wavs:
            channels, width, rate, frames, _ = self.cache[name]
            with self.subTest(clip=name):
                self.assertEqual(SAMPLE_RATE, rate, f"{name}: sample rate {rate}")
                self.assertEqual(2, width, f"{name}: {width * 8}-bit, expected 16")
                self.assertGreater(frames, 0, f"{name}: empty")
                self.assertIn(channels, (1, 2), f"{name}: {channels} channels")

    def test_channel_layout_matches_cue_kind(self):
        """One-shots must be mono or they will not pan on a spatial source."""
        for name, entry in self.cues.items():
            channels = self.cache[name][0]
            expected = entry.get("channels", 1 if entry["kind"] == "oneshot" else 2)
            with self.subTest(clip=name):
                self.assertEqual(expected, channels,
                                 f"{name}: {channels}ch, manifest declares {expected}ch")

    def test_oneshot_levels(self):
        for name, entry in self.cues.items():
            if entry["kind"] != "oneshot":
                continue
            peak = peak_of(self.cache[name][4])
            with self.subTest(clip=name):
                self.assertGreaterEqual(peak, PEAK_MIN, f"{name}: peak {peak:.3f} too quiet")
                self.assertLessEqual(peak, PEAK_MAX, f"{name}: peak {peak:.3f} clipping")

    def test_beds_share_a_consistent_level(self):
        """Beds must sit within a few dB of each other.

        Before the build levelled them by integrated loudness, these sources
        spanned 29 dB peak-to-peak: the day ambience was effectively inaudible
        while the night bed dominated. RMS is the stdlib-only stand-in for the
        loudness measurement the build does with ffmpeg.
        """
        levels = {}
        for name, entry in self.cues.items():
            if entry["kind"] != "bed":
                continue
            samples = self.cache[name][4]
            peak = peak_of(samples)
            self.assertLessEqual(peak, BED_PEAK_MAX, f"{name}: peak {peak:.3f} clipping")
            rms = (sum((s / 32767.0) ** 2 for s in samples) / len(samples)) ** 0.5
            self.assertGreater(rms, 0.0, f"{name}: silent")
            levels[name] = 20.0 * math.log10(rms)

        self.assertTrue(levels, "no beds found")
        loudest = max(levels, key=levels.get)
        quietest = min(levels, key=levels.get)
        spread = levels[loudest] - levels[quietest]
        self.assertLessEqual(
            spread, BED_RMS_SPREAD_DB,
            f"bed levels span {spread:.1f} dB — {loudest} at {levels[loudest]:.1f} dB vs "
            f"{quietest} at {levels[quietest]:.1f} dB",
        )

    def test_beds_are_continuous_across_the_loop_point(self):
        """A bed wraps end-to-start every cycle; a step there ticks audibly.

        The builder crossfades the tail over the head, so the two ends should sit
        at a similar level rather than at zero.
        """
        for name, entry in self.cues.items():
            if entry["kind"] != "bed":
                continue
            _, _, _, _, samples = self.cache[name]
            window = min(len(samples) // 20, SAMPLE_RATE // 4) or 1
            head = sum(abs(s) for s in samples[:window]) / window / 32767.0
            tail = sum(abs(s) for s in samples[-window:]) / window / 32767.0
            with self.subTest(clip=name):
                louder = max(head, tail, 1e-6)
                self.assertLess(abs(head - tail) / louder, 0.75,
                                f"{name}: loop ends mismatch (head {head:.4f} vs tail {tail:.4f})")

    def test_oneshots_start_and_end_quietly(self):
        """Edge fades keep a trimmed one-shot from clicking."""
        for name, entry in self.cues.items():
            if entry["kind"] != "oneshot":
                continue
            samples = self.cache[name][4]
            with self.subTest(clip=name):
                self.assertLess(abs(samples[0]) / 32767.0, 0.02, f"{name}: starts on a click")
                self.assertLess(abs(samples[-1]) / 32767.0, 0.02, f"{name}: ends on a click")

    def test_every_cue_has_a_source_and_license(self):
        licenses = self.manifest["licenses"]
        packs = self.manifest["packs"]
        for name, entry in self.cues.items():
            with self.subTest(clip=name):
                self.assertTrue(entry.get("source"), f"{name}: no source file recorded")
                self.assertIn(entry.get("pack"), packs, f"{name}: unknown pack")
                self.assertIn(entry.get("license"), licenses, f"{name}: unknown license")

    def test_licenses_permit_shipping(self):
        for key, terms in self.manifest["licenses"].items():
            with self.subTest(license=key):
                self.assertTrue(terms["commercial_use"], f"{key}: commercial use not permitted")
                self.assertTrue(terms["modification"], f"{key}: modification not permitted")

    def test_meta_files_match_cue_kind(self):
        for name in self.wavs:
            meta_path = os.path.join(AUDIO_DIR, f"{name}.wav.meta")
            with self.subTest(clip=name):
                self.assertTrue(os.path.exists(meta_path), f"{name}: no .meta")
                with open(meta_path) as handle:
                    meta = handle.read()
                entry = self.cues.get(name)
                streaming = (entry or {}).get("kind") == "bed" or GENERATED.get(name) == "streaming"
                self.assertIn(f"loadType: {2 if streaming else 0}", meta,
                              f"{name}: wrong loadType for a {'bed' if streaming else 'one-shot'}")
                if entry and entry["kind"] == "bed":
                    # Beds keep source mastering and stereo width; Unity's import
                    # normalize and mono fold would undo both.
                    self.assertIn("normalize: 0", meta, f"{name}: bed must not be re-normalized")
                    self.assertIn("forceToMono: 0", meta, f"{name}: bed must not be folded to mono")


if __name__ == "__main__":
    if not os.path.isdir(AUDIO_DIR):
        print(f"missing {AUDIO_DIR}", file=sys.stderr)
        sys.exit(1)
    unittest.main(verbosity=2)
