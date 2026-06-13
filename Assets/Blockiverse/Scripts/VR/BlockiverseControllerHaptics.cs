using UnityEngine;
using UnityEngine.XR;

namespace Blockiverse.VR
{
    public readonly struct BlockiverseHapticPattern
    {
        public BlockiverseHapticPattern(float amplitude, float durationSeconds)
        {
            Amplitude = Mathf.Clamp01(amplitude);
            DurationSeconds = Mathf.Max(0f, durationSeconds);
        }

        public float Amplitude { get; }
        public float DurationSeconds { get; }

        public static BlockiverseHapticPattern BlockBreak => new(0.6f, 0.05f);
        public static BlockiverseHapticPattern BlockPlace => new(0.4f, 0.04f);
        public static BlockiverseHapticPattern UiTick => new(0.25f, 0.02f);
        public static BlockiverseHapticPattern CraftSuccess => new(0.28f, 0.04f);
        public static BlockiverseHapticPattern CraftFail => new(0.18f, 0.06f);
        public static BlockiverseHapticPattern PlayerDamage => new(0.35f, 0.08f);
        public static BlockiverseHapticPattern LowHealth => new(0.22f, 0.12f);
        public static BlockiverseHapticPattern PlayerDeath => new(0.5f, 0.18f);
        public static BlockiverseHapticPattern TeleportLand => new(0.22f, 0.04f);
        public static BlockiverseHapticPattern SnapTurn => new(0.16f, 0.025f);

        public BlockiverseHapticPattern Scale(float intensity)
        {
            return new BlockiverseHapticPattern(Amplitude * Mathf.Clamp01(intensity), DurationSeconds);
        }
    }

    public sealed class BlockiverseControllerHaptics : MonoBehaviour
    {
        [SerializeField] BlockiverseControllerRole role;

        static readonly System.Collections.Generic.List<InputDevice> DeviceScratch = new();
        InputDevice cachedDevice;

        public BlockiverseControllerRole Role => role;

        public void Configure(BlockiverseControllerRole controllerRole)
        {
            role = controllerRole;
            cachedDevice = default;
        }

        public bool SendPattern(BlockiverseHapticPattern pattern)
        {
            return SendImpulse(pattern.Amplitude, pattern.DurationSeconds);
        }

        public bool SendImpulse(float amplitude, float durationSeconds)
        {
            if (TryGetCachedImpulseDevice(out InputDevice device))
                return device.SendHapticImpulse(0u, amplitude, durationSeconds);

            return false;
        }

        bool TryGetCachedImpulseDevice(out InputDevice device)
        {
            if (SupportsImpulse(cachedDevice))
            {
                device = cachedDevice;
                return true;
            }

            InputDeviceCharacteristics hand = role == BlockiverseControllerRole.Left
                ? InputDeviceCharacteristics.Left
                : InputDeviceCharacteristics.Right;

            InputDeviceCharacteristics characteristics =
                InputDeviceCharacteristics.Controller | hand;

            DeviceScratch.Clear();
            InputDevices.GetDevicesWithCharacteristics(characteristics, DeviceScratch);

            foreach (InputDevice candidate in DeviceScratch)
            {
                if (SupportsImpulse(candidate))
                {
                    cachedDevice = candidate;
                    device = candidate;
                    return true;
                }
            }

            device = default;
            return false;
        }

        static bool SupportsImpulse(InputDevice device) =>
            device.isValid &&
            device.TryGetHapticCapabilities(out HapticCapabilities capabilities) &&
            capabilities.supportsImpulse;
    }
}
