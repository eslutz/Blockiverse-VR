using System.Collections.Generic;
using Blockiverse.UI.Toolkit;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Blockiverse.Editor
{
    // Reads a scene into the plain snapshot that XrUiToolkitConfigurationValidator judges.
    //
    // This lives in the Editor assembly, not beside the validator in Blockiverse.UI.Toolkit, for a
    // concrete reason: it reads XRUIInputModule.bypassUIToolkitEvents, and XRUIInputModule's base
    // chain runs through UnityEngine.EventSystems, which ships in the UnityEngine.UI assembly
    // (com.unity.ugui). Referencing it would drag uGUI into the one assembly ADR 0010 keeps free of
    // it — and the asmdef test would fail, correctly. Diagnostics do not need to ship, so the
    // Editor assembly is where this belongs.
    //
    // It is also the only place that searches a scene wholesale. Screen controllers receive their
    // references; they do not go looking.
    public static class XrUiToolkitSceneInspector
    {
        public static XrUiToolkitSceneState Capture(Scene scene)
        {
            int mask = ResolveRayRaycastLayerMask(scene, out bool hasUiRay);
            return Capture(scene, mask, hasUiRay);
        }

        public static XrUiToolkitSceneState Capture(Scene scene, int rayRaycastLayerMask, bool hasUiRay)
        {
            List<XRUIToolkitManager> toolkitManagers = CollectInScene<XRUIToolkitManager>(scene);
            List<PanelInputConfiguration> configurations = CollectInScene<PanelInputConfiguration>(scene);
            List<XRUIInputModule> inputModules = CollectInScene<XRUIInputModule>(scene);
            List<UIDocument> documents = CollectInScene<UIDocument>(scene);

            int enabledManagers = 0;

            foreach (XRUIToolkitManager manager in toolkitManagers)
            {
                if (manager.isActiveAndEnabled)
                    enabledManagers++;
            }

            // Only ENABLED configurations count, and the settings are read off an enabled one.
            // PanelInputConfiguration.current is assigned in OnEnable and cleared in OnDisable, so a
            // disabled instance has no effect at all. Counting it would let a scene whose only
            // configuration sits disabled — world-space input completely dead — report clean, and
            // reading settings off a disabled instance could report `Never` while an enabled one
            // beside it is on `AutoSwitch`.
            var enabledConfigurations = new List<PanelInputConfiguration>();

            foreach (PanelInputConfiguration candidate in configurations)
            {
                if (candidate.isActiveAndEnabled)
                    enabledConfigurations.Add(candidate);
            }

            PanelInputConfiguration configuration =
                enabledConfigurations.Count > 0 ? enabledConfigurations[0] : null;
            XRUIInputModule inputModule = inputModules.Count > 0 ? inputModules[0] : null;

            var panels = new List<XrUiToolkitPanelState>(documents.Count);

            foreach (UIDocument document in documents)
            {
                PanelSettings settings = document.panelSettings;
                Collider collider = document.GetComponent<Collider>();

                // Effective, not merely enabled: Collider.enabled stays true when its GameObject is
                // deactivated, but a collider on an inactive object is not in the physics scene and
                // intercepts nothing.
                bool colliderIsEffective =
                    collider != null && collider.enabled && collider.gameObject.activeInHierarchy;

                panels.Add(new XrUiToolkitPanelState(
                    document.gameObject.name,
                    document.isActiveAndEnabled,
                    settings != null,
                    settings != null && settings.renderMode == PanelRenderMode.WorldSpace,
                    collider != null,
                    colliderIsEffective,
                    document.gameObject.layer,
                    document.parentUI != null));
            }

            // A missing configuration is reported once, as PanelInputConfigurationMissing. Reporting
            // its two settings as "wrong" as well would be three findings for one cause, so the
            // absent case reports the compliant value and lets the missing-component finding stand.
            bool redirectionIsNever = configuration == null ||
                configuration.panelInputRedirection == PanelInputConfiguration.PanelInputRedirection.Never;
            bool processWorldSpaceInput = configuration == null || configuration.processWorldSpaceInput;

            return new XrUiToolkitSceneState(
                enabledManagers,
                enabledConfigurations.Count,
                redirectionIsNever,
                processWorldSpaceInput,
                inputModule != null,
                inputModule != null && inputModule.bypassUIToolkitEvents,
                hasUiRay,
                rayRaycastLayerMask,
                panels);
        }

        // The mask the controller rays actually cast against, read from the rays themselves rather
        // than assumed.
        //
        // `hasUiRay` is reported separately from the mask, because "no rig in this scene" and "a rig
        // whose rays raycast Nothing" are different problems that both produce a mask of 0.
        // Collapsing them lets a genuinely broken rig — enableUIInteraction on, raycastMask empty,
        // so no panel on any layer is reachable — take the "unknown, do not judge" escape hatch and
        // report clean.
        public static int ResolveRayRaycastLayerMask(Scene scene, out bool hasUiRay)
        {
            int mask = 0;
            hasUiRay = false;

            foreach (XRRayInteractor ray in CollectInScene<XRRayInteractor>(scene))
            {
                if (!ray.enableUIInteraction)
                    continue;

                hasUiRay = true;
                mask |= ray.raycastMask.value;
            }

            return mask;
        }

        // GetComponentsInChildren(bool, List<T>) CLEARS the list it is handed, so accumulating across
        // scene roots needs a per-root buffer drained into the result. Passing the accumulator
        // straight in keeps only the last root's components — and in a scene whose UI lives under one
        // root and whose EventSystem lives under another, that silently finds half the scene.
        static List<T> CollectInScene<T>(Scene scene) where T : Component
        {
            var results = new List<T>();
            var buffer = new List<T>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(true, buffer);

                foreach (T component in buffer)
                {
                    if (component != null)
                        results.Add(component);
                }
            }

            return results;
        }
    }
}
