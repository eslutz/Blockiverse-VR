#!/usr/bin/env python3
"""Validate the curated block-texture pilot manifest.

This is intentionally stdlib-only so it can run in GitHub repository checks
without Unity or the raw texture staging directory. It guards the provenance and
state machine; build-curated-textures.py separately verifies adopted source files.
"""
from __future__ import annotations

import json
import unittest
from pathlib import Path
from urllib.parse import urlparse


HERE = Path(__file__).resolve().parent
MANIFEST_PATH = HERE / "curated-texture-manifest.json"
EXPECTED_SET_ID = "curated_v1"
EXPECTED_TILE_PIXELS = 32
EXPECTED_PILOT_IDS = {
    "meadow_turf",
    "loose_loam",
    "graystone",
    "branchwood_log",
    "leafmoss",
    "lumen_quartz_cluster",
    "embercoal_seam",
    "rosycopper_bloom",
    "rustcore_ore",
    "build_table",
    "glowwick",
    "storage_crate",
}
ALLOWED_STATUSES = {"researching", "candidate", "adopted", "rejected"}
ALLOWED_STRATEGIES = {"direct", "composite", "custom"}
ALLOWED_SOURCE_HOSTS = {"opengameart.org", "www.opengameart.org", "kenney.nl", "www.kenney.nl"}


def load_manifest() -> dict:
    with MANIFEST_PATH.open(encoding="utf-8") as handle:
        return json.load(handle)


def host_of(url: str) -> str:
    return (urlparse(url).hostname or "").lower()


class CuratedTextureManifestTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest = load_manifest()
        cls.licenses = cls.manifest["licenses"]
        cls.packs = cls.manifest["packs"]
        cls.pilot = cls.manifest["pilot"]

    def test_manifest_targets_curated_v1_at_project_resolution(self):
        self.assertEqual(EXPECTED_SET_ID, self.manifest["setId"])
        self.assertEqual(EXPECTED_TILE_PIXELS, self.manifest["tilePixels"])

    def test_manifest_contains_exactly_the_existing_twelve_texture_pilot_ids(self):
        ids = [entry["id"] for entry in self.pilot]
        self.assertEqual(len(ids), len(set(ids)), "pilot contains duplicate block IDs")
        self.assertEqual(EXPECTED_PILOT_IDS, set(ids))

    def test_every_pilot_entry_has_a_known_state_and_strategy(self):
        for entry in self.pilot:
            with self.subTest(texture=entry["id"]):
                self.assertIn(entry["status"], ALLOWED_STATUSES)
                self.assertIn(entry["strategy"], ALLOWED_STRATEGIES)
                self.assertTrue(entry.get("notes"), "decision rationale is required")

    def test_every_pack_has_a_shipping_safe_license_and_original_source_page(self):
        for pack_id, pack in self.packs.items():
            with self.subTest(pack=pack_id):
                license_id = pack.get("license")
                self.assertIn(license_id, self.licenses)
                terms = self.licenses[license_id]
                self.assertTrue(terms["commercialUse"])
                self.assertTrue(terms["modification"])
                self.assertTrue(terms["redistribution"])
                self.assertEqual("https", urlparse(pack["sourcePage"]).scheme)
                self.assertIn(host_of(pack["sourcePage"]), ALLOWED_SOURCE_HOSTS)
                self.assertTrue(pack.get("stagingPath"))

    def test_candidates_with_concrete_source_files_point_to_original_provider_pages(self):
        for entry in self.pilot:
            if not entry.get("sourceFile"):
                continue
            with self.subTest(texture=entry["id"]):
                self.assertIn(entry.get("pack"), self.packs)
                self.assertTrue(entry.get("sourcePage"))
                self.assertEqual("https", urlparse(entry["sourcePage"]).scheme)
                self.assertIn(host_of(entry["sourcePage"]), ALLOWED_SOURCE_HOSTS)

    def test_direct_candidates_declare_a_deterministic_32px_transform(self):
        for entry in self.pilot:
            if entry["status"] not in {"candidate", "adopted"} or entry["strategy"] != "direct":
                continue
            with self.subTest(texture=entry["id"]):
                self.assertTrue(entry.get("sourceFile"))
                transform = entry.get("transform") or {}
                self.assertIn(transform.get("crop"), {"none", "center-square"})
                self.assertIn(transform.get("scale"), {"lanczos", "neighbor"})
                self.assertEqual(EXPECTED_TILE_PIXELS, transform.get("size"))

    def test_adopted_assets_are_cc0_and_fully_buildable(self):
        for entry in self.pilot:
            if entry["status"] != "adopted":
                continue
            with self.subTest(texture=entry["id"]):
                self.assertNotEqual("custom", entry["strategy"])
                self.assertIn(entry.get("pack"), self.packs)
                self.assertEqual("cc0", self.packs[entry["pack"]]["license"])
                self.assertTrue(entry.get("sourceFile"))
                self.assertIsNotNone(entry.get("transform"))


if __name__ == "__main__":
    unittest.main()
