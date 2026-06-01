using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;

namespace Blockiverse.VR
{
    /// <summary>
    /// Keeps the <see cref="TunnelingVignetteController"/> default aperture in sync with
    /// <see cref="BlockiverseComfortSettings.VignetteAperture"/> every frame. This lets the
    /// in-game vignette strength slider take effect at runtime without the rig needing a direct
    /// reference to the vignette controller.
    /// </summary>
    [RequireComponent(typeof(TunnelingVignetteController))]
    public sealed class BlockiverseVignetteSettingsDriver : MonoBehaviour
    {
        [SerializeField] BlockiverseComfortSettings comfortSettings;

        TunnelingVignetteController vignetteController;
        float lastAppliedAperture = -1.0f;

        public void Configure(BlockiverseComfortSettings settings)
        {
            comfortSettings = settings;
            lastAppliedAperture = -1.0f;
        }

        void Awake()
        {
            vignetteController = GetComponent<TunnelingVignetteController>();
        }

        void Update()
        {
            if (comfortSettings == null || vignetteController == null)
                return;

            float aperture = comfortSettings.VignetteAperture;

            if (Mathf.Approximately(aperture, lastAppliedAperture))
                return;

            lastAppliedAperture = aperture;
            VignetteParameters p = vignetteController.defaultParameters;
            p.apertureSize = aperture;
            vignetteController.defaultParameters = p;
        }
    }
}
