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
            ConfigureXrMainCamera(camera);
            cameraObject.AddComponent<AudioListener>();
            TrackedPoseDriver poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
            ConfigureHeadPoseDriverReferenceActions(poseDriver);

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

            RemoveStaleChild(cameraOffset, LeftAimPoseName);
            RemoveStaleChild(cameraOffset, RightAimPoseName);
            RemoveStaleChild(cameraOffset, LeftRayOriginName);
            RemoveStaleChild(cameraOffset, RightRayOriginName);

            Camera xrCamera = origin.Camera;
            ConfigureXrMainCamera(xrCamera);
            TrackedPoseDriver poseDriver = xrCamera != null
                ? xrCamera.GetComponent<TrackedPoseDriver>()
                : rig.GetComponentInChildren<TrackedPoseDriver>(true);

            if (poseDriver == null && xrCamera != null)
                poseDriver = xrCamera.gameObject.AddComponent<TrackedPoseDriver>();

            ConfigureHeadPoseDriverReferenceActions(poseDriver);
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

        static void ConfigureXrMainCamera(Camera camera)
        {
            if (camera == null)
                return;

            camera.cullingMask |= BlockiverseProject.VrUiRaycastLayerMask;
            camera.cullingMask &= ~BlockiverseProject.XrVisualProjectionLayerMask;
            EditorUtility.SetDirty(camera);
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
            controller.layer = 0;

            // Native controller tracking: a TrackedPoseDriver drives the controller transform in
            // Update + BeforeRender, matching the head and removing the old hand-written pose.
            TrackedPoseDriver poseDriver = EnsureComponent<TrackedPoseDriver>(controller);
            ConfigureControllerPoseDriverReferenceActions(poseDriver, role);
            poseDriver.enabled = true;

            BlockiverseControllerAnchor anchor = EnsureComponent<BlockiverseControllerAnchor>(controller);
            anchor.Configure(role, poseDriver);

            BlockiverseControllerHaptics haptics = EnsureComponent<BlockiverseControllerHaptics>(controller);
            haptics.Configure(role);

            Transform rayOrigin = EnsureControllerRayOrigin(controller.transform);
            EnsureControllerInteractors(controller, inputRig, role, rayOrigin);

            EditorUtility.SetDirty(poseDriver);
            EditorUtility.SetDirty(anchor);
            EditorUtility.SetDirty(haptics);
        }

        static Transform EnsureControllerRayOrigin(Transform controller)
        {
            GameObject rayOrigin = EnsureChild(controller, ControllerRayOriginName);
            rayOrigin.layer = controller != null ? controller.gameObject.layer : 0;
            rayOrigin.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.Euler(90.0f, 0.0f, 0.0f));
            EditorUtility.SetDirty(rayOrigin);
            return rayOrigin.transform;
        }

        // Builds native interaction (UI + block targeting) and teleport rays on each controller.
        // The mediator enables only the active tool-hand interaction ray, while either controller
        // can own teleport when Teleport mode and thumbstick-forward are active.
        static XRRayInteractor EnsureControllerInteractors(
            GameObject controller,
            BlockiverseInputRig inputRig,
            BlockiverseControllerRole role,
            Transform rayOrigin)
        {
            Material pointerMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.PointerLineMaterialPath);
            BlockiverseComfortSettings settings = inputRig != null ? inputRig.GetComponent<BlockiverseComfortSettings>() : null;
            string mapName = role == BlockiverseControllerRole.Left
                ? BlockiverseInputActionNames.LeftHandMap
                : BlockiverseInputActionNames.RightHandMap;
            rayOrigin ??= controller.transform;

            GameObject interactionRayObject = EnsureChild(controller.transform, InteractionRayName);
            interactionRayObject.layer = controller.layer;
            interactionRayObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            interactionRayObject.SetActive(true);

            XRRayInteractor interactionRay = EnsureComponent<XRRayInteractor>(interactionRayObject);
            BlockiverseRayDefaults.ConfigureInteractionRay(interactionRay, rayOrigin, GetVrUiRaycastLayerMask());
            interactionRay.selectInput = MakeUnusedButtonReader("Select");
            interactionRay.activateInput = MakeUnusedButtonReader("Activate");
            interactionRay.uiPressInput = MakeButtonReader(
                "UI Press",
                LoadInputActionReference(mapName, BlockiverseInputActionNames.UiPress));
            interactionRay.uiScrollInput = MakeVector2Reader(
                "UI Scroll",
                LoadInputActionReference(mapName, BlockiverseInputActionNames.UiScroll));
            ConfigureLineVisual(interactionRayObject, pointerMaterial);
            EditorUtility.SetDirty(interactionRay);

            // Both controllers get a teleport ray; the mediator activates it only in Teleport mode.
            GameObject teleportRayObject = EnsureChild(controller.transform, TeleportRayName);
            teleportRayObject.layer = controller.layer;
            teleportRayObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            XRRayInteractor teleportRay = EnsureComponent<XRRayInteractor>(teleportRayObject);
            BlockiverseRayDefaults.ConfigureTeleportRay(teleportRay, rayOrigin, GetInteractionLayerMask());
            // Teleport on thumb-release: selectInput = thumbstick/y composite, OnSelectExited fires on release.
            teleportRay.selectInput = MakeButtonReader(
                "Teleport Select",
                LoadInputActionReference(mapName, BlockiverseInputActionNames.TeleportSelect));
            teleportRay.activateInput = MakeUnusedButtonReader("Activate");
            teleportRay.uiPressInput = MakeUnusedButtonReader("UI Press");
            teleportRay.uiScrollInput = MakeVector2Reader("UI Scroll", null);
            ConfigureLineVisual(teleportRayObject, pointerMaterial);
            teleportRayObject.SetActive(false);
            EditorUtility.SetDirty(teleportRay);

            BlockiverseLocomotionRayMediator mediator = EnsureComponent<BlockiverseLocomotionRayMediator>(controller);
            mediator.Configure(inputRig, settings, interactionRay, teleportRay, role, controller.GetComponent<BlockiverseControllerAnchor>());
            EditorUtility.SetDirty(mediator);

            return interactionRay;
        }

        static void ConfigureLineVisual(GameObject rayObject, Material pointerMaterial)
        {
            LineRenderer lineRenderer = EnsureComponent<LineRenderer>(rayObject);
            XRInteractorLineVisual lineVisual = EnsureComponent<XRInteractorLineVisual>(rayObject);
            BlockiverseRayDefaults.ConfigureLineVisual(lineRenderer, lineVisual, pointerMaterial, PointerLineColor);

            EditorUtility.SetDirty(lineRenderer);
            EditorUtility.SetDirty(lineVisual);
        }

        // Use InputActionReference mode so the bootstrapper-assigned reader does not take ownership
        // of the action's enable/disable lifecycle. The rig enables/disables the whole
        // InputActionAsset, and InputAction mode would fight that.
        static void ConfigureHeadPoseDriverReferenceActions(TrackedPoseDriver poseDriver)
        {
            BlockiverseInputRig.ConfigurePoseDriverActionReferences(
                poseDriver,
                LoadInputActionReference(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.Position),
                LoadInputActionReference(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.Rotation),
                LoadInputActionReference(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.TrackingState));
        }

        static void ConfigureControllerPoseDriverReferenceActions(TrackedPoseDriver poseDriver, BlockiverseControllerRole role)
        {
            string mapName = role == BlockiverseControllerRole.Left
                ? BlockiverseInputActionNames.LeftHandMap
                : BlockiverseInputActionNames.RightHandMap;

            BlockiverseInputRig.ConfigurePoseDriverActionReferences(
                poseDriver,
                LoadInputActionReference(mapName, BlockiverseInputActionNames.Position),
                LoadInputActionReference(mapName, BlockiverseInputActionNames.Rotation),
                LoadInputActionReference(mapName, BlockiverseInputActionNames.TrackingState));
        }

        static XRInputButtonReader MakeButtonReader(string name, InputActionReference reference)
        {
            if (reference == null)
                return MakeUnusedButtonReader(name);

            var reader = new XRInputButtonReader(name,
                inputSourceMode: XRInputButtonReader.InputSourceMode.InputActionReference);

            reader.inputActionReferencePerformed = reference;
            return reader;
        }

        static XRInputButtonReader MakeUnusedButtonReader(string name)
        {
            return new XRInputButtonReader(name,
                inputSourceMode: XRInputButtonReader.InputSourceMode.Unused);
        }

        static XRInputValueReader<Vector2> MakeVector2Reader(string name, InputActionReference reference)
        {
            if (reference == null)
                return new XRInputValueReader<Vector2>(name, XRInputValueReader.InputSourceMode.Unused);

            return new XRInputValueReader<Vector2>(name,
                XRInputValueReader.InputSourceMode.InputActionReference)
            {
                inputActionReference = reference
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
            BlockiverseKeyboardHandVisibilityController keyboardHandVisibility =
                EnsureComponent<BlockiverseKeyboardHandVisibilityController>(rig);
            keyboardHandVisibility.Configure(avatarRig);
            MetaHorizonAvatarProvider avatarProvider = EnsureComponent<MetaHorizonAvatarProvider>(rig);
            BlockiverseMetaAvatarPresenter avatarPresenter = EnsureComponent<BlockiverseMetaAvatarPresenter>(rig);
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;
            Transform leftHand = cameraOffset != null ? cameraOffset.Find("Left Controller") : null;
            Transform rightHand = cameraOffset != null ? cameraOffset.Find("Right Controller") : null;

            avatarRig.ConfigureTrackingSources(head, leftHand, rightHand);
            avatarRig.SetMetaAvatarAvailable(false);
            avatarRig.ConfigureFallbackProxy(true);
            avatarRig.ConfigureFirstPersonFallbackVisuals(true);
            avatarPresenter.Configure(
                avatarProvider,
                avatarRig,
                head,
                leftHand,
                rightHand,
                MetaAvatarPresentationMode.LocalFirstPerson);
            EditorUtility.SetDirty(avatarRig);
            EditorUtility.SetDirty(keyboardHandVisibility);
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

            Transform routedMenuParent = EnsureMenuCompositionSurface(cameraOffset, head).transform.Find(MenuCompositionCanvasName);
            BlockiverseComfortSettings settings = rig.GetComponent<BlockiverseComfortSettings>();
            BlockiverseHeightReset heightReset = rig.GetComponent<BlockiverseHeightReset>();
            GameObject menuObject = EnsureRoutedMenuRectChild(cameraOffset, routedMenuParent, leftController, ComfortMenuName);
            const float comfortMenuScale = 0.00105f;
            menuObject.transform.localPosition = new Vector3(0.0f, 1.42f, 1.18f);
            menuObject.transform.localRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
            menuObject.transform.localScale = Vector3.one * comfortMenuScale;

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
            RemoveStaleChild(panelObject.transform, "Dominant Hand Only Toggle");
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
                TitleSizeWithClose(ComfortMenuSize.x, 56.0f));

            EnsureButtonControl(
                panelObject.transform,
                "Close Button",
                "Close",
                TopRightClosePosition(ComfortMenuSize.x),
                MenuCloseButtonSize);

            // --- Movement Mode (Glide / Teleport) ---
            EnsureLabel(panelObject.transform, "Movement Label", "Movement Mode", 22,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(32.0f, -94.0f), new Vector2(300.0f, 36.0f));

            Toggle glideToggle = EnsureToggleControl(
                panelObject.transform,
                "Glide Toggle",
                "Glide Motion",
                settings == null || settings.LocomotionMode == BlockiverseLocomotionMode.Glide,
                new Vector2(32.0f, -134.0f));

            Toggle teleportToggle = EnsureToggleControl(
                panelObject.transform,
                "Teleport Toggle",
                "Teleport",
                settings != null && settings.LocomotionMode == BlockiverseLocomotionMode.Teleport,
                new Vector2(32.0f, -182.0f));

            Slider moveSpeedSlider = EnsureSettingsSlider(
                panelObject.transform,
                "Move Speed Slider",
                "Move Speed",
                settings != null ? settings.ContinuousMoveSpeed : 1.8f,
                new Vector2(32.0f, -246.0f),
                minValue: 0.5f,
                maxValue: 4.0f);

            // --- Turning ---
            EnsureLabel(panelObject.transform, "Turning Label", "Turning", 22,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(532.0f, -94.0f), new Vector2(300.0f, 36.0f));

            Toggle smoothTurnToggle = EnsureToggleControl(
                panelObject.transform,
                "Smooth Turn Toggle",
                "Smooth Turn",
                settings != null && settings.SmoothTurnEnabled,
                new Vector2(532.0f, -134.0f));

            Slider snapTurnSlider = EnsureSnapTurnSlider(
                panelObject.transform,
                settings != null ? settings.SnapTurnDegrees : 45.0f,
                new Vector2(532.0f, -198.0f));

            Toggle turnAroundToggle = EnsureToggleControl(
                panelObject.transform,
                "Turn Around Toggle",
                "Turn Around",
                settings == null || settings.SnapTurnAroundEnabled,
                new Vector2(532.0f, -300.0f));

            Slider smoothTurnSpeedSlider = EnsureSettingsSlider(
                panelObject.transform,
                "Smooth Turn Speed Slider",
                "Smooth Turn Speed",
                settings != null ? settings.ContinuousTurnSpeed : 60.0f,
                new Vector2(532.0f, -364.0f),
                minValue: 30.0f,
                maxValue: 180.0f);

            // --- Hand Roles ---
            EnsureLabel(panelObject.transform, "Control Options Label", "Control Options", 22,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(32.0f, -380.0f), new Vector2(300.0f, 36.0f));

            Toggle leftHandToggle = EnsureToggleControl(
                panelObject.transform,
                "Left Hand Toggle",
                "Left-Handed",
                settings != null && settings.DominantHand == BlockiverseControllerRole.Left,
                new Vector2(32.0f, -420.0f));

            Toggle toggleToMineToggle = EnsureToggleControl(
                panelObject.transform,
                "Toggle To Mine Toggle",
                "Toggle To Mine",
                settings != null && settings.ToggleToMineEnabled,
                new Vector2(32.0f, -468.0f));

            // --- Vignette ---
            EnsureLabel(panelObject.transform, "View Comfort Label", "View Comfort", 22,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(532.0f, -500.0f), new Vector2(300.0f, 36.0f));

            Toggle vignetteToggle = EnsureToggleControl(
                panelObject.transform,
                "Vignette Toggle",
                "Motion Vignette",
                settings != null && settings.VignetteEnabled,
                new Vector2(532.0f, -540.0f));

            Slider vignetteSlider = EnsureVignetteSlider(
                panelObject.transform,
                settings != null ? settings.VignetteStrength : 0.0f,
                new Vector2(532.0f, -604.0f));

            EnsureLabel(panelObject.transform, "Player View Label", "Player View", 22,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(32.0f, -590.0f), new Vector2(300.0f, 36.0f));

            Slider eyeHeightSlider = EnsureSettingsSlider(
                panelObject.transform,
                "Eye Height Slider",
                "Eye Height",
                settings != null ? settings.StandingEyeHeight : 1.6f,
                new Vector2(32.0f, -632.0f),
                minValue: 1.0f,
                maxValue: 2.2f);

            Slider uiScaleSlider = EnsureSettingsSlider(
                panelObject.transform,
                "UI Scale Slider",
                "UI Scale",
                settings != null ? settings.UiScale : 1.0f,
                new Vector2(532.0f, -724.0f),
                minValue: 0.85f,
                maxValue: 1.35f);

            // --- Height Reset ---
            Button heightResetButton = EnsureButtonControl(
                panelObject.transform,
                "Height Reset Button",
                "Reset Height",
                new Vector2(32.0f, -742.0f));

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
                toggleToMineToggle,
                eyeHeightSlider,
                moveSpeedSlider,
                smoothTurnSpeedSlider,
                uiScaleSlider);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(menuObject);
            presenter.Configure(canvas, head, 1.3f, 0.0f, -0.06f, 0.0f, comfortMenuScale);
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
            MeshRenderer vignetteRenderer = controller.GetComponent<MeshRenderer>();

            // Default parameters: aperture 0.85 is subtler than the XRI default (0.7).
            // The comfort menu's vignette strength slider adjusts this at runtime.
            controller.defaultParameters = new VignetteParameters
            {
                apertureSize = aperture,
                featheringEffect = 0.2f,
                easeInTime = 0.3f,
                easeOutTime = 0.3f,
            };

            // Ease the comfort vignette in/out only for intentional player locomotion that causes
            // vection or a viewpoint jump. Gravity and jump providers can report active while the
            // rig settles onto terrain during startup, which would close the menu view while idle.
            // Snap turn is itself a discrete comfort option, so it is intentionally excluded too.
            controller.locomotionVignetteProviders.Clear();
            AddVignetteProvider(controller, rig.GetComponent<ContinuousMoveProvider>());
            AddVignetteProvider(controller, rig.GetComponent<ContinuousTurnProvider>());
            AddVignetteProvider(controller, rig.GetComponent<TeleportationProvider>());

            BlockiverseVignetteSettingsDriver driver = EnsureComponent<BlockiverseVignetteSettingsDriver>(controller.gameObject);
            driver.Configure(vignetteSettings);

            if (vignetteRenderer != null)
            {
                vignetteRenderer.enabled = vignetteSettings != null && vignetteSettings.VignetteEnabled;
                EditorUtility.SetDirty(vignetteRenderer);
            }

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

        static void RemoveStaleChild(Transform parent, string childName)
        {
            Transform stale = parent != null ? parent.Find(childName) : null;
            if (stale != null)
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
        }

        static XRRayInteractor FindInteractionRay(GameObject rig)
        {
            foreach (string controllerName in new[] { "Right Controller", "Left Controller" })
            {
                Transform rayTransform = rig.transform.Find($"Camera Offset/{controllerName}/{InteractionRayName}");
                XRRayInteractor ray = rayTransform != null ? rayTransform.GetComponent<XRRayInteractor>() : null;
                if (ray != null)
                    return ray;
            }

            return null;
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
            flight.FlightEnabledDefault = false;
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
            vfxPool.ConfigureParticleMaterial(EnsureTransparentVfxParticleMaterial());
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
            BlockiverseDominantHandResolver dominantHandResolver = EnsureComponent<BlockiverseDominantHandResolver>(rig);
            dominantHandResolver.Configure(rig.GetComponent<BlockiverseComfortSettings>());
            EditorUtility.SetDirty(dominantHandResolver);

            BlockiverseVerboseTraceController verboseTrace = EnsureComponent<BlockiverseVerboseTraceController>(rig);
            verboseTrace.Configure(inputRig, null, controller, audioCuePlayer, vfxCuePlayer, musicController, interactionHaptics);
            EditorUtility.SetDirty(verboseTrace);

            inputRig?.ConfigureTeleportFeedback(audioCuePlayer);
            ConfigurePanelFeedbackReferences(rig, audioCuePlayer, interactionHaptics);

            EditorUtility.SetDirty(feedbackSettings);
            EditorUtility.SetDirty(audioCuePlayer);
            EditorUtility.SetDirty(vfxPool);
            EditorUtility.SetDirty(vfxCuePlayer);
            EditorUtility.SetDirty(interactionHaptics);
            EditorUtility.SetDirty(survivalFeedback);
            EditorUtility.SetDirty(weatherFeedback);
            EditorUtility.SetDirty(verboseTrace);

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
