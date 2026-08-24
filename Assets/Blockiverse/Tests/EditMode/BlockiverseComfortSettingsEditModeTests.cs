using System.Collections.Generic;
using Blockiverse.UI;
using Blockiverse.VR;
using Blockiverse.Core;
using NUnit.Framework;
using UnityEngine;

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
        public void SwimmingDefaultsToSinkingAndTheAccommodationIsTheInverseSetting()
        {
            // The one place this game deliberately defaults away from the gentler option: water
            // should read as something you work against, not a floor you bob on. Turning the
            // setting off is what restores exact neutral buoyancy, so the default must be ON and
            // the escape hatch must exist.
            BlockiverseComfortSettings settings = CreateSettings();

            Assert.That(settings.SwimPassiveSinkEnabled, Is.True,
                "Negative buoyancy is the ratified default.");
            Assert.That(settings.SwimVignetteBoost, Is.True,
                "The unrequested vertical motion gets the same comfort aid as driven motion.");

            settings.SwimPassiveSinkEnabled = false;

            Assert.That(settings.SwimPassiveSinkEnabled, Is.False,
                "The accommodation has to be reachable, or the default is not defensible.");
        }

        [Test]
        public void SwimSpeedFactorIsClampedToTheRangeTheMotionHelperExpects()
        {
            BlockiverseComfortSettings settings = CreateSettings();

            Assert.That(settings.SwimSpeedFactor,
                Is.EqualTo(BlockiverseSwimMotion.DefaultSwimSpeedFactor).Within(0.0001f),
                "The settings default and the motion helper's default must be the same number.");

            settings.SwimSpeedFactor = 99.0f;

            Assert.That(settings.SwimSpeedFactor,
                Is.EqualTo(BlockiverseSwimMotion.MaximumSwimSpeedFactor).Within(0.0001f));

            settings.SwimSpeedFactor = -5.0f;

            Assert.That(settings.SwimSpeedFactor,
                Is.EqualTo(BlockiverseSwimMotion.MinimumSwimSpeedFactor).Within(0.0001f),
                "A zero factor would strand a swimmer motionless with no way to tell why.");
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
        public void FixedStandingEyeHeightIsAGameConstantNotAUserSetting()
        {
            // The eye-height slider was removed: dialing the view height independently of the
            // collision capsule is what made the player feel too tall. Height is now a single
            // choice between the fixed player size and the player's real height.
            Assert.That(BlockiverseComfortSettings.FixedStandingEyeHeight, Is.EqualTo(1.7f).Within(0.001f));
            Assert.That(
                typeof(BlockiverseComfortSettings).GetProperty("StandingEyeHeight"),
                Is.Null,
                "Eye height must not be a user-adjustable comfort setting.");
        }

        [Test]
        public void SprintAndCrouchDefaultToClickAndHold()
        {
            BlockiverseComfortSettings settings = CreateSettings();

            Assert.That(
                settings.SprintToggleEnabled,
                Is.False,
                "Sprint should default to click-and-hold.");
            Assert.That(
                settings.CrouchToggleEnabled,
                Is.False,
                "Crouch should default to click-and-hold.");
        }

        // Was WorldSpacePresenterAppliesComfortUiScale, retargeted at the UI Toolkit panel
        // placement component that inherited the presenter's scale job. Worth keeping rather
        // than dropping: UiScale is the accessibility setting for players who cannot read a
        // 1.0-scale panel, and a placement path that quietly ignores it fails only in a
        // headset. WorldSpaceUiPlacementController had no test reference at all before this.
        [Test]
        public void WorldSpaceUiPlacementAppliesComfortUiScale()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            settings.UiScale = 1.25f;
            Transform head = CreateObject("Head").transform;
            WorldSpaceUiPlacementController placement =
                CreateObject("World Space Panel").AddComponent<WorldSpaceUiPlacementController>();

            placement.Configure(
                head,
                distance: 1.0f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f);
            placement.ConfigureComfortSettings(settings);
            placement.OnShown(recenter: true);

            Assert.That(
                placement.transform.localScale.x,
                Is.EqualTo(WorldSpaceUiPlacementController.BasePanelScale * 1.25f).Within(0.00001f));

            // Negative control: with no comfort settings attached the panel must still land on
            // the base scale rather than on zero.
            WorldSpaceUiPlacementController unconfigured =
                CreateObject("Unscaled Panel").AddComponent<WorldSpaceUiPlacementController>();
            unconfigured.Configure(head, 1.0f, 0.0f, 0.0f, 0.0f);
            unconfigured.OnShown(recenter: true);

            Assert.That(
                unconfigured.transform.localScale.x,
                Is.EqualTo(WorldSpaceUiPlacementController.BasePanelScale).Within(0.00001f));
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
