using System.Collections;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class BlockiverseLocomotionPlayModeTests
    {
        [Test]
        public void TeleportMovesCameraToRequestedWorldPosition()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);

            try
            {
                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                var teleport = rigObject.AddComponent<BlockiverseTeleportLocomotion>();
                teleport.Configure(origin, settings);

                Assert.That(teleport.TryTeleportTo(new Vector3(2.0f, 0.0f, 3.0f)), Is.True);
                Assert.That(Vector3.Distance(origin.Camera.transform.position, new Vector3(2.0f, 0.0f, 3.0f)), Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
            }
        }

        [Test]
        public void TeleportMovesOffsetCameraToRequestedWorldPosition()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);

            try
            {
                origin.Camera.transform.localPosition = new Vector3(0.75f, 0.0f, -0.25f);

                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                var teleport = rigObject.AddComponent<BlockiverseTeleportLocomotion>();
                teleport.Configure(origin, settings);

                Vector3 target = new(2.0f, 0.0f, 3.0f);

                Assert.That(teleport.TryTeleportTo(target), Is.True);
                Assert.That(Vector3.Distance(origin.Camera.transform.position, target), Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
            }
        }

        [Test]
        public void SnapTurnRotatesXrOriginByConfiguredDegrees()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);

            try
            {
                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                var snapTurn = rigObject.AddComponent<BlockiverseSnapTurnLocomotion>();
                snapTurn.Configure(origin, settings);

                snapTurn.ApplySnapTurn(1);
                Assert.That(Mathf.DeltaAngle(origin.transform.eulerAngles.y, 45.0f), Is.EqualTo(0.0f).Within(0.1f));
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
            }
        }

        [Test]
        public void SnapTurnRotatesAroundOffsetCameraPosition()
        {
            GameObject rigObject = CreateXrOrigin(out XROrigin origin);

            try
            {
                origin.Camera.transform.localPosition = new Vector3(0.5f, 0.0f, 1.0f);
                Vector3 cameraPosition = origin.Camera.transform.position;

                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                settings.SnapTurnDegrees = 60.0f;

                var snapTurn = rigObject.AddComponent<BlockiverseSnapTurnLocomotion>();
                snapTurn.Configure(origin, settings);

                snapTurn.ApplySnapTurn(1);

                Assert.That(Vector3.Distance(origin.Camera.transform.position, cameraPosition), Is.LessThan(0.01f));
                Assert.That(Mathf.DeltaAngle(origin.Camera.transform.eulerAngles.y, 60.0f), Is.EqualTo(0.0f).Within(0.1f));
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
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
                Object.DestroyImmediate(rigObject);
            }
        }

        [UnityTest]
        public IEnumerator BootSceneContainsComfortSettingsMenu()
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync("Boot", LoadSceneMode.Single);

            while (!operation.isDone)
                yield return null;

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
            cameraObject.AddComponent<TrackedPoseDriver>();

            origin = rigObject.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.Camera = camera;
            rigObject.SetActive(true);

            return rigObject;
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

                var teleport = rigObject.AddComponent<BlockiverseTeleportLocomotion>();
                teleport.Configure(origin, settings);

                var snapTurn = rigObject.AddComponent<BlockiverseSnapTurnLocomotion>();
                snapTurn.Configure(origin, settings);

                var heightReset = rigObject.AddComponent<BlockiverseHeightReset>();
                heightReset.Configure(origin, settings);

                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);
                inputRig.ConfigureLocomotion(teleport, snapTurn, heightReset);

                var canvas = menuObject.AddComponent<Canvas>();
                var menu = menuObject.AddComponent<BlockiverseComfortMenu>();
                menu.Configure(canvas, settings);
                inputRig.MenuPressed.AddListener(menu.ToggleVisible);

                Set(gamepad.rightStick, new Vector2(0.50f, 0.0f));
                yield return null;
                Assert.That(Mathf.DeltaAngle(origin.transform.eulerAngles.y, 0.0f), Is.EqualTo(0.0f).Within(0.1f));

                Set(gamepad.rightStick, new Vector2(0.80f, 0.0f));
                yield return null;
                Assert.That(Mathf.DeltaAngle(origin.transform.eulerAngles.y, 45.0f), Is.EqualTo(0.0f).Within(0.1f));

                yield return null;
                Assert.That(Mathf.DeltaAngle(origin.transform.eulerAngles.y, 45.0f), Is.EqualTo(0.0f).Within(0.1f));

                Set(gamepad.rightStick, Vector2.zero);
                yield return null;
                Set(gamepad.rightStick, new Vector2(-0.80f, 0.0f));
                yield return null;
                Assert.That(Mathf.DeltaAngle(origin.transform.eulerAngles.y, 0.0f), Is.EqualTo(0.0f).Within(0.1f));

                Press(gamepad.leftShoulder);
                yield return null;
                Assert.That(Vector3.Distance(origin.transform.position, Vector3.zero), Is.LessThan(0.01f));

                Press(gamepad.buttonSouth);
                yield return null;
                Assert.That(Vector3.Distance(origin.transform.position, new Vector3(0.0f, 0.0f, 2.0f)), Is.LessThan(0.01f));

                yield return null;
                Assert.That(Vector3.Distance(origin.transform.position, new Vector3(0.0f, 0.0f, 2.0f)), Is.LessThan(0.01f));

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
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(actions);
            }
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
            cameraObject.AddComponent<TrackedPoseDriver>();

            origin = rigObject.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.Camera = camera;
            rigObject.SetActive(true);

            return rigObject;
        }

        static InputActionAsset CreateTestActions()
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();

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
    }
}
