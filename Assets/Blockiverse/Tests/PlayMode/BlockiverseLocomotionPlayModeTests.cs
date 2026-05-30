using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
                continuousMove.leftHandMoveInput = CreateManualVector2Reader("Left Hand Move", new Vector2(0.0f, 1.0f));

                yield return null;

                Assert.That(origin.transform.position.x, Is.GreaterThan(0.0f));
                Assert.That(Mathf.Abs(origin.transform.position.z), Is.LessThan(0.01f));
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
                settings.ContinuousMoveEnabled = false;
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
                continuousMove.leftHandMoveInput = CreateManualVector2Reader("Left Hand Move", new Vector2(0.0f, 1.0f));

                yield return null;

                Assert.That(continuousMove.enabled, Is.False);
                Assert.That(origin.transform.position, Is.EqualTo(Vector3.zero));
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
                Assert.That(origin.CameraYOffset, Is.EqualTo(1.6f).Within(0.01f));
            }
            finally
            {
                DestroyRigImmediate(rigObject);
            }
        }

        [UnityTest]
        public IEnumerator BootSceneContainsComfortSettingsMenu()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle("Boot");

            BlockiverseComfortMenu menu = Object.FindFirstObjectByType<BlockiverseComfortMenu>(FindObjectsInactive.Include);
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.IsVisible, Is.False);

            menu.Show();
            Assert.That(menu.IsVisible, Is.True);

            menu.Hide();
            Assert.That(menu.IsVisible, Is.False);
        }

        [Test]
        public void ComfortMenuRegistersCallbacksWhenControlsAreConfiguredAfterAwake()
        {
            var settingsObject = new GameObject("Comfort Settings");
            var menuObject = new GameObject("Comfort Menu");
            var teleportObject = new GameObject("Teleport Toggle");
            var smoothTurnObject = new GameObject("Smooth Turn Toggle");
            var snapTurnObject = new GameObject("Snap Turn Slider");

            try
            {
                var settings = settingsObject.AddComponent<BlockiverseComfortSettings>();
                var menu = menuObject.AddComponent<BlockiverseComfortMenu>();
                var canvas = menuObject.AddComponent<Canvas>();
                var teleportToggle = teleportObject.AddComponent<Toggle>();
                var smoothTurnToggle = smoothTurnObject.AddComponent<Toggle>();
                var snapTurnSlider = snapTurnObject.AddComponent<Slider>();

                teleportToggle.isOn = true;
                smoothTurnToggle.isOn = false;
                snapTurnSlider.minValue = 15.0f;
                snapTurnSlider.maxValue = 90.0f;
                snapTurnSlider.value = 45.0f;

                menu.Configure(canvas, settings);
                menu.ConfigureControls(teleportToggle, smoothTurnToggle, snapTurnSlider);

                teleportToggle.isOn = false;
                smoothTurnToggle.isOn = true;
                snapTurnSlider.value = 60.0f;

                Assert.That(settings.TeleportEnabled, Is.False);
                Assert.That(settings.SmoothTurnEnabled, Is.True);
                Assert.That(settings.SnapTurnDegrees, Is.EqualTo(60.0f).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(snapTurnObject);
                Object.DestroyImmediate(smoothTurnObject);
                Object.DestroyImmediate(teleportObject);
                Object.DestroyImmediate(menuObject);
                Object.DestroyImmediate(settingsObject);
            }
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
            snapTurn.enableTurnAround = false;
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
        [UnityTest]
        public IEnumerator ConfiguredInputActionsDriveLocomotionAndComfortMenu()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);
            GameObject menuObject = new("Comfort Menu");
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

                var canvas = menuObject.AddComponent<Canvas>();
                var menu = menuObject.AddComponent<BlockiverseComfortMenu>();
                menu.Configure(canvas, settings);
                inputRig.MenuPressed.AddListener(menu.ToggleVisible);

                Set(gamepad.rightStick, Vector2.right);
                yield return null;
                yield return null;
                Assert.That(Mathf.DeltaAngle(origin.transform.eulerAngles.y, 45.0f), Is.EqualTo(0.0f).Within(0.1f));

                Set(gamepad.rightStick, Vector2.zero);
                yield return null;

                // Teleport is now native target-based (held Teleport Mode enables the teleport ray,
                // which selects a TeleportationArea). Holding the mode alone must not move the rig.
                Press(gamepad.leftShoulder);
                yield return null;
                Assert.That(Vector3.Distance(origin.transform.position, Vector3.zero), Is.LessThan(0.01f));
                Release(gamepad.leftShoulder);
                yield return null;

                origin.CameraYOffset = 1.2f;
                Press(gamepad.selectButton);
                yield return null;
                Assert.That(origin.CameraYOffset, Is.EqualTo(settings.StandingEyeHeight).Within(0.01f));

                origin.CameraYOffset = 1.1f;
                yield return null;
                Assert.That(origin.CameraYOffset, Is.EqualTo(1.1f).Within(0.01f));

                Assert.That(menu.IsVisible, Is.False);
                Press(gamepad.startButton);
                yield return null;
                Assert.That(menu.IsVisible, Is.True);

                yield return null;
                Assert.That(menu.IsVisible, Is.True);

                Release(gamepad.startButton);
                yield return null;
                Press(gamepad.startButton);
                yield return null;
                Assert.That(menu.IsVisible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(menuObject);
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

                Set(gamepad.leftStick, new Vector2(0.0f, 1.0f));
                yield return null;

                Assert.That(origin.transform.position.z, Is.GreaterThan(0.0f));

                Set(gamepad.leftStick, Vector2.zero);
                yield return null;
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

        static void ConfigureXriLocomotionStack(
            GameObject rigObject,
            XROrigin origin,
            out XRBodyTransformer bodyTransformer,
            out LocomotionMediator mediator,
            out TeleportationProvider teleport,
            out ContinuousMoveProvider continuousMove,
            out SnapTurnProvider snapTurn)
        {
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
            snapTurn.enableTurnAround = false;
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

        static InputActionAsset CreateTestActions()
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();

            InputActionMap leftHand = actions.AddActionMap(BlockiverseInputActionNames.LeftHandMap);
            leftHand.AddAction(
                BlockiverseInputActionNames.Move,
                InputActionType.PassThrough,
                "<Gamepad>/leftStick",
                expectedControlLayout: "Vector2");

            InputActionMap rightHand = actions.AddActionMap(BlockiverseInputActionNames.RightHandMap);
            rightHand.AddAction(
                BlockiverseInputActionNames.Turn,
                InputActionType.PassThrough,
                "<Gamepad>/rightStick",
                expectedControlLayout: "Vector2");
            rightHand.AddAction(BlockiverseInputActionNames.TeleportMode, InputActionType.Button, "<Gamepad>/leftShoulder");
            rightHand.AddAction(BlockiverseInputActionNames.TeleportSelect, InputActionType.Button, "<Gamepad>/buttonSouth");

            InputActionMap gameplay = actions.AddActionMap(BlockiverseInputActionNames.GameplayMap);
            gameplay.AddAction(BlockiverseInputActionNames.HeightReset, InputActionType.Button, "<Gamepad>/select");
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
    }
}
