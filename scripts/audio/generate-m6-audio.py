#!/usr/bin/env python3
"""Generate original placeholder interaction/UI sound effects for M6.

These are synthesized from scratch (no sampled or third-party audio) so they are
safe to ship as temporary cues until authored sound design replaces them. Run from
the repository root: python3 scripts/audio/generate-m6-audio.py
"""
import hashlib
import math
import os
import random
import struct

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
AUDIO_DIR = "Assets/Blockiverse/Audio"
SAMPLE_RATE = 44100


def envelope(progress, attack=0.02, release=0.6):
    if progress < attack:
        return progress / attack
    decay = (progress - attack) / max(1e-6, 1.0 - attack)
    return max(0.0, 1.0 - decay) ** (1.0 / release)


def tone(frequency, t):
    return math.sin(2.0 * math.pi * frequency * t)


def block_break(duration=0.14):
    rng = random.Random(101)
    total = int(SAMPLE_RATE * duration)
    samples = []
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        noise = rng.uniform(-1.0, 1.0)
        body = tone(140.0, t) * 0.4
        amp = envelope(progress, attack=0.005, release=0.9)
        samples.append((noise * 0.6 + body) * amp * 0.7)
    return samples


def block_place(duration=0.12):
    total = int(SAMPLE_RATE * duration)
    samples = []
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        body = tone(190.0, t) * 0.7 + tone(95.0, t) * 0.3
        amp = envelope(progress, attack=0.004, release=0.8)
        samples.append(body * amp * 0.7)
    return samples


def ui_blip(frequency, duration=0.06):
    total = int(SAMPLE_RATE * duration)
    samples = []
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        amp = envelope(progress, attack=0.01, release=0.7)
        samples.append(tone(frequency, t) * amp * 0.5)
    return samples


def ui_sequence(frequencies, duration=0.14):
    total = int(SAMPLE_RATE * duration)
    step = total // len(frequencies)
    samples = []
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        index = min(len(frequencies) - 1, i // step)
        amp = envelope(progress, attack=0.01, release=0.6)
        samples.append(tone(frequencies[index], t) * amp * 0.5)
    return samples


CLIPS = {
    "block_break": block_break(),
    "block_place": block_place(),
    "ui_select": ui_blip(660.0),
    "ui_confirm": ui_sequence([523.25, 783.99]),
    "ui_cancel": ui_sequence([440.0, 329.63]),
}


def write_wav(path, samples):
    frames = bytearray()
    for sample in samples:
        clamped = max(-1.0, min(1.0, sample))
        frames += struct.pack("<h", int(clamped * 32767.0))

    data_size = len(frames)
    with open(path, "wb") as handle:
        handle.write(b"RIFF")
        handle.write(struct.pack("<I", 36 + data_size))
        handle.write(b"WAVE")
        handle.write(b"fmt ")
        handle.write(struct.pack("<IHHIIHH", 16, 1, 1, SAMPLE_RATE, SAMPLE_RATE * 2, 2, 16))
        handle.write(b"data")
        handle.write(struct.pack("<I", data_size))
        handle.write(frames)


def stable_guid(relative_path):
    return hashlib.md5(relative_path.encode("utf-8")).hexdigest()


def write_audio_meta(relative_path):
    guid = stable_guid(relative_path)
    meta = (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "AudioImporter:\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 6\n"
        "  defaultSettings:\n"
        "    serializedVersion: 2\n"
        "    loadType: 0\n"
        "    sampleRateSetting: 0\n"
        "    sampleRateOverride: 44100\n"
        "    compressionFormat: 1\n"
        "    quality: 1\n"
        "    conversionMode: 0\n"
        "    preloadAudioData: 1\n"
        "  platformSettingOverrides: {}\n"
        "  forceToMono: 1\n"
        "  normalize: 1\n"
        "  preloadAudioData: 1\n"
        "  loadInBackground: 0\n"
        "  ambisonic: 0\n"
        "  3D: 0\n"
        "  userData:\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    )
    with open(os.path.join(ROOT, relative_path + ".meta"), "w", newline="\n") as handle:
        handle.write(meta)


def main():
    os.makedirs(os.path.join(ROOT, AUDIO_DIR), exist_ok=True)
    for name, samples in CLIPS.items():
        relative_path = f"{AUDIO_DIR}/{name}.wav"
        write_wav(os.path.join(ROOT, relative_path), samples)
        write_audio_meta(relative_path)
        print(f"wrote {relative_path} ({len(samples)} samples)")


if __name__ == "__main__":
    main()
