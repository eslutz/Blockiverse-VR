using System.IO;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
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
using TMPro;
using UnityEngine.UI;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseRigPrefabTests
    {
        [Test]
        public void XrRigPrefabIsWiredForQuestControllerAnchorsAndHaptics()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<BlockiverseInputRig>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BlockiverseComfortTransition>(), Is.Not.Null);
            Assert.That(prefab.transform.Find("Camera Offset/Left Controller"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Camera Offset/Right Controller"), Is.Not.Null);

            BlockiverseControllerAnchor[] anchors = prefab
                .GetComponentsInChildren<BlockiverseControllerAnchor>(true);
            BlockiverseControllerHaptics[] haptics = prefab
                .GetComponentsInChildren<BlockiverseControllerHaptics>(true);

            Assert.That(anchors, Has.Length.EqualTo(2));
            Assert.That(haptics, Has.Length.EqualTo(2));
            Assert.That(anchors.Select(anchor => anchor.Role), Is.EquivalentTo(new[]
            {
                BlockiverseControllerRole.Left,
                BlockiverseControllerRole.Right
            }));
            Assert.That(haptics.Select(controllerHaptics => controllerHaptics.Role), Is.EquivalentTo(new[]
            {
                BlockiverseControllerRole.Left,
                BlockiverseControllerRole.Right
            }));

            AssertController(prefab, "Left Controller", BlockiverseControllerRole.Left);
            AssertController(prefab, "Right Controller", BlockiverseControllerRole.Right);
        }

        [Test]
        public void XrRigRuntimeRepairsHeadPoseDriverAndXriLocomotionProviders()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);

            try
            {
                BlockiverseInputRig inputRig = instance.GetComponent<BlockiverseInputRig>();
                inputRig?.RepairRuntimeTracking();

                XRBodyTransformer bodyTransformer = instance.GetComponent<XRBodyTransformer>();
                LocomotionMediator mediator = instance.GetComponent<LocomotionMediator>();
                ContinuousMoveProvider continuousMove = instance.GetComponent<ContinuousMoveProvider>();
                SnapTurnProvider snapTurn = instance.GetComponent<SnapTurnProvider>();
                TeleportationProvider teleport = instance.GetComponent<TeleportationProvider>();
                ContinuousTurnProvider continuousTurn = instance.GetComponent<ContinuousTurnProvider>();
                GravityProvider gravityProvider = instance.GetComponent<GravityProvider>();
                JumpProvider jumpProvider = instance.GetComponent<JumpProvider>();
                CharacterController characterController = instance.GetComponent<CharacterController>();
                BlockiverseComfortTransition comfortTransition = instance.GetComponent<BlockiverseComfortTransition>();
                TrackedPoseDriver poseDriver = inputRig?.HeadPoseDriver;
                BlockiverseFoveatedRenderingController foveatedRenderingController =
                    inputRig?.FoveatedRenderingController;

                Assert.That(inputRig, Is.Not.Null);
                Assert.That(bodyTransformer, Is.Not.Null);
                Assert.That(mediator, Is.Not.Null);
                Assert.That(continuousMove, Is.Not.Null);
                Assert.That(snapTurn, Is.Not.Null);
                Assert.That(teleport, Is.Not.Null);
                Assert.That(inputRig.BodyTransformer, Is.SameAs(bodyTransformer));
                Assert.That(inputRig.LocomotionMediator, Is.SameAs(mediator));
                Assert.That(inputRig.ContinuousMoveProvider, Is.SameAs(continuousMove));
                Assert.That(inputRig.SnapTurnProvider, Is.SameAs(snapTurn));
                Assert.That(inputRig.TeleportationProvider, Is.SameAs(teleport));
                Assert.That(continuousMove.mediator, Is.SameAs(mediator));
                Assert.That(snapTurn.mediator, Is.SameAs(mediator));
                Assert.That(snapTurn.enableTurnAround, Is.True, "Comfort defaults must expose the stick-down 180 degree turn-around option.");
                Assert.That(teleport.mediator, Is.SameAs(mediator));
                Assert.That(continuousTurn, Is.Not.Null);
                Assert.That(inputRig.ContinuousTurnProvider, Is.SameAs(continuousTurn));
                Assert.That(continuousTurn.mediator, Is.SameAs(mediator));
                // Gravity + physics-based jumping: CharacterController gives the player a
                // collision capsule, GravityProvider applies gravity via sphere-cast grounding,
                // and JumpProvider drives kinematic jump arcs with coyote time.
                Assert.That(characterController, Is.Not.Null, "Rig must have a CharacterController for physics-based locomotion.");
                Assert.That(gravityProvider, Is.Not.Null, "Rig must have a GravityProvider for falling off edges.");
                Assert.That(jumpProvider, Is.Not.Null, "Rig must have a JumpProvider for jumping.");
                Assert.That(comfortTransition, Is.Not.Null, "Rig repair must preserve the comfort fade transition for load and respawn jumps.");
                Assert.That(inputRig.CharacterController, Is.SameAs(characterController));
                Assert.That(inputRig.GravityProvider, Is.SameAs(gravityProvider));
                Assert.That(inputRig.JumpProvider, Is.SameAs(jumpProvider));
                Assert.That(foveatedRenderingController, Is.Not.Null, "Rig repair must enable fixed foveated rendering for Quest builds.");
                Assert.That(
                    foveatedRenderingController.FoveatedRenderingLevel,
                    Is.EqualTo(BlockiverseFoveatedRenderingController.DefaultFoveatedRenderingLevel));
                Assert.That(gravityProvider.mediator, Is.SameAs(mediator));
                Assert.That(gravityProvider.enabled, Is.True);
                Assert.That(gravityProvider.useGravity, Is.True);
                Assert.That(gravityProvider.useLocalSpaceGravity, Is.True);
                AssertGravityUsesVoxelTerrainMask(gravityProvider);
                Assert.That(jumpProvider.mediator, Is.SameAs(mediator));
                Assert.That(jumpProvider.disableGravityDuringJump, Is.False);
                Assert.That(jumpProvider.inAirJumpCount, Is.EqualTo(0));
                AssertJumpProviderUsesGameplayJump(inputRig, jumpProvider);
                Assert.That(poseDriver, Is.Not.Null);
                Assert.That(poseDriver.enabled, Is.True);
                Assert.That(poseDriver.updateType, Is.EqualTo(TrackedPoseDriver.UpdateType.UpdateAndBeforeRender));
                Assert.That(poseDriver.trackingType, Is.EqualTo(TrackedPoseDriver.TrackingType.RotationAndPosition));
                AssertBinding(poseDriver.positionInput, "<XRHMD>/centerEyePosition");
                AssertBinding(poseDriver.rotationInput, "<XRHMD>/centerEyeRotation");
                AssertBinding(poseDriver.trackingStateInput, "<XRHMD>/trackingState");

                AssertControllerPoseDriver(instance, "Left Controller", "<XRController>{LeftHand}/devicePosition", "<XRController>{LeftHand}/deviceRotation");
                AssertControllerPoseDriver(instance, "Right Controller", "<XRController>{RightHand}/devicePosition", "<XRController>{RightHand}/deviceRotation");
                AssertAimPoseDriver(instance, "Left Aim Pose", "<XRController>{LeftHand}/pointerPosition", "<XRController>{LeftHand}/pointerRotation");
                AssertAimPoseDriver(instance, "Right Aim Pose", "<XRController>{RightHand}/pointerPosition", "<XRController>{RightHand}/pointerRotation");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void XrRigPrefabIsWiredForComfortSettingsMenu()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            BlockiverseInputRig inputRig = prefab.GetComponent<BlockiverseInputRig>();
            XROrigin origin = prefab.GetComponent<XROrigin>();
            BlockiverseComfortSettings settings = prefab.GetComponent<BlockiverseComfortSettings>();
            Transform menuTransform = prefab.transform.Find("Camera Offset/Comfort Settings Menu");
            BlockiverseComfortMenu menu = menuTransform?.GetComponent<BlockiverseComfortMenu>();

            Assert.That(inputRig, Is.Not.Null);
            Assert.That(origin, Is.Not.Null);
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.VignetteEnabled, Is.False, "Generated rig should not start with the motion vignette enabled over the title/menu.");
            Assert.That(settings.VignetteStrength, Is.EqualTo(0.0f).Within(0.001f), "Generated rig should start with a fully open vignette strength.");
            Assert.That(settings.VignetteAperture, Is.EqualTo(1.0f).Within(0.001f), "Generated rig should leave the title/menu view unobscured.");
            Assert.That(origin.CameraYOffset, Is.EqualTo(settings.StandingEyeHeight).Within(0.01f));
            Assert.That(menuTransform, Is.Not.Null);
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.IsVisible, Is.False);
            Assert.That(inputRig.MenuPressed.GetPersistentEventCount(), Is.EqualTo(0),
                "Hardware Menu is routed by BlockiverseMenuController at runtime; persistent comfort toggles double-handle pause/back.");
            BlockiverseWorldSpacePanelPresenter presenter = menuTransform.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Assert.That(presenter, Is.Not.Null);
            Assert.That(presenter.PlaysShowFeedback, Is.True);
            Assert.That(presenter.ShowFeedbackCue, Is.EqualTo(BlockiverseAudioCue.UiConfirm));
            Assert.That(presenter.PlaysHideFeedback, Is.True);
            Assert.That(presenter.HideFeedbackCue, Is.EqualTo(BlockiverseAudioCue.UiCancel));
            Assert.That(menuTransform.localScale.x, Is.LessThanOrEqualTo(0.00135f), "Comfort menu should no longer fill the first-person view.");

            Image panelImage = menuTransform.Find("Panel")?.GetComponent<Image>();
            TMP_Text title = menuTransform.Find("Panel/Title")?.GetComponent<TMP_Text>();

            Assert.That(panelImage, Is.Not.Null);
            Assert.That(panelImage.color.a, Is.GreaterThanOrEqualTo(0.98f), "Comfort menu panel should be effectively opaque over terrain.");
            Assert.That(title, Is.Not.Null);
            Assert.That(title.fontSize, Is.LessThanOrEqualTo(34.0f), "Comfort menu title should fit a compact VR panel.");

            // Menu must contain Glide and Teleport selectors + vignette toggle.
            Toggle glideToggle = menuTransform.Find("Panel/Glide Toggle")?.GetComponent<Toggle>();
            Toggle teleportToggle = menuTransform.Find("Panel/Teleport Toggle")?.GetComponent<Toggle>();
            Toggle vignetteToggle = menuTransform.Find("Panel/Vignette Toggle")?.GetComponent<Toggle>();
            Slider vignetteSlider = menuTransform.Find("Panel/Vignette Slider/Slider")?.GetComponent<Slider>();

            Assert.That(glideToggle, Is.Not.Null, "Comfort menu should have a Glide Motion toggle.");
            Assert.That(teleportToggle, Is.Not.Null, "Comfort menu should have a Teleport toggle.");
            Assert.That(vignetteToggle, Is.Not.Null, "Comfort menu should have a Motion Vignette toggle.");
            Assert.That(vignetteSlider, Is.Not.Null, "Comfort menu should have a vignette strength slider.");
        }

        [Test]
        public void XrRigPrefabHasComfortTunnelingVignetteWiredToLocomotion()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            TunnelingVignetteController vignette = prefab.GetComponentInChildren<TunnelingVignetteController>(true);
            MeshRenderer vignetteRenderer = vignette != null ? vignette.GetComponent<MeshRenderer>() : null;

            Assert.That(vignette, Is.Not.Null, "Rig should carry a TunnelingVignetteController for comfort.");
            Assert.That(vignette.GetComponent<MeshFilter>()?.sharedMesh, Is.Not.Null, "Vignette mesh must be imported from the sample.");
            Assert.That(vignetteRenderer?.sharedMaterial, Is.Not.Null, "Vignette material must be assigned.");
            Assert.That(vignetteRenderer.enabled, Is.False, "Disabled comfort vignette must not render over the menu before locomotion.");
            Assert.That(vignette.defaultParameters.apertureSize, Is.EqualTo(1.0f).Within(0.001f), "Startup vignette aperture should be fully open until intentional locomotion.");
            Assert.That(
                vignette.locomotionVignetteProviders,
                Has.All.Matches<LocomotionVignetteProvider>(p => p.enabled && p.locomotionProvider != null),
                "Every vignette provider must be enabled and reference a locomotion provider.");

            var providerTypes = vignette.locomotionVignetteProviders
                .Select(provider => provider.locomotionProvider.GetType())
                .ToList();

            // Continuous motions and teleport mask intentional vection/viewpoint jumps. Snap turn,
            // gravity, and jump are excluded so the vignette does not close while the title/menu rig
            // is idle, settling, or performing a discrete comfort turn.
            Assert.That(providerTypes, Has.Count.GreaterThanOrEqualTo(3));
            Assert.That(providerTypes, Contains.Item(typeof(ContinuousMoveProvider)));
            Assert.That(providerTypes, Contains.Item(typeof(ContinuousTurnProvider)));
            Assert.That(providerTypes, Contains.Item(typeof(TeleportationProvider)));
            Assert.That(providerTypes, Has.No.Member(typeof(SnapTurnProvider)));
            Assert.That(providerTypes, Has.No.Member(typeof(GravityProvider)));
            Assert.That(providerTypes, Has.No.Member(typeof(JumpProvider)));
        }

        [Test]
        public void XrRigPrefabShowsFallbackHandsUntilLocalMetaAvatarIsReady()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);

            try
            {
                BlockiverseNetworkAvatarRig avatarRig = instance.GetComponent<BlockiverseNetworkAvatarRig>();

                Assert.That(avatarRig, Is.Not.Null);
                Assert.That(avatarRig.FirstPersonFallbackVisualsEnabled, Is.True);

                avatarRig.SetMetaAvatarAvailable(false);
                avatarRig.RefreshAvatarMode();

                Renderer[] renderers = avatarRig.FallbackRoot.GetComponentsInChildren<Renderer>(includeInactive: true);

                Assert.That(renderers, Has.Some.Matches<Renderer>(renderer =>
                    renderer.transform.name == "Fallback Left Hand" && renderer.enabled));
                Assert.That(renderers, Has.Some.Matches<Renderer>(renderer =>
                    renderer.transform.name == "Fallback Right Hand" && renderer.enabled));
                Assert.That(renderers, Has.None.Matches<Renderer>(renderer =>
                    renderer.transform.name == "Fallback Head" && renderer.enabled));
                Assert.That(renderers, Has.None.Matches<Renderer>(renderer =>
                    renderer.transform.name == "Fallback Body" && renderer.enabled));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void CreativeInputBridgeKeepsRayVisibleForPausedMenus()
        {
            GameObject root = new("Menu Ray Test");

            try
            {
                BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false);

                GameObject rayObject = new("Interaction Ray");
                rayObject.transform.SetParent(root.transform, worldPositionStays: false);
                XRRayInteractor ray = rayObject.AddComponent<XRRayInteractor>();
                LineRenderer lineRenderer = rayObject.AddComponent<LineRenderer>();
                XRInteractorLineVisual lineVisual = rayObject.AddComponent<XRInteractorLineVisual>();
                CreativeInteractionController interactionController = root.AddComponent<CreativeInteractionController>();
                BlockiverseCreativeInputBridge bridge = root.AddComponent<BlockiverseCreativeInputBridge>();

                interactionController.SetBlockEditingEnabled(false);
                lineRenderer.enabled = true;
                lineVisual.enabled = true;

                bridge.Configure(null, ray, interactionController);

                Assert.That(lineRenderer.enabled, Is.True, "Menus need the ray visual even while world input is blocked.");
                Assert.That(lineVisual.enabled, Is.True, "Menus need the XRI line visual even while world input is blocked.");
            }
            finally
            {
                BlockiverseRuntimeState.Reset();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void XrRigBootstrapperLeavesFallAndJumpOutOfVignetteProviders()
        {
            string source = File.ReadAllText("Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.XrRig.cs");

            Assert.That(
                source,
                Does.Not.Contain("AddVignetteProvider(controller, rig.GetComponent<GravityProvider>())"),
                "Gravity can become active during startup grounding, so it must not close the comfort vignette while idle.");
            Assert.That(
                source,
                Does.Not.Contain("AddVignetteProvider(controller, rig.GetComponent<JumpProvider>())"),
                "Jump arcs are discrete gameplay movement and should not be a startup/menu vignette trigger.");
        }

        [Test]
        public void InputRigSuppressesTurnWhileTurnHandRayHoversUi()
        {
            string source = File.ReadAllText("Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs");

            StringAssert.Contains("UpdateTurnProviderEnabledState()", source);
            StringAssert.Contains("IsActiveTurnRayOverUi()", source);
            StringAssert.Contains("IsOverUIGameObject()", source);
            StringAssert.Contains("!smoothTurn && !suppressTurnForUi", source);
            StringAssert.Contains("smoothTurn && !suppressTurnForUi", source);
        }

        [Test]
        public void XrRigPrefabHasJumpGravityAndCharacterControllerForCollision()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            JumpProvider jump = prefab.GetComponent<JumpProvider>();
            GravityProvider gravity = prefab.GetComponent<GravityProvider>();
            CharacterController cc = prefab.GetComponentInChildren<CharacterController>(true);

            Assert.That(jump, Is.Not.Null, "Rig should carry a JumpProvider.");
            Assert.That(gravity, Is.Not.Null, "Rig should carry a GravityProvider.");
            Assert.That(cc, Is.Not.Null, "Rig origin should carry a CharacterController for voxel collision.");
            Assert.That(gravity.enabled, Is.True, "Gravity must remain enabled so the player falls after jumping or walking off edges.");
            Assert.That(gravity.useGravity, Is.True, "Gravity must apply to the XR Origin.");
            Assert.That(gravity.useLocalSpaceGravity, Is.True, "Gravity should follow the XR Origin up direction.");
            AssertGravityUsesVoxelTerrainMask(gravity);
            Assert.That(jump.disableGravityDuringJump, Is.False, "Jump must not disable gravity and leave the player floating.");
            Assert.That(jump.inAirJumpCount, Is.EqualTo(0), "Only grounded jumps should be allowed for now.");
        }

        [Test]
        public void XrRigPrefabInputBindingsHaveJumpAndThumbstickUpTeleport()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            BlockiverseInputRig inputRig = prefab.GetComponent<BlockiverseInputRig>();

            Assert.That(inputRig, Is.Not.Null);
            Assert.That(inputRig.InputActions, Is.Not.Null);

            InputActionMap gameplayMap = inputRig.InputActions.FindActionMap(BlockiverseInputActionNames.GameplayMap, throwIfNotFound: false);
            Assert.That(gameplayMap, Is.Not.Null);

            InputAction jumpAction = gameplayMap.FindAction(BlockiverseInputActionNames.Jump, throwIfNotFound: false);
            Assert.That(jumpAction, Is.Not.Null, "Jump action must exist in the gameplay map.");
            Assert.That(jumpAction.bindings, Has.Some.Matches<InputBinding>(b =>
                (b.effectivePath ?? b.path ?? "") == "<XRController>{RightHand}/primaryButton"),
                "Jump should be bound to Right A.");
            Assert.That(jumpAction.bindings, Has.None.Matches<InputBinding>(b =>
                (b.effectivePath ?? b.path ?? "") == "<XRController>{LeftHand}/primaryButton"),
                "Jump must no longer be bound to Left X.");
            Assert.That(gameplayMap.FindAction(BlockiverseInputActionNames.Undo, throwIfNotFound: false), Is.Null,
                "Undo must not have a controller button.");
            InputAction blockEditingToggle = gameplayMap.FindAction(BlockiverseInputActionNames.BlockEditingToggle, throwIfNotFound: false);
            Assert.That(blockEditingToggle, Is.Not.Null, "Right B should toggle block editing through the gameplay map.");
            Assert.That(blockEditingToggle.bindings, Has.Some.Matches<InputBinding>(b =>
                (b.effectivePath ?? b.path ?? "") == "<XRController>{RightHand}/secondaryButton"),
                "Block editing toggle should be bound to Right B.");
            InputAction sprintAction = gameplayMap.FindAction(BlockiverseInputActionNames.Sprint, throwIfNotFound: false);
            Assert.That(sprintAction, Is.Not.Null, "Left stick click should drive sprint through the gameplay map.");
            Assert.That(sprintAction.bindings, Has.Some.Matches<InputBinding>(b =>
                (b.effectivePath ?? b.path ?? "") == "<XRController>{LeftHand}/thumbstickClicked"),
                "Sprint should be bound to left stick click.");
            Assert.That(inputRig.InputActions.actionMaps.SelectMany(map => map.bindings), Has.None.Matches<InputBinding>(b =>
                (b.effectivePath ?? b.path ?? "") == "<XRController>{LeftHand}/primaryButton" ||
                (b.effectivePath ?? b.path ?? "") == "<XRController>{LeftHand}/secondaryButton"),
                "Left X and Left Y are intentionally unassigned.");

            InputActionMap rightHandMap = inputRig.InputActions.FindActionMap(BlockiverseInputActionNames.RightHandMap, throwIfNotFound: false);
            Assert.That(rightHandMap?.FindAction(BlockiverseInputActionNames.Jump, throwIfNotFound: false), Is.Null,
                "RightHand/Jump is stale; JumpProvider should read Blockiverse Gameplay/Jump.");

            // Teleport Mode on both hands must be thumbstick-based, not a hardware button.
            foreach (string mapName in new[] { BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.RightHandMap })
            {
                InputActionMap map = inputRig.InputActions.FindActionMap(mapName, throwIfNotFound: false);
                Assert.That(map, Is.Not.Null, $"{mapName} must exist.");
                InputAction teleportMode = map.FindAction(BlockiverseInputActionNames.TeleportMode, throwIfNotFound: false);
                InputAction teleportSelect = map.FindAction(BlockiverseInputActionNames.TeleportSelect, throwIfNotFound: false);
                Assert.That(teleportMode, Is.Not.Null, $"Teleport Mode must exist in {mapName}.");
                Assert.That(teleportSelect, Is.Not.Null, $"Teleport Select must exist in {mapName}.");
                Assert.That(teleportMode.bindings, Has.Some.Matches<InputBinding>(b =>
                    (b.effectivePath ?? b.path ?? "").Contains("thumbstick")),
                    $"Teleport Mode in {mapName} should be bound to thumbstick.");
                Assert.That(teleportSelect.bindings, Has.Some.Matches<InputBinding>(b =>
                    (b.effectivePath ?? b.path ?? "").Contains("thumbstick")),
                    $"Teleport Select in {mapName} should be bound to thumbstick.");
                Assert.That(teleportMode.bindings, Has.None.Matches<InputBinding>(b =>
                    (b.effectivePath ?? b.path ?? "").Contains("primaryButton") ||
                    (b.effectivePath ?? b.path ?? "").Contains("triggerPressed")),
                    $"Teleport Mode in {mapName} must not use trigger or A button.");
                Assert.That(teleportSelect.bindings, Has.None.Matches<InputBinding>(b =>
                    (b.effectivePath ?? b.path ?? "").Contains("primaryButton") ||
                    (b.effectivePath ?? b.path ?? "").Contains("triggerPressed")),
                    $"Teleport Select in {mapName} must not use trigger or A button.");
            }
        }

        [Test]
        public void XrRigRuntimeRepairsEveryRayInteractorInput()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);

            try
            {
                BlockiverseInputRig inputRig = instance.GetComponent<BlockiverseInputRig>();

                Assert.That(inputRig, Is.Not.Null);
                inputRig.RepairRuntimeTracking();

                InputAction rightUiPress = inputRig.InputActions
                    .FindActionMap(BlockiverseInputActionNames.RightHandMap)
                    .FindAction(BlockiverseInputActionNames.UiPress);
                InputAction rightTeleportSelect = inputRig.InputActions
                    .FindActionMap(BlockiverseInputActionNames.RightHandMap)
                    .FindAction(BlockiverseInputActionNames.TeleportSelect);
                InputAction leftTeleportSelect = inputRig.InputActions
                    .FindActionMap(BlockiverseInputActionNames.LeftHandMap)
                    .FindAction(BlockiverseInputActionNames.TeleportSelect);

                XRRayInteractor rightInteractionRay = instance.transform
                    .Find("Camera Offset/Right Controller/Interaction Ray")
                    ?.GetComponent<XRRayInteractor>();
                XRRayInteractor rightTeleportRay = instance.transform
                    .Find("Camera Offset/Right Controller/Teleport Ray")
                    ?.GetComponent<XRRayInteractor>();
                XRRayInteractor leftTeleportRay = instance.transform
                    .Find("Camera Offset/Left Controller/Teleport Ray")
                    ?.GetComponent<XRRayInteractor>();

                Assert.That(rightInteractionRay, Is.Not.Null);
                Assert.That(rightTeleportRay, Is.Not.Null);
                Assert.That(leftTeleportRay, Is.Not.Null);
                AssertButtonReaderReferencesAction(rightInteractionRay.uiPressInput, rightUiPress, "Right trigger must click UI through the right UI Press action.");
                Assert.That(rightInteractionRay.enableUIInteraction, Is.True);
                Assert.That(rightInteractionRay.blockUIOnInteractableSelection, Is.False,
                    "Selecting block interactables must not suppress UI clicks while a menu is visible.");
                Assert.That(rightInteractionRay.maxRaycastDistance, Is.EqualTo(CreativeInteractionController.MaxBlockInteractionReachMeters).Within(0.001f));
                AssertButtonReaderReferencesAction(rightTeleportRay.selectInput, rightTeleportSelect, "Right teleport ray must use right thumbstick select.");
                AssertButtonReaderReferencesAction(leftTeleportRay.selectInput, leftTeleportSelect, "Left teleport ray must use left thumbstick select.");
                Assert.That(rightInteractionRay.rayOriginTransform, Is.Not.Null, "UI/block ray should use the OpenXR aim pose, not the controller grip pose.");
                Assert.That(rightInteractionRay.rayOriginTransform.name, Is.EqualTo("Right Aim Pose"));
                Assert.That(rightTeleportRay.rayOriginTransform, Is.SameAs(rightInteractionRay.rayOriginTransform));
                Assert.That(leftTeleportRay.rayOriginTransform, Is.Not.Null);
                Assert.That(leftTeleportRay.rayOriginTransform.name, Is.EqualTo("Left Aim Pose"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void CreativeFlightControllerTogglesXriFlightForCreativeWorlds()
        {
            var rigObject = new GameObject("Creative Flight Rig");
            var worldObject = new GameObject("Creative Flight World");

            try
            {
                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                var continuousMove = rigObject.AddComponent<ContinuousMoveProvider>();
                var gravity = rigObject.AddComponent<GravityProvider>();
                var jump = rigObject.AddComponent<JumpProvider>();
                inputRig.ConfigureLocomotion(
                    teleport: null,
                    snapTurn: null,
                    reset: null,
                    continuousMove: continuousMove,
                    gravity: gravity,
                    jump: jump);

                continuousMove.enabled = true;
                continuousMove.enableFly = false;
                continuousMove.moveSpeed = 1.0f;
                gravity.useGravity = true;
                jump.enabled = true;

                CreativeWorldManager worldManager = worldObject.AddComponent<CreativeWorldManager>();
                worldManager.SetGameMode(WorldGameMode.Creative);
                var flight = rigObject.AddComponent<BlockiverseCreativeFlightController>();
                flight.Configure(inputRig, worldManager);

                flight.ApplyFlightState();

                Assert.That(flight.IsFlightActive, Is.True);
                Assert.That(continuousMove.enabled, Is.False, "Creative flight moves by holding Right A toward the right-hand aim pose, not by stick locomotion.");
                Assert.That(continuousMove.enableFly, Is.False);
                Assert.That(gravity.useGravity, Is.False);
                Assert.That(jump.enabled, Is.False);
                Assert.That(inputRig.TurnWithBothHands, Is.True, "Both sticks should keep turning available while the player is in creative flight.");

                flight.SetFlightActive(false);

                Assert.That(flight.IsFlightActive, Is.False);
                Assert.That(continuousMove.enableFly, Is.False);
                Assert.That(gravity.useGravity, Is.True);
                Assert.That(jump.enabled, Is.True);
                Assert.That(inputRig.TurnWithBothHands, Is.False);

                flight.SetFlightActive(true);

                Assert.That(flight.IsFlightActive, Is.True);
                Assert.That(continuousMove.enabled, Is.False);
                Assert.That(gravity.useGravity, Is.False);
                Assert.That(jump.enabled, Is.False);

                worldManager.SetGameMode(WorldGameMode.Survival);
                flight.ApplyFlightState();

                Assert.That(flight.IsFlightActive, Is.False);
                Assert.That(continuousMove.enableFly, Is.False);
                Assert.That(gravity.useGravity, Is.True);
                Assert.That(jump.enabled, Is.True);
                Assert.That(inputRig.TurnWithBothHands, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(worldObject);
            }
        }

        [Test]
        public void SprintClickTogglesAndHoldTemporarilyRaisesMoveSpeed()
        {
            MethodInfo toggleMethod = typeof(BlockiverseInputRig).GetMethod(
                "ShouldToggleSprint",
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo speedMethod = typeof(BlockiverseInputRig).GetMethod(
                "ResolveSprintMoveSpeed",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(toggleMethod, Is.Not.Null, "Sprint should expose its click-vs-hold threshold for tests.");
            Assert.That(speedMethod, Is.Not.Null, "Sprint should expose move-speed scaling for tests.");
            Assert.That((bool)toggleMethod.Invoke(null, new object[] { 0.10f }), Is.True);
            Assert.That((bool)toggleMethod.Invoke(null, new object[] { 0.50f }), Is.False);
            Assert.That((float)speedMethod.Invoke(null, new object[] { 1.8f, false }), Is.EqualTo(1.8f).Within(0.001f));
            Assert.That((float)speedMethod.Invoke(null, new object[] { 1.8f, true }), Is.EqualTo(3.96f).Within(0.001f));
        }

        [Test]
        public void CreativeFlightMovementUsesRightHandAimOnlyWhileJumpHeld()
        {
            Vector3 displacement = BlockiverseCreativeFlightController.ComputeFlightDisplacement(
                new Vector3(0.0f, 0.25f, 1.0f),
                moveHeld: true,
                deltaSeconds: 0.5f);
            Vector3 expectedDirection = new Vector3(0.0f, 0.25f, 1.0f).normalized;

            Assert.That(displacement.normalized.x, Is.EqualTo(expectedDirection.x).Within(0.001f));
            Assert.That(displacement.normalized.y, Is.EqualTo(expectedDirection.y).Within(0.001f));
            Assert.That(displacement.normalized.z, Is.EqualTo(expectedDirection.z).Within(0.001f));
            Assert.That(displacement.magnitude, Is.EqualTo(BlockiverseCreativeFlightController.FlightSpeedBlocksPerSecond * 0.5f).Within(0.001f));

            Vector3 idle = BlockiverseCreativeFlightController.ComputeFlightDisplacement(Vector3.forward, moveHeld: false, deltaSeconds: 1.0f);

            Assert.That(idle, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void CreativeFlightSprintUsesCanonicalSprintSpeed()
        {
            MethodInfo sprintFlightMethod = typeof(BlockiverseCreativeFlightController).GetMethod(
                nameof(BlockiverseCreativeFlightController.ComputeFlightDisplacement),
                new[] { typeof(Vector3), typeof(bool), typeof(bool), typeof(float) });

            Assert.That(sprintFlightMethod, Is.Not.Null, "Creative flight should accept sprint state when computing displacement.");

            Vector3 sprint = (Vector3)sprintFlightMethod.Invoke(null, new object[]
            {
                Vector3.forward,
                true,
                true,
                0.5f,
            });

            Assert.That(sprint.normalized, Is.EqualTo(Vector3.forward));
            Assert.That(sprint.magnitude, Is.EqualTo(2.2f).Within(0.001f));
        }

        [Test]
        public void XrRigRuntimeRepairReusesInputActionReferences()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);

            try
            {
                BlockiverseInputRig inputRig = instance.GetComponent<BlockiverseInputRig>();

                Assert.That(inputRig, Is.Not.Null);
                inputRig.RepairRuntimeTracking();

                XRRayInteractor rightInteractionRay = instance.transform
                    .Find("Camera Offset/Right Controller/Interaction Ray")
                    ?.GetComponent<XRRayInteractor>();
                XRRayInteractor rightTeleportRay = instance.transform
                    .Find("Camera Offset/Right Controller/Teleport Ray")
                    ?.GetComponent<XRRayInteractor>();

                Assert.That(inputRig.JumpProvider, Is.Not.Null);
                Assert.That(rightInteractionRay, Is.Not.Null);
                Assert.That(rightTeleportRay, Is.Not.Null);

                InputActionReference jumpReference = inputRig.JumpProvider.jumpInput.inputActionReferencePerformed;
                InputActionReference uiPressReference = rightInteractionRay.uiPressInput.inputActionReferencePerformed;
                InputActionReference teleportReference = rightTeleportRay.selectInput.inputActionReferencePerformed;

                Assert.That(jumpReference, Is.Not.Null);
                Assert.That(uiPressReference, Is.Not.Null);
                Assert.That(teleportReference, Is.Not.Null);

                inputRig.RepairRuntimeTracking();

                Assert.That(inputRig.JumpProvider.jumpInput.inputActionReferencePerformed,
                    Is.SameAs(jumpReference),
                    "Repeated repair should reuse the Jump InputActionReference instead of allocating a new ScriptableObject.");
                Assert.That(rightInteractionRay.uiPressInput.inputActionReferencePerformed,
                    Is.SameAs(uiPressReference),
                    "Repeated repair should reuse the UI press InputActionReference instead of allocating a new ScriptableObject.");
                Assert.That(rightTeleportRay.selectInput.inputActionReferencePerformed,
                    Is.SameAs(teleportReference),
                    "Repeated repair should reuse the teleport select InputActionReference instead of allocating a new ScriptableObject.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void XrRigPrefabIsWiredForNativeInteractorsAndBlockMenu()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            Transform rightController = prefab.transform.Find("Camera Offset/Right Controller");
            Transform interactionRayTransform = rightController?.Find("Interaction Ray");
            Transform teleportRayTransform = rightController?.Find("Teleport Ray");
            // Left controller now also carries a teleport ray for Teleport mode.
            Transform leftController = prefab.transform.Find("Camera Offset/Left Controller");
            Transform rightAimPose = prefab.transform.Find("Camera Offset/Right Aim Pose");
            Transform leftAimPose = prefab.transform.Find("Camera Offset/Left Aim Pose");
            Transform leftTeleportRayTransform = leftController?.Find("Teleport Ray");
            Transform blockMenu = prefab.transform.Find("Camera Offset/Block Menu");
            XRRayInteractor interactionRay = interactionRayTransform?.GetComponent<XRRayInteractor>();
            XRInteractorLineVisual interactionLineVisual = interactionRayTransform?.GetComponent<XRInteractorLineVisual>();
            XRRayInteractor teleportRay = teleportRayTransform?.GetComponent<XRRayInteractor>();
            BlockiverseLocomotionRayMediator mediator = rightController?.GetComponent<BlockiverseLocomotionRayMediator>();
            BlockiverseCreativeInputBridge creativeInputBridge = prefab.GetComponent<BlockiverseCreativeInputBridge>();
            CreativeHotbar hotbar = blockMenu?.GetComponent<CreativeHotbar>();
            Canvas blockMenuCanvas = blockMenu?.GetComponent<Canvas>();
            BlockiverseWorldSpacePanelPresenter blockMenuPresenter = blockMenu?.GetComponent<BlockiverseWorldSpacePanelPresenter>();

            Assert.That(rightController, Is.Not.Null);
            Assert.That(prefab.transform.Find("Camera Offset/Left Controller"), Is.Not.Null);
            Assert.That(interactionRay, Is.Not.Null);
            Assert.That(interactionRay.enableUIInteraction, Is.True);
            Assert.That(interactionRay.blockUIOnInteractableSelection, Is.False);
            Assert.That(interactionRay.lineType, Is.EqualTo(XRRayInteractor.LineType.StraightLine));
            Assert.That(interactionRay.maxRaycastDistance, Is.EqualTo(CreativeInteractionController.MaxBlockInteractionReachMeters).Within(0.001f));
            Assert.That(rightAimPose, Is.Not.Null, "Right app pointer should be driven by the OpenXR aim pose.");
            Assert.That(leftAimPose, Is.Not.Null, "Left teleport pointer should be driven by the OpenXR aim pose.");
            Assert.That(interactionRay.rayOriginTransform, Is.SameAs(rightAimPose));
            Assert.That(interactionLineVisual, Is.Not.Null);
            Assert.That(interactionLineVisual.lineWidth, Is.EqualTo(0.01f).Within(0.0001f));
            Assert.That(teleportRay, Is.Not.Null);
            Assert.That(teleportRay.lineType, Is.EqualTo(XRRayInteractor.LineType.ProjectileCurve));
            Assert.That(teleportRay.rayOriginTransform, Is.SameAs(rightAimPose));
            Assert.That(teleportRayTransform.gameObject.activeSelf, Is.False);
            Assert.That(mediator, Is.Not.Null);
            // Left controller also carries a teleport ray (either thumbstick can aim in Teleport mode).
            Assert.That(leftTeleportRayTransform, Is.Not.Null, "Left controller must have a Teleport Ray.");
            XRRayInteractor leftTeleportRay = leftTeleportRayTransform.GetComponent<XRRayInteractor>();
            Assert.That(leftTeleportRay, Is.Not.Null);
            Assert.That(leftTeleportRay.rayOriginTransform, Is.SameAs(leftAimPose));
            Assert.That(leftTeleportRayTransform.gameObject.activeSelf, Is.False);
            Assert.That(creativeInputBridge, Is.Not.Null);
            Assert.That(prefab.transform.Find("Camera Offset/Right Controller/Ray Pointer Line"), Is.Null);
            Assert.That(blockMenu, Is.Not.Null);
            Assert.That(hotbar, Is.Not.Null);
            Assert.That(blockMenuCanvas, Is.Not.Null);
            Assert.That(blockMenuPresenter, Is.Not.Null);
            Assert.That(blockMenuPresenter.PlaysShowFeedback, Is.True);
            Assert.That(blockMenuPresenter.ShowFeedbackCue, Is.EqualTo(BlockiverseAudioCue.InventoryOpen));
            Assert.That(blockMenuPresenter.PlaysHideFeedback, Is.True);
            Assert.That(blockMenuPresenter.HideFeedbackCue, Is.EqualTo(BlockiverseAudioCue.InventoryClose));
            Assert.That(blockMenuCanvas.enabled, Is.False);

            BlockiverseInputRig inputRig = prefab.GetComponent<BlockiverseInputRig>();
            UnityEngine.Events.UnityEvent quickMenuEvent = inputRig.QuickMenuPressed;
            Assert.That(quickMenuEvent, Is.Not.Null);
            Assert.That(quickMenuEvent.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(quickMenuEvent.GetPersistentTarget(0), Is.SameAs(blockMenuPresenter));
            Assert.That(quickMenuEvent.GetPersistentMethodName(0), Is.EqualTo(nameof(BlockiverseWorldSpacePanelPresenter.ToggleVisible)));
        }

        [Test]
        public void XrRigPrefabHasFeedbackServicesAndAllAudioCueClipsAssigned()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            BlockiverseFeedbackSettings feedbackSettings = prefab.GetComponent<BlockiverseFeedbackSettings>();
            BlockiverseAudioCuePlayer audioCuePlayer = prefab.GetComponent<BlockiverseAudioCuePlayer>();
            BlockiverseVfxPool vfxPool = prefab.GetComponent<BlockiverseVfxPool>();
            BlockiverseVfxCuePlayer vfxCuePlayer = prefab.GetComponent<BlockiverseVfxCuePlayer>();
            BlockiverseInteractionHaptics interactionHaptics = prefab.GetComponent<BlockiverseInteractionHaptics>();
            BlockiverseInputRig inputRig = prefab.GetComponent<BlockiverseInputRig>();

            Assert.That(feedbackSettings, Is.Not.Null);
            Assert.That(audioCuePlayer, Is.Not.Null);
            Assert.That(vfxPool, Is.Not.Null);
            Assert.That(vfxCuePlayer, Is.Not.Null);
            Assert.That(interactionHaptics, Is.Not.Null);
            Assert.That(inputRig, Is.Not.Null);
            Assert.That(audioCuePlayer.FeedbackSettings, Is.SameAs(feedbackSettings));
            Assert.That(vfxCuePlayer.FeedbackSettings, Is.SameAs(feedbackSettings));
            Assert.That(vfxCuePlayer.Pool, Is.SameAs(vfxPool));
            Assert.That(interactionHaptics.FeedbackSettings, Is.SameAs(feedbackSettings));
            Assert.That(interactionHaptics.DominantHandHaptics, Is.Not.Null);
            Assert.That(inputRig.AudioCuePlayer, Is.SameAs(audioCuePlayer));

            foreach (BlockiverseAudioCue cue in System.Enum.GetValues(typeof(BlockiverseAudioCue)))
                Assert.That(audioCuePlayer.HasClipForCue(cue), Is.True, $"{cue} should have an assigned generated clip.");

            Assert.That(audioCuePlayer.FootstepClipCount, Is.EqualTo(2));

            // The music bed: a controller on the rig with a generated track per context.
            BlockiverseMusicController musicController = prefab.GetComponent<BlockiverseMusicController>();
            Assert.That(musicController, Is.Not.Null);
            foreach (BlockiverseMusicContext context in new[]
            {
                BlockiverseMusicContext.Menu,
                BlockiverseMusicContext.Day,
                BlockiverseMusicContext.Night,
                BlockiverseMusicContext.Cave,
            })
            {
                Assert.That(musicController.ResolveTrackClip(context), Is.Not.Null,
                    $"{context} should have an assigned generated music track.");
            }
        }

        [Test]
        public void XrRigPrefabShowsControllerMappingPopupAndInteractiveSurvivalHud()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            Transform popup = prefab.transform.Find("Camera Offset/Controller Mapping Popup");
            Transform startupOverlay = prefab.transform.Find("Camera Offset/Startup Loading Overlay");
            Transform survivalHud = prefab.transform.Find("Camera Offset/Survival HUD");

            Assert.That(popup, Is.Not.Null);
            BlockiverseWorldSpacePanelPresenter popupPresenter = popup.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Assert.That(popupPresenter, Is.Not.Null);
            Assert.That(popupPresenter.ShowOnStart, Is.False,
                "The title router must own first-frame menu visibility; controls stay available from Settings.");
            Assert.That(popup.GetComponent<Canvas>()?.enabled, Is.False);
            Assert.That(popup.GetComponentsInChildren<Button>(includeInactive: true), Has.Length.GreaterThanOrEqualTo(1));
            string popupText = string.Join("\n", popup.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .Select(label => label.text));

            // Canonical controller mapping (shared with the Settings → Controls screen).
            Assert.That(popupText, Does.Contain("Right trigger: press UI or break blocks"));
            Assert.That(popupText, Does.Contain("Right grip: place or use"));
            Assert.That(popupText, Does.Contain("Left grip: blocks menu"));
            Assert.That(popupText, Does.Contain("Menu: pause"));
            Assert.That(popupText, Does.Contain("Right stick: snap turn"));
            Assert.That(popupText, Does.Contain("Right A: jump"));
            Assert.That(popupText, Does.Contain("Right B: toggle block editing"));
            Assert.That(popupText, Does.Contain("Left stick: move"));
            Assert.That(popupText, Does.Contain("Left stick click: sprint toggle / hold sprint"));
            Assert.That(popupText, Does.Not.Contain("Right A + trigger"));
            Assert.That(popupText, Does.Not.Contain("Left X: jump"));
            Assert.That(popupText, Does.Not.Contain("Left Y: undo"));
            Assert.That(popupText, Does.Not.Contain("undo"));

            Assert.That(startupOverlay, Is.Not.Null);
            BlockiverseWorldSpacePanelPresenter startupPresenter = startupOverlay.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Assert.That(startupPresenter, Is.Not.Null);
            Assert.That(startupPresenter.ShowOnStart, Is.False,
                "The loading artwork must not auto-render over the title menu after the app reaches the menu.");
            Assert.That(startupOverlay.GetComponent<Canvas>()?.enabled, Is.False);
            Assert.That(startupOverlay.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Null,
                "Startup artwork is decorative and must not intercept tracked-device UI rays.");

            Canvas popupCanvas = popup.GetComponent<Canvas>();
            Canvas startupCanvas = startupOverlay.GetComponent<Canvas>();
            Assert.That(popupCanvas, Is.Not.Null);
            Assert.That(startupCanvas, Is.Not.Null);
            Assert.That(popupCanvas.sortingOrder, Is.GreaterThan(startupCanvas.sortingOrder),
                "The first-run controller map must render in front of any startup artwork.");

            CanvasGroup startupInputGate = startupOverlay.GetComponent<CanvasGroup>();
            Assert.That(startupInputGate, Is.Not.Null);
            Assert.That(startupInputGate.interactable, Is.False);
            Assert.That(startupInputGate.blocksRaycasts, Is.False);

            foreach (Graphic graphic in startupOverlay.GetComponentsInChildren<Graphic>(includeInactive: true))
                Assert.That(graphic.raycastTarget, Is.False, $"{graphic.name} must not receive UI raycasts.");

            Assert.That(survivalHud, Is.Not.Null);
            Assert.That(survivalHud.GetComponentsInChildren<Button>(includeInactive: true), Has.Length.GreaterThanOrEqualTo(11));
            Assert.That(survivalHud.GetComponentInChildren<SurvivalCraftingPanel>(includeInactive: true), Is.Not.Null);
            Assert.That(survivalHud.GetComponentInChildren<SurvivalInventoryPanel>(includeInactive: true), Is.Not.Null);

            RectTransform survivalHudRect = survivalHud.GetComponent<RectTransform>();
            Assert.That(survivalHudRect, Is.Not.Null);
            Assert.That(survivalHudRect.rect.width, Is.LessThanOrEqualTo(600.0f),
                "Gameplay HUD should be a compact overlay, not the full survival menu.");
            Assert.That(survivalHudRect.rect.height, Is.LessThanOrEqualTo(220.0f),
                "Gameplay HUD should not occupy the player's central field of view.");
            Assert.That(survivalHud.Find("Panel/Inventory")?.gameObject.activeSelf, Is.False,
                "The full inventory panel belongs behind an explicit inventory route, not always-visible gameplay HUD.");
            Assert.That(survivalHud.Find("Panel/Crafting")?.gameObject.activeSelf, Is.False,
                "The full crafting panel belongs behind an explicit crafting route, not always-visible gameplay HUD.");
            Assert.That(survivalHud.Find("Panel/Shared Crate")?.gameObject.activeSelf, Is.False,
                "The shared crate panel belongs behind an explicit crate route, not always-visible gameplay HUD.");
        }

        [Test]
        public void XrRigPrefabHasSinglePlayerFallbackAvatarRig()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);

            try
            {
                Component avatarRig = instance.GetComponent("BlockiverseNetworkAvatarRig");

                Assert.That(avatarRig, Is.Not.Null);
                Assert.That(instance.GetComponent("NetworkObject"), Is.Null, "The XR rig is an unspawned local pose proxy; NetworkObject belongs on the network player prefab.");
                avatarRig.GetType().GetMethod("RefreshAvatarMode").Invoke(avatarRig, null);
                Assert.That(GetAvatarProperty<bool>(avatarRig, "FallbackProxyEnabled"), Is.True);
                Assert.That(GetAvatarProperty<bool>(avatarRig, "MetaAvatarAvailable"), Is.False);
                Assert.That(GetAvatarProperty<Transform>(avatarRig, "FallbackRoot"), Is.Not.Null);
                Assert.That(GetAvatarProperty<Transform>(avatarRig, "HeadAnchor"), Is.Not.Null);
                Assert.That(GetAvatarProperty<Transform>(avatarRig, "LeftHandAnchor"), Is.Not.Null);
                Assert.That(GetAvatarProperty<Transform>(avatarRig, "RightHandAnchor"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static void AssertController(GameObject prefab, string controllerName, BlockiverseControllerRole expectedRole)
        {
            Transform controller = prefab.transform.Find($"Camera Offset/{controllerName}");

            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.GetComponent<BlockiverseControllerAnchor>()?.Role, Is.EqualTo(expectedRole));
            Assert.That(controller.GetComponent<BlockiverseControllerHaptics>()?.Role, Is.EqualTo(expectedRole));
        }

        static void AssertBinding(InputActionProperty property, string expectedPath)
        {
            Assert.That(property.action, Is.Not.Null);
            Assert.That(property.action.bindings, Has.Some.Matches<InputBinding>(
                binding => binding.effectivePath == expectedPath || binding.path == expectedPath));
        }

        static void AssertJumpProviderUsesGameplayJump(BlockiverseInputRig inputRig, JumpProvider jumpProvider)
        {
            InputAction jumpAction = inputRig.InputActions
                .FindActionMap(BlockiverseInputActionNames.GameplayMap)
                .FindAction(BlockiverseInputActionNames.Jump);

            Assert.That(jumpProvider.jumpInput.inputSourceMode,
                Is.EqualTo(XRInputButtonReader.InputSourceMode.InputActionReference));
            Assert.That(jumpProvider.jumpInput.inputActionReferencePerformed?.action,
                Is.SameAs(jumpAction));
        }

        static void AssertButtonReaderReferencesAction(XRInputButtonReader reader, InputAction action, string message)
        {
            Assert.That(reader.inputSourceMode,
                Is.EqualTo(XRInputButtonReader.InputSourceMode.InputActionReference),
                message);
            Assert.That(reader.inputActionReferencePerformed?.action,
                Is.SameAs(action),
                message);
        }

        static void AssertControllerPoseDriver(GameObject instance, string controllerName, string positionPath, string rotationPath)
        {
            Transform controller = instance.transform.Find($"Camera Offset/{controllerName}");

            Assert.That(controller, Is.Not.Null);

            TrackedPoseDriver driver = controller.GetComponent<TrackedPoseDriver>();

            Assert.That(driver, Is.Not.Null);
            Assert.That(driver.enabled, Is.True);
            Assert.That(driver.updateType, Is.EqualTo(TrackedPoseDriver.UpdateType.UpdateAndBeforeRender));
            Assert.That(driver.trackingType, Is.EqualTo(TrackedPoseDriver.TrackingType.RotationAndPosition));
            AssertBinding(driver.positionInput, positionPath);
            AssertBinding(driver.rotationInput, rotationPath);
        }

        static void AssertAimPoseDriver(GameObject instance, string aimPoseName, string positionPath, string rotationPath)
        {
            Transform aimPose = instance.transform.Find($"Camera Offset/{aimPoseName}");

            Assert.That(aimPose, Is.Not.Null);

            TrackedPoseDriver driver = aimPose.GetComponent<TrackedPoseDriver>();

            Assert.That(driver, Is.Not.Null);
            Assert.That(driver.enabled, Is.True);
            Assert.That(driver.updateType, Is.EqualTo(TrackedPoseDriver.UpdateType.UpdateAndBeforeRender));
            Assert.That(driver.trackingType, Is.EqualTo(TrackedPoseDriver.TrackingType.RotationAndPosition));
            AssertBinding(driver.positionInput, positionPath);
            AssertBinding(driver.rotationInput, rotationPath);
        }

        static void AssertGravityUsesVoxelTerrainMask(GravityProvider gravityProvider)
        {
            int terrainLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);

            Assert.That(terrainLayer, Is.GreaterThanOrEqualTo(0), "Blockiverse terrain interaction layer must exist.");
            Assert.That(gravityProvider.sphereCastLayerMask.value, Is.EqualTo(1 << terrainLayer),
                "Gravity grounding must ignore the player CharacterController and only test voxel terrain.");
            Assert.That(gravityProvider.sphereCastTriggerInteraction, Is.EqualTo(QueryTriggerInteraction.Ignore));
        }

        static T GetAvatarProperty<T>(Component avatarRig, string propertyName)
        {
            return (T)avatarRig.GetType().GetProperty(propertyName).GetValue(avatarRig);
        }
    }
}
