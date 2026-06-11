#!/usr/bin/env python3
"""Regression checks for generated Blockiverse audio cues."""
import importlib.util
import math
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
GENERATOR_PATH = ROOT / "scripts" / "audio" / "generate-audio.py"
MILESTONE_GENERATOR_PREFIX = "generate-" + "m"


def load_generator():
    spec = importlib.util.spec_from_file_location("generate_audio", GENERATOR_PATH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class GeneratedAudioTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # One shared render: the module synthesizes every clip (including the
        # multi-second music beds) at import time.
        cls.generator = load_generator()

    def test_generator_uses_reusable_name(self):
        self.assertTrue(GENERATOR_PATH.exists())
        milestone_generators = [
            path.name
            for path in (ROOT / "scripts" / "audio").glob("generate-*.py")
            if path.name.startswith(MILESTONE_GENERATOR_PREFIX)
            and len(path.name) > len(MILESTONE_GENERATOR_PREFIX)
            and path.name[len(MILESTONE_GENERATOR_PREFIX)].isdigit()
        ]
        self.assertEqual(milestone_generators, [])

    def test_clip_set_covers_phase_13_catalog(self):
        self.assertEqual(
            set(self.generator.CLIPS),
            {
                "block_break",
                "block_place",
                "ui_select",
                "ui_confirm",
                "ui_cancel",
                "footstep_01",
                "footstep_02",
                "inventory_open",
                "inventory_close",
                "craft_success",
                "craft_fail",
                "tool_hit_soft",
                "tool_hit_stone",
                "tool_wrong",
                "pickup_item",
                "container_open",
                "container_close",
                "torch_ignite",
                "torch_loop",
                "campfire_loop",
                "rain_light_loop",
                "rain_heavy_loop",
                "thunder_near",
                "thunder_far",
                "snow_wind_loop",
                "cave_ambience_loop",
                "day_ambience_loop",
                "night_ambience_loop",
                "multiplayer_join",
                "multiplayer_leave",
                "music_menu",
                "music_day",
                "music_night",
                "music_cave",
            },
        )

    def test_generated_clips_have_polished_duration_and_headroom(self):
        expected_duration_ranges = {
            "block_break": (0.22, 0.34),
            "block_place": (0.16, 0.26),
            "ui_select": (0.08, 0.14),
            "ui_confirm": (0.14, 0.24),
            "ui_cancel": (0.14, 0.24),
            "footstep_01": (0.10, 0.18),
            "footstep_02": (0.10, 0.18),
            "inventory_open": (0.14, 0.24),
            "inventory_close": (0.14, 0.24),
            "craft_success": (0.18, 0.30),
            "craft_fail": (0.18, 0.30),
            "tool_hit_soft": (0.12, 0.24),
            "tool_hit_stone": (0.12, 0.26),
            "tool_wrong": (0.12, 0.24),
            "pickup_item": (0.08, 0.16),
            "container_open": (0.16, 0.32),
            "container_close": (0.14, 0.28),
            "torch_ignite": (0.18, 0.34),
            "torch_loop": (0.75, 1.25),
            "campfire_loop": (0.85, 1.35),
            "rain_light_loop": (0.90, 1.50),
            "rain_heavy_loop": (0.90, 1.50),
            "thunder_near": (0.55, 1.10),
            "thunder_far": (0.55, 1.20),
            "snow_wind_loop": (0.90, 1.50),
            "cave_ambience_loop": (0.90, 1.50),
            "day_ambience_loop": (0.90, 1.50),
            "night_ambience_loop": (0.90, 1.50),
            "multiplayer_join": (0.14, 0.28),
            "multiplayer_leave": (0.14, 0.30),
            "music_menu": (30.0, 40.0),
            "music_day": (28.0, 40.0),
            "music_night": (30.0, 40.0),
            "music_cave": (30.0, 40.0),
        }

        for name, (minimum, maximum) in expected_duration_ranges.items():
            with self.subTest(name=name):
                samples = self.generator.CLIPS[name]
                duration = len(samples) / self.generator.SAMPLE_RATE
                peak = max(abs(sample) for sample in samples)
                rms = math.sqrt(sum(sample * sample for sample in samples) / len(samples))

                self.assertGreaterEqual(duration, minimum)
                self.assertLessEqual(duration, maximum)
                self.assertGreaterEqual(peak, 0.60)
                self.assertLessEqual(peak, 0.86)
                self.assertGreater(rms, 0.012)
                self.assertLess(abs(samples[0]), 0.002)
                self.assertLess(abs(samples[-1]), 0.002)
                self.assertTrue(all(math.isfinite(sample) for sample in samples))


if __name__ == "__main__":
    unittest.main()
