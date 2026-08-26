using System;
using System.Collections.Generic;
using System.Linq;
using Blockiverse.Core;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Tables;

namespace Blockiverse.Editor
{
    // The UI Toolkit screens' localization backfill (ADR 0010 Phases 2–5).
    //
    // The uGUI menus reached the table through the bootstrapper's reverse English lookup, so
    // strings the bootstrapper hard-coded (comfort rows, LAN buttons, the controller-mapping
    // copy) never received keys. The UI Toolkit controllers render those strings through
    // UiText against the keys below; until an entry exists UiText falls back to the raw key,
    // which is exactly what this backfill exists to prevent shipping.
    //
    // The ui.value.canonical.* block deliberately populates part of a namespace ADR 0011 left
    // to the humanize fallback: every English value is byte-identical to the fallback's
    // output, so uGUI behaviour (table-first, then humanize) is unchanged and the strings
    // become genuinely translatable. This is the ADR's deferred "registry display names"
    // seam landing with the screens that consume it.
    //
    // Idempotent: existing entries are updated in place, Smart stays off (no arguments in any
    // of these), and re-runs are byte-stable because entries are written in key order.
    public static class BlockiverseUiToolkitTableBackfill
    {
        const string TableName = "UI";

        // key → (english, translator comment)
        static readonly (string key, string english, string comment)[] Entries =
        {
            ("ui.action.lan.host", "Host",
                "LAN multiplayer screen: button that starts hosting a LAN session (verb, imperative). The uGUI panel hard-coded this label unlocalized."),
            ("ui.action.lan.join", "Join",
                "LAN multiplayer screen: button that joins the LAN session at the entered address (verb, imperative). The uGUI panel hard-coded this label unlocalized."),
            ("ui.action.lan.stop", "Stop",
                "LAN multiplayer screen: button that stops the current hosted or joined LAN session (verb, imperative). The uGUI panel hard-coded this label unlocalized."),
            ("ui.action.pause.screens", "Screens",
                "Pause-menu row and hub title: opens the list of gameplay screens (inventory, "
                + "crafting, shared crate, blocks). The guaranteed route to them when the wrist "
                + "menu is unavailable."),
            ("ui.generated.audio_feedback.classic_block_sounds", "Classic Block Sounds",
                "Easter-egg toggle label on the audio settings screen. Swaps block break/place cues back to the original synthesized sounds; 'Classic' as in retro/original."),
            ("ui.generated.audio_feedback.haptics", "Haptics",
                "Toggle label on the audio settings screen. Enables controller vibration feedback; noun, the haptic system as a whole."),
            ("ui.generated.audio_feedback.mute_all", "Mute All",
                "Toggle label on the audio settings screen. Silences every audio bus while on. 'Mute' is a verb-phrase acting on all sound."),
            ("ui.generated.audio_feedback.reduced_flash", "Reduced Flash",
                "Accessibility toggle label on the audio settings screen. Softens bright flashing effects such as lightning; adjective + noun describing the resulting mode."),
            ("ui.generated.audio_feedback.reduced_particles", "Reduced Particles",
                "Accessibility/comfort toggle label on the audio settings screen. Lowers particle effect density; adjective + noun describing the resulting mode."),
            ("ui.generated.comfort.control_options", "Control Options",
                "Group heading on the comfort screen for input-style options (handedness, mining input, sprint/crouch toggle modes)."),
            ("ui.generated.comfort.crouch_toggle", "Click To Toggle Crouch",
                "Toggle label: stick-click toggles crouch on/off instead of the default click-and-hold. Independent of the sprint setting."),
            ("ui.generated.comfort.glide_motion", "Glide Motion",
                "Toggle label: smooth/continuous stick locomotion mode. One half of a radio pair with Teleport."),
            ("ui.generated.comfort.left_handed", "Left-Handed",
                "Toggle label: makes the left controller the dominant hand. Off = right-handed."),
            ("ui.generated.comfort.motion_vignette", "Motion Vignette",
                "Toggle label: tunnel-vision vignette that narrows the view during locomotion to reduce motion sickness."),
            ("ui.generated.comfort.view_anchor", "View Anchor",
                "Toggle label: a small static dot at the centre of vision that gives the eye a fixed "
                + "reference during continuous locomotion, reducing motion sickness. Not an aiming "
                + "reticle — this game aims with the controller ray."),
            ("ui.generated.comfort.move_speed", "Move Speed",
                "Slider label: continuous movement speed in metres per second while gliding."),
            ("ui.generated.comfort.movement_mode", "Movement Mode",
                "Group heading on the comfort screen for the locomotion controls (glide vs teleport, move speed, head-bob)."),
            ("ui.generated.comfort.player_view", "Player View",
                "Group heading on the comfort screen for player height/body options (height reset, real-height mode)."),
            ("ui.generated.comfort.real_player_height", "Use My Real Height",
                "Toggle label: player collision and view follow their real tracked height instead of the fixed in-game player size."),
            ("ui.generated.comfort.reset_height", "Reset Height",
                "Button label: recalibrates the player's standing height to their current headset position. Imperative verb."),
            ("ui.generated.comfort.smooth_turn", "Smooth Turn",
                "Toggle label: continuous turning instead of discrete snap turns."),
            ("ui.generated.comfort.smooth_turn_speed", "Smooth Turn Speed",
                "Slider label: continuous turn speed in degrees per second (30-180)."),
            ("ui.generated.comfort.snap_turn", "Snap Turn",
                "Slider label: the angle in degrees of each discrete snap turn (15-90)."),
            ("ui.generated.comfort.sprint_toggle", "Click To Toggle Sprint",
                "Toggle label: stick-click toggles sprint on/off instead of the default click-and-hold. Independent of the crouch setting."),
            ("ui.generated.comfort.swim_climb_out", "Climb Out At Low Banks",
                "Toggle label: automatic assist that lifts a swimmer over a low shore edge instead of gravity pulling them back into the water."),
            ("ui.generated.comfort.swim_sink", "Sink When Not Swimming",
                "Toggle label: while in water and not actively swimming the player slowly sinks. Off = neutral buoyancy."),
            ("ui.generated.comfort.swim_speed", "Swim Speed",
                "Slider label: horizontal swim speed as a fraction of walking speed (0.30-1.00)."),
            ("ui.generated.comfort.swim_vignette", "Vignette While Sinking",
                "Toggle label: the motion vignette also engages during passive underwater descent, not only during driven movement."),
            ("ui.generated.comfort.teleport", "Teleport",
                "Toggle label: teleport locomotion mode (point and blink). One half of a radio pair with Glide Motion. Noun, the movement style."),
            ("ui.generated.comfort.title", "Comfort Settings",
                "Comfort settings screen title. Noun phrase heading the VR comfort/accessibility options panel."),
            ("ui.generated.comfort.toggle_to_mine", "Toggle To Mine",
                "Toggle label: mining input becomes press-once-to-start / press-again-to-stop instead of hold-to-mine."),
            ("ui.generated.comfort.turn_around", "Turn Around",
                "Toggle label: enables a quick 180-degree turn gesture (stick pulled back)."),
            ("ui.generated.comfort.turning", "Turning",
                "Group heading on the comfort screen for the turn controls (smooth turn, snap turn angle, turn-around)."),
            ("ui.generated.comfort.ui_scale", "UI Scale",
                "Slider label: size multiplier for the world-space menu panels (0.85-1.35)."),
            ("ui.generated.comfort.view_comfort", "View Comfort",
                "Group heading on the comfort screen for the anti-nausea view aids (motion vignette and its strength, UI scale)."),
            ("ui.generated.comfort.vignette_strength", "Strength",
                "Slider label directly under the Motion Vignette toggle: how strongly the vignette narrows the view (0 = open, 1 = strongest)."),
            ("ui.generated.comfort.walk_head_bob", "Walk Head-Bob",
                "Toggle label: adds a subtle vertical bobbing to glide movement to mimic walking. Off = perfectly smooth glide."),
            ("ui.generated.crafting.repair", "Repair Held Tool",
                "Crafting screen repair button label; byte-parity with the uGUI crafting panel's bootstrapper-generated button text, which has no table entry today."),
            ("ui.generated.crate.deposit", "Deposit Held",
                "CrateScreen deposit button label (deposits the whole selected-hotbar stack into the shared crate). The uGUI panel hardcoded this button text."),
            ("ui.generated.crate.title", "Shared Crate",
                "CrateScreen (station_crate) header title. The generated uGUI crate panel hardcoded this as scene text."),
            ("ui.generated.creative_tools.environment", "Environment",
                "Section heading on the Creative Tools screen above the time-of-day/day-speed sliders and cycle/weather controls."),
            ("ui.generated.creative_tools.region_operations", "Region Operations",
                "Section heading on the Creative Tools screen above the fill/replace/delete/copy/paste/undo/redo rows."),
            ("ui.generated.lan.encryption_toggle", "Encrypted (server must offer TLS)",
                "LAN multiplayer screen: label on the transport-encryption toggle. The parenthetical explains that the server must support TLS for the option to work."),
            ("ui.generated.lan.saved_servers", "Saved Servers",
                "LAN multiplayer screen: heading above the bookmark rows (servers previously joined, most recent first, rejoinable in one tap). The rows themselves are addresses/nicknames and stay unlocalized."),
            ("ui.status.blocks.none_selected", "No block",
                "Empty-hotbar selected-block line on the creative hotbar. uGUI hard-coded the literal 'No block'."),
            ("ui.toolkit.controller_map.body",
                "Dominant trigger: press UI / break\nDominant grip: place / use\nSupport grip: blocks menu\nMenu: pause\nDominant stick: snap turn\nDominant stick click: toggle block editing\nDominant primary button: jump / swim up\nDominant secondary button: crouch / swim down\nSupport stick: move\nSupport stick click: sprint\nEither stick hold up: teleport aim, release to land",
                "Canonical 11-line controller mapping copy, byte-equal to ControllerMappingText in the bootstrapper. Shared by the controller-mapping and controls screens. Keep the line structure; each line is 'control: action'."),
            ("ui.value.canonical.balanced", "Balanced",
                "New World starting-biome value label; humanize-fallback parity for canonical id 'balanced'."),
            ("ui.value.canonical.creative", "Creative",
                "New World game-mode value label; humanize-fallback parity for canonical id 'creative'."),
            ("ui.value.canonical.drybrush", "Drybrush",
                "New World starting-biome value label; humanize-fallback parity for canonical id 'drybrush'."),
            ("ui.value.canonical.dunes", "Dunes",
                "New World starting-biome value label; humanize-fallback parity for canonical id 'dunes'."),
            ("ui.value.canonical.easy", "Easy",
                "New World difficulty value label; humanize-fallback parity for canonical id 'easy'."),
            ("ui.value.canonical.flat_builder", "Flat Builder",
                "New World world-preset value label; humanize-fallback parity for canonical id 'flat_builder'."),
            ("ui.value.canonical.hard", "Hard",
                "New World difficulty value label; humanize-fallback parity for canonical id 'hard'."),
            ("ui.value.canonical.highlands", "Highlands",
                "New World starting-biome value label; humanize-fallback parity for canonical id 'highlands'."),
            ("ui.value.canonical.meadow", "Meadow",
                "New World starting-biome value label; humanize-fallback parity for canonical id 'meadow'."),
            ("ui.value.canonical.normal", "Normal",
                "New World difficulty value label; humanize-fallback parity for canonical id 'normal'."),
            ("ui.value.canonical.pinewild", "Pinewild",
                "New World starting-biome value label; humanize-fallback parity for canonical id 'pinewild'."),
            ("ui.value.canonical.survival", "Survival",
                "New World game-mode value label; humanize-fallback parity for canonical id 'survival'."),
            ("ui.value.canonical.survival_terrain", "Survival Terrain",
                "New World world-preset value label; humanize-fallback parity for canonical id 'survival_terrain'."),
            ("ui.value.canonical.tundra", "Tundra",
                "New World starting-biome value label; humanize-fallback parity for canonical id 'tundra'."),
            ("ui.value.canonical.void_builder", "Void Builder",
                "New World world-preset value label; humanize-fallback parity for canonical id 'void_builder'."),
            ("ui.value.canonical.wetland", "Wetland",
                "New World starting-biome value label; humanize-fallback parity for canonical id 'wetland'."),

            // Gameplay vitals readout. The uGUI HUD wrote these words into one concatenated
            // sentence, so they never existed as entries of their own; the metered rows need each
            // as a standalone label (ADR 0010 amendment 2026-08-25).
            ("ui.screen.vitals.health", "Health",
                "Gameplay HUD vital label, beside a meter and a number. Noun, not a verb. The label column is about 110 px wide, so a long translation clips rather than wraps."),
            ("ui.screen.vitals.hunger", "Hunger",
                "Gameplay HUD vital label. The player's need for food, not their appetite. Same 110 px width constraint as Health."),
            ("ui.screen.vitals.thirst", "Thirst",
                "Gameplay HUD vital label. The player's need for water. Same 110 px width constraint."),
            ("ui.screen.vitals.stamina", "Stamina",
                "Gameplay HUD vital label. The exertion budget sprinting and harvesting consume. Same 110 px width constraint."),
            ("ui.screen.vitals.marker.low", "!",
                "One-character marker beside a gameplay HUD vital that has fallen below half. It exists so the warning does not depend on colour alone. Keep to one or two characters and prefer ASCII - it must render in every locale's font."),
            // Multiplayer presence subtitles. Hard-coded English in SurvivalFeedbackBridge until
            // 2026-08-25 — the bridge had no localization access, so there was nowhere to put a key.
            ("ui.status.multiplayer.player_joined", "Player joined.",
                "Subtitle shown when another player joins the session. A full sentence with a period, matching the other subtitle lines. 'Player' is any other person in the session, not a role or class."),
            ("ui.status.multiplayer.player_left", "Player left.",
                "Subtitle shown when another player leaves the session, whether deliberately or by disconnecting. Full sentence with a period."),

            // Settings row for the diagnostic readout. Two entries rather than one Smart entry with
            // a state argument, because MenuAction labels resolve through Text(key, fallback) and
            // carry no arguments — the factory picks the key instead.
            ("ui.action.settings.debug_overlay_on", "Debug Overlay: On",
                "Settings row that turns the diagnostic readout OFF when pressed - the label states the CURRENT state, not the action, matching how the other stateful rows in this game read. 'Debug Overlay' is the developer-facing readout of position, biome, weather and frame time."),
            ("ui.action.settings.debug_overlay_off", "Debug Overlay: Off",
                "Settings row that turns the diagnostic readout ON when pressed. The label states the CURRENT state, not the action."),

            ("ui.screen.vitals.marker.critical", "!!",
                "Marker beside a gameplay HUD vital below a quarter - the more urgent partner of the low marker, and it must read as MORE severe than that one. Same one-to-two character ASCII constraint."),
        };

        // One batchmode entry for the whole UI Toolkit integration step: table entries first
        // (the generated screens bind them), then the Boot-scene menu generation.
        public static void ApplyAndGenerateMenus()
        {
            Apply();
            BlockiverseProjectBootstrapper.BootstrapUiToolkitMenus();
        }

        [MenuItem("Blockiverse/Localization/Backfill UI Toolkit Entries")]
        public static void Apply()
        {
            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(TableName);

            if (collection == null)
                throw new InvalidOperationException("UI String Table Collection missing — run the table migrator first.");

            Locale english = LocalizationEditorSettings.GetLocale("en");

            if (english == null)
                throw new InvalidOperationException("en locale missing.");

            var table = (StringTable)collection.GetTable(english.Identifier);
            int added = 0;

            foreach ((string key, string value, string commentText) in Entries.OrderBy(e => e.key, StringComparer.Ordinal))
            {
                StringTableEntry entry = table.GetEntry(key);

                if (entry == null)
                {
                    entry = table.AddEntry(key, value);
                    added++;
                }

                entry.Value = value;
                // None of these carries arguments; Smart stays off like every migrated entry.
                entry.IsSmart = false;
                EnsureComment(entry.SharedEntry, commentText);
            }

            EditorUtility.SetDirty(table);
            EditorUtility.SetDirty(table.SharedData);
            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssets();

            BlockiverseLog.Info(
                BlockiverseLogCategory.Bootstrap,
                $"UI Toolkit table backfill: {added} entries added, {Entries.Length - added} already present.");
        }

        static void EnsureComment(SharedTableData.SharedTableEntry shared, string text)
        {
            Comment comment = shared.Metadata.GetMetadata<Comment>();

            if (comment == null)
            {
                comment = new Comment();
                shared.Metadata.AddMetadata(comment);
            }

            // The enforcement suite rejects empty and constructor-default comments; a
            // hand-written comment that already exists is left alone.
            if (string.IsNullOrWhiteSpace(comment.CommentText) || comment.CommentText == "Comment Text")
                comment.CommentText = text;
        }
    }
}
