using System.Collections.Generic;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

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

        [Test]
        public void CreativeHotbarShowSelectionAndHidePlayInventoryFeedback()
        {
            GameObject hotbarObject = new("Creative Hotbar");
            objectsToDestroy.Add(hotbarObject);
            GameObject hotbarVisibility = new("Hotbar Visibility");
            hotbarVisibility.transform.SetParent(hotbarObject.transform, false);
            CreativeHotbar hotbar = hotbarObject.AddComponent<CreativeHotbar>();
            BlockiverseUiToolkitMenuPresenter presenter = hotbarObject.AddComponent<BlockiverseUiToolkitMenuPresenter>();
            var label = new Label();
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            BlockiverseInteractionHaptics haptics = CreateHaptics();
            var playedCues = new List<BlockiverseAudioCue>();
            int uiTicks = 0;

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.InventoryOpen, CreateClip("inventory_open"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.UiSelect, CreateClip("ui_select"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.InventoryClose, CreateClip("inventory_close"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);
            haptics.UiTickRequested += () => uiTicks++;

            hotbar.ConfigureDefault(label);
            hotbar.ConfigureVisibilityRoot(hotbarVisibility);
            hotbar.ConfigureFeedback(audioCuePlayer);
            presenter.ConfigureWorldSpaceTarget(
                hotbarObject,
                targetHeadset: null,
                distance: 1.0f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f,
                recenterWhenShown: false);
            presenter.ConfigureFeedback(
                audioCuePlayer,
                haptics,
                BlockiverseAudioCue.InventoryOpen,
                BlockiverseAudioCue.InventoryClose);

            hotbar.Show();
            hotbar.SelectNext();
            hotbar.Hide();

            Assert.That(playedCues, Is.EqualTo(new[]
            {
                BlockiverseAudioCue.InventoryOpen,
                BlockiverseAudioCue.UiSelect,
                BlockiverseAudioCue.InventoryClose
            }));
            Assert.That(uiTicks, Is.EqualTo(1));
        }

        [Test]
        public void UiToolkitMenuPresenterShowAndHidePlayConfiguredFeedback()
        {
            GameObject panelObject = new("UI Toolkit Menu Presenter");
            objectsToDestroy.Add(panelObject);
            BlockiverseUiToolkitMenuPresenter presenter = panelObject.AddComponent<BlockiverseUiToolkitMenuPresenter>();
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            BlockiverseInteractionHaptics haptics = CreateHaptics();
            var playedCues = new List<BlockiverseAudioCue>();
            int uiTicks = 0;

            panelObject.SetActive(false);
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.InventoryOpen, CreateClip("inventory_open"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.InventoryClose, CreateClip("inventory_close"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);
            haptics.UiTickRequested += () => uiTicks++;

            presenter.ConfigureWorldSpaceTarget(
                panelObject,
                targetHeadset: null,
                distance: 1.0f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f,
                recenterWhenShown: false);
            presenter.ConfigureFeedback(
                audioCuePlayer,
                haptics,
                BlockiverseAudioCue.InventoryOpen,
                BlockiverseAudioCue.InventoryClose);

            presenter.Show();
            presenter.Hide();

            Assert.That(playedCues, Is.EqualTo(new[]
            {
                BlockiverseAudioCue.InventoryOpen,
                BlockiverseAudioCue.InventoryClose
            }));
            Assert.That(uiTicks, Is.EqualTo(2));
        }

        BlockiverseAudioCuePlayer CreateCuePlayer()
        {
            var gameObject = new GameObject("Audio Cue Player");
            objectsToDestroy.Add(gameObject);
            gameObject.AddComponent<AudioSource>();
            return gameObject.AddComponent<BlockiverseAudioCuePlayer>();
        }

        BlockiverseInteractionHaptics CreateHaptics()
        {
            var gameObject = new GameObject("Interaction Haptics");
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<BlockiverseInteractionHaptics>();
        }

        static AudioClip CreateClip(string name)
        {
            return AudioClip.Create(name, 16, 1, 44100, false);
        }
    }
}
