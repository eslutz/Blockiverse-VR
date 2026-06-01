using System.Collections;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class BlockiverseInteractionPlayModeTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator HighlightTargetReconfigurationRestoresTheNewRendererMaterials()
        {
            GameObject firstObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject secondObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Material firstMaterial = new(Shader.Find("Sprites/Default"));
            Material secondMaterial = new(Shader.Find("Sprites/Default"));
            Material highlightMaterial = new(Shader.Find("Sprites/Default"));

            try
            {
                MeshRenderer firstRenderer = firstObject.GetComponent<MeshRenderer>();
                MeshRenderer secondRenderer = secondObject.GetComponent<MeshRenderer>();
                firstRenderer.sharedMaterial = firstMaterial;
                secondRenderer.sharedMaterial = secondMaterial;

                BlockiverseHighlightTarget target = firstObject.AddComponent<BlockiverseHighlightTarget>();
                target.Configure(firstRenderer, highlightMaterial);
                target.SetHighlighted(true);
                target.SetHighlighted(false);

                Assert.That(firstRenderer.sharedMaterial, Is.SameAs(firstMaterial));

                target.Configure(secondRenderer, highlightMaterial);
                target.SetHighlighted(true);
                target.SetHighlighted(false);

                yield return null;

                Assert.That(secondRenderer.sharedMaterial, Is.SameAs(secondMaterial));
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
                Object.DestroyImmediate(firstMaterial);
                Object.DestroyImmediate(secondMaterial);
                Object.DestroyImmediate(highlightMaterial);
            }
        }

        [UnityTest]
        public IEnumerator BootSceneContainsCreativeWorld()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle("Boot");

            yield return null;

            GameObject worldObject = GameObject.Find("Creative World");
            int interactionLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);

            Assert.That(worldObject, Is.Not.Null);
            Assert.That(interactionLayer, Is.GreaterThanOrEqualTo(0));

            CreativeWorldManager manager = worldObject.GetComponent<CreativeWorldManager>();
            VoxelWorldRenderer renderer = worldObject.GetComponent<VoxelWorldRenderer>();
            BlockiverseCreativeInputBridge[] bridges = Object.FindObjectsByType<BlockiverseCreativeInputBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            MeshFilter[] chunkFilters = worldObject.GetComponentsInChildren<MeshFilter>();
            MeshRenderer chunkRenderer = null;
            int activeSceneBridgeCount = 0;

            foreach (BlockiverseCreativeInputBridge bridge in bridges)
            {
                if (bridge.gameObject.scene == SceneManager.GetActiveScene())
                    activeSceneBridgeCount++;
            }

            foreach (MeshRenderer candidate in worldObject.GetComponentsInChildren<MeshRenderer>())
            {
                if (candidate.gameObject.name.StartsWith("Chunk "))
                {
                    chunkRenderer = candidate;
                    break;
                }
            }

            Assert.That(manager, Is.Not.Null);
            Assert.That(renderer, Is.Not.Null);
            Assert.That(worldObject.GetComponent<BlockiverseCreativeInputBridge>(), Is.Null);
            Assert.That(activeSceneBridgeCount, Is.EqualTo(1));
            Assert.That(manager.World, Is.Not.Null);
            Assert.That(manager.World.Bounds.Width, Is.GreaterThan(0));
            Assert.That(renderer.Stats.ChunkCount, Is.GreaterThan(0));
            Assert.That(renderer.Stats.TriangleCount, Is.GreaterThan(0));
            Assert.That(chunkFilters, Has.Length.GreaterThan(0));
            Assert.That(chunkRenderer, Is.Not.Null);
            Assert.That(BlockVisualAtlas.TryGetBaseTexture(chunkRenderer.sharedMaterial, out Texture texture), Is.True);
            Assert.That(BlockVisualAtlas.IsAuthoredAtlasTexture(texture), Is.True);
            Assert.That(GameObject.Find("Interaction Test Block"), Is.Null);
        }

        [UnityTearDown]
        public IEnumerator CleanupTrackedPoseDriversAfterTest()
        {
            yield return BlockiversePlayModeSceneTestUtility.CleanupTrackedPoseDrivers();
        }
    }

    public sealed class BlockiverseInteractionInputPlayModeTests : InputTestFixture
    {
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

                var blockCanvas = blockMenuObject.AddComponent<Canvas>();
                BlockiverseQuickMenuPlaceholder blockMenu = blockMenuObject.AddComponent<BlockiverseQuickMenuPlaceholder>();
                blockMenu.Configure(blockCanvas);

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
        public IEnumerator RightSelectRightActivateAndUndoRaiseCreativeEvents()
        {
            GameObject rigObject = new("Test Input Rig");
            InputActionAsset actions = CreateCreativeBindingActions();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            int breakPresses = 0;
            int placePresses = 0;
            int undoPresses = 0;

            try
            {
                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);
                inputRig.BreakPressed.AddListener(() => breakPresses++);
                inputRig.PlacePressed.AddListener(() => placePresses++);
                inputRig.UndoPressed.AddListener(() => undoPresses++);

                Press(gamepad.rightTrigger);
                yield return null;
                Release(gamepad.rightTrigger);
                yield return null;

                Press(gamepad.rightShoulder);
                yield return null;
                Release(gamepad.rightShoulder);
                yield return null;

                Press(gamepad.buttonEast);
                yield return null;

                Assert.That(breakPresses, Is.EqualTo(1));
                Assert.That(placePresses, Is.EqualTo(1));
                Assert.That(undoPresses, Is.EqualTo(1));
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

            InputActionMap gameplay = actions.AddActionMap(BlockiverseInputActionNames.GameplayMap);
            gameplay.AddAction(BlockiverseInputActionNames.Undo, InputActionType.Button, "<Gamepad>/buttonEast");

            return actions;
        }
    }
}
