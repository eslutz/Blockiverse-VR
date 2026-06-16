using Blockiverse.Gameplay;
using UnityEngine;

namespace Blockiverse.VR
{
    // Persists the player-facing comfort and feedback settings across launches via PlayerPrefs:
    // loads them over the components' serialized defaults on startup, then writes back whenever a
    // value changes (debounced to a slow poll) and on pause/quit. Without this every option in
    // the comfort menu silently reset each session.
    [DisallowMultipleComponent]
    public sealed class BlockiverseSettingsPersistence : MonoBehaviour
    {
        const string KeyPrefix = "Blockiverse.Settings.";
        public const string DominantHandPrefsKey = KeyPrefix + "DominantHand";
        const int VignettePrefsVersion = 2;
        const float PollIntervalSeconds = 5.0f;

        [SerializeField] BlockiverseComfortSettings comfortSettings;
        [SerializeField] BlockiverseFeedbackSettings feedbackSettings;

        float nextPollTime;
        int lastSavedHash;
        bool loaded;

        void Start()
        {
            ResolveReferences();
            LoadSettings();
        }

        void Update()
        {
            if (!loaded || Time.unscaledTime < nextPollTime)
                return;

            nextPollTime = Time.unscaledTime + PollIntervalSeconds;
            SaveIfChanged();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                SaveIfChanged();
        }

        void OnApplicationQuit()
        {
            SaveIfChanged();
        }

        void ResolveReferences()
        {
            if (comfortSettings == null)
                comfortSettings = GetComponent<BlockiverseComfortSettings>() ??
                    FindFirstObjectByType<BlockiverseComfortSettings>(FindObjectsInactive.Include);

            if (feedbackSettings == null)
                feedbackSettings = GetComponent<BlockiverseFeedbackSettings>() ??
                    FindFirstObjectByType<BlockiverseFeedbackSettings>(FindObjectsInactive.Include);
        }

        void LoadSettings()
        {
            if (comfortSettings != null)
            {
                comfortSettings.LocomotionMode = (BlockiverseLocomotionMode)PlayerPrefs.GetInt(
                    KeyPrefix + "LocomotionMode", (int)comfortSettings.LocomotionMode);
                // A stale or corrupt pref can hold an int outside the enum; fall back to Glide
                // rather than letting an undefined value degrade the rig to Teleport behavior.
                if (!System.Enum.IsDefined(typeof(BlockiverseLocomotionMode), comfortSettings.LocomotionMode))
                    comfortSettings.LocomotionMode = BlockiverseLocomotionMode.Glide;
                comfortSettings.DominantHand = (BlockiverseControllerRole)PlayerPrefs.GetInt(
                    DominantHandPrefsKey, (int)comfortSettings.DominantHand);
                if (!System.Enum.IsDefined(typeof(BlockiverseControllerRole), comfortSettings.DominantHand))
                    comfortSettings.DominantHand = BlockiverseControllerRole.Right;
                comfortSettings.ToggleToMineEnabled = PlayerPrefs.GetInt(
                    KeyPrefix + "ToggleToMine", comfortSettings.ToggleToMineEnabled ? 1 : 0) != 0;
                comfortSettings.ContinuousMoveSpeed = PlayerPrefs.GetFloat(
                    KeyPrefix + "MoveSpeed", comfortSettings.ContinuousMoveSpeed);
                comfortSettings.SmoothTurnEnabled = PlayerPrefs.GetInt(
                    KeyPrefix + "SmoothTurn", comfortSettings.SmoothTurnEnabled ? 1 : 0) != 0;
                comfortSettings.ContinuousTurnSpeed = PlayerPrefs.GetFloat(
                    KeyPrefix + "ContinuousTurnSpeed", comfortSettings.ContinuousTurnSpeed);
                comfortSettings.SnapTurnDegrees = PlayerPrefs.GetFloat(
                    KeyPrefix + "SnapTurnDegrees", comfortSettings.SnapTurnDegrees);
                comfortSettings.StandingEyeHeight = PlayerPrefs.GetFloat(
                    KeyPrefix + "StandingEyeHeight", comfortSettings.StandingEyeHeight);
                comfortSettings.UiScale = PlayerPrefs.GetFloat(
                    KeyPrefix + "UiScale", comfortSettings.UiScale);

                if (HasCurrentVignettePrefs())
                {
                    comfortSettings.VignetteEnabled = PlayerPrefs.GetInt(
                        KeyPrefix + "VignetteEnabled", comfortSettings.VignetteEnabled ? 1 : 0) != 0;
                    comfortSettings.VignetteStrength = PlayerPrefs.GetFloat(
                        KeyPrefix + "VignetteStrength", comfortSettings.VignetteStrength);
                }
                else
                {
                    ResetVignettePrefsForReadableStartup();
                }
            }

            if (feedbackSettings != null)
            {
                feedbackSettings.MasterVolume = PlayerPrefs.GetFloat(KeyPrefix + "MasterVolume", feedbackSettings.MasterVolume);
                feedbackSettings.EffectsVolume = PlayerPrefs.GetFloat(KeyPrefix + "EffectsVolume", feedbackSettings.EffectsVolume);
                feedbackSettings.UiVolume = PlayerPrefs.GetFloat(KeyPrefix + "UiVolume", feedbackSettings.UiVolume);
                feedbackSettings.WeatherVolume = PlayerPrefs.GetFloat(KeyPrefix + "WeatherVolume", feedbackSettings.WeatherVolume);
                feedbackSettings.MusicVolume = PlayerPrefs.GetFloat(KeyPrefix + "MusicVolume", feedbackSettings.MusicVolume);
                feedbackSettings.MuteAll = PlayerPrefs.GetInt(KeyPrefix + "MuteAll", feedbackSettings.MuteAll ? 1 : 0) != 0;
                feedbackSettings.HapticsEnabled = PlayerPrefs.GetInt(KeyPrefix + "HapticsEnabled", feedbackSettings.HapticsEnabled ? 1 : 0) != 0;
                feedbackSettings.HapticIntensity = PlayerPrefs.GetFloat(KeyPrefix + "HapticIntensity", feedbackSettings.HapticIntensity);
                feedbackSettings.ReducedFlash = PlayerPrefs.GetInt(KeyPrefix + "ReducedFlash", feedbackSettings.ReducedFlash ? 1 : 0) != 0;
                feedbackSettings.ReducedParticles = PlayerPrefs.GetInt(KeyPrefix + "ReducedParticles", feedbackSettings.ReducedParticles ? 1 : 0) != 0;
            }

            lastSavedHash = ComputeSettingsHash();
            loaded = true;
        }

        void SaveIfChanged()
        {
            int hash = ComputeSettingsHash();
            if (hash == lastSavedHash)
                return;

            lastSavedHash = hash;

            if (comfortSettings != null)
            {
                PlayerPrefs.SetInt(KeyPrefix + "LocomotionMode", (int)comfortSettings.LocomotionMode);
                PlayerPrefs.SetInt(DominantHandPrefsKey, (int)comfortSettings.DominantHand);
                PlayerPrefs.SetInt(KeyPrefix + "ToggleToMine", comfortSettings.ToggleToMineEnabled ? 1 : 0);
                PlayerPrefs.SetFloat(KeyPrefix + "MoveSpeed", comfortSettings.ContinuousMoveSpeed);
                PlayerPrefs.SetInt(KeyPrefix + "SmoothTurn", comfortSettings.SmoothTurnEnabled ? 1 : 0);
                PlayerPrefs.SetFloat(KeyPrefix + "ContinuousTurnSpeed", comfortSettings.ContinuousTurnSpeed);
                PlayerPrefs.SetFloat(KeyPrefix + "SnapTurnDegrees", comfortSettings.SnapTurnDegrees);
                PlayerPrefs.SetFloat(KeyPrefix + "StandingEyeHeight", comfortSettings.StandingEyeHeight);
                PlayerPrefs.SetFloat(KeyPrefix + "UiScale", comfortSettings.UiScale);
                PlayerPrefs.SetInt(KeyPrefix + "VignetteEnabled", comfortSettings.VignetteEnabled ? 1 : 0);
                PlayerPrefs.SetFloat(KeyPrefix + "VignetteStrength", comfortSettings.VignetteStrength);
                PlayerPrefs.SetInt(KeyPrefix + "VignettePrefsVersion", VignettePrefsVersion);
            }

            if (feedbackSettings != null)
            {
                PlayerPrefs.SetFloat(KeyPrefix + "MasterVolume", feedbackSettings.MasterVolume);
                PlayerPrefs.SetFloat(KeyPrefix + "EffectsVolume", feedbackSettings.EffectsVolume);
                PlayerPrefs.SetFloat(KeyPrefix + "UiVolume", feedbackSettings.UiVolume);
                PlayerPrefs.SetFloat(KeyPrefix + "WeatherVolume", feedbackSettings.WeatherVolume);
                PlayerPrefs.SetFloat(KeyPrefix + "MusicVolume", feedbackSettings.MusicVolume);
                PlayerPrefs.SetInt(KeyPrefix + "MuteAll", feedbackSettings.MuteAll ? 1 : 0);
                PlayerPrefs.SetInt(KeyPrefix + "HapticsEnabled", feedbackSettings.HapticsEnabled ? 1 : 0);
                PlayerPrefs.SetFloat(KeyPrefix + "HapticIntensity", feedbackSettings.HapticIntensity);
                PlayerPrefs.SetInt(KeyPrefix + "ReducedFlash", feedbackSettings.ReducedFlash ? 1 : 0);
                PlayerPrefs.SetInt(KeyPrefix + "ReducedParticles", feedbackSettings.ReducedParticles ? 1 : 0);
            }

            PlayerPrefs.Save();
        }

        int ComputeSettingsHash()
        {
            unchecked
            {
                int hash = 17;
                if (comfortSettings != null)
                {
                    hash = hash * 31 + (int)comfortSettings.LocomotionMode;
                    hash = hash * 31 + (int)comfortSettings.DominantHand;
                    hash = hash * 31 + (comfortSettings.ToggleToMineEnabled ? 1 : 0);
                    hash = hash * 31 + comfortSettings.ContinuousMoveSpeed.GetHashCode();
                    hash = hash * 31 + (comfortSettings.SmoothTurnEnabled ? 1 : 0);
                    hash = hash * 31 + comfortSettings.ContinuousTurnSpeed.GetHashCode();
                    hash = hash * 31 + comfortSettings.SnapTurnDegrees.GetHashCode();
                    hash = hash * 31 + comfortSettings.StandingEyeHeight.GetHashCode();
                    hash = hash * 31 + comfortSettings.UiScale.GetHashCode();
                    hash = hash * 31 + (comfortSettings.VignetteEnabled ? 1 : 0);
                    hash = hash * 31 + comfortSettings.VignetteStrength.GetHashCode();
                }

                if (feedbackSettings != null)
                {
                    hash = hash * 31 + feedbackSettings.MasterVolume.GetHashCode();
                    hash = hash * 31 + feedbackSettings.EffectsVolume.GetHashCode();
                    hash = hash * 31 + feedbackSettings.UiVolume.GetHashCode();
                    hash = hash * 31 + feedbackSettings.WeatherVolume.GetHashCode();
                    hash = hash * 31 + feedbackSettings.MusicVolume.GetHashCode();
                    hash = hash * 31 + (feedbackSettings.MuteAll ? 1 : 0);
                    hash = hash * 31 + (feedbackSettings.HapticsEnabled ? 1 : 0);
                    hash = hash * 31 + feedbackSettings.HapticIntensity.GetHashCode();
                    hash = hash * 31 + (feedbackSettings.ReducedFlash ? 1 : 0);
                    hash = hash * 31 + (feedbackSettings.ReducedParticles ? 1 : 0);
                }

                return hash;
            }
        }

        static bool HasCurrentVignettePrefs()
        {
            return PlayerPrefs.GetInt(KeyPrefix + "VignettePrefsVersion", 0) >= VignettePrefsVersion;
        }

        void ResetVignettePrefsForReadableStartup()
        {
            comfortSettings.VignetteEnabled = false;
            comfortSettings.VignetteStrength = 0.0f;
            PlayerPrefs.DeleteKey(KeyPrefix + "VignetteEnabled");
            PlayerPrefs.DeleteKey(KeyPrefix + "VignetteStrength");
            PlayerPrefs.SetInt(KeyPrefix + "VignettePrefsVersion", VignettePrefsVersion);
            PlayerPrefs.Save();
        }
    }
}
