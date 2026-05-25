using System;
using System.Collections;
using System.Reflection;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
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
            Type pointerType = Type.GetType("Blockiverse.VR.BlockiverseRayPointer, Blockiverse.VR");
            Type targetType = Type.GetType("Blockiverse.VR.BlockiverseHighlightTarget, Blockiverse.VR");

            Assert.That(pointerType, Is.Not.Null);
            Assert.That(targetType, Is.Not.Null);

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

                Component target = targetObject.AddComponent(targetType);
                targetType.GetMethod("Configure")?.Invoke(target, new object[] { renderer, highlightMaterial });

                lineRenderer.useWorldSpace = true;
                lineRenderer.positionCount = 2;

                Component pointer = pointerObject.AddComponent(pointerType);
                pointerType.GetMethod("Configure")?.Invoke(
                    pointer,
                    new object[] { pointerObject.transform, lineRenderer, (LayerMask)Physics.DefaultRaycastLayers, 4.0f });

                Physics.SyncTransforms();
                pointerType.GetMethod("Refresh")?.Invoke(pointer, null);
                yield return null;

                Assert.That(renderer.sharedMaterial, Is.SameAs(highlightMaterial));

                pointerObject.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                Physics.SyncTransforms();
                pointerType.GetMethod("Refresh")?.Invoke(pointer, null);
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
        public IEnumerator BootSceneContainsInteractionTestBlock()
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync("Boot", LoadSceneMode.Single);

            while (!operation.isDone)
                yield return null;

            Type targetType = Type.GetType("Blockiverse.VR.BlockiverseHighlightTarget, Blockiverse.VR");

            Assert.That(targetType, Is.Not.Null);
            Assert.That(UnityEngine.Object.FindFirstObjectByType(targetType, FindObjectsInactive.Include), Is.Not.Null);
        }
    }

    public sealed class BlockiverseInteractionInputPlayModeTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator LeftActivateTogglesBlockMenuWithoutTogglingComfortMenu()
        {
            Type quickMenuType = Type.GetType("Blockiverse.VR.BlockiverseQuickMenuPlaceholder, Blockiverse.VR");
            PropertyInfo quickMenuPressedProperty = typeof(BlockiverseInputRig).GetProperty("QuickMenuPressed");

            Assert.That(quickMenuType, Is.Not.Null);
            Assert.That(quickMenuPressedProperty, Is.Not.Null);

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
                Component blockMenu = blockMenuObject.AddComponent(quickMenuType);
                quickMenuType.GetMethod("Configure")?.Invoke(blockMenu, new object[] { blockCanvas });

                UnityEvent quickMenuPressed = quickMenuPressedProperty.GetValue(inputRig) as UnityEvent;
                MethodInfo toggleVisible = quickMenuType.GetMethod("ToggleVisible");
                quickMenuPressed.AddListener(() => toggleVisible.Invoke(blockMenu, null));

                Press(gamepad.leftShoulder);
                yield return null;

                Assert.That(ReadBoolProperty(quickMenuType, blockMenu, "IsVisible"), Is.True);
                Assert.That(comfortMenu.IsVisible, Is.False);

                Release(gamepad.leftShoulder);
                yield return null;
                Press(gamepad.startButton);
                yield return null;

                Assert.That(ReadBoolProperty(quickMenuType, blockMenu, "IsVisible"), Is.True);
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

        static bool ReadBoolProperty(Type type, object instance, string propertyName)
        {
            PropertyInfo property = type.GetProperty(propertyName);

            Assert.That(property, Is.Not.Null);
            return (bool)property.GetValue(instance);
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
    }
}
