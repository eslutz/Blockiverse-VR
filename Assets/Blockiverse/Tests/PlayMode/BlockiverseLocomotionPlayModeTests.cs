using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;
using InputTrackingState = UnityEngine.XR.InputTrackingState;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class BlockiverseLocomotionPlayModeTests
    {
        [UnityTest]
        public IEnumerator TeleportationProviderMovesBodyToRequestedWorldPosition()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);

            try
            {
                ConfigureXriLocomotionStack(
                    rigObject,
                    origin,
                    out _,
                    out _,
                    out TeleportationProvider teleport,
                    out _,
                    out _);

                Assert.That(teleport.QueueTeleportRequest(new TeleportRequest
                {
                    destinationPosition = new Vector3(2.0f, 0.0f, 3.0f),
                    destinationRotation = Quaternion.identity,
                    matchOrientation = MatchOrientation.None
                }), Is.True);

                yield return null;

                Assert.That(Vector3.Distance(origin.transform.position, new Vector3(2.0f, 0.0f, 3.0f)), Is.LessThan(0.01f));
            }
            finally
            {
                DestroyRigImmediate(rigObject);
            }
        }

        [UnityTest]
        public IEnumerator TeleportationProviderPreservesOffsetCameraWhenMovingBody()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);

            try
            {
                Vector3 cameraOffset = new(0.75f, 0.0f, -0.25f);
                Vector3 destination = new(2.0f, 0.0f, 3.0f);

                origin.Camera.transform.localPosition = cameraOffset;
                ConfigureXriLocomotionStack(
                    rigObject,
                    origin,
                    out _,
                    out _,
                    out TeleportationProvider teleport,
                    out _,
                    out _);

                Assert.That(teleport.QueueTeleportRequest(new TeleportRequest
                {
                    destinationPosition = destination,
                    destinationRotation = Quaternion.identity,
                    matchOrientation = MatchOrientation.None
                }), Is.True);

                yield return null;

                Assert.That(origin.Camera.transform.localPosition, Is.EqualTo(cameraOffset));
                Assert.That(Vector3.Distance(origin.Camera.transform.position, destination), Is.LessThan(0.01f));
                Assert.That(Vector3.Distance(origin.transform.position, destination - cameraOffset), Is.LessThan(0.01f));
            }
            finally
            {
                DestroyRigImmediate(rigObject);
            }
        }

        [UnityTest]
        public IEnumerator SnapTurnProviderRotatesXrOriginByConfiguredDegrees()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);

            try
            {
                ConfigureXriLocomotionStack(
                    rigObject,
                    origin,
                    out _,
                    out _,
                    out _,
                    out _,
                    out SnapTurnProvider snapTurn);
                snapTurn.turnAmount = 45.0f;
                snapTurn.rightHandTurnInput = CreateManualVector2Reader("Right Hand Snap Turn", Vector2.right);

                yield return null;

                Assert.That(Mathf.DeltaAngle(origin.transform.eulerAngles.y, 45.0f), Is.EqualTo(0.0f).Within(0.1f));
            }
            finally
            {
                DestroyRigImmediate(rigObject);
            }
        }

        [UnityTest]
        public IEnumerator SnapTurnProviderRotatesAroundOffsetCameraPosition()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);

            try
            {
                origin.Camera.transform.localPosition = new Vector3(0.5f, 0.0f, 1.0f);
                Vector3 cameraPosition = origin.Camera.transform.position;
                ConfigureXriLocomotionStack(
                    rigObject,
                    origin,
                    out _,
                    out _,
                    out _,
                    out _,
                    out SnapTurnProvider snapTurn);
                snapTurn.turnAmount = 60.0f;
                snapTurn.rightHandTurnInput = CreateManualVector2Reader("Right Hand Snap Turn", Vector2.right);

                yield return null;

                Assert.That(Vector3.Distance(origin.Camera.transform.position, cameraPosition), Is.LessThan(0.01f));
                Assert.That(Mathf.DeltaAngle(origin.Camera.transform.eulerAngles.y, 60.0f), Is.EqualTo(0.0f).Within(0.1f));
            }
            finally
            {
                DestroyRigImmediate(rigObject);
            }
        }

        [UnityTest]
        public IEnumerator ContinuousMoveProviderTranslatesOriginRelativeToHeadYaw()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);

            try
            {
                origin.CameraYOffset = 1.6f;
                origin.Camera.transform.localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                ConfigureXriLocomotionStack(
                    rigObject,
                    origin,
                    out _,
                    out _,
                    out _,
                    out ContinuousMoveProvider continuousMove,
                    out _);
                continuousMove.moveSpeed = 2.0f;

                yield return null;

                Vector3 startPosition = origin.transform.position;
                Vector3 expectedMoveDirection = Vector3.ProjectOnPlane(origin.Camera.transform.forward, origin.transform.up).normalized;
                continuousMove.leftHandMoveInput = CreateManualVector2Reader("Left Hand Move", new Vector2(0.0f, 1.0f));

                // Capture the starting position after the first frame so the assertion measures
                // only the delta from continuous move, not any displacement caused by a
                // GravityProvider that may be active elsewhere in the scene (e.g. the Boot scene
                // was loaded by a prior test and not fully unloaded yet).
                yield return null;
                Vector3 positionAfterFirstFrame = origin.transform.position;
                yield return null;
                Vector3 delta = origin.transform.position - positionAfterFirstFrame;

                Assert.That(delta.magnitude, Is.GreaterThan(0.0f));
                Assert.That(Vector3.Dot(delta.normalized, expectedMoveDirection), Is.GreaterThan(0.95f));
            }
            finally
            {
                DestroyRigImmediate(rigObject);
            }
        }

        [UnityTest]
        public IEnumerator InputRigDisablesContinuousMoveProviderWhenComfortToggleIsOff()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);

            try
            {
                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                settings.LocomotionMode = BlockiverseLocomotionMode.Teleport;
                ConfigureXriLocomotionStack(
                    rigObject,
                    origin,
                    out XRBodyTransformer bodyTransformer,
                    out LocomotionMediator mediator,
                    out TeleportationProvider teleport,
                    out ContinuousMoveProvider continuousMove,
                    out SnapTurnProvider snapTurn);

                var heightReset = rigObject.AddComponent<BlockiverseHeightReset>();
                heightReset.Configure(origin, settings);

                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.ConfigureLocomotion(teleport, snapTurn, heightReset, continuousMove, mediator, bodyTransformer, settings);

                yield return null;

                Vector3 disabledPosition = origin.transform.position;
                continuousMove.leftHandMoveInput = CreateManualVector2Reader("Left Hand Move", new Vector2(0.0f, 1.0f));

                yield return null;

                Assert.That(continuousMove.enabled, Is.False);
                Assert.That(Vector3.Distance(origin.transform.position, disabledPosition), Is.LessThan(0.001f));
            }
            finally
            {
                DestroyRigImmediate(rigObject);
            }
        }

        [Test]
        public void HeightResetRestoresStandingEyeHeight()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);

            try
            {
                origin.CameraYOffset = 1.2f;

                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                var heightReset = rigObject.AddComponent<BlockiverseHeightReset>();
                heightReset.Configure(origin, settings);

                heightReset.ResetHeight();
                Assert.That(origin.CameraYOffset, Is.EqualTo(BlockiverseComfortSettings.FixedStandingEyeHeight).Within(0.01f));
            }
            finally
            {
                DestroyRigImmediate(rigObject);
            }
        }

        [Test]
        public void HeightResetCalibratesCameraOffsetForFloorOrigin()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);

            try
            {
                origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                origin.Camera.transform.localPosition = new Vector3(0.0f, 1.05f, 0.0f);

                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                var heightReset = rigObject.AddComponent<BlockiverseHeightReset>();
                heightReset.Configure(origin, settings);

                heightReset.ResetHeight();

                // Fixed mode normalizes the view to the eye-height constant regardless of the
                // tracked camera height (1.05 here).
                Assert.That(
                    origin.CameraFloorOffsetObject.transform.localPosition.y,
                    Is.EqualTo(BlockiverseComfortSettings.FixedStandingEyeHeight - 1.05f).Within(0.01f));
            }
            finally
            {
                DestroyRigImmediate(rigObject);
            }
        }

        // Retargeted from the uGUI comfort menu at the uGUI cutover. What survives is the part
        // no EditMode test can cover: that the GENERATED Boot scene actually contains a comfort
        // screen which found its named elements. A screen that generates but never binds renders
        // as a healthy blank panel, so IsBound is the assertion that matters here.
        //
        // Initial visibility is deliberately not asserted: the first-run comfort route makes it
        // depend on a PlayerPrefs flag, where the uGUI menu was only ever toggled by input.
        //
        // The configure-after-Awake callback-registration guard that used to sit alongside this
        // is now ComfortSettingsScreenEditModeTests' Attach/Reattach pair.
        [UnityTest]
        public IEnumerator BootSceneContainsComfortSettingsScreen()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle("Boot");

            ComfortSettingsScreenController screen =
                Object.FindFirstObjectByType<ComfortSettingsScreenController>(FindObjectsInactive.Include);
            Assert.That(screen, Is.Not.Null);
            Assert.That(screen.IsBound, Is.True,
                "The generated comfort screen did not find its elements — the UXML did not load, " +
                "or its element names drifted from the controller.");

            screen.SetVisible(true, true);
            Assert.That(screen.IsVisible, Is.True);

            screen.SetVisible(false, false);
            Assert.That(screen.IsVisible, Is.False);

            Scene cleanupScene = SceneManager.CreateScene("LocomotionTestCleanup");
            SceneManager.SetActiveScene(cleanupScene);
            AsyncOperation unload = SceneManager.UnloadSceneAsync("Boot");

            if (unload != null)
                yield return unload;
        }

        static GameObject CreateXrOrigin(out XROrigin origin)
        {
            GameObject rigObject = new("Test XR Origin");
            rigObject.SetActive(false);

            GameObject cameraOffset = new("Camera Offset");
            cameraOffset.transform.SetParent(rigObject.transform, false);

            GameObject cameraObject = new("Main Camera");
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();

            origin = rigObject.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.Camera = camera;
            rigObject.SetActive(true);

            return rigObject;
        }

        static void ConfigureXriLocomotionStack(
            GameObject rigObject,
            XROrigin origin,
            out XRBodyTransformer bodyTransformer,
            out LocomotionMediator mediator,
            out TeleportationProvider teleport,
            out ContinuousMoveProvider continuousMove,
            out SnapTurnProvider snapTurn)
        {
            CharacterController characterController = rigObject.GetComponent<CharacterController>();

            if (characterController == null)
                characterController = rigObject.AddComponent<CharacterController>();

            BlockiverseInputRig.ConfigureCharacterController(characterController);

            bodyTransformer = rigObject.AddComponent<XRBodyTransformer>();
            bodyTransformer.xrOrigin = origin;

            mediator = rigObject.AddComponent<LocomotionMediator>();
            mediator.xrOrigin = origin;

            teleport = rigObject.AddComponent<TeleportationProvider>();
            teleport.mediator = mediator;
            teleport.delayTime = 0.0f;

            continuousMove = rigObject.AddComponent<ContinuousMoveProvider>();
            continuousMove.mediator = mediator;
            continuousMove.forwardSource = origin.Camera.transform;
            continuousMove.enableStrafe = true;
            continuousMove.enableFly = false;
            continuousMove.leftHandMoveInput = CreateUnusedVector2Reader("Left Hand Move");
            continuousMove.rightHandMoveInput = CreateUnusedVector2Reader("Right Hand Move");

            snapTurn = rigObject.AddComponent<SnapTurnProvider>();
            snapTurn.mediator = mediator;
            snapTurn.enableTurnLeftRight = true;
            snapTurn.enableTurnAround = true;
            snapTurn.delayTime = 0.0f;
            snapTurn.leftHandTurnInput = CreateUnusedVector2Reader("Left Hand Snap Turn");
            snapTurn.rightHandTurnInput = CreateUnusedVector2Reader("Right Hand Snap Turn");
        }

        static void DestroyRigImmediate(GameObject rigObject)
        {
            if (rigObject == null)
                return;

            foreach (TrackedPoseDriver driver in rigObject.GetComponentsInChildren<TrackedPoseDriver>(true))
                driver.enabled = false;

            Object.DestroyImmediate(rigObject);
        }

        static XRInputValueReader<Vector2> CreateManualVector2Reader(string name, Vector2 value)
        {
            return new XRInputValueReader<Vector2>(name, XRInputValueReader.InputSourceMode.ManualValue)
            {
                manualValue = value
            };
        }

        static XRInputValueReader<Vector2> CreateUnusedVector2Reader(string name)
        {
            return new XRInputValueReader<Vector2>(name, XRInputValueReader.InputSourceMode.Unused);
        }

        [UnityTearDown]
        public IEnumerator CleanupTrackedPoseDriversAfterTest()
        {
            yield return BlockiversePlayModeSceneTestUtility.CleanupTrackedPoseDrivers();
        }
    }

    public sealed class BlockiverseInputRigActionPlayModeTests : InputTestFixture
    {
        [SetUp]
        public override void Setup()
        {
            base.Setup();
            BlockiverseRuntimeState.Reset();
        }

        [TearDown]
        public override void TearDown()
        {
            BlockiverseRuntimeState.Reset();
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator ConfiguredInputActionsDriveLocomotionAndMenuToggle()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);
            InputActionAsset actions = CreateTestActions();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();

            try
            {
                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                settings.SnapTurnDegrees = 45.0f;

                ConfigureXriLocomotionStack(
                    rigObject,
                    origin,
                    out XRBodyTransformer bodyTransformer,
                    out LocomotionMediator mediator,
                    out TeleportationProvider teleport,
                    out ContinuousMoveProvider continuousMove,
                    out SnapTurnProvider snapTurn);

                var heightReset = rigObject.AddComponent<BlockiverseHeightReset>();
                heightReset.Configure(origin, settings);

                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);
                inputRig.ConfigureLocomotion(teleport, snapTurn, heightReset, continuousMove, mediator, bodyTransformer, settings);

                // The tail of this test is about MenuPressed edges, not about a menu: one press
                // toggles once, holding does not re-toggle, and release-then-press toggles back.
                // A plain flag is the whole contract; it used to be the uGUI comfort menu's
                // ToggleVisible, which pinned an input test to a menu backend.
                bool menuVisible = false;
                inputRig.MenuPressed.AddListener(() => menuVisible = !menuVisible);

                Set(gamepad.rightStick, Vector2.right);
                yield return null;
                yield return null;
                Assert.That(Mathf.DeltaAngle(origin.transform.eulerAngles.y, 45.0f), Is.EqualTo(0.0f).Within(0.1f));

                Set(gamepad.rightStick, Vector2.zero);
                yield return null;
                yield return null;

                // Teleport is now native target-based (held Teleport Mode enables the teleport ray,
                // which selects a TeleportationArea). Holding the mode alone must not move the rig.
                Vector3 positionBeforeTeleportMode = origin.transform.position;
                Press(gamepad.leftShoulder);
                yield return null;
                Vector3 teleportModeDelta = origin.transform.position - positionBeforeTeleportMode;
                Assert.That(Vector3.ProjectOnPlane(teleportModeDelta, origin.transform.up).magnitude, Is.LessThan(0.01f));
                Release(gamepad.leftShoulder);
                yield return null;

                Assert.That(menuVisible, Is.False);
                Press(gamepad.startButton);
                yield return null;
                Assert.That(menuVisible, Is.True);

                yield return null;
                Assert.That(menuVisible, Is.True);

                Release(gamepad.startButton);
                yield return null;
                Press(gamepad.startButton);
                yield return null;
                Assert.That(menuVisible, Is.False);
            }
            finally
            {
                DestroyRigImmediate(rigObject);
                Object.DestroyImmediate(actions);
            }
        }

        [UnityTest]
        public IEnumerator MoveActionDrivesContinuousLocomotion()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);
            InputActionAsset actions = CreateTestActions();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            GameObject floor = null;

            try
            {
                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                settings.ContinuousMoveSpeed = 2.0f;

                ConfigureXriLocomotionStack(
                    rigObject,
                    origin,
                    out XRBodyTransformer bodyTransformer,
                    out LocomotionMediator mediator,
                    out TeleportationProvider teleport,
                    out ContinuousMoveProvider continuousMove,
                    out SnapTurnProvider snapTurn);

                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);
                inputRig.ConfigureLocomotion(teleport, snapTurn, null, continuousMove, mediator, bodyTransformer, settings);

                // Ground the rig: the rig auto-adds a GravityProvider, and without a floor
                // the origin falls during the measured frame — XRI 3.5's in-air control
                // damping then dilutes the horizontal move direction below the threshold.
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Move Test Floor";
                floor.layer = BlockiverseProject.InteractionLayerIndex;
                floor.transform.localScale = new Vector3(20.0f, 1.0f, 20.0f);
                floor.transform.position = new Vector3(0.0f, -0.5f, 0.0f);

                yield return null;

                Vector3 expectedMoveDirection = Vector3.ProjectOnPlane(origin.Camera.transform.forward, origin.transform.up).normalized;
                Set(gamepad.leftStick, new Vector2(0.0f, 1.0f));

                // Measure the second frame only (sibling-test pattern): the first frame
                // after input absorbs gravity snap-down/CharacterController settling from
                // scene state leaked by earlier tests in the same play session.
                yield return null;
                Vector3 positionAfterFirstFrame = origin.transform.position;
                yield return null;

                Vector3 movement = origin.transform.position - positionAfterFirstFrame;
                Assert.That(movement.magnitude, Is.GreaterThan(0.0f));
                Assert.That(Vector3.Dot(movement.normalized, expectedMoveDirection), Is.GreaterThan(0.95f));

                Set(gamepad.leftStick, Vector2.zero);
                yield return null;
            }
            finally
            {
                if (floor != null)
                    Object.DestroyImmediate(floor);
                DestroyRigImmediate(rigObject);
                Object.DestroyImmediate(actions);
            }
        }

        [UnityTest]
        public IEnumerator LeftHandedModeSwapsControllerRoles()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);
            InputActionAsset actions = CreateTestActions();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();

            try
            {
                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                settings.DominantHand = BlockiverseControllerRole.Left;
                settings.ContinuousMoveSpeed = 2.0f;
                settings.SnapTurnDegrees = 45.0f;

                ConfigureXriLocomotionStack(
                    rigObject,
                    origin,
                    out XRBodyTransformer bodyTransformer,
                    out LocomotionMediator mediator,
                    out TeleportationProvider teleport,
                    out ContinuousMoveProvider continuousMove,
                    out SnapTurnProvider snapTurn);

                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);
                inputRig.ConfigureLocomotion(teleport, snapTurn, null, continuousMove, mediator, bodyTransformer, settings);
                // Sprint/crouch input is gated behind the menu router's world-input grant
                // since the M8.5 menu gate; simulate an active world session.
                BlockiverseRuntimeState.SetRouterState(isGamePaused: false, allowWorldInput: true);

                int breakPresses = 0;
                int placePresses = 0;
                int quickMenuPresses = 0;
                int blockTogglePresses = 0;
                inputRig.BreakPressed.AddListener(() => breakPresses++);
                inputRig.PlacePressed.AddListener(() => placePresses++);
                inputRig.QuickMenuPressed.AddListener(() => quickMenuPresses++);
                inputRig.BlockEditingTogglePressed.AddListener(() => blockTogglePresses++);

                yield return null;
                Assert.That(inputRig.ActiveMoveHand, Is.EqualTo(BlockiverseControllerRole.Right));
                Assert.That(inputRig.ActiveTurnHand, Is.EqualTo(BlockiverseControllerRole.Left));
                Assert.That(inputRig.ActiveToolHand, Is.EqualTo(BlockiverseControllerRole.Left));

                InputAction leftPrimary = actions
                    .FindActionMap(BlockiverseInputActionNames.LeftHandMap)
                    .FindAction(BlockiverseInputActionNames.PrimaryButton);
                Assert.That(inputRig.ResolveJumpActionForCurrentControls(), Is.SameAs(leftPrimary));
                Assert.That(inputRig.JumpProvider?.jumpInput.inputActionReferencePerformed?.action,
                    Is.SameAs(leftPrimary),
                    "JumpProvider should follow the dominant controller primary button.");

                Vector3 startPosition = origin.transform.position;
                Set(gamepad.rightStick, new Vector2(0.0f, 1.0f));
                yield return null;

                Assert.That((origin.transform.position - startPosition).magnitude, Is.GreaterThan(0.0f));

                Set(gamepad.rightStick, Vector2.zero);
                yield return null;

                // Sprint and crouch default to click-and-hold (independent control-style
                // settings landed with the parallel input fixes; toggle is opt-in via
                // SprintToggleEnabled/CrouchToggleEnabled). Active only while held.
                Press(gamepad.rightStickButton);
                yield return null;
                Assert.That(inputRig.SprintActive, Is.True);
                Release(gamepad.rightStickButton);
                yield return null;
                Assert.That(inputRig.SprintActive, Is.False,
                    "Sprint defaults to click-and-hold; releasing the support stick must end it.");

                Press(gamepad.selectButton);
                yield return null;
                Assert.That(inputRig.CrouchActive, Is.True);
                Release(gamepad.selectButton);
                yield return null;
                Assert.That(inputRig.CrouchActive, Is.False,
                    "Crouch defaults to click-and-hold; releasing the dominant secondary button must end it.");

                Set(gamepad.leftStick, Vector2.right);
                yield return null;
                yield return null;

                Assert.That(Mathf.DeltaAngle(origin.transform.eulerAngles.y, 45.0f), Is.EqualTo(0.0f).Within(0.1f));

                Set(gamepad.leftStick, Vector2.zero);
                yield return null;

                Press(gamepad.rightTrigger);
                yield return null;
                Assert.That(breakPresses, Is.EqualTo(0), "Support trigger should not break blocks.");
                Release(gamepad.rightTrigger);
                yield return null;

                Press(gamepad.leftTrigger);
                yield return null;
                Assert.That(breakPresses, Is.EqualTo(1));
                Release(gamepad.leftTrigger);
                yield return null;

                Press(gamepad.leftShoulder);
                yield return null;
                Assert.That(placePresses, Is.EqualTo(1));
                Release(gamepad.leftShoulder);
                yield return null;

                Press(gamepad.rightShoulder);
                yield return null;
                Assert.That(quickMenuPresses, Is.EqualTo(1));
                Release(gamepad.rightShoulder);
                yield return null;

                Press(gamepad.leftStickButton);
                yield return null;
                Assert.That(blockTogglePresses, Is.EqualTo(1));
                Release(gamepad.leftStickButton);
                yield return null;
            }
            finally
            {
                DestroyRigImmediate(rigObject);
                Object.DestroyImmediate(actions);
            }
        }

        [UnityTest]
        public IEnumerator RightHandedModeUsesSupportSprintAndDominantCrouch()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);
            InputActionAsset actions = CreateTestActions();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();

            try
            {
                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                settings.DominantHand = BlockiverseControllerRole.Right;

                ConfigureXriLocomotionStack(
                    rigObject,
                    origin,
                    out XRBodyTransformer bodyTransformer,
                    out LocomotionMediator mediator,
                    out TeleportationProvider teleport,
                    out ContinuousMoveProvider continuousMove,
                    out SnapTurnProvider snapTurn);

                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);
                inputRig.ConfigureLocomotion(teleport, snapTurn, null, continuousMove, mediator, bodyTransformer, settings);
                // Sprint/crouch input is gated behind the menu router's world-input grant
                // since the M8.5 menu gate; simulate an active world session.
                BlockiverseRuntimeState.SetRouterState(isGamePaused: false, allowWorldInput: true);

                yield return null;

                Press(gamepad.leftStickButton);
                yield return null;
                Assert.That(inputRig.SprintActive, Is.True);
                Assert.That(inputRig.CrouchActive, Is.False,
                    "Sprint and crouch are on different physical controls now -- the support stick " +
                    "click must never crouch.");
                Release(gamepad.leftStickButton);
                yield return null;

                Press(gamepad.buttonEast);
                yield return null;
                Assert.That(inputRig.CrouchActive, Is.True);
                Release(gamepad.buttonEast);
                yield return null;
                Assert.That(inputRig.CrouchActive, Is.False,
                    "Crouch defaults to click-and-hold; releasing the dominant secondary button must end it.");
            }
            finally
            {
                DestroyRigImmediate(rigObject);
                Object.DestroyImmediate(actions);
            }
        }

        [UnityTest]
        public IEnumerator DominantHandOwnsTheOnlyActiveInteractionRay()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);
            InputActionAsset actions = CreateTestActions();

            try
            {
                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                settings.DominantHand = BlockiverseControllerRole.Right;

                ConfigureXriLocomotionStack(
                    rigObject,
                    origin,
                    out XRBodyTransformer bodyTransformer,
                    out LocomotionMediator mediator,
                    out TeleportationProvider teleport,
                    out ContinuousMoveProvider continuousMove,
                    out SnapTurnProvider snapTurn);

                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);
                inputRig.ConfigureLocomotion(teleport, snapTurn, null, continuousMove, mediator, bodyTransformer, settings);

                BlockiverseLocomotionRayMediator leftMediator = CreateRayMediator(
                    rigObject,
                    "Left Controller",
                    BlockiverseControllerRole.Left,
                    inputRig,
                    settings,
                    out GameObject leftInteractionObject,
                    out _);
                BlockiverseLocomotionRayMediator rightMediator = CreateRayMediator(
                    rigObject,
                    "Right Controller",
                    BlockiverseControllerRole.Right,
                    inputRig,
                    settings,
                    out GameObject rightInteractionObject,
                    out _);

                yield return null;

                InvokePrivate(leftMediator, "Update");
                InvokePrivate(rightMediator, "Update");

                Assert.That(leftInteractionObject.activeSelf, Is.False);
                Assert.That(rightInteractionObject.activeSelf, Is.True);

                settings.DominantHand = BlockiverseControllerRole.Left;
                yield return null;

                InvokePrivate(leftMediator, "Update");
                InvokePrivate(rightMediator, "Update");

                Assert.That(leftInteractionObject.activeSelf, Is.True);
                Assert.That(rightInteractionObject.activeSelf, Is.False);
            }
            finally
            {
                DestroyRigImmediate(rigObject);
                Object.DestroyImmediate(actions);
            }
        }

        [UnityTest]
        public IEnumerator ConfiguredHeadPoseDriverAppliesTrackedHmdPoseDuringBeforeRenderInputUpdate()
        {
            GameObject cameraObject = new("Head Camera");

            InputSystem.RegisterLayout(@"
            {
                ""name"": ""BlockiverseBeforeRenderHMD"",
                ""extend"": ""XRHMD"",
                ""beforeRender"": ""Update""
            }");
            var hmd = (XRHMD)InputSystem.AddDevice("BlockiverseBeforeRenderHMD");
            TrackedPoseDriver poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
            BlockiverseInputRig.ConfigureHeadPoseDriverActions(poseDriver);
            Vector3 trackedPosition = new(0.2f, 1.62f, 0.35f);
            Quaternion trackedRotation = Quaternion.Euler(6.0f, 42.0f, 0.0f);

            Assert.That(poseDriver.enabled, Is.True);
            Assert.That(poseDriver.updateType, Is.EqualTo(TrackedPoseDriver.UpdateType.UpdateAndBeforeRender));
            Assert.That(hmd.updateBeforeRender, Is.True);

            Set(hmd.trackingState, 3, queueEventOnly: true);
            Set(hmd.centerEyePosition, trackedPosition, queueEventOnly: true);
            Set(hmd.centerEyeRotation, trackedRotation, queueEventOnly: true);

            RunInputSystemUpdate(InputUpdateType.BeforeRender);

            Assert.That(Vector3.Distance(cameraObject.transform.localPosition, trackedPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Dot(cameraObject.transform.localRotation, trackedRotation), Is.GreaterThan(0.9999f));

            InputSystem.RemoveDevice(hmd);
            poseDriver.enabled = false;
            Object.DestroyImmediate(cameraObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ControllerPoseDriverAppliesTrackedPose()
        {
            GameObject controllerObject = new("Left Controller");
            controllerObject.SetActive(false);
            XRController controller = InputSystem.AddDevice<XRController>();

            try
            {
                InputSystem.SetDeviceUsage(controller, CommonUsages.LeftHand);

                // Native controller tracking: the TrackedPoseDriver configured by the rig drives
                // the controller transform from the XRController device pose. Configure while the
                // object is inactive so OnEnable binds and enables the pose actions in one pass.
                TrackedPoseDriver poseDriver = controllerObject.AddComponent<TrackedPoseDriver>();
                BlockiverseInputRig.ConfigureControllerPoseDriverActions(poseDriver, BlockiverseControllerRole.Left);

                BlockiverseControllerAnchor anchor = controllerObject.AddComponent<BlockiverseControllerAnchor>();
                anchor.Configure(BlockiverseControllerRole.Left, poseDriver);

                controllerObject.SetActive(true);

                Vector3 trackedPosition = new(0.25f, 1.1f, 0.4f);
                Quaternion trackedRotation = Quaternion.Euler(10.0f, 20.0f, 30.0f);

                // TrackedPoseDriver respects tracking state (ignoreTrackingState = false), so the
                // device must report Position|Rotation tracked (3) for the pose to be applied.
                Press(controller.isTracked);
                Set(controller.trackingState, 3);
                Set(controller.devicePosition, trackedPosition);
                Set(controller.deviceRotation, trackedRotation);
                yield return null;
                yield return null;

                Assert.That(anchor.Role, Is.EqualTo(BlockiverseControllerRole.Left));
                Assert.That(anchor.IsTracked, Is.True);
                Assert.That(Vector3.Distance(controllerObject.transform.localPosition, trackedPosition), Is.LessThan(0.001f));
                Assert.That(Quaternion.Dot(controllerObject.transform.localRotation, trackedRotation), Is.GreaterThan(0.999f));
            }
            finally
            {
                if (controller != null)
                InputSystem.RemoveDevice(controller);

                Object.DestroyImmediate(controllerObject);
            }
        }

        [UnityTest]
        public IEnumerator InteractionRayStaysHiddenUntilControllerHasPositionAndRotationTracking()
        {
            GameObject controllerObject = new("Right Controller");
            controllerObject.SetActive(false);
            XRController controller = InputSystem.AddDevice<XRController>();

            try
            {
                InputSystem.SetDeviceUsage(controller, CommonUsages.RightHand);

                TrackedPoseDriver poseDriver = controllerObject.AddComponent<TrackedPoseDriver>();
                BlockiverseInputRig.ConfigureControllerPoseDriverActions(poseDriver, BlockiverseControllerRole.Right);

                BlockiverseControllerAnchor anchor = controllerObject.AddComponent<BlockiverseControllerAnchor>();
                anchor.Configure(BlockiverseControllerRole.Right, poseDriver);

                GameObject interactionObject = new("Interaction Ray");
                GameObject teleportObject = new("Teleport Ray");
                interactionObject.transform.SetParent(controllerObject.transform, false);
                teleportObject.transform.SetParent(controllerObject.transform, false);

                XRRayInteractor interactionRay = interactionObject.AddComponent<XRRayInteractor>();
                XRRayInteractor teleportRay = teleportObject.AddComponent<XRRayInteractor>();
                controllerObject.SetActive(true);

                BlockiverseLocomotionRayMediator mediator = controllerObject.AddComponent<BlockiverseLocomotionRayMediator>();
                mediator.Configure(
                    rig: null,
                    settings: null,
                    interaction: interactionRay,
                    teleport: teleportRay,
                    controllerRole: BlockiverseControllerRole.Right);

                Press(controller.isTracked);
                Set(controller.trackingState, (int)InputTrackingState.Rotation);
                Set(controller.deviceRotation, Quaternion.Euler(10.0f, 20.0f, 30.0f));
                yield return null;

                Assert.That(anchor.IsTracked, Is.False,
                    "Rotation-only tracking should not count as a usable pointer pose because the ray origin position is stale.");
                Assert.That(interactionObject.activeSelf, Is.False,
                    "The interaction ray should stay hidden while the controller has no tracked position.");

                Set(controller.trackingState, (int)(InputTrackingState.Position | InputTrackingState.Rotation));
                Set(controller.devicePosition, new Vector3(0.25f, 1.1f, 0.4f));
                Set(controller.deviceRotation, Quaternion.Euler(10.0f, 20.0f, 30.0f));
                yield return null;

                Assert.That(anchor.IsTracked, Is.True);
                Assert.That(interactionObject.activeSelf, Is.True);
            }
            finally
            {
                if (controller != null)
                    InputSystem.RemoveDevice(controller);

                Object.DestroyImmediate(controllerObject);
            }
        }

        [UnityTest]
        public IEnumerator SmoothTurnComfortTogglesBetweenSnapAndContinuousTurn()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);
            InputActionAsset actions = CreateTestActions();

            try
            {
                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();

                ConfigureXriLocomotionStack(
                    rigObject,
                    origin,
                    out XRBodyTransformer bodyTransformer,
                    out LocomotionMediator mediator,
                    out TeleportationProvider teleport,
                    out ContinuousMoveProvider continuousMove,
                    out SnapTurnProvider snapTurn);

                ContinuousTurnProvider continuousTurn = rigObject.AddComponent<ContinuousTurnProvider>();
                continuousTurn.mediator = mediator;

                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);
                inputRig.ConfigureLocomotion(teleport, snapTurn, null, continuousMove, mediator, bodyTransformer, settings, continuousTurn);

                settings.SmoothTurnEnabled = false;
                yield return null;
                Assert.That(snapTurn.enabled, Is.True);
                Assert.That(continuousTurn.enabled, Is.False);

                settings.SmoothTurnEnabled = true;
                yield return null;
                Assert.That(snapTurn.enabled, Is.False);
                Assert.That(continuousTurn.enabled, Is.True);
            }
            finally
            {
                DestroyRigImmediate(rigObject);
                Object.DestroyImmediate(actions);
            }
        }

        [UnityTest]
        public IEnumerator TeleportRayRemainsActiveForReleaseFrame()
        {
            GameObject controllerObject = new("Teleport Mediator");
            GameObject interactionObject = new("Interaction Ray");
            GameObject teleportObject = new("Teleport Ray");

            try
            {
                interactionObject.transform.SetParent(controllerObject.transform, false);
                teleportObject.transform.SetParent(controllerObject.transform, false);
                XRRayInteractor interactionRay = interactionObject.AddComponent<XRRayInteractor>();
                XRRayInteractor teleportRay = teleportObject.AddComponent<XRRayInteractor>();
                BlockiverseLocomotionRayMediator mediator = controllerObject.AddComponent<BlockiverseLocomotionRayMediator>();

                mediator.Configure(
                    rig: null,
                    settings: null,
                    interaction: interactionRay,
                    teleport: teleportRay,
                    controllerRole: BlockiverseControllerRole.Right);

                InvokePrivate(mediator, "SetTeleportActive", true);

                Assert.That(teleportObject.activeSelf, Is.True);
                Assert.That(interactionObject.activeSelf, Is.False);

                InvokePrivate(mediator, "SetTeleportActive", false);

                Assert.That(teleportObject.activeSelf, Is.True,
                    "Thumbstick release must leave the teleport interactor alive for one frame so XRI can process select exit.");
                Assert.That(interactionObject.activeSelf, Is.False,
                    "The regular interaction ray should not re-enable until the teleport release frame has drained.");

                yield return null;

                Assert.That(teleportObject.activeSelf, Is.False);
                Assert.That(interactionObject.activeSelf, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [UnityTest]
        public IEnumerator TeleportRayStaysHiddenWhileWorldInputIsSuppressed()
        {
            GameObject rigObject = new("Menu-Suppressed Teleport Rig");
            GameObject controllerObject = new("Right Controller");
            GameObject interactionObject = new("Interaction Ray");
            GameObject teleportObject = new("Teleport Ray");
            InputActionAsset actions = CreateTestActions();
            InputAction teleportModeAction = new("Held Teleport Mode", InputActionType.Button, "<Gamepad>/leftShoulder");
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();

            try
            {
                controllerObject.transform.SetParent(rigObject.transform, false);
                interactionObject.transform.SetParent(controllerObject.transform, false);
                teleportObject.transform.SetParent(controllerObject.transform, false);

                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                settings.LocomotionMode = BlockiverseLocomotionMode.Teleport;

                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);

                XRRayInteractor interactionRay = interactionObject.AddComponent<XRRayInteractor>();
                XRRayInteractor teleportRay = teleportObject.AddComponent<XRRayInteractor>();
                teleportObject.SetActive(false);
                BlockiverseLocomotionRayMediator mediator = controllerObject.AddComponent<BlockiverseLocomotionRayMediator>();
                mediator.Configure(
                    inputRig,
                    settings,
                    interactionRay,
                    teleportRay,
                    BlockiverseControllerRole.Right);
                SetPrivateField(mediator, "teleportModeAction", teleportModeAction);

                actions.Enable();
                teleportModeAction.Enable();
                BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false);

                Press(gamepad.leftShoulder);
                InputSystem.Update();
                Assert.That(teleportModeAction.IsPressed(), Is.True,
                    "The test must hold teleport mode so a hidden teleport ray proves menu suppression, not missing input.");
                InvokePrivate(mediator, "Update");

                Assert.That(teleportObject.activeSelf, Is.False,
                    "The teleport arc should not appear while title/menu routing suppresses world input.");
                Assert.That(interactionObject.activeSelf, Is.True,
                    "The UI interaction ray should remain available for menu selection.");

                BlockiverseRuntimeState.SetRouterState(isGamePaused: false, allowWorldInput: true);
                InvokePrivate(mediator, "Update");

                Assert.That(teleportObject.activeSelf, Is.True,
                    "Teleport mode should resume once world input is enabled.");
                Assert.That(interactionObject.activeSelf, Is.False);

                yield return null;
            }
            finally
            {
                BlockiverseRuntimeState.Reset();
                Release(gamepad.leftShoulder);
                InputSystem.RemoveDevice(gamepad);
                teleportModeAction.Dispose();
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(actions);
            }
        }

        static void ConfigureXriLocomotionStack(
            GameObject rigObject,
            XROrigin origin,
            out XRBodyTransformer bodyTransformer,
            out LocomotionMediator mediator,
            out TeleportationProvider teleport,
            out ContinuousMoveProvider continuousMove,
            out SnapTurnProvider snapTurn)
        {
            CharacterController characterController = rigObject.GetComponent<CharacterController>();

            if (characterController == null)
                characterController = rigObject.AddComponent<CharacterController>();

            BlockiverseInputRig.ConfigureCharacterController(characterController);

            bodyTransformer = rigObject.AddComponent<XRBodyTransformer>();
            bodyTransformer.xrOrigin = origin;

            mediator = rigObject.AddComponent<LocomotionMediator>();
            mediator.xrOrigin = origin;

            teleport = rigObject.AddComponent<TeleportationProvider>();
            teleport.mediator = mediator;
            teleport.delayTime = 0.0f;

            continuousMove = rigObject.AddComponent<ContinuousMoveProvider>();
            continuousMove.mediator = mediator;
            continuousMove.forwardSource = origin.Camera.transform;
            continuousMove.enableStrafe = true;
            continuousMove.enableFly = false;

            snapTurn = rigObject.AddComponent<SnapTurnProvider>();
            snapTurn.mediator = mediator;
            snapTurn.enableTurnLeftRight = true;
            snapTurn.enableTurnAround = true;
            snapTurn.delayTime = 0.0f;
        }

        static GameObject CreateXrOrigin(out XROrigin origin)
        {
            GameObject rigObject = new("Test Action XR Origin");
            rigObject.SetActive(false);

            GameObject cameraOffset = new("Camera Offset");
            cameraOffset.transform.SetParent(rigObject.transform, false);

            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();

            origin = rigObject.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.Camera = camera;
            rigObject.SetActive(true);

            return rigObject;
        }

        static BlockiverseLocomotionRayMediator CreateRayMediator(
            GameObject rigObject,
            string controllerName,
            BlockiverseControllerRole hand,
            BlockiverseInputRig inputRig,
            BlockiverseComfortSettings settings,
            out GameObject interactionObject,
            out GameObject teleportObject)
        {
            GameObject controllerObject = new(controllerName);
            controllerObject.transform.SetParent(rigObject.transform, false);

            interactionObject = new("Interaction Ray");
            interactionObject.transform.SetParent(controllerObject.transform, false);
            XRRayInteractor interactionRay = interactionObject.AddComponent<XRRayInteractor>();

            teleportObject = new("Teleport Ray");
            teleportObject.transform.SetParent(controllerObject.transform, false);
            XRRayInteractor teleportRay = teleportObject.AddComponent<XRRayInteractor>();

            BlockiverseLocomotionRayMediator mediator = controllerObject.AddComponent<BlockiverseLocomotionRayMediator>();
            mediator.Configure(inputRig, settings, interactionRay, teleportRay, hand);
            return mediator;
        }

        static InputActionAsset CreateTestActions()
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();

            InputActionMap leftHand = actions.AddActionMap(BlockiverseInputActionNames.LeftHandMap);
            leftHand.AddAction(
                BlockiverseInputActionNames.Move,
                InputActionType.PassThrough,
                "<Gamepad>/leftStick",
                expectedControlLayout: "Vector2");
            leftHand.AddAction(
                BlockiverseInputActionNames.Turn,
                InputActionType.PassThrough,
                "<Gamepad>/leftStick",
                expectedControlLayout: "Vector2");
            leftHand.AddAction(BlockiverseInputActionNames.Select, InputActionType.Button, "<Gamepad>/leftTrigger");
            leftHand.AddAction(BlockiverseInputActionNames.Activate, InputActionType.Button, "<Gamepad>/leftShoulder");
            leftHand.AddAction(BlockiverseInputActionNames.PrimaryButton, InputActionType.Button, "<Gamepad>/buttonWest");
            leftHand.AddAction(BlockiverseInputActionNames.SecondaryButton, InputActionType.Button, "<Gamepad>/select");
            leftHand.AddAction(BlockiverseInputActionNames.Sprint, InputActionType.Button, "<Gamepad>/leftStickPress");
            leftHand.AddAction(BlockiverseInputActionNames.Crouch, InputActionType.Button, "<Gamepad>/select");
            leftHand.AddAction(BlockiverseInputActionNames.BlockEditingToggle, InputActionType.Button, "<Gamepad>/leftStickPress");
            leftHand.AddAction(BlockiverseInputActionNames.TeleportMode, InputActionType.Button, "<Gamepad>/leftShoulder");
            leftHand.AddAction(BlockiverseInputActionNames.TeleportSelect, InputActionType.Button, "<Gamepad>/buttonSouth");

            InputActionMap rightHand = actions.AddActionMap(BlockiverseInputActionNames.RightHandMap);
            rightHand.AddAction(
                BlockiverseInputActionNames.Move,
                InputActionType.PassThrough,
                "<Gamepad>/rightStick",
                expectedControlLayout: "Vector2");
            rightHand.AddAction(
                BlockiverseInputActionNames.Turn,
                InputActionType.PassThrough,
                "<Gamepad>/rightStick",
                expectedControlLayout: "Vector2");
            rightHand.AddAction(BlockiverseInputActionNames.Select, InputActionType.Button, "<Gamepad>/rightTrigger");
            rightHand.AddAction(BlockiverseInputActionNames.Activate, InputActionType.Button, "<Gamepad>/rightShoulder");
            rightHand.AddAction(BlockiverseInputActionNames.PrimaryButton, InputActionType.Button, "<Gamepad>/buttonNorth");
            rightHand.AddAction(BlockiverseInputActionNames.SecondaryButton, InputActionType.Button, "<Gamepad>/buttonEast");
            rightHand.AddAction(BlockiverseInputActionNames.Sprint, InputActionType.Button, "<Gamepad>/rightStickPress");
            rightHand.AddAction(BlockiverseInputActionNames.Crouch, InputActionType.Button, "<Gamepad>/buttonEast");
            rightHand.AddAction(BlockiverseInputActionNames.BlockEditingToggle, InputActionType.Button, "<Gamepad>/rightStickPress");
            rightHand.AddAction(BlockiverseInputActionNames.TeleportMode, InputActionType.Button, "<Gamepad>/leftShoulder");
            rightHand.AddAction(BlockiverseInputActionNames.TeleportSelect, InputActionType.Button, "<Gamepad>/buttonSouth");

            InputActionMap gameplay = actions.AddActionMap(BlockiverseInputActionNames.GameplayMap);
            gameplay.AddAction(BlockiverseInputActionNames.Menu, InputActionType.Button, "<Gamepad>/start");

            return actions;
        }

        static void DestroyRigImmediate(GameObject rigObject)
        {
            if (rigObject == null)
                return;

            foreach (TrackedPoseDriver driver in rigObject.GetComponentsInChildren<TrackedPoseDriver>(true))
                driver.enabled = false;

            Object.DestroyImmediate(rigObject);
        }

        [UnityTearDown]
        public IEnumerator CleanupTrackedPoseDriversAfterTest()
        {
            yield return BlockiversePlayModeSceneTestUtility.CleanupTrackedPoseDrivers();
        }

        static void RunInputSystemUpdate(InputUpdateType updateType)
        {
            MethodInfo updateMethod = typeof(InputSystem).GetMethod(
                "Update",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(InputUpdateType) },
                null);

            Assert.That(updateMethod, Is.Not.Null);
            updateMethod.Invoke(null, new object[] { updateType });
        }

        static object InvokePrivate(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"{methodName} should exist.");
            return method.Invoke(target, args);
        }

        static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{fieldName} should exist.");
            field.SetValue(target, value);
        }
    }
}
