using UnityEngine;

namespace Blockiverse.Gameplay
{
    public enum BlockiverseAudioCategory
    {
        Effects,
        Ui,
        Weather
    }

    [DisallowMultipleComponent]
    public sealed class BlockiverseFeedbackSettings : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float masterVolume = 1.0f;
        [SerializeField, Range(0f, 1f)] float effectsVolume = 1.0f;
        [SerializeField, Range(0f, 1f)] float uiVolume = 1.0f;
        [SerializeField, Range(0f, 1f)] float weatherVolume = 1.0f;
        [SerializeField] bool muteAll;
        [SerializeField] bool hapticsEnabled = true;
        [SerializeField, Range(0f, 1f)] float hapticIntensity = 1.0f;
        [SerializeField] bool reducedFlash;
        [SerializeField] bool reducedParticles;

        public float MasterVolume
        {
            get => masterVolume;
            set => masterVolume = Mathf.Clamp01(value);
        }

        public float EffectsVolume
        {
            get => effectsVolume;
            set => effectsVolume = Mathf.Clamp01(value);
        }

        public float UiVolume
        {
            get => uiVolume;
            set => uiVolume = Mathf.Clamp01(value);
        }

        public float WeatherVolume
        {
            get => weatherVolume;
            set => weatherVolume = Mathf.Clamp01(value);
        }

        public bool MuteAll
        {
            get => muteAll;
            set => muteAll = value;
        }

        public bool HapticsEnabled
        {
            get => hapticsEnabled;
            set => hapticsEnabled = value;
        }

        public float HapticIntensity
        {
            get => hapticIntensity;
            set => hapticIntensity = Mathf.Clamp01(value);
        }

        public bool ReducedFlash
        {
            get => reducedFlash;
            set => reducedFlash = value;
        }

        public bool ReducedParticles
        {
            get => reducedParticles;
            set => reducedParticles = value;
        }

        public float ResolveVolume(BlockiverseAudioCategory category)
        {
            if (muteAll)
                return 0f;

            float categoryVolume = category switch
            {
                BlockiverseAudioCategory.Ui => uiVolume,
                BlockiverseAudioCategory.Weather => weatherVolume,
                _ => effectsVolume
            };

            return Mathf.Clamp01(masterVolume * categoryVolume);
        }

        public float ResolveHapticIntensity()
        {
            return hapticsEnabled
                ? hapticIntensity
                : 0f;
        }

        void OnValidate()
        {
            MasterVolume = masterVolume;
            EffectsVolume = effectsVolume;
            UiVolume = uiVolume;
            WeatherVolume = weatherVolume;
            HapticIntensity = hapticIntensity;
        }
    }
}
