#pragma warning disable 0618
using System.Collections;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class BlockiverseInteractionPlayModeTests : InputTestFixture
    {
        static readonly MethodInfo CreativeInputBridgeUpdateMethod =
            typeof(BlockiverseCreativeInputBridge).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

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
        public IEnumerator BlockEditingToggleHidesAndRestoresTheInteractionRayVisual()
        {
            var bridgeObject = new GameObject("Creative Input Bridge");
            var rayObject = new GameObject("Interaction Ray");
            var controllerObject = new GameObject("Creative Controller");

            try
            {
                rayObject.transform.SetParent(bridgeObject.transform);
                XRRayInteractor ray = rayObject.AddComponent<XRRayInteractor>();
                LineRenderer lineRenderer = rayObject.AddComponent<LineRenderer>();
                XRInteractorLineVisual lineVisual = rayObject.AddComponent<XRInteractorLineVisual>();
                CreativeInteractionController controller = controllerObject.AddComponent<CreativeInteractionController>();
                BlockiverseInputRig rig = bridgeObject.AddComponent<BlockiverseInputRig>();
                BlockiverseCreativeInputBridge bridge = bridgeObject.AddComponent<BlockiverseCreativeInputBridge>();

                lineRenderer.enabled = true;
                lineVisual.enabled = true;
                bridge.Configure(rig, ray, controller);

                BlockiverseRuntimeState.SetRouterState(isGamePaused: false, allowWorldInput: true);
                controller.SetBlockEditingEnabled(false);
                CreativeInputBridgeUpdateMethod.Invoke(bridge, null);

                Assert.That(lineRenderer.enabled, Is.False);
                Assert.That(lineVisual.enabled, Is.False);

                controller.SetBlockEditingEnabled(true);
                CreativeInputBridgeUpdateMethod.Invoke(bridge, null);

                Assert.That(lineRenderer.enabled, Is.True);
                Assert.That(lineVisual.enabled, Is.True);

                yield return null;
            }
            finally
            {
                Object.DestroyImmediate(bridgeObject);
                Object.DestroyImmediate(controllerObject);
            }
        }

        [UnityTest]
        public IEnumerator MenuModeKeepsInteractionRayVisualAtDefaultLengthWhenUiIsMissed()
        {
            var bridgeObject = new GameObject("Creative Input Bridge");
            var rayObject = new GameObject("Interaction Ray");

            try
            {
                rayObject.transform.SetParent(bridgeObject.transform);
                XRRayInteractor ray = rayObject.AddComponent<XRRayInteractor>();
                LineRenderer lineRenderer = rayObject.AddComponent<LineRenderer>();
                XRInteractorLineVisual lineVisual = rayObject.AddComponent<XRInteractorLineVisual>();
                BlockiverseInputRig rig = bridgeObject.AddComponent<BlockiverseInputRig>();
                BlockiverseCreativeInputBridge bridge = bridgeObject.AddComponent<BlockiverseCreativeInputBridge>();

                lineRenderer.enabled = true;
                lineVisual.enabled = true;
                lineVisual.overrideInteractorLineLength = false;
                lineVisual.lineLength = 10.0f;

                BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false);
                bridge.Configure(rig, ray, null);
                CreativeInputBridgeUpdateMethod.Invoke(bridge, null);
                yield return null;

                Assert.That(lineRenderer.enabled, Is.True);
                Assert.That(lineVisual.enabled, Is.True);
                Assert.That(lineVisual.overrideInteractorLineLength, Is.False,
                    "A menu-mode ray with no UI hit should keep the normal XRI line length; shortening it hides ray/menu alignment defects.");
                Assert.That(lineVisual.lineLength, Is.EqualTo(10.0f).Within(0.001f));

                BlockiverseRuntimeState.SetRouterState(isGamePaused: false, allowWorldInput: true);
                CreativeInputBridgeUpdateMethod.Invoke(bridge, null);

                Assert.That(lineVisual.overrideInteractorLineLength, Is.False,
                    "World/block interaction mode should restore the normal XRI line visual length behavior.");
                Assert.That(lineVisual.lineLength, Is.EqualTo(10.0f).Within(0.001f));
            }
            finally
            {
                BlockiverseRuntimeState.Reset();
                Object.DestroyImmediate(bridgeObject);
            }
        }

        [UnityTest]
        public IEnumerator BootSceneDefersCreativeWorldGenerationUntilSessionSelection()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle("Boot");

            yield return null;

            GameObject worldObject = GameObject.Find("Creative World");
            int interactionLayer = BlockiverseProject.InteractionLayerIndex;

            Assert.That(worldObject, Is.Not.Null);
            Assert.That(interactionLayer, Is.GreaterThanOrEqualTo(0));

            CreativeWorldManager manager = worldObject.GetComponent<CreativeWorldManager>();
            VoxelWorldRenderer renderer = worldObject.GetComponent<VoxelWorldRenderer>();
            BlockiverseCreativeInputBridge[] bridges = Object.FindObjectsByType<BlockiverseCreativeInputBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            MeshFilter[] chunkFilters = worldObject.GetComponentsInChildren<MeshFilter>();
            int activeSceneBridgeCount = 0;

            foreach (BlockiverseCreativeInputBridge bridge in bridges)
            {
                if (bridge.gameObject.scene == SceneManager.GetActiveScene())
                    activeSceneBridgeCount++;
            }

            Assert.That(manager, Is.Not.Null);
            Assert.That(worldObject.GetComponent<BlockiverseCreativeInputBridge>(), Is.Null);
            Assert.That(activeSceneBridgeCount, Is.EqualTo(1));
            Assert.That(manager.World, Is.Null);
            Assert.That(renderer == null || renderer.Stats.ChunkCount == 0, Is.True);
            Assert.That(chunkFilters, Is.Empty);
            Assert.That(Object.FindAnyObjectByType<BlockiverseVoidSafetyFloor>(FindObjectsInactive.Include), Is.Null);
            Assert.That(GameObject.Find("Interaction Test Block"), Is.Null);
        }

        [UnityTearDown]
        public IEnumerator CleanupTrackedPoseDriversAfterTest()
        {
            BlockiverseRuntimeState.Reset();
            yield return BlockiversePlayModeSceneTestUtility.CleanupTrackedPoseDrivers();
        }
    }

    public sealed class BlockiverseInteractionInputPlayModeTests : InputTestFixture
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
        public IEnumerator LeftActivateTogglesBlockMenuWithoutTogglingComfortMenu()
        {
            GameObject rigObject = new("Test Input Rig");
            GameObject comfortMenuObject = new("Comfort Menu");
            GameObject blockMenuObject = new("Block Menu Placeholder");
            InputActionAsset actions = CreateTestActions();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();

            try
            {
                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);

                var comfortSettings = rigObject.AddComponent<BlockiverseComfortSettings>();
                var comfortCanvas = comfortMenuObject.AddComponent<Canvas>();
                var comfortMenu = comfortMenuObject.AddComponent<BlockiverseComfortMenu>();
                comfortMenu.Configure(comfortCanvas, comfortSettings);
                inputRig.MenuPressed.AddListener(comfortMenu.ToggleVisible);

                // Use the real quick-menu presenter (the component the bootstrapper wires to
                // QuickMenuPressed) rather than a placeholder, starting hidden like the runtime menu.
                var blockCanvas = blockMenuObject.AddComponent<Canvas>();
                var blockMenu = blockMenuObject.AddComponent<BlockiverseWorldSpacePanelPresenter>();
                blockMenu.Configure(blockCanvas, rigObject.transform, 1.12f, -0.34f, -0.18f, 0.0f);
                blockCanvas.enabled = false;

                inputRig.QuickMenuPressed.AddListener(blockMenu.ToggleVisible);

                Press(gamepad.leftShoulder);
                yield return null;

                Assert.That(blockMenu.IsVisible, Is.True);
                Assert.That(comfortMenu.IsVisible, Is.False);

                Release(gamepad.leftShoulder);
                yield return null;
                Press(gamepad.startButton);
                yield return null;

                Assert.That(blockMenu.IsVisible, Is.True);
                Assert.That(comfortMenu.IsVisible, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blockMenuObject);
                UnityEngine.Object.DestroyImmediate(comfortMenuObject);
                UnityEngine.Object.DestroyImmediate(rigObject);
                UnityEngine.Object.DestroyImmediate(actions);
            }
        }

        [UnityTest]
        public IEnumerator RightSelectAndRightActivateRaiseCreativeEvents()
        {
            GameObject rigObject = new("Test Input Rig");
            InputActionAsset actions = CreateCreativeBindingActions();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            int breakPresses = 0;
            int placePresses = 0;

            try
            {
                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);
                inputRig.BreakPressed.AddListener(() => breakPresses++);
                inputRig.PlacePressed.AddListener(() => placePresses++);

                Press(gamepad.rightTrigger);
                yield return null;
                Release(gamepad.rightTrigger);
                yield return null;

                Press(gamepad.rightShoulder);
                yield return null;
                Release(gamepad.rightShoulder);
                yield return null;

                Assert.That(breakPresses, Is.EqualTo(1));
                Assert.That(placePresses, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(actions);
            }
        }

        [UnityTest]
        public IEnumerator RightSelectStillRaisesBreakEventWhileWorldInputIsSuppressed()
        {
            GameObject rigObject = new("Test Input Rig");
            InputActionAsset actions = CreateCreativeBindingActions();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            int breakPresses = 0;
            int breakReleases = 0;
            int placePresses = 0;

            try
            {
                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);
                inputRig.BreakPressed.AddListener(() => breakPresses++);
                inputRig.BreakReleased.AddListener(() => breakReleases++);
                inputRig.PlacePressed.AddListener(() => placePresses++);

                BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false);

                Press(gamepad.rightTrigger);
                yield return null;
                Release(gamepad.rightTrigger);
                yield return null;

                Press(gamepad.rightShoulder);
                yield return null;

                Assert.That(breakPresses, Is.EqualTo(1),
                    "Menu-specific trigger fallbacks need the dominant trigger event even while world editing is suppressed.");
                Assert.That(breakReleases, Is.EqualTo(1));
                Assert.That(placePresses, Is.EqualTo(0),
                    "World edit/use actions should remain suppressed while routed menus are active.");
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(actions);
            }
        }

        static InputActionAsset CreateTestActions()
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();

            InputActionMap leftHand = actions.AddActionMap(BlockiverseInputActionNames.LeftHandMap);
            leftHand.AddAction(BlockiverseInputActionNames.Activate, InputActionType.Button, "<Gamepad>/leftShoulder");

            InputActionMap gameplay = actions.AddActionMap(BlockiverseInputActionNames.GameplayMap);
            gameplay.AddAction(BlockiverseInputActionNames.Menu, InputActionType.Button, "<Gamepad>/start");

            return actions;
        }

        static InputActionAsset CreateCreativeBindingActions()
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();

            InputActionMap rightHand = actions.AddActionMap(BlockiverseInputActionNames.RightHandMap);
            rightHand.AddAction(BlockiverseInputActionNames.Select, InputActionType.Button, "<Gamepad>/rightTrigger");
            rightHand.AddAction(BlockiverseInputActionNames.Activate, InputActionType.Button, "<Gamepad>/rightShoulder");

            return actions;
        }
    }
}
