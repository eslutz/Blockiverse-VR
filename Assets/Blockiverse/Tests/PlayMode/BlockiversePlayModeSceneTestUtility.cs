#pragma warning disable 0618
using System.Collections;
using System.IO;
using Blockiverse.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

    public sealed class BlockiverseRuntimeActionBindingsCleanup : IPostBuildCleanup
    {
        public void Cleanup()
        {
            string streamingAssetsPath = Path.Combine(Application.dataPath, "StreamingAssets");
#if UNITY_EDITOR
            AssetDatabase.DeleteAsset("Assets/StreamingAssets/RuntimeActionBindings.json");
            AssetDatabase.DeleteAsset("Assets/StreamingAssets/RuntimeActionBindings.json.meta");
#endif
            DeleteFileIfPresent(Path.Combine(streamingAssetsPath, "RuntimeActionBindings.json"));
            DeleteFileIfPresent(Path.Combine(streamingAssetsPath, "RuntimeActionBindings.json.meta"));
            DeleteFileIfPresent(Path.Combine(Directory.GetCurrentDirectory(), "RuntimeActionBindings.json"));
#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
        }

        static void DeleteFileIfPresent(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
