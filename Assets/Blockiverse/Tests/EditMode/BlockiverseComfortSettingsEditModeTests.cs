using System.Collections.Generic;
using Blockiverse.UI;
using Blockiverse.VR;
using Blockiverse.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseComfortSettingsEditModeTests
    {
        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
                if (target != null)
                    Object.DestroyImmediate(target);
            objectsToDestroy.Clear();
        }

        [Test]
        public void DefaultVignetteIsComfortFirstLowStrength()
        {
            BlockiverseComfortSettings settings = CreateSettings();

            // Comfort-first baseline: the motion vignette ships on at a low strength. It only renders
            // during locomotion, so a static title/menu remains readable while nausea is reduced.
            Assert.That(settings.VignetteEnabled, Is.True);
            Assert.That(settings.VignetteStrength, Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(settings.SnapTurnAroundEnabled, Is.True);
            Assert.That(settings.VignetteAperture, Is.EqualTo(0.88f).Within(0.001f));
        }

        [Test]
        public void DefaultGlideStyleIsSmooth()
        {
            BlockiverseComfortSettings settings = CreateSettings();

            Assert.That(settings.GlideStyle, Is.EqualTo(GlideStyle.Smooth));
        }

        [Test]
        public void VignetteStrengthNarrowsApertureAsSliderIncreases()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            settings.VignetteEnabled = true;

            settings.VignetteStrength = 0.0f;
            Assert.That(settings.VignetteAperture, Is.EqualTo(1.0f).Within(0.001f));

            settings.VignetteStrength = 0.5f;
            Assert.That(settings.VignetteAperture, Is.EqualTo(0.8f).Within(0.001f));

            settings.VignetteStrength = 1.0f;
            Assert.That(settings.VignetteAperture, Is.EqualTo(0.6f).Within(0.001f));

            settings.VignetteEnabled = false;
            Assert.That(settings.VignetteAperture, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void ComfortMenuEyeHeightSliderUpdatesStandingEyeHeight()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            BlockiverseComfortMenu menu = CreateObject("Comfort Menu").AddComponent<BlockiverseComfortMenu>();
            Slider eyeHeightSlider = CreateObject("Eye Height Slider").AddComponent<Slider>();
            eyeHeightSlider.minValue = 1.0f;
            eyeHeightSlider.maxValue = 2.2f;

            menu.Configure(null, settings);
            menu.ConfigureControls(
                targetGlideToggle: null,
                targetTeleportToggle: null,
                targetSmoothTurnToggle: null,
                targetSnapTurnSlider: null,
                targetEyeHeightSlider: eyeHeightSlider);

            eyeHeightSlider.value = 1.75f;

            Assert.That(settings.StandingEyeHeight, Is.EqualTo(1.75f).Within(0.001f));
        }

        [Test]
        public void ComfortMenuSlidersUpdateMoveTurnAndUiScaleSettings()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            BlockiverseComfortMenu menu = CreateObject("Comfort Menu").AddComponent<BlockiverseComfortMenu>();
            Slider moveSpeedSlider = CreateObject("Move Speed Slider").AddComponent<Slider>();
            Slider smoothTurnSpeedSlider = CreateObject("Smooth Turn Speed Slider").AddComponent<Slider>();
            Slider uiScaleSlider = CreateObject("UI Scale Slider").AddComponent<Slider>();
            moveSpeedSlider.minValue = 0.5f;
            moveSpeedSlider.maxValue = 4.0f;
            smoothTurnSpeedSlider.minValue = 30.0f;
            smoothTurnSpeedSlider.maxValue = 180.0f;
            uiScaleSlider.minValue = 0.85f;
            uiScaleSlider.maxValue = 1.35f;

            menu.Configure(null, settings);
            menu.ConfigureControls(
                targetGlideToggle: null,
                targetTeleportToggle: null,
                targetSmoothTurnToggle: null,
                targetSnapTurnSlider: null,
                targetMoveSpeedSlider: moveSpeedSlider,
                targetSmoothTurnSpeedSlider: smoothTurnSpeedSlider,
                targetUiScaleSlider: uiScaleSlider);

            moveSpeedSlider.value = 2.4f;
            smoothTurnSpeedSlider.value = 95.0f;
            uiScaleSlider.value = 1.2f;

            Assert.That(settings.ContinuousMoveSpeed, Is.EqualTo(2.4f).Within(0.001f));
            Assert.That(settings.ContinuousTurnSpeed, Is.EqualTo(95.0f).Within(0.001f));
            Assert.That(settings.UiScale, Is.EqualTo(1.2f).Within(0.001f));
        }

        [Test]
        public void ComfortMenuToggleToMineUpdatesMiningInputSetting()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            BlockiverseComfortMenu menu = CreateObject("Comfort Menu").AddComponent<BlockiverseComfortMenu>();
            Toggle toggleToMine = CreateObject("Toggle To Mine").AddComponent<Toggle>();

            menu.Configure(null, settings);
            menu.ConfigureControls(
                targetGlideToggle: null,
                targetTeleportToggle: null,
                targetSmoothTurnToggle: null,
                targetSnapTurnSlider: null,
                targetToggleToMineToggle: toggleToMine);

            toggleToMine.isOn = true;

            Assert.That(settings.ToggleToMineEnabled, Is.True);
        }

        [Test]
        public void ComfortMenuTurnAroundToggleUpdatesSnapTurnSetting()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            BlockiverseComfortMenu menu = CreateObject("Comfort Menu").AddComponent<BlockiverseComfortMenu>();
            Toggle turnAroundToggle = CreateObject("Turn Around Toggle").AddComponent<Toggle>();

            menu.Configure(null, settings);
            menu.ConfigureControls(
                targetGlideToggle: null,
                targetTeleportToggle: null,
                targetSmoothTurnToggle: null,
                targetSnapTurnSlider: null,
                targetTurnAroundToggle: turnAroundToggle);

            turnAroundToggle.isOn = false;

            Assert.That(settings.SnapTurnAroundEnabled, Is.False);
        }

        [Test]
        public void WorldSpacePresenterAppliesComfortUiScale()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            settings.UiScale = 1.25f;
            Transform head = CreateObject("Head").transform;
            BlockiverseWorldSpacePanelPresenter presenter =
                CreateObject("World Space Panel").AddComponent<BlockiverseWorldSpacePanelPresenter>();
            Canvas canvas = presenter.gameObject.AddComponent<Canvas>();

            presenter.Configure(
                canvas,
                head,
                distance: 1.0f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f,
                scale: 0.002f);
            presenter.ConfigureComfortSettings(settings);
            presenter.Recenter();

            Assert.That(presenter.transform.localScale.x, Is.EqualTo(0.0025f).Within(0.00001f));
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
