using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Gameplay;
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

        [Test]
        public void ClassicBlockSoundsSurvivesARestart()
        {
            // Persisting a new setting takes three edits — load, save, and the
            // change-detection hash. Miss the hash and the value reads back fine in
            // the same session but never actually reaches PlayerPrefs, so this
            // asserts the write, not just the read.
            (BlockiverseFeedbackSettings feedback, BlockiverseSettingsPersistence persistence) =
                CreateFeedbackWithPersistence();
            Assert.That(feedback.ClassicBlockSoundsEnabled, Is.False, "should default off");

            feedback.ClassicBlockSoundsEnabled = true;
            Invoke(persistence, "SaveIfChanged");

            Assert.That(PlayerPrefs.GetInt(KeyPrefix + "ClassicBlockSounds", 0), Is.EqualTo(1),
                "enabling the setting should reach PlayerPrefs");

            // A fresh component pair stands in for the next launch.
            (BlockiverseFeedbackSettings reloaded, _) = CreateFeedbackWithPersistence();
            Assert.That(reloaded.ClassicBlockSoundsEnabled, Is.True);
        }

        (BlockiverseFeedbackSettings, BlockiverseSettingsPersistence) CreateFeedbackWithPersistence()
        {
            GameObject target = new("Feedback Settings Persistence");
            objectsToDestroy.Add(target);

            BlockiverseFeedbackSettings feedback = target.AddComponent<BlockiverseFeedbackSettings>();
            BlockiverseSettingsPersistence persistence = target.AddComponent<BlockiverseSettingsPersistence>();
            Invoke(persistence, "Start");
            return (feedback, persistence);
        }

        static void Invoke(BlockiverseSettingsPersistence persistence, string methodName)
        {
            MethodInfo method = typeof(BlockiverseSettingsPersistence)
                .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null, $"{methodName} should exist");
            method.Invoke(persistence, null);
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
            PlayerPrefs.DeleteKey(KeyPrefix + "ClassicBlockSounds");
            PlayerPrefs.Save();
        }
    }
}
