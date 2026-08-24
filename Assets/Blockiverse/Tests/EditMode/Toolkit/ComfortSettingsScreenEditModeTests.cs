using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // UI Toolkit mirror of the BlockiverseComfortMenu behaviours pinned by
    // BlockiverseComfortSettingsEditModeTests: every control maps its comfort setting, the
    // Glide/Teleport pair is a radio group that can never be both-off, push-down never echoes
    // back into settings, and — unlike the uGUI panel, whose UnregisterControlCallbacks
    // omitted 8 of 20 controls — unregistration is complete.
    //
    // ChangeEvents do not dispatch on a panel-less tree in EditMode (BaseField.value falls
    // back to SetValueWithoutNotify without a panel), so these tests drive the controller's
    // public handler seams after setting widget values, exactly as the build spec's fallback
    // prescribes. The no-echo test runs a control experiment first so it stays meaningful if
    // event dispatch ever starts working in this environment.
    public sealed class ComfortSettingsScreenEditModeTests
    {
        const string DocumentPath = "Assets/Blockiverse/UI/Documents/ComfortSettingsScreen.uxml";

        // 14 toggles + 6 sliders + close + height reset.
        const int ExpectedElementCallbackCount = 22;

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
        public void AttachBindsEveryNamedElementAndRegistersEveryCallback()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            ComfortSettingsScreenController controller = CreateController(settings);

            controller.AttachForTest(InstantiateTree());

            Assert.That(controller.IsBound, Is.True,
                "Every named element in the document must resolve; a rename in either file is a break.");
            Assert.That(controller.RegisteredElementCallbackCount, Is.EqualTo(ExpectedElementCallbackCount));
            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1));
        }

        [Test]
        public void AttachingToAForeignTreeReportsUnboundAndWiresNothing()
        {
            // Negative control for the whole class: against a tree with none of the named
            // elements the controller must fail loudly, register nothing, and a handler drive
            // must leave settings untouched — a do-nothing controller cannot pass this pair.
            BlockiverseComfortSettings settings = CreateSettings();
            ComfortSettingsScreenController controller = CreateController(settings);

            bool previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                controller.AttachForTest(new VisualElement());
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
            }

            Assert.That(controller.IsBound, Is.False);
            Assert.That(controller.RegisteredElementCallbackCount, Is.Zero);

            controller.ApplyOtherControlsWithFeedback();

            Assert.That(settings.SmoothTurnEnabled, Is.False,
                "With no widgets bound the shared handler must not invent mutations.");
        }

        [Test]
        public void EveryControlValueFlowsIntoComfortSettings()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            ComfortSettingsScreenController controller = CreateController(settings);
            VisualElement root = InstantiateTree();
            controller.AttachForTest(root);

            // Flip every shared-handler control to a value distinct from its settings default,
            // so a dropped mapping shows up as a stayed-default assertion failure below.
            root.Q<Toggle>("bv-comfort-smooth-turn").SetValueWithoutNotify(true);
            root.Q<Toggle>("bv-comfort-turn-around").SetValueWithoutNotify(false);
            root.Q<Toggle>("bv-comfort-left-hand").SetValueWithoutNotify(true);
            root.Q<Toggle>("bv-comfort-toggle-to-mine").SetValueWithoutNotify(true);
            root.Q<Toggle>("bv-comfort-real-height").SetValueWithoutNotify(true);
            root.Q<Toggle>("bv-comfort-sprint-toggle").SetValueWithoutNotify(true);
            root.Q<Toggle>("bv-comfort-crouch-toggle").SetValueWithoutNotify(true);
            root.Q<Toggle>("bv-comfort-swim-sink").SetValueWithoutNotify(false);
            root.Q<Toggle>("bv-comfort-swim-vignette").SetValueWithoutNotify(false);
            root.Q<Toggle>("bv-comfort-swim-climb-out").SetValueWithoutNotify(false);
            root.Q<Toggle>("bv-comfort-vignette").SetValueWithoutNotify(false);
            root.Q<Toggle>("bv-comfort-glide-bob").SetValueWithoutNotify(true);
            root.Q<Slider>("bv-comfort-move-speed").SetValueWithoutNotify(2.4f);
            root.Q<Slider>("bv-comfort-snap-turn").SetValueWithoutNotify(75.4f);
            root.Q<Slider>("bv-comfort-smooth-turn-speed").SetValueWithoutNotify(95.0f);
            root.Q<Slider>("bv-comfort-vignette-strength").SetValueWithoutNotify(0.7f);
            root.Q<Slider>("bv-comfort-ui-scale").SetValueWithoutNotify(1.2f);
            root.Q<Slider>("bv-comfort-swim-speed").SetValueWithoutNotify(0.8f);

            controller.ApplyOtherControlsWithFeedback();

            Assert.That(settings.SmoothTurnEnabled, Is.True);
            Assert.That(settings.SnapTurnAroundEnabled, Is.False);
            Assert.That(settings.DominantHand, Is.EqualTo(BlockiverseControllerRole.Left));
            Assert.That(settings.ToggleToMineEnabled, Is.True);
            Assert.That(settings.RealPlayerHeightEnabled, Is.True);
            Assert.That(settings.SprintToggleEnabled, Is.True);
            Assert.That(settings.CrouchToggleEnabled, Is.True);
            Assert.That(settings.SwimPassiveSinkEnabled, Is.False,
                "The sink toggle maps directly onto the setting, not inverted.");
            Assert.That(settings.SwimVignetteBoost, Is.False);
            Assert.That(settings.SwimClimbOutEnabled, Is.False);
            Assert.That(settings.VignetteEnabled, Is.False);
            Assert.That(settings.GlideStyle, Is.EqualTo(GlideStyle.Bobbing));
            Assert.That(settings.ContinuousMoveSpeed, Is.EqualTo(2.4f).Within(0.001f));
            Assert.That(settings.SnapTurnDegrees, Is.EqualTo(75.0f).Within(0.001f),
                "Snap turn keeps the uGUI wholeNumbers contract by rounding to whole degrees.");
            Assert.That(settings.ContinuousTurnSpeed, Is.EqualTo(95.0f).Within(0.001f));
            Assert.That(settings.VignetteStrength, Is.EqualTo(0.7f).Within(0.001f));
            Assert.That(settings.UiScale, Is.EqualTo(1.2f).Within(0.001f));
            Assert.That(settings.SwimSpeedFactor, Is.EqualTo(0.8f).Within(0.001f));
        }

        [Test]
        public void SprintAndCrouchToggleModesAreIndependent()
        {
            // Mirror of the uGUI oracle: hold to sprint while crouch is a click toggle.
            BlockiverseComfortSettings settings = CreateSettings();
            ComfortSettingsScreenController controller = CreateController(settings);
            VisualElement root = InstantiateTree();
            controller.AttachForTest(root);

            root.Q<Toggle>("bv-comfort-crouch-toggle").SetValueWithoutNotify(true);
            controller.ApplyOtherControlsWithFeedback();

            Assert.That(settings.CrouchToggleEnabled, Is.True);
            Assert.That(settings.SprintToggleEnabled, Is.False,
                "Crouch and sprint control styles must be set independently.");

            root.Q<Toggle>("bv-comfort-sprint-toggle").SetValueWithoutNotify(true);
            controller.ApplyOtherControlsWithFeedback();

            Assert.That(settings.SprintToggleEnabled, Is.True);
            Assert.That(settings.CrouchToggleEnabled, Is.True);
        }

        [Test]
        public void GlideAndTeleportAreARadioPairThatIsNeverBothOff()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            ComfortSettingsScreenController controller = CreateController(settings);
            VisualElement root = InstantiateTree();
            controller.AttachForTest(root);
            Toggle glide = root.Q<Toggle>("bv-comfort-glide");
            Toggle teleport = root.Q<Toggle>("bv-comfort-teleport");

            // Attach synced the settings default: Glide on, Teleport off.
            Assert.That(glide.value, Is.True);
            Assert.That(teleport.value, Is.False);

            // Selecting Teleport deselects Glide.
            teleport.SetValueWithoutNotify(true);
            controller.ApplyTeleportToggled(true);

            Assert.That(settings.LocomotionMode, Is.EqualTo(BlockiverseLocomotionMode.Teleport));
            Assert.That(glide.value, Is.False);
            Assert.That(glide.value ^ teleport.value, Is.True, "Exactly one mode is ever selected.");

            // Switching Teleport off implicitly selects Glide rather than leaving both off.
            teleport.SetValueWithoutNotify(false);
            controller.ApplyTeleportToggled(false);

            Assert.That(settings.LocomotionMode, Is.EqualTo(BlockiverseLocomotionMode.Glide));
            Assert.That(glide.value, Is.True);
            Assert.That(glide.value || teleport.value, Is.True, "The pair must never be both-off.");

            // And the mirror image: switching Glide off implicitly selects Teleport.
            glide.SetValueWithoutNotify(false);
            controller.ApplyGlideToggled(false);

            Assert.That(settings.LocomotionMode, Is.EqualTo(BlockiverseLocomotionMode.Teleport));
            Assert.That(teleport.value, Is.True);
        }

        [Test]
        public void RefreshFromSettingsPushesValuesDownWithoutEcho()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            ComfortSettingsScreenController controller = CreateController(settings);
            VisualElement root = InstantiateTree();
            controller.AttachForTest(root);
            Slider moveSpeed = root.Q<Slider>("bv-comfort-move-speed");
            Toggle glide = root.Q<Toggle>("bv-comfort-glide");

            int changeEventCount = 0;
            moveSpeed.RegisterValueChangedCallback(_ => changeEventCount++);
            glide.RegisterValueChangedCallback(_ => changeEventCount++);

            // Control experiment: does a plain value assignment dispatch ChangeEvents in this
            // environment at all? Without a panel it does not, and the echo assertion below is
            // then carried by the value-equality checks; with a panel it does, and the counter
            // becomes a genuine echo detector.
            moveSpeed.value = 3.3f;
            int baseline = changeEventCount;

            settings.LocomotionMode = BlockiverseLocomotionMode.Teleport;
            settings.SmoothTurnEnabled = true;
            settings.SnapTurnAroundEnabled = false;
            settings.ContinuousMoveSpeed = 2.2f;
            settings.ContinuousTurnSpeed = 120.0f;
            settings.SnapTurnDegrees = 60.0f;
            settings.DominantHand = BlockiverseControllerRole.Left;
            settings.ToggleToMineEnabled = true;
            settings.RealPlayerHeightEnabled = true;
            settings.SprintToggleEnabled = true;
            settings.CrouchToggleEnabled = true;
            settings.SwimPassiveSinkEnabled = false;
            settings.SwimSpeedFactor = 0.4f;
            settings.SwimVignetteBoost = false;
            settings.SwimClimbOutEnabled = false;
            settings.VignetteEnabled = false;
            settings.VignetteStrength = 0.9f;
            settings.UiScale = 1.25f;
            settings.GlideStyle = GlideStyle.Bobbing;

            controller.RefreshFromSettings();

            Assert.That(glide.value, Is.False);
            Assert.That(root.Q<Toggle>("bv-comfort-teleport").value, Is.True);
            Assert.That(root.Q<Toggle>("bv-comfort-smooth-turn").value, Is.True);
            Assert.That(root.Q<Toggle>("bv-comfort-turn-around").value, Is.False);
            Assert.That(root.Q<Toggle>("bv-comfort-left-hand").value, Is.True);
            Assert.That(root.Q<Toggle>("bv-comfort-toggle-to-mine").value, Is.True);
            Assert.That(root.Q<Toggle>("bv-comfort-real-height").value, Is.True);
            Assert.That(root.Q<Toggle>("bv-comfort-sprint-toggle").value, Is.True);
            Assert.That(root.Q<Toggle>("bv-comfort-crouch-toggle").value, Is.True);
            Assert.That(root.Q<Toggle>("bv-comfort-swim-sink").value, Is.False);
            Assert.That(root.Q<Toggle>("bv-comfort-swim-vignette").value, Is.False);
            Assert.That(root.Q<Toggle>("bv-comfort-swim-climb-out").value, Is.False);
            Assert.That(root.Q<Toggle>("bv-comfort-vignette").value, Is.False);
            Assert.That(root.Q<Toggle>("bv-comfort-glide-bob").value, Is.True);
            Assert.That(moveSpeed.value, Is.EqualTo(2.2f).Within(0.001f));
            Assert.That(root.Q<Slider>("bv-comfort-snap-turn").value, Is.EqualTo(60.0f).Within(0.001f));
            Assert.That(root.Q<Slider>("bv-comfort-smooth-turn-speed").value, Is.EqualTo(120.0f).Within(0.001f));
            Assert.That(root.Q<Slider>("bv-comfort-vignette-strength").value, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(root.Q<Slider>("bv-comfort-ui-scale").value, Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(root.Q<Slider>("bv-comfort-swim-speed").value, Is.EqualTo(0.4f).Within(0.001f));

            Assert.That(changeEventCount, Is.EqualTo(baseline),
                "Push-down must go through SetValueWithoutNotify — it may never raise ChangeEvents.");
            Assert.That(settings.ContinuousMoveSpeed, Is.EqualTo(2.2f).Within(0.001f),
                "Push-down must not write back into settings.");
        }

        [Test]
        public void SliderValueLabelsShowFiguresForTheCurrentValues()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            ComfortSettingsScreenController controller = CreateController(settings);
            VisualElement root = InstantiateTree();
            controller.AttachForTest(root);

            // Attach pushed the settings defaults down and rendered them.
            Assert.That(root.Q<Label>("bv-comfort-move-speed-value").text, Is.EqualTo("1.8"));
            Assert.That(root.Q<Label>("bv-comfort-snap-turn-value").text, Is.EqualTo("45"));
            Assert.That(root.Q<Label>("bv-comfort-smooth-turn-speed-value").text, Is.EqualTo("60"));
            Assert.That(root.Q<Label>("bv-comfort-vignette-strength-value").text, Is.EqualTo("0.30"));
            Assert.That(root.Q<Label>("bv-comfort-ui-scale-value").text, Is.EqualTo("1.00"));
            Assert.That(root.Q<Label>("bv-comfort-swim-speed-value").text, Is.EqualTo("0.55"));

            // And a change through the shared handler re-renders the readout.
            root.Q<Slider>("bv-comfort-ui-scale").SetValueWithoutNotify(1.2f);
            controller.ApplyOtherControlsWithFeedback();

            Assert.That(root.Q<Label>("bv-comfort-ui-scale-value").text, Is.EqualTo("1.20"));
        }

        [Test]
        public void RuntimeLabelsAreAppliedToEveryTitleHeadingAndControl()
        {
            // None of the comfort strings exist in the UI table yet, so every label renders
            // through UiText at attach time (falling back to the requested key until the entry
            // lands). Empty text here means ApplyStaticLabels lost a control.
            BlockiverseComfortSettings settings = CreateSettings();
            ComfortSettingsScreenController controller = CreateController(settings);
            VisualElement root = InstantiateTree();
            controller.AttachForTest(root);

            Assert.That(root.Q<Label>("bv-comfort-title").text, Is.Not.Empty);
            Assert.That(root.Q<Label>("bv-comfort-movement-heading").text, Is.Not.Empty);
            Assert.That(root.Q<Label>("bv-comfort-turning-heading").text, Is.Not.Empty);
            Assert.That(root.Q<Label>("bv-comfort-control-options-heading").text, Is.Not.Empty);
            Assert.That(root.Q<Label>("bv-comfort-view-comfort-heading").text, Is.Not.Empty);
            Assert.That(root.Q<Label>("bv-comfort-player-view-heading").text, Is.Not.Empty);
            Assert.That(root.Q<Button>("bv-comfort-height-reset").text, Is.Not.Empty);

            foreach (Toggle toggle in root.Query<Toggle>().ToList())
                Assert.That(toggle.label, Is.Not.Empty, $"Toggle '{toggle.name}' lost its label.");

            foreach (Slider slider in root.Query<Slider>().ToList())
            {
                // ScrollView scrollbars are built from an INTERNAL Slider named
                // "unity-slider"; it is Unity chrome, not an authored control, and
                // legitimately has no label.
                if (slider.name == "unity-slider")
                    continue;

                Assert.That(slider.label, Is.Not.Empty, $"Slider '{slider.name}' lost its label.");
            }
        }

        [Test]
        public void ReattachUnregistersEverythingItRegistered()
        {
            // The uGUI panel's unregister omitted 8 of its 20 controls. The controller counts
            // real Register/Unregister calls, so a port of that defect surfaces here as a
            // count above the registered set size after a second attach.
            BlockiverseComfortSettings settings = CreateSettings();
            ComfortSettingsScreenController controller = CreateController(settings);

            controller.AttachForTest(InstantiateTree());
            Assert.That(controller.RegisteredElementCallbackCount, Is.EqualTo(ExpectedElementCallbackCount));

            VisualElement secondRoot = InstantiateTree();
            controller.AttachForTest(secondRoot);

            Assert.That(controller.AttachCount, Is.EqualTo(2));
            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1));
            Assert.That(controller.RegisteredElementCallbackCount, Is.EqualTo(ExpectedElementCallbackCount),
                "An incomplete unregister leaves stale registrations behind across re-attach.");

            // Positive control: the controller is still functional against the new tree.
            secondRoot.Q<Toggle>("bv-comfort-toggle-to-mine").SetValueWithoutNotify(true);
            controller.ApplyOtherControlsWithFeedback();
            Assert.That(settings.ToggleToMineEnabled, Is.True);
        }

        [Test]
        public void CloseDispatchesTheCanonicalCloseActionThroughTheMenuController()
        {
            // The PlayerPrefs first-run seen flag is BlockiverseMenuController's job; this
            // screen only dispatches the canonical action id. Snapshot and restore the flag so
            // the test leaves the developer's editor state alone.
            int seenBefore = PlayerPrefs.GetInt(BlockiverseMenuController.ComfortScreenSeenPrefKey, 0);
            try
            {
                BlockiverseComfortSettings settings = CreateSettings();
                BlockiverseMenuController menuController =
                    CreateObject("Menu Controller").AddComponent<BlockiverseMenuController>();
                UiToolkitMenuHost host = CreateObject("Menu Host").AddComponent<UiToolkitMenuHost>();
                host.Configure(menuController);
                menuController.RegisterFrontend(host);

                ComfortSettingsScreenController controller = CreateController(settings);
                controller.ConfigureHost(host);
                controller.AttachForTest(InstantiateTree());

                string screenBefore = menuController.Router.ActiveScreen.ScreenId;
                menuController.DispatchAction(MenuActions.SettingsOpenComfort);
                Assert.That(menuController.Router.ActiveScreen.ScreenId,
                    Is.EqualTo(MenuActions.ComfortSettingsScreen));

                controller.RequestClose();

                Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(screenBefore),
                    "settings_comfort.close must pop the comfort route.");
                Assert.That(PlayerPrefs.GetInt(BlockiverseMenuController.ComfortScreenSeenPrefKey, 0),
                    Is.EqualTo(1),
                    "The seen flag is set by the menu controller when the close action routes through it.");
            }
            finally
            {
                PlayerPrefs.SetInt(BlockiverseMenuController.ComfortScreenSeenPrefKey, seenBefore);
                PlayerPrefs.Save();
            }
        }

        VisualElement InstantiateTree()
        {
            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, $"UXML document missing at {DocumentPath}");
            return tree.Instantiate();
        }

        ComfortSettingsScreenController CreateController(BlockiverseComfortSettings settings)
        {
            GameObject gameObject = CreateObject("Comfort Screen Under Test");
            gameObject.AddComponent<UIDocument>();
            ComfortSettingsScreenController controller =
                gameObject.AddComponent<ComfortSettingsScreenController>();
            controller.ConfigureSettings(settings);
            return controller;
        }

        BlockiverseComfortSettings CreateSettings()
        {
            return CreateObject("Comfort Settings").AddComponent<BlockiverseComfortSettings>();
        }

        GameObject CreateObject(string name)
        {
            GameObject target = new(name);
            objectsToDestroy.Add(target);
            return target;
        }
    }
}
