using System.Collections;
using System.IO;
using Blockiverse.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;

namespace Blockiverse.Tests.PlayMode
{
    static class BlockiversePlayModeSceneTestUtility
    {
        public static IEnumerator LoadSceneSingle(string sceneName)
        {
            BlockiverseRuntimeState.Reset();
            yield return CleanupTrackedPoseDrivers();

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

            Assert.That(operation, Is.Not.Null);

            while (!operation.isDone)
                yield return null;
        }

        public static IEnumerator CleanupTrackedPoseDrivers()
        {
            BlockiverseRuntimeState.Reset();
            CleanupRuntimeActionBindings();
            DisableTrackedPoseDrivers();
            yield return null;
            CleanupRuntimeActionBindings();
            DisableTrackedPoseDrivers();
        }

        static void CleanupRuntimeActionBindings()
        {
            DeleteFileIfPresent(Path.Combine(Application.dataPath, "StreamingAssets", "RuntimeActionBindings.json"));
            DeleteFileIfPresent(Path.Combine(Application.dataPath, "StreamingAssets", "RuntimeActionBindings.json.meta"));
            DeleteFileIfPresent(Path.Combine(Directory.GetCurrentDirectory(), "RuntimeActionBindings.json"));
        }

        static void DeleteFileIfPresent(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        static void DisableTrackedPoseDrivers()
        {
            foreach (TrackedPoseDriver driver in Object.FindObjectsByType<TrackedPoseDriver>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (driver != null && driver.enabled)
                    driver.enabled = false;
            }
        }
    }
}
