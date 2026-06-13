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

        self.assertEqual(len(block_names), 51)
        self.assertEqual(len(set(block_names)), len(block_names))
        self.assertIn("worldroot", block_names)
        self.assertIn("mend_bench", block_names)
        self.assertIn("staropal_geode", block_names)
        self.assertIn("bedroll", block_names)
        self.assertIn("fired_brick_block", block_names)
        self.assertNotIn("fired_brick", block_names)

        self.assertGreaterEqual(len(item_names), 55)
        self.assertIn("fired_brick", item_names)
        self.assertIn("reedwood_delver", item_names)
        self.assertIn("field_bandage", item_names)
        self.assertIn("bedroll", item_names)
        self.assertIn("berry_cluster", item_names)
        self.assertIn("grain_bundle", item_names)
        self.assertIn("reed_fiber", item_names)
        self.assertIn("raw_paletin", item_names)
        self.assertIn("raw_sunmetal", item_names)
        self.assertIn("spark_niter", item_names)
        self.assertIn("raw_umbralite", item_names)
        self.assertIn("staropal_shard", item_names)
        self.assertIn("raw_morsel", item_names)
        self.assertIn("berry_mash", item_names)
        self.assertIn("flatbread", item_names)
        self.assertIn("cooked_morsel", item_names)
        self.assertIn("trail_ration", item_names)
        self.assertNotIn("berrybush", item_names)
        self.assertNotIn("grain_stalk", item_names)
        self.assertNotIn("reedgrass", item_names)
        self.assertNotIn("niterstone", item_names)
        self.assertNotIn("paletin_thread", item_names)
        self.assertNotIn("sunmetal_fleck", item_names)
        self.assertNotIn("umbralite_node", item_names)
        self.assertNotIn("staropal_geode", item_names)

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
        self.assertEqual(self.generator.ATLAS_ROWS, 10)
        self.assertEqual(self.generator.TILE_PIXELS, 16)
        self.assertEqual(self.generator.ATLAS_TILE_PADDING_PIXELS, 4)
        self.assertEqual(self.generator.atlas_width(), 192)
        self.assertEqual(self.generator.atlas_height(), 240)
        self.assertLess(
            max(block[1] for block in self.generator.BLOCKS),
            self.generator.ATLAS_COLUMNS * self.generator.ATLAS_ROWS,
        )
        self.assertEqual(self.generator.atlas_max_texture_size(), 256)

    def test_resource_and_crop_blocks_use_shape_cues(self):
        patterns = {block[0]: block[4] for block in self.generator.BLOCKS}

        self.assertEqual(patterns["embercoal_seam"], "ore_bands")
        self.assertEqual(patterns["rosycopper_bloom"], "ore_diagonal")
        self.assertEqual(patterns["rustcore_ore"], "ore_cross")
        self.assertEqual(patterns["paletin_thread"], "ore_threads")
        self.assertEqual(patterns["berrybush"], "berries_cluster")
        self.assertEqual(patterns["grain_stalk"], "grain_heads")

    def test_generated_item_icon_importers_are_single_sprites(self):
        for name, *_ in self.generator.ITEMS:
            with self.subTest(name=name):
                meta_path = ROOT / "Assets" / "Blockiverse" / "Art" / "Textures" / "Items" / f"{name}.png.meta"
                self.assertTrue(meta_path.exists(), f"Missing item icon meta for {name}")
                meta = meta_path.read_text(encoding="utf-8")
                self.assertIn("  textureType: 8\n", meta)
                self.assertIn("  spriteMode: 1\n", meta)
                self.assertNotIn("  spriteMode: 2\n", meta)


if __name__ == "__main__":
    unittest.main()
