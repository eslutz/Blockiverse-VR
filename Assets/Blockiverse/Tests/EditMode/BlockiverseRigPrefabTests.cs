using System.IO;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UIDocument = UnityEngine.UIElements.UIDocument;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseRigPrefabTests
    {
        const string ControllerRayOriginName = "Ray Origin";
        static readonly Quaternion ControllerRayOriginLocalRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);

        [Test]
        public void XrRigPrefabIsWiredForQuestControllerAnchorsAndHaptics()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<BlockiverseInputRig>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BlockiverseComfortTransition>(), Is.Not.Null);
            Assert.That(prefab.transform.Find("Camera Offset/Left Controller"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Camera Offset/Right Controller"), Is.Not.Null);

            BlockiverseControllerAnchor[] anchors = prefab
                .GetComponentsInChildren<BlockiverseControllerAnchor>(true);
            BlockiverseControllerHaptics[] haptics = prefab
                .GetComponentsInChildren<BlockiverseControllerHaptics>(true);

            Assert.That(anchors, Has.Length.EqualTo(2));
            Assert.That(haptics, Has.Length.EqualTo(2));
            Assert.That(anchors.Select(anchor => anchor.Role), Is.EquivalentTo(new[]
            {
                BlockiverseControllerRole.Left,
                BlockiverseControllerRole.Right
            }));
            Assert.That(haptics.Select(controllerHaptics => controllerHaptics.Role), Is.EquivalentTo(new[]
            {
                BlockiverseControllerRole.Left,
                BlockiverseControllerRole.Right
            }));

            AssertController(prefab, "Left Controller", BlockiverseControllerRole.Left);
            AssertController(prefab, "Right Controller", BlockiverseControllerRole.Right);
        }

        [Test]
        public void XrRigRuntimeRepairsHeadPoseDriverAndXriLocomotionProviders()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);

            try
            {
                BlockiverseInputRig inputRig = instance.GetComponent<BlockiverseInputRig>();
                inputRig?.RepairRuntimeTracking();

                XRBodyTransformer bodyTransformer = instance.GetComponent<XRBodyTransformer>();
                LocomotionMediator mediator = instance.GetComponent<LocomotionMediator>();
                ContinuousMoveProvider continuousMove = instance.GetComponent<ContinuousMoveProvider>();
                SnapTurnProvider snapTurn = instance.GetComponent<SnapTurnProvider>();
                TeleportationProvider teleport = instance.GetComponent<TeleportationProvider>();
                ContinuousTurnProvider continuousTurn = instance.GetComponent<ContinuousTurnProvider>();
                GravityProvider gravityProvider = instance.GetComponent<GravityProvider>();
                JumpProvider jumpProvider = instance.GetComponent<JumpProvider>();
                CharacterController characterController = instance.GetComponent<CharacterController>();
                BlockiverseComfortTransition comfortTransition = instance.GetComponent<BlockiverseComfortTransition>();
                TrackedPoseDriver poseDriver = inputRig?.HeadPoseDriver;
                BlockiverseFoveatedRenderingController foveatedRenderingController =
                    inputRig?.FoveatedRenderingController;

                Assert.That(inputRig, Is.Not.Null);
                Assert.That(bodyTransformer, Is.Not.Null);
                Assert.That(mediator, Is.Not.Null);
                Assert.That(continuousMove, Is.Not.Null);
                Assert.That(snapTurn, Is.Not.Null);
                Assert.That(teleport, Is.Not.Null);
                Assert.That(inputRig.BodyTransformer, Is.SameAs(bodyTransformer));
                Assert.That(inputRig.LocomotionMediator, Is.SameAs(mediator));
                Assert.That(inputRig.ContinuousMoveProvider, Is.SameAs(continuousMove));
                Assert.That(inputRig.SnapTurnProvider, Is.SameAs(snapTurn));
                Assert.That(inputRig.TeleportationProvider, Is.SameAs(teleport));
                Assert.That(continuousMove.mediator, Is.SameAs(mediator));
                Assert.That(snapTurn.mediator, Is.SameAs(mediator));
                Assert.That(snapTurn.enableTurnAround, Is.True, "Comfort defaults must expose the stick-down 180 degree turn-around option.");
                Assert.That(teleport.mediator, Is.SameAs(mediator));
                Assert.That(continuousTurn, Is.Not.Null);
                Assert.That(inputRig.ContinuousTurnProvider, Is.SameAs(continuousTurn));
                Assert.That(continuousTurn.mediator, Is.SameAs(mediator));
                // Gravity + physics-based jumping: CharacterController gives the player a
                // collision capsule, GravityProvider applies gravity via sphere-cast grounding,
                // and JumpProvider drives kinematic jump arcs with coyote time.
                Assert.That(characterController, Is.Not.Null, "Rig must have a CharacterController for physics-based locomotion.");
                Assert.That(gravityProvider, Is.Not.Null, "Rig must have a GravityProvider for falling off edges.");
                Assert.That(jumpProvider, Is.Not.Null, "Rig must have a JumpProvider for jumping.");
                Assert.That(comfortTransition, Is.Not.Null, "Rig repair must preserve the comfort fade transition for load and respawn jumps.");
                Assert.That(inputRig.CharacterController, Is.SameAs(characterController));
                Assert.That(inputRig.GravityProvider, Is.SameAs(gravityProvider));
                Assert.That(inputRig.JumpProvider, Is.SameAs(jumpProvider));
                Assert.That(foveatedRenderingController, Is.Not.Null, "Rig repair must enable fixed foveated rendering for Quest builds.");
                Assert.That(
                    foveatedRenderingController.FoveatedRenderingLevel,
                    Is.EqualTo(BlockiverseFoveatedRenderingController.DefaultFoveatedRenderingLevel));
                Assert.That(gravityProvider.mediator, Is.SameAs(mediator));
                Assert.That(gravityProvider.enabled, Is.True);
                Assert.That(gravityProvider.useGravity, Is.True);
                Assert.That(gravityProvider.useLocalSpaceGravity, Is.True);
                AssertGravityUsesVoxelTerrainMask(gravityProvider);
                Assert.That(jumpProvider.mediator, Is.SameAs(mediator));
                Assert.That(jumpProvider.disableGravityDuringJump, Is.False);
                Assert.That(jumpProvider.inAirJumpCount, Is.EqualTo(0));
                AssertJumpProviderUsesDominantPrimary(inputRig, jumpProvider);
                Assert.That(poseDriver, Is.Not.Null);
                Assert.That(poseDriver.enabled, Is.True);
                Assert.That(poseDriver.updateType, Is.EqualTo(TrackedPoseDriver.UpdateType.UpdateAndBeforeRender));
                Assert.That(poseDriver.trackingType, Is.EqualTo(TrackedPoseDriver.TrackingType.RotationAndPosition));
                AssertBinding(poseDriver.positionInput, "<XRHMD>/centerEyePosition");
                AssertBinding(poseDriver.rotationInput, "<XRHMD>/centerEyeRotation");
                AssertBinding(poseDriver.trackingStateInput, "<XRHMD>/trackingState");

                AssertControllerPoseDriver(instance, "Left Controller", "<XRController>{LeftHand}/devicePosition", "<XRController>{LeftHand}/deviceRotation");
                AssertControllerPoseDriver(instance, "Right Controller", "<XRController>{RightHand}/devicePosition", "<XRController>{RightHand}/deviceRotation");
            }
            finally
            {
                DestroyImmediateIgnoringUnityCleanupLogs(instance);
            }
        }

        [Test]
        public void XrRigPrefabUsesUiToolkitMenusWithoutGeneratedComfortSurface()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            BlockiverseInputRig inputRig = prefab.GetComponent<BlockiverseInputRig>();
            XROrigin origin = prefab.GetComponent<XROrigin>();
            BlockiverseComfortSettings settings = prefab.GetComponent<BlockiverseComfortSettings>();
            BlockiverseDominantHandResolver dominantHandResolver = prefab.GetComponent<BlockiverseDominantHandResolver>();

            Assert.That(inputRig, Is.Not.Null);
            Assert.That(origin, Is.Not.Null);
            Assert.That(settings, Is.Not.Null);
            Assert.That(dominantHandResolver, Is.Not.Null);
            Assert.That(settings.VignetteEnabled, Is.False, "Generated rig should not start with the motion vignette enabled over the title/menu.");
            Assert.That(settings.VignetteStrength, Is.EqualTo(0.0f).Within(0.001f), "Generated rig should start with a fully open vignette strength.");
            Assert.That(settings.VignetteAperture, Is.EqualTo(1.0f).Within(0.001f), "Generated rig should leave the title/menu view unobscured.");
            Assert.That(origin.CameraYOffset, Is.EqualTo(settings.StandingEyeHeight).Within(0.01f));
            Assert.That(prefab.transform.Find("Camera Offset/UI Toolkit Menu Surface"), Is.Null,
                "The title/menu surface is a fixed Boot-scene menu-world object, not a child of the XR rig.");
            Assert.That(inputRig.MenuPressed.GetPersistentEventCount(), Is.EqualTo(0),
                "Hardware Menu is routed by BlockiverseMenuController at runtime; persistent comfort toggles double-handle pause/back.");
        }

        [Test]
        public void GeneratedUiToolkitMenuHasReadablePhysicalSize()
        {
            Scene scene = EditorSceneManager.OpenScene(BlockiverseProject.BootScenePath, OpenSceneMode.Single);
            GameObject menuRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Menu World UI");
            Transform surface = menuRoot != null ? menuRoot.transform.Find("UI Toolkit Menu Surface") : null;
            Assert.That(surface, Is.Not.Null, "Runtime UI Toolkit menu surface should be generated under the fixed menu-world root.");
            Assert.That(surface.localScale.x, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableTransformScale).Within(0.00001f),
                "UI Toolkit menu uses a pixel layout with a small world transform scale so it renders at a readable VR size.");
            Assert.That(surface.localScale.y, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableTransformScale).Within(0.00001f));
            Assert.That(surface.localScale.z, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableTransformScale).Within(0.00001f));

            UIDocument document = surface.GetComponent<UIDocument>();
            Assert.That(document, Is.Not.Null);
            Assert.That(document.worldSpaceSizeMode, Is.EqualTo(UIDocument.WorldSpaceSizeMode.Fixed));
            Assert.That(document.worldSpaceSize.x, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceSize.x).Within(0.001f),
                "UITK menu width should stay headset-readable without filling the player's view.");
            Assert.That(document.worldSpaceSize.y, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceSize.y).Within(0.001f),
                "UITK menu height should stay headset-readable without filling the player's view.");
            var serializedPanelSettings = new SerializedObject(document.panelSettings);
            SerializedProperty pixelsPerUnit = serializedPanelSettings.FindProperty("m_PixelsPerUnit");
            Assert.That(pixelsPerUnit, Is.Not.Null,
                "PanelSettings should expose the UI Toolkit pixels-per-unit conversion used for world-space rendering.");
            Assert.That(
                document.worldSpaceSize.x / pixelsPerUnit.floatValue * surface.localScale.x,
                Is.EqualTo(1.28f).Within(0.01f),
                "Rendered UI Toolkit geometry should be physically readable; a large collider alone does not make the visual panel readable.");
            Assert.That(
                document.worldSpaceSize.y / pixelsPerUnit.floatValue * surface.localScale.y,
                Is.EqualTo(0.72f).Within(0.01f));

            BlockiverseUiToolkitMenuPresenter presenter = surface.GetComponent<BlockiverseUiToolkitMenuPresenter>();
            Assert.That(presenter, Is.Not.Null);
            FieldInfo panelScaleField = typeof(BlockiverseUiToolkitMenuPresenter)
                .GetField("panelScale", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(panelScaleField, Is.Not.Null);
            Assert.That((float)panelScaleField.GetValue(presenter), Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableTransformScale).Within(0.00001f),
                "Presenter scale should match the fixed generated menu transform scale.");

            BoxCollider worldSpaceCollider = surface.GetComponent<BoxCollider>();
            Assert.That(worldSpaceCollider, Is.Not.Null,
                "UI Toolkit world-space menus need a collider so XR UI rays can hit the document instead of passing through it.");
            Assert.That(worldSpaceCollider.isTrigger, Is.True);
            Assert.That(worldSpaceCollider.size.x, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize.x).Within(0.001f));
            Assert.That(worldSpaceCollider.size.y, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize.y).Within(0.001f));
            Assert.That(worldSpaceCollider.size.x * surface.localScale.x, Is.EqualTo(1.28f).Within(0.01f),
                "UI Toolkit menu should be large enough to read in headset without returning to the previous oversized panel.");
            Assert.That(worldSpaceCollider.size.y * surface.localScale.y, Is.EqualTo(0.72f).Within(0.01f));

            var serializedDocument = new SerializedObject(document);
            SerializedProperty colliderProperty = serializedDocument.FindProperty("m_WorldSpaceCollider");
            Assert.That(colliderProperty, Is.Not.Null,
                "UIDocument should expose the serialized world-space collider reference.");
            Assert.That(colliderProperty.objectReferenceValue, Is.SameAs(worldSpaceCollider),
                "UIDocument must be wired to the generated collider for UI Toolkit XR interaction.");
        }

        [Test]
        public void XrRigPrefabHasComfortTunnelingVignetteWiredToLocomotion()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            TunnelingVignetteController vignette = prefab.GetComponentInChildren<TunnelingVignetteController>(true);
            MeshRenderer vignetteRenderer = vignette != null ? vignette.GetComponent<MeshRenderer>() : null;

            Assert.That(vignette, Is.Not.Null, "Rig should carry a TunnelingVignetteController for comfort.");
            Assert.That(vignette.GetComponent<MeshFilter>()?.sharedMesh, Is.Not.Null, "Vignette mesh must be imported from the sample.");
            Assert.That(vignetteRenderer?.sharedMaterial, Is.Not.Null, "Vignette material must be assigned.");
            Assert.That(vignetteRenderer.enabled, Is.False, "Disabled comfort vignette must not render over the menu before locomotion.");
            Assert.That(vignette.defaultParameters.apertureSize, Is.EqualTo(1.0f).Within(0.001f), "Startup vignette aperture should be fully open until intentional locomotion.");
            Assert.That(
                vignette.locomotionVignetteProviders,
                Has.All.Matches<LocomotionVignetteProvider>(p => p.enabled && p.locomotionProvider != null),
                "Every vignette provider must be enabled and reference a locomotion provider.");

            var providerTypes = vignette.locomotionVignetteProviders
                .Select(provider => provider.locomotionProvider.GetType())
                .ToList();

            // Continuous motions and teleport mask intentional vection/viewpoint jumps. Snap turn,
            // gravity, and jump are excluded so the vignette does not close while the title/menu rig
            // is idle, settling, or performing a discrete comfort turn.
            Assert.That(providerTypes, Has.Count.GreaterThanOrEqualTo(3));
            Assert.That(providerTypes, Contains.Item(typeof(ContinuousMoveProvider)));
            Assert.That(providerTypes, Contains.Item(typeof(ContinuousTurnProvider)));
            Assert.That(providerTypes, Contains.Item(typeof(TeleportationProvider)));
            Assert.That(providerTypes, Has.No.Member(typeof(SnapTurnProvider)));
            Assert.That(providerTypes, Has.No.Member(typeof(GravityProvider)));
            Assert.That(providerTypes, Has.No.Member(typeof(JumpProvider)));
        }

        [Test]
        public void XrRigPrefabShowsFallbackHandsUntilLocalMetaAvatarIsReady()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);

            try
            {
                BlockiverseNetworkAvatarRig avatarRig = instance.GetComponent<BlockiverseNetworkAvatarRig>();

                Assert.That(avatarRig, Is.Not.Null);
                Assert.That(avatarRig.FirstPersonFallbackVisualsEnabled, Is.True);

                avatarRig.SetMetaAvatarAvailable(false);
                avatarRig.RefreshAvatarMode();

                Renderer[] renderers = avatarRig.FallbackRoot.GetComponentsInChildren<Renderer>(includeInactive: true);

                Assert.That(renderers, Has.Some.Matches<Renderer>(renderer =>
                    renderer.transform.name == "Fallback Left Hand" && renderer.enabled));
                Assert.That(renderers, Has.Some.Matches<Renderer>(renderer =>
                    renderer.transform.name == "Fallback Right Hand" && renderer.enabled));
                Assert.That(renderers, Has.None.Matches<Renderer>(renderer =>
                    renderer.transform.name == "Fallback Head" && renderer.enabled));
                Assert.That(renderers, Has.None.Matches<Renderer>(renderer =>
                    renderer.transform.name == "Fallback Body" && renderer.enabled));
            }
            finally
            {
                DestroyImmediateIgnoringUnityCleanupLogs(instance);
            }
        }

        [Test]
        public void CreativeInputBridgeKeepsWorldSpaceMenuRayAtGameplayLength()
        {
            GameObject root = new("Menu Ray Test");

            try
            {
                BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false);

                GameObject rayObject = new("Interaction Ray");
                rayObject.transform.SetParent(root.transform, worldPositionStays: false);
                XRRayInteractor ray = rayObject.AddComponent<XRRayInteractor>();
                LineRenderer lineRenderer = rayObject.AddComponent<LineRenderer>();
                XRInteractorLineVisual lineVisual = rayObject.AddComponent<XRInteractorLineVisual>();
                CreativeInteractionController interactionController = root.AddComponent<CreativeInteractionController>();
                BlockiverseCreativeInputBridge bridge = root.AddComponent<BlockiverseCreativeInputBridge>();

                interactionController.SetBlockEditingEnabled(false);
                lineRenderer.enabled = true;
                lineVisual.enabled = true;
                lineVisual.overrideInteractorLineLength = false;
                lineVisual.lineLength = CreativeInteractionController.MaxBlockInteractionReachMeters;

                bridge.Configure(null, ray, interactionController);

                Assert.That(lineRenderer.enabled, Is.True, "Menus need the ray visual even while world input is blocked.");
                Assert.That(lineVisual.enabled, Is.True, "Menus need the XRI line visual even while world input is blocked.");
                Assert.That(lineVisual.overrideInteractorLineLength, Is.False,
                    "World-space XR menus should keep the controller ray at its normal gameplay length.");
                Assert.That(lineVisual.lineLength, Is.EqualTo(CreativeInteractionController.MaxBlockInteractionReachMeters).Within(0.001f));
            }
            finally
            {
                BlockiverseRuntimeState.Reset();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void XrRigBootstrapperLeavesFallAndJumpOutOfVignetteProviders()
        {
            string source = File.ReadAllText("Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.XrRig.cs");

            Assert.That(
                source,
                Does.Not.Contain("AddVignetteProvider(controller, rig.GetComponent<GravityProvider>())"),
                "Gravity can become active during startup grounding, so it must not close the comfort vignette while idle.");
            Assert.That(
                source,
                Does.Not.Contain("AddVignetteProvider(controller, rig.GetComponent<JumpProvider>())"),
                "Jump arcs are discrete gameplay movement and should not be a startup/menu vignette trigger.");
        }

        [Test]
        public void InputRigSuppressesTurnWhileToolHandRayHoversUi()
        {
            string source = File.ReadAllText("Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs");

            StringAssert.Contains("UpdateTurnProviderEnabledState()", source);
            StringAssert.Contains("IsActiveTurnRayOverUi()", source);
            StringAssert.Contains("GetToolHand()", source);
            StringAssert.Contains("IsOverUIGameObject()", source);
            StringAssert.Contains("!smoothTurn && !suppressTurnForUi", source);
            StringAssert.Contains("smoothTurn && !suppressTurnForUi", source);
        }

        [Test]
        public void XrRigPrefabHasJumpGravityAndCharacterControllerForCollision()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            JumpProvider jump = prefab.GetComponent<JumpProvider>();
            GravityProvider gravity = prefab.GetComponent<GravityProvider>();
            CharacterController cc = prefab.GetComponentInChildren<CharacterController>(true);

            Assert.That(jump, Is.Not.Null, "Rig should carry a JumpProvider.");
            Assert.That(gravity, Is.Not.Null, "Rig should carry a GravityProvider.");
            Assert.That(cc, Is.Not.Null, "Rig origin should carry a CharacterController for voxel collision.");
            Assert.That(gravity.enabled, Is.True, "Gravity must remain enabled so the player falls after jumping or walking off edges.");
            Assert.That(gravity.useGravity, Is.True, "Gravity must apply to the XR Origin.");
            Assert.That(gravity.useLocalSpaceGravity, Is.True, "Gravity should follow the XR Origin up direction.");
            AssertGravityUsesVoxelTerrainMask(gravity);
            Assert.That(jump.disableGravityDuringJump, Is.False, "Jump must not disable gravity and leave the player floating.");
            Assert.That(jump.inAirJumpCount, Is.EqualTo(0), "Only grounded jumps should be allowed for now.");
        }

        [Test]
        public void XrRigPrefabInputBindingsHaveJumpAndThumbstickUpTeleport()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            BlockiverseInputRig inputRig = prefab.GetComponent<BlockiverseInputRig>();

            Assert.That(inputRig, Is.Not.Null);
            Assert.That(inputRig.InputActions, Is.Not.Null);

            InputActionMap gameplayMap = inputRig.InputActions.FindActionMap(BlockiverseInputActionNames.GameplayMap, throwIfNotFound: false);
            Assert.That(gameplayMap, Is.Not.Null);

            Assert.That(gameplayMap.FindAction(BlockiverseInputActionNames.Jump, throwIfNotFound: false), Is.Null,
                "Jump should be resolved from the dominant controller map.");
            Assert.That(gameplayMap.FindAction(BlockiverseInputActionNames.BlockEditingToggle, throwIfNotFound: false), Is.Null,
                "Block editing toggle should be resolved from the dominant controller map.");
            Assert.That(gameplayMap.FindAction(BlockiverseInputActionNames.Sprint, throwIfNotFound: false), Is.Null,
                "Sprint should be resolved from the support controller map.");
            Assert.That(gameplayMap.FindAction(BlockiverseInputActionNames.Crouch, throwIfNotFound: false), Is.Null,
                "Crouch should be resolved from the dominant controller map.");
            Assert.That(gameplayMap.FindAction(BlockiverseInputActionNames.Undo, throwIfNotFound: false), Is.Null,
                "Undo must not have a controller button.");

            // Teleport Mode on both hands must be thumbstick-based, not a hardware button.
            foreach (string mapName in new[] { BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.RightHandMap })
            {
                InputActionMap map = inputRig.InputActions.FindActionMap(mapName, throwIfNotFound: false);
                Assert.That(map, Is.Not.Null, $"{mapName} must exist.");
                string handUsage = mapName == BlockiverseInputActionNames.LeftHandMap ? "LeftHand" : "RightHand";
                AssertControllerRoleActions(map, handUsage);
                Assert.That(map.FindAction(BlockiverseInputActionNames.Jump, throwIfNotFound: false), Is.Null,
                    $"{mapName}/Jump is stale; JumpProvider should read the dominant controller primary button.");
                InputAction teleportMode = map.FindAction(BlockiverseInputActionNames.TeleportMode, throwIfNotFound: false);
                InputAction teleportSelect = map.FindAction(BlockiverseInputActionNames.TeleportSelect, throwIfNotFound: false);
                Assert.That(teleportMode, Is.Not.Null, $"Teleport Mode must exist in {mapName}.");
                Assert.That(teleportSelect, Is.Not.Null, $"Teleport Select must exist in {mapName}.");
                Assert.That(teleportMode.bindings, Has.Some.Matches<InputBinding>(b =>
                    (b.effectivePath ?? b.path ?? "").Contains("thumbstick")),
                    $"Teleport Mode in {mapName} should be bound to thumbstick.");
                Assert.That(teleportSelect.bindings, Has.Some.Matches<InputBinding>(b =>
                    (b.effectivePath ?? b.path ?? "").Contains("thumbstick")),
                    $"Teleport Select in {mapName} should be bound to thumbstick.");
                Assert.That(teleportMode.bindings, Has.None.Matches<InputBinding>(b =>
                    (b.effectivePath ?? b.path ?? "").Contains("primaryButton") ||
                    (b.effectivePath ?? b.path ?? "").Contains("triggerPressed")),
                    $"Teleport Mode in {mapName} must not use trigger or A button.");
                Assert.That(teleportSelect.bindings, Has.None.Matches<InputBinding>(b =>
                    (b.effectivePath ?? b.path ?? "").Contains("primaryButton") ||
                    (b.effectivePath ?? b.path ?? "").Contains("triggerPressed")),
                    $"Teleport Select in {mapName} must not use trigger or A button.");
            }
        }

        [Test]
        public void XrRigRuntimeRepairsEveryRayInteractorInput()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);

            try
            {
                BlockiverseInputRig inputRig = instance.GetComponent<BlockiverseInputRig>();

                Assert.That(inputRig, Is.Not.Null);
                inputRig.RepairRuntimeTracking();

                InputAction rightUiPress = inputRig.InputActions
                    .FindActionMap(BlockiverseInputActionNames.RightHandMap)
                    .FindAction(BlockiverseInputActionNames.UiPress);
                InputAction leftUiPress = inputRig.InputActions
                    .FindActionMap(BlockiverseInputActionNames.LeftHandMap)
                    .FindAction(BlockiverseInputActionNames.UiPress);
                InputAction rightTeleportSelect = inputRig.InputActions
                    .FindActionMap(BlockiverseInputActionNames.RightHandMap)
                    .FindAction(BlockiverseInputActionNames.TeleportSelect);
                InputAction leftTeleportSelect = inputRig.InputActions
                    .FindActionMap(BlockiverseInputActionNames.LeftHandMap)
                    .FindAction(BlockiverseInputActionNames.TeleportSelect);

                XRRayInteractor rightInteractionRay = instance.transform
                    .Find("Camera Offset/Right Controller/Interaction Ray")
                    ?.GetComponent<XRRayInteractor>();
                XRRayInteractor rightTeleportRay = instance.transform
                    .Find("Camera Offset/Right Controller/Teleport Ray")
                    ?.GetComponent<XRRayInteractor>();
                XRRayInteractor leftInteractionRay = instance.transform
                    .Find("Camera Offset/Left Controller/Interaction Ray")
                    ?.GetComponent<XRRayInteractor>();
                XRRayInteractor leftTeleportRay = instance.transform
                    .Find("Camera Offset/Left Controller/Teleport Ray")
                    ?.GetComponent<XRRayInteractor>();
                Transform rightController = instance.transform.Find("Camera Offset/Right Controller");
                Transform leftController = instance.transform.Find("Camera Offset/Left Controller");
                Transform rightRayOrigin = instance.transform.Find("Camera Offset/Right Ray Origin");
                Transform leftRayOrigin = instance.transform.Find("Camera Offset/Left Ray Origin");

                XRInteractorLineVisual rightInteractionLineVisual = rightInteractionRay?.GetComponent<XRInteractorLineVisual>();
                XRInteractorLineVisual rightTeleportLineVisual = rightTeleportRay?.GetComponent<XRInteractorLineVisual>();
                XRInteractorLineVisual leftInteractionLineVisual = leftInteractionRay?.GetComponent<XRInteractorLineVisual>();
                XRInteractorLineVisual leftTeleportLineVisual = leftTeleportRay?.GetComponent<XRInteractorLineVisual>();

                Assert.That(rightInteractionRay, Is.Not.Null);
                Assert.That(rightTeleportRay, Is.Not.Null);
                Assert.That(leftInteractionRay, Is.Not.Null);
                Assert.That(leftTeleportRay, Is.Not.Null);
                Assert.That(rightInteractionLineVisual, Is.Not.Null);
                Assert.That(rightTeleportLineVisual, Is.Not.Null);
                Assert.That(leftInteractionLineVisual, Is.Not.Null);
                Assert.That(leftTeleportLineVisual, Is.Not.Null);
                Assert.That(rightController, Is.Not.Null);
                Assert.That(leftController, Is.Not.Null);
                Assert.That(rightRayOrigin, Is.Null,
                    "Runtime repair must remove stale pointer-pose ray origins that can diverge from the visible controller.");
                Assert.That(leftRayOrigin, Is.Null,
                    "Runtime repair must remove stale pointer-pose ray origins that can diverge from the visible controller.");

                rightInteractionLineVisual.overrideInteractorLineOrigin = true;
                rightInteractionLineVisual.lineOriginTransform = null;
                rightTeleportLineVisual.overrideInteractorLineOrigin = true;
                rightTeleportLineVisual.lineOriginTransform = null;
                leftInteractionLineVisual.overrideInteractorLineOrigin = true;
                leftInteractionLineVisual.lineOriginTransform = null;
                leftTeleportLineVisual.overrideInteractorLineOrigin = true;
                leftTeleportLineVisual.lineOriginTransform = null;

                inputRig.RepairRuntimeTracking();

                Transform rightControllerRayOrigin = rightController.Find(ControllerRayOriginName);
                Transform leftControllerRayOrigin = leftController.Find(ControllerRayOriginName);
                NearFarInteractor rightUiToolkitInteractor = rightController
                    .Find("UI Toolkit Ray Input")
                    ?.GetComponent<NearFarInteractor>();
                NearFarInteractor leftUiToolkitInteractor = leftController
                    .Find("UI Toolkit Ray Input")
                    ?.GetComponent<NearFarInteractor>();

                AssertControllerRayOrigin(rightController, rightControllerRayOrigin);
                AssertControllerRayOrigin(leftController, leftControllerRayOrigin);
                AssertButtonReaderReferencesAction(rightInteractionRay.uiPressInput, rightUiPress, "Right trigger must click UI through the right UI Press action.");
                AssertButtonReaderReferencesAction(leftInteractionRay.uiPressInput, leftUiPress, "Left trigger must click UI through the left UI Press action.");
                AssertInteractionRayDefaults(rightInteractionRay);
                AssertInteractionRayDefaults(leftInteractionRay);
                AssertUiToolkitNearFarInteractorDefaults(
                    rightUiToolkitInteractor,
                    rightControllerRayOrigin,
                    rightUiPress,
                    "Right UI Toolkit near/far interactor must drive world-space UI Toolkit menus.");
                AssertUiToolkitNearFarInteractorDefaults(
                    leftUiToolkitInteractor,
                    leftControllerRayOrigin,
                    leftUiPress,
                    "Left UI Toolkit near/far interactor must drive world-space UI Toolkit menus.");
                AssertButtonReaderReferencesAction(rightTeleportRay.selectInput, rightTeleportSelect, "Right teleport ray must use right thumbstick select.");
                AssertButtonReaderReferencesAction(leftTeleportRay.selectInput, leftTeleportSelect, "Left teleport ray must use left thumbstick select.");
                Assert.That(rightInteractionRay.rayOriginTransform, Is.SameAs(rightControllerRayOrigin));
                Assert.That(leftInteractionRay.rayOriginTransform, Is.SameAs(leftControllerRayOrigin));
                Assert.That(rightTeleportRay.rayOriginTransform, Is.SameAs(rightControllerRayOrigin));
                Assert.That(leftTeleportRay.rayOriginTransform, Is.SameAs(leftControllerRayOrigin));
                Assert.That(rightInteractionLineVisual.overrideInteractorLineOrigin, Is.False,
                    "Runtime repair should leave the ray interactor as the single visual origin source.");
                Assert.That(rightInteractionLineVisual.lineOriginTransform, Is.Null);
                Assert.That(rightTeleportLineVisual.overrideInteractorLineOrigin, Is.False);
                Assert.That(rightTeleportLineVisual.lineOriginTransform, Is.Null);
                Assert.That(leftInteractionLineVisual.overrideInteractorLineOrigin, Is.False);
                Assert.That(leftInteractionLineVisual.lineOriginTransform, Is.Null);
                Assert.That(leftTeleportLineVisual.overrideInteractorLineOrigin, Is.False);
                Assert.That(leftTeleportLineVisual.lineOriginTransform, Is.Null);
            }
            finally
            {
                DestroyImmediateIgnoringUnityCleanupLogs(instance);
            }
        }

        [Test]
        public void CreativeFlightControllerStartsGroundedAndTogglesFlightForCreativeWorlds()
        {
            var rigObject = new GameObject("Creative Flight Rig");
            var worldObject = new GameObject("Creative Flight World");

            try
            {
                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                var continuousMove = rigObject.AddComponent<ContinuousMoveProvider>();
                var gravity = rigObject.AddComponent<GravityProvider>();
                var jump = rigObject.AddComponent<JumpProvider>();
                inputRig.ConfigureLocomotion(
                    teleport: null,
                    snapTurn: null,
                    reset: null,
                    continuousMove: continuousMove,
                    gravity: gravity,
                    jump: jump);

                continuousMove.enabled = true;
                continuousMove.enableFly = false;
                continuousMove.moveSpeed = 1.0f;
                gravity.useGravity = true;
                jump.enabled = true;

                CreativeWorldManager worldManager = worldObject.AddComponent<CreativeWorldManager>();
                worldManager.SetGameMode(WorldGameMode.Creative);
                var flight = rigObject.AddComponent<BlockiverseCreativeFlightController>();
                flight.Configure(inputRig, worldManager);

                flight.ApplyFlightState();

                Assert.That(flight.IsFlightActive, Is.False, "Entering creative mode should not automatically enter flight mode.");
                Assert.That(continuousMove.enableFly, Is.False);
                Assert.That(continuousMove.enabled, Is.True);
                Assert.That(gravity.useGravity, Is.True);
                Assert.That(jump.enabled, Is.True);
                Assert.That(inputRig.TurnWithBothHands, Is.False);

                flight.SetFlightActive(true);

                Assert.That(flight.IsFlightActive, Is.True);
                Assert.That(continuousMove.enabled, Is.False, "Creative flight moves by holding the dominant primary button toward the dominant-hand aim pose, not by stick locomotion.");
                Assert.That(continuousMove.enableFly, Is.False);
                Assert.That(gravity.useGravity, Is.False);
                Assert.That(jump.enabled, Is.False);
                Assert.That(inputRig.TurnWithBothHands, Is.True, "Both sticks should keep turning available while the player is in creative flight.");

                flight.SetFlightActive(false);

                Assert.That(flight.IsFlightActive, Is.False);
                Assert.That(continuousMove.enableFly, Is.False);
                Assert.That(gravity.useGravity, Is.True);
                Assert.That(jump.enabled, Is.True);
                Assert.That(inputRig.TurnWithBothHands, Is.False);

                flight.SetFlightActive(true);

                Assert.That(flight.IsFlightActive, Is.True);
                Assert.That(continuousMove.enabled, Is.False);
                Assert.That(gravity.useGravity, Is.False);
                Assert.That(jump.enabled, Is.False);

                worldManager.SetGameMode(WorldGameMode.Survival);
                flight.ApplyFlightState();

                Assert.That(flight.IsFlightActive, Is.False);
                Assert.That(continuousMove.enableFly, Is.False);
                Assert.That(gravity.useGravity, Is.True);
                Assert.That(jump.enabled, Is.True);
                Assert.That(inputRig.TurnWithBothHands, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(worldObject);
            }
        }

        [Test]
        public void MenuInputDoesNotSuppressXriLocomotionProviders()
        {
            var rigObject = new GameObject("Menu Locomotion Rig");

            try
            {
                var settings = rigObject.AddComponent<BlockiverseComfortSettings>();
                settings.LocomotionMode = BlockiverseLocomotionMode.Glide;
                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                var continuousMove = rigObject.AddComponent<ContinuousMoveProvider>();
                var snapTurn = rigObject.AddComponent<SnapTurnProvider>();
                var gravity = rigObject.AddComponent<GravityProvider>();
                var jump = rigObject.AddComponent<JumpProvider>();

                inputRig.ConfigureLocomotion(
                    teleport: null,
                    snapTurn: snapTurn,
                    reset: null,
                    continuousMove: continuousMove,
                    settings: settings,
                    gravity: gravity,
                    jump: jump);

                inputRig.RefreshLocomotionProviderState();

                Assert.That(continuousMove.enabled, Is.True);
                Assert.That(gravity.enabled, Is.True);
                Assert.That(gravity.useGravity, Is.True);
                Assert.That(jump.enabled, Is.True);
                Assert.That(snapTurn.enabled, Is.True);

                BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false, menuInputActive: true);
                inputRig.RefreshLocomotionProviderState();

                Assert.That(continuousMove.enabled, Is.True,
                    "Menu world grounding, not menu route state, should keep the title player stable.");
                Assert.That(gravity.enabled, Is.True);
                Assert.That(gravity.useGravity, Is.True);
                Assert.That(jump.enabled, Is.True);
                Assert.That(snapTurn.enabled, Is.True);

                BlockiverseRuntimeState.Reset();
                inputRig.RefreshLocomotionProviderState();

                Assert.That(continuousMove.enabled, Is.True);
                Assert.That(gravity.enabled, Is.True);
                Assert.That(gravity.useGravity, Is.True);
                Assert.That(jump.enabled, Is.True);
                Assert.That(snapTurn.enabled, Is.True);
            }
            finally
            {
                BlockiverseRuntimeState.Reset();
                Object.DestroyImmediate(rigObject);
            }
        }

        [Test]
        public void XrRigPrefabDoesNotDefaultCreativeFlightOn()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            BlockiverseCreativeFlightController flight = prefab.GetComponent<BlockiverseCreativeFlightController>();

            Assert.That(flight, Is.Not.Null);
            Assert.That(flight.FlightEnabledDefault, Is.False, "Creative players should enter grounded locomotion until they explicitly toggle flight.");
        }

        [Test]
        public void SprintClickTogglesAndHoldTemporarilyRaisesMoveSpeed()
        {
            MethodInfo toggleMethod = typeof(BlockiverseInputRig).GetMethod(
                "ShouldToggleSprint",
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo speedMethod = typeof(BlockiverseInputRig).GetMethod(
                "ResolveSprintMoveSpeed",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(toggleMethod, Is.Not.Null, "Sprint should expose its click-vs-hold threshold for tests.");
            Assert.That(speedMethod, Is.Not.Null, "Sprint should expose move-speed scaling for tests.");
            Assert.That((bool)toggleMethod.Invoke(null, new object[] { 0.10f }), Is.True);
            Assert.That((bool)toggleMethod.Invoke(null, new object[] { 0.50f }), Is.False);
            Assert.That((float)speedMethod.Invoke(null, new object[] { 1.8f, false }), Is.EqualTo(1.8f).Within(0.001f));
            Assert.That((float)speedMethod.Invoke(null, new object[] { 1.8f, true }), Is.EqualTo(3.96f).Within(0.001f));
        }

        [Test]
        public void CreativeFlightMovementUsesAimOnlyWhileJumpHeld()
        {
            Vector3 displacement = BlockiverseCreativeFlightController.ComputeFlightDisplacement(
                new Vector3(0.0f, 0.25f, 1.0f),
                moveHeld: true,
                deltaSeconds: 0.5f);
            Vector3 expectedDirection = new Vector3(0.0f, 0.25f, 1.0f).normalized;

            Assert.That(displacement.normalized.x, Is.EqualTo(expectedDirection.x).Within(0.001f));
            Assert.That(displacement.normalized.y, Is.EqualTo(expectedDirection.y).Within(0.001f));
            Assert.That(displacement.normalized.z, Is.EqualTo(expectedDirection.z).Within(0.001f));
            Assert.That(displacement.magnitude, Is.EqualTo(BlockiverseCreativeFlightController.FlightSpeedBlocksPerSecond * 0.5f).Within(0.001f));

            Vector3 idle = BlockiverseCreativeFlightController.ComputeFlightDisplacement(Vector3.forward, moveHeld: false, deltaSeconds: 1.0f);

            Assert.That(idle, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void CreativeFlightMovementUsesDominantHandAim()
        {
            var rigObject = new GameObject("Creative Flight Dominant Rig");
            var settingsObject = new GameObject("Creative Flight Comfort Settings");

            try
            {
                Transform cameraOffset = new GameObject("Camera Offset").transform;
                cameraOffset.SetParent(rigObject.transform, false);

                Transform leftController = new GameObject("Left Controller").transform;
                leftController.SetParent(cameraOffset, false);
                leftController.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);

                Transform rightController = new GameObject("Right Controller").transform;
                rightController.SetParent(cameraOffset, false);
                rightController.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);

                var comfortSettings = settingsObject.AddComponent<BlockiverseComfortSettings>();
                var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.ConfigureLocomotion(
                    teleport: null,
                    snapTurn: null,
                    reset: null,
                    settings: comfortSettings);

                var flight = rigObject.AddComponent<BlockiverseCreativeFlightController>();
                flight.Configure(inputRig);

                MethodInfo resolveForward = typeof(BlockiverseCreativeFlightController).GetMethod(
                    "ResolveFlightForward",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(resolveForward, Is.Not.Null, "Creative flight should resolve travel direction from the current dominant controller.");

                comfortSettings.DominantHand = BlockiverseControllerRole.Left;
                Vector3 leftForward = (Vector3)resolveForward.Invoke(flight, null);

                comfortSettings.DominantHand = BlockiverseControllerRole.Right;
                Vector3 rightForward = (Vector3)resolveForward.Invoke(flight, null);

                Assert.That(leftForward.x, Is.EqualTo(-1.0f).Within(0.001f));
                Assert.That(rightForward.x, Is.EqualTo(1.0f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(settingsObject);
            }
        }

        [Test]
        public void CreativeFlightSprintUsesCanonicalSprintSpeed()
        {
            MethodInfo sprintFlightMethod = typeof(BlockiverseCreativeFlightController).GetMethod(
                nameof(BlockiverseCreativeFlightController.ComputeFlightDisplacement),
                new[] { typeof(Vector3), typeof(bool), typeof(bool), typeof(float) });

            Assert.That(sprintFlightMethod, Is.Not.Null, "Creative flight should accept sprint state when computing displacement.");

            Vector3 sprint = (Vector3)sprintFlightMethod.Invoke(null, new object[]
            {
                Vector3.forward,
                true,
                true,
                0.5f,
            });

            Assert.That(sprint.normalized, Is.EqualTo(Vector3.forward));
            Assert.That(sprint.magnitude, Is.EqualTo(2.2f).Within(0.001f));
        }

        [Test]
        public void XrRigRuntimeRepairReusesInputActionReferences()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);

            try
            {
                BlockiverseInputRig inputRig = instance.GetComponent<BlockiverseInputRig>();

                Assert.That(inputRig, Is.Not.Null);
                inputRig.RepairRuntimeTracking();

                XRRayInteractor rightInteractionRay = instance.transform
                    .Find("Camera Offset/Right Controller/Interaction Ray")
                    ?.GetComponent<XRRayInteractor>();
                XRRayInteractor rightTeleportRay = instance.transform
                    .Find("Camera Offset/Right Controller/Teleport Ray")
                    ?.GetComponent<XRRayInteractor>();
                Transform rightController = instance.transform.Find("Camera Offset/Right Controller");
                Transform rightRayOrigin = instance.transform.Find("Camera Offset/Right Ray Origin");
                Transform rightControllerRayOrigin = rightController?.Find(ControllerRayOriginName);

                Assert.That(inputRig.JumpProvider, Is.Not.Null);
                Assert.That(rightController, Is.Not.Null);
                Assert.That(rightRayOrigin, Is.Null,
                    "Runtime repair must remove stale pointer-pose ray origins that can diverge from the visible controller.");
                AssertControllerRayOrigin(rightController, rightControllerRayOrigin);
                Assert.That(rightInteractionRay, Is.Not.Null);
                Assert.That(rightTeleportRay, Is.Not.Null);
                Assert.That(rightInteractionRay.rayOriginTransform, Is.SameAs(rightControllerRayOrigin));
                Assert.That(rightTeleportRay.rayOriginTransform, Is.SameAs(rightControllerRayOrigin));

                InputActionReference jumpReference = inputRig.JumpProvider.jumpInput.inputActionReferencePerformed;
                InputActionReference uiPressReference = rightInteractionRay.uiPressInput.inputActionReferencePerformed;
                InputActionReference teleportReference = rightTeleportRay.selectInput.inputActionReferencePerformed;

                Assert.That(jumpReference, Is.Not.Null);
                Assert.That(uiPressReference, Is.Not.Null);
                Assert.That(teleportReference, Is.Not.Null);

                inputRig.RepairRuntimeTracking();

                Assert.That(inputRig.JumpProvider.jumpInput.inputActionReferencePerformed,
                    Is.SameAs(jumpReference),
                    "Repeated repair should reuse the Jump InputActionReference instead of allocating a new ScriptableObject.");
                Assert.That(rightInteractionRay.uiPressInput.inputActionReferencePerformed,
                    Is.SameAs(uiPressReference),
                    "Repeated repair should reuse the UI press InputActionReference instead of allocating a new ScriptableObject.");
                Assert.That(rightTeleportRay.selectInput.inputActionReferencePerformed,
                    Is.SameAs(teleportReference),
                    "Repeated repair should reuse the teleport select InputActionReference instead of allocating a new ScriptableObject.");
            }
            finally
            {
                DestroyImmediateIgnoringUnityCleanupLogs(instance);
            }
        }

        [Test]
        public void PersistedXriInputsUseGeneratedAssetOwnedReferences()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            foreach (TrackedPoseDriver poseDriver in prefab.GetComponentsInChildren<TrackedPoseDriver>(true))
            {
                AssertActionPropertyUsesGeneratedReference(poseDriver.positionInput, $"{poseDriver.name} position");
                AssertActionPropertyUsesGeneratedReference(poseDriver.rotationInput, $"{poseDriver.name} rotation");
                AssertActionPropertyUsesGeneratedReference(poseDriver.trackingStateInput, $"{poseDriver.name} tracking state");
            }

            foreach (XRRayInteractor ray in prefab.GetComponentsInChildren<XRRayInteractor>(true))
            {
                AssertButtonReaderUsesGeneratedReference(ray.uiPressInput, $"{ray.name} UI press", allowUnused: true);
                AssertValueReaderUsesGeneratedReference(ray.uiScrollInput, $"{ray.name} UI scroll", allowUnused: true);
                AssertButtonReaderUsesGeneratedReference(ray.selectInput, $"{ray.name} select", allowUnused: true);
            }

            BlockiverseInputRig inputRig = prefab.GetComponent<BlockiverseInputRig>();
            Assert.That(inputRig, Is.Not.Null);
            AssertButtonReaderUsesGeneratedReference(inputRig.JumpProvider.jumpInput, "Jump", allowUnused: false);

            ContinuousMoveProvider continuousMove = prefab.GetComponent<ContinuousMoveProvider>();
            SnapTurnProvider snapTurn = prefab.GetComponent<SnapTurnProvider>();
            ContinuousTurnProvider continuousTurn = prefab.GetComponent<ContinuousTurnProvider>();

            Assert.That(continuousMove, Is.Not.Null);
            Assert.That(snapTurn, Is.Not.Null);
            Assert.That(continuousTurn, Is.Not.Null);
            AssertValueReaderUsesGeneratedReference(continuousMove.leftHandMoveInput, "Left hand move", allowUnused: false);
            AssertValueReaderUsesGeneratedReference(continuousMove.rightHandMoveInput, "Right hand move", allowUnused: true);
            AssertValueReaderUsesGeneratedReference(snapTurn.leftHandTurnInput, "Left hand snap turn", allowUnused: true);
            AssertValueReaderUsesGeneratedReference(snapTurn.rightHandTurnInput, "Right hand snap turn", allowUnused: false);
            AssertValueReaderUsesGeneratedReference(continuousTurn.leftHandTurnInput, "Left hand smooth turn", allowUnused: true);
            AssertValueReaderUsesGeneratedReference(continuousTurn.rightHandTurnInput, "Right hand smooth turn", allowUnused: false);

            try
            {
                Scene scene = EditorSceneManager.OpenScene(BlockiverseProject.BootScenePath, OpenSceneMode.Single);
                XRUIInputModule inputModule = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<XRUIInputModule>(true))
                    .SingleOrDefault();

                Assert.That(inputModule, Is.Not.Null);
                AssertGeneratedReference(inputModule.leftClickAction, "Boot XRUIInputModule left click");
                AssertGeneratedReference(inputModule.scrollWheelAction, "Boot XRUIInputModule scroll");
                AssertGeneratedReference(inputModule.navigateAction, "Boot XRUIInputModule navigate");
                AssertGeneratedReference(inputModule.submitAction, "Boot XRUIInputModule submit");

                UnityEngine.UIElements.PanelInputConfiguration panelInputConfiguration =
                    inputModule.GetComponent<UnityEngine.UIElements.PanelInputConfiguration>();
                Assert.That(panelInputConfiguration, Is.Not.Null,
                    "UI Toolkit world-space tracked-device input needs PanelInputConfiguration on the EventSystem.");
                Assert.That(
                    panelInputConfiguration.panelInputRedirection,
                    Is.EqualTo(UnityEngine.UIElements.PanelInputConfiguration.PanelInputRedirection.Never),
                    "UI Toolkit tracked-device input should not be redirected through the EventSystem.");
            }
            finally
            {
                OpenEmptySceneIgnoringUnityCleanupLogs();
            }
        }

        [Test]
        public void GeneratedScenesDoNotStoreSceneLocalInputActionReferences()
        {
            foreach (string scenePath in new[]
                     {
                         BlockiverseProject.BootScenePath,
                         BlockiverseProject.MultiplayerTestScenePath,
                     })
            {
                string yaml = File.ReadAllText(scenePath);
                Assert.That(
                    yaml,
                    Does.Not.Contain("m_EditorClassIdentifier: Unity.InputSystem::UnityEngine.InputSystem.InputActionReference"),
                    $"{scenePath} should reference generated InputActionReference assets instead of storing scene-local reference objects.");
            }
        }

        [Test]
        public void XrRigPrefabIsWiredForNativeInteractorsAndUiToolkitBlockCatalog()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            Transform rightController = prefab.transform.Find("Camera Offset/Right Controller");
            Transform rightInteractionRayTransform = rightController?.Find("Interaction Ray");
            Transform teleportRayTransform = rightController?.Find("Teleport Ray");
            Transform rightUiToolkitRayTransform = rightController?.Find("UI Toolkit Ray Input");
            Transform rightControllerRayOrigin = rightController?.Find(ControllerRayOriginName);
            Transform leftController = prefab.transform.Find("Camera Offset/Left Controller");
            Transform leftInteractionRayTransform = leftController?.Find("Interaction Ray");
            Transform leftUiToolkitRayTransform = leftController?.Find("UI Toolkit Ray Input");
            Transform leftControllerRayOrigin = leftController?.Find(ControllerRayOriginName);
            Transform rightRayOrigin = prefab.transform.Find("Camera Offset/Right Ray Origin");
            Transform leftRayOrigin = prefab.transform.Find("Camera Offset/Left Ray Origin");
            Transform leftTeleportRayTransform = leftController?.Find("Teleport Ray");
            XRRayInteractor rightInteractionRay = rightInteractionRayTransform?.GetComponent<XRRayInteractor>();
            XRRayInteractor leftInteractionRay = leftInteractionRayTransform?.GetComponent<XRRayInteractor>();
            NearFarInteractor rightUiToolkitInteractor = rightUiToolkitRayTransform?.GetComponent<NearFarInteractor>();
            NearFarInteractor leftUiToolkitInteractor = leftUiToolkitRayTransform?.GetComponent<NearFarInteractor>();
            XRInteractorLineVisual rightInteractionLineVisual = rightInteractionRayTransform?.GetComponent<XRInteractorLineVisual>();
            XRInteractorLineVisual leftInteractionLineVisual = leftInteractionRayTransform?.GetComponent<XRInteractorLineVisual>();
            XRRayInteractor teleportRay = teleportRayTransform?.GetComponent<XRRayInteractor>();
            XRInteractorLineVisual teleportLineVisual = teleportRayTransform?.GetComponent<XRInteractorLineVisual>();
            BlockiverseLocomotionRayMediator mediator = rightController?.GetComponent<BlockiverseLocomotionRayMediator>();
            BlockiverseCreativeInputBridge creativeInputBridge = prefab.GetComponent<BlockiverseCreativeInputBridge>();
            CreativeHotbar hotbar = prefab.GetComponent<CreativeHotbar>();

            Assert.That(rightController, Is.Not.Null);
            Assert.That(prefab.transform.Find("Camera Offset/Left Controller"), Is.Not.Null);
            Assert.That(rightInteractionRay, Is.Not.Null);
            Assert.That(leftInteractionRay, Is.Not.Null);
            Assert.That(rightUiToolkitRayTransform, Is.Not.Null);
            Assert.That(leftUiToolkitRayTransform, Is.Not.Null);
            Assert.That(rightUiToolkitRayTransform.parent, Is.SameAs(rightController),
                "UI Toolkit menu input must stay active even if the gameplay interaction ray object is toggled.");
            Assert.That(leftUiToolkitRayTransform.parent, Is.SameAs(leftController),
                "UI Toolkit menu input must stay active even if the gameplay interaction ray object is toggled.");
            Assert.That(rightInteractionRayTransform.Find("UI Toolkit Ray Input"), Is.Null,
                "UI Toolkit menu input must not be nested under the toggled gameplay interaction ray.");
            Assert.That(leftInteractionRayTransform.Find("UI Toolkit Ray Input"), Is.Null,
                "UI Toolkit menu input must not be nested under the toggled gameplay interaction ray.");
            AssertInteractionRayDefaults(rightInteractionRay);
            AssertInteractionRayDefaults(leftInteractionRay);
            Assert.That(prefab.transform.Find("Camera Offset/Right Aim Pose"), Is.Null,
                "The interaction ray should not use a separately tracked aim pose that can diverge from the visible controller.");
            Assert.That(prefab.transform.Find("Camera Offset/Left Aim Pose"), Is.Null,
                "The teleport ray should not use a separately tracked aim pose that can diverge from the visible controller.");
            Assert.That(rightRayOrigin, Is.Null,
                "The interaction ray must not use a separately tracked pointer-pose ray origin that can diverge from the visible controller.");
            Assert.That(leftRayOrigin, Is.Null,
                "The teleport ray must not use a separately tracked pointer-pose ray origin that can diverge from the visible controller.");
            AssertControllerRayOrigin(rightController, rightControllerRayOrigin);
            AssertControllerRayOrigin(leftController, leftControllerRayOrigin);
            Assert.That(rightInteractionRay.rayOriginTransform, Is.SameAs(rightControllerRayOrigin));
            Assert.That(leftInteractionRay.rayOriginTransform, Is.SameAs(leftControllerRayOrigin));
            AssertUiToolkitNearFarInteractorDefaults(
                rightUiToolkitInteractor,
                rightControllerRayOrigin,
                null,
                "Right interaction ray must include the XRI interactor type that supports world-space UI Toolkit pointers.");
            AssertUiToolkitNearFarInteractorDefaults(
                leftUiToolkitInteractor,
                leftControllerRayOrigin,
                null,
                "Left interaction ray must include the XRI interactor type that supports world-space UI Toolkit pointers.");
            Assert.That(rightInteractionLineVisual, Is.Not.Null);
            AssertLineVisualDefaults(rightInteractionLineVisual);
            Assert.That(leftInteractionLineVisual, Is.Not.Null);
            AssertLineVisualDefaults(leftInteractionLineVisual);
            Assert.That(teleportRay, Is.Not.Null);
            AssertTeleportRayDefaults(teleportRay);
            Assert.That(teleportRay.rayOriginTransform, Is.SameAs(rightControllerRayOrigin));
            Assert.That(teleportLineVisual, Is.Not.Null);
            AssertLineVisualDefaults(teleportLineVisual);
            Assert.That(teleportRayTransform.gameObject.activeSelf, Is.False);
            Assert.That(mediator, Is.Not.Null);
            // Left controller also carries a teleport ray (either thumbstick can aim in Teleport mode).
            Assert.That(leftTeleportRayTransform, Is.Not.Null, "Left controller must have a Teleport Ray.");
            XRRayInteractor leftTeleportRay = leftTeleportRayTransform.GetComponent<XRRayInteractor>();
            XRInteractorLineVisual leftTeleportLineVisual = leftTeleportRayTransform.GetComponent<XRInteractorLineVisual>();
            Assert.That(leftTeleportRay, Is.Not.Null);
            AssertTeleportRayDefaults(leftTeleportRay);
            Assert.That(leftTeleportRay.rayOriginTransform, Is.SameAs(leftControllerRayOrigin));
            Assert.That(leftTeleportLineVisual, Is.Not.Null);
            AssertLineVisualDefaults(leftTeleportLineVisual);
            Assert.That(leftTeleportRayTransform.gameObject.activeSelf, Is.False);
            Assert.That(creativeInputBridge, Is.Not.Null);
            Assert.That(prefab.transform.Find("Camera Offset/Right Controller/Ray Pointer Line"), Is.Null);
            Assert.That(hotbar, Is.Not.Null);

            BlockiverseInputRig inputRig = prefab.GetComponent<BlockiverseInputRig>();
            UnityEngine.Events.UnityEvent quickMenuEvent = inputRig.QuickMenuPressed;
            Assert.That(quickMenuEvent, Is.Not.Null);
            Assert.That(quickMenuEvent.GetPersistentEventCount(), Is.EqualTo(0),
                "Support-grip quick menu input must be routed by BlockiverseMenuController at runtime so UI Toolkit menus can own raycasts.");
        }

        [Test]
        public void UiToolkitNearFarInteractorsStayActiveWhenGameplayInteractionRayIsHidden()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                Transform rightController = instance.transform.Find("Camera Offset/Right Controller");
                Transform leftController = instance.transform.Find("Camera Offset/Left Controller");
                Transform rightInteractionRay = rightController?.Find("Interaction Ray");
                Transform leftInteractionRay = leftController?.Find("Interaction Ray");
                Transform rightUiToolkitRay = rightController?.Find("UI Toolkit Ray Input");
                Transform leftUiToolkitRay = leftController?.Find("UI Toolkit Ray Input");

                Assert.That(rightInteractionRay, Is.Not.Null);
                Assert.That(leftInteractionRay, Is.Not.Null);
                Assert.That(rightUiToolkitRay, Is.Not.Null);
                Assert.That(leftUiToolkitRay, Is.Not.Null);

                rightInteractionRay.gameObject.SetActive(false);
                leftInteractionRay.gameObject.SetActive(false);

                Assert.That(rightUiToolkitRay.gameObject.activeInHierarchy, Is.True,
                    "UI Toolkit XRI menu input must remain active when the gameplay interaction ray is hidden.");
                Assert.That(leftUiToolkitRay.gameObject.activeInHierarchy, Is.True,
                    "UI Toolkit XRI menu input must remain active when the gameplay interaction ray is hidden.");
                Assert.That(rightUiToolkitRay.GetComponent<NearFarInteractor>(), Is.Not.Null);
                Assert.That(leftUiToolkitRay.GetComponent<NearFarInteractor>(), Is.Not.Null);
            }
            finally
            {
                DestroyImmediateIgnoringUnityCleanupLogs(instance);
            }
        }

        [Test]
        public void XrRigPrefabHasFeedbackServicesAndAllAudioCueClipsAssigned()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            BlockiverseFeedbackSettings feedbackSettings = prefab.GetComponent<BlockiverseFeedbackSettings>();
            BlockiverseAudioCuePlayer audioCuePlayer = prefab.GetComponent<BlockiverseAudioCuePlayer>();
            BlockiverseVfxPool vfxPool = prefab.GetComponent<BlockiverseVfxPool>();
            BlockiverseVfxCuePlayer vfxCuePlayer = prefab.GetComponent<BlockiverseVfxCuePlayer>();
            BlockiverseInteractionHaptics interactionHaptics = prefab.GetComponent<BlockiverseInteractionHaptics>();
            BlockiverseInputRig inputRig = prefab.GetComponent<BlockiverseInputRig>();

            Assert.That(feedbackSettings, Is.Not.Null);
            Assert.That(audioCuePlayer, Is.Not.Null);
            Assert.That(vfxPool, Is.Not.Null);
            Assert.That(vfxCuePlayer, Is.Not.Null);
            Assert.That(interactionHaptics, Is.Not.Null);
            Assert.That(inputRig, Is.Not.Null);
            Assert.That(audioCuePlayer.FeedbackSettings, Is.SameAs(feedbackSettings));
            Assert.That(vfxCuePlayer.FeedbackSettings, Is.SameAs(feedbackSettings));
            Assert.That(vfxCuePlayer.Pool, Is.SameAs(vfxPool));
            Assert.That(interactionHaptics.FeedbackSettings, Is.SameAs(feedbackSettings));
            Assert.That(interactionHaptics.DominantHandHaptics, Is.Not.Null);
            Assert.That(inputRig.AudioCuePlayer, Is.SameAs(audioCuePlayer));

            foreach (BlockiverseAudioCue cue in System.Enum.GetValues(typeof(BlockiverseAudioCue)))
                Assert.That(audioCuePlayer.HasClipForCue(cue), Is.True, $"{cue} should have an assigned generated clip.");

            Assert.That(audioCuePlayer.FootstepClipCount, Is.EqualTo(2));

            // The music bed: a controller on the rig with a generated track per context.
            BlockiverseMusicController musicController = prefab.GetComponent<BlockiverseMusicController>();
            Assert.That(musicController, Is.Not.Null);
            foreach (BlockiverseMusicContext context in new[]
            {
                BlockiverseMusicContext.Menu,
                BlockiverseMusicContext.Day,
                BlockiverseMusicContext.Night,
                BlockiverseMusicContext.Cave,
            })
            {
                Assert.That(musicController.ResolveTrackClip(context), Is.Not.Null,
                    $"{context} should have an assigned generated music track.");
            }
        }

        [Test]
        public void XrRigPrefabRoutesControllerMappingThroughUiToolkitAndKeepsInteractiveSurvivalHud()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            Transform uiToolkitSurface = prefab.transform.Find("Camera Offset/UI Toolkit Menu Surface");
            Transform survivalHud = prefab.transform.Find("Camera Offset/Survival HUD");

            BlockiverseMenuController menuController = prefab.GetComponent<BlockiverseMenuController>();
            Assert.That(uiToolkitSurface, Is.Null,
                "The title/menu UI Toolkit surface is fixed in the Boot scene's menu world instead of following the XR rig.");
            Assert.That(menuController, Is.Not.Null);

            Assert.That(survivalHud, Is.Not.Null);
            Assert.That(survivalHud.GetComponent<SurvivalHudController>(), Is.Not.Null);
            Assert.That(survivalHud.GetComponent<BlockiverseHudToolkitSurface>(), Is.Not.Null);

            string prefabYaml = File.ReadAllText(BlockiverseProject.XrRigPrefabPath);
            Assert.That(prefabYaml, Does.Not.Contain("Survival" + "Inventory" + "Panel"));
            Assert.That(prefabYaml, Does.Not.Contain("Survival" + "Crafting" + "Panel"));
            Assert.That(prefabYaml, Does.Not.Contain("Survival" + "Crate" + "Panel"));
            Assert.That(prefabYaml, Does.Not.Contain("Survival" + "Health" + "Panel"));

            UIDocument survivalHudDocument = survivalHud.GetComponent<UIDocument>();
            Assert.That(survivalHudDocument, Is.Not.Null);
            Assert.That(survivalHudDocument.worldSpaceSize.x, Is.LessThanOrEqualTo(600.0f),
                "Gameplay HUD should be a compact overlay, not the full survival menu.");
            Assert.That(survivalHudDocument.worldSpaceSize.y, Is.LessThanOrEqualTo(220.0f),
                "Gameplay HUD should not occupy the player's central field of view.");
            Assert.That(survivalHud.Find("Panel/Inventory"), Is.Null,
                "Inventory menu rendering belongs to the UI Toolkit runtime surface, not hidden HUD children.");
            Assert.That(survivalHud.Find("Panel/Crafting"), Is.Null,
                "Crafting menu rendering belongs to the UI Toolkit runtime surface, not hidden HUD children.");
            Assert.That(survivalHud.Find("Panel/Shared Crate"), Is.Null,
                "Shared crate menu rendering belongs to the UI Toolkit runtime surface, not hidden HUD children.");
        }

        [Test]
        public void XrRigPrefabHasSinglePlayerFallbackAvatarRig()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);

            try
            {
                Component avatarRig = instance.GetComponent("BlockiverseNetworkAvatarRig");

                Assert.That(avatarRig, Is.Not.Null);
                Assert.That(instance.GetComponent("NetworkObject"), Is.Null, "The XR rig is an unspawned local pose proxy; NetworkObject belongs on the network player prefab.");
                avatarRig.GetType().GetMethod("RefreshAvatarMode").Invoke(avatarRig, null);
                Assert.That(GetAvatarProperty<bool>(avatarRig, "FallbackProxyEnabled"), Is.True);
                Assert.That(GetAvatarProperty<bool>(avatarRig, "MetaAvatarAvailable"), Is.False);
                Assert.That(GetAvatarProperty<Transform>(avatarRig, "FallbackRoot"), Is.Not.Null);
                Assert.That(GetAvatarProperty<Transform>(avatarRig, "HeadAnchor"), Is.Not.Null);
                Assert.That(GetAvatarProperty<Transform>(avatarRig, "LeftHandAnchor"), Is.Not.Null);
                Assert.That(GetAvatarProperty<Transform>(avatarRig, "RightHandAnchor"), Is.Not.Null);
            }
            finally
            {
                DestroyImmediateIgnoringUnityCleanupLogs(instance);
            }
        }

        static void AssertController(GameObject prefab, string controllerName, BlockiverseControllerRole expectedRole)
        {
            Transform controller = prefab.transform.Find($"Camera Offset/{controllerName}");

            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.GetComponent<BlockiverseControllerAnchor>()?.Role, Is.EqualTo(expectedRole));
            Assert.That(controller.GetComponent<BlockiverseControllerHaptics>()?.Role, Is.EqualTo(expectedRole));
        }

        static void AssertControllerRayOrigin(Transform controller, Transform rayOrigin)
        {
            Assert.That(rayOrigin, Is.Not.Null, $"{controller?.name} should carry a controller-local ray origin.");
            Assert.That(rayOrigin.parent, Is.SameAs(controller));
            Assert.That(Vector3.Distance(rayOrigin.localPosition, Vector3.zero), Is.LessThan(0.0001f),
                "The ray should originate at the tracked controller, not a separate pointer-pose transform.");
            Assert.That(Quaternion.Angle(rayOrigin.localRotation, ControllerRayOriginLocalRotation), Is.LessThan(0.001f),
                "Quest grip-pose forward points up like a stick; the child origin flips XRI forward onto the physical pointing axis.");
        }

        static void AssertInteractionRayDefaults(XRRayInteractor ray)
        {
            Assert.That(ray, Is.Not.Null);
            Assert.That(ray.lineType, Is.EqualTo(XRRayInteractor.LineType.StraightLine));
            Assert.That(ray.hitDetectionType, Is.EqualTo(XRRayInteractor.HitDetectionType.Raycast));
            Assert.That(ray.hitClosestOnly, Is.True);
            Assert.That(ray.blendVisualLinePoints, Is.True);
            Assert.That(ray.raycastTriggerInteraction, Is.EqualTo(QueryTriggerInteraction.Collide),
                "UI Toolkit menu surfaces use trigger colliders, so the visible pointer ray should stop at the menu.");
            Assert.That(ray.raycastSnapVolumeInteraction, Is.EqualTo(XRRayInteractor.QuerySnapVolumeInteraction.Ignore));
            Assert.That(ray.raycastMask.value, Is.EqualTo(BlockiverseProject.VrUiRaycastLayerMask),
                "Visible controller UI rays should only query the dedicated UI Toolkit menu layer.");
            Assert.That(ray.enableUIInteraction, Is.True);
            Assert.That(ray.blockUIOnInteractableSelection, Is.False,
                "Block targeting must not suppress UI clicks while a menu is visible.");
            Assert.That(ray.interactionLayers.value, Is.EqualTo(BlockiverseRayDefaults.DefaultXriInteractionLayerMask),
                "World-space UI and simple interactables should keep XRI Default overlap; selection inputs stay disabled elsewhere.");
            Assert.That(ray.maxRaycastDistance, Is.EqualTo(CreativeInteractionController.MaxBlockInteractionReachMeters).Within(0.001f));
        }

        static void AssertUiToolkitNearFarInteractorDefaults(
            NearFarInteractor interactor,
            Transform expectedRayOrigin,
            InputAction expectedUiPress,
            string message)
        {
            Assert.That(interactor, Is.Not.Null, message);
            Assert.That(interactor.enableNearCasting, Is.False,
                "UI Toolkit menu pointing should only use the far ray so the current controller-hand/menu visual relationship stays unchanged.");
            Assert.That(interactor.enableFarCasting, Is.True);
            Assert.That(interactor.enableUIInteraction, Is.True);
            Assert.That(interactor.blockUIOnInteractableSelection, Is.False);
            AssertButtonReaderUnused(interactor.selectInput, "Select");
            AssertButtonReaderUnused(interactor.activateInput, "Activate");
            if (expectedUiPress != null)
                AssertButtonReaderReferencesAction(interactor.uiPressInput, expectedUiPress, message);

            Assert.That(interactor.farInteractionCaster, Is.TypeOf<CurveInteractionCaster>());
            var farCaster = (CurveInteractionCaster)interactor.farInteractionCaster;
            Assert.That(farCaster.castOrigin, Is.SameAs(expectedRayOrigin));
            Assert.That(farCaster.raycastMask.value, Is.EqualTo(BlockiverseProject.VrUiRaycastLayerMask));
            Assert.That(farCaster.raycastTriggerInteraction, Is.EqualTo(QueryTriggerInteraction.Collide),
                "UI Toolkit menu surfaces use trigger colliders; the UITK far caster must hit them for hover and click events.");
            Assert.That(farCaster.raycastSnapVolumeInteraction, Is.EqualTo(CurveInteractionCaster.QuerySnapVolumeInteraction.Ignore));
            Assert.That(farCaster.raycastUIDocumentTriggerInteraction, Is.EqualTo(QueryUIDocumentInteraction.Collide),
                "UI Toolkit menu colliders are triggers; the far caster must still include UIDocument trigger hits.");
            Assert.That(farCaster.hitDetectionType, Is.EqualTo(CurveInteractionCaster.HitDetectionType.Raycast));
            Assert.That(farCaster.castDistance, Is.EqualTo(CreativeInteractionController.MaxBlockInteractionReachMeters).Within(0.001f));
            Assert.That(farCaster.targetNumCurveSegments, Is.EqualTo(1));
        }

        static void AssertTeleportRayDefaults(XRRayInteractor ray)
        {
            Assert.That(ray, Is.Not.Null);
            Assert.That(ray.lineType, Is.EqualTo(XRRayInteractor.LineType.ProjectileCurve));
            Assert.That(ray.hitDetectionType, Is.EqualTo(XRRayInteractor.HitDetectionType.Raycast));
            Assert.That(ray.hitClosestOnly, Is.True);
            Assert.That(ray.blendVisualLinePoints, Is.True);
            Assert.That(ray.raycastSnapVolumeInteraction, Is.EqualTo(XRRayInteractor.QuerySnapVolumeInteraction.Ignore));
            Assert.That(ray.enableUIInteraction, Is.False);
        }

        static void AssertLineVisualDefaults(XRInteractorLineVisual lineVisual)
        {
            Assert.That(lineVisual.lineWidth, Is.EqualTo(0.01f).Within(0.0001f));
            Assert.That(lineVisual.overrideInteractorLineOrigin, Is.False);
            Assert.That(lineVisual.lineOriginTransform, Is.Null);
            Assert.That(lineVisual.overrideInteractorLineLength, Is.False);
            Assert.That(lineVisual.autoAdjustLineLength, Is.True);
            Assert.That(lineVisual.minLineLength, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(lineVisual.stopLineAtFirstRaycastHit, Is.True);
            Assert.That(lineVisual.smoothMovement, Is.False,
                "Controller-tracked rays should not smooth visual points across startup pose changes.");
        }

        static void AssertBinding(InputActionProperty property, string expectedPath)
        {
            Assert.That(property.action, Is.Not.Null);
            Assert.That(property.action.bindings, Has.Some.Matches<InputBinding>(
                binding => binding.effectivePath == expectedPath || binding.path == expectedPath));
        }

        static void AssertJumpProviderUsesDominantPrimary(BlockiverseInputRig inputRig, JumpProvider jumpProvider)
        {
            InputAction jumpAction = inputRig.ResolveJumpActionForCurrentControls();

            Assert.That(jumpProvider.jumpInput.inputSourceMode,
                Is.EqualTo(XRInputButtonReader.InputSourceMode.InputActionReference));
            Assert.That(jumpProvider.jumpInput.inputActionReferencePerformed?.action,
                Is.SameAs(jumpAction));
        }

        static void AssertControllerRoleActions(InputActionMap map, string handUsage)
        {
            string controllerPath = $"<XRController>{{{handUsage}}}";
            InputAction primary = map.FindAction(BlockiverseInputActionNames.PrimaryButton, throwIfNotFound: false);
            InputAction secondary = map.FindAction(BlockiverseInputActionNames.SecondaryButton, throwIfNotFound: false);
            InputAction sprint = map.FindAction(BlockiverseInputActionNames.Sprint, throwIfNotFound: false);
            InputAction crouch = map.FindAction(BlockiverseInputActionNames.Crouch, throwIfNotFound: false);

            Assert.That(primary, Is.Not.Null, $"{map.name}/Primary Button should exist.");
            Assert.That(primary.bindings,
                Has.Some.Matches<InputBinding>(b => (b.effectivePath ?? b.path ?? "") == $"{controllerPath}/primaryButton"),
                $"{map.name}/Primary Button should be bound to {controllerPath}/primaryButton.");
            Assert.That(secondary, Is.Not.Null, $"{map.name}/Secondary Button should exist.");
            Assert.That(secondary.bindings,
                Has.Some.Matches<InputBinding>(b => (b.effectivePath ?? b.path ?? "") == $"{controllerPath}/secondaryButton"),
                $"{map.name}/Secondary Button should be bound to {controllerPath}/secondaryButton.");
            Assert.That(sprint, Is.Not.Null, $"{map.name}/Sprint should exist.");
            Assert.That(sprint.bindings,
                Has.Some.Matches<InputBinding>(b => (b.effectivePath ?? b.path ?? "") == $"{controllerPath}/thumbstickClicked"),
                $"{map.name}/Sprint should be bound to {controllerPath}/thumbstickClicked.");
            Assert.That(crouch, Is.Not.Null, $"{map.name}/Crouch should exist.");
            Assert.That(crouch.bindings,
                Has.Some.Matches<InputBinding>(b => (b.effectivePath ?? b.path ?? "") == $"{controllerPath}/thumbstickClicked"),
                $"{map.name}/Crouch should be bound to {controllerPath}/thumbstickClicked.");
        }

        static void AssertButtonReaderReferencesAction(XRInputButtonReader reader, InputAction action, string message)
        {
            Assert.That(reader.inputSourceMode,
                Is.EqualTo(XRInputButtonReader.InputSourceMode.InputActionReference),
                message);
            Assert.That(reader.inputActionReferencePerformed?.action,
                Is.SameAs(action),
                message);
        }

        static void AssertButtonReaderUnused(XRInputButtonReader reader, string context)
        {
            Assert.That(reader.inputSourceMode,
                Is.EqualTo(XRInputButtonReader.InputSourceMode.Unused),
                $"{context} should be unused.");
            Assert.That(reader.inputActionReferencePerformed, Is.Null);
            Assert.That(reader.inputActionReferenceValue, Is.Null);
        }

        static void AssertActionPropertyUsesGeneratedReference(InputActionProperty property, string context)
        {
            AssertGeneratedReference(property.reference, context);
            Assert.That(property.action, Is.Not.Null, $"{context} should resolve a generated action.");
        }

        static void AssertButtonReaderUsesGeneratedReference(XRInputButtonReader reader, string context, bool allowUnused)
        {
            if (allowUnused && reader.inputSourceMode == XRInputButtonReader.InputSourceMode.Unused)
                return;

            Assert.That(reader.inputSourceMode,
                Is.EqualTo(XRInputButtonReader.InputSourceMode.InputActionReference),
                $"{context} should use InputActionReference mode.");
            AssertGeneratedReference(reader.inputActionReferencePerformed, context);
        }

        static void AssertValueReaderUsesGeneratedReference(XRInputValueReader<Vector2> reader, string context, bool allowUnused)
        {
            if (allowUnused && reader.inputSourceMode == XRInputValueReader.InputSourceMode.Unused)
                return;

            Assert.That(reader.inputSourceMode,
                Is.EqualTo(XRInputValueReader.InputSourceMode.InputActionReference),
                $"{context} should use InputActionReference mode.");
            AssertGeneratedReference(reader.inputActionReference, context);
        }

        static void AssertGeneratedReference(InputActionReference reference, string context)
        {
            Assert.That(reference, Is.Not.Null, $"{context} should use a generated InputActionReference asset.");
            Assert.That(reference.action, Is.Not.Null, $"{context} reference should resolve an action.");

            string path = AssetDatabase.GetAssetPath(reference);

            Assert.That(path, Does.StartWith(BlockiverseProject.InputActionReferencesFolderPath + "/"),
                $"{context} should reference an asset-owned InputActionReference, not a scene-local object.");
        }

        static void AssertControllerPoseDriver(GameObject instance, string controllerName, string positionPath, string rotationPath)
        {
            Transform controller = instance.transform.Find($"Camera Offset/{controllerName}");

            Assert.That(controller, Is.Not.Null);

            TrackedPoseDriver driver = controller.GetComponent<TrackedPoseDriver>();

            Assert.That(driver, Is.Not.Null);
            Assert.That(driver.enabled, Is.True);
            Assert.That(driver.updateType, Is.EqualTo(TrackedPoseDriver.UpdateType.UpdateAndBeforeRender));
            Assert.That(driver.trackingType, Is.EqualTo(TrackedPoseDriver.TrackingType.RotationAndPosition));
            AssertBinding(driver.positionInput, positionPath);
            AssertBinding(driver.rotationInput, rotationPath);
        }

        static void AssertGravityUsesVoxelTerrainMask(GravityProvider gravityProvider)
        {
            Assert.That(gravityProvider.sphereCastLayerMask.value, Is.EqualTo(BlockiverseProject.InteractionLayerMask),
                "Gravity grounding must ignore the player CharacterController and only test voxel terrain.");
            Assert.That(gravityProvider.sphereCastTriggerInteraction, Is.EqualTo(QueryTriggerInteraction.Ignore));
        }

        static T GetAvatarProperty<T>(Component avatarRig, string propertyName)
        {
            return (T)avatarRig.GetType().GetProperty(propertyName).GetValue(avatarRig);
        }

        static void DestroyImmediateIgnoringUnityCleanupLogs(Object target)
        {
            if (target == null)
                return;

            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                Object.DestroyImmediate(target);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        static void OpenEmptySceneIgnoringUnityCleanupLogs()
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }
    }
}
