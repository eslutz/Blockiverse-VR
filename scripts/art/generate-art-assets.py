#!/usr/bin/env python3
import math
import os
import struct
import zlib


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BLOCK_SOURCE_DIR = "Assets/Blockiverse/Art/Textures/Blocks/Source"
BLOCK_TEXTURE_SET_ROOT = "Assets/Blockiverse/Art/Textures/Blocks/TextureSets"
BLOCK_TEXTURE_SET_IDS = ("original", "enhanced", "ai_simplified", "ai")
ITEM_DIR = "Assets/Blockiverse/Art/Textures/Items"
UI_DIR = "Assets/Blockiverse/Art/Sprites/UI"
VFX_DIR = "Assets/Blockiverse/Art/Sprites/VFX"
ATLAS_COLUMNS = 8
ATLAS_ROWS = 10
TILE_PIXELS = 32
ATLAS_TILE_PADDING_PIXELS = 8
ATLAS_TILE_STRIDE_PIXELS = TILE_PIXELS + ATLAS_TILE_PADDING_PIXELS * 2


BLOCKS = [
    ("meadow_turf", 0, (57, 134, 68), (151, 211, 83), "mottled", 11),
    ("loose_loam", 1, (105, 69, 44), (166, 111, 67), "grain", 23),
    ("graystone", 2, (91, 100, 112), (157, 167, 178), "strata", 37),
    ("branchwood_log", 3, (139, 90, 43), (215, 151, 76), "rings", 41),
    ("leafmoss", 4, (48, 125, 57), (106, 199, 74), "leaves", 53),
    ("lumen_quartz_cluster", 5, (72, 176, 208), (203, 242, 250), "crystal", 67),
    ("embercoal_seam", 6, (29, 30, 34), (86, 87, 98), "ore_bands", 71),
    ("rosycopper_bloom", 7, (92, 83, 74), (234, 125, 62), "ore_diagonal", 83),
    ("rustcore_ore", 8, (82, 87, 90), (215, 203, 176), "ore_cross", 97),
    ("build_table", 9, (144, 94, 48), (231, 183, 101), "grid", 101),
    ("glowwick", 10, (61, 82, 49), (255, 191, 51), "glow", 109),
    ("storage_crate", 11, (116, 77, 41), (205, 144, 70), "crate", 127),
    ("worldroot", 12, (32, 35, 42), (89, 94, 105), "deep", 131),
    ("deepmantle", 13, (43, 43, 52), (118, 116, 130), "strata", 137),
    ("dark_slate", 14, (50, 58, 72), (117, 129, 146), "strata", 139),
    ("warm_granite", 15, (118, 91, 76), (194, 151, 122), "speckles", 149),
    ("white_limestone", 16, (169, 169, 143), (229, 228, 195), "sediment", 151),
    ("black_basalt", 17, (34, 35, 39), (92, 86, 83), "column", 157),
    ("dry_turf", 18, (136, 112, 47), (215, 174, 71), "mottled", 163),
    ("snowcap_turf", 19, (98, 134, 124), (231, 242, 240), "snow", 167),
    ("rootsoil", 20, (72, 53, 37), (138, 93, 54), "roots", 173),
    ("claybed", 21, (133, 87, 66), (198, 130, 94), "grain", 179),
    ("river_silt", 22, (94, 91, 68), (156, 153, 109), "grain", 181),
    ("pale_sand", 23, (185, 169, 113), (238, 221, 157), "grain", 191),
    ("shingle_gravel", 24, (91, 88, 84), (158, 154, 147), "pebbles", 193),
    ("snowpack", 25, (177, 211, 221), (246, 251, 255), "snow", 197),
    ("frostglass", 26, (78, 154, 184), (183, 232, 246), "glass", 199),
    ("thornbrush", 27, (91, 76, 42), (169, 112, 57), "thorn", 211),
    ("reedgrass", 28, (84, 128, 64), (176, 190, 85), "reeds", 223),
    ("work_plank", 29, (132, 82, 42), (212, 145, 70), "planks", 227),
    ("cutstone_block", 30, (100, 106, 112), (175, 181, 187), "brick", 229),
    ("fired_brick_block", 31, (128, 58, 43), (208, 102, 65), "brick", 233),
    ("clearpane_glass", 32, (105, 188, 214), (220, 250, 255), "glass", 239),
    ("surface_pebbles", 33, (92, 87, 78), (181, 174, 151), "pebbles", 241),
    ("flinty_shingle", 34, (60, 67, 73), (187, 192, 186), "chips", 251),
    ("paletin_thread", 35, (79, 84, 89), (213, 196, 155), "ore_threads", 257),
    ("sunmetal_fleck", 36, (127, 81, 43), (255, 196, 74), "sparkle", 263),
    ("niterstone_pocket", 37, (95, 90, 82), (220, 214, 184), "speckles", 269),
    ("brightsalt_crust", 38, (164, 174, 164), (246, 246, 225), "crust", 271),
    ("shellgrit_bed", 39, (156, 139, 110), (229, 205, 171), "chips", 277),
    ("resin_knot", 40, (110, 72, 39), (230, 143, 50), "rings", 281),
    ("berrybush", 41, (50, 112, 54), (203, 56, 91), "berries_cluster", 283),
    ("grain_stalk", 42, (119, 135, 54), (228, 196, 81), "grain_heads", 293),
    ("umbralite_node", 43, (37, 32, 59), (117, 81, 181), "crystal", 307),
    ("staropal_geode", 44, (45, 42, 70), (227, 196, 255), "crystal", 311),
    ("campfire", 45, (112, 68, 42), (255, 132, 43), "flame", 313),
    ("clay_kiln", 46, (120, 73, 56), (218, 124, 78), "kiln", 317),
    ("bellows_forge", 47, (66, 68, 72), (235, 116, 57), "forge", 331),
    ("prep_board", 48, (143, 92, 50), (227, 178, 102), "grid", 337),
    ("mend_bench", 49, (104, 74, 52), (196, 165, 94), "tools", 347),
    ("lumen_lamp", 50, (53, 73, 76), (245, 236, 146), "lamp", 349),
    ("spark_flare", 51, (83, 52, 38), (255, 231, 97), "flare", 351),
    ("tended_soil", 52, (86, 61, 41), (138, 102, 64), "tilled", 353),
    ("grain_stalk_s1", 53, (106, 125, 57), (207, 181, 73), "crop_sprout", 359),
    ("grain_stalk_s2", 54, (112, 133, 58), (219, 190, 78), "crop_mid", 367),
    ("berrybush_s1", 55, (45, 99, 49), (135, 178, 74), "bush_sprout", 373),
    ("berrybush_s2", 56, (48, 109, 52), (185, 54, 86), "bush_mid", 379),
    ("reedgrass_s1", 57, (74, 116, 62), (151, 174, 82), "reed_sprout", 383),
    ("sapling", 58, (93, 72, 44), (102, 175, 78), "sapling", 389),
    ("sapling_s1", 59, (95, 74, 45), (113, 184, 82), "sapling_mid", 397),
    ("sapling_s2", 60, (98, 77, 47), (122, 194, 88), "sapling_tall", 401),
    ("grain_stalk_s3", 61, (119, 138, 58), (232, 200, 82), "crop_full", 409),
    ("grain_stalk_s4", 62, (126, 143, 59), (244, 210, 89), "grain_heads", 419),
    ("berrybush_s3", 63, (51, 118, 56), (205, 56, 91), "berries_cluster", 421),
    ("berrybush_s4", 64, (52, 124, 58), (219, 63, 98), "berries_cluster", 431),
    ("berrybush_s5", 65, (54, 132, 60), (236, 70, 107), "berries_cluster", 433),
    ("reedgrass_s2", 66, (80, 126, 64), (168, 186, 86), "reeds", 439),
    ("reedgrass_s3", 67, (84, 132, 66), (184, 198, 92), "reeds", 443),
    ("smooth_branchwood", 68, (138, 88, 43), (214, 149, 75), "smooth_planks", 449),
    ("reed_basket", 69, (126, 93, 48), (214, 178, 93), "basket", 457),
    ("tool_rack", 70, (96, 69, 47), (207, 185, 123), "rack", 461),
    ("pantry_jar", 71, (119, 81, 62), (224, 169, 111), "jar", 463),
    ("deep_locker", 72, (38, 41, 49), (124, 132, 148), "locker", 467),
    ("freshwater", 73, (38, 126, 182), (133, 218, 248), "fluid_water", 479),
    ("brine", 74, (34, 111, 151), (164, 226, 226), "fluid_brine", 487),
    ("emberflow", 75, (91, 38, 28), (255, 116, 44), "fluid_ember", 491),
    ("bedroll", 76, (70, 88, 98), (219, 92, 83), "bedroll", 499),
]


BLOCK_SOURCE_ALIASES = [
    ("freshwater_flow", "freshwater", (44, 144, 201), (154, 230, 255), "fluid_water", 503),
    ("brine_flow", "brine", (40, 128, 169), (184, 235, 232), "fluid_brine", 509),
    ("emberflow_flow", "emberflow", (110, 42, 29), (255, 142, 55), "fluid_ember", 521),
]


ITEMS = [
    ("meadow_turf", (57, 134, 68), (151, 211, 83), "block"),
    ("dry_turf", (136, 112, 47), (215, 174, 71), "block"),
    ("snowcap_turf", (98, 134, 124), (231, 242, 240), "block"),
    ("loose_loam", (105, 69, 44), (166, 111, 67), "block"),
    ("rootsoil", (72, 53, 37), (138, 93, 54), "block"),
    ("claybed", (133, 87, 66), (198, 130, 94), "block"),
    ("river_silt", (94, 91, 68), (156, 153, 109), "block"),
    ("pale_sand", (185, 169, 113), (238, 221, 157), "block"),
    ("shingle_gravel", (91, 88, 84), (158, 154, 147), "block"),
    ("graystone", (91, 100, 112), (157, 167, 178), "block"),
    ("dark_slate", (50, 58, 72), (117, 129, 146), "block"),
    ("warm_granite", (118, 91, 76), (194, 151, 122), "block"),
    ("white_limestone", (169, 169, 143), (229, 228, 195), "block"),
    ("black_basalt", (34, 35, 39), (92, 86, 83), "block"),
    ("branchwood_log", (139, 90, 43), (215, 151, 76), "log"),
    ("leafmoss", (48, 125, 57), (106, 199, 74), "leaf"),
    ("thornbrush", (91, 76, 42), (169, 112, 57), "plant"),
    ("work_plank", (132, 82, 42), (212, 145, 70), "block"),
    ("cutstone_block", (100, 106, 112), (175, 181, 187), "block"),
    ("fired_brick", (128, 58, 43), (208, 102, 65), "block"),
    ("clearpane_glass", (105, 188, 214), (220, 250, 255), "shard"),
    ("build_table", (144, 94, 48), (231, 183, 101), "kit"),
    ("glowwick", (61, 82, 49), (255, 191, 51), "torch"),
    ("storage_crate", (116, 77, 41), (205, 144, 70), "kit"),
    ("campfire", (112, 68, 42), (255, 132, 43), "torch"),
    ("clay_kiln", (120, 73, 56), (218, 124, 78), "kit"),
    ("bellows_forge", (66, 68, 72), (235, 116, 57), "kit"),
    ("prep_board", (143, 92, 50), (227, 178, 102), "kit"),
    ("mend_bench", (104, 74, 52), (196, 165, 94), "kit"),
    ("bedroll", (70, 88, 98), (219, 92, 83), "kit"),
    ("surface_pebbles", (92, 87, 78), (181, 174, 151), "nugget"),
    ("flinty_shingle", (60, 67, 73), (187, 192, 186), "shard"),
    ("embercoal", (29, 30, 34), (86, 87, 98), "nugget"),
    ("raw_rosycopper", (118, 76, 48), (241, 135, 70), "nugget"),
    ("raw_paletin", (79, 84, 89), (213, 196, 155), "thread"),
    ("raw_rustcore", (89, 91, 94), (223, 216, 194), "nugget"),
    ("raw_sunmetal", (127, 81, 43), (255, 196, 74), "nugget"),
    ("lumen_crystal", (72, 176, 208), (203, 242, 250), "shard"),
    ("spark_niter", (95, 90, 82), (220, 214, 184), "nugget"),
    ("brightsalt", (164, 174, 164), (246, 246, 225), "nugget"),
    ("shellgrit", (156, 139, 110), (229, 205, 171), "nugget"),
    ("resin_knot", (110, 72, 39), (230, 143, 50), "drop"),
    ("berry_cluster", (50, 112, 54), (203, 56, 91), "berry"),
    ("grain_bundle", (119, 135, 54), (228, 196, 81), "grain"),
    ("reed_fiber", (84, 128, 64), (176, 190, 85), "thread"),
    ("raw_morsel", (126, 63, 58), (226, 129, 112), "drop"),
    ("berry_mash", (88, 42, 83), (210, 67, 129), "berry"),
    ("flatbread", (164, 122, 62), (238, 196, 117), "grain"),
    ("cooked_morsel", (99, 54, 40), (218, 126, 70), "drop"),
    ("trail_ration", (116, 83, 48), (228, 176, 94), "kit"),
    ("raw_umbralite", (37, 32, 59), (117, 81, 181), "shard"),
    ("staropal_shard", (45, 42, 70), (227, 196, 255), "shard"),
    ("reedwood_delver", (113, 76, 48), (195, 217, 210), "pick"),
    ("reedwood_spade", (113, 76, 48), (195, 217, 210), "spade"),
    ("reedwood_feller", (113, 76, 48), (195, 217, 210), "axe"),
    ("reedwood_sickle", (113, 76, 48), (195, 217, 210), "sickle"),
    ("reedwood_mallet", (113, 76, 48), (195, 217, 210), "mallet"),
    ("reedwood_carver", (113, 76, 48), (195, 217, 210), "carver"),
    ("reedwood_tiller", (113, 76, 48), (195, 217, 210), "tiller"),
    ("flint_delver", (70, 75, 80), (220, 221, 204), "pick"),
    ("flint_spade", (70, 75, 80), (220, 221, 204), "spade"),
    ("flint_feller", (70, 75, 80), (220, 221, 204), "axe"),
    ("flint_sickle", (70, 75, 80), (220, 221, 204), "sickle"),
    ("flint_mallet", (70, 75, 80), (220, 221, 204), "mallet"),
    ("flint_carver", (70, 75, 80), (220, 221, 204), "carver"),
    ("flint_tiller", (70, 75, 80), (220, 221, 204), "tiller"),
    ("field_bandage", (216, 205, 178), (236, 84, 91), "bandage"),
]


UI_SPRITES = [
    ("hotbar_frame", 128, 24, (34, 39, 48, 168), (104, 191, 147, 235), "frame"),
    ("selected_slot", 24, 24, (42, 48, 58, 70), (255, 206, 83, 255), "selected"),
    ("health_pip", 16, 16, (98, 38, 56, 0), (239, 84, 93, 255), "pip"),
    ("inventory_panel", 96, 64, (35, 43, 54, 218), (105, 191, 148, 245), "panel"),
    ("crafting_panel", 96, 64, (47, 41, 56, 218), (231, 183, 101, 245), "panel"),
    ("multiplayer_status_badge", 48, 20, (33, 41, 52, 190), (93, 177, 222, 250), "badge"),
    ("settings_panel", 96, 64, (37, 42, 51, 218), (112, 178, 220, 245), "panel"),
    ("feedback_toast", 96, 32, (32, 38, 43, 216), (255, 206, 83, 245), "badge"),
]


VFX_SPRITES = [
    ("block_dust_particle", (139, 121, 96), (222, 206, 170), "dust"),
    ("block_puff_particle", (165, 155, 136), (238, 231, 208), "puff"),
    ("resource_spark_particle", (75, 192, 214), (244, 254, 255), "spark"),
    ("craft_spark_particle", (245, 188, 74), (255, 241, 151), "spark"),
    ("rain_splash_particle", (91, 177, 220), (205, 239, 255), "splash"),
    ("snowflake_particle", (202, 231, 244), (255, 255, 255), "flake"),
    ("fog_wisp_particle", (169, 185, 190), (232, 241, 242), "wisp"),
    ("ember_particle", (238, 96, 39), (255, 216, 80), "ember"),
]


META_GUIDS = {
    "Assets/Blockiverse/Art/Textures/Blocks": "f6658262dc8e4ddfb830252033f0a3c4",
    BLOCK_SOURCE_DIR: "5a7112ded78d4dcdb752cab899a42bd0",
    BLOCK_TEXTURE_SET_ROOT: "cc62f93e8c5d405b90b1a18f9bd0667e",
    ITEM_DIR: "5ffb1d25fb834582a8f9dfe90a1e8f9c",
    "Assets/Blockiverse/Art/Sprites": "6df33f05aaad4bd3842acb84f6e3b257",
    UI_DIR: "8d31bf8a0efa46d0a3f33813b3353db2",
    VFX_DIR: "56d6236dd85e435d97ad2210d6f1af10",
}


def clamp(value):
    return max(0, min(255, int(round(value))))


def hash_pixel(x, y, seed):
    value = seed
    value = (value * 397) ^ x
    value = (value * 397) ^ y
    value ^= value >> 13
    value = (value * 1274126177) & 0xFFFFFFFF
    return value


def mix(a, b, factor):
    return tuple(clamp(a[i] * (1.0 - factor) + b[i] * factor) for i in range(len(a)))


def shade(color, factor):
    return tuple(clamp(channel * factor) for channel in color)


def with_alpha(color, alpha=255):
    return color if len(color) == 4 else color + (alpha,)


def clamp01(value):
    return max(0.0, min(1.0, value))


def block_pixel(base, accent, pattern, seed, x, y):
    h = hash_pixel(x, y, seed)
    last = TILE_PIXELS - 1
    mid = last * 0.5
    nx = x / last
    ny = y / last
    dx = x - mid
    dy = y - mid
    center = math.sqrt(dx * dx + dy * dy)
    cell2 = hash_pixel(x // 2, y // 2, seed + 17)
    cell4 = hash_pixel(x // 4, y // 4, seed + 29)
    edge = 0.80 if x in (0, last) or y in (0, last) else 1.0
    top_light = 1.06 - ny * 0.11
    factor = 0.10 + (h & 31) / 310.0 + (cell4 & 7) / 120.0
    dark = False

    if pattern == "grain":
        factor += 0.28 if (x + (cell4 % 3)) % 7 <= 1 or h % 13 == 0 else 0.0
        dark = h % 17 == 0
    elif pattern == "strata":
        factor += 0.34 if (y + (cell4 % 3)) % 8 <= 1 or h % 19 == 0 else 0.0
        dark = (y + cell2) % 11 == 0
    elif pattern == "rings":
        factor += 0.34 if int(center / 2.0) % 4 <= 1 else 0.0
        dark = abs(center - 10.0) < 1.0 or h % 31 == 0
    elif pattern == "leaves":
        factor += 0.42 if h % 5 <= 1 or cell2 % 11 <= 2 else 0.0
        dark = h % 13 == 0
    elif pattern == "crystal":
        factor += 0.48 if abs(x - y) <= 1 or abs((last - x) - y) <= 1 or h % 17 == 0 else 0.0
        dark = h % 23 == 0
    elif pattern == "veins":
        factor += 0.50 if abs(math.sin((x + y + seed) * 0.25) * 7 + (x - mid)) < 1.7 or h % 29 == 0 else 0.0
    elif pattern == "ore_bands":
        factor += 0.42 if y % 9 in (2, 3, 4) or h % 37 == 0 else 0.0
        dark = h % 11 == 0
    elif pattern == "ore_diagonal":
        factor += 0.46 if (x + y + cell4 % 4) % 11 <= 2 or h % 31 == 0 else 0.0
    elif pattern == "ore_cross":
        factor += 0.48 if abs(x - y) <= 2 or abs((last - x) - y) <= 2 or h % 41 == 0 else 0.0
    elif pattern == "ore_threads":
        factor += 0.52 if abs(math.sin((x + y) * 0.33) * 7 + (x - mid)) < 1.8 or h % 43 == 0 else 0.0
    elif pattern == "grid":
        factor += 0.35 if x % 10 <= 1 or y % 10 <= 1 else 0.0
        dark = x % 10 == 0 or y % 10 == 0
    elif pattern == "glow":
        factor += 0.62 if center < TILE_PIXELS * 0.28 or h % 23 == 0 else 0.0
        top_light = 1.12
    elif pattern == "crate":
        factor += 0.38 if x < 4 or y < 4 or x > last - 4 or y > last - 4 or abs(x - y) <= 1 or abs(x + y - last) <= 1 else 0.0
        dark = x in (4, last - 4) or y in (4, last - 4)
    elif pattern == "deep":
        factor += 0.25 if y > TILE_PIXELS * 0.66 or h % 29 == 0 else 0.0
        dark = h % 7 == 0
    elif pattern == "speckles":
        factor += 0.38 if h % 6 == 0 or cell2 % 17 == 0 else 0.0
        dark = h % 10 == 0
    elif pattern == "sediment":
        factor += 0.30 if (y + cell4 % 4) % 9 <= 1 or h % 23 == 0 else 0.0
    elif pattern == "column":
        factor += 0.32 if x % 8 <= 1 or (y + h) % 31 == 0 else 0.0
        dark = x % 8 == 0
    elif pattern == "snow":
        factor += 0.55 if y < TILE_PIXELS * 0.30 or h % 11 <= 1 else 0.0
        dark = cell4 % 19 == 0
    elif pattern == "roots":
        factor += 0.42 if (x + y + h) % 13 <= 2 or abs(math.sin(y * 0.34 + seed) * 7 + dx) < 1.6 else 0.0
        dark = h % 9 == 0
    elif pattern == "pebbles":
        factor += 0.44 if (x // 4 + y // 3 + h) % 5 == 0 else 0.0
        dark = h % 8 == 0
    elif pattern == "glass":
        factor += 0.50 if abs(x - y) <= 1 or abs(x + y - last) <= 1 or x in (4, last - 4) or y in (4, last - 4) else 0.0
        edge = 0.92
    elif pattern == "thorn":
        factor += 0.40 if abs(math.sin(y * 0.42 + seed) * 9 + dx) < 1.7 or (x + y) % 13 <= 1 else 0.0
        dark = h % 6 == 0
    elif pattern == "reeds":
        factor += 0.42 if x % 6 <= 1 or abs(math.sin(y * 0.27 + x) * 5) < 1.1 else 0.0
        dark = x % 6 == 0
    elif pattern == "planks":
        factor += 0.32 if y % 10 <= 1 or h % 19 == 0 else 0.0
        dark = y % 10 == 0 or x % 15 == 0
    elif pattern == "smooth_planks":
        factor += 0.26 if y % 12 <= 1 or h % 23 == 0 else 0.0
        dark = y % 12 == 0
    elif pattern == "brick":
        mortar = y % 10 <= 1 or (x % 16 <= 1 if (y // 10) % 2 == 0 else (x + 8) % 16 <= 1)
        factor += 0.35 if mortar or h % 29 == 0 else 0.0
        dark = mortar
    elif pattern == "chips":
        factor += 0.42 if abs(x - y) < 2 or h % 10 == 0 else 0.0
        dark = h % 12 == 0
    elif pattern == "sparkle":
        factor += 0.62 if abs(x - y) <= 1 or abs(x + y - last) <= 1 or h % 17 == 0 else 0.0
    elif pattern == "crust":
        factor += 0.50 if y < 6 or x < 4 or h % 13 <= 1 else 0.0
        dark = h % 17 == 0
    elif pattern == "berries":
        factor += 0.44 if h % 8 <= 2 else 0.0
    elif pattern == "berries_cluster":
        berry_centers = ((8, 9), (20, 8), (14, 18), (24, 23), (6, 24), (17, 26))
        factor += 0.58 if any((x - cx) * (x - cx) + (y - cy) * (y - cy) <= 8 for cx, cy in berry_centers) else (0.25 if h % 5 <= 1 else 0.0)
        dark = h % 12 == 0
    elif pattern == "grain_heads":
        factor += 0.58 if x % 7 in (2, 3) or ((x + y) % 7 == 0 and 5 <= y <= 22) else 0.0
    elif pattern in ("crop_sprout", "crop_mid", "crop_full"):
        growth = {"crop_sprout": 0.45, "crop_mid": 0.65, "crop_full": 0.85}[pattern]
        factor += 0.48 if y > TILE_PIXELS * (1.0 - growth) and (x % 7 <= 1 or h % 11 == 0) else 0.0
        dark = y > TILE_PIXELS * 0.82 and h % 9 == 0
    elif pattern in ("bush_sprout", "bush_mid"):
        factor += 0.46 if center < TILE_PIXELS * (0.22 if pattern == "bush_sprout" else 0.34) or h % 7 <= 1 else 0.0
        dark = h % 11 == 0
    elif pattern == "reed_sprout":
        factor += 0.42 if x % 9 <= 1 and y > TILE_PIXELS * 0.38 else 0.0
    elif pattern in ("sapling", "sapling_mid", "sapling_tall"):
        height = {"sapling": 0.55, "sapling_mid": 0.70, "sapling_tall": 0.86}[pattern]
        trunk = abs(dx) < 2.2 and y > TILE_PIXELS * (1.0 - height)
        leaves = center < TILE_PIXELS * (0.18 if pattern == "sapling" else 0.24) and y < TILE_PIXELS * 0.55
        factor += 0.52 if trunk or leaves or h % 17 == 0 else 0.0
        dark = trunk and h % 4 == 0
    elif pattern == "flame":
        flame = abs(dx) < (12.0 * (1.0 - ny)) + 2.0 and y < TILE_PIXELS * 0.82
        factor += 0.64 if flame or h % 23 == 0 else 0.0
        top_light = 1.15
        dark = y > TILE_PIXELS * 0.78
    elif pattern == "lamp":
        glass = center < TILE_PIXELS * 0.30 or (abs(dx) < 8 and abs(dy) < 12)
        factor += 0.66 if glass or h % 31 == 0 else 0.0
        top_light = 1.12
        dark = x < 4 or x > last - 4 or y < 4 or y > last - 4
    elif pattern == "flare":
        factor += 0.70 if abs(dx) < 4 or abs(dy) < 4 or abs(x - y) <= 2 or abs(x + y - last) <= 2 else 0.0
        top_light = 1.18
    elif pattern == "kiln":
        factor += 0.36 if y % 9 <= 1 or x in (4, last - 4) or center < 7 else 0.0
        dark = center < 5 or h % 13 == 0
    elif pattern == "forge":
        factor += 0.48 if center < 8 or x in (4, last - 4) or y in (6, last - 6) else 0.0
        dark = h % 7 == 0
    elif pattern == "tools":
        factor += 0.44 if abs(x - y) < 2 or abs((last - x) - y) < 2 or h % 29 == 0 else 0.0
        dark = h % 11 == 0
    elif pattern == "tilled":
        factor += 0.30 if x % 8 <= 2 or h % 17 == 0 else 0.0
        dark = x % 8 == 0 or h % 10 == 0
    elif pattern == "basket":
        factor += 0.38 if (x + y) % 8 <= 2 or (x - y) % 8 <= 2 else 0.0
        dark = x % 8 == 0 or y % 8 == 0
    elif pattern == "rack":
        factor += 0.42 if x in (5, 6, last - 6, last - 5) or y in (8, last - 8) or abs(x - y) <= 1 else 0.0
        dark = h % 8 == 0
    elif pattern == "jar":
        jar = (dx * dx) / 130.0 + (dy * dy) / 180.0 < 1.0
        factor += 0.50 if jar or abs(dx) < 2 else 0.0
        dark = jar and (x < mid or y > mid) and h % 5 == 0
    elif pattern == "locker":
        factor += 0.38 if x < 4 or x > last - 4 or y < 4 or y > last - 4 or x == int(mid) or h % 23 == 0 else 0.0
        dark = x in (4, int(mid), last - 4) or y in (4, last - 4)
    elif pattern == "fluid_water":
        wave = abs(math.sin((x + seed) * 0.34) * 4 + math.sin(y * 0.29) * 3) < 1.6
        factor += 0.52 if wave or h % 17 == 0 else 0.0
        edge = 0.94
    elif pattern == "fluid_brine":
        wave = abs(math.sin((x + y + seed) * 0.24) * 5) < 1.3
        factor += 0.48 if wave or h % 19 == 0 else 0.0
        dark = h % 9 == 0
        edge = 0.94
    elif pattern == "fluid_ember":
        vein = abs(math.sin((x + seed) * 0.30) * 8 + math.cos(y * 0.37) * 5) < 2.0
        factor += 0.68 if vein or h % 11 == 0 else 0.0
        dark = not vein and h % 4 == 0
        top_light = 1.20
    elif pattern == "bedroll":
        factor += 0.40 if x % 9 <= 1 or y % 12 <= 1 or h % 19 == 0 else 0.0
        dark = x < 5 or x > last - 5 or y < 5 or y > last - 5
    else:
        factor += 0.28 if h % 7 <= 1 else 0.0

    color = mix(base, accent, clamp01(factor))
    if dark:
        color = shade(color, 0.76)
    if h % 47 == 0:
        color = mix(color, accent, 0.35)
    return with_alpha(shade(color, edge * top_light), 255)


def make_block_tile(block):
    _, _, base, accent, pattern, seed = block
    return [[block_pixel(base, accent, pattern, seed, x, y) for x in range(TILE_PIXELS)] for y in range(TILE_PIXELS)]


def atlas_width():
    return ATLAS_COLUMNS * ATLAS_TILE_STRIDE_PIXELS


def atlas_height():
    return ATLAS_ROWS * ATLAS_TILE_STRIDE_PIXELS


def empty_image(width, height, color=(0, 0, 0, 0)):
    return [[color for _ in range(width)] for _ in range(height)]


def make_item_icon(item):
    _, base, accent, shape = item
    image = empty_image(64, 64)
    for y in range(64):
        for x in range(64):
            dx = x - 31.5
            dy = y - 31.5
            h = hash_pixel(x, y, len(shape) * 31)
            color = None

            if shape == "log":
                if 12 <= x <= 51 and 20 <= y <= 43:
                    color = accent if x % 9 in (0, 1) or h % 17 == 0 else base
            elif shape == "shard":
                if abs(dx * 0.6 + dy * 0.95) < 22 and 12 < y < 53 and 13 < x < 51:
                    color = accent if x == y or h % 11 == 0 else base
            elif shape == "nugget":
                if (dx * dx) / 380 + (dy * dy) / 260 < 1:
                    color = accent if h % 7 <= 2 else base
            elif shape == "kit":
                if 15 <= x <= 49 and 16 <= y <= 48:
                    color = accent if x in range(15, 20) or y in range(16, 21) or h % 23 == 0 else base
            elif shape == "block":
                if 14 <= x <= 49 and 14 <= y <= 49:
                    edge = x in (14, 15, 48, 49) or y in (14, 15, 48, 49)
                    color = accent if edge or h % 11 == 0 else base
            elif shape == "leaf":
                if (dx * dx) / 520 + (dy * dy) / 360 < 1:
                    color = accent if h % 5 <= 1 else base
            elif shape == "plant":
                if abs(dx) < 5 and 13 < y < 53:
                    color = base
                elif abs(dx + dy * 0.3) < 4 and 18 < y < 54:
                    color = accent
                elif abs(dx - dy * 0.3) < 4 and 18 < y < 54:
                    color = accent
            elif shape == "torch":
                if 28 <= x <= 36 and 23 < y < 55:
                    color = base
                elif (dx * dx) / 115 + ((y - 20) * (y - 20)) / 180 < 1:
                    color = accent
            elif shape == "thread":
                if abs(math.sin((x + y) * 0.18) * 12 + dx) < 4 and 14 < y < 52:
                    color = accent
            elif shape == "drop":
                if (dx * dx) / 260 + ((y - 35) * (y - 35)) / 380 < 1 and y > 13:
                    color = accent if h % 4 else base
            elif shape == "berry":
                if (dx * dx + dy * dy) < 290 or ((x - 22) ** 2 + (y - 24) ** 2) < 90 or ((x - 42) ** 2 + (y - 40) ** 2) < 100:
                    color = accent if h % 3 else base
            elif shape == "grain":
                if 29 <= x <= 34 and 14 < y < 55:
                    color = base
                elif abs(dx) < 12 and 12 < y < 38 and h % 3 == 0:
                    color = accent
            elif shape == "tool":
                if abs(dx + dy * 0.45) < 5 and -20 < dy < 18:
                    color = accent
                elif abs(dx - 12) < 4 and 12 < y < 50:
                    color = base
            elif shape == "pick":
                if 13 <= y <= 21 and 10 <= x <= 53 and abs(y - 17) + abs(x - 32) < 30:
                    color = accent
                elif abs(dx) < 4 and 18 < y < 52:
                    color = base
            elif shape == "spade":
                if (dx * dx) / 100 + ((y - 22) * (y - 22)) / 120 < 1:
                    color = accent
                elif abs(dx) < 4 and 28 < y < 54:
                    color = base
            elif shape == "axe":
                if 15 <= y <= 28 and 18 <= x <= 47 and abs(x - 29) + abs(y - 21) < 23:
                    color = accent
                elif abs(dx - 6) < 4 and 20 < y < 54:
                    color = base
            elif shape == "sickle":
                if 13 < y < 43 and 12 < x < 50 and abs(math.sqrt((x - 34) ** 2 + (y - 28) ** 2) - 17) < 4:
                    color = accent
                elif abs(dx - 7) < 4 and 30 < y < 55:
                    color = base
            elif shape == "mallet":
                if 14 <= y <= 27 and 12 <= x <= 51:
                    color = accent
                elif abs(dx) < 4 and 26 < y < 55:
                    color = base
            elif shape == "carver":
                if abs(dx + dy * 0.25) < 4 and 12 < y < 49:
                    color = accent
                elif 35 <= x <= 42 and 37 <= y <= 55:
                    color = base
            elif shape == "tiller":
                if abs(dx) < 4 and 14 < y < 55:
                    color = base
                elif y in range(16, 23) and 14 <= x <= 50 and x % 9 < 5:
                    color = accent
            elif shape == "bandage":
                if 14 <= x <= 50 and 24 <= y <= 40:
                    color = base
                if 28 <= x <= 36 and 18 <= y <= 46:
                    color = accent

            if color is not None:
                image[y][x] = with_alpha(color, 255)
    return image


def make_vfx_sprite(sprite):
    _, base, accent, shape = sprite
    image = empty_image(32, 32)
    for y in range(32):
        for x in range(32):
            dx = x - 15.5
            dy = y - 15.5
            distance = math.sqrt(dx * dx + dy * dy)
            h = hash_pixel(x, y, len(shape) * 43)
            color = None
            alpha = 0

            if shape == "dust" and distance < 12 and h % 4 <= 1:
                color = base if h % 3 else accent
                alpha = 180
            elif shape == "puff" and distance < 13:
                color = mix(base, accent, max(0.0, 1.0 - distance / 13.0))
                alpha = clamp(150 * (1.0 - distance / 13.0))
            elif shape == "spark" and (abs(dx) < 2 or abs(dy) < 2 or abs(dx - dy) < 2 or abs(dx + dy) < 2) and distance < 13:
                color = accent if distance < 5 else base
                alpha = 220
            elif shape == "splash" and (abs(dy + 5) < 2 or abs(dx) < 3 or h % 13 == 0) and distance < 13:
                color = accent
                alpha = 170
            elif shape == "flake" and (abs(dx) < 1.5 or abs(dy) < 1.5 or abs(dx - dy) < 1.5 or abs(dx + dy) < 1.5) and distance < 11:
                color = accent
                alpha = 210
            elif shape == "wisp" and abs(math.sin(x * 0.26) * 8 + dy) < 4 and 3 < x < 29:
                color = mix(base, accent, x / 31.0)
                alpha = 95
            elif shape == "ember" and distance < 8:
                color = accent if distance < 3 else base
                alpha = clamp(230 * (1.0 - distance / 8.0))

            if color is not None:
                image[y][x] = with_alpha(color, alpha)
    return image


def make_ui_sprite(sprite):
    _, width, height, fill, accent, shape = sprite
    image = empty_image(width, height)
    for y in range(height):
        for x in range(width):
            border = x in (0, width - 1) or y in (0, height - 1)
            inner_border = x in (1, width - 2) or y in (1, height - 2)

            if shape == "pip":
                dx = (x - width / 2) / (width / 2)
                dy = (y - height / 2) / (height / 2)
                if dx * dx + dy * dy < 0.72:
                    image[y][x] = accent if y < height * 0.7 else shade(accent[:3], 0.75) + (accent[3],)
            elif border or inner_border:
                image[y][x] = accent
            elif 2 <= x <= width - 3 and 2 <= y <= height - 3:
                image[y][x] = fill

            if shape == "selected" and 4 <= x <= width - 5 and 4 <= y <= height - 5:
                image[y][x] = (255, 222, 119, 80)
            elif shape == "badge" and 7 <= x <= 15 and 6 <= y <= 13:
                image[y][x] = accent
    return image


def png_bytes(image):
    height = len(image)
    width = len(image[0])
    raw = bytearray()

    for row in image:
        raw.append(0)
        for pixel in row:
            raw.extend(pixel)

    def chunk(kind, data):
        payload = kind + data
        return struct.pack(">I", len(data)) + payload + struct.pack(">I", zlib.crc32(payload) & 0xFFFFFFFF)

    signature = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return signature + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")


def paeth(a, b, c):
    p = a + b - c
    pa = abs(p - a)
    pb = abs(p - b)
    pc = abs(p - c)
    if pa <= pb and pa <= pc:
        return a
    if pb <= pc:
        return b
    return c


def read_rgba_png(relative_path):
    path = os.path.join(ROOT, relative_path)
    data = open(path, "rb").read()
    if not data.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError(f"Not a PNG: {relative_path}")

    offset = 8
    width = height = bit_depth = color_type = None
    idat = bytearray()
    while offset < len(data):
        length = int.from_bytes(data[offset : offset + 4], "big")
        tag = data[offset + 4 : offset + 8]
        payload = data[offset + 8 : offset + 8 + length]
        offset += 12 + length

        if tag == b"IHDR":
            width = int.from_bytes(payload[0:4], "big")
            height = int.from_bytes(payload[4:8], "big")
            bit_depth = payload[8]
            color_type = payload[9]
            if payload[12] != 0:
                raise ValueError(f"Interlaced PNG is not supported: {relative_path}")
        elif tag == b"IDAT":
            idat.extend(payload)
        elif tag == b"IEND":
            break

    if width is None or height is None or bit_depth != 8 or color_type not in (2, 6):
        raise ValueError(f"Unsupported PNG format: {relative_path}")

    channels = 4 if color_type == 6 else 3
    stride = width * channels
    raw = zlib.decompress(bytes(idat))
    rows = []
    cursor = 0
    previous = bytearray(stride)

    for _ in range(height):
        filter_type = raw[cursor]
        cursor += 1
        row = bytearray(raw[cursor : cursor + stride])
        cursor += stride

        for i in range(stride):
            left = row[i - channels] if i >= channels else 0
            up = previous[i]
            up_left = previous[i - channels] if i >= channels else 0

            if filter_type == 1:
                row[i] = (row[i] + left) & 0xFF
            elif filter_type == 2:
                row[i] = (row[i] + up) & 0xFF
            elif filter_type == 3:
                row[i] = (row[i] + ((left + up) >> 1)) & 0xFF
            elif filter_type == 4:
                row[i] = (row[i] + paeth(left, up, up_left)) & 0xFF
            elif filter_type != 0:
                raise ValueError(f"Unsupported PNG filter {filter_type}: {relative_path}")

        rows.append(bytes(row))
        previous = row

    pixels = []
    for row in rows:
        out_row = []
        for x in range(width):
            base = x * channels
            r = row[base]
            g = row[base + 1]
            b = row[base + 2]
            a = row[base + 3] if channels == 4 else 255
            out_row.append((r, g, b, a))
        pixels.append(out_row)
    return pixels


def write_png(relative_path, image):
    path = os.path.join(ROOT, relative_path)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as handle:
        handle.write(png_bytes(image))


def guid_for(path):
    if path not in META_GUIDS:
        META_GUIDS[path] = f"{zlib.crc32(path.encode('utf-8')) & 0xFFFFFFFF:08x}" * 4
    return META_GUIDS[path][:32]


def write_folder_meta(relative_path):
    path = os.path.join(ROOT, f"{relative_path}.meta")
    if os.path.exists(path):
        return
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(
            f"fileFormatVersion: 2\n"
            f"guid: {guid_for(relative_path)}\n"
            "folderAsset: yes\n"
            "DefaultImporter:\n"
            "  externalObjects: {}\n"
            "  userData:\n"
            "  assetBundleName:\n"
            "  assetBundleVariant:\n"
        )


def write_texture_meta(
    relative_path,
    sprite=False,
    max_size=64,
    enable_mipmaps=False,
    filter_mode=0,
    aniso=1,
    android_texture_compression=0,
):
    path = os.path.join(ROOT, f"{relative_path}.meta")
    texture_type = 8 if sprite else 0
    sprite_mode = 1 if sprite else 0
    alpha_transparency = 1 if sprite else 0
    pixels_per_unit = 16 if sprite else 100
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(
            "fileFormatVersion: 2\n"
            f"guid: {guid_for(relative_path)}\n"
            "TextureImporter:\n"
            "  internalIDToNameTable: []\n"
            "  externalObjects: {}\n"
            "  serializedVersion: 13\n"
            "  mipmaps:\n"
            "    mipMapMode: 0\n"
            f"    enableMipMap: {1 if enable_mipmaps else 0}\n"
            "    sRGBTexture: 1\n"
            "    linearTexture: 0\n"
            "    fadeOut: 0\n"
            "    borderMipMap: 0\n"
            "    mipMapsPreserveCoverage: 0\n"
            "    alphaTestReferenceValue: 0.5\n"
            "    mipMapFadeDistanceStart: 1\n"
            "    mipMapFadeDistanceEnd: 3\n"
            "  bumpmap:\n"
            "    convertToNormalMap: 0\n"
            "    externalNormalMap: 0\n"
            "    heightScale: 0.25\n"
            "    normalMapFilter: 0\n"
            "  isReadable: 0\n"
            "  streamingMipmaps: 0\n"
            "  streamingMipmapsPriority: 0\n"
            "  vTOnly: 0\n"
            "  ignoreMipmapLimit: 0\n"
            "  grayScaleToAlpha: 0\n"
            "  generateCubemap: 6\n"
            "  cubemapConvolution: 0\n"
            "  seamlessCubemap: 0\n"
            "  textureFormat: 1\n"
            f"  maxTextureSize: {max_size}\n"
            "  textureSettings:\n"
            "    serializedVersion: 2\n"
            f"    filterMode: {filter_mode}\n"
            f"    aniso: {aniso}\n"
            "    mipBias: 0\n"
            "    wrapU: 1\n"
            "    wrapV: 1\n"
            "    wrapW: 1\n"
            "  nPOTScale: 0\n"
            "  lightmap: 0\n"
            "  compressionQuality: 50\n"
            f"  spriteMode: {sprite_mode}\n"
            "  spriteExtrude: 1\n"
            "  spriteMeshType: 1\n"
            "  alignment: 0\n"
            "  spritePivot: {x: 0.5, y: 0.5}\n"
            f"  spritePixelsToUnits: {pixels_per_unit}\n"
            "  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n"
            "  spriteGenerateFallbackPhysicsShape: 1\n"
            "  alphaUsage: 1\n"
            f"  alphaIsTransparency: {alpha_transparency}\n"
            "  spriteTessellationDetail: -1\n"
            f"  textureType: {texture_type}\n"
            "  textureShape: 1\n"
            "  singleChannelComponent: 0\n"
            "  flipbookRows: 1\n"
            "  flipbookColumns: 1\n"
            "  maxTextureSizeSet: 0\n"
            "  compressionQualitySet: 0\n"
            "  textureFormatSet: 0\n"
            "  ignorePngGamma: 0\n"
            "  applyGammaDecoding: 0\n"
            "  swizzle: 50462976\n"
            "  cookieLightType: 0\n"
            "  platformSettings:\n"
            "  - serializedVersion: 3\n"
            "    buildTarget: DefaultTexturePlatform\n"
            f"    maxTextureSize: {max_size}\n"
            "    resizeAlgorithm: 0\n"
            "    textureFormat: -1\n"
            "    textureCompression: 0\n"
            "    compressionQuality: 50\n"
            "    crunchedCompression: 0\n"
            "    allowsAlphaSplitting: 0\n"
            "    overridden: 0\n"
            "    androidETC2FallbackOverride: 0\n"
            "    forceMaximumCompressionQuality_BC6H_BC7: 0\n"
            "  - serializedVersion: 3\n"
            "    buildTarget: Android\n"
            f"    maxTextureSize: {max_size}\n"
            "    resizeAlgorithm: 0\n"
            "    textureFormat: -1\n"
            f"    textureCompression: {android_texture_compression}\n"
            "    compressionQuality: 50\n"
            "    crunchedCompression: 0\n"
            "    allowsAlphaSplitting: 0\n"
            "    overridden: 1\n"
            "    androidETC2FallbackOverride: 0\n"
            "    forceMaximumCompressionQuality_BC6H_BC7: 0\n"
            "  spriteSheet:\n"
            "    serializedVersion: 2\n"
            "    sprites: []\n"
            "    outline: []\n"
            "    physicsShape: []\n"
            "    bones: []\n"
            "    spriteID: 5e97eb03825dee720800000000000000\n"
            "    internalID: 0\n"
            "    vertices: []\n"
            "    indices:\n"
            "    edges: []\n"
            "    weights: []\n"
            "    secondaryTextures: []\n"
            "    nameFileIdTable: {}\n"
            "  mipmapLimitGroupName:\n"
            "  pSDRemoveMatte: 0\n"
            "  userData:\n"
            "  assetBundleName:\n"
            "  assetBundleVariant:\n"
        )


def next_power_of_two(value):
    return 1 << (value - 1).bit_length()


def atlas_max_texture_size():
    return next_power_of_two(max(atlas_width(), atlas_height()))


def blit_padded_tile(atlas, tile, origin_x, origin_y):
    for y in range(-ATLAS_TILE_PADDING_PIXELS, TILE_PIXELS + ATLAS_TILE_PADDING_PIXELS):
        source_y = max(0, min(TILE_PIXELS - 1, y))
        for x in range(-ATLAS_TILE_PADDING_PIXELS, TILE_PIXELS + ATLAS_TILE_PADDING_PIXELS):
            source_x = max(0, min(TILE_PIXELS - 1, x))
            atlas[origin_y + y][origin_x + x] = tile[source_y][source_x]


def texture_set_atlas_path(set_id):
    return f"{BLOCK_TEXTURE_SET_ROOT}/{set_id}/blockiverse_block_atlas.png"


def write_texture_set_atlas(set_id):
    source_dir = f"{BLOCK_TEXTURE_SET_ROOT}/{set_id}/Source"
    atlas = empty_image(atlas_width(), atlas_height(), (58, 60, 65, 255))

    for name, tile_index, *_ in BLOCKS:
        tile_path = f"{source_dir}/{name}.png"
        tile = read_rgba_png(tile_path)
        if len(tile) != TILE_PIXELS or len(tile[0]) != TILE_PIXELS:
            raise ValueError(f"{tile_path} must be {TILE_PIXELS}x{TILE_PIXELS}")

        tile_x = tile_index % ATLAS_COLUMNS
        tile_y = tile_index // ATLAS_COLUMNS
        origin_x = tile_x * ATLAS_TILE_STRIDE_PIXELS + ATLAS_TILE_PADDING_PIXELS
        origin_y = tile_y * ATLAS_TILE_STRIDE_PIXELS + ATLAS_TILE_PADDING_PIXELS
        blit_padded_tile(atlas, tile, origin_x, origin_y)

    atlas_path = texture_set_atlas_path(set_id)
    write_png(atlas_path, atlas)
    write_texture_meta(
        atlas_path,
        sprite=False,
        max_size=atlas_max_texture_size(),
        enable_mipmaps=True,
        filter_mode=2,
        aniso=4,
        android_texture_compression=1,
    )


def write_assets():
    for folder in [
        "Assets/Blockiverse/Art/Textures",
        "Assets/Blockiverse/Art/Textures/Blocks",
        BLOCK_SOURCE_DIR,
        BLOCK_TEXTURE_SET_ROOT,
        *[f"{BLOCK_TEXTURE_SET_ROOT}/{set_id}" for set_id in BLOCK_TEXTURE_SET_IDS],
        ITEM_DIR,
        "Assets/Blockiverse/Art/Sprites",
        UI_DIR,
        VFX_DIR,
    ]:
        os.makedirs(os.path.join(ROOT, folder), exist_ok=True)
        write_folder_meta(folder)

    for block in BLOCKS:
        name, tile_index, *_ = block
        tile = make_block_tile(block)
        write_png(f"{BLOCK_SOURCE_DIR}/{name}.png", tile)
        write_texture_meta(f"{BLOCK_SOURCE_DIR}/{name}.png", sprite=False, max_size=TILE_PIXELS)

    for name, _, base, accent, pattern, seed in BLOCK_SOURCE_ALIASES:
        tile = make_block_tile((name, -1, base, accent, pattern, seed))
        write_png(f"{BLOCK_SOURCE_DIR}/{name}.png", tile)
        write_texture_meta(f"{BLOCK_SOURCE_DIR}/{name}.png", sprite=False, max_size=TILE_PIXELS)

    for set_id in BLOCK_TEXTURE_SET_IDS:
        write_texture_set_atlas(set_id)

    for item in ITEMS:
        name = item[0]
        write_png(f"{ITEM_DIR}/{name}.png", make_item_icon(item))
        write_texture_meta(f"{ITEM_DIR}/{name}.png", sprite=True, max_size=64)

    for sprite in UI_SPRITES:
        name = sprite[0]
        write_png(f"{UI_DIR}/{name}.png", make_ui_sprite(sprite))
        write_texture_meta(f"{UI_DIR}/{name}.png", sprite=True, max_size=128)

    for sprite in VFX_SPRITES:
        name = sprite[0]
        write_png(f"{VFX_DIR}/{name}.png", make_vfx_sprite(sprite))
        write_texture_meta(f"{VFX_DIR}/{name}.png", sprite=True, max_size=32)


if __name__ == "__main__":
    write_assets()
