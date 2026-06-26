using System;
using System.Reflection;
using UnityEngine;

namespace Blockiverse.VR
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseDominantHandResolver : MonoBehaviour
    {
        [SerializeField] BlockiverseComfortSettings comfortSettings;

        public void Configure(BlockiverseComfortSettings settings)
        {
            comfortSettings = settings;
        }

        void Awake()
        {
            ApplySystemDominantHandIfUnset();
        }

        public bool ApplySystemDominantHandIfUnset()
        {
            ResolveSettings();

            if (comfortSettings == null ||
                PlayerPrefs.HasKey(BlockiverseSettingsPersistence.DominantHandPrefsKey) ||
                !TryResolveSystemDominantHand(out BlockiverseControllerRole dominantHand))
            {
                return false;
            }

            comfortSettings.DominantHand = dominantHand;
            return true;
        }

        public static bool TryResolveSystemDominantHand(out BlockiverseControllerRole dominantHand)
        {
            dominantHand = BlockiverseControllerRole.Right;

            try
            {
                Type inputType = Type.GetType("OVRInput, Oculus.VR");
                MethodInfo getDominantHand = inputType?.GetMethod(
                    "GetDominantHand",
                    BindingFlags.Public | BindingFlags.Static);

                if (getDominantHand == null)
                    return false;

                object result = getDominantHand.Invoke(null, null);
                string value = result?.ToString();

                if (string.Equals(value, "LeftHanded", StringComparison.Ordinal))
                {
                    dominantHand = BlockiverseControllerRole.Left;
                    return true;
                }

                if (string.Equals(value, "RightHanded", StringComparison.Ordinal))
                {
                    dominantHand = BlockiverseControllerRole.Right;
                    return true;
                }
            }
            catch (Exception)
            {
                // System dominant-hand lookup is a startup preference hint only; the in-game
                // Comfort setting remains authoritative and must keep the rig bootable.
            }

            return false;
        }

        void ResolveSettings()
        {
            if (comfortSettings != null)
                return;

            comfortSettings = GetComponent<BlockiverseComfortSettings>() ??
                FindAnyObjectByType<BlockiverseComfortSettings>(FindObjectsInactive.Include);
        }
    }
}
