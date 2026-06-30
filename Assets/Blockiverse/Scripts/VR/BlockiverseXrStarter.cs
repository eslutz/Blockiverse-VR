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

            if (!settings.Manager.isInitializationComplete)
            {
                // Manager is not initialized. Automatic Loading should still be true,
                // so it might be in progress or failed.
                return;
            }

            if (!settings.Manager.activeLoader)
            {
                // No active loader.
                return;
            }

            // Start subsystems manually.
            settings.Manager.StartSubsystems();
        }
    }
}
