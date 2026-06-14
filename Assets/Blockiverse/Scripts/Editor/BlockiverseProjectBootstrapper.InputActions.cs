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
            var existingAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                BlockiverseProject.InputActionsAssetPath);

            if (existingAsset != null)
            {
                EnsureInputActionSchema(existingAsset);
                File.WriteAllText(BlockiverseProject.InputActionsAssetPath, existingAsset.ToJson());
                AssetDatabase.ImportAsset(
                    BlockiverseProject.InputActionsAssetPath,
                    ImportAssetOptions.ForceSynchronousImport);

                return AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                    BlockiverseProject.InputActionsAssetPath) ?? existingAsset;
            }

            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            AddControllerMap(asset, BlockiverseInputActionNames.LeftHandMap, "<XRController>{LeftHand}");
            AddControllerMap(asset, BlockiverseInputActionNames.RightHandMap, "<XRController>{RightHand}");
            AddGameplayMap(asset);

            File.WriteAllText(BlockiverseProject.InputActionsAssetPath, asset.ToJson());
            UnityEngine.Object.DestroyImmediate(asset);

            AssetDatabase.ImportAsset(
                BlockiverseProject.InputActionsAssetPath,
                ImportAssetOptions.ForceSynchronousImport);

            var importedAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                BlockiverseProject.InputActionsAssetPath);

            if (importedAsset == null)
                throw new InvalidOperationException("Unable to create Blockiverse input actions asset.");

            return importedAsset;
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
                RemoveActionBinding(gameplayMap, BlockiverseInputActionNames.Jump, "<XRController>{LeftHand}/primaryButton");
                EnsureButtonAction(
                    gameplayMap,
                    BlockiverseInputActionNames.Jump,
                    "<XRController>{RightHand}/primaryButton");
                EnsureButtonAction(
                    gameplayMap,
                    BlockiverseInputActionNames.BlockEditingToggle,
                    "<XRController>{RightHand}/secondaryButton");
                EnsureButtonAction(
                    gameplayMap,
                    BlockiverseInputActionNames.Sprint,
                    "<XRController>{LeftHand}/thumbstickClicked");
                RemoveActionBinding(gameplayMap, BlockiverseInputActionNames.BlockEditingToggle, "<XRController>{LeftHand}/secondaryButton");
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
            if (controllerPath.Contains("{LeftHand}", StringComparison.Ordinal))
            {
                RemoveAction(map, BlockiverseInputActionNames.PrimaryButton);
                RemoveAction(map, BlockiverseInputActionNames.SecondaryButton);
            }
            else
            {
                EnsureButtonAction(map, BlockiverseInputActionNames.PrimaryButton, $"{controllerPath}/primaryButton");
                EnsureButtonAction(map, BlockiverseInputActionNames.SecondaryButton, $"{controllerPath}/secondaryButton");
            }

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

        // Import TextMeshPro Essential Resources once so the default font asset is available for
        // procedurally-created TMP labels. The package lives in com.unity.ugui's Package Resources
        // folder. If TMP Settings are already present the import is skipped.
    }
}
