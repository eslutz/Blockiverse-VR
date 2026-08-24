using System.Collections.Generic;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.Voxel;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using TMPro;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseAudioCuePlayerEditModeTests
    {
        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        [Test]
        public void FootstepCueAlternatesBetweenConfiguredClips()
        {
            BlockiverseAudioCuePlayer player = CreateCuePlayer();
            AudioClip first = CreateClip("footstep_01");
            AudioClip second = CreateClip("footstep_02");
            var playedClips = new List<string>();

            player.ConfigureFootstepClips(first, second);
            player.CuePlayed += (_, clip) => playedClips.Add(clip.name);

            player.PlayCue(BlockiverseAudioCue.Footstep);
            player.PlayCue(BlockiverseAudioCue.Footstep);
            player.PlayCue(BlockiverseAudioCue.Footstep);

            Assert.That(playedClips, Is.EqualTo(new[] { "footstep_01", "footstep_02", "footstep_01" }));
        }

        [Test]
        public void InventoryAndCraftingCuesResolveConfiguredClips()
        {
            BlockiverseAudioCuePlayer player = CreateCuePlayer();
            var playedCues = new List<BlockiverseAudioCue>();

            player.ConfigureClip(BlockiverseAudioCue.InventoryOpen, CreateClip("inventory_open"));
            player.ConfigureClip(BlockiverseAudioCue.InventoryClose, CreateClip("inventory_close"));
            player.ConfigureClip(BlockiverseAudioCue.CraftSuccess, CreateClip("craft_success"));
            player.ConfigureClip(BlockiverseAudioCue.CraftFail, CreateClip("craft_fail"));
            player.CuePlayed += (cue, _) => playedCues.Add(cue);

            player.PlayCue(BlockiverseAudioCue.InventoryOpen);
            player.PlayCue(BlockiverseAudioCue.InventoryClose);
            player.PlayCue(BlockiverseAudioCue.CraftSuccess);
            player.PlayCue(BlockiverseAudioCue.CraftFail);

            Assert.That(playedCues, Is.EqualTo(new[]
            {
                BlockiverseAudioCue.InventoryOpen,
                BlockiverseAudioCue.InventoryClose,
                BlockiverseAudioCue.CraftSuccess,
                BlockiverseAudioCue.CraftFail
            }));
        }

        [Test]
        public void Phase13CuesResolveConfiguredClipsAndCategories()
        {
            BlockiverseAudioCuePlayer player = CreateCuePlayer();
            var playedCues = new List<BlockiverseAudioCue>();

            foreach (BlockiverseAudioCue cue in System.Enum.GetValues(typeof(BlockiverseAudioCue)))
            {
                if (cue == BlockiverseAudioCue.Footstep)
                    player.ConfigureFootstepClips(CreateClip("footstep_01"), CreateClip("footstep_02"));
                else
                    player.ConfigureClip(cue, CreateClip(cue.ToString()));
            }

            player.CuePlayed += (cue, _) => playedCues.Add(cue);

            player.PlayCue(BlockiverseAudioCue.ToolHitSoft);
            player.PlayCue(BlockiverseAudioCue.ToolHitStone);
            player.PlayCue(BlockiverseAudioCue.PickupItem);
            player.PlayCue(BlockiverseAudioCue.ContainerOpen);
            player.PlayCue(BlockiverseAudioCue.TorchIgnite);
            player.PlayCue(BlockiverseAudioCue.RainLightLoop);
            player.PlayCueAt(BlockiverseAudioCue.BlockBreak, Vector3.one);

            Assert.That(playedCues, Is.EqualTo(new[]
            {
                BlockiverseAudioCue.ToolHitSoft,
                BlockiverseAudioCue.ToolHitStone,
                BlockiverseAudioCue.PickupItem,
                BlockiverseAudioCue.ContainerOpen,
                BlockiverseAudioCue.TorchIgnite,
                BlockiverseAudioCue.RainLightLoop,
                BlockiverseAudioCue.BlockBreak
            }));
            Assert.That(BlockiverseAudioCuePlayer.GetCategory(BlockiverseAudioCue.UiConfirm), Is.EqualTo(BlockiverseAudioCategory.Ui));
            Assert.That(BlockiverseAudioCuePlayer.GetCategory(BlockiverseAudioCue.RainLightLoop), Is.EqualTo(BlockiverseAudioCategory.Weather));
            Assert.That(BlockiverseAudioCuePlayer.GetCategory(BlockiverseAudioCue.BlockBreak), Is.EqualTo(BlockiverseAudioCategory.Effects));
        }

        [Test]
        public void BlockFeedbackCuesUseStoneCueForHardBlocksAndSoftCueForOrganicBlocks()
        {
            BlockRegistry registry = BlockRegistry.Default;

            Assert.That(
                BlockiverseBlockFeedbackCues.ToolHitForBlock(registry, BlockRegistry.Graystone),
                Is.EqualTo(BlockiverseAudioCue.ToolHitStone));
            Assert.That(
                BlockiverseBlockFeedbackCues.ToolHitForBlock(registry, BlockRegistry.EmbercoalSeam),
                Is.EqualTo(BlockiverseAudioCue.ToolHitStone));
            Assert.That(
                BlockiverseBlockFeedbackCues.ToolHitForBlock(registry, BlockRegistry.BranchwoodLog),
                Is.EqualTo(BlockiverseAudioCue.ToolHitSoft));
        }

        [Test]
        public void LoopCuesUsePersistentSourcesUntilStopped()
        {
            BlockiverseAudioCuePlayer player = CreateCuePlayer();
            AudioClip loopClip = CreateClip("rain_light_loop");
            var playedCues = new List<BlockiverseAudioCue>();

            player.ConfigureClip(BlockiverseAudioCue.RainLightLoop, loopClip);
            player.CuePlayed += (cue, _) => playedCues.Add(cue);

            bool started = player.StartLoop(BlockiverseAudioCue.RainLightLoop);
            bool duplicateStart = player.StartLoop(BlockiverseAudioCue.RainLightLoop);

            Assert.That(started, Is.True);
            Assert.That(duplicateStart, Is.False);
            Assert.That(player.ActiveLoopCount, Is.EqualTo(1));
            Assert.That(player.IsLoopActive(BlockiverseAudioCue.RainLightLoop), Is.True);
            Assert.That(BlockiverseAudioCuePlayer.IsLoopCue(BlockiverseAudioCue.RainLightLoop), Is.True);
            Assert.That(BlockiverseAudioCuePlayer.IsLoopCue(BlockiverseAudioCue.ThunderNear), Is.False);
            Assert.That(playedCues, Is.EqualTo(new[] { BlockiverseAudioCue.RainLightLoop }));

            player.StopLoop(BlockiverseAudioCue.RainLightLoop);

            Assert.That(player.ActiveLoopCount, Is.Zero);
            Assert.That(player.IsLoopActive(BlockiverseAudioCue.RainLightLoop), Is.False);
        }

        [Test]
        public void FeedbackSettingsScaleAudioAndHaptics()
        {
            var gameObject = new GameObject("Feedback Settings");
            objectsToDestroy.Add(gameObject);
            BlockiverseFeedbackSettings settings = gameObject.AddComponent<BlockiverseFeedbackSettings>();

            settings.MasterVolume = 0.5f;
            settings.EffectsVolume = 0.5f;
            settings.UiVolume = 0.25f;
            settings.WeatherVolume = 0.2f;
            settings.HapticIntensity = 0.4f;

            Assert.That(settings.ResolveVolume(BlockiverseAudioCategory.Effects), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(settings.ResolveVolume(BlockiverseAudioCategory.Ui), Is.EqualTo(0.125f).Within(0.001f));
            Assert.That(settings.ResolveVolume(BlockiverseAudioCategory.Weather), Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(BlockiverseHapticPattern.BlockBreak.Scale(settings.ResolveHapticIntensity()).Amplitude, Is.EqualTo(0.24f).Within(0.001f));

            settings.MuteAll = true;
            settings.HapticsEnabled = false;

            Assert.That(settings.ResolveVolume(BlockiverseAudioCategory.Effects), Is.Zero);
            Assert.That(BlockiverseHapticPattern.BlockBreak.Scale(settings.ResolveHapticIntensity()).Amplitude, Is.Zero);
        }

        // The scene hotbar (Blockiverse.Gameplay's CreativeHotbar) survives the uGUI cutover —
        // CreativeHotbarController mirrors Toolkit selection into it — and it owns these three
        // cues itself. What went with the uGUI presenter is the haptic tick that used to ride
        // alongside show/hide; the Toolkit quick menu plays its own through
        // CreativeHotbarController.SetQuickMenuVisible, which no test covers.
        [Test]
        public void CreativeHotbarShowSelectionAndHidePlayInventoryFeedback()
        {
            GameObject hotbarObject = new("Creative Hotbar");
            objectsToDestroy.Add(hotbarObject);
            Canvas canvas = hotbarObject.AddComponent<Canvas>();
            CreativeHotbar hotbar = hotbarObject.AddComponent<CreativeHotbar>();
            TMP_Text label = CreateText("Selected Block Label");
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            var playedCues = new List<BlockiverseAudioCue>();

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.InventoryOpen, CreateClip("inventory_open"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.UiSelect, CreateClip("ui_select"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.InventoryClose, CreateClip("inventory_close"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);

            hotbar.ConfigureDefault(label);
            hotbar.ConfigureCanvas(canvas);
            hotbar.ConfigureFeedback(audioCuePlayer);

            hotbar.Show();
            hotbar.SelectNext();
            hotbar.Hide();

            // Order matters, not just membership: a selection cue that fires before the open
            // cue reads as a stutter in the headset.
            Assert.That(playedCues, Is.EqualTo(new[]
            {
                BlockiverseAudioCue.InventoryOpen,
                BlockiverseAudioCue.UiSelect,
                BlockiverseAudioCue.InventoryClose
            }));
        }

        [Test]
        public void PerCallVolumeScaleCannotBypassTheMixGates()
        {
            // The whole reason the thunder distance curve folds into ResolveVolume rather than
            // being applied at the PlayOneShot call: a caller must not be able to scale its way
            // past mute-all or the category bus.
            BlockiverseAudioCuePlayer player = CreateCuePlayer();
            player.ConfigureClip(BlockiverseAudioCue.ThunderFar, CreateClip("thunder_far"));

            var settingsObject = new GameObject("Thunder Feedback Settings");
            objectsToDestroy.Add(settingsObject);
            BlockiverseFeedbackSettings settings = settingsObject.AddComponent<BlockiverseFeedbackSettings>();
            player.ConfigureFeedbackSettings(settings);

            var played = new List<BlockiverseAudioCue>();
            player.CuePlayed += (cue, _) => played.Add(cue);

            player.PlayCue(BlockiverseAudioCue.ThunderFar, volumeScale: 0.25f);
            Assert.That(played, Has.Count.EqualTo(1), "A quiet distant clap should still play.");

            // Zero scale is how a strike past the silence distance drops out, and it must drop out
            // rather than play inaudibly.
            player.PlayCue(BlockiverseAudioCue.ThunderFar, volumeScale: 0.0f);
            Assert.That(played, Has.Count.EqualTo(1));

            settings.WeatherVolume = 0.0f;
            player.PlayCue(BlockiverseAudioCue.ThunderFar, volumeScale: 1.0f);
            Assert.That(played, Has.Count.EqualTo(1), "The weather bus must still be able to silence thunder.");

            settings.WeatherVolume = 1.0f;
            settings.MuteAll = true;
            player.PlayCue(BlockiverseAudioCue.ThunderFar, volumeScale: 1.0f);
            Assert.That(played, Has.Count.EqualTo(1), "Mute All must still win over a full-volume strike.");
        }

        BlockiverseAudioCuePlayer CreateCuePlayer()
        {
            var gameObject = new GameObject("Audio Cue Player");
            objectsToDestroy.Add(gameObject);
            gameObject.AddComponent<AudioSource>();
            return gameObject.AddComponent<BlockiverseAudioCuePlayer>();
        }

        TextMeshProUGUI CreateText(string name)
        {
            var gameObject = new GameObject(name);
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<TextMeshProUGUI>();
        }

        static AudioClip CreateClip(string name)
        {
            return AudioClip.Create(name, 16, 1, 44100, false);
        }
    }
}
