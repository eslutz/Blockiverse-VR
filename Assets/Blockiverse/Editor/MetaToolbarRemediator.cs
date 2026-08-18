using UnityEditor;

namespace Blockiverse.Editor
{
    /// <summary>
    /// Programmatically disables the Meta XR Status Icon on the main toolbar.
    /// This resolves the Unity 6 warning: "We have detected that your project includes custom elements 
    /// added to the Unity Editor's main toolbar using unsupported methods."
    /// </summary>
    [InitializeOnLoad]
    internal static class MetaToolbarRemediator
    {
        private const string StatusIconKey = "Meta.XR.SDK.StatusIcon.Enabled";

        static MetaToolbarRemediator()
        {
            if (EditorPrefs.GetBool(StatusIconKey, true))
            {
                EditorPrefs.SetBool(StatusIconKey, false);
            }
        }
    }
}
