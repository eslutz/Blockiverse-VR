#!/usr/bin/env python3
import math
import os
import struct
import zlib


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BLOCK_SOURCE_DIR = "Assets/Blockiverse/Art/Textures/Blocks/Source"
ITEM_DIR = "Assets/Blockiverse/Art/Textures/Items"
UI_DIR = "Assets/Blockiverse/Art/Sprites/UI"
ATLAS_PATH = "Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png"


BLOCKS = [
    ("meadow_turf", 0, (57, 134, 68), (151, 211, 83), "mottled", 11),
    ("loam", 1, (105, 69, 44), (166, 111, 67), "grain", 23),
    ("slate", 2, (91, 100, 112), (157, 167, 178), "strata", 37),
    ("timber", 3, (139, 90, 43), (215, 151, 76), "rings", 41),
    ("leafmass", 4, (48, 125, 57), (106, 199, 74), "leaves", 53),
    ("clearstone", 5, (72, 176, 208), (203, 242, 250), "crystal", 67),
    ("coalstone", 6, (29, 30, 34), (86, 87, 98), "veins", 71),
    ("copperstone", 7, (92, 83, 74), (234, 125, 62), "veins", 83),
    ("ironstone", 8, (82, 87, 90), (215, 203, 176), "veins", 97),
    ("workbench", 9, (144, 94, 48), (231, 183, 101), "grid", 101),
    ("torchbud", 10, (61, 82, 49), (255, 191, 51), "glow", 109),
    ("storage_crate", 11, (116, 77, 41), (205, 144, 70), "crate", 127),
]


ITEMS = [
    ("timber_chunk", (168, 101, 45), (229, 163, 83), "log"),
    ("slate_shard", (83, 93, 105), (173, 184, 196), "shard"),
    ("copper_nugget", (118, 76, 48), (241, 135, 70), "nugget"),
    ("iron_nugget", (89, 91, 94), (223, 216, 194), "nugget"),
    ("workbench_kit", (137, 91, 48), (237, 190, 107), "kit"),
    ("crate_kit", (119, 82, 46), (214, 151, 74), "kit"),
    ("chipper_tool", (113, 76, 48), (195, 217, 210), "tool"),
    ("pick_tool", (89, 91, 99), (220, 221, 204), "pick"),
]


UI_SPRITES = [
    ("hotbar_frame", 128, 24, (34, 39, 48, 168), (104, 191, 147, 235), "frame"),
    ("selected_slot", 24, 24, (42, 48, 58, 70), (255, 206, 83, 255), "selected"),
    ("health_pip", 16, 16, (98, 38, 56, 0), (239, 84, 93, 255), "pip"),
    ("inventory_panel", 96, 64, (35, 43, 54, 218), (105, 191, 148, 245), "panel"),
    ("crafting_panel", 96, 64, (47, 41, 56, 218), (231, 183, 101, 245), "panel"),
    ("multiplayer_status_badge", 48, 20, (33, 41, 52, 190), (93, 177, 222, 250), "badge"),
]


META_GUIDS = {
    ATLAS_PATH: "90a6fc9b496045d7ad07d8e02954ce10",
    "Assets/Blockiverse/Art/Textures/Blocks": "f6658262dc8e4ddfb830252033f0a3c4",
    BLOCK_SOURCE_DIR: "5a7112ded78d4dcdb752cab899a42bd0",
    ITEM_DIR: "5ffb1d25fb834582a8f9dfe90a1e8f9c",
    "Assets/Blockiverse/Art/Sprites": "6df33f05aaad4bd3842acb84f6e3b257",
    UI_DIR: "8d31bf8a0efa46d0a3f33813b3353db2",
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

            if color is not None:
                image[y][x] = with_alpha(color, 255)
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
    ]:
        os.makedirs(os.path.join(ROOT, folder), exist_ok=True)
        write_folder_meta(folder)

    atlas = empty_image(64, 64, (58, 60, 65, 255))

    for block in BLOCKS:
        name, tile_index, *_ = block
        tile = make_block_tile(block)
        write_png(f"{BLOCK_SOURCE_DIR}/{name}.png", tile)
        write_texture_meta(f"{BLOCK_SOURCE_DIR}/{name}.png", sprite=False, max_size=16)
        tile_x = tile_index % 4
        tile_y = tile_index // 4
        origin_x = tile_x * 16
        origin_y = tile_y * 16
        for y in range(16):
            for x in range(16):
                atlas[origin_y + y][origin_x + x] = tile[y][x]

    write_png(ATLAS_PATH, atlas)
    write_texture_meta(ATLAS_PATH, sprite=False, max_size=64)

    for item in ITEMS:
        name = item[0]
        write_png(f"{ITEM_DIR}/{name}.png", make_item_icon(item))
        write_texture_meta(f"{ITEM_DIR}/{name}.png", sprite=True, max_size=64)

    for sprite in UI_SPRITES:
        name = sprite[0]
        write_png(f"{UI_DIR}/{name}.png", make_ui_sprite(sprite))
        write_texture_meta(f"{UI_DIR}/{name}.png", sprite=True, max_size=128)


if __name__ == "__main__":
    write_assets()
