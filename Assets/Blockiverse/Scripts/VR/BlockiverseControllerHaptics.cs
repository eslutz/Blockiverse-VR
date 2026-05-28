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
    }

    public sealed class BlockiverseControllerHaptics : MonoBehaviour
    {
        [SerializeField] BlockiverseControllerRole role;

        public BlockiverseControllerRole Role => role;

        public void Configure(BlockiverseControllerRole controllerRole)
        {
            role = controllerRole;
        }

        public bool SendPattern(BlockiverseHapticPattern pattern)
        {
            return SendImpulse(pattern.Amplitude, pattern.DurationSeconds);
        }

        public bool SendImpulse(float amplitude, float durationSeconds)
        {
            InputDeviceCharacteristics hand = role == BlockiverseControllerRole.Left
                ? InputDeviceCharacteristics.Left
                : InputDeviceCharacteristics.Right;

            InputDeviceCharacteristics characteristics =
                InputDeviceCharacteristics.Controller | hand;

            var devices = new System.Collections.Generic.List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(characteristics, devices);

            foreach (InputDevice device in devices)
            {
                if (device.isValid && device.TryGetHapticCapabilities(out HapticCapabilities capabilities) &&
                    capabilities.supportsImpulse)
                {
                    return device.SendHapticImpulse(0u, amplitude, durationSeconds);
                }
            }

            return false;
        }
    }
}
