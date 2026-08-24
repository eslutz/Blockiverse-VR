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
            EnsureXrRigInteraction(rig, inputRig);
            EnsureXrRigTunnelingVignette(rig);
            // The boot splash stays uGUI-side deliberately: WorldLoadingScreenController is a
            // routed screen and its header says the splash and its 2.25 s auto-hide timer are a
            // separate object that was not ported.
            EnsureXrRigStartupLoadingOverlay(rig);
            EnsureXrRigSubtitleToast(rig);
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
            EnsureXrRigInteraction(rig, inputRig);
            EnsureXrRigTunnelingVignette(rig);
            // The boot splash stays uGUI-side deliberately: WorldLoadingScreenController is a
            // routed screen and its header says the splash and its 2.25 s auto-hide timer are a
            // separate object that was not ported.
            EnsureXrRigStartupLoadingOverlay(rig);
            EnsureXrRigSubtitleToast(rig);
            EnsureXrRigCreativeInputBridge(rig, inputRig);
            EnsureXrRigCreativeFlight(rig, inputRig);
            EnsureXrRigFeedback(rig, inputRig);
            EnsureXrRigGameMenus(rig, inputRig);
            RemoveMissingScriptComponents(rig);
        }

        // Strips components whose script no longer exists, anywhere under the rig.
        //
        // Deleting a MonoBehaviour's .cs does not remove the component from an object that already
        // had it: this pass re-authors the existing prefab rather than rebuilding it, so the object
        // survives carrying a missing-script entry. Unity then warns on every load and drops it
        // silently at build time, which is a bad way to find out.
        //
        // Retiring the uGUI menus removed most of them with their whole GameObjects
        // (RetiredUguiMenuPanelNames), but that only helps a component whose object also died. The
        // catalog browser shared the Block Menu with CreativeHotbar — the object SURVIVES because
        // the hotbar is gameplay state — so nothing would have taken the browser off it. Sweeping
        // by missing-script rather than by type also means this keeps working after the class is
        // gone, which a typed RemoveComponentIfPresent<T> cannot do.
        static void RemoveMissingScriptComponents(GameObject root)
        {
            if (root == null)
                return;

            int removed = 0;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(includeInactive: true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);

            if (removed > 0)
            {
                EditorUtility.SetDirty(root);
                BlockiverseLog.Info(
                    BlockiverseLogCategory.General,
                    $"Removed {removed} component(s) with missing scripts from {root.name}.");
            }
        }

        static void ConfigureXrMainCamera(Camera camera)
        {
            if (camera == null)
                return;

            // Fluid must be here as well as terrain. Water lives on its own layer so gravity's
            // ground cast cannot see it; if that layer is missing from the culling mask, water
            // still collides and still raycasts but renders nowhere — invisible in the editor
            // game view and only obvious on device.
            camera.cullingMask |= BlockiverseProject.InteractionLayerMask | BlockiverseProject.FluidLayerMask;
            camera.cullingMask &= ~(BlockiverseProject.CompositionUiLayerMask |
                                    BlockiverseProject.XrVisualProjectionLayerMask |
                                    BlockiverseProject.MirrorAvatarLayerMask);
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

            Transform rayOrigin = EnsureControllerRayOrigin(controller.transform, role);
            EnsureControllerInteractors(controller, inputRig, role, rayOrigin);

            EditorUtility.SetDirty(poseDriver);
            EditorUtility.SetDirty(anchor);
            EditorUtility.SetDirty(haptics);
        }

        // Rays emit from a controller-local origin that BlockiverseAimPoseRayOrigin keeps on the
        // OpenXR aim pose at runtime (the controller itself rides the grip pose). The serialized
        // local pose is the fallback offset used until the first tracked aim sample arrives.
        static Transform EnsureControllerRayOrigin(Transform controller, BlockiverseControllerRole role)
        {
            GameObject rayOrigin = EnsureChild(controller, ControllerRayOriginName);
            rayOrigin.layer = controller != null ? controller.gameObject.layer : 0;
            rayOrigin.transform.SetLocalPositionAndRotation(
                BlockiverseAimPoseRayOrigin.ResolveFallbackLocalPosition(role),
                BlockiverseAimPoseRayOrigin.ResolveFallbackLocalRotation(role));
            BlockiverseAimPoseRayOrigin aimOrigin = EnsureComponent<BlockiverseAimPoseRayOrigin>(rayOrigin);
            aimOrigin.Configure(role);
            EditorUtility.SetDirty(aimOrigin);
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
            interactionRay.selectInput = MakeUnusedButtonReader(interactionRay.selectInput, "Select");
            interactionRay.activateInput = MakeUnusedButtonReader(interactionRay.activateInput, "Activate");
            interactionRay.uiPressInput = MakeButtonReader(
                interactionRay.uiPressInput,
                "UI Press",
                LoadInputActionReference(mapName, BlockiverseInputActionNames.UiPress));
            interactionRay.uiScrollInput = MakeVector2Reader(
                interactionRay.uiScrollInput,
                "UI Scroll",
                LoadInputActionReference(mapName, BlockiverseInputActionNames.UiScroll));
            ConfigureLineVisual(interactionRayObject, pointerMaterial);
            EditorUtility.SetDirty(interactionRay);

            // Both controllers get a teleport ray; the mediator activates it only in Teleport mode.
            GameObject teleportRayObject = EnsureChild(controller.transform, TeleportRayName);
            teleportRayObject.layer = controller.layer;
            teleportRayObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            XRRayInteractor teleportRay = EnsureComponent<XRRayInteractor>(teleportRayObject);
            // Terrain + fluid: the ray stops at the water surface so the player lands treading,
            // instead of the ray dying on an unregistered collider and teleport failing near water.
            BlockiverseRayDefaults.ConfigureTeleportRay(teleportRay, rayOrigin, GetVrUiRaycastLayerMask());
            // Teleport on thumb-release: selectInput = thumbstick/y composite, OnSelectExited fires on release.
            teleportRay.selectInput = MakeButtonReader(
                teleportRay.selectInput,
                "Teleport Select",
                LoadInputActionReference(mapName, BlockiverseInputActionNames.TeleportSelect));
            teleportRay.activateInput = MakeUnusedButtonReader(teleportRay.activateInput, "Activate");
            teleportRay.uiPressInput = MakeUnusedButtonReader(teleportRay.uiPressInput, "UI Press");
            teleportRay.uiScrollInput = MakeVector2Reader(teleportRay.uiScrollInput, "UI Scroll", null);
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

        static XRInputButtonReader MakeButtonReader(
            XRInputButtonReader current,
            string name,
            InputActionReference reference)
        {
            if (reference == null)
                return MakeUnusedButtonReader(current, name);

            XRInputButtonReader reader = current ?? new XRInputButtonReader(name);
            reader.inputSourceMode = XRInputButtonReader.InputSourceMode.InputActionReference;
            reader.inputActionReferencePerformed = reference;
            reader.inputActionReferenceValue = reference;
            reader.SetObjectReference(null);
            return reader;
        }

        static XRInputButtonReader MakeUnusedButtonReader(XRInputButtonReader current, string name)
        {
            XRInputButtonReader reader = current ?? new XRInputButtonReader(name);
            reader.inputSourceMode = XRInputButtonReader.InputSourceMode.Unused;
            reader.inputActionReferencePerformed = null;
            reader.inputActionReferenceValue = null;
            reader.SetObjectReference(null);
            reader.manualPerformed = false;
            reader.manualValue = 0.0f;
            return reader;
        }

        static XRInputValueReader<Vector2> MakeVector2Reader(
            XRInputValueReader<Vector2> current,
            string name,
            InputActionReference reference)
        {
            XRInputValueReader<Vector2> reader = current ?? new XRInputValueReader<Vector2>(name);
            reader.inputSourceMode = reference == null
                ? XRInputValueReader.InputSourceMode.Unused
                : XRInputValueReader.InputSourceMode.InputActionReference;
            reader.inputActionReference = reference;
            reader.SetObjectReference(null);
            reader.manualValue = Vector2.zero;
            return reader;
        }

        static InputAction FindRigAction(BlockiverseInputRig inputRig, string mapName, string actionName)
        {
            InputActionAsset asset = inputRig != null ? inputRig.InputActions : null;
            InputActionMap map = asset?.FindActionMap(mapName, throwIfNotFound: false);
            return map?.FindAction(actionName, throwIfNotFound: false);
        }

        static void EnsureXrRigAvatar(GameObject rig)
        {
            // The XRI input manager feeds rig-relative head/controller poses to the avatar
            // entity so the avatar body tracks the player. It must be on the rig (or a child)
            // before the entity is instantiated at runtime; its sources are wired below once
            // the rig transforms are resolved.
            BlockiverseXriAvatarInputManager avatarInputManager = EnsureComponent<BlockiverseXriAvatarInputManager>(rig);

            // This component is intentionally unspawned on the local XR rig; the spawned network
            // player prefab owns the NetworkObject used for multiplayer avatar relay.
            BlockiverseNetworkAvatarRig avatarRig = EnsureComponent<BlockiverseNetworkAvatarRig>(rig);
            BlockiverseKeyboardHandVisibilityController keyboardHandVisibility =
                EnsureComponent<BlockiverseKeyboardHandVisibilityController>(rig);
            keyboardHandVisibility.Configure(avatarRig);

            // Item icons are rig state, not menu state: the library is a component on the rig that
            // screens look up by scene search. Its generator used to be called from
            // EnsureXrRigSurvivalHud — the uGUI survival HUD builder — which was its only call
            // site, so removing the uGUI menus would have stopped the component being generated at
            // all and left the surviving UI Toolkit inventory and crafting screens with no icons.
            // Nothing would have failed loudly; the icons would just be missing.
            EnsureItemIconLibrary(rig);

            MetaHorizonAvatarProvider avatarProvider = EnsureComponent<MetaHorizonAvatarProvider>(rig);
            BlockiverseMetaAvatarPresenter avatarPresenter = EnsureComponent<BlockiverseMetaAvatarPresenter>(rig);
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;
            Transform leftHand = cameraOffset != null ? cameraOffset.Find("Left Controller") : null;
            Transform rightHand = cameraOffset != null ? cameraOffset.Find("Right Controller") : null;

            // Lights the first-person hands from the voxel world's own light, because their
            // material is unlit and nothing else would give them brightness. Lives on the rig
            // rather than beside the hands: the avatar rig is in Networking, which cannot reach
            // VoxelLightSampler in Gameplay.
            BlockiverseHandLightDriver handLight = EnsureComponent<BlockiverseHandLightDriver>(rig);
            handLight.Configure(avatarRig, null);
            EditorUtility.SetDirty(handLight);

            avatarRig.ConfigureTrackingSources(head, leftHand, rightHand);
            avatarRig.SetMetaAvatarAvailable(false);
            avatarRig.ConfigureFallbackProxy(true);
            avatarRig.ConfigureFirstPersonFallbackVisuals(true);
            // The rig root is the floor-level tracking origin: the avatar entity sits under
            // it at an identity local pose, and the tracking delegate converts the head and
            // controller poses into this space.
            avatarInputManager.ConfigureSources(rig.transform, head, leftHand, rightHand);
            avatarPresenter.Configure(
                avatarProvider,
                avatarRig,
                head,
                leftHand,
                rightHand,
                MetaAvatarPresentationMode.LocalFirstPerson);
            EditorUtility.SetDirty(avatarRig);
            EditorUtility.SetDirty(keyboardHandVisibility);
            EditorUtility.SetDirty(avatarInputManager);
            EditorUtility.SetDirty(avatarProvider);
            EditorUtility.SetDirty(avatarPresenter);
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

            // Default parameters: startup vignette aperture should be fully open (1.0f) until
            // intentional locomotion, to satisfy comfort and testing constraints.
            controller.defaultParameters = new VignetteParameters
            {
                apertureSize = 1.0f,
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

            // The swim provider drives the vignette itself rather than through a
            // LocomotionVignetteProvider entry, because it engages only for its own vertical motion
            // and only while the Vignette While Sinking comfort setting is on -- neither of which a
            // static list entry can express.
            BlockiverseSwimProvider swimProvider = rig.GetComponent<BlockiverseSwimProvider>();

            if (swimProvider != null)
            {
                swimProvider.ConfigureVignette(controller);
                EditorUtility.SetDirty(swimProvider);
            }

            BlockiverseVignetteSettingsDriver driver = EnsureComponent<BlockiverseVignetteSettingsDriver>(controller.gameObject);
            driver.Configure(vignetteSettings);

            if (vignetteRenderer != null)
            {
                vignetteRenderer.enabled = false;
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

            // Flight is a LocomotionProvider now, so it needs the mediator like every other one --
            // gravity, swim, jump, teleport, move, snap turn and continuous turn are all wired the
            // same way. Without it the component serializes m_Mediator: {fileID: 0}.
            LocomotionMediator flightMediator = rig.GetComponent<LocomotionMediator>();

            if (flightMediator != null)
                flight.mediator = flightMediator;

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

            // Glide footsteps + landing thump. Footsteps come off the shared gait cycle so they land
            // on the camera bob; the landing cue is the feedback component's own.
            BlockiverseGaitCycle gaitCycle = EnsureComponent<BlockiverseGaitCycle>(rig);
            gaitCycle.Configure(rig.GetComponent<CharacterController>());
            EditorUtility.SetDirty(gaitCycle);

            BlockiverseLocomotionFeedback locomotionFeedback = EnsureComponent<BlockiverseLocomotionFeedback>(rig);
            // The world reference lets footsteps sample the block underfoot and pick
            // the matching surface bank; null in the prefab path, resolved at runtime.
            locomotionFeedback.Configure(
                rig.GetComponent<CharacterController>(),
                audioCuePlayer,
                gaitCycle,
                UnityEngine.Object.FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include));
            EditorUtility.SetDirty(locomotionFeedback);

            // Water audio hangs off the swim provider's state machine, which is the only
            // authoritative submersion state on the rig. The provider is added by the locomotion
            // pass; GetComponent rather than EnsureComponent so ordering stays that pass's job.
            BlockiverseSwimFeedback swimFeedback = EnsureComponent<BlockiverseSwimFeedback>(rig);
            swimFeedback.Configure(rig.GetComponent<BlockiverseSwimProvider>(), audioCuePlayer);
            EditorUtility.SetDirty(swimFeedback);

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

            var materialBanks = new BlockiverseMaterialBank[MaterialAudioAssets.Length];
            for (int index = 0; index < MaterialAudioAssets.Length; index++)
            {
                (BlockiverseMaterialFamily family, string breakAsset, string placeAsset) = MaterialAudioAssets[index];
                materialBanks[index] = new BlockiverseMaterialBank
                {
                    Family = family,
                    BreakClip = LoadAudioClip(breakAsset),
                    PlaceClip = LoadAudioClip(placeAsset),
                };
            }
            audioCuePlayer.ConfigureMaterialBanks(materialBanks);

            var footstepBanks = new BlockiverseFootstepBank[FootstepSurfaceAssets.Length];
            for (int index = 0; index < FootstepSurfaceAssets.Length; index++)
            {
                (BlockiverseSurfaceFamily surface, string prefix) = FootstepSurfaceAssets[index];
                var clips = new AudioClip[FootstepVariantsPerSurface];
                for (int variant = 0; variant < FootstepVariantsPerSurface; variant++)
                    clips[variant] = LoadAudioClip($"{prefix}_{variant + 1:00}");

                footstepBanks[index] = new BlockiverseFootstepBank { Surface = surface, Clips = clips };
            }
            audioCuePlayer.ConfigureFootstepBanks(footstepBanks);

            // Legacy flat bank, kept as the fallback for a surface with no bank
            // (and for callers that still play a bare Footstep cue).
            audioCuePlayer.ConfigureFootstepClips(
                LoadAudioClip("footstep_soil_01"),
                LoadAudioClip("footstep_soil_02"));

            audioCuePlayer.ConfigureToolHitClips(
                new[]
                {
                    LoadAudioClip("tool_hit_soft_01"),
                    LoadAudioClip("tool_hit_soft_02"),
                    LoadAudioClip("tool_hit_soft_03"),
                },
                new[]
                {
                    LoadAudioClip("tool_hit_stone_01"),
                    LoadAudioClip("tool_hit_stone_02"),
                    LoadAudioClip("tool_hit_stone_03"),
                });

            audioCuePlayer.ConfigureClassicBlockClips(
                LoadAudioClip("classic_block_break"),
                LoadAudioClip("classic_block_place"));
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
            // The uGUI audio-settings panel and comfort menu used to be configured here too; both
            // are UI Toolkit screens now and read BlockiverseFeedbackSettings themselves.
            foreach (CreativeHotbar hotbar in rig.GetComponentsInChildren<CreativeHotbar>(true))
            {
                hotbar.ConfigureFeedback(audioCuePlayer);
                EditorUtility.SetDirty(hotbar);
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
