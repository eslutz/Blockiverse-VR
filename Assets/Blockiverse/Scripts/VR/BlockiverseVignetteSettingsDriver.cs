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
        MeshRenderer vignetteRenderer;
        float lastAppliedAperture = -1.0f;
        bool? lastRendererEnabled;

        public void Configure(BlockiverseComfortSettings settings)
        {
            comfortSettings = settings;
            lastAppliedAperture = -1.0f;
            lastRendererEnabled = null;
        }

        void Awake()
        {
            vignetteController = GetComponent<TunnelingVignetteController>();
            vignetteRenderer = GetComponent<MeshRenderer>();
            ApplySettings(force: true);
        }

        void Update()
        {
            ApplySettings(force: false);
        }

        void ApplySettings(bool force)
        {
            if (comfortSettings == null || vignetteController == null)
                return;

            float aperture = comfortSettings.VignetteAperture;

            if (force || !Mathf.Approximately(aperture, lastAppliedAperture))
            {
                lastAppliedAperture = aperture;
                VignetteParameters p = vignetteController.defaultParameters;
                p.apertureSize = aperture;
                vignetteController.defaultParameters = p;
            }

            bool rendererEnabled = comfortSettings.VignetteEnabled;
            if (vignetteRenderer != null && (force || lastRendererEnabled != rendererEnabled))
            {
                vignetteRenderer.enabled = rendererEnabled;
                lastRendererEnabled = rendererEnabled;
            }
        }
    }
}
