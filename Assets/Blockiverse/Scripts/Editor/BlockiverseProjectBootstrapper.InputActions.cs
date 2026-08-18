using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.MetaAvatars;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.VR;
using Oculus.Avatar2;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Editor.Configuration;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.Interaction.Toolkit;
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
using Unity.XR.CoreUtils;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        static InputActionAsset EnsureInputActions()
        {
            EnsureFolder(BlockiverseProject.InputActionReferencesFolderPath);

            string json = BuildDeterministicInputActionsJson();

            if (!File.Exists(BlockiverseProject.InputActionsAssetPath) ||
                File.ReadAllText(BlockiverseProject.InputActionsAssetPath) != json)
            {
                File.WriteAllText(BlockiverseProject.InputActionsAssetPath, json);
            }

            AssetDatabase.ImportAsset(
                BlockiverseProject.InputActionsAssetPath,
                ImportAssetOptions.ForceSynchronousImport);

            var importedAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                BlockiverseProject.InputActionsAssetPath);

            if (importedAsset == null)
                throw new InvalidOperationException("Unable to create Blockiverse input actions asset.");

            EnsureInputActionReferences(importedAsset);
            return importedAsset;
        }

        static string BuildDeterministicInputActionsJson()
        {
            var assetJson = new InputActionAssetJson
            {
                version = 1,
                name = "BlockiverseInputActions",
                maps = new[]
                {
                    CreateHeadMap(),
                    CreateControllerMapJson(BlockiverseInputActionNames.LeftHandMap, "<XRController>{LeftHand}"),
                    CreateControllerMapJson(BlockiverseInputActionNames.RightHandMap, "<XRController>{RightHand}"),
                    CreateGameplayMapJson(),
                },
                controlSchemes = Array.Empty<InputControlSchemeJson>(),
            };

            return JsonUtility.ToJson(assetJson, true) + "\n";
        }

        static InputActionMapJson CreateHeadMap()
        {
            var actions = new[]
            {
                CreateAction(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.Position, InputActionType.PassThrough, "Vector3"),
                CreateAction(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.Rotation, InputActionType.PassThrough, "Quaternion"),
                CreateAction(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.LeftEyePosition, InputActionType.PassThrough, "Vector3"),
                CreateAction(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.LeftEyeRotation, InputActionType.PassThrough, "Quaternion"),
                CreateAction(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.RightEyePosition, InputActionType.PassThrough, "Vector3"),
                CreateAction(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.RightEyeRotation, InputActionType.PassThrough, "Quaternion"),
                CreateAction(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.TrackingState, InputActionType.PassThrough, "Integer"),
            };

            var bindings = new[]
            {
                CreateBinding(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.Position, "center-eye-position", "<XRHMD>/centerEyePosition"),
                CreateBinding(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.Rotation, "center-eye-rotation", "<XRHMD>/centerEyeRotation"),
                CreateBinding(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.LeftEyePosition, "left-eye-position", "<XRHMD>/leftEyePosition"),
                CreateBinding(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.LeftEyeRotation, "left-eye-rotation", "<XRHMD>/leftEyeRotation"),
                CreateBinding(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.RightEyePosition, "right-eye-position", "<XRHMD>/rightEyePosition"),
                CreateBinding(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.RightEyeRotation, "right-eye-rotation", "<XRHMD>/rightEyeRotation"),
                CreateBinding(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.TrackingState, "tracking-state", "<XRHMD>/trackingState"),
            };

            return CreateMap(BlockiverseInputActionNames.HeadMap, actions, bindings);
        }

        static InputActionMapJson CreateControllerMapJson(string mapName, string controllerPath)
        {
            var actions = new[]
            {
                CreateAction(mapName, BlockiverseInputActionNames.Position, InputActionType.PassThrough, "Vector3"),
                CreateAction(mapName, BlockiverseInputActionNames.Rotation, InputActionType.PassThrough, "Quaternion"),
                CreateAction(mapName, BlockiverseInputActionNames.IsTracked, InputActionType.Button, "Button"),
                CreateAction(mapName, BlockiverseInputActionNames.TrackingState, InputActionType.PassThrough, "Integer"),
                CreateAction(mapName, BlockiverseInputActionNames.Select, InputActionType.Button, "Button"),
                CreateAction(mapName, BlockiverseInputActionNames.Activate, InputActionType.Button, "Button"),
                CreateAction(mapName, BlockiverseInputActionNames.PrimaryButton, InputActionType.Button, "Button"),
                CreateAction(mapName, BlockiverseInputActionNames.SecondaryButton, InputActionType.Button, "Button"),
                CreateAction(mapName, BlockiverseInputActionNames.UiPress, InputActionType.Button, "Button"),
                CreateAction(mapName, BlockiverseInputActionNames.UiScroll, InputActionType.PassThrough, "Vector2"),
                CreateAction(mapName, BlockiverseInputActionNames.HapticDevice, InputActionType.PassThrough, string.Empty),
                CreateAction(mapName, BlockiverseInputActionNames.Move, InputActionType.PassThrough, "Vector2"),
                CreateAction(mapName, BlockiverseInputActionNames.Turn, InputActionType.PassThrough, "Vector2"),
                CreateAction(mapName, BlockiverseInputActionNames.Sprint, InputActionType.Button, "Button"),
                CreateAction(mapName, BlockiverseInputActionNames.Crouch, InputActionType.Button, "Button"),
                CreateAction(mapName, BlockiverseInputActionNames.TeleportMode, InputActionType.Button, "Button"),
                CreateAction(mapName, BlockiverseInputActionNames.TeleportSelect, InputActionType.Button, "Button"),
                CreateAction(mapName, BlockiverseInputActionNames.AimPosition, InputActionType.PassThrough, "Vector3"),
                CreateAction(mapName, BlockiverseInputActionNames.AimRotation, InputActionType.PassThrough, "Quaternion"),
            };

            var bindings = new List<InputBindingJson>
            {
                CreateBinding(mapName, BlockiverseInputActionNames.Position, "device-position", $"{controllerPath}/devicePosition"),
                CreateBinding(mapName, BlockiverseInputActionNames.Rotation, "device-rotation", $"{controllerPath}/deviceRotation"),
                CreateBinding(mapName, BlockiverseInputActionNames.IsTracked, "is-tracked", $"{controllerPath}/isTracked"),
                CreateBinding(mapName, BlockiverseInputActionNames.TrackingState, "tracking-state", $"{controllerPath}/trackingState"),
                CreateBinding(mapName, BlockiverseInputActionNames.Select, "trigger-pressed", $"{controllerPath}/triggerPressed"),
                CreateBinding(mapName, BlockiverseInputActionNames.Activate, "grip-pressed", $"{controllerPath}/gripPressed"),
                CreateBinding(mapName, BlockiverseInputActionNames.PrimaryButton, "primary-button", $"{controllerPath}/primaryButton"),
                CreateBinding(mapName, BlockiverseInputActionNames.SecondaryButton, "secondary-button", $"{controllerPath}/secondaryButton"),
                CreateBinding(mapName, BlockiverseInputActionNames.UiPress, "ui-trigger-pressed", $"{controllerPath}/triggerPressed"),
                CreateBinding(mapName, BlockiverseInputActionNames.UiScroll, "ui-thumbstick", $"{controllerPath}/thumbstick"),
                CreateBinding(mapName, BlockiverseInputActionNames.HapticDevice, "haptic-device", $"{controllerPath}/*"),
                CreateBinding(mapName, BlockiverseInputActionNames.Move, "move-thumbstick", $"{controllerPath}/thumbstick", StickDeadzoneProcessor),
                CreateBinding(mapName, BlockiverseInputActionNames.Turn, "turn-thumbstick", $"{controllerPath}/thumbstick", StickDeadzoneProcessor),
                CreateBinding(mapName, BlockiverseInputActionNames.Sprint, "thumbstick-clicked", $"{controllerPath}/thumbstickClicked"),
                CreateBinding(mapName, BlockiverseInputActionNames.Crouch, "crouch-thumbstick-clicked", $"{controllerPath}/thumbstickClicked"),
                CreateBinding(mapName, BlockiverseInputActionNames.AimPosition, "pointer-position", $"{controllerPath}/pointerPosition"),
                CreateBinding(mapName, BlockiverseInputActionNames.AimRotation, "pointer-rotation", $"{controllerPath}/pointerRotation"),
            };

            AddThumbstickYCompositeBindings(bindings, mapName, BlockiverseInputActionNames.TeleportMode, controllerPath);
            AddThumbstickYCompositeBindings(bindings, mapName, BlockiverseInputActionNames.TeleportSelect, controllerPath);

            return CreateMap(mapName, actions, bindings.ToArray());
        }

        static InputActionMapJson CreateGameplayMapJson()
        {
            var actions = new[]
            {
                CreateAction(BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Menu, InputActionType.Button, "Button"),
            };

            var bindings = new[]
            {
                CreateBinding(BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Menu, "left-menu-button", "<XRController>{LeftHand}/menuButton"),
            };

            return CreateMap(BlockiverseInputActionNames.GameplayMap, actions, bindings);
        }

        static InputActionMapJson CreateMap(string mapName, InputActionJson[] actions, InputBindingJson[] bindings)
        {
            return new InputActionMapJson
            {
                name = mapName,
                id = BlockiverseDeterministicInputIds.ForMap(mapName).ToString(),
                actions = actions,
                bindings = bindings,
            };
        }

        static InputActionJson CreateAction(string mapName, string actionName, InputActionType actionType, string expectedControlType)
        {
            return new InputActionJson
            {
                name = actionName,
                type = actionType.ToString(),
                id = BlockiverseDeterministicInputIds.ForAction(mapName, actionName).ToString(),
                expectedControlType = expectedControlType,
                processors = string.Empty,
                interactions = string.Empty,
                initialStateCheck = actionType == InputActionType.PassThrough,
            };
        }

        static InputBindingJson CreateBinding(
            string mapName,
            string actionName,
            string bindingKey,
            string path,
            string processors = "")
        {
            return new InputBindingJson
            {
                name = string.Empty,
                id = BlockiverseDeterministicInputIds.ForBinding(mapName, actionName, bindingKey).ToString(),
                path = path,
                interactions = string.Empty,
                processors = processors,
                groups = string.Empty,
                action = actionName,
                isComposite = false,
                isPartOfComposite = false,
            };
        }

        static void AddThumbstickYCompositeBindings(
            List<InputBindingJson> bindings,
            string mapName,
            string actionName,
            string controllerPath)
        {
            bindings.Add(new InputBindingJson
            {
                name = "1DAxis",
                id = BlockiverseDeterministicInputIds.ForBinding(mapName, actionName, "thumbstick-y-composite").ToString(),
                path = "1DAxis",
                interactions = string.Empty,
                processors = string.Empty,
                groups = string.Empty,
                action = actionName,
                isComposite = true,
                isPartOfComposite = false,
            });
            bindings.Add(new InputBindingJson
            {
                name = "Positive",
                id = BlockiverseDeterministicInputIds.ForBinding(mapName, actionName, "thumbstick-y-positive").ToString(),
                path = $"{controllerPath}/thumbstick/y",
                interactions = string.Empty,
                processors = string.Empty,
                groups = string.Empty,
                action = actionName,
                isComposite = false,
                isPartOfComposite = true,
            });
        }

        static void EnsureInputActionReferences(InputActionAsset asset)
        {
            foreach (InputActionMap map in asset.actionMaps)
            {
                foreach (InputAction action in map.actions)
                    EnsureInputActionReference(asset, map.name, action.name);
            }

            AssetDatabase.SaveAssets();
        }

        static InputActionReference EnsureInputActionReference(InputActionAsset asset, string mapName, string actionName)
        {
            string path = BlockiverseInputActionReferencePaths.GetReferencePath(mapName, actionName);
            InputActionReference reference = AssetDatabase.LoadAssetAtPath<InputActionReference>(path);

            if (reference == null)
            {
                reference = ScriptableObject.CreateInstance<InputActionReference>();
                AssetDatabase.CreateAsset(reference, path);
            }

            if (reference.action != asset.FindActionMap(mapName).FindAction(actionName))
            {
                reference.Set(asset, mapName, actionName);
                reference.name = $"{mapName}/{actionName}";
                EditorUtility.SetDirty(reference);
            }

            return reference;
        }

        static InputActionReference LoadInputActionReference(string mapName, string actionName)
        {
            return AssetDatabase.LoadAssetAtPath<InputActionReference>(
                BlockiverseInputActionReferencePaths.GetReferencePath(mapName, actionName));
        }

        static void EnsureInputActionSchema(InputActionAsset asset)
        {
            InputActionMap[] enabledMaps = asset.actionMaps
                .Where(actionMap => actionMap.enabled)
                .ToArray();

            foreach (InputActionMap enabledMap in enabledMaps)
                enabledMap.Disable();

            try
            {
                InputActionMap gameplayMap = asset.FindActionMap(BlockiverseInputActionNames.GameplayMap, throwIfNotFound: false);
                InputActionMap leftHandMap = asset.FindActionMap(BlockiverseInputActionNames.LeftHandMap, throwIfNotFound: false);
                InputActionMap rightHandMap = asset.FindActionMap(BlockiverseInputActionNames.RightHandMap, throwIfNotFound: false);

                if (leftHandMap == null)
                    AddControllerMap(asset, BlockiverseInputActionNames.LeftHandMap, "<XRController>{LeftHand}");
                else
                    EnsureControllerMapSchema(leftHandMap, "<XRController>{LeftHand}");

                if (rightHandMap == null)
                    AddControllerMap(asset, BlockiverseInputActionNames.RightHandMap, "<XRController>{RightHand}");
                else
                    EnsureControllerMapSchema(rightHandMap, "<XRController>{RightHand}");

                if (gameplayMap == null)
                {
                    AddGameplayMap(asset);
                    EditorUtility.SetDirty(asset);
                    return;
                }

                EnsureButtonAction(
                    gameplayMap,
                    BlockiverseInputActionNames.Menu,
                    "<XRController>{LeftHand}/menuButton");
                RemoveAction(gameplayMap, BlockiverseInputActionNames.Jump);
                RemoveAction(gameplayMap, BlockiverseInputActionNames.BlockEditingToggle);
                RemoveAction(gameplayMap, BlockiverseInputActionNames.Sprint);
                RemoveAction(gameplayMap, BlockiverseInputActionNames.Undo);
                RemoveActionBinding(gameplayMap, BlockiverseInputActionNames.HeightReset, "<XRController>{LeftHand}/primaryButton");
                EditorUtility.SetDirty(asset);
            }
            finally
            {
                foreach (InputActionMap enabledMap in enabledMaps)
                    enabledMap.Enable();
            }
        }

        static void RemoveAction(InputActionMap map, string actionName)
        {
            InputAction action = map?.FindAction(actionName, throwIfNotFound: false);
            action?.RemoveAction();
        }

        static void EnsureButtonAction(InputActionMap map, string actionName, string bindingPath)
        {
            InputAction action = map.FindAction(actionName, throwIfNotFound: false);

            if (action == null)
                action = map.AddAction(actionName, InputActionType.Button, bindingPath);

            bool hasBinding = action.bindings.Any(binding => binding.path == bindingPath);

            if (!hasBinding)
                action.AddBinding(bindingPath);
        }

        static void EnsureControllerMapSchema(InputActionMap map, string controllerPath)
        {
            RemoveAction(map, BlockiverseInputActionNames.Jump);
            EnsureButtonAction(map, BlockiverseInputActionNames.PrimaryButton, $"{controllerPath}/primaryButton");
            EnsureButtonAction(map, BlockiverseInputActionNames.SecondaryButton, $"{controllerPath}/secondaryButton");
            EnsureButtonAction(map, BlockiverseInputActionNames.Sprint, $"{controllerPath}/thumbstickClicked");
            EnsureButtonAction(map, BlockiverseInputActionNames.Crouch, $"{controllerPath}/thumbstickClicked");

            EnsureThumbstickVector2Action(map, BlockiverseInputActionNames.Move, controllerPath);
            EnsureThumbstickVector2Action(map, BlockiverseInputActionNames.Turn, controllerPath);
            EnsureThumbstickYAction(map, BlockiverseInputActionNames.TeleportMode, controllerPath);
            EnsureThumbstickYAction(map, BlockiverseInputActionNames.TeleportSelect, controllerPath);
            RemoveActionBindingsContaining(map, BlockiverseInputActionNames.TeleportMode, "primaryButton", "triggerPressed");
            RemoveActionBindingsContaining(map, BlockiverseInputActionNames.TeleportSelect, "primaryButton", "triggerPressed");
        }

        static void EnsureThumbstickVector2Action(InputActionMap map, string actionName, string controllerPath)
        {
            InputAction action = map.FindAction(actionName, throwIfNotFound: false);

            if (action == null)
                action = map.AddAction(actionName, InputActionType.PassThrough, expectedControlLayout: "Vector2");

            string thumbstickPath = $"{controllerPath}/thumbstick";
            bool hasDeadzonedThumbstick = false;

            for (int index = action.bindings.Count - 1; index >= 0; index--)
            {
                InputBinding binding = action.bindings[index];
                if (binding.path != thumbstickPath)
                    continue;

                if (binding.processors == StickDeadzoneProcessor)
                {
                    hasDeadzonedThumbstick = true;
                    continue;
                }

                action.ChangeBinding(index).Erase();
            }

            if (!hasDeadzonedThumbstick)
                action.AddBinding(thumbstickPath, processors: StickDeadzoneProcessor);
        }

        static void EnsureThumbstickYAction(InputActionMap map, string actionName, string controllerPath)
        {
            InputAction action = map.FindAction(actionName, throwIfNotFound: false);

            if (action == null)
                action = map.AddAction(actionName, InputActionType.Button);

            string thumbstickPath = $"{controllerPath}/thumbstick/y";
            bool hasThumbstickY = action.bindings.Any(binding => binding.path == thumbstickPath);

            if (!hasThumbstickY)
                AddThumbstickYComposite(action, controllerPath);
        }

        static void AddThumbstickYComposite(InputAction action, string controllerPath)
        {
            action.AddCompositeBinding("1DAxis")
                .With("Positive", $"{controllerPath}/thumbstick/y");
        }

        [Serializable]
        sealed class InputActionAssetJson
        {
            public int version;
            public string name;
            public InputActionMapJson[] maps;
            public InputControlSchemeJson[] controlSchemes;
        }

        [Serializable]
        sealed class InputActionMapJson
        {
            public string name;
            public string id;
            public InputActionJson[] actions;
            public InputBindingJson[] bindings;
        }

        [Serializable]
        sealed class InputActionJson
        {
            public string name;
            public string type;
            public string id;
            public string expectedControlType;
            public string processors;
            public string interactions;
            public bool initialStateCheck;
        }

        [Serializable]
        sealed class InputBindingJson
        {
            public string name;
            public string id;
            public string path;
            public string interactions;
            public string processors;
            public string groups;
            public string action;
            public bool isComposite;
            public bool isPartOfComposite;
        }

        [Serializable]
        sealed class InputControlSchemeJson
        {
            public string name;
            public string bindingGroup;
            public string[] devices;
        }

        // Import TextMeshPro Essential Resources once so the default font asset is available for
        // procedurally-created TMP labels. The package lives in com.unity.ugui's Package Resources
        // folder. If TMP Settings are already present the import is skipped.
    }
}
