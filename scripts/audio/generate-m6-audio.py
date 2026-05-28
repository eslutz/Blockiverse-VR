#!/usr/bin/env python3
"""Generate original interaction/UI sound effects for M6.

These are synthesized from scratch (no sampled or third-party audio) so they are
safe to ship as original Blockiverse cues. Run from the repository root:
python3 scripts/audio/generate-m6-audio.py
"""
import hashlib
import math
import os
import random
import struct

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
AUDIO_DIR = "Assets/Blockiverse/Audio"
SAMPLE_RATE = 44100
TARGET_PEAK = 0.82


def envelope(progress, attack=0.02, release=0.6):
    if progress < attack:
        return progress / attack
    decay = (progress - attack) / max(1e-6, 1.0 - attack)
    return max(0.0, 1.0 - decay) ** (1.0 / release)


def smoothstep(value):
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)


def seconds_to_samples(duration):
    return int(SAMPLE_RATE * duration)


def tone(frequency, t):
    return math.sin(2.0 * math.pi * frequency * t)


def normalize(samples, target_peak=TARGET_PEAK):
    peak = max(abs(sample) for sample in samples)
    if peak <= 1e-9:
        return samples
    gain = target_peak / peak
    return [sample * gain for sample in samples]


def apply_edge_fades(samples, fade_in=0.004, fade_out=0.018):
    fade_in_samples = max(1, seconds_to_samples(fade_in))
    fade_out_samples = max(1, seconds_to_samples(fade_out))
    total = len(samples)
    faded = []
    for i, sample in enumerate(samples):
        gain = 1.0
        if i < fade_in_samples:
            gain *= smoothstep(i / fade_in_samples)
        remaining = total - 1 - i
        if remaining < fade_out_samples:
            gain *= smoothstep(remaining / fade_out_samples)
        faded.append(sample * gain)
    return faded


def finalize(samples):
    return normalize(apply_edge_fades(samples))


def filtered_noise(rng, state, cutoff_hz):
    raw = rng.uniform(-1.0, 1.0)
    rc = 1.0 / (2.0 * math.pi * cutoff_hz)
    dt = 1.0 / SAMPLE_RATE
    alpha = dt / (rc + dt)
    state += alpha * (raw - state)
    return raw, state


def gaussian(t, center, width):
    offset = (t - center) / max(width, 1e-6)
    return math.exp(-offset * offset)


def block_break(duration=0.28):
    rng = random.Random(101)
    total = seconds_to_samples(duration)
    samples = []
    dust_state = 0.0
    shard_centers = [0.026, 0.047, 0.074, 0.116, 0.171, 0.218]
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        raw_noise, dust_state = filtered_noise(rng, dust_state, 1750.0)
        grit = raw_noise - dust_state * 0.72
        impact = math.exp(-t * 26.0)
        crumble = max(0.0, 1.0 - progress) ** 1.65
        body = (
            tone(93.0 - progress * 18.0, t) * 0.55
            + tone(151.0, t) * 0.16
        ) * impact
        dust = (dust_state * 0.38 + grit * 0.24) * crumble
        shards = 0.0
        for index, center in enumerate(shard_centers):
            width = 0.0045 + index * 0.0009
            pitch = 520.0 + index * 91.0
            sign = -1.0 if index % 2 else 1.0
            shards += tone(pitch, t) * gaussian(t, center, width) * sign * 0.18
        samples.append(body + dust + shards)
    return finalize(samples)


def block_place(duration=0.2):
    rng = random.Random(202)
    total = seconds_to_samples(duration)
    samples = []
    noise_state = 0.0
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        raw_noise, noise_state = filtered_noise(rng, noise_state, 1100.0)
        attack = envelope(progress, attack=0.012, release=0.5)
        thump = (
            tone(105.0 + progress * 14.0, t) * 0.48
            + tone(205.0, t) * 0.19
        ) * math.exp(-t * 18.0)
        click = (raw_noise - noise_state) * gaussian(t, 0.012, 0.0045) * 0.26
        settle = (
            tone(312.0, t) * gaussian(t, 0.058, 0.026) * 0.17
            + tone(246.0, t) * gaussian(t, 0.096, 0.04) * 0.1
        )
        samples.append((thump + click + settle) * attack)
    return finalize(samples)


def ui_pluck(frequency, duration=0.1, accent=0.0):
    total = seconds_to_samples(duration)
    samples = []
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        amp = envelope(progress, attack=0.08, release=0.55)
        shimmer = tone(frequency * 2.01, t) * 0.18 + tone(frequency * 3.0, t) * 0.05
        bend = tone(frequency + accent * math.exp(-t * 24.0), t) * 0.72
        samples.append((bend + shimmer) * amp)
    return finalize(samples)


def ui_sequence(frequencies, duration=0.18):
    total = seconds_to_samples(duration)
    samples = []
    segment_duration = duration / len(frequencies)
    for i in range(total):
        t = i / SAMPLE_RATE
        sample = 0.0
        for index, frequency in enumerate(frequencies):
            start = index * segment_duration * 0.74
            local_t = t - start
            if local_t < 0.0:
                continue
            local_progress = local_t / segment_duration
            if local_progress > 1.25:
                continue
            amp = envelope(min(local_progress, 1.0), attack=0.12, release=0.5)
            tail = math.exp(-max(0.0, local_progress - 1.0) * 8.0)
            sample += (
                tone(frequency, t) * 0.66
                + tone(frequency * 2.0, t) * 0.16
                + tone(frequency * 2.5, t) * 0.04
            ) * amp * tail
        samples.append(sample)
    return finalize(samples)


def footstep(seed, duration=0.135, base_frequency=118.0):
    rng = random.Random(seed)
    total = seconds_to_samples(duration)
    samples = []
    noise_state = 0.0
    heel_time = 0.012
    toe_time = 0.068
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        raw_noise, noise_state = filtered_noise(rng, noise_state, 950.0)
        ground = max(0.0, 1.0 - progress) ** 1.25
        heel = (
            tone(base_frequency, t) * 0.42
            + tone(base_frequency * 1.78, t) * 0.12
        ) * gaussian(t, heel_time, 0.018)
        toe = (
            tone(base_frequency * 1.35, t) * 0.18
            + (raw_noise - noise_state * 0.45) * 0.16
        ) * gaussian(t, toe_time, 0.03)
        cloth = noise_state * 0.08 * ground
        samples.append(heel + toe + cloth)
    return finalize(samples)


def inventory_open(duration=0.18):
    total = seconds_to_samples(duration)
    samples = []
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        lift = smoothstep(progress)
        amp = envelope(progress, attack=0.05, release=0.6)
        sample = (
            tone(294.0 + 72.0 * lift, t) * 0.42
            + tone(588.0 + 120.0 * lift, t) * 0.16
            + tone(880.0, t) * gaussian(t, 0.052, 0.018) * 0.11
        ) * amp
        samples.append(sample)
    return finalize(samples)


def inventory_close(duration=0.16):
    total = seconds_to_samples(duration)
    samples = []
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        drop = 1.0 - smoothstep(progress)
        amp = envelope(progress, attack=0.04, release=0.48)
        sample = (
            tone(340.0 + 58.0 * drop, t) * 0.38
            + tone(210.0, t) * gaussian(t, 0.078, 0.028) * 0.24
        ) * amp
        samples.append(sample)
    return finalize(samples)


def craft_success(duration=0.24):
    total = seconds_to_samples(duration)
    samples = []
    notes = [392.0, 523.25, 659.25]
    segment = duration / len(notes)
    for i in range(total):
        t = i / SAMPLE_RATE
        sample = 0.0
        for index, frequency in enumerate(notes):
            start = index * segment * 0.62
            local_t = t - start
            if local_t < 0.0:
                continue
            local_progress = min(local_t / segment, 1.0)
            amp = envelope(local_progress, attack=0.09, release=0.56)
            sample += (
                tone(frequency, t) * 0.46
                + tone(frequency * 2.0, t) * 0.12
            ) * amp * math.exp(max(0.0, local_t - segment) * -7.5)
        samples.append(sample)
    return finalize(samples)


def craft_fail(duration=0.22):
    total = seconds_to_samples(duration)
    samples = []
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        amp = envelope(progress, attack=0.05, release=0.5)
        bend = 1.0 - smoothstep(progress)
        wobble = tone(12.0, t) * 5.0
        sample = (
            tone(262.0 + 52.0 * bend + wobble, t) * 0.42
            + tone(196.0, t) * gaussian(t, 0.105, 0.055) * 0.25
        ) * amp
        samples.append(sample)
    return finalize(samples)


CLIPS = {
    "block_break": block_break(),
    "block_place": block_place(),
    "ui_select": ui_pluck(659.25, accent=24.0),
    "ui_confirm": ui_sequence([523.25, 783.99]),
    "ui_cancel": ui_sequence([440.0, 329.63]),
    "footstep_01": footstep(303, base_frequency=112.0),
    "footstep_02": footstep(404, base_frequency=128.0),
    "inventory_open": inventory_open(),
    "inventory_close": inventory_close(),
    "craft_success": craft_success(),
    "craft_fail": craft_fail(),
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
