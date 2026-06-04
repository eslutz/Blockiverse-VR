using System.Linq;
using Blockiverse.Core;
using Blockiverse.Gameplay;
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
                TrackedPoseDriver poseDriver = inputRig?.HeadPoseDriver;

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
                Assert.That(inputRig.CharacterController, Is.SameAs(characterController));
                Assert.That(inputRig.GravityProvider, Is.SameAs(gravityProvider));
                Assert.That(inputRig.JumpProvider, Is.SameAs(jumpProvider));
                Assert.That(gravityProvider.mediator, Is.SameAs(mediator));
                Assert.That(jumpProvider.mediator, Is.SameAs(mediator));
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
            Assert.That(origin.CameraYOffset, Is.EqualTo(settings.StandingEyeHeight).Within(0.01f));
            Assert.That(menuTransform, Is.Not.Null);
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.IsVisible, Is.False);
            Assert.That(inputRig.MenuPressed.GetPersistentEventCount(), Is.EqualTo(1));
            BlockiverseWorldSpacePanelPresenter presenter = menuTransform.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Assert.That(presenter, Is.Not.Null);
            Assert.That(presenter.PlaysShowFeedback, Is.True);
            Assert.That(presenter.ShowFeedbackCue, Is.EqualTo(BlockiverseAudioCue.UiConfirm));
            Assert.That(presenter.PlaysHideFeedback, Is.True);
            Assert.That(presenter.HideFeedbackCue, Is.EqualTo(BlockiverseAudioCue.UiCancel));
            Assert.That(inputRig.MenuPressed.GetPersistentTarget(0), Is.SameAs(presenter));
            Assert.That(inputRig.MenuPressed.GetPersistentMethodName(0), Is.EqualTo(nameof(BlockiverseWorldSpacePanelPresenter.ToggleVisible)));

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

            Assert.That(vignette, Is.Not.Null, "Rig should carry a TunnelingVignetteController for comfort.");
            Assert.That(vignette.GetComponent<MeshFilter>()?.sharedMesh, Is.Not.Null, "Vignette mesh must be imported from the sample.");
            Assert.That(vignette.GetComponent<MeshRenderer>()?.sharedMaterial, Is.Not.Null, "Vignette material must be assigned.");
            Assert.That(
                vignette.locomotionVignetteProviders,
                Has.All.Matches<LocomotionVignetteProvider>(p => p.enabled && p.locomotionProvider != null),
                "Every vignette provider must be enabled and reference a locomotion provider.");

            var providerTypes = vignette.locomotionVignetteProviders
                .Select(provider => provider.locomotionProvider.GetType())
                .ToList();

            // Continuous motions and teleport mask vection/viewpoint jumps; snap turn is a discrete
            // comfort option and is intentionally excluded to avoid a per-turn vignette flicker.
            Assert.That(providerTypes, Has.Count.EqualTo(3));
            Assert.That(providerTypes, Contains.Item(typeof(ContinuousMoveProvider)));
            Assert.That(providerTypes, Contains.Item(typeof(ContinuousTurnProvider)));
            Assert.That(providerTypes, Contains.Item(typeof(TeleportationProvider)));
            Assert.That(providerTypes, Has.No.Member(typeof(SnapTurnProvider)));
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
                (b.effectivePath ?? b.path ?? "").Contains("primaryButton")),
                "Jump should be bound to primaryButton.");

            InputActionMap rightHandMap = inputRig.InputActions.FindActionMap(BlockiverseInputActionNames.RightHandMap, throwIfNotFound: false);
            Assert.That(rightHandMap?.FindAction(BlockiverseInputActionNames.Jump, throwIfNotFound: false), Is.Null,
                "RightHand/Jump is stale; JumpProvider should read Blockiverse Gameplay/Jump.");

            // Teleport Mode on both hands must be thumbstick-based, not a hardware button.
            foreach (string mapName in new[] { BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.RightHandMap })
            {
                InputActionMap map = inputRig.InputActions.FindActionMap(mapName, throwIfNotFound: false);
                Assert.That(map, Is.Not.Null, $"{mapName} must exist.");
                InputAction teleportMode = map.FindAction(BlockiverseInputActionNames.TeleportMode, throwIfNotFound: false);
                Assert.That(teleportMode, Is.Not.Null, $"Teleport Mode must exist in {mapName}.");
                Assert.That(teleportMode.bindings, Has.Some.Matches<InputBinding>(b =>
                    (b.effectivePath ?? b.path ?? "").Contains("thumbstick")),
                    $"Teleport Mode in {mapName} should be bound to thumbstick.");
                Assert.That(teleportMode.bindings, Has.None.Matches<InputBinding>(b =>
                    (b.effectivePath ?? b.path ?? "").Contains("primaryButton") ||
                    (b.effectivePath ?? b.path ?? "").Contains("triggerPressed")),
                    $"Teleport Mode in {mapName} must not use trigger or A button.");
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
            Assert.That(interactionRay.lineType, Is.EqualTo(XRRayInteractor.LineType.StraightLine));
            Assert.That(interactionLineVisual, Is.Not.Null);
            Assert.That(teleportRay, Is.Not.Null);
            Assert.That(teleportRay.lineType, Is.EqualTo(XRRayInteractor.LineType.ProjectileCurve));
            Assert.That(teleportRayTransform.gameObject.activeSelf, Is.False);
            Assert.That(mediator, Is.Not.Null);
            // Left controller also carries a teleport ray (either thumbstick can aim in Teleport mode).
            Assert.That(leftTeleportRayTransform, Is.Not.Null, "Left controller must have a Teleport Ray.");
            Assert.That(leftTeleportRayTransform.GetComponent<XRRayInteractor>(), Is.Not.Null);
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
        public void XrRigPrefabHasAllM6AudioCueClipsAssigned()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            BlockiverseAudioCuePlayer audioCuePlayer = prefab.GetComponent<BlockiverseAudioCuePlayer>();

            Assert.That(audioCuePlayer, Is.Not.Null);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.BlockBreak), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.BlockPlace), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.UiSelect), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.UiConfirm), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.UiCancel), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.InventoryOpen), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.InventoryClose), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.CraftSuccess), Is.True);
            Assert.That(audioCuePlayer.HasClipForCue(BlockiverseAudioCue.CraftFail), Is.True);
            Assert.That(audioCuePlayer.FootstepClipCount, Is.EqualTo(2));
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
            Assert.That(popupPresenter.ShowOnStart, Is.True);
            Assert.That(popup.GetComponent<Canvas>()?.enabled, Is.False);
            Assert.That(popup.GetComponentsInChildren<Button>(includeInactive: true), Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(
                popup.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    .Any(label => label.text.Contains("Right trigger")),
                Is.True);

            Assert.That(startupOverlay, Is.Not.Null);
            BlockiverseWorldSpacePanelPresenter startupPresenter = startupOverlay.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Assert.That(startupPresenter, Is.Not.Null);
            Assert.That(startupPresenter.ShowOnStart, Is.True);
            Assert.That(startupOverlay.GetComponent<Canvas>()?.enabled, Is.False);

            Assert.That(survivalHud, Is.Not.Null);
            Assert.That(survivalHud.GetComponentsInChildren<Button>(includeInactive: true), Has.Length.GreaterThanOrEqualTo(11));
            Assert.That(survivalHud.GetComponentInChildren<SurvivalCraftingPanel>(includeInactive: true), Is.Not.Null);
            Assert.That(survivalHud.GetComponentInChildren<SurvivalInventoryPanel>(includeInactive: true), Is.Not.Null);
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

        static T GetAvatarProperty<T>(Component avatarRig, string propertyName)
        {
            return (T)avatarRig.GetType().GetProperty(propertyName).GetValue(avatarRig);
        }
    }
}
