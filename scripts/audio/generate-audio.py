#!/usr/bin/env python3
"""Generate original interaction/UI sound effects for Blockiverse VR.

These are synthesized from scratch (no sampled or third-party audio) so they are
safe to ship as original Blockiverse cues. Run from the repository root:
python3 scripts/audio/generate-audio.py
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


def tool_hit_soft(duration=0.18):
    rng = random.Random(505)
    total = seconds_to_samples(duration)
    samples = []
    noise_state = 0.0
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        raw_noise, noise_state = filtered_noise(rng, noise_state, 720.0)
        thud = tone(122.0 - 16.0 * progress, t) * math.exp(-t * 22.0) * 0.46
        brush = noise_state * (1.0 - progress) ** 1.4 * 0.34
        leaf = (raw_noise - noise_state) * gaussian(t, 0.052, 0.028) * 0.12
        samples.append(thud + brush + leaf)
    return finalize(samples)


def tool_hit_stone(duration=0.2):
    rng = random.Random(606)
    total = seconds_to_samples(duration)
    samples = []
    noise_state = 0.0
    chip_centers = [0.018, 0.041, 0.083, 0.132]
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        raw_noise, noise_state = filtered_noise(rng, noise_state, 1900.0)
        body = (tone(156.0, t) * 0.36 + tone(276.0, t) * 0.15) * math.exp(-t * 20.0)
        grit = (raw_noise - noise_state * 0.55) * (1.0 - progress) ** 1.2 * 0.24
        chips = 0.0
        for index, center in enumerate(chip_centers):
            chips += tone(650.0 + index * 210.0, t) * gaussian(t, center, 0.0038) * 0.17
        samples.append(body + grit + chips)
    return finalize(samples)


def tool_wrong(duration=0.18):
    total = seconds_to_samples(duration)
    samples = []
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        drop = 1.0 - smoothstep(progress)
        buzz = tone(38.0, t) * 6.0
        sample = (
            tone(164.0 + 34.0 * drop + buzz, t) * 0.48
            + tone(91.0, t) * gaussian(t, 0.092, 0.055) * 0.25
        ) * envelope(progress, attack=0.04, release=0.45)
        samples.append(sample)
    return finalize(samples)


def pickup_item(duration=0.12):
    return ui_sequence([587.33, 783.99], duration=duration)


def container_open(duration=0.24):
    rng = random.Random(707)
    total = seconds_to_samples(duration)
    samples = []
    noise_state = 0.0
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        raw_noise, noise_state = filtered_noise(rng, noise_state, 650.0)
        creak = tone(98.0 + 55.0 * smoothstep(progress), t) * envelope(progress, attack=0.09, release=0.62) * 0.34
        latch = (raw_noise - noise_state) * gaussian(t, 0.034, 0.008) * 0.34
        wood = noise_state * gaussian(t, 0.15, 0.07) * 0.22
        samples.append(creak + latch + wood)
    return finalize(samples)


def container_close(duration=0.2):
    rng = random.Random(808)
    total = seconds_to_samples(duration)
    samples = []
    noise_state = 0.0
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        raw_noise, noise_state = filtered_noise(rng, noise_state, 780.0)
        knock = (tone(116.0, t) * 0.48 + tone(232.0, t) * 0.14) * gaussian(t, 0.046, 0.035)
        latch = (raw_noise - noise_state * 0.4) * gaussian(t, 0.118, 0.01) * 0.22
        tail = noise_state * (1.0 - progress) ** 1.6 * 0.14
        samples.append(knock + latch + tail)
    return finalize(samples)


def torch_ignite(duration=0.26):
    rng = random.Random(909)
    total = seconds_to_samples(duration)
    samples = []
    noise_state = 0.0
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        raw_noise, noise_state = filtered_noise(rng, noise_state, 2400.0)
        flare = (raw_noise - noise_state * 0.35) * gaussian(t, 0.052, 0.034) * 0.34
        bloom = (tone(330.0 + smoothstep(progress) * 120.0, t) * 0.2) * envelope(progress, attack=0.06, release=0.7)
        hiss = noise_state * (1.0 - progress) ** 0.8 * 0.16
        samples.append(flare + bloom + hiss)
    return finalize(samples)


def soft_fire_loop(seed, duration, cutoff, pulse_frequency):
    rng = random.Random(seed)
    total = seconds_to_samples(duration)
    samples = []
    state = 0.0
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        raw_noise, state = filtered_noise(rng, state, cutoff)
        pulse = 0.68 + 0.2 * tone(pulse_frequency, t) + 0.12 * tone(pulse_frequency * 1.7, t)
        loop_fade = smoothstep(min(progress * 8.0, 1.0)) * smoothstep(min((1.0 - progress) * 8.0, 1.0))
        samples.append((state * 0.42 + raw_noise * 0.08) * pulse * loop_fade)
    return finalize(samples)


def weather_loop(seed, duration, cutoff, wind_frequency, density):
    rng = random.Random(seed)
    total = seconds_to_samples(duration)
    samples = []
    state = 0.0
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        raw_noise, state = filtered_noise(rng, state, cutoff)
        gust = 0.62 + 0.22 * tone(wind_frequency, t) + 0.12 * tone(wind_frequency * 0.43, t)
        droplets = 0.0
        if density > 0.0 and rng.random() < density:
            droplets = raw_noise * rng.uniform(0.15, 0.42)
        loop_fade = smoothstep(min(progress * 8.0, 1.0)) * smoothstep(min((1.0 - progress) * 8.0, 1.0))
        samples.append((state * 0.36 * gust + droplets) * loop_fade)
    return finalize(samples)


def thunder(seed, duration, near):
    rng = random.Random(seed)
    total = seconds_to_samples(duration)
    samples = []
    state = 0.0
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        raw_noise, state = filtered_noise(rng, state, 120.0 if near else 80.0)
        crack = (raw_noise - state * 0.2) * gaussian(t, 0.075 if near else 0.19, 0.028 if near else 0.08)
        rumble = (tone(42.0, t) * 0.4 + state * 0.85) * math.exp(-progress * (2.6 if near else 1.7))
        samples.append(crack * (0.52 if near else 0.24) + rumble)
    return finalize(samples)


def ambience(seed, duration, base_frequency, shimmer_frequency, noise_cutoff):
    rng = random.Random(seed)
    total = seconds_to_samples(duration)
    samples = []
    state = 0.0
    for i in range(total):
        t = i / SAMPLE_RATE
        progress = i / total
        _, state = filtered_noise(rng, state, noise_cutoff)
        pad = tone(base_frequency, t) * 0.18 + tone(base_frequency * 1.5, t) * 0.08
        shimmer = tone(shimmer_frequency + 2.0 * tone(0.21, t), t) * 0.04
        loop_fade = smoothstep(min(progress * 8.0, 1.0)) * smoothstep(min((1.0 - progress) * 8.0, 1.0))
        samples.append((pad + shimmer + state * 0.28) * loop_fade)
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
    "tool_hit_soft": tool_hit_soft(),
    "tool_hit_stone": tool_hit_stone(),
    "tool_wrong": tool_wrong(),
    "pickup_item": pickup_item(),
    "container_open": container_open(),
    "container_close": container_close(),
    "torch_ignite": torch_ignite(),
    "torch_loop": soft_fire_loop(1001, 1.0, 1100.0, 7.0),
    "campfire_loop": soft_fire_loop(1002, 1.1, 900.0, 4.5),
    "rain_light_loop": weather_loop(1101, 1.2, 1300.0, 0.55, 0.006),
    "rain_heavy_loop": weather_loop(1102, 1.2, 1800.0, 0.72, 0.018),
    "thunder_near": thunder(1201, 0.82, near=True),
    "thunder_far": thunder(1202, 0.95, near=False),
    "snow_wind_loop": weather_loop(1301, 1.2, 520.0, 0.36, 0.001),
    "cave_ambience_loop": ambience(1401, 1.2, 64.0, 211.0, 130.0),
    "day_ambience_loop": ambience(1402, 1.2, 92.0, 392.0, 190.0),
    "night_ambience_loop": ambience(1403, 1.2, 72.0, 247.0, 150.0),
    "multiplayer_join": ui_sequence([392.0, 523.25, 659.25], duration=0.2),
    "multiplayer_leave": ui_sequence([659.25, 493.88, 329.63], duration=0.22),
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
