#!/usr/bin/env python3
"""Author `audio-manifest.json`, the cue -> source mapping the audio build reads.

    python3 scripts/audio/make-audio-manifest.py

The manifest is generated rather than hand-written because most of it is regular:
nine block families each need a break and a place cue, seven walkable surfaces
each need four footstep variants, and so on. Writing that by hand invites the
kind of copy-paste drift that only shows up as one silent cue in the headset.

Source in-points come from measured onsets, not guesses — see
`docs/audio/audio-asset-manifest.md` for the provenance table this feeds.
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "audio-manifest.json")

# ── Licenses ────────────────────────────────────────────────────────────────
LICENSES = {
    "sonniss": {
        "name": "Sonniss #GameAudioGDC Bundle Licensing Agreement",
        "url": "https://sonniss.com/gdc-bundle-license/",
        "attribution_required": False,
        "commercial_use": True,
        "modification": True,
        "restrictions": "No resale as-is; no authorship claim on the original recordings; "
                        "no use for training artificial intelligence technologies.",
    },
    "cc0": {
        "name": "Creative Commons Zero 1.0 Universal",
        "url": "https://creativecommons.org/publicdomain/zero/1.0/",
        "attribution_required": False,
        "commercial_use": True,
        "modification": True,
        "restrictions": "None.",
    },
}

PACKS = {
    "kenney_interface": ("cc0", "Kenney — Interface Sounds", "extract/kenney_interface-sounds/Audio"),
    "kenney_ui": ("cc0", "Kenney — UI Audio", "extract/kenney_ui-audio/Audio"),
    "kenney_impact": ("cc0", "Kenney — Impact Sounds", "extract/kenney_impact-sounds/Audio"),
    "kenney_rpg": ("cc0", "Kenney — RPG Audio", "extract/kenney_rpg-audio/Audio"),
    "oga_steps": ("cc0", "OpenGameArt — Different steps on wood, stone, leaves, gravel and mud (TinyWorlds)",
                  "extract/oga_tinyworlds_steps"),
    "oga_snow": ("cc0", "OpenGameArt — 42 Snow and Gravel Footsteps (Corsica_S / Iwan Gabovitch)",
                 "extract/oga_snow_gravel/Corsica_S-Walking_in_Snow"),
    "s2019": ("sonniss", "Sonniss GDC 2019 Game Audio Bundle", "raw/sonniss2019"),
    "s2020": ("sonniss", "Sonniss GDC 2020 Game Audio Bundle", "raw/sonniss2020"),
    "s2026": ("sonniss", "Sonniss GDC 2026 Game Audio Bundle", "raw/sonniss2026"),
}

# Long Sonniss filenames, aliased so the cue table below stays readable.
S = {
    "ice_snap":    ("s2026", "Alexander Kopeikin - 100 kHz Designed Ice__ice, crack, ice block snapping-001.flac"),
    "ice_fissure": ("s2026", "Alexander Kopeikin - 100 kHz Designed Ice__ice, surface cracking, fissure, fast, hard-003.flac"),
    "ice_crush":   ("s2026", "Alexander Kopeikin - 100 kHz Designed Ice__ice, block of ice crushed, heavy-015.flac"),
    "ui_accept":   ("s2026", "Cinematic Sound Design - UI Interaction Elements__Accept Boing Crunch.flac"),
    "ui_deny":     ("s2026", "Cinematic Sound Design - UI Interaction Elements__Deny Muted.flac"),
    "ui_ting":     ("s2026", "Cinematic Sound Design - UI Interaction Elements__Ting Coins.flac"),
    "ui_denydark": ("s2026", "Cinematic Sound Design - System & UI Feedback Elements__Interface Deny Low Fat Dark.flac"),
    "metal_impact":("s2026", "Epic Stock Media - HD Game Materials__METLImpt_Metal Old File Impact Tap Against Tire Iron Metallic Hit 01_ESM_HDGM.flac"),
    "storm_texas": ("s2026", "Epic Stock Media - Public Spaces - Storms Lakes Parks and Rural Nature Exteriors__STORM_Texas Rain Thunder Initial Crash Boom Storm 01 Clap Lightning_ESM_CPS.flac"),
    "cave_synth":  ("s2026", "Epic Stock Media - Strange Game Ambient Loops 3__DSGNSynth_Dark Loop Mystic Forest Tonal Steady Synth_ESM_SGA3.flac"),
    "night_amb":   ("s2026", "Epic Stock Media - Synthesized Nature Loops and Sounds__AMBTrop_Loop Ambience Jungle Night Humid Birds Bug Chirps 01_ESM_SNLS.flac"),
    "fire_loop":   ("s2026", "Epic Stock Media - Synthesized Nature Loops and Sounds__FIREBurn_Loop Elements Fire Crackling Crunchy Flame Burn 03_ESM_SNLS.flac"),
    "wind_loop":   ("s2026", "Epic Stock Media - Synthesized Nature Loops and Sounds__WINDInt_Loop Weather Wind Whipping Constricted Flow Turbulent 01_ESM_SNLS.flac"),
    "campfire":    ("s2026", "Ivo Vicic - Campfire - Bonfire FX__24 Campfire, Dropping Fresh Pine Branches in Fire, Crackling, Sizzling Strong, Close 02.flac"),
    "thunder_int": ("s2026", "Jake Fielding - Interior Wind Rain and Storms__THUN_Interior Thunder Rumble_JF_INT Storm_01.flac"),
    "meadow_amb":  ("s2026", "Just Sound Effects - Highlands of Norway__AMBSwmp_Meadow Pipits calling many Insects humming Wind blowing through Grass_JSE_HoN_Stereo.flac"),
    "panting":     ("s2026", "SoundBits - Vox Hominis - Human Effort Voices__HMNBrth_Panting Male 02 04_SNDBTS_VH.flac"),

    "breathe":     ("s2020", "Articulated Sounds - Human Male Breathe__HUMAN BREATH Male_Deep Mouth Inhale and Powerful Strong Exhale with Acceleration_A.flac"),
    "rain_heavy":  ("s2020", "Articulated Sounds - Moody Rain Loops__WEATHER Rain, Large, Camping Tent near Sea, Isle-au-Coudre, Canada, LOOP.flac"),
    "wood_crack":  ("s2020", "Bluezone - Forest Creature Sound Effects__Bluezone_BC0269_creature_wood_texture_crack_heavy_rumble_006.flac"),
    "snow_walk":   ("s2020", "Bluezone - Snow Footsteps__Bluezone_BC0264_snow_footsteps_walk_013.flac"),
    "snow_deep":   ("s2020", "Bluezone - Snow Footsteps__Bluezone_BC0264_snow_footsteps_walk_deep_008.flac"),
    "snow_light":  ("s2020", "Bluezone - Snow Footsteps__Bluezone_BC0264_snow_footsteps_walk_light_008.flac"),
    "splash_big":  ("s2020", "Bluezone - Splash__Bluezone_BC0256_water_splash_008.flac"),
    "splash_small":("s2020", "Bluezone - Splash__Bluezone_BC0256_water_splash_small_008.flac"),
    "splash_drop": ("s2020", "Bluezone - Splash__Bluezone_BC0256_water_splash_drop_006.flac"),
    "rock_big":    ("s2020", "PMSFX - Rocky Impacts__PM_RI_Designed_7 Rocks Impact Hit Big LFE Heavy Designed.flac"),
    "rock_53":     ("s2020", "PMSFX - Rocky Impacts__PM_RI_Source_53 Rocks Impact Hit Single Stone.flac"),
    "rock_92":     ("s2020", "PMSFX - Rocky Impacts__PM_RI_Source_92 Rocks Impact Hit Single Stone.flac"),
    "grass_113":   ("s2020", "PMSFX - STEPS Dry Grass & Shrubs__PM_SDGS_113 Footstep Step Dry Grass Shrubs Pine Needles Meadow .flac"),
    "grass_14":    ("s2020", "PMSFX - STEPS Dry Grass & Shrubs__PM_SDGS_14 Footstep Step Dry Grass Shrubs Pine Needles Meadow .flac"),
    "grass_186":   ("s2020", "PMSFX - STEPS Dry Grass & Shrubs__PM_SDGS_186 Footstep Step Dry Grass Shrubs Pine Needles Meadow .flac"),
    "brick_16":    ("s2020", "PMSFX - Shattering Bricks__PM_SB_SOURCE_16 Impact brick rock dirt gravel single hit.flac"),
    "brick_61":    ("s2020", "PMSFX - Shattering Bricks__PM_SB_SOURCE_61 Impact brick rock dirt gravel single hit.flac"),
    "chest_close": ("s2020", "SoundFxwizard - Closets Small Doors__CST_Chest_Ancient_Close2_SFXWiz.flac"),
    "drawer_open": ("s2020", "SoundFxwizard - Closets Small Doors__CST_Cutlery_Drawer_Open_Medium_SFXWiz.flac"),
    "rain_light":  ("s2020", "Soundholder - Rain & Thunder__ambient rain light washed out stereo MS.flac"),
    "waterflow":   ("s2020", "Systematic-Sound - Sounds Of Nature – Flowing Water 01__AMB WATERFLOW Stream Splash 2m Close M.flac"),
    "debris_fall": ("s2020", "Tatak Audio - Rocks and Debris__Tatak_ROCKS_Falling Heavy Dusty Debris.flac"),
    "pebbles":     ("s2020", "Tatak Audio - Rocks and Debris__Tatak_ROCKS_Pebbles Dust Tumbling Rolling Debris.flac"),
    "rock_stones": ("s2020", "Tatak Audio - Rocks and Debris__Tatak_ROCKS_Single Rock Impact on Stones.flac"),
    "puddle":      ("s2020", "Wav Junction Sound Effects - Footsteps__0014_Footsteps_water_puddle_single_splashes.flac"),
    "pour_bowl":   ("s2020", "Wav Junction Sound Effects - Glassware__0026_Pouring_Liquid_in_to_bowl_5.flac"),
    "shovel_dirt": ("s2020", "Wav Junction Sound Effects - Tools__0026_Shovel_Scraping_Dirt.flac"),

    "glass_break": ("s2019", "Airborne Sound - Elements Glass__Glass,Plate Glass,Thick,Break,Topple,Schoeps.flac"),
    "cave_amb":    ("s2019", "Articulated Sounds - Ghosts Return__ATMO EERIE Cave, Water Drips, Emptyness, Howling Interior Wind, Oppressive, LOOP.flac"),
    "wood_creak":  ("s2019", "BlueZone - Wood Sound Effects__Bluezone_BC0254_wood_creaking_tree_005.flac"),
    "sip":         ("s2019", "Eiravaein Works - WakeyWakey__WakeyWakey,tea,gaiwan,ceramic,glazed,thick,3piece,cup,full,drink,sipping,slurp,swallow,mouthclicks,alt5.flac"),
    "crystal":     ("s2019", "Impact Soundworks - Super FX 8 and 16-bit Video Game SFX__Misc_Glass_Crystal_Shatter.flac"),
    "dirt_dig":    ("s2019", "Matt Script - You Me & Debris__impact_gritty_grit_sandy_dirt_dig_shovel_shingle_roof_02.flac"),
    "wood_debris": ("s2019", "Matt Script - You Me & Debris__impact_wood_debris_fall_hit_02.flac"),
    "wood_snap":   ("s2019", "Matt Script - You Me & Debris__wood_breaking_cracking_snapping_breaking_peeling_bones_break_snap_crack_13.flac"),
    "wood_break":  ("s2019", "Rock The Speakerbox - Broken__BROKEN - DESIGNED - WOOD Break Small.flac"),
    "pour_water":  ("s2019", "Sound Spark LLC – Forge Sound Design Toolkit__Water_Pouring_02.flac"),
    "drawer_wood_close": ("s2019", "Soundopolis - Household Doors HD__Nightstand_Modern_Drawers_Wood_Close_x6_Fienup_001.flac"),
    "drawer_wood_open":  ("s2019", "Soundopolis - Household Doors HD__Nightstand_Old_Drawers_Wood_Open_x11_Fienup_001.flac"),
}


def src(alias):
    pack, name = S[alias]
    return f"{PACKS[pack][2]}/{name}", pack


def k(pack, filename):
    return f"{PACKS[pack][2]}/{filename}", pack


CUES = []


def add(cue, source, pack, start, duration, kind="oneshot", **kw):
    entry = {"cue": cue, "source": source, "pack": pack, "license": PACKS[pack][0],
             "start": round(start, 4), "duration": round(duration, 4), "kind": kind}
    entry.update(kw)
    CUES.append(entry)


def add_alias(cue, alias, start, duration, kind="oneshot", **kw):
    source, pack = src(alias)
    add(cue, source, pack, start, duration, kind, **kw)


def add_kenney(cue, pack, filename, start, duration, **kw):
    source, p = k(pack, filename)
    add(cue, source, p, start, duration, "oneshot", **kw)


# ── Footsteps: 7 surfaces x 4 variants ──────────────────────────────────────
# Kenney's impact pack ships surface-labelled footsteps, which is exactly the
# axis the ruleset asks for; Sonniss and OGA fill the surfaces it lacks.
for i, f in enumerate(["footstep_grass_000", "footstep_grass_001",
                       "footstep_grass_002", "footstep_grass_003"], 1):
    add_kenney(f"footstep_soil_{i:02d}", "kenney_impact", f"{f}.ogg", 0, 0.30, gain_db=-2)
for i, f in enumerate(["footstep_concrete_000", "footstep_concrete_001",
                       "footstep_concrete_002", "footstep_concrete_003"], 1):
    add_kenney(f"footstep_stone_{i:02d}", "kenney_impact", f"{f}.ogg", 0, 0.30, gain_db=-2)
for i, f in enumerate(["footstep_wood_000", "footstep_wood_001",
                       "footstep_wood_002", "footstep_wood_003"], 1):
    add_kenney(f"footstep_wood_{i:02d}", "kenney_impact", f"{f}.ogg", 0, 0.30, gain_db=-2)
for i, f in enumerate(["footstep_snow_000", "footstep_snow_001",
                       "footstep_snow_002", "footstep_snow_003"], 1):
    add_kenney(f"footstep_snow_{i:02d}", "kenney_impact", f"{f}.ogg", 0, 0.32, gain_db=-2)
# Gravel/sand: granular crunch. PMSFX dry-grass steps read as loose grit; the
# OGA gravel take adds a coarser fourth variant.
add_alias("footstep_gravelsand_01", "grass_113", 0.05, 0.28, gain_db=-2)
add_alias("footstep_gravelsand_02", "grass_14", 0.06, 0.28, gain_db=-2)
add_alias("footstep_gravelsand_03", "grass_186", 0.02, 0.26, gain_db=-2)
add_kenney("footstep_gravelsand_04", "oga_steps", "gravel.ogg", 0, 0.30, gain_db=-2)
# Leaf: soft rustle, no impact transient.
add_kenney("footstep_leaf_01", "oga_steps", "leaves01.ogg", 0, 0.32, gain_db=-3)
add_kenney("footstep_leaf_02", "oga_steps", "leaves02.ogg", 0, 0.32, gain_db=-3)
add_kenney("footstep_leaf_03", "oga_steps", "mud02.ogg", 0, 0.30, gain_db=-3)
add_kenney("footstep_leaf_04", "kenney_impact", "footstep_grass_004.ogg", 0, 0.30, gain_db=-4)
# Water: single puddle splashes, taken from measured onsets in a 29-take file.
for i, t in enumerate([0.0, 1.06, 2.10, 3.22], 1):
    add_alias(f"footstep_water_{i:02d}", "puddle", t, 0.34, gain_db=-2)

# ── Block break/place per material family ───────────────────────────────────
BREAK = {
    "soil":       ("dirt_dig", 0.02, 0.34, 0),
    "stone":      ("rock_53", 0.0, 0.42, 0),
    "gravelsand": ("brick_16", 0.0, 0.40, 0),
    "wood":       ("wood_snap", 0.0, 0.38, 0),
    "leaf":       ("grass_14", 0.06, 0.30, -3),
    "glass":      ("glass_break", 0.0, 0.45, 0),
    "crystal":    ("crystal", 0.0, 0.45, 0),
    # A rock impact reads as plain stone; ore needs an audible metallic ring so
    # the player can tell they just broke something worth having.
    "oremetal":   ("metal_impact", 0.0, 0.42, 0),
    "snow":       ("snow_deep", 0.0, 0.36, 0),
}
for family, (alias, start, dur, gain) in BREAK.items():
    add_alias(f"block_break_{family}", alias, start, dur, gain_db=gain)

# Placement is the same material arriving rather than breaking: shorter, softer,
# no debris tail. Kenney's graded impact set gives a consistent family of thumps.
PLACE = {
    "soil":       ("kenney_impact", "impactSoft_medium_000.ogg", 0.24, -1),
    "stone":      ("kenney_impact", "impactPlate_light_000.ogg", 0.24, -1),
    "gravelsand": ("kenney_impact", "impactSoft_heavy_001.ogg", 0.24, -1),
    "wood":       ("kenney_impact", "impactWood_light_000.ogg", 0.24, -1),
    "leaf":       ("kenney_impact", "impactSoft_medium_002.ogg", 0.24, -4),
    "glass":      ("kenney_impact", "impactGlass_light_000.ogg", 0.24, -2),
    "crystal":    ("kenney_impact", "impactBell_heavy_002.ogg", 0.28, -3),
    "oremetal":   ("kenney_impact", "impactMetal_light_000.ogg", 0.26, -2),
    "snow":       ("kenney_impact", "footstep_snow_004.ogg", 0.26, -1),
}
for family, (pack, filename, dur, gain) in PLACE.items():
    add_kenney(f"block_place_{family}", pack, filename, 0, dur, gain_db=gain)

# Generic fallbacks for a block with no family mapping.
add_kenney("block_break", "kenney_impact", "impactMining_000.ogg", 0, 0.34)
add_kenney("block_place", "kenney_impact", "impactGeneric_light_000.ogg", 0, 0.22, gain_db=-1)

# ── Tool contact (3 variants each; mining repeats on a cadence) ─────────────
for i, f in enumerate(["impactMining_001", "impactMining_002", "impactMining_003"], 1):
    add_kenney(f"tool_hit_stone_{i:02d}", "kenney_impact", f"{f}.ogg", 0, 0.30)
add_alias("tool_hit_soft_01", "grass_113", 0.05, 0.26, gain_db=-1)
add_kenney("tool_hit_soft_02", "kenney_impact", "impactSoft_medium_003.ogg", 0, 0.26)
add_kenney("tool_hit_soft_03", "kenney_impact", "impactSoft_heavy_003.ogg", 0, 0.26, gain_db=-2)
add_alias("tool_wrong", "ui_denydark", 0.0, 0.34, gain_db=-2)

# ── UI ──────────────────────────────────────────────────────────────────────
add_kenney("ui_select", "kenney_interface", "select_004.ogg", 0, 0.16, gain_db=-3)
add_kenney("ui_confirm", "kenney_interface", "confirmation_002.ogg", 0, 0.34)
add_kenney("ui_cancel", "kenney_interface", "back_002.ogg", 0, 0.30)
add_kenney("inventory_open", "kenney_interface", "open_001.ogg", 0, 0.36)
add_kenney("inventory_close", "kenney_interface", "close_002.ogg", 0, 0.32)
add_alias("craft_success", "ui_accept", 0.0, 0.55)
add_alias("craft_fail", "ui_deny", 0.0, 0.45, gain_db=-2)
add_alias("pickup_item", "ui_ting", 0.0, 0.40, gain_db=-2)
add_kenney("multiplayer_join", "kenney_interface", "maximize_006.ogg", 0, 0.45)
add_kenney("multiplayer_leave", "kenney_interface", "minimize_006.ogg", 0, 0.45)

# ── Containers ──────────────────────────────────────────────────────────────
add_alias("container_open", "drawer_wood_open", 0.30, 0.50)
add_alias("container_close", "drawer_wood_close", 0.25, 0.45)

# ── Fire ────────────────────────────────────────────────────────────────────
add_alias("torch_ignite", "campfire", 2.10, 0.55)
add_alias("torch_loop", "fire_loop", 0.5, 9.0, kind="bed", channels=1, crossfade=1.2, gain_db=-6)
# 70 s in is steady crackle. Earlier windows are the branch-dropping section,
# whose 36 dB crest factor forces the loudness pass to clamp for headroom and
# lands the bed ~8 dB under every other one.
add_alias("campfire_loop", "campfire", 70.0, 14.0, kind="bed", crossfade=2.0, gain_db=-3)

# ── Weather ─────────────────────────────────────────────────────────────────
add_alias("rain_light_loop", "rain_light", 6.0, 20.0, kind="bed", crossfade=2.5, gain_db=-3)
add_alias("rain_heavy_loop", "rain_heavy", 8.0, 20.0, kind="bed", crossfade=2.5, gain_db=-2)
add_alias("snow_wind_loop", "wind_loop", 0.3, 6.0, kind="bed", crossfade=1.2, gain_db=-4)
add_alias("thunder_near", "storm_texas", 3.6, 3.20, channels=2, gain_db=-1, fade_out=0.35)
add_alias("thunder_far", "thunder_int", 0.4, 3.60, channels=2, gain_db=-4, fade_out=0.5)

# ── Ambience ────────────────────────────────────────────────────────────────
add_alias("day_ambience_loop", "meadow_amb", 10.0, 24.0, kind="bed", crossfade=3.0, gain_db=-5)
add_alias("night_ambience_loop", "night_amb", 5.0, 24.0, kind="bed", crossfade=3.0, gain_db=-6)
add_alias("cave_ambience_loop", "cave_amb", 12.0, 22.0, kind="bed", crossfade=3.0, gain_db=-5)

# ── Player vitals ───────────────────────────────────────────────────────────
add_alias("player_hurt", "panting", 0.55, 0.55, gain_db=-1)
add_alias("low_health", "breathe", 1.20, 1.60, gain_db=-3)
add_alias("player_death", "breathe", 7.50, 2.20, gain_db=-2, semitones=-2, fade_out=0.6)

# ── Water and fluids ────────────────────────────────────────────────────────
add_alias("water_splash", "splash_big", 0.0, 1.10)
add_alias("swim_stroke", "splash_small", 0.0, 0.70, gain_db=-3)
add_alias("water_scoop", "pour_bowl", 0.20, 0.80, gain_db=-2)
add_alias("submerged_loop", "waterflow", 30.0, 12.0, kind="bed", channels=1,
          crossfade=2.0, gain_db=-8, semitones=-5)
# Emberflow is lava: the same fire bed dropped well below its recorded register
# so it reads as molten rather than as a campfire.
add_alias("emberflow_loop", "fire_loop", 1.0, 8.0, kind="bed", channels=1,
          crossfade=1.5, gain_db=-5, semitones=-7)

# ── Consumption ─────────────────────────────────────────────────────────────
# `eat` is a dry organic crunch pitched down into mouth register — there is no
# clean chew recording in any of the licensed packs, and a pitched grass-step
# take reads far more like a bite than any of the foley alternatives.
add_alias("eat", "grass_186", 0.02, 0.42, gain_db=-2, semitones=-6)
add_alias("drink", "sip", 0.35, 0.90, gain_db=-1)

# ── Landing ─────────────────────────────────────────────────────────────────
add_kenney("landing", "kenney_impact", "impactSoft_heavy_000.ogg", 0, 0.34)


def main():
    packs = {pid: {"name": meta[1], "license": meta[0], "path": meta[2]}
             for pid, meta in PACKS.items()}
    manifest = {
        "$comment": "Generated by scripts/audio/make-audio-manifest.py — do not hand-edit. "
                    "Consumed by build-audio-assets.py and validate-audio-assets.py.",
        "licenses": LICENSES,
        "packs": packs,
        "cues": CUES,
    }
    with open(OUT, "w", newline="\n") as handle:
        json.dump(manifest, handle, indent=1)
        handle.write("\n")
    beds = sum(1 for c in CUES if c["kind"] == "bed")
    print(f"wrote {OUT}")
    print(f"  {len(CUES)} cues ({beds} beds, {len(CUES) - beds} one-shots)")
    names = [c["cue"] for c in CUES]
    dupes = {n for n in names if names.count(n) > 1}
    if dupes:
        raise SystemExit(f"duplicate cue names: {sorted(dupes)}")


if __name__ == "__main__":
    main()
