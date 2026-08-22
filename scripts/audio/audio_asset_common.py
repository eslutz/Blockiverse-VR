#!/usr/bin/env python3
"""Shared asset-writing rules for the two Blockiverse audio pipelines.

Blockiverse has two audio pipelines that must agree on how a clip lands in the
Unity project:

  generate-audio.py     synthesizes the original music bed and the classic block
                        cues kept behind the Classic Block Sounds setting.
  build-audio-assets.py processes licensed third-party source recordings into
                        the shipping sound-effect cues.

Both write into `Assets/Blockiverse/Audio`, so both must derive GUIDs the same
way and emit `.meta` files Unity reads identically. That logic lives here rather
than being duplicated, because a GUID disagreement between the two silently
breaks every serialized clip reference on the XR rig prefab.
"""
import hashlib
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
AUDIO_DIR = "Assets/Blockiverse/Audio"
SAMPLE_RATE = 44100
TARGET_PEAK = 0.82


def stable_guid(relative_path):
    """Unity GUID derived from the repo-relative asset path.

    Deterministic on purpose: regenerating an asset preserves every serialized
    reference to it. The corollary is that RENAMING a clip changes its GUID and
    orphans the prefab field pointing at it, so cue filenames are effectively
    part of the project's ABI.
    """
    return hashlib.md5(relative_path.encode("utf-8")).hexdigest()


def audio_meta_text(relative_path, streaming=False, force_mono=True, normalize=True):
    """Unity `.meta` for an audio asset.

    streaming   Beds (music, ambience, weather) stream from disk. A 30 s stereo
                PCM bed held in memory costs megabytes on Quest for something
                that plays one at a time.
    force_mono  On for spatial one-shots, which must be mono to pan correctly.
                Off for beds, which play non-spatially and lose their sense of
                space when folded down.
    normalize   On for one-shots, which the pipeline levels to a common peak.
                Off for beds: Unity's import normalize re-levels professionally
                mastered source and flattens the quiet-to-loud relationship that
                makes a bed sound like a place rather than a texture.
    """
    load_type = 2 if streaming else 0
    preload = 0 if streaming else 1
    return (
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(relative_path)}\n"
        "AudioImporter:\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 6\n"
        "  defaultSettings:\n"
        "    serializedVersion: 2\n"
        f"    loadType: {load_type}\n"
        "    sampleRateSetting: 0\n"
        "    sampleRateOverride: 44100\n"
        "    compressionFormat: 1\n"
        "    quality: 1\n"
        "    conversionMode: 0\n"
        f"    preloadAudioData: {preload}\n"
        "  platformSettingOverrides: {}\n"
        f"  forceToMono: {1 if force_mono else 0}\n"
        f"  normalize: {1 if normalize else 0}\n"
        f"  preloadAudioData: {preload}\n"
        "  loadInBackground: 0\n"
        "  ambisonic: 0\n"
        "  3D: 0\n"
        "  userData:\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    )


def write_audio_meta(relative_path, streaming=False, force_mono=True, normalize=True):
    text = audio_meta_text(relative_path, streaming, force_mono, normalize)
    with open(os.path.join(ROOT, relative_path + ".meta"), "w", newline="\n") as handle:
        handle.write(text)
