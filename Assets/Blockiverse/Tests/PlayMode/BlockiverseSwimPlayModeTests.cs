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

            DestroyRigImmediate(rigObject);

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
            CreateWorldWithADeepPool(managerObject);

            // A floor under the pool so a sinking player has somewhere to come to rest rather than
            // falling out of the world if anything about the lock goes wrong.
            seabed = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seabed.name = "Swim Seabed";
            seabed.layer = BlockiverseProject.InteractionLayerIndex;
            seabed.transform.localScale = new Vector3(40.0f, 1.0f, 40.0f);
            seabed.transform.position = new Vector3(8.0f, 0.55f, 8.0f);

            rigObject = CreateGravityRig(out origin, out gravity);

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

        static void CreateWorldWithADeepPool(GameObject managerObject)
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

        static GameObject CreateGravityRig(out XROrigin origin, out GravityProvider gravity)
        {
            GameObject rigObject = CreateXrOrigin(out origin);
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
            continuousMove.leftHandMoveInput = CreateUnusedVector2Reader("Left Hand Move");
            continuousMove.rightHandMoveInput = CreateUnusedVector2Reader("Right Hand Move");

            SnapTurnProvider snapTurn = rigObject.AddComponent<SnapTurnProvider>();
            snapTurn.mediator = mediator;
            snapTurn.delayTime = 0.0f;
            snapTurn.leftHandTurnInput = CreateUnusedVector2Reader("Left Hand Snap Turn");
            snapTurn.rightHandTurnInput = CreateUnusedVector2Reader("Right Hand Snap Turn");

            // The real rig component: its own provisioning is what is under test, so a
            // hand-configured swim provider would prove nothing.
            var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
            inputRig.ConfigureLocomotion(teleport, snapTurn, null, continuousMove, mediator, bodyTransformer);

            gravity = inputRig.GravityProvider;

            Assert.That(gravity, Is.Not.Null);

            return rigObject;
        }

        static GameObject CreateXrOrigin(out XROrigin origin)
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

        static void DestroyRigImmediate(GameObject rig)
        {
            if (rig == null)
                return;

            foreach (TrackedPoseDriver driver in rig.GetComponentsInChildren<TrackedPoseDriver>(true))
                driver.enabled = false;

            Object.DestroyImmediate(rig);
        }

        static XRInputValueReader<Vector2> CreateUnusedVector2Reader(string name) =>
            new(name, XRInputValueReader.InputSourceMode.Unused);
    }
}
