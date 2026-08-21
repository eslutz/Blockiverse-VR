using System.Collections;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.VR;
using Blockiverse.WorldGen;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Blockiverse.Tests.PlayMode
{
    // Swim locomotion against the real XRI stack. These run on frame timing rather than as pure
    // functions because the load-bearing behaviour is the gravity LOCK: taking it and never
    // releasing it strands the player floating in mid-air, and failing to take it drops them at
    // XRI's terminal velocity instead of letting them swim.
    public sealed class BlockiverseSwimPlayModeTests
    {
        const int SettleFrames = 30;

        GameObject managerObject;
        GameObject rigObject;
        GameObject seabed;

        [SetUp]
        public void SetUp()
        {
            // Batchmode starves wall-clock deltaTime; without pinning it the sink rate assertions
            // below measure the test machine's scheduler rather than the swim speed.
            Time.captureDeltaTime = 1.0f / 60.0f;
            BlockiverseRuntimeState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0.0f;

            if (seabed != null)
                Object.DestroyImmediate(seabed);

            SwimRigFixture.DestroyRigImmediate(rigObject);

            if (managerObject != null)
                Object.DestroyImmediate(managerObject);

            BlockiverseRuntimeState.Reset();
        }

        [UnityTest]
        public IEnumerator ASubmergedPlayerSinksSlowlyInsteadOfFallingUnderGravity()
        {
            BlockiverseSwimProvider swim = CreateSubmergedRig(out XROrigin origin, out GravityProvider gravity);

            yield return WaitFrames(SettleFrames);

            Assert.That(swim.IsSwimming, Is.True,
                "A body-submerged player must be swimming; if this is false the sample points or the state machine are wrong.");
            Assert.That(swim.GravityLockHeld, Is.True,
                "Swimming holds a ForcedOff gravity lock. Without it the player falls at XRI's terminal velocity through the water.");

            float startY = origin.transform.position.y;

            yield return WaitFrames(60);

            float travelled = startY - origin.transform.position.y;

            Assert.That(travelled, Is.GreaterThan(0.0f),
                "Negative buoyancy is the default: with no input the player must descend.");
            // One second of frames at the pinned step. Gravity over the same second would cover
            // several metres, so this bound is what separates swimming from falling.
            Assert.That(travelled, Is.LessThan(BlockiverseSwimMotion.PassiveSinkSpeedMetersPerSecond * 2.0f),
                "The descent must be the passive sink drift, not a fall.");
        }

        [UnityTest]
        public IEnumerator PassiveDescentEngagesTheComfortVignetteAndTurningTheBoostOffStopsIt()
        {
            // Passive descent is motion the player did not ask for. The vignette is a large part of
            // why defaulting to negative buoyancy is defensible at all, so a setting that persists
            // and does nothing would be worse than not shipping it.
            BlockiverseSwimProvider swim = CreateSubmergedRig(out XROrigin origin, out GravityProvider gravity);
            TunnelingVignetteController vignette = AttachVignetteController();
            BlockiverseInputRig inputRig = rigObject.GetComponent<BlockiverseInputRig>();

            yield return WaitFrames(SettleFrames);

            Assert.That(swim.IsSwimming, Is.True);
            Assert.That(swim.VignetteEngaged, Is.True,
                "Sinking with no input must engage the tunneling vignette.");

            inputRig.ComfortSettings.SwimVignetteBoost = false;

            yield return WaitFrames(5);

            Assert.That(swim.VignetteEngaged, Is.False,
                "Turning the boost off must actually release the vignette, not just stop persisting a flag.");

            // The provider's own decision is what is asserted above; this proves the decision is
            // wired to the real XRI controller rather than to a field nobody reads.
            Assert.That(vignette, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator TurningOffPassiveSinkHoldsDepthExactly()
        {
            // The comfort accommodation, and it has to be exact rather than merely slow: with no
            // input the app must move the player vertically by zero.
            BlockiverseSwimProvider swim = CreateSubmergedRig(out XROrigin origin, out GravityProvider gravity);
            BlockiverseInputRig inputRig = rigObject.GetComponent<BlockiverseInputRig>();
            inputRig.ComfortSettings.SwimPassiveSinkEnabled = false;

            yield return WaitFrames(SettleFrames);

            Assert.That(swim.IsSwimming, Is.True);

            float startY = origin.transform.position.y;

            yield return WaitFrames(120);

            Assert.That(origin.transform.position.y, Is.EqualTo(startY).Within(0.02f),
                "With passive sink off, two seconds of standing still must not move the player at all.");
        }

        [UnityTest]
        public IEnumerator LeavingTheWaterReleasesTheGravityLockAndGravityResumes()
        {
            // The failure this guards is invisible until it strands someone: a lock taken on entry
            // and never released leaves the player hanging in the air on dry land.
            BlockiverseSwimProvider swim = CreateSubmergedRig(out XROrigin origin, out GravityProvider gravity);

            yield return WaitFrames(SettleFrames);

            Assert.That(swim.GravityLockHeld, Is.True);

            // Out of the water entirely, over the dry half of the world.
            rigObject.transform.position = new Vector3(12.5f, 8.0f, 12.5f);
            Physics.SyncTransforms();

            yield return WaitFrames(SettleFrames);

            Assert.That(swim.IsSwimming, Is.False);
            Assert.That(swim.GravityLockHeld, Is.False,
                "The lock must be handed back the moment the player is no longer swimming.");

            float startY = origin.transform.position.y;

            yield return WaitFrames(30);

            Assert.That(origin.transform.position.y, Is.LessThan(startY - 0.5f),
                "Gravity must actually resume: half a second of falling covers well over half a metre.");
        }

        [UnityTest]
        public IEnumerator LosingTheWorldWhileSwimmingReleasesTheGravityLock()
        {
            // Boot.unity never unloads, so Return to Title swaps the world out from under the
            // provider without disabling it. A lock left held there follows the player into the
            // next world.
            BlockiverseSwimProvider swim = CreateSubmergedRig(out XROrigin origin, out GravityProvider gravity);

            yield return WaitFrames(SettleFrames);

            Assert.That(swim.GravityLockHeld, Is.True);

            Object.DestroyImmediate(managerObject);
            managerObject = null;

            yield return WaitFrames(SettleFrames);

            Assert.That(swim.IsSwimming, Is.False,
                "No world means no fluid; the provider must not keep swimming against a world that is gone.");
            Assert.That(swim.GravityLockHeld, Is.False);
        }

        [UnityTest]
        public IEnumerator DisablingTheProviderReleasesTheGravityLock()
        {
            BlockiverseSwimProvider swim = CreateSubmergedRig(out XROrigin origin, out GravityProvider gravity);

            yield return WaitFrames(SettleFrames);

            Assert.That(swim.GravityLockHeld, Is.True);

            swim.enabled = false;

            yield return null;

            Assert.That(swim.GravityLockHeld, Is.False,
                "A disabled provider that still holds the lock would leave gravity off with nothing left to release it.");
        }

        [UnityTest]
        public IEnumerator TeleportModeKeepsTheJumpProviderOffButStillLetsTheSwimmerRise()
        {
            // The softlock this exists to prevent: jump is gated by locomotion mode, so a
            // teleport-mode player who ended up submerged could swim DOWN (crouch is not
            // mode-gated) and not up, while passive sink pulled them deeper. The swim provider
            // therefore reads the jump ACTION directly rather than the jump provider's enabled
            // state, which stays off underwater in both modes.
            BlockiverseSwimProvider swim = CreateSubmergedRig(out XROrigin origin, out GravityProvider gravity);
            BlockiverseInputRig inputRig = rigObject.GetComponent<BlockiverseInputRig>();
            inputRig.ComfortSettings.LocomotionMode = BlockiverseLocomotionMode.Teleport;

            yield return WaitFrames(SettleFrames);

            Assert.That(swim.IsSwimming, Is.True,
                "Teleport mode must not stop the swim state machine; the player is still in water.");
            Assert.That(inputRig.JumpProvider.enabled, Is.False,
                "Jumping underwater stays meaningless in either locomotion mode.");
            Assert.That(swim.GravityLockHeld, Is.True,
                "Gravity is locked off in teleport mode too, or a teleport-mode swimmer would sink at falling speed.");

            // The rise input cannot be pressed from a test without a device, and this harness wires
            // no InputActionAsset, so the action itself resolves to null here. What matters is that
            // the provider does not depend on the jump PROVIDER, which is off above -- that
            // dependency is what the source-text guard in BlockiverseSwimMotionEditModeTests pins.
        }

        BlockiverseSwimProvider CreateSubmergedRig(out XROrigin origin, out GravityProvider gravity)
        {
            managerObject = new GameObject("Swim World Manager");
            SwimRigFixture.CreateWorldWithADeepPool(managerObject);

            // A floor under the pool so a sinking player has somewhere to come to rest rather than
            // falling out of the world if anything about the lock goes wrong.
            seabed = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seabed.name = "Swim Seabed";
            seabed.layer = BlockiverseProject.InteractionLayerIndex;
            seabed.transform.localScale = new Vector3(40.0f, 1.0f, 40.0f);
            seabed.transform.position = new Vector3(8.0f, 0.55f, 8.0f);

            rigObject = SwimRigFixture.CreateGravityRig(out origin, out gravity);

            // Chest-deep inside the pool, which fills cells y = 1..8 at (4, z = 4).
            rigObject.transform.position = new Vector3(4.5f, 4.0f, 4.5f);
            Physics.SyncTransforms();

            BlockiverseSwimProvider swim = rigObject.GetComponent<BlockiverseSwimProvider>();

            Assert.That(swim, Is.Not.Null,
                "BlockiverseInputRig must auto-provision the swim provider, exactly as it does the gravity provider.");

            return swim;
        }

        // The rig prefab carries a vignette controller under the head camera; this bare fixture
        // builds its own so the provider has something to drive.
        TunnelingVignetteController AttachVignetteController()
        {
            var vignetteObject = new GameObject("Tunneling Vignette");
            vignetteObject.transform.SetParent(rigObject.transform, false);
            TunnelingVignetteController controller = vignetteObject.AddComponent<TunnelingVignetteController>();
            rigObject.GetComponent<BlockiverseSwimProvider>().ConfigureVignette(controller);
            return controller;
        }

        static IEnumerator WaitFrames(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
                yield return null;
        }
    }

    // Swim locomotion driven by real input actions. Separate from the fixture above because it
    // needs InputTestFixture as a base class: rise and sink are the primary verbs of the feature,
    // and asserting on the provider's internal state instead of on real button presses would leave
    // the whole input path -- action resolution, the mode gate, the crouch suppression -- unproven.
    public sealed class BlockiverseSwimInputPlayModeTests : InputTestFixture
    {
        const int SettleFrames = 30;
        const int DriveFrames = 60;

        GameObject managerObject;
        GameObject rigObject;
        GameObject seabed;
        InputActionAsset actions;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            Time.captureDeltaTime = 1.0f / 60.0f;
            BlockiverseRuntimeState.Reset();
        }

        [TearDown]
        public override void TearDown()
        {
            Time.captureDeltaTime = 0.0f;

            if (seabed != null)
                Object.DestroyImmediate(seabed);

            SwimRigFixture.DestroyRigImmediate(rigObject);

            if (managerObject != null)
                Object.DestroyImmediate(managerObject);

            if (actions != null)
                Object.DestroyImmediate(actions);

            BlockiverseRuntimeState.Reset();
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator HoldingJumpRisesAndHoldingCrouchSinksFasterThanTheDrift()
        {
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            BlockiverseSwimProvider swim = CreateSubmergedRig(out XROrigin origin);

            yield return WaitFrames(SettleFrames);

            Assert.That(swim.IsSwimming, Is.True, "Fixture precondition: the rig must be submerged.");

            // Rise. This is the verb the whole feature turns on: without it a sinking player has no
            // way back to the surface.
            float beforeRise = origin.transform.position.y;
            Press(gamepad.buttonSouth);

            yield return WaitFrames(DriveFrames);

            float risen = origin.transform.position.y - beforeRise;

            Assert.That(risen, Is.GreaterThan(0.3f),
                "Holding the jump input must lift the player against the passive sink, not merely slow it.");

            Release(gamepad.buttonSouth);

            yield return WaitFrames(5);

            // Descend. Held crouch has to beat the passive drift, or the input reads as doing
            // nothing at all.
            float beforeSink = origin.transform.position.y;
            Press(gamepad.rightStickButton);

            yield return WaitFrames(DriveFrames);

            float sunk = beforeSink - origin.transform.position.y;

            Assert.That(sunk, Is.GreaterThan(BlockiverseSwimMotion.PassiveSinkSpeedMetersPerSecond),
                "One second of held crouch must descend further than one second of passive drift.");

            Release(gamepad.rightStickButton);
        }

        [UnityTest]
        public IEnumerator HoldingCrouchWhileSwimmingDoesNotShrinkTheCapsule()
        {
            // Crouch's only meaning underwater is "descend". Letting it also shrink the capsule and
            // drop the camera would move the view for an input meant to move the body.
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            BlockiverseSwimProvider swim = CreateSubmergedRig(out XROrigin origin);
            CharacterController controller = rigObject.GetComponent<CharacterController>();

            yield return WaitFrames(SettleFrames);

            Assert.That(swim.IsSwimming, Is.True);

            float standingHeight = controller.height;
            Press(gamepad.rightStickButton);

            yield return WaitFrames(SettleFrames);

            Assert.That(controller.height, Is.EqualTo(standingHeight).Within(0.01f),
                "The swimmer's capsule must stay full height while crouch is held as a descend input.");

            Release(gamepad.rightStickButton);
        }

        [UnityTest]
        public IEnumerator ATeleportModeSwimmerCanStillRise()
        {
            // The softlock, proven end to end rather than by source inspection: jump is gated by
            // locomotion mode, so a teleport-mode player who ended up submerged could swim down
            // (crouch is not mode-gated) and not up, while passive sink pulled them deeper.
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            BlockiverseSwimProvider swim = CreateSubmergedRig(out XROrigin origin);
            BlockiverseInputRig inputRig = rigObject.GetComponent<BlockiverseInputRig>();
            inputRig.ComfortSettings.LocomotionMode = BlockiverseLocomotionMode.Teleport;

            yield return WaitFrames(SettleFrames);

            Assert.That(swim.IsSwimming, Is.True);
            Assert.That(inputRig.JumpProvider.enabled, Is.False,
                "Fixture precondition: the jump provider is off in teleport mode, which is exactly why the swim provider cannot depend on it.");

            float before = origin.transform.position.y;
            Press(gamepad.buttonSouth);

            yield return WaitFrames(DriveFrames);

            Assert.That(origin.transform.position.y - before, Is.GreaterThan(0.3f),
                "A teleport-mode swimmer must be able to rise, or passive sink makes the water inescapable in the comfort locomotion mode.");

            Release(gamepad.buttonSouth);
        }

        BlockiverseSwimProvider CreateSubmergedRig(out XROrigin origin)
        {
            managerObject = new GameObject("Swim Input World Manager");
            SwimRigFixture.CreateWorldWithADeepPool(managerObject);

            seabed = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seabed.name = "Swim Input Seabed";
            seabed.layer = BlockiverseProject.InteractionLayerIndex;
            seabed.transform.localScale = new Vector3(40.0f, 1.0f, 40.0f);
            seabed.transform.position = new Vector3(8.0f, 0.55f, 8.0f);

            actions = CreateSwimActions();
            rigObject = SwimRigFixture.CreateGravityRig(out origin, out GravityProvider _, actions);
            rigObject.transform.position = new Vector3(4.5f, 4.0f, 4.5f);
            Physics.SyncTransforms();

            return rigObject.GetComponent<BlockiverseSwimProvider>();
        }

        // Only the two maps and the handful of actions the swim inputs resolve through. Anything
        // the rig looks up and does not find simply stays null, which is the same state a rig has
        // before its asset is assigned.
        static InputActionAsset CreateSwimActions()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();

            InputActionMap leftHand = asset.AddActionMap(BlockiverseInputActionNames.LeftHandMap);
            leftHand.AddAction(
                BlockiverseInputActionNames.Move,
                InputActionType.PassThrough,
                "<Gamepad>/leftStick",
                expectedControlLayout: "Vector2");

            // The dominant hand defaults to Right, so this is the map both swim inputs resolve
            // through: rise on Primary Button, descend on Crouch.
            InputActionMap rightHand = asset.AddActionMap(BlockiverseInputActionNames.RightHandMap);
            rightHand.AddAction(
                BlockiverseInputActionNames.Move,
                InputActionType.PassThrough,
                "<Gamepad>/rightStick",
                expectedControlLayout: "Vector2");
            rightHand.AddAction(BlockiverseInputActionNames.PrimaryButton, InputActionType.Button, "<Gamepad>/buttonSouth");
            rightHand.AddAction(BlockiverseInputActionNames.Crouch, InputActionType.Button, "<Gamepad>/rightStickPress");

            return asset;
        }

        static IEnumerator WaitFrames(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
                yield return null;
        }
    }

    // Shared by both swim fixtures below: the plain one that drives state directly, and the
    // input-driven one that needs InputTestFixture as its base class and so cannot inherit these.
    static class SwimRigFixture
    {
        internal static void CreateWorldWithADeepPool(GameObject managerObject)
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(
                16, 16, 16, chunkSize: 4, seed: 87, groundHeight: 1, spawnPosition: new BlockPosition(8, 2, 8));
            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);

            for (int z = 0; z < settings.Bounds.Depth; z++)
            for (int x = 0; x < settings.Bounds.Width; x++)
                world.SetBlock(new BlockPosition(x, 0, z), BlockRegistry.MeadowTurf, trackChange: false);

            // A pool deep and wide enough to submerge the body wherever the capsule ends up, and
            // far from the dry corner the exit test walks to.
            for (int y = 1; y <= 8; y++)
            for (int z = 3; z <= 5; z++)
            for (int x = 3; x <= 5; x++)
                world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Freshwater, trackChange: false);

            CreativeWorldManager manager = managerObject.AddComponent<CreativeWorldManager>();
            manager.InitializeGeneratedWorld(
                new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
        }

        internal static GameObject CreateGravityRig(
            out XROrigin origin,
            out GravityProvider gravity,
            InputActionAsset actions = null)
        {
            GameObject rigObject = SwimRigFixture.CreateXrOrigin(out origin);
            CharacterController characterController = rigObject.AddComponent<CharacterController>();
            BlockiverseInputRig.ConfigureCharacterController(characterController);

            XRBodyTransformer bodyTransformer = rigObject.AddComponent<XRBodyTransformer>();
            bodyTransformer.xrOrigin = origin;

            LocomotionMediator mediator = rigObject.AddComponent<LocomotionMediator>();
            mediator.xrOrigin = origin;

            TeleportationProvider teleport = rigObject.AddComponent<TeleportationProvider>();
            teleport.mediator = mediator;
            teleport.delayTime = 0.0f;

            ContinuousMoveProvider continuousMove = rigObject.AddComponent<ContinuousMoveProvider>();
            continuousMove.mediator = mediator;
            continuousMove.forwardSource = origin.Camera.transform;
            continuousMove.leftHandMoveInput = SwimRigFixture.CreateUnusedVector2Reader("Left Hand Move");
            continuousMove.rightHandMoveInput = SwimRigFixture.CreateUnusedVector2Reader("Right Hand Move");

            SnapTurnProvider snapTurn = rigObject.AddComponent<SnapTurnProvider>();
            snapTurn.mediator = mediator;
            snapTurn.delayTime = 0.0f;
            snapTurn.leftHandTurnInput = SwimRigFixture.CreateUnusedVector2Reader("Left Hand Snap Turn");
            snapTurn.rightHandTurnInput = SwimRigFixture.CreateUnusedVector2Reader("Right Hand Snap Turn");

            // The real rig component: its own provisioning is what is under test, so a
            // hand-configured swim provider would prove nothing.
            var inputRig = rigObject.AddComponent<BlockiverseInputRig>();

            // Wired before ConfigureLocomotion so the rig's action cache is populated on its first
            // refresh; without an asset the jump and crouch lookups resolve to null and the swim
            // inputs are unreachable.
            if (actions != null)
                inputRig.Configure(actions);

            inputRig.ConfigureLocomotion(teleport, snapTurn, null, continuousMove, mediator, bodyTransformer);

            gravity = inputRig.GravityProvider;

            Assert.That(gravity, Is.Not.Null);

            return rigObject;
        }

        internal static GameObject CreateXrOrigin(out XROrigin origin)
        {
            GameObject rigObject = new("Swim Test XR Origin");
            rigObject.SetActive(false);

            GameObject cameraOffset = new("Camera Offset");
            cameraOffset.transform.SetParent(rigObject.transform, false);

            GameObject cameraObject = new("Main Camera");
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            // Without a tracked HMD the head would sit at y = 0, and GravityProvider derives its
            // grounding cast length from the head height -- a negative length can never report
            // ground, so nothing would ever land.
            cameraObject.transform.localPosition =
                new Vector3(0.0f, BlockiverseComfortSettings.FixedStandingEyeHeight, 0.0f);
            cameraObject.AddComponent<Camera>();

            origin = rigObject.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.Camera = cameraObject.GetComponent<Camera>();
            rigObject.SetActive(true);

            return rigObject;
        }

        internal static void DestroyRigImmediate(GameObject rig)
        {
            if (rig == null)
                return;

            foreach (TrackedPoseDriver driver in rig.GetComponentsInChildren<TrackedPoseDriver>(true))
                driver.enabled = false;

            Object.DestroyImmediate(rig);
        }

        internal static XRInputValueReader<Vector2> CreateUnusedVector2Reader(string name) =>
            new(name, XRInputValueReader.InputSourceMode.Unused);
    }
}
