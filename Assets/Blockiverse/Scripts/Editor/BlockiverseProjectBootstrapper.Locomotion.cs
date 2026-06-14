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
        static void EnsureXrRigLocomotion(GameObject rig, BlockiverseInputRig inputRig, XROrigin origin)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(rig);

            BlockiverseComfortSettings settings = rig.GetComponent<BlockiverseComfortSettings>();

            if (settings == null)
                settings = rig.AddComponent<BlockiverseComfortSettings>();

            // Regenerated rigs should always start with a readable title/menu view. Players can
            // opt into motion tunneling from the comfort menu after startup.
            settings.VignetteEnabled = false;
            settings.VignetteStrength = 0.0f;

            if (origin != null)
                origin.CameraYOffset = settings.StandingEyeHeight;

            // Collision capsule so gravity/jumping land on the voxel terrain. Added before the body
            // transformer so it auto-binds a CharacterControllerBodyManipulator when it initializes.
            CharacterController characterController = rig.GetComponent<CharacterController>();

            if (characterController == null)
                characterController = rig.AddComponent<CharacterController>();

            BlockiverseInputRig.ConfigureCharacterController(characterController);

            XRBodyTransformer bodyTransformer = rig.GetComponent<XRBodyTransformer>();

            if (bodyTransformer == null)
                bodyTransformer = rig.AddComponent<XRBodyTransformer>();

            bodyTransformer.xrOrigin = origin;

            LocomotionMediator mediator = rig.GetComponent<LocomotionMediator>();

            if (mediator == null)
                mediator = rig.AddComponent<LocomotionMediator>();

            // Gravity must exist before Jump: JumpProvider disables itself in Awake without a GravityProvider.
            GravityProvider gravityProvider = rig.GetComponent<GravityProvider>();

            if (gravityProvider == null)
                gravityProvider = rig.AddComponent<GravityProvider>();

            gravityProvider.mediator = mediator;
            gravityProvider.enabled = true;
            gravityProvider.useGravity = true;
            gravityProvider.useLocalSpaceGravity = true;
            gravityProvider.sphereCastLayerMask = GetInteractionLayerMask();
            gravityProvider.sphereCastTriggerInteraction = QueryTriggerInteraction.Ignore;

            JumpProvider jumpProvider = rig.GetComponent<JumpProvider>();

            if (jumpProvider == null)
                jumpProvider = rig.AddComponent<JumpProvider>();

            jumpProvider.mediator = mediator;
            jumpProvider.jumpHeight = JumpHeightMeters;
            jumpProvider.disableGravityDuringJump = false;
            jumpProvider.unlimitedInAirJumps = false;
            jumpProvider.inAirJumpCount = 0;

            TeleportationProvider teleport = rig.GetComponent<TeleportationProvider>();

            if (teleport == null)
                teleport = rig.AddComponent<TeleportationProvider>();

            teleport.mediator = mediator;
            teleport.delayTime = 0.0f;

            ContinuousMoveProvider continuousMove = rig.GetComponent<ContinuousMoveProvider>();

            if (continuousMove == null)
                continuousMove = rig.AddComponent<ContinuousMoveProvider>();

            continuousMove.mediator = mediator;
            continuousMove.forwardSource = origin != null && origin.Camera != null ? origin.Camera.transform : rig.transform;
            continuousMove.enableStrafe = true;
            continuousMove.enableFly = false;
            continuousMove.moveSpeed = settings.ContinuousMoveSpeed;

            SnapTurnProvider snapTurn = rig.GetComponent<SnapTurnProvider>();

            if (snapTurn == null)
                snapTurn = rig.AddComponent<SnapTurnProvider>();

            snapTurn.mediator = mediator;
            snapTurn.turnAmount = settings.SnapTurnDegrees;
            snapTurn.enableTurnLeftRight = true;
            snapTurn.enableTurnAround = settings.SnapTurnAroundEnabled;
            snapTurn.delayTime = 0.0f;

            ContinuousTurnProvider continuousTurn = rig.GetComponent<ContinuousTurnProvider>();

            if (continuousTurn == null)
                continuousTurn = rig.AddComponent<ContinuousTurnProvider>();

            continuousTurn.mediator = mediator;

            BlockiverseHeightReset heightReset = rig.GetComponent<BlockiverseHeightReset>();

            if (heightReset == null)
                heightReset = rig.AddComponent<BlockiverseHeightReset>();

            heightReset.Configure(origin, settings);
            inputRig.ConfigureLocomotion(teleport, snapTurn, heightReset, continuousMove, mediator, bodyTransformer, settings, continuousTurn, gravityProvider, jumpProvider, characterController);

            BlockiverseAudioCuePlayer audioCuePlayer = rig.GetComponent<BlockiverseAudioCuePlayer>();
            inputRig.ConfigureTeleportFeedback(audioCuePlayer);
        }

        // ── Game menu system ─────────────────────────────────────────────────────────────────────
    }
}
