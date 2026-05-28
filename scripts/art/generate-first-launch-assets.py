#!/usr/bin/env python3
import math
import os
import struct
import zlib


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BRANDING_DIR = "Assets/Blockiverse/Art/Sprites/Branding"
APP_ICON_PATH = f"{BRANDING_DIR}/blockiverse_app_icon.png"
LAUNCH_ART_PATH = f"{BRANDING_DIR}/blockiverse_launch_landscape.png"
ANDROID_LIBRARY_DIR = "Assets/Plugins/Android/BlockiverseBranding.androidlib"
ANDROID_MANIFEST_PATH = f"{ANDROID_LIBRARY_DIR}/AndroidManifest.xml"
ANDROID_GRADLE_PATH = f"{ANDROID_LIBRARY_DIR}/build.gradle"
ANDROID_RES_DIR = f"{ANDROID_LIBRARY_DIR}/res"
ANDROID_VALUES_DIR = f"{ANDROID_RES_DIR}/values"
ANDROID_STRINGS_PATH = f"{ANDROID_VALUES_DIR}/strings.xml"

META_GUIDS = {
    BRANDING_DIR: "c226a9d3f04c4f458a81934051013f9e",
    APP_ICON_PATH: "a7184a73e67f4e688ee627ea6a34c6d9",
    LAUNCH_ART_PATH: "6e4cf5e26f6546f0b967f7ea13af3f7c",
    ANDROID_LIBRARY_DIR: "c09d8e7d2cbb4fb79f22b6f2dd0a2752",
    ANDROID_MANIFEST_PATH: "897410b6efad4bb68d8c70a42704d399",
    ANDROID_GRADLE_PATH: "0ce7101eaa524377b56ea116feebc54a",
    ANDROID_RES_DIR: "2905a0a08949423d92ad23a5fe19d03c",
    ANDROID_VALUES_DIR: "b07d2e72e62142c1a7c5905d7dba0c2d",
    ANDROID_STRINGS_PATH: "3a7b6efc472c4d61b7fb685e03cf7894",
}


def clamp(value):
    return max(0, min(255, int(round(value))))


def shade(color, factor):
    return tuple(clamp(channel * factor) for channel in color)


def mix(a, b, factor):
    return tuple(clamp(a[index] * (1.0 - factor) + b[index] * factor) for index in range(len(a)))


def empty_image(width, height, color=(0, 0, 0, 0)):
    return [[color for _ in range(width)] for _ in range(height)]


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
            "fileFormatVersion: 2\n"
            f"guid: {guid_for(relative_path)}\n"
            "folderAsset: yes\n"
            "DefaultImporter:\n"
            "  externalObjects: {}\n"
            "  userData:\n"
            "  assetBundleName:\n"
            "  assetBundleVariant:\n"
        )


def write_default_meta(relative_path):
    with open(os.path.join(ROOT, f"{relative_path}.meta"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(
            "fileFormatVersion: 2\n"
            f"guid: {guid_for(relative_path)}\n"
            "DefaultImporter:\n"
            "  externalObjects: {}\n"
            "  userData:\n"
            "  assetBundleName:\n"
            "  assetBundleVariant:\n"
        )


def write_texture_meta(relative_path, max_size, sprite=False):
    texture_type = 8 if sprite else 0
    sprite_mode = 1 if sprite else 0
    pixels_per_unit = 100 if not sprite else 100

    with open(os.path.join(ROOT, f"{relative_path}.meta"), "w", encoding="utf-8", newline="\n") as handle:
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
            "  alphaIsTransparency: 0\n"
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
            "    weights:\n"
            "    secondaryTextures: []\n"
            "    nameFileIdTable: {}\n"
            "  mipmapLimitGroupName:\n"
            "  pSDRemoveMatte: 0\n"
            "  userData:\n"
            "  assetBundleName:\n"
            "  assetBundleVariant:\n"
        )


def make_icon(size):
    image = empty_image(size, size, (14, 23, 28, 255))
    center = (size - 1) / 2.0
    radius = size * 0.46

    for y in range(size):
        for x in range(size):
            dx = x - center
            dy = y - center
            distance = math.sqrt(dx * dx + dy * dy)

            if distance > radius:
                image[y][x] = (0, 0, 0, 0)
                continue

            sky_factor = max(0.0, min(1.0, y / size))
            base = mix((34, 88, 105), (18, 33, 38), sky_factor)
            image[y][x] = base + (255,)

    block = size // 7
    origin_x = size // 2 - block * 2
    origin_y = size // 2 - block
    colors = [
        (78, 164, 87),
        (118, 83, 50),
        (93, 151, 77),
        (47, 121, 78),
        (232, 181, 72),
        (87, 102, 116),
        (82, 188, 214),
        (132, 92, 53),
        (215, 151, 76),
    ]

    for row in range(3):
        for column in range(3):
            color = colors[row * 3 + column]
            x0 = origin_x + column * block
            y0 = origin_y + row * block
            for y in range(y0, y0 + block - 2):
                for x in range(x0, x0 + block - 2):
                    if 0 <= x < size and 0 <= y < size:
                        edge = 0.72 if x in (x0, x0 + block - 3) or y in (y0, y0 + block - 3) else 1.0
                        image[y][x] = shade(color, edge) + (255,)

    # Readable block-font B mark made from original rectangles.
    mark_color = (245, 247, 231)
    shadow_color = (6, 16, 20)
    segments = [
        (size * 31 // 100, size * 24 // 100, size * 10 // 100, size * 52 // 100),
        (size * 41 // 100, size * 24 // 100, size * 25 // 100, size * 10 // 100),
        (size * 41 // 100, size * 45 // 100, size * 23 // 100, size * 10 // 100),
        (size * 41 // 100, size * 66 // 100, size * 27 // 100, size * 10 // 100),
        (size * 62 // 100, size * 34 // 100, size * 10 // 100, size * 11 // 100),
        (size * 64 // 100, size * 55 // 100, size * 10 // 100, size * 11 // 100),
    ]

    for color, offset in [(shadow_color, size // 60), (mark_color, 0)]:
        for x0, y0, width, height in segments:
            for y in range(y0 + offset, y0 + height + offset):
                for x in range(x0 + offset, x0 + width + offset):
                    if 0 <= x < size and 0 <= y < size:
                        image[y][x] = color + (255,)

    return image


def make_fallback_launch_art():
    width = 1280
    height = 720
    image = empty_image(width, height, (95, 183, 219, 255))

    for y in range(height):
        t = y / height
        color = mix((104, 190, 225), (236, 213, 148), t * 0.72)
        for x in range(width):
            image[y][x] = color + (255,)

    horizon = 410
    for z in range(9):
        y_base = horizon + z * 28
        shade_factor = 0.86 + z * 0.025
        for y in range(y_base, min(height, y_base + 34)):
            for x in range(width):
                if (x // 38 + z) % 3 != 0:
                    color = shade((75, 154, 72), shade_factor)
                else:
                    color = shade((131, 102, 63), shade_factor)
                image[y][x] = color + (255,)

    for mountain_x, mountain_y, mountain_w, color in [
        (120, 210, 310, (88, 124, 137)),
        (760, 170, 360, (100, 132, 141)),
        (520, 245, 220, (84, 126, 118)),
    ]:
        for y in range(mountain_y, horizon + 20):
            half = (horizon + 20 - y) * mountain_w / (horizon + 20 - mountain_y) / 2
            start_x = max(0, int(round(mountain_x - half)))
            end_x = min(width, int(round(mountain_x + half)))
            for x in range(start_x, end_x):
                image[y][x] = shade(color, 0.92 + (y - mountain_y) / height) + (255,)

    for x0, y0 in [(112, 452), (188, 488), (975, 438), (1052, 478)]:
        for y in range(y0 - 80, y0):
            for x in range(x0 - 48, x0 + 48):
                if abs(x - x0) + abs(y - (y0 - 40)) < 78:
                    image[y][x] = (70, 142, 67, 255)
        for y in range(y0, y0 + 84):
            for x in range(x0 - 16, x0 + 16):
                image[y][x] = (127, 84, 45, 255)

    return image


def write_assets():
    folders = [
        BRANDING_DIR,
        ANDROID_LIBRARY_DIR,
        ANDROID_RES_DIR,
        ANDROID_VALUES_DIR,
    ]

    for folder in folders:
        os.makedirs(os.path.join(ROOT, folder), exist_ok=True)
        write_folder_meta(folder)

    write_png(APP_ICON_PATH, make_icon(512))
    write_texture_meta(APP_ICON_PATH, 512, sprite=False)

    if not os.path.exists(os.path.join(ROOT, LAUNCH_ART_PATH)):
        write_png(LAUNCH_ART_PATH, make_fallback_launch_art())

    write_texture_meta(LAUNCH_ART_PATH, 2048, sprite=False)

    with open(os.path.join(ROOT, ANDROID_MANIFEST_PATH), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" />\n"
        )

    write_default_meta(ANDROID_MANIFEST_PATH)

    with open(os.path.join(ROOT, ANDROID_GRADLE_PATH), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(
            "apply plugin: 'com.android.library'\n\n"
            "dependencies {\n"
            "    implementation fileTree(dir: 'bin', include: ['*.jar'])\n"
            "    implementation fileTree(dir: 'libs', include: ['*.jar'])\n"
            "}\n\n"
            "android {\n"
            "    namespace 'dev.ericslutz.blockiversevr.branding'\n"
            "    compileSdk 34\n"
            "    buildToolsVersion = '36.0.0'\n\n"
            "    defaultConfig {\n"
            "        minSdk 32\n"
            "        targetSdk 34\n"
            "    }\n\n"
            "    lint {\n"
            "        abortOnError false\n"
            "    }\n\n"
            "    sourceSets {\n"
            "        main {\n"
            "            manifest.srcFile 'AndroidManifest.xml'\n"
            "            res.srcDirs = ['res']\n"
            "            assets.srcDirs = ['assets']\n"
            "            jniLibs.srcDirs = ['libs']\n"
            "        }\n"
            "    }\n"
            "}\n"
        )

    write_default_meta(ANDROID_GRADLE_PATH)

    with open(os.path.join(ROOT, ANDROID_STRINGS_PATH), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            "<resources>\n"
            "    <string name=\"app_name\">Blockiverse VR</string>\n"
            "    <string name=\"game_view_content_description\">Blockiverse VR</string>\n"
            "</resources>\n"
        )

    write_default_meta(ANDROID_STRINGS_PATH)


if __name__ == "__main__":
    write_assets()
