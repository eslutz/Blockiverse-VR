using System;
using Blockiverse.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace Blockiverse.VR
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseXrUiInteractionLab : MonoBehaviour
    {
        static readonly string[] DisabledNormalLaunchBehaviourNames =
        {
            "Blockiverse.UI.BlockiverseMenuController",
            "Blockiverse.Gameplay.CreativeWorldManager",
            "Blockiverse.Gameplay.WeatherFeedbackController",
            "Blockiverse.Gameplay.BlockiverseVfxCuePlayer",
            "Blockiverse.Gameplay.BlockiverseVfxPool",
            "Blockiverse.Gameplay.BlockiverseMusicController",
            "Blockiverse.Gameplay.PerformanceStatsOverlay",
        };

        static readonly string[] HiddenNormalLaunchObjectNames =
        {
            "Startup Loading Overlay",
            "Artwork",
            "Controller Mapping Popup",
            "Title Menu",
            "Pause Menu",
            "Block Menu",
            "Survival HUD",
            "Creative Tools Panel",
            "Comfort Settings Menu",
        };

        [SerializeField] Canvas[] labCanvases = Array.Empty<Canvas>();
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] Button[] testButtons = Array.Empty<Button>();
        [SerializeField] TMP_Text[] testStatusLabels = Array.Empty<TMP_Text>();
        [SerializeField] string[] testOptionNames = Array.Empty<string>();
        [SerializeField] bool keepNormalLaunchCanvasesHidden = true;

        float keepAliveUntil;
        float nextDiagnosticsAt;
        string lastProbeMessage = "Ready. No normal launch menu or generated world is active.";
        string rayDiagnostics = "Ray: waiting for first update.";

        public void Configure(
            Canvas[] canvases,
            TMP_Text status,
            Button[] buttons,
            TMP_Text[] statusLabels,
            string[] optionNames)
        {
            labCanvases = canvases ?? Array.Empty<Canvas>();
            statusLabel = status;
            testButtons = buttons ?? Array.Empty<Button>();
            testStatusLabels = statusLabels ?? Array.Empty<TMP_Text>();
            testOptionNames = optionNames ?? Array.Empty<string>();
        }

        public void Report(string message)
        {
            lastProbeMessage = string.IsNullOrWhiteSpace(message) ? lastProbeMessage : message;
            RenderStatus();
        }

        void RenderStatus()
        {
            if (statusLabel == null)
                return;

            statusLabel.text =
                "XR UI Lab\n" +
                "Aim at each button. The ray should stop on the panel before trigger press.\n\n" +
                lastProbeMessage + "\n" +
                rayDiagnostics;
        }

        void Awake()
        {
            keepAliveUntil = Time.unscaledTime + 15.0f;
            InstallInteractionProbes();
            PrepareLab();
        }

        void Start()
        {
            InstallInteractionProbes();
            PrepareLab();
            Report("Ready. No normal launch menu or generated world is active.");
        }

        void LateUpdate()
        {
            if (Time.unscaledTime <= keepAliveUntil || keepNormalLaunchCanvasesHidden)
                HideNormalLaunchVisuals();

            DisableLabLocomotion();
            ForceLabUiLayer();
            ForceInteractionRayVisible();
            UpdateRayDiagnostics();
        }

        void PrepareLab()
        {
            BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false);
            DisableNormalLaunchBehaviours();
            HideNormalLaunchVisuals();
            DisableLabLocomotion();
            ForceLabUiLayer();
            ForceInteractionRayVisible();
        }

        void InstallInteractionProbes()
        {
            for (int i = 0; i < testButtons.Length; i++)
            {
                Button button = testButtons[i];
                if (button == null)
                    continue;

                BlockiverseXrUiInteractionProbe probe = button.GetComponent<BlockiverseXrUiInteractionProbe>();
                if (probe == null)
                    probe = button.gameObject.AddComponent<BlockiverseXrUiInteractionProbe>();

                TMP_Text localStatus = i < testStatusLabels.Length ? testStatusLabels[i] : null;
                string optionName = i < testOptionNames.Length ? testOptionNames[i] : button.gameObject.name;
                probe.Configure(optionName, localStatus);
            }
        }

        void DisableNormalLaunchBehaviours()
        {
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null || behaviour == this)
                    continue;

                string typeName = behaviour.GetType().FullName;
                foreach (string disabledName in DisabledNormalLaunchBehaviourNames)
                {
                    if (!string.Equals(typeName, disabledName, StringComparison.Ordinal))
                        continue;

                    behaviour.enabled = false;
                    break;
                }
            }
        }

        void HideNormalLaunchVisuals()
        {
            HideNormalLaunchNamedObjects();
            HideNormalLaunchCanvases();
            DisableNormalLaunchParticles();
        }

        void HideNormalLaunchNamedObjects()
        {
            foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate == null || candidate.IsChildOf(transform))
                    continue;

                foreach (string hiddenName in HiddenNormalLaunchObjectNames)
                {
                    if (!string.Equals(candidate.name, hiddenName, StringComparison.Ordinal))
                        continue;

                    candidate.gameObject.SetActive(false);
                    break;
                }
            }
        }

        void HideNormalLaunchCanvases()
        {
            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas == null || IsLabCanvas(canvas))
                    continue;

                canvas.enabled = false;
                canvas.gameObject.SetActive(false);
            }
        }

        void DisableNormalLaunchParticles()
        {
            foreach (ParticleSystem particleSystem in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (particleSystem == null || particleSystem.transform.IsChildOf(transform))
                    continue;

                particleSystem.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.gameObject.SetActive(false);
            }
        }

        static void DisableLabLocomotion()
        {
            foreach (GravityProvider gravityProvider in FindObjectsByType<GravityProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (gravityProvider == null)
                    continue;

                gravityProvider.useGravity = false;
                gravityProvider.enabled = false;
            }

            foreach (JumpProvider jumpProvider in FindObjectsByType<JumpProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (jumpProvider != null)
                    jumpProvider.enabled = false;
            }

            foreach (ContinuousMoveProvider moveProvider in FindObjectsByType<ContinuousMoveProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (moveProvider != null)
                    moveProvider.enabled = false;
            }

            foreach (TeleportationProvider teleportProvider in FindObjectsByType<TeleportationProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (teleportProvider != null)
                    teleportProvider.enabled = false;
            }

            foreach (ContinuousTurnProvider turnProvider in FindObjectsByType<ContinuousTurnProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (turnProvider != null)
                    turnProvider.enabled = false;
            }

            foreach (SnapTurnProvider turnProvider in FindObjectsByType<SnapTurnProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (turnProvider != null)
                    turnProvider.enabled = false;
            }
        }

        void ForceLabUiLayer()
        {
            int interactionLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);
            if (interactionLayer < 0)
                interactionLayer = BlockiverseProject.InteractionLayerIndex;

            foreach (Canvas canvas in labCanvases)
            {
                if (canvas == null)
                    continue;

                SetLayerRecursively(canvas.gameObject, interactionLayer);
            }
        }

        static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null || layer < 0)
                return;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(includeInactive: true))
                child.gameObject.layer = layer;
        }

        bool IsLabCanvas(Canvas canvas)
        {
            if (canvas.transform.IsChildOf(transform))
                return true;

            foreach (Canvas labCanvas in labCanvases)
            {
                if (labCanvas == canvas)
                    return true;
            }

            return false;
        }

        static void ForceInteractionRayVisible()
        {
            foreach (XRRayInteractor ray in FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (ray == null)
                    continue;

                bool isInteractionRay = ray.name.IndexOf("Interaction", StringComparison.OrdinalIgnoreCase) >= 0;
                ray.gameObject.SetActive(isInteractionRay);

                if (!isInteractionRay)
                    continue;

                ray.enableUIInteraction = true;
                ray.blockUIOnInteractableSelection = false;

                XRInteractorLineVisual lineVisual = ray.GetComponent<XRInteractorLineVisual>();
                if (lineVisual == null)
                    continue;

                lineVisual.enabled = true;
                lineVisual.overrideInteractorLineLength = false;
                lineVisual.stopLineAtFirstRaycastHit = true;
            }
        }

        void UpdateRayDiagnostics()
        {
            if (Time.unscaledTime < nextDiagnosticsAt)
                return;

            nextDiagnosticsAt = Time.unscaledTime + 0.20f;

            XRRayInteractor ray = FindInteractionRay();
            if (ray == null)
            {
                rayDiagnostics = "Ray: no active Interaction XRRayInteractor found.";
                RenderStatus();
                return;
            }

            string uiHit = ray.TryGetCurrentUIRaycastResult(out RaycastResult uiResult)
                ? $"{uiResult.gameObject.name} {uiResult.distance:F2}m"
                : "none";
            string physicsHit = ray.TryGetCurrent3DRaycastHit(out RaycastHit hit)
                ? $"{hit.collider.name} {hit.distance:F2}m"
                : "none";
            string uiModel = ray.TryGetUIModel(out var model)
                ? $"model={model.raycastPoints.Count}pts select={model.select}"
                : "model=none";

            rayDiagnostics =
                $"Ray: {ray.name} active={ray.isActiveAndEnabled} UI={uiHit} 3D={physicsHit} {uiModel}";
            RenderStatus();
        }

        static XRRayInteractor FindInteractionRay()
        {
            foreach (XRRayInteractor ray in FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (ray == null || !ray.isActiveAndEnabled)
                    continue;

                if (ray.name.IndexOf("Interaction", StringComparison.OrdinalIgnoreCase) >= 0)
                    return ray;
            }

            return null;
        }
    }

    [DisallowMultipleComponent]
    public sealed class BlockiverseXrUiInteractionProbe : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerClickHandler
    {
        [SerializeField] string optionName;
        [SerializeField] TMP_Text localStatusLabel;

        Button button;
        int hoverEnterCount;
        int hoverExitCount;
        int pointerDownCount;
        int pointerUpCount;
        int pointerClickCount;
        int buttonClickCount;

        public void Configure(string option, TMP_Text statusLabel)
        {
            optionName = option;
            localStatusLabel = statusLabel;
            Render("ready");
        }

        void Awake()
        {
            EnsureButton();
            Render("ready");
        }

        void OnEnable()
        {
            EnsureButton();
            if (button != null)
                button.onClick.AddListener(OnButtonClicked);
        }

        void OnDisable()
        {
            if (button != null)
                button.onClick.RemoveListener(OnButtonClicked);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hoverEnterCount++;
            Render("hover enter");
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hoverExitCount++;
            Render("hover exit");
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pointerDownCount++;
            Render("pointer down");
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pointerUpCount++;
            Render("pointer up");
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            pointerClickCount++;
            Render("pointer click");
        }

        void OnButtonClicked()
        {
            buttonClickCount++;
            Render("Button.onClick");
        }

        void EnsureButton()
        {
            if (button == null)
                button = GetComponent<Button>();
        }

        void Render(string eventName)
        {
            string label = string.IsNullOrWhiteSpace(optionName) ? gameObject.name : optionName;
            string message =
                $"{label}\n" +
                $"Last: {eventName}\n" +
                $"Enter {hoverEnterCount}  Exit {hoverExitCount}\n" +
                $"Down {pointerDownCount}  Up {pointerUpCount}\n" +
                $"PointerClick {pointerClickCount}  Button.onClick {buttonClickCount}";

            if (localStatusLabel != null)
                localStatusLabel.text = message;

            BlockiverseXrUiInteractionLab lab = FindFirstObjectByType<BlockiverseXrUiInteractionLab>();
            if (lab != null)
                lab.Report(message);
        }
    }
}
