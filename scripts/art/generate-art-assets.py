#!/usr/bin/env python3
import math
import os
import struct
import zlib


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BLOCK_SOURCE_DIR = "Assets/Blockiverse/Art/Textures/Blocks/Source"
ITEM_DIR = "Assets/Blockiverse/Art/Textures/Items"
UI_DIR = "Assets/Blockiverse/Art/Sprites/UI"
VFX_DIR = "Assets/Blockiverse/Art/Sprites/VFX"
ATLAS_PATH = "Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png"
ATLAS_COLUMNS = 8
ATLAS_ROWS = 7


BLOCKS = [
    ("meadow_turf", 0, (57, 134, 68), (151, 211, 83), "mottled", 11),
    ("loose_loam", 1, (105, 69, 44), (166, 111, 67), "grain", 23),
    ("graystone", 2, (91, 100, 112), (157, 167, 178), "strata", 37),
    ("branchwood_log", 3, (139, 90, 43), (215, 151, 76), "rings", 41),
    ("leafmoss", 4, (48, 125, 57), (106, 199, 74), "leaves", 53),
    ("lumen_quartz_cluster", 5, (72, 176, 208), (203, 242, 250), "crystal", 67),
    ("embercoal_seam", 6, (29, 30, 34), (86, 87, 98), "veins", 71),
    ("rosycopper_bloom", 7, (92, 83, 74), (234, 125, 62), "veins", 83),
    ("rustcore_ore", 8, (82, 87, 90), (215, 203, 176), "veins", 97),
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
    ("fired_brick", 31, (128, 58, 43), (208, 102, 65), "brick", 233),
    ("clearpane_glass", 32, (105, 188, 214), (220, 250, 255), "glass", 239),
    ("surface_pebbles", 33, (92, 87, 78), (181, 174, 151), "pebbles", 241),
    ("flinty_shingle", 34, (60, 67, 73), (187, 192, 186), "chips", 251),
    ("paletin_thread", 35, (79, 84, 89), (213, 196, 155), "veins", 257),
    ("sunmetal_fleck", 36, (127, 81, 43), (255, 196, 74), "sparkle", 263),
    ("niterstone_pocket", 37, (95, 90, 82), (220, 214, 184), "speckles", 269),
    ("brightsalt_crust", 38, (164, 174, 164), (246, 246, 225), "crust", 271),
    ("shellgrit_bed", 39, (156, 139, 110), (229, 205, 171), "chips", 277),
    ("resin_knot", 40, (110, 72, 39), (230, 143, 50), "rings", 281),
    ("berrybush", 41, (50, 112, 54), (203, 56, 91), "berries", 283),
    ("grain_stalk", 42, (119, 135, 54), (228, 196, 81), "reeds", 293),
    ("umbralite_node", 43, (37, 32, 59), (117, 81, 181), "crystal", 307),
    ("staropal_geode", 44, (45, 42, 70), (227, 196, 255), "crystal", 311),
    ("campfire", 45, (112, 68, 42), (255, 132, 43), "flame", 313),
    ("clay_kiln", 46, (120, 73, 56), (218, 124, 78), "kiln", 317),
    ("bellows_forge", 47, (66, 68, 72), (235, 116, 57), "forge", 331),
    ("prep_board", 48, (143, 92, 50), (227, 178, 102), "grid", 337),
    ("mend_bench", 49, (104, 74, 52), (196, 165, 94), "tools", 347),
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
    ("reedgrass", (84, 128, 64), (176, 190, 85), "plant"),
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
    ("surface_pebbles", (92, 87, 78), (181, 174, 151), "nugget"),
    ("flinty_shingle", (60, 67, 73), (187, 192, 186), "shard"),
    ("embercoal", (29, 30, 34), (86, 87, 98), "nugget"),
    ("raw_rosycopper", (118, 76, 48), (241, 135, 70), "nugget"),
    ("paletin_thread", (79, 84, 89), (213, 196, 155), "thread"),
    ("raw_rustcore", (89, 91, 94), (223, 216, 194), "nugget"),
    ("sunmetal_fleck", (127, 81, 43), (255, 196, 74), "nugget"),
    ("lumen_crystal", (72, 176, 208), (203, 242, 250), "shard"),
    ("niterstone", (95, 90, 82), (220, 214, 184), "nugget"),
    ("brightsalt", (164, 174, 164), (246, 246, 225), "nugget"),
    ("shellgrit", (156, 139, 110), (229, 205, 171), "nugget"),
    ("resin_knot", (110, 72, 39), (230, 143, 50), "drop"),
    ("berrybush", (50, 112, 54), (203, 56, 91), "berry"),
    ("grain_stalk", (119, 135, 54), (228, 196, 81), "grain"),
    ("umbralite_node", (37, 32, 59), (117, 81, 181), "shard"),
    ("staropal_geode", (45, 42, 70), (227, 196, 255), "shard"),
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
    ATLAS_PATH: "90a6fc9b496045d7ad07d8e02954ce10",
    "Assets/Blockiverse/Art/Textures/Blocks": "f6658262dc8e4ddfb830252033f0a3c4",
    BLOCK_SOURCE_DIR: "5a7112ded78d4dcdb752cab899a42bd0",
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


def block_pixel(base, accent, pattern, seed, x, y):
    h = hash_pixel(x, y, seed)
    edge = 0.78 if x in (0, 15) or y in (0, 15) else 1.0
    center = math.sqrt((x - 7.5) ** 2 + (y - 7.5) ** 2)
    use_accent = False

    if pattern == "grain":
        use_accent = x % 5 == 0 or h % 9 == 0
    elif pattern == "strata":
        use_accent = y % 4 == 0 or h % 17 == 0
    elif pattern == "rings":
        use_accent = int(center) % 5 <= 1
    elif pattern == "leaves":
        use_accent = h % 5 <= 1
    elif pattern == "crystal":
        use_accent = x == y or x + y == 15 or h % 13 == 0
    elif pattern == "veins":
        use_accent = x == (h + y) % 16 or h % 19 == 0
    elif pattern == "grid":
        use_accent = x % 7 == 0 or y % 7 == 0
    elif pattern == "glow":
        use_accent = center < 5 or h % 23 == 0
    elif pattern == "crate":
        use_accent = x < 2 or y < 2 or x > 13 or y > 13 or x == y or x + y == 15
    elif pattern == "deep":
        use_accent = y > 11 or h % 29 == 0
    elif pattern == "speckles":
        use_accent = h % 6 == 0
    elif pattern == "sediment":
        use_accent = y % 5 == 0 or h % 23 == 0
    elif pattern == "column":
        use_accent = x % 4 == 0 or (y + h) % 31 == 0
    elif pattern == "snow":
        use_accent = y < 4 or h % 11 <= 1
    elif pattern == "roots":
        use_accent = (x + y + h) % 9 <= 1
    elif pattern == "pebbles":
        use_accent = (x // 3 + y // 3 + h) % 5 == 0
    elif pattern == "glass":
        use_accent = x == y or x + y == 15 or x in (2, 13) or y in (2, 13)
    elif pattern == "thorn":
        use_accent = x == (y + h) % 16 or x + y in (9, 18, 24)
    elif pattern == "reeds":
        use_accent = x % 4 == 1 or x % 7 == 0
    elif pattern == "planks":
        use_accent = y in (4, 9, 14) or h % 19 == 0
    elif pattern == "brick":
        use_accent = y in (3, 8, 13) or (x in (7, 8) and y < 8) or (x in (3, 4, 12, 13) and y >= 8)
    elif pattern == "chips":
        use_accent = abs(x - y) < 2 or h % 10 == 0
    elif pattern == "sparkle":
        use_accent = x == y or x + y == 15 or h % 17 == 0
    elif pattern == "crust":
        use_accent = y < 3 or x < 2 or h % 13 <= 1
    elif pattern == "berries":
        use_accent = h % 8 <= 2
    elif pattern == "flame":
        use_accent = center < 4 or y < 5 or h % 23 == 0
    elif pattern == "kiln":
        use_accent = y in (4, 9, 14) or x in (2, 13) or center < 3
    elif pattern == "forge":
        use_accent = center < 4 or x in (2, 13) or y in (3, 12)
    elif pattern == "tools":
        use_accent = abs(x - y) < 2 or abs((15 - x) - y) < 2 or h % 29 == 0
    else:
        use_accent = h % 7 <= 1

    color = accent if use_accent else base
    return with_alpha(shade(color, edge), 255)


def make_block_tile(block):
    _, _, base, accent, pattern, seed = block
    return [[block_pixel(base, accent, pattern, seed, x, y) for x in range(16)] for y in range(16)]


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


def write_texture_meta(relative_path, sprite=False, max_size=64):
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
            "    enableMipMap: 0\n"
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
            "    filterMode: 0\n"
            "    aniso: 1\n"
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
            "    textureCompression: 0\n"
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


def write_assets():
    for folder in [
        "Assets/Blockiverse/Art/Textures",
        "Assets/Blockiverse/Art/Textures/Blocks",
        BLOCK_SOURCE_DIR,
        ITEM_DIR,
        "Assets/Blockiverse/Art/Sprites",
        UI_DIR,
        VFX_DIR,
    ]:
        os.makedirs(os.path.join(ROOT, folder), exist_ok=True)
        write_folder_meta(folder)

    atlas = empty_image(ATLAS_COLUMNS * 16, ATLAS_ROWS * 16, (58, 60, 65, 255))

    for block in BLOCKS:
        name, tile_index, *_ = block
        tile = make_block_tile(block)
        write_png(f"{BLOCK_SOURCE_DIR}/{name}.png", tile)
        write_texture_meta(f"{BLOCK_SOURCE_DIR}/{name}.png", sprite=False, max_size=16)
        tile_x = tile_index % ATLAS_COLUMNS
        tile_y = tile_index // ATLAS_COLUMNS
        origin_x = tile_x * 16
        origin_y = tile_y * 16
        for y in range(16):
            for x in range(16):
                atlas[origin_y + y][origin_x + x] = tile[y][x]

    write_png(ATLAS_PATH, atlas)
    write_texture_meta(ATLAS_PATH, sprite=False, max_size=max(ATLAS_COLUMNS, ATLAS_ROWS) * 16)

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
