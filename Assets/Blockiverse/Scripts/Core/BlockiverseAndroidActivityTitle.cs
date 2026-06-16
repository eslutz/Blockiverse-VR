using System;
using UnityEngine;

namespace Blockiverse.Core
{
    public static class BlockiverseAndroidActivityTitle
    {
        public const string Title = BlockiverseProject.ProductName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyOnStartup()
        {
            TryApply();
        }

        public static bool TryApply()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity == null)
                        return false;

                    activity.Call("setTitle", Title);

                    using (var taskDescription =
                           new AndroidJavaObject("android.app.ActivityManager$TaskDescription", Title))
                    {
                        activity.Call("setTaskDescription", taskDescription);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BlockiverseAndroidActivityTitle] Failed to set Android activity title: {ex.Message}");
                return false;
            }
#else
            return false;
#endif
        }
    }
}
