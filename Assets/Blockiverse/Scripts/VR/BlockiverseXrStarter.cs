using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

namespace Blockiverse.VR
{
    /// <summary>
    /// Manually starts XR subsystems on application start.
    /// This allows us to disable 'Automatic Running' in XR settings, which resolves 
    /// a persistent Unity 6 warning: "Call to StopSubsystems without an initialized manager."
    /// </summary>
    internal static class BlockiverseXrStarter
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeXr()
        {
            var settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null)
            {
                return;
            }

            // Create a temporary startup helper GameObject to run the coroutine across scenes
            var go = new GameObject("XR Starter Helper");
            Object.DontDestroyOnLoad(go);
            var helper = go.AddComponent<XrStarterHelper>();
            helper.StartCoroutine(helper.InitializeRoutine(settings.Manager));
        }

        private class XrStarterHelper : MonoBehaviour
        {
            public IEnumerator InitializeRoutine(XRManagerSettings manager)
            {
                // Wait until the manager has completed initialization
                float timeout = Time.realtimeSinceStartup + 10.0f; // 10 second timeout safety net
                while (!manager.isInitializationComplete && Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }

                if (!manager.isInitializationComplete)
                {
                    Debug.LogError("[XR Starter] XR Initialization timed out.");
                    Destroy(gameObject);
                    yield break;
                }

                if (!manager.activeLoader)
                {
                    // No active loader, initialize loader manually if automatic loading failed or was not set
                    Debug.Log("[XR Starter] No active loader found. Initializing loader...");
                    yield return manager.InitializeLoader();
                }

                if (manager.activeLoader)
                {
                    Debug.Log($"[XR Starter] Starting XR Subsystems with loader: {manager.activeLoader.name}");
                    manager.StartSubsystems();
                }
                else
                {
                    Debug.LogError("[XR Starter] Failed to initialize XR loader.");
                }

                Destroy(gameObject);
            }
        }
    }
}
