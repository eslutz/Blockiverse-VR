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
        static void EnsureXrRigGameMenus(GameObject rig, BlockiverseInputRig inputRig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");

            if (cameraOffset == null)
                return;

            BlockiverseStationInteractionState stationInteractionState = EnsureStationMenuState(cameraOffset);
            BlockiverseCreativeToolsInteractionState creativeToolsInteractionState = EnsureCreativeToolsState(cameraOffset);
            BlockiverseHudToolkitSurface gameplayHudSurface =
                cameraOffset.Find(SurvivalHudName)?.GetComponent<BlockiverseHudToolkitSurface>();

            BlockiverseMenuController controller = EnsureComponent<BlockiverseMenuController>(rig);
            controller.Configure(inputRig);
            controller.ConfigureUiToolkitMenuSurface(null);
            controller.ConfigureHudToolkitSurface(gameplayHudSurface);
            controller.ConfigureStationInteractionState(stationInteractionState);

            EditorUtility.SetDirty(creativeToolsInteractionState);

            // The session coordinator implements the menu's save/load/new-world/continue verbs.
            BlockiverseWorldSessionController sessionController = EnsureComponent<BlockiverseWorldSessionController>(rig);
            EditorUtility.SetDirty(sessionController);

            if (inputRig != null)
            {
                // The controller subscribes to MenuPressed at runtime (Start → AddListener), so a
                // persistent listener here would double-fire the pause toggle. Only scrub any stale
                // persistent listener a previous bootstrap left on the prefab.
                RemovePersistentListeners(inputRig.MenuPressed, controller, nameof(BlockiverseMenuController.OnMenuPressed));
                EditorUtility.SetDirty(inputRig);
            }

            EnsureSurvivalHudVrDisplay(cameraOffset);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(rig);
        }

        static BlockiverseStationInteractionState EnsureStationMenuState(Transform parent)
        {
            GameObject stateRoot = EnsureChild(parent, StationMenuStateName);
            stateRoot.transform.localPosition = Vector3.zero;
            stateRoot.transform.localRotation = Quaternion.identity;
            stateRoot.transform.localScale = Vector3.one;

            BlockiverseStationInteractionState stationInteractionState = EnsureComponent<BlockiverseStationInteractionState>(stateRoot);
            EditorUtility.SetDirty(stationInteractionState);
            EditorUtility.SetDirty(stateRoot);
            return stationInteractionState;
        }

        static BlockiverseCreativeToolsInteractionState EnsureCreativeToolsState(Transform parent)
        {
            GameObject stateRoot = EnsureChild(parent, CreativeToolsMenuStateName);
            stateRoot.transform.localPosition = Vector3.zero;
            stateRoot.transform.localRotation = Quaternion.identity;
            stateRoot.transform.localScale = Vector3.one;

            BlockiverseCreativeToolsInteractionState creativeToolsInteractionState = EnsureComponent<BlockiverseCreativeToolsInteractionState>(stateRoot);
            EditorUtility.SetDirty(creativeToolsInteractionState);
            EditorUtility.SetDirty(stateRoot);
            return creativeToolsInteractionState;
        }

        static void EnsureSurvivalHudVrDisplay(Transform cameraOffset)
        {
            if (cameraOffset == null)
                return;

            Transform panel = cameraOffset.Find(SurvivalHudName);
            if (panel == null)
                return;

            SetLayerRecursively(panel.gameObject, GetInteractionLayerIndex());
            panel.gameObject.SetActive(true);
            EditorUtility.SetDirty(panel.gameObject);
        }

    }
}
