using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.MetaAvatars;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.VR;
using Oculus.Avatar2;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Editor.Configuration;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CoreUtils;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        static GameObject CreateXrRigInstance()
        {
            GameObject rig = new(BlockiverseProject.XrRigRootName);
            rig.AddComponent<BlockiverseXRRigMarker>();
            rig.AddComponent<BlockiversePlayerRigAnchor>();
            InputActionAsset inputActions = EnsureInputActions();
            BlockiverseInputRig inputRig = rig.AddComponent<BlockiverseInputRig>();
            inputRig.Configure(inputActions);

            GameObject cameraOffset = new("Camera Offset");
            cameraOffset.transform.SetParent(rig.transform, false);

            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            cameraObject.transform.localPosition = new Vector3(0.0f, 1.6f, 0.0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500.0f;
            cameraObject.AddComponent<AudioListener>();
            TrackedPoseDriver poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
            BlockiverseInputRig.ConfigureHeadPoseDriverActions(poseDriver);

            XROrigin origin = rig.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.Camera = camera;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            inputRig.ConfigureHeadPoseDriver(poseDriver);
            EnsureXrRigLocomotion(rig, inputRig, origin);

            CreateControllerAnchor(
                "Left Controller",
                cameraOffset.transform,
                new Vector3(-0.25f, 1.25f, 0.35f),
                inputRig,
                BlockiverseControllerRole.Left);
            CreateControllerAnchor(
                "Right Controller",
                cameraOffset.transform,
                new Vector3(0.25f, 1.25f, 0.35f),
                inputRig,
                BlockiverseControllerRole.Right);

            EnsureXrRigAvatar(rig);
            EnsureComponent<BlockiverseFoveatedRenderingController>(rig);
            EnsureComponent<BlockiverseComfortTransition>(rig);
            EnsureComponent<BlockiverseTmpFontFallbackBootstrapper>(rig);
            EnsureXrRigComfortMenu(rig, inputRig);
            EnsureXrRigInteraction(rig, inputRig);
            EnsureXrRigTunnelingVignette(rig);
            EnsureXrRigStartupLoadingOverlay(rig);
            EnsureXrRigControllerMappingPopup(rig);
            EnsureXrRigSurvivalHud(rig);
            EnsureXrRigCreativeInputBridge(rig, inputRig);
            EnsureXrRigCreativeFlight(rig, inputRig);
            EnsureXrRigFeedback(rig, inputRig);
            EnsureXrRigGameMenus(rig, inputRig);
            return rig;
        }

        static void EnsureXrRigControllerBindings(GameObject rig)
        {
            InputActionAsset inputActions = EnsureInputActions();
            EnsureComponent<BlockiversePlayerRigAnchor>(rig);
            BlockiverseInputRig inputRig = rig.GetComponent<BlockiverseInputRig>();

            if (inputRig == null)
                inputRig = rig.AddComponent<BlockiverseInputRig>();

            inputRig.Configure(inputActions);

            Transform cameraOffset = rig.transform.Find("Camera Offset");

            if (cameraOffset == null)
            {
                GameObject cameraOffsetObject = new("Camera Offset");
                cameraOffsetObject.transform.SetParent(rig.transform, false);
                cameraOffset = cameraOffsetObject.transform;
            }

            XROrigin origin = rig.GetComponent<XROrigin>();

            if (origin == null)
                origin = rig.AddComponent<XROrigin>();

            if (origin.CameraFloorOffsetObject == null)
                origin.CameraFloorOffsetObject = cameraOffset.gameObject;

            if (origin.Camera == null)
                origin.Camera = cameraOffset.GetComponentInChildren<Camera>(true);

            Camera xrCamera = origin.Camera;
            TrackedPoseDriver poseDriver = xrCamera != null
                ? xrCamera.GetComponent<TrackedPoseDriver>()
                : rig.GetComponentInChildren<TrackedPoseDriver>(true);

            if (poseDriver == null && xrCamera != null)
                poseDriver = xrCamera.gameObject.AddComponent<TrackedPoseDriver>();

            BlockiverseInputRig.ConfigureHeadPoseDriverActions(poseDriver);
            inputRig.ConfigureHeadPoseDriver(poseDriver);
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            EnsureXrRigLocomotion(rig, inputRig, origin);

            EnsureControllerAnchor(
                "Left Controller",
                cameraOffset,
                new Vector3(-0.25f, 1.25f, 0.35f),
                inputRig,
                BlockiverseControllerRole.Left);
            EnsureControllerAnchor(
                "Right Controller",
                cameraOffset,
                new Vector3(0.25f, 1.25f, 0.35f),
                inputRig,
                BlockiverseControllerRole.Right);

            EnsureXrRigAvatar(rig);
            EnsureComponent<BlockiverseFoveatedRenderingController>(rig);
            EnsureComponent<BlockiverseComfortTransition>(rig);
            EnsureComponent<BlockiverseTmpFontFallbackBootstrapper>(rig);
            EnsureXrRigComfortMenu(rig, inputRig);
            EnsureXrRigInteraction(rig, inputRig);
            EnsureXrRigTunnelingVignette(rig);
            EnsureXrRigStartupLoadingOverlay(rig);
            EnsureXrRigControllerMappingPopup(rig);
            EnsureXrRigSurvivalHud(rig);
            EnsureXrRigCreativeInputBridge(rig, inputRig);
            EnsureXrRigCreativeFlight(rig, inputRig);
            EnsureXrRigFeedback(rig, inputRig);
            EnsureXrRigGameMenus(rig, inputRig);
        }

        static void EnsureControllerAnchor(
            string name,
            Transform parent,
            Vector3 localPosition,
            BlockiverseInputRig inputRig,
            BlockiverseControllerRole role)
        {
            Transform existingController = parent.Find(name);

            if (existingController == null)
            {
                CreateControllerAnchor(name, parent, localPosition, inputRig, role);
                return;
            }

            ConfigureControllerAnchor(existingController.gameObject, inputRig, role);
        }

        static void CreateControllerAnchor(
            string name,
            Transform parent,
            Vector3 localPosition,
            BlockiverseInputRig inputRig,
            BlockiverseControllerRole role)
        {
            GameObject controller = new(name);
            controller.transform.SetParent(parent, false);
            controller.transform.localPosition = localPosition;
            ConfigureControllerAnchor(controller, inputRig, role);
        }

        static void ConfigureControllerAnchor(
            GameObject controller,
            BlockiverseInputRig inputRig,
            BlockiverseControllerRole role)
        {
            // Native controller tracking: a TrackedPoseDriver drives the controller transform in
            // Update + BeforeRender, matching the head and removing the old hand-written pose.
            TrackedPoseDriver poseDriver = EnsureComponent<TrackedPoseDriver>(controller);
            BlockiverseInputRig.ConfigureControllerPoseDriverActions(poseDriver, role);
            poseDriver.enabled = true;

            BlockiverseControllerAnchor anchor = EnsureComponent<BlockiverseControllerAnchor>(controller);
            anchor.Configure(role, poseDriver);

            BlockiverseControllerHaptics haptics = EnsureComponent<BlockiverseControllerHaptics>(controller);
            haptics.Configure(role);

            EnsureControllerInteractors(controller, inputRig, role);

            EditorUtility.SetDirty(poseDriver);
            EditorUtility.SetDirty(anchor);
            EditorUtility.SetDirty(haptics);
        }

        // Builds the native interaction (UI + block targeting, right hand only) and teleport rays
        // on each controller, plus the mediator that switches between them while the locomotion
        // mode is Teleport and thumbstick-forward is pushed.
        static XRRayInteractor EnsureControllerInteractors(
            GameObject controller,
            BlockiverseInputRig inputRig,
            BlockiverseControllerRole role)
        {
            Material pointerMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.PointerLineMaterialPath);
            BlockiverseComfortSettings settings = inputRig != null ? inputRig.GetComponent<BlockiverseComfortSettings>() : null;
            string mapName = role == BlockiverseControllerRole.Left
                ? BlockiverseInputActionNames.LeftHandMap
                : BlockiverseInputActionNames.RightHandMap;
            Transform aimPose = EnsureControllerAimPose(controller.transform.parent, role);

            XRRayInteractor interactionRay = null;

            // Only the right controller carries the UI/block interaction ray.
            if (role == BlockiverseControllerRole.Right)
            {
                GameObject interactionRayObject = EnsureChild(controller.transform, InteractionRayName);
                interactionRayObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                interactionRayObject.SetActive(true);

                interactionRay = EnsureComponent<XRRayInteractor>(interactionRayObject);
                interactionRay.lineType = XRRayInteractor.LineType.StraightLine;
                interactionRay.enableUIInteraction = true;
                interactionRay.blockUIOnInteractableSelection = false;
                interactionRay.maxRaycastDistance = CreativeInteractionController.MaxBlockInteractionReachMeters;
                interactionRay.manipulateAttachTransform = false;
                interactionRay.rayOriginTransform = aimPose;
                // Empty interaction layers: the ray never selects 3D interactables (incl. the chunk
                // TeleportationArea). UI still works (separate path) and block targeting uses
                // TryGetCurrent3DRaycastHit on this raycast mask.
                interactionRay.interactionLayers = 0;
                interactionRay.raycastMask = GetInteractionLayerMask();
                interactionRay.uiPressInput = MakeButtonReader("UI Press", FindRigAction(inputRig, mapName, BlockiverseInputActionNames.UiPress));
                interactionRay.uiScrollInput = MakeVector2Reader("UI Scroll", FindRigAction(inputRig, mapName, BlockiverseInputActionNames.UiScroll));
                ConfigureLineVisual(interactionRayObject, pointerMaterial);
                EditorUtility.SetDirty(interactionRay);
            }

            // Both controllers get a teleport ray; the mediator activates it only in Teleport mode.
            GameObject teleportRayObject = EnsureChild(controller.transform, TeleportRayName);
            teleportRayObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            XRRayInteractor teleportRay = EnsureComponent<XRRayInteractor>(teleportRayObject);
            teleportRay.lineType = XRRayInteractor.LineType.ProjectileCurve;
            teleportRay.enableUIInteraction = false;
            teleportRay.manipulateAttachTransform = false;
            teleportRay.rayOriginTransform = aimPose;
            teleportRay.raycastMask = GetInteractionLayerMask();
            // Teleport on thumb-release: selectInput = thumbstick/y composite, OnSelectExited fires on release.
            teleportRay.selectInput = MakeButtonReader("Teleport Select", FindRigAction(inputRig, mapName, BlockiverseInputActionNames.TeleportSelect));
            ConfigureLineVisual(teleportRayObject, pointerMaterial);
            teleportRayObject.SetActive(false);
            EditorUtility.SetDirty(teleportRay);

            BlockiverseLocomotionRayMediator mediator = EnsureComponent<BlockiverseLocomotionRayMediator>(controller);
            mediator.Configure(inputRig, settings, interactionRay, teleportRay, role);
            EditorUtility.SetDirty(mediator);

            return interactionRay;
        }

        static Transform EnsureControllerAimPose(Transform cameraOffset, BlockiverseControllerRole role)
        {
            string aimPoseName = role == BlockiverseControllerRole.Left ? LeftAimPoseName : RightAimPoseName;
            Transform aimPose = cameraOffset != null ? cameraOffset.Find(aimPoseName) : null;

            if (aimPose == null)
            {
                GameObject aimPoseObject = EnsureChild(cameraOffset, aimPoseName);
                aimPose = aimPoseObject.transform;
            }

            aimPose.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            TrackedPoseDriver poseDriver = EnsureComponent<TrackedPoseDriver>(aimPose.gameObject);
            BlockiverseInputRig.ConfigureControllerAimPoseDriverActions(poseDriver, role);
            poseDriver.enabled = true;
            EditorUtility.SetDirty(poseDriver);
            EditorUtility.SetDirty(aimPose.gameObject);
            return aimPose;
        }

        static void ConfigureLineVisual(GameObject rayObject, Material pointerMaterial)
        {
            LineRenderer lineRenderer = EnsureComponent<LineRenderer>(rayObject);
            lineRenderer.useWorldSpace = true;
            lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            if (pointerMaterial != null)
                lineRenderer.sharedMaterial = pointerMaterial;

            lineRenderer.startColor = PointerLineColor;
            lineRenderer.endColor = PointerLineColor;

            XRInteractorLineVisual lineVisual = EnsureComponent<XRInteractorLineVisual>(rayObject);
            lineVisual.lineWidth = 0.01f;
            lineVisual.overrideInteractorLineLength = false;
            lineVisual.stopLineAtFirstRaycastHit = true;

            EditorUtility.SetDirty(lineRenderer);
            EditorUtility.SetDirty(lineVisual);
        }

        // Use InputActionReference mode so the bootstrapper-assigned reader does not take ownership
        // of the action's enable/disable lifecycle. The rig enables/disables the whole
        // InputActionAsset, and InputAction mode would fight that.
        static XRInputButtonReader MakeButtonReader(string name, InputAction action)
        {
            var reader = new XRInputButtonReader(name,
                inputSourceMode: XRInputButtonReader.InputSourceMode.InputActionReference);

            if (action != null)
                reader.inputActionReferencePerformed = InputActionReference.Create(action);

            return reader;
        }

        static XRInputValueReader<Vector2> MakeVector2Reader(string name, InputAction action)
        {
            if (action == null)
                return new XRInputValueReader<Vector2>(name, XRInputValueReader.InputSourceMode.Unused);

            return new XRInputValueReader<Vector2>(name,
                XRInputValueReader.InputSourceMode.InputActionReference)
            {
                inputActionReference = InputActionReference.Create(action)
            };
        }

        static InputAction FindRigAction(BlockiverseInputRig inputRig, string mapName, string actionName)
        {
            InputActionAsset asset = inputRig != null ? inputRig.InputActions : null;
            InputActionMap map = asset?.FindActionMap(mapName, throwIfNotFound: false);
            return map?.FindAction(actionName, throwIfNotFound: false);
        }

        static void EnsureXrRigAvatar(GameObject rig)
        {
            // The XRI input manager feeds OvrPlugin controller/HMD pose data to the avatar entity
            // so that hands track the physical controllers. It must be on the rig (or a child)
            // before the entity is instantiated at runtime.
            EnsureComponent<BlockiverseXriAvatarInputManager>(rig);

            // This component is intentionally unspawned on the local XR rig; the spawned network
            // player prefab owns the NetworkObject used for multiplayer avatar relay.
            BlockiverseNetworkAvatarRig avatarRig = EnsureComponent<BlockiverseNetworkAvatarRig>(rig);
            MetaHorizonAvatarProvider avatarProvider = EnsureComponent<MetaHorizonAvatarProvider>(rig);
            BlockiverseMetaAvatarPresenter avatarPresenter = EnsureComponent<BlockiverseMetaAvatarPresenter>(rig);
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;
            Transform leftHand = cameraOffset != null ? cameraOffset.Find("Left Controller") : null;
            Transform rightHand = cameraOffset != null ? cameraOffset.Find("Right Controller") : null;

            avatarRig.ConfigureTrackingSources(head, leftHand, rightHand);
            avatarRig.SetMetaAvatarAvailable(false);
            avatarRig.ConfigureFallbackProxy(true);
            avatarPresenter.Configure(
                avatarProvider,
                avatarRig,
                head,
                leftHand,
                rightHand,
                MetaAvatarPresentationMode.LocalFirstPerson);
            EditorUtility.SetDirty(avatarRig);
            EditorUtility.SetDirty(avatarProvider);
            EditorUtility.SetDirty(avatarPresenter);
        }

        static void EnsureXrRigComfortMenu(GameObject rig, BlockiverseInputRig inputRig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform leftController = cameraOffset != null ? cameraOffset.Find("Left Controller") : null;
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            BlockiverseComfortSettings settings = rig.GetComponent<BlockiverseComfortSettings>();
            BlockiverseHeightReset heightReset = rig.GetComponent<BlockiverseHeightReset>();
            GameObject menuObject = EnsureRectChildMigrated(cameraOffset, leftController, ComfortMenuName);
            menuObject.transform.localPosition = new Vector3(0.0f, 1.42f, 1.18f);
            menuObject.transform.localRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
            menuObject.transform.localScale = Vector3.one * 0.0013f;

            RectTransform menuRect = menuObject.GetComponent<RectTransform>();
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ComfortMenuSize.x);
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ComfortMenuSize.y);

            Canvas canvas = EnsureComponent<Canvas>(menuObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(menuObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(menuObject);

            GameObject panelObject = EnsureRectChild(menuObject.transform, "Panel");
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image panelImage = EnsureComponent<Image>(panelObject);
            Sprite comfortPanelSprite = GetRoundedSprite();
            if (comfortPanelSprite != null)
            {
                panelImage.sprite = comfortPanelSprite;
                panelImage.type = Image.Type.Sliced;
            }
            panelImage.color = ComfortMenuPanelColor;

            EnsureLabel(
                panelObject.transform,
                "Title",
                "Comfort Settings",
                32,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(32.0f, -36.0f),
                new Vector2(460.0f, 56.0f));

            EnsureButtonControl(
                panelObject.transform,
                "Close Button",
                "Close",
                new Vector2(344.0f, -36.0f),
                new Vector2(144.0f, 48.0f));

            // --- Movement Mode (Glide / Teleport) ---
            EnsureLabel(panelObject.transform, "Movement Label", "Movement Mode", 22,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(32.0f, -90.0f), new Vector2(300.0f, 36.0f));

            Toggle glideToggle = EnsureToggleControl(
                panelObject.transform,
                "Glide Toggle",
                "Glide Motion",
                settings == null || settings.LocomotionMode == BlockiverseLocomotionMode.Glide,
                new Vector2(32.0f, -126.0f));

            Toggle teleportToggle = EnsureToggleControl(
                panelObject.transform,
                "Teleport Toggle",
                "Teleport",
                settings != null && settings.LocomotionMode == BlockiverseLocomotionMode.Teleport,
                new Vector2(32.0f, -170.0f));

            Slider moveSpeedSlider = EnsureSettingsSlider(
                panelObject.transform,
                "Move Speed Slider",
                "Move Speed",
                settings != null ? settings.ContinuousMoveSpeed : 1.8f,
                new Vector2(32.0f, -222.0f),
                minValue: 0.5f,
                maxValue: 4.0f);

            // --- Turning ---
            Toggle smoothTurnToggle = EnsureToggleControl(
                panelObject.transform,
                "Smooth Turn Toggle",
                "Smooth Turn",
                settings != null && settings.SmoothTurnEnabled,
                new Vector2(32.0f, -324.0f));

            Slider snapTurnSlider = EnsureSnapTurnSlider(
                panelObject.transform,
                settings != null ? settings.SnapTurnDegrees : 45.0f,
                new Vector2(32.0f, -370.0f));

            Toggle turnAroundToggle = EnsureToggleControl(
                panelObject.transform,
                "Turn Around Toggle",
                "Turn Around",
                settings == null || settings.SnapTurnAroundEnabled,
                new Vector2(32.0f, -416.0f));

            Slider smoothTurnSpeedSlider = EnsureSettingsSlider(
                panelObject.transform,
                "Smooth Turn Speed Slider",
                "Smooth Turn Speed",
                settings != null ? settings.ContinuousTurnSpeed : 60.0f,
                new Vector2(32.0f, -506.0f),
                minValue: 30.0f,
                maxValue: 180.0f);

            // --- Hand Roles ---
            Toggle leftHandToggle = EnsureToggleControl(
                panelObject.transform,
                "Left Hand Toggle",
                "Left-Handed",
                settings != null && settings.DominantHand == BlockiverseControllerRole.Left,
                new Vector2(32.0f, -610.0f));

            Toggle dominantHandOnlyToggle = EnsureToggleControl(
                panelObject.transform,
                "Dominant Hand Only Toggle",
                "One-Handed Controls",
                settings != null && settings.DominantHandOnlyControls,
                new Vector2(32.0f, -654.0f));

            Toggle toggleToMineToggle = EnsureToggleControl(
                panelObject.transform,
                "Toggle To Mine Toggle",
                "Toggle To Mine",
                settings != null && settings.ToggleToMineEnabled,
                new Vector2(32.0f, -698.0f));

            // --- Vignette ---
            Toggle vignetteToggle = EnsureToggleControl(
                panelObject.transform,
                "Vignette Toggle",
                "Motion Vignette",
                settings == null || settings.VignetteEnabled,
                new Vector2(32.0f, -762.0f));

            Slider vignetteSlider = EnsureVignetteSlider(
                panelObject.transform,
                settings != null ? settings.VignetteStrength : 1.0f,
                new Vector2(32.0f, -806.0f));

            Slider eyeHeightSlider = EnsureSettingsSlider(
                panelObject.transform,
                "Eye Height Slider",
                "Eye Height",
                settings != null ? settings.StandingEyeHeight : 1.6f,
                new Vector2(32.0f, -902.0f),
                minValue: 1.0f,
                maxValue: 2.2f);

            Slider uiScaleSlider = EnsureSettingsSlider(
                panelObject.transform,
                "UI Scale Slider",
                "UI Scale",
                settings != null ? settings.UiScale : 1.0f,
                new Vector2(32.0f, -994.0f),
                minValue: 0.85f,
                maxValue: 1.35f);

            // --- Height Reset ---
            Button heightResetButton = EnsureButtonControl(
                panelObject.transform,
                "Height Reset Button",
                "Reset Height",
                new Vector2(32.0f, -1106.0f));

            if (heightReset != null)
            {
                RemovePersistentListeners(
                    heightResetButton.onClick,
                    heightReset,
                    nameof(BlockiverseHeightReset.ResetHeight));
                UnityEventTools.AddPersistentListener(heightResetButton.onClick, heightReset.ResetHeight);
                EditorUtility.SetDirty(heightResetButton);
            }

            BlockiverseComfortMenu menu = EnsureComponent<BlockiverseComfortMenu>(menuObject);
            menu.Configure(canvas, settings, heightReset);
            menu.ConfigureControls(
                glideToggle,
                teleportToggle,
                smoothTurnToggle,
                snapTurnSlider,
                turnAroundToggle,
                vignetteToggle,
                vignetteSlider,
                leftHandToggle,
                dominantHandOnlyToggle,
                toggleToMineToggle,
                eyeHeightSlider,
                moveSpeedSlider,
                smoothTurnSpeedSlider,
                uiScaleSlider);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(menuObject);
            presenter.Configure(canvas, head, 1.3f, 0.0f, -0.06f, 0.0f, 0.0013f);
            presenter.ConfigureComfortSettings(settings);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            if (inputRig != null)
            {
                // Hardware Menu is owned by BlockiverseMenuController's pause/back route.
                // Comfort settings open through the Settings menu, so scrub stale direct toggles.
                RemovePersistentListeners(
                    inputRig.MenuPressed,
                    menu,
                    nameof(BlockiverseComfortMenu.ToggleVisible));
                RemovePersistentListeners(
                    inputRig.MenuPressed,
                    presenter,
                    nameof(BlockiverseWorldSpacePanelPresenter.ToggleVisible));
                EditorUtility.SetDirty(inputRig);
            }

            EditorUtility.SetDirty(menuObject);
            EditorUtility.SetDirty(menu);
            EditorUtility.SetDirty(presenter);
        }

        static void EnsureXrRigTunnelingVignette(GameObject rig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform headCamera = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (headCamera == null)
                return;

            Transform existing = headCamera.Find(TunnelingVignetteName);
            TunnelingVignetteController controller = existing != null
                ? existing.GetComponent<TunnelingVignetteController>()
                : null;

            if (controller == null)
            {
                GameObject vignettePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TunnelingVignettePrefabPath);

                if (vignettePrefab == null)
                {
                    BlockiverseLog.Warning(BlockiverseLogCategory.Bootstrap, $"Tunneling vignette prefab not found at {TunnelingVignettePrefabPath}; skipping comfort vignette.");
                    return;
                }

                var vignetteInstance = (GameObject)PrefabUtility.InstantiatePrefab(vignettePrefab);
                vignetteInstance.transform.SetParent(headCamera, false);
                vignetteInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                vignetteInstance.name = TunnelingVignetteName;
                PrefabUtility.UnpackPrefabInstance(vignetteInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                controller = vignetteInstance.GetComponent<TunnelingVignetteController>();
            }

            if (controller == null)
                return;

            BlockiverseComfortSettings vignetteSettings = rig.GetComponent<BlockiverseComfortSettings>();
            float aperture = vignetteSettings != null ? vignetteSettings.VignetteAperture : 0.85f;

            // Default parameters: aperture 0.85 is subtler than the XRI default (0.7).
            // The comfort menu's vignette strength slider adjusts this at runtime.
            controller.defaultParameters = new VignetteParameters
            {
                apertureSize = aperture,
                featheringEffect = 0.2f,
                easeInTime = 0.3f,
                easeOutTime = 0.3f,
            };

            // Ease the comfort vignette in/out during locomotion that causes vection or a viewpoint
            // jump: continuous move, continuous (smooth) turn, teleport, gravity-driven falls, and
            // physics jump arcs. Snap turn is itself a discrete comfort option, so it is intentionally
            // excluded to avoid a vignette flicker on every snap.
            controller.locomotionVignetteProviders.Clear();
            AddVignetteProvider(controller, rig.GetComponent<ContinuousMoveProvider>());
            AddVignetteProvider(controller, rig.GetComponent<ContinuousTurnProvider>());
            AddVignetteProvider(controller, rig.GetComponent<TeleportationProvider>());
            AddVignetteProvider(controller, rig.GetComponent<GravityProvider>());
            AddVignetteProvider(controller, rig.GetComponent<JumpProvider>());

            BlockiverseVignetteSettingsDriver driver = EnsureComponent<BlockiverseVignetteSettingsDriver>(controller.gameObject);
            driver.Configure(vignetteSettings);

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(driver);
            EditorUtility.SetDirty(controller.gameObject);
        }

        static void AddVignetteProvider(TunnelingVignetteController controller, LocomotionProvider provider)
        {
            if (provider == null)
                return;

            controller.locomotionVignetteProviders.Add(new LocomotionVignetteProvider
            {
                locomotionProvider = provider,
                enabled = true,
            });
        }

        static void EnsureXrRigInteraction(GameObject rig, BlockiverseInputRig inputRig)
        {
            // The native XRRayInteractor (built alongside the controller anchor) replaces the old
            // custom ray pointer + UI pointer; strip any stale objects/scripts from older prefabs.
            RemoveStaleRayPointer(rig);
            EnsureBlockMenuPlaceholder(rig, inputRig);
        }

        static void RemoveStaleRayPointer(GameObject rig)
        {
            Transform staleLine = rig.transform.Find("Camera Offset/Right Controller/" + PointerLineName);

            if (staleLine != null)
                UnityEngine.Object.DestroyImmediate(staleLine.gameObject);

            foreach (Transform child in rig.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
        }

        static XRRayInteractor FindInteractionRay(GameObject rig)
        {
            Transform rayTransform = rig.transform.Find("Camera Offset/Right Controller/" + InteractionRayName);
            return rayTransform != null ? rayTransform.GetComponent<XRRayInteractor>() : null;
        }

        static void EnsureXrRigCreativeInputBridge(GameObject rig, BlockiverseInputRig inputRig)
        {
            XRRayInteractor interactionRay = FindInteractionRay(rig);
            BlockiverseCreativeInputBridge bridge = EnsureComponent<BlockiverseCreativeInputBridge>(rig);
            bridge.Configure(inputRig, interactionRay, null);
            EditorUtility.SetDirty(bridge);
        }

        static void EnsureXrRigCreativeFlight(GameObject rig, BlockiverseInputRig inputRig)
        {
            BlockiverseCreativeFlightController flight = EnsureComponent<BlockiverseCreativeFlightController>(rig);
            flight.Configure(inputRig);
            EditorUtility.SetDirty(flight);
        }

        static void EnsureXrRigFeedback(
            GameObject rig,
            BlockiverseInputRig inputRig,
            CreativeInteractionController controller = null)
        {
            BlockiverseFeedbackSettings feedbackSettings = EnsureComponent<BlockiverseFeedbackSettings>(rig);

            BlockiverseAudioCuePlayer audioCuePlayer = EnsureComponent<BlockiverseAudioCuePlayer>(rig);
            ConfigureGeneratedAudioClips(audioCuePlayer);
            audioCuePlayer.Configure(controller);
            audioCuePlayer.ConfigureFeedbackSettings(feedbackSettings);

            BlockiverseVfxPool vfxPool = EnsureComponent<BlockiverseVfxPool>(rig);
            vfxPool.ConfigureParticleMaterial(
                EnsureMaterial(BlockiverseProject.VfxParticleMaterialPath, Color.white, preferUnlit: true));
            vfxPool.ConfigureParticleSprites(
                GetVfxSprite("block_dust_particle"),
                GetVfxSprite("block_puff_particle"),
                GetVfxSprite("resource_spark_particle"),
                GetVfxSprite("craft_spark_particle"),
                GetVfxSprite("rain_splash_particle"),
                GetVfxSprite("snowflake_particle"),
                GetVfxSprite("fog_wisp_particle"),
                GetVfxSprite("ember_particle"));
            BlockiverseVfxCuePlayer vfxCuePlayer = EnsureComponent<BlockiverseVfxCuePlayer>(rig);
            vfxCuePlayer.Configure(controller, vfxPool, feedbackSettings);
            EditorUtility.SetDirty(vfxPool);

            BlockiverseInteractionHaptics interactionHaptics = EnsureComponent<BlockiverseInteractionHaptics>(rig);
            interactionHaptics.Configure(controller, FindControllerHaptics(rig, BlockiverseControllerRole.Right));
            interactionHaptics.ConfigureFeedbackSettings(feedbackSettings);

            // Survival command + multiplayer presence cues, and the weather/ambience driver —
            // both discover their runtime dependencies (sync, world manager) on enable.
            SurvivalFeedbackBridge survivalFeedback = EnsureComponent<SurvivalFeedbackBridge>(rig);
            WeatherFeedbackController weatherFeedback = EnsureComponent<WeatherFeedbackController>(rig);

            // The sparse music bed: generated tracks per context (menu/day/night/cave).
            BlockiverseMusicController musicController = EnsureComponent<BlockiverseMusicController>(rig);
            musicController.ConfigureClips(
                LoadAudioClip("music_menu"),
                LoadAudioClip("music_day"),
                LoadAudioClip("music_night"),
                LoadAudioClip("music_cave"));
            musicController.ConfigureFeedbackSettings(feedbackSettings);
            EditorUtility.SetDirty(musicController);

            // Glide footsteps + landing thump from the rig's character controller.
            BlockiverseLocomotionFeedback locomotionFeedback = EnsureComponent<BlockiverseLocomotionFeedback>(rig);
            locomotionFeedback.Configure(rig.GetComponent<CharacterController>(), audioCuePlayer);
            EditorUtility.SetDirty(locomotionFeedback);

            // Comfort + feedback settings persist across launches (PlayerPrefs).
            BlockiverseSettingsPersistence settingsPersistence = EnsureComponent<BlockiverseSettingsPersistence>(rig);
            EditorUtility.SetDirty(settingsPersistence);

            inputRig?.ConfigureTeleportFeedback(audioCuePlayer);
            ConfigurePanelFeedbackReferences(rig, audioCuePlayer, interactionHaptics);

            EditorUtility.SetDirty(feedbackSettings);
            EditorUtility.SetDirty(audioCuePlayer);
            EditorUtility.SetDirty(vfxPool);
            EditorUtility.SetDirty(vfxCuePlayer);
            EditorUtility.SetDirty(interactionHaptics);
            EditorUtility.SetDirty(survivalFeedback);
            EditorUtility.SetDirty(weatherFeedback);

            if (inputRig != null)
                EditorUtility.SetDirty(inputRig);
        }

        static void ConfigureGeneratedAudioClips(BlockiverseAudioCuePlayer audioCuePlayer)
        {
            foreach ((BlockiverseAudioCue cue, string assetName) in AudioCueAssets)
                audioCuePlayer.ConfigureClip(cue, LoadAudioClip(assetName));

            audioCuePlayer.ConfigureFootstepClips(
                LoadAudioClip("footstep_01"),
                LoadAudioClip("footstep_02"));
        }

        static AudioClip LoadAudioClip(string assetName)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Blockiverse/Audio/{assetName}.wav");
        }

        static BlockiverseControllerHaptics FindControllerHaptics(GameObject rig, BlockiverseControllerRole role)
        {
            foreach (BlockiverseControllerHaptics haptics in rig.GetComponentsInChildren<BlockiverseControllerHaptics>(true))
            {
                if (haptics.Role == role)
                    return haptics;
            }

            return rig.GetComponentInChildren<BlockiverseControllerHaptics>(true);
        }

        static void ConfigurePanelFeedbackReferences(
            GameObject rig,
            BlockiverseAudioCuePlayer audioCuePlayer,
            BlockiverseInteractionHaptics interactionHaptics)
        {
            foreach (CreativeHotbar hotbar in rig.GetComponentsInChildren<CreativeHotbar>(true))
            {
                hotbar.ConfigureFeedback(audioCuePlayer);
                EditorUtility.SetDirty(hotbar);
            }

            foreach (BlockiverseComfortMenu menu in rig.GetComponentsInChildren<BlockiverseComfortMenu>(true))
            {
                menu.ConfigureFeedback(audioCuePlayer, interactionHaptics);
                EditorUtility.SetDirty(menu);
            }

            foreach (BlockiverseWorldSpacePanelPresenter presenter in rig.GetComponentsInChildren<BlockiverseWorldSpacePanelPresenter>(true))
            {
                presenter.ConfigureFeedback(
                    audioCuePlayer,
                    interactionHaptics,
                    presenter.ShowFeedbackCue,
                    presenter.HideFeedbackCue,
                    presenter.PlaysShowFeedback,
                    presenter.PlaysHideFeedback);
                EditorUtility.SetDirty(presenter);
            }
        }
    }
}
