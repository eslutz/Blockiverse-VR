#!/usr/bin/env python3
"""Regression checks for generated Blockiverse art assets."""
import importlib.util
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
GENERATOR_PATH = ROOT / "scripts" / "art" / "generate-art-assets.py"
MILESTONE_GENERATOR_PREFIX = "generate-" + "m"


def load_generator():
    spec = importlib.util.spec_from_file_location("generate_art_assets", GENERATOR_PATH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class GeneratedArtAssetTests(unittest.TestCase):
    def setUp(self):
        self.generator = load_generator()

    def test_generator_uses_reusable_name(self):
        self.assertTrue(GENERATOR_PATH.exists())
        milestone_generators = [
            path.name
            for path in (ROOT / "scripts" / "art").glob("generate-*.py")
            if path.name.startswith(MILESTONE_GENERATOR_PREFIX)
            and len(path.name) > len(MILESTONE_GENERATOR_PREFIX)
            and path.name[len(MILESTONE_GENERATOR_PREFIX)].isdigit()
        ]
        self.assertEqual(milestone_generators, [])

    def test_phase_14_manifest_covers_canonical_asset_sets(self):
        block_names = [block[0] for block in self.generator.BLOCKS]
        item_names = [item[0] for item in self.generator.ITEMS]
        ui_names = [sprite[0] for sprite in self.generator.UI_SPRITES]
        vfx_names = [sprite[0] for sprite in self.generator.VFX_SPRITES]

        self.assertEqual(len(block_names), 50)
        self.assertEqual(len(set(block_names)), len(block_names))
        self.assertIn("worldroot", block_names)
        self.assertIn("mend_bench", block_names)
        self.assertIn("staropal_geode", block_names)

        self.assertGreaterEqual(len(item_names), 55)
        self.assertIn("reedwood_delver", item_names)
        self.assertIn("field_bandage", item_names)

        self.assertIn("settings_panel", ui_names)
        self.assertIn("feedback_toast", ui_names)

        self.assertEqual(
            set(vfx_names),
            {
                "block_dust_particle",
                "block_puff_particle",
                "resource_spark_particle",
                "craft_spark_particle",
                "rain_splash_particle",
                "snowflake_particle",
                "fog_wisp_particle",
                "ember_particle",
            },
        )

    def test_generated_image_dimensions_match_phase_14_policy(self):
        tile = self.generator.make_block_tile(self.generator.BLOCKS[0])
        icon = self.generator.make_item_icon(self.generator.ITEMS[0])
        vfx = self.generator.make_vfx_sprite(self.generator.VFX_SPRITES[0])

        self.assertEqual((len(tile[0]), len(tile)), (16, 16))
        self.assertEqual((len(icon[0]), len(icon)), (64, 64))
        self.assertEqual((len(vfx[0]), len(vfx)), (32, 32))

        self.assertEqual(self.generator.ATLAS_COLUMNS, 8)
        self.assertEqual(self.generator.ATLAS_ROWS, 7)


if __name__ == "__main__":
    unittest.main()
