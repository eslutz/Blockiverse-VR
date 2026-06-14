using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Blockiverse.Editor
{
    [InitializeOnLoad]
    static class MetaProjectSetupCiGuard
    {
        static MetaProjectSetupCiGuard()
        {
            if (!Application.isBatchMode || Application.platform != RuntimePlatform.LinuxEditor)
                return;

            DisableMetaProjectSetupBackgroundChecks();
        }

        static void DisableMetaProjectSetupBackgroundChecks()
        {
            Type updaterType = Type.GetType("OVRProjectSetupUpdater, Oculus.VR.Editor")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("OVRProjectSetupUpdater"))
                    .FirstOrDefault(type => type != null);
            MethodInfo setupTemporaryRegistry = updaterType?.GetMethod(
                "SetupTemporaryRegistry",
                BindingFlags.Static | BindingFlags.NonPublic);
            setupTemporaryRegistry?.Invoke(null, null);
        }
    }
}
