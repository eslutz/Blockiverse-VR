#!/usr/bin/env python3
"""Regression checks for generated Blockiverse M6 audio cues."""
import importlib.util
import math
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
GENERATOR_PATH = ROOT / "scripts" / "audio" / "generate-m6-audio.py"


def load_generator():
    spec = importlib.util.spec_from_file_location("generate_m6_audio", GENERATOR_PATH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class GeneratedM6AudioTests(unittest.TestCase):
    def setUp(self):
        self.generator = load_generator()

    def test_clip_set_stays_compatible_with_prefab_assignments(self):
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
        }

        for name, (minimum, maximum) in expected_duration_ranges.items():
            with self.subTest(name=name):
                samples = self.generator.CLIPS[name]
                duration = len(samples) / self.generator.SAMPLE_RATE
                peak = max(abs(sample) for sample in samples)
                rms = math.sqrt(sum(sample * sample for sample in samples) / len(samples))

                self.assertGreaterEqual(duration, minimum)
                self.assertLessEqual(duration, maximum)
                self.assertGreaterEqual(peak, 0.72)
                self.assertLessEqual(peak, 0.86)
                self.assertGreater(rms, 0.025)
                self.assertLess(abs(samples[0]), 0.002)
                self.assertLess(abs(samples[-1]), 0.002)
                self.assertTrue(all(math.isfinite(sample) for sample in samples))


if __name__ == "__main__":
    unittest.main()
