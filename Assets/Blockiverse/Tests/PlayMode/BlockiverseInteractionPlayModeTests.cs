using System.Collections;
using Blockiverse.Core;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class BlockiverseInteractionPlayModeTests
    {
        [UnityTest]
        public IEnumerator RayPointerHighlightsTargetAndRestoresWhenAimChanges()
        {
            GameObject pointerObject = new("Test Ray Pointer");
            GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var pointerLineObject = new GameObject("Pointer Line");
            Material originalMaterial = new(Shader.Find("Sprites/Default"));
            Material highlightMaterial = new(Shader.Find("Sprites/Default"));
            LineRenderer lineRenderer = pointerLineObject.AddComponent<LineRenderer>();

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

                lineRenderer.useWorldSpace = true;
                lineRenderer.positionCount = 2;

                BlockiverseRayPointer pointer = pointerObject.AddComponent<BlockiverseRayPointer>();
                pointer.Configure(pointerObject.transform, lineRenderer, Physics.DefaultRaycastLayers, 4.0f);

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
                UnityEngine.Object.DestroyImmediate(originalMaterial);
                UnityEngine.Object.DestroyImmediate(highlightMaterial);
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
        public IEnumerator BootSceneContainsInteractionTestBlock()
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync("Boot", LoadSceneMode.Single);

            while (!operation.isDone)
                yield return null;

            GameObject blockObject = GameObject.Find("Interaction Test Block");
            int interactionLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);

            Assert.That(blockObject, Is.Not.Null);
            Assert.That(interactionLayer, Is.GreaterThanOrEqualTo(0));
            Assert.That(blockObject.layer, Is.EqualTo(interactionLayer));

            BoxCollider collider = blockObject.GetComponent<BoxCollider>();
            MeshRenderer renderer = blockObject.GetComponent<MeshRenderer>();
            BlockiverseHighlightTarget target = blockObject.GetComponent<BlockiverseHighlightTarget>();
            Material originalMaterial = renderer?.sharedMaterial;

            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.enabled, Is.True);
            Assert.That(renderer, Is.Not.Null);
            Assert.That(originalMaterial, Is.Not.Null);
            Assert.That(originalMaterial.name, Does.Contain("BlockiverseTestBlock"));
            Assert.That(target, Is.Not.Null);

            target.SetHighlighted(true);
            Assert.That(renderer.sharedMaterial, Is.Not.Null);
            Assert.That(renderer.sharedMaterial.name, Does.Contain("BlockiverseHighlight"));

            target.SetHighlighted(false);
            Assert.That(renderer.sharedMaterial, Is.SameAs(originalMaterial));
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
    }
}
