using System.Collections;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class BlockiverseInteractionPlayModeTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator RayPointerHighlightsTargetAndRestoresWhenAimChanges()
        {
            GameObject rigObject = new("Test Input Rig");
            GameObject pointerObject = new("Test Ray Pointer");
            GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var pointerLineObject = new GameObject("Pointer Line");
            InputActionAsset actions = CreateTrackingActions();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            Material originalMaterial = new(Shader.Find("Sprites/Default"));
            Material highlightMaterial = new(Shader.Find("Sprites/Default"));
            LineRenderer lineRenderer = pointerLineObject.AddComponent<LineRenderer>();

            try
            {
                BlockiverseInputRig inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);

                BlockiverseControllerAnchor anchor = pointerObject.AddComponent<BlockiverseControllerAnchor>();
                anchor.Configure(inputRig, BlockiverseControllerRole.Right);

                pointerLineObject.transform.SetParent(pointerObject.transform, false);
                pointerObject.transform.position = Vector3.zero;
                pointerObject.transform.rotation = Quaternion.identity;
                targetObject.transform.position = new Vector3(0.0f, 0.0f, 2.0f);

                MeshRenderer renderer = targetObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = originalMaterial;

                BlockiverseHighlightTarget target = targetObject.AddComponent<BlockiverseHighlightTarget>();
                target.Configure(renderer, highlightMaterial);

                lineRenderer.useWorldSpace = true;
                lineRenderer.positionCount = 2;

                BlockiverseRayPointer pointer = pointerObject.AddComponent<BlockiverseRayPointer>();
                pointer.Configure(pointerObject.transform, lineRenderer, Physics.DefaultRaycastLayers, 4.0f, anchor);

                Press(gamepad.rightShoulder);
                yield return null;

                Assert.That(lineRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(lineRenderer.receiveShadows, Is.False);

                Physics.SyncTransforms();
                pointer.Refresh();
                yield return null;

                Assert.That(renderer.sharedMaterial, Is.SameAs(highlightMaterial));

                pointerObject.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                Physics.SyncTransforms();
                pointer.Refresh();
                yield return null;

                Assert.That(renderer.sharedMaterial, Is.SameAs(originalMaterial));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pointerObject);
                UnityEngine.Object.DestroyImmediate(targetObject);
                UnityEngine.Object.DestroyImmediate(rigObject);
                UnityEngine.Object.DestroyImmediate(actions);
                UnityEngine.Object.DestroyImmediate(originalMaterial);
                UnityEngine.Object.DestroyImmediate(highlightMaterial);
            }
        }

        [UnityTest]
        public IEnumerator RayPointerClearsHighlightAndHidesLineWhenTrackingSourceIsMissing()
        {
            GameObject pointerObject = new("Test Ray Pointer");
            GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject pointerLineObject = new("Pointer Line");
            Material originalMaterial = new(Shader.Find("Sprites/Default"));
            Material highlightMaterial = new(Shader.Find("Sprites/Default"));

            try
            {
                pointerLineObject.transform.SetParent(pointerObject.transform, false);
                pointerObject.transform.position = Vector3.zero;
                pointerObject.transform.rotation = Quaternion.identity;
                targetObject.transform.position = new Vector3(0.0f, 0.0f, 2.0f);

                MeshRenderer renderer = targetObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = originalMaterial;
                BlockiverseHighlightTarget target = targetObject.AddComponent<BlockiverseHighlightTarget>();
                target.Configure(renderer, highlightMaterial);

                LineRenderer lineRenderer = pointerLineObject.AddComponent<LineRenderer>();
                lineRenderer.useWorldSpace = true;
                lineRenderer.positionCount = 2;
                lineRenderer.enabled = true;

                BlockiverseRayPointer pointer = pointerObject.AddComponent<BlockiverseRayPointer>();
                pointer.Configure(pointerObject.transform, lineRenderer, Physics.DefaultRaycastLayers, 4.0f);

                Physics.SyncTransforms();
                pointer.Refresh();
                yield return null;

                Assert.That(lineRenderer.enabled, Is.False);
                Assert.That(renderer.sharedMaterial, Is.SameAs(originalMaterial));
                Assert.That(pointer.HighlightedTarget, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(pointerLineObject);
                Object.DestroyImmediate(pointerObject);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(originalMaterial);
                Object.DestroyImmediate(highlightMaterial);
            }
        }

        [UnityTest]
        public IEnumerator RayPointerHidesLineAndClearsHighlightWhenDisabled()
        {
            GameObject rigObject = new("Test Input Rig");
            GameObject pointerObject = new("Test Ray Pointer");
            GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject pointerLineObject = new("Pointer Line");
            InputActionAsset actions = CreateTrackingActions();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            Material originalMaterial = new(Shader.Find("Sprites/Default"));
            Material highlightMaterial = new(Shader.Find("Sprites/Default"));

            try
            {
                BlockiverseInputRig inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);

                BlockiverseControllerAnchor anchor = pointerObject.AddComponent<BlockiverseControllerAnchor>();
                anchor.Configure(inputRig, BlockiverseControllerRole.Right);

                pointerLineObject.transform.SetParent(pointerObject.transform, false);
                pointerObject.transform.position = Vector3.zero;
                pointerObject.transform.rotation = Quaternion.identity;
                targetObject.transform.position = new Vector3(0.0f, 0.0f, 2.0f);

                MeshRenderer renderer = targetObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = originalMaterial;
                BlockiverseHighlightTarget target = targetObject.AddComponent<BlockiverseHighlightTarget>();
                target.Configure(renderer, highlightMaterial);

                LineRenderer lineRenderer = pointerLineObject.AddComponent<LineRenderer>();
                lineRenderer.useWorldSpace = true;
                lineRenderer.positionCount = 2;

                BlockiverseRayPointer pointer = pointerObject.AddComponent<BlockiverseRayPointer>();
                pointer.Configure(pointerObject.transform, lineRenderer, Physics.DefaultRaycastLayers, 4.0f, anchor);

                Press(gamepad.rightShoulder);
                yield return null;

                Physics.SyncTransforms();
                pointer.Refresh();
                yield return null;

                Assert.That(lineRenderer.enabled, Is.True);
                Assert.That(pointer.HighlightedTarget, Is.SameAs(target));
                Assert.That(renderer.sharedMaterial, Is.SameAs(highlightMaterial));

                pointer.enabled = false;
                yield return null;

                Assert.That(lineRenderer.enabled, Is.False);
                Assert.That(pointer.HighlightedTarget, Is.Null);
                Assert.That(renderer.sharedMaterial, Is.SameAs(originalMaterial));
            }
            finally
            {
                Object.DestroyImmediate(pointerLineObject);
                Object.DestroyImmediate(pointerObject);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(actions);
                Object.DestroyImmediate(originalMaterial);
                Object.DestroyImmediate(highlightMaterial);
            }
        }

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
            AsyncOperation operation = SceneManager.LoadSceneAsync("Boot", LoadSceneMode.Single);

            while (!operation.isDone)
                yield return null;

            yield return null;

            GameObject worldObject = GameObject.Find("Creative World");
            int interactionLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);

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
            Assert.That(renderer, Is.Not.Null);
            Assert.That(worldObject.GetComponent<BlockiverseCreativeInputBridge>(), Is.Null);
            Assert.That(activeSceneBridgeCount, Is.EqualTo(1));
            Assert.That(manager.World, Is.Not.Null);
            Assert.That(manager.World.Bounds.Width, Is.GreaterThan(0));
            Assert.That(renderer.TextureSource, Is.EqualTo(BlockTextureSource.AuthoredAtlas));
            Assert.That(renderer.Stats.ChunkCount, Is.GreaterThan(0));
            Assert.That(renderer.Stats.TriangleCount, Is.GreaterThan(0));
            Assert.That(chunkFilters, Has.Length.GreaterThan(0));
            Assert.That(GameObject.Find("Interaction Test Block"), Is.Null);
        }

        static InputActionAsset CreateTrackingActions()
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap rightHand = actions.AddActionMap(BlockiverseInputActionNames.RightHandMap);
            rightHand.AddAction(BlockiverseInputActionNames.IsTracked, InputActionType.Button, "<Gamepad>/rightShoulder");
            return actions;
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
        public IEnumerator RayPointerClearsHighlightAndHidesLineWhenControllerLosesTracking()
        {
            GameObject rigObject = new("Test Input Rig");
            GameObject pointerObject = new("Right Controller");
            GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject pointerLineObject = new("Pointer Line");
            InputActionAsset actions = CreateTrackingActions();
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            Material originalMaterial = new(Shader.Find("Sprites/Default"));
            Material highlightMaterial = new(Shader.Find("Sprites/Default"));

            try
            {
                BlockiverseInputRig inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(actions);

                BlockiverseControllerAnchor anchor = pointerObject.AddComponent<BlockiverseControllerAnchor>();
                anchor.Configure(inputRig, BlockiverseControllerRole.Right);

                pointerObject.transform.position = Vector3.zero;
                pointerObject.transform.rotation = Quaternion.identity;
                targetObject.transform.position = new Vector3(0.0f, 0.0f, 2.0f);

                MeshRenderer renderer = targetObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = originalMaterial;
                BlockiverseHighlightTarget target = targetObject.AddComponent<BlockiverseHighlightTarget>();
                target.Configure(renderer, highlightMaterial);

                pointerLineObject.transform.SetParent(pointerObject.transform, false);
                LineRenderer lineRenderer = pointerLineObject.AddComponent<LineRenderer>();
                lineRenderer.useWorldSpace = true;
                lineRenderer.positionCount = 2;

                BlockiverseRayPointer pointer = pointerObject.AddComponent<BlockiverseRayPointer>();
                pointer.Configure(pointerObject.transform, lineRenderer, Physics.DefaultRaycastLayers, 4.0f);

                Press(gamepad.rightShoulder);
                yield return null;

                Physics.SyncTransforms();
                pointer.Refresh();
                yield return null;

                Assert.That(lineRenderer.enabled, Is.True);
                Assert.That(renderer.sharedMaterial, Is.SameAs(highlightMaterial));
                Assert.That(pointer.HighlightedTarget, Is.SameAs(target));

                Release(gamepad.rightShoulder);
                yield return null;

                Physics.SyncTransforms();
                pointer.Refresh();
                yield return null;

                Assert.That(lineRenderer.enabled, Is.False);
                Assert.That(renderer.sharedMaterial, Is.SameAs(originalMaterial));
                Assert.That(pointer.HighlightedTarget, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(pointerLineObject);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(pointerObject);
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(actions);
                Object.DestroyImmediate(originalMaterial);
                Object.DestroyImmediate(highlightMaterial);
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

        static InputActionAsset CreateTrackingActions()
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap rightHand = actions.AddActionMap(BlockiverseInputActionNames.RightHandMap);
            rightHand.AddAction(BlockiverseInputActionNames.IsTracked, InputActionType.Button, "<Gamepad>/rightShoulder");
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
