using System.Collections.Generic;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.UI.Toolkit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // UI Toolkit audio settings screen (settings_audio), ported from
    // BlockiverseAudioSettingsPanel. UIDocument builds no panel in EditMode, so the real
    // VisualTreeAsset is instantiated by hand and attached through AttachForTest; and because
    // a panel-less BaseField never dispatches ChangeEvents, control changes are driven
    // through the controller's public Apply* seams (the registered callbacks are one-line
    // delegations to those seams — the spec-sanctioned EditMode fallback).
    public sealed class AudioSettingsScreenEditModeTests
    {
        const string DocumentPath = "Assets/Blockiverse/UI/Documents/AudioSettingsScreen.uxml";

        static readonly string[] SliderNames =
        {
            "bv-audio-master-volume",
            "bv-audio-effects-volume",
            "bv-audio-ui-volume",
            "bv-audio-weather-volume",
            "bv-audio-music-volume",
            "bv-audio-haptic-strength",
        };

        static readonly string[] ToggleNames =
        {
            "bv-audio-mute-all",
            "bv-audio-haptics",
            "bv-audio-reduced-flash",
            "bv-audio-reduced-particles",
            "bv-audio-classic-block-sounds",
        };

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

        static VisualElement InstantiateDocument()
        {
            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, $"document missing at {DocumentPath}");
            return tree.Instantiate();
        }

        AudioSettingsScreenController CreateController()
        {
            var gameObject = new GameObject("Audio Settings Screen Under Test");
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<AudioSettingsScreenController>();
        }

        BlockiverseFeedbackSettings CreateSettings()
        {
            var gameObject = new GameObject("Feedback Settings Under Test");
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<BlockiverseFeedbackSettings>();
        }

        (AudioSettingsScreenController controller, VisualElement root, BlockiverseFeedbackSettings settings)
            CreateAttachedWithSettings()
        {
            AudioSettingsScreenController controller = CreateController();
            BlockiverseFeedbackSettings settings = CreateSettings();
            controller.ConfigureFeedbackSettings(settings);
            VisualElement root = InstantiateDocument();
            controller.AttachForTest(root);
            return (controller, root, settings);
        }

        // Guards UXML/controller name agreement independently of the controller: a renamed
        // element in the document makes this fail even if the controller's Require calls were
        // edited to match the rename.
        [Test]
        public void TheDocumentCarriesEveryNamedControl()
        {
            VisualElement root = InstantiateDocument();

            Assert.That(root.Q<VisualElement>(UiToolkitScreenController.ScreenRootElementName), Is.Not.Null,
                "screen root element missing");

            foreach (string name in SliderNames)
                Assert.That(root.Q<Slider>(name), Is.Not.Null, $"slider '{name}' missing");

            foreach (string name in ToggleNames)
                Assert.That(root.Q<Toggle>(name), Is.Not.Null, $"toggle '{name}' missing");

            Assert.That(root.Q<Button>("bv-audio-close"), Is.Not.Null, "close button missing");
        }

        [Test]
        public void AttachBindsTheDocumentAndBalancesCallbacks()
        {
            AudioSettingsScreenController controller = CreateController();

            controller.AttachForTest(InstantiateDocument());

            Assert.That(controller.IsBound, Is.True);
            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1));
            Assert.That(controller.AttachCount, Is.EqualTo(1));

            // Re-attach (UIDocument rebuilds its tree behind the component in Play mode) must
            // unregister from the old elements before registering on the new ones; a drifted
            // balance here is the double-binding bug the base class exists to prevent.
            controller.AttachForTest(InstantiateDocument());

            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1));
            Assert.That(controller.AttachCount, Is.EqualTo(2));
        }

        // Negative control: a document that lost its elements must report unbound, not
        // present as a healthy blank panel.
        [Test]
        public void AttachToAnEmptyTreeReportsUnbound()
        {
            AudioSettingsScreenController controller = CreateController();

            LogAssert.ignoreFailingMessages = true;
            try
            {
                controller.AttachForTest(new VisualElement());
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.That(controller.IsBound, Is.False);
        }

        [Test]
        public void SliderSeamMutatesTheBoundSettingsValue()
        {
            (AudioSettingsScreenController controller, VisualElement _, BlockiverseFeedbackSettings settings) =
                CreateAttachedWithSettings();

            controller.ApplyMasterVolume(0.25f);
            controller.ApplyMusicVolume(0.05f);
            controller.ApplyHapticIntensity(0.4f);

            Assert.That(settings.MasterVolume, Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(settings.MusicVolume, Is.EqualTo(0.05f).Within(1e-5f));
            Assert.That(settings.HapticIntensity, Is.EqualTo(0.4f).Within(1e-5f));
        }

        [Test]
        public void ToggleSeamMutatesTheBoundSettingsFlag()
        {
            (AudioSettingsScreenController controller, VisualElement _, BlockiverseFeedbackSettings settings) =
                CreateAttachedWithSettings();

            // Positive controls: every flag starts on its serialized default.
            Assert.That(settings.MuteAll, Is.False);
            Assert.That(settings.HapticsEnabled, Is.True);
            Assert.That(settings.ClassicBlockSoundsEnabled, Is.False);

            controller.ApplyMuteAll(true);
            controller.ApplyHapticsEnabled(false);
            controller.ApplyReducedFlash(true);
            controller.ApplyReducedParticles(true);
            controller.ApplyClassicBlockSounds(true);

            Assert.That(settings.MuteAll, Is.True);
            Assert.That(settings.HapticsEnabled, Is.False);
            Assert.That(settings.ReducedFlash, Is.True);
            Assert.That(settings.ReducedParticles, Is.True);
            Assert.That(settings.ClassicBlockSoundsEnabled, Is.True);
        }

        // Eleven near-identical handlers make crossed wiring the likely defect; a change to
        // one field must leave every other field alone.
        [Test]
        public void SeamsWriteOnlyTheirMatchingSettingsField()
        {
            (AudioSettingsScreenController controller, VisualElement _, BlockiverseFeedbackSettings settings) =
                CreateAttachedWithSettings();

            settings.MasterVolume = 0.9f;
            settings.EffectsVolume = 0.8f;
            settings.UiVolume = 0.7f;
            settings.WeatherVolume = 0.6f;
            settings.MusicVolume = 0.5f;
            settings.HapticIntensity = 0.4f;

            controller.ApplyUiVolume(0.22f);

            Assert.That(settings.UiVolume, Is.EqualTo(0.22f).Within(1e-5f));
            Assert.That(settings.MasterVolume, Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(settings.EffectsVolume, Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(settings.WeatherVolume, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(settings.MusicVolume, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(settings.HapticIntensity, Is.EqualTo(0.4f).Within(1e-5f));
        }

        [Test]
        public void VolumeWritesClampLikeTheSettingsStore()
        {
            (AudioSettingsScreenController controller, VisualElement _, BlockiverseFeedbackSettings settings) =
                CreateAttachedWithSettings();

            controller.ApplyMasterVolume(1.5f);

            Assert.That(settings.MasterVolume, Is.EqualTo(1f).Within(1e-5f));
        }

        // The rig (and its settings component) is built after the panels, so the reference
        // arrives by scene lookup on first use rather than at attach time.
        [Test]
        public void SeamResolvesTheSettingsStoreLazilyFromTheScene()
        {
            // An ambient store (an open Boot scene in a local editor) would make the lookup
            // nondeterministic; batch runs execute in a clean scene where this always holds.
            Assume.That(
                Object.FindFirstObjectByType<BlockiverseFeedbackSettings>(FindObjectsInactive.Include) == null,
                "a pre-existing BlockiverseFeedbackSettings would make lazy resolution ambiguous");

            AudioSettingsScreenController controller = CreateController();
            controller.AttachForTest(InstantiateDocument());
            BlockiverseFeedbackSettings settings = CreateSettings();

            controller.ApplyEffectsVolume(0.3f);

            Assert.That(settings.EffectsVolume, Is.EqualTo(0.3f).Within(1e-5f));
        }

        [Test]
        public void PushDownRefreshesControlsWithoutWritingBack()
        {
            (AudioSettingsScreenController controller, VisualElement root, BlockiverseFeedbackSettings settings) =
                CreateAttachedWithSettings();
            Slider master = root.Q<Slider>("bv-audio-master-volume");
            Slider music = root.Q<Slider>("bv-audio-music-volume");
            Toggle muteAll = root.Q<Toggle>("bv-audio-mute-all");
            Toggle haptics = root.Q<Toggle>("bv-audio-haptics");

            // A stale control value that differs from the store: a wrong-direction sync would
            // pull 0.9 back into the settings instead of pushing 0.3 down.
            master.SetValueWithoutNotify(0.9f);
            settings.MasterVolume = 0.3f;
            settings.MusicVolume = 0.15f;
            settings.MuteAll = true;
            settings.HapticsEnabled = false;

            controller.RefreshControlsFromSettings();

            Assert.That(master.value, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(music.value, Is.EqualTo(0.15f).Within(1e-5f));
            Assert.That(muteAll.value, Is.True);
            Assert.That(haptics.value, Is.False);
            Assert.That(settings.MasterVolume, Is.EqualTo(0.3f).Within(1e-5f),
                "push-down must never write control state back into the settings store");
        }

        [Test]
        public void ShowingTheScreenPushesTheLiveSettingsDown()
        {
            AudioSettingsScreenController controller = CreateController();
            BlockiverseFeedbackSettings settings = CreateSettings();
            settings.MusicVolume = 0.15f;
            // Configure before attach: while unbound the configure seam stores the reference
            // without refreshing, preserving the document's design-time value as the
            // pre-condition below.
            controller.ConfigureFeedbackSettings(settings);
            VisualElement root = InstantiateDocument();
            controller.AttachForTest(root);
            Slider music = root.Q<Slider>("bv-audio-music-volume");

            // Positive control: the document's design-time preview value, untouched so far.
            Assert.That(music.value, Is.EqualTo(0.5f).Within(1e-5f));

            controller.SetVisible(true, true);

            Assert.That(music.value, Is.EqualTo(0.15f).Within(1e-5f));
        }

        [Test]
        public void ReattachRetargetsTheNewTreeOnly()
        {
            (AudioSettingsScreenController controller, VisualElement firstRoot, BlockiverseFeedbackSettings settings) =
                CreateAttachedWithSettings();
            VisualElement secondRoot = InstantiateDocument();

            controller.AttachForTest(secondRoot);
            settings.MusicVolume = 0.15f;
            controller.RefreshControlsFromSettings();

            Assert.That(secondRoot.Q<Slider>("bv-audio-music-volume").value, Is.EqualTo(0.15f).Within(1e-5f));
            Assert.That(firstRoot.Q<Slider>("bv-audio-music-volume").value, Is.EqualTo(0.5f).Within(1e-5f),
                "the detached tree must no longer receive pushes");
        }

        [Test]
        public void CloseSeamRoutesBackToTheSettingsHub()
        {
            var menuControllerObject = new GameObject("Menu Controller Under Test");
            objectsToDestroy.Add(menuControllerObject);
            BlockiverseMenuController menuController =
                menuControllerObject.AddComponent<BlockiverseMenuController>();

            var hostObject = new GameObject("Menu Host Under Test");
            objectsToDestroy.Add(hostObject);
            UiToolkitMenuHost host = hostObject.AddComponent<UiToolkitMenuHost>();
            host.Configure(menuController);

            AudioSettingsScreenController controller = CreateController();
            controller.ConfigureHost(host);
            controller.AttachForTest(InstantiateDocument());

            menuController.DispatchAction(MenuActions.TitleSettings);
            menuController.DispatchAction(MenuActions.SettingsOpenAudio);
            Assert.That(menuController.Router.ActiveScreen.ScreenId,
                Is.EqualTo(MenuActions.AudioSettingsScreen), "positive control: navigation to the screen failed");

            controller.RequestClose();

            Assert.That(menuController.Router.ActiveScreen.ScreenId,
                Is.EqualTo(MenuActions.SettingsScreen));
        }

        // The toggle rows have no table entries yet, so their labels resolve through UiText
        // at attach time with the requested ui.generated.audio_feedback.* keys — the labels
        // light up automatically the moment the entries land in the UI table.
        [Test]
        public void ToggleLabelsResolveThroughUiTextKeys()
        {
            (AudioSettingsScreenController _, VisualElement root, BlockiverseFeedbackSettings _) =
                CreateAttachedWithSettings();

            var labelKeysByName = new Dictionary<string, string>
            {
                ["bv-audio-mute-all"] = "ui.generated.audio_feedback.mute_all",
                ["bv-audio-haptics"] = "ui.generated.audio_feedback.haptics",
                ["bv-audio-reduced-flash"] = "ui.generated.audio_feedback.reduced_flash",
                ["bv-audio-reduced-particles"] = "ui.generated.audio_feedback.reduced_particles",
                ["bv-audio-classic-block-sounds"] = "ui.generated.audio_feedback.classic_block_sounds",
            };

            foreach (KeyValuePair<string, string> pair in labelKeysByName)
            {
                Toggle toggle = root.Q<Toggle>(pair.Key);
                Assert.That(toggle.label, Is.Not.Null.And.Not.Empty, $"toggle '{pair.Key}' has no label");
                Assert.That(toggle.label, Is.EqualTo(UiText.Get(pair.Value)),
                    $"toggle '{pair.Key}' must label itself through UiText with key '{pair.Value}'");
            }
        }
    }
}
