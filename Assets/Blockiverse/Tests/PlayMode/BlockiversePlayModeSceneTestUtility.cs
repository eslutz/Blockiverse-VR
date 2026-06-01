using System.Collections;
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
            yield return CleanupTrackedPoseDrivers();

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

            Assert.That(operation, Is.Not.Null);

            while (!operation.isDone)
                yield return null;
        }

        public static IEnumerator CleanupTrackedPoseDrivers()
        {
            DisableTrackedPoseDrivers();
            yield return null;
            DisableTrackedPoseDrivers();
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
