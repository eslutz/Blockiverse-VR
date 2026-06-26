using System.Collections.Generic;
using Blockiverse.VR;
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
        public void DefaultVignetteStartsOpenForReadableTitleMenu()
        {
            BlockiverseComfortSettings settings = CreateSettings();

            Assert.That(settings.VignetteEnabled, Is.False);
            Assert.That(settings.VignetteStrength, Is.EqualTo(0.0f));
            Assert.That(settings.SnapTurnAroundEnabled, Is.True);
            Assert.That(settings.VignetteAperture, Is.EqualTo(1.0f).Within(0.001f));
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
        public void WorldSpacePresenterAppliesComfortUiScale()
        {
            BlockiverseComfortSettings settings = CreateSettings();
            settings.UiScale = 1.25f;
            Transform head = CreateObject("Head").transform;
            BlockiverseUiToolkitMenuPresenter presenter =
                CreateObject("UI Toolkit Menu Presenter").AddComponent<BlockiverseUiToolkitMenuPresenter>();

            presenter.ConfigureWorldSpaceTarget(
                presenter.gameObject,
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
            var target = new GameObject(name);
            objectsToDestroy.Add(target);
            return target;
        }
    }
}
