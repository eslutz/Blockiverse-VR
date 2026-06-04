using System.Collections.Generic;
using Blockiverse.Gameplay;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

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
        public void CreativeHotbarShowSelectionAndHidePlayInventoryFeedback()
        {
            GameObject hotbarObject = new("Creative Hotbar");
            objectsToDestroy.Add(hotbarObject);
            Canvas canvas = hotbarObject.AddComponent<Canvas>();
            CreativeHotbar hotbar = hotbarObject.AddComponent<CreativeHotbar>();
            BlockiverseWorldSpacePanelPresenter presenter = hotbarObject.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            TMP_Text label = CreateText("Selected Block Label");
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
            hotbar.ConfigureCanvas(canvas);
            hotbar.ConfigureFeedback(audioCuePlayer);
            presenter.Configure(
                canvas,
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
        public void WorldSpacePanelPresenterShowAndHidePlayConfiguredFeedback()
        {
            GameObject panelObject = new("World Space Panel");
            objectsToDestroy.Add(panelObject);
            Canvas canvas = panelObject.AddComponent<Canvas>();
            BlockiverseWorldSpacePanelPresenter presenter = panelObject.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            BlockiverseInteractionHaptics haptics = CreateHaptics();
            var playedCues = new List<BlockiverseAudioCue>();
            int uiTicks = 0;

            canvas.enabled = false;
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.InventoryOpen, CreateClip("inventory_open"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.InventoryClose, CreateClip("inventory_close"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);
            haptics.UiTickRequested += () => uiTicks++;

            presenter.Configure(
                canvas,
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
