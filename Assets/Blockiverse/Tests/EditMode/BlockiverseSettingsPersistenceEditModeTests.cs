using System.Collections.Generic;
using System.Reflection;
using Blockiverse.VR;
using Blockiverse.Core;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseSettingsPersistenceEditModeTests
    {
        const string KeyPrefix = "Blockiverse.Settings.";
        const int VignettePrefsVersion = 3;

        readonly List<GameObject> objectsToDestroy = new();

        [SetUp]
        public void SetUp()
        {
            ClearPrefs();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
                if (target != null)
                    Object.DestroyImmediate(target);

            objectsToDestroy.Clear();
            ClearPrefs();
        }

        [Test]
        public void StaleVignettePrefsResetToOpenStartupView()
        {
            PlayerPrefs.SetFloat(KeyPrefix + "MoveSpeed", 2.4f);
            PlayerPrefs.SetInt(KeyPrefix + "VignetteEnabled", 1);
            PlayerPrefs.SetFloat(KeyPrefix + "VignetteStrength", 1.0f);

            BlockiverseComfortSettings settings = CreateSettingsWithPersistence();

            Assert.That(settings.ContinuousMoveSpeed, Is.EqualTo(2.4f).Within(0.001f));
            Assert.That(settings.VignetteEnabled, Is.True);
            Assert.That(settings.VignetteStrength, Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(settings.VignetteAperture, Is.EqualTo(0.88f).Within(0.001f));
            Assert.That(PlayerPrefs.HasKey(KeyPrefix + "VignetteEnabled"), Is.False);
            Assert.That(PlayerPrefs.HasKey(KeyPrefix + "VignetteStrength"), Is.False);
            Assert.That(PlayerPrefs.GetInt(KeyPrefix + "VignettePrefsVersion", 0), Is.EqualTo(VignettePrefsVersion));
        }

        [Test]
        public void CurrentVignettePrefsRemainLoadableAfterMigration()
        {
            PlayerPrefs.SetInt(KeyPrefix + "VignettePrefsVersion", VignettePrefsVersion);
            PlayerPrefs.SetInt(KeyPrefix + "VignetteEnabled", 1);
            PlayerPrefs.SetFloat(KeyPrefix + "VignetteStrength", 0.5f);

            BlockiverseComfortSettings settings = CreateSettingsWithPersistence();

            Assert.That(settings.VignetteEnabled, Is.True);
            Assert.That(settings.VignetteStrength, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(settings.VignetteAperture, Is.EqualTo(0.8f).Within(0.001f));
        }

        BlockiverseComfortSettings CreateSettingsWithPersistence()
        {
            GameObject target = new("Settings Persistence");
            objectsToDestroy.Add(target);

            BlockiverseComfortSettings settings = target.AddComponent<BlockiverseComfortSettings>();
            BlockiverseSettingsPersistence persistence = target.AddComponent<BlockiverseSettingsPersistence>();
            MethodInfo startMethod = typeof(BlockiverseSettingsPersistence)
                .GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(startMethod, Is.Not.Null);
            startMethod.Invoke(persistence, null);
            return settings;
        }

        static void ClearPrefs()
        {
            PlayerPrefs.DeleteKey(KeyPrefix + "MoveSpeed");
            PlayerPrefs.DeleteKey(KeyPrefix + "VignetteEnabled");
            PlayerPrefs.DeleteKey(KeyPrefix + "VignetteStrength");
            PlayerPrefs.DeleteKey(KeyPrefix + "VignettePrefsVersion");
            PlayerPrefs.Save();
        }
    }
}
