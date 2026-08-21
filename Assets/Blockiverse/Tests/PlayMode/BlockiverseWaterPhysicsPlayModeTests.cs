using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace Blockiverse.Tests.PlayMode
{
    // Fluid physics layer behaviour with the real XRI locomotion stack: fluid-layer colliders
    // must be visible to targeting/teleport queries but never act as ground or a contact
    // obstacle, and a dive into water must not charge fall damage while emberflow still does.
    // These tests assert POST-BOOTSTRAP project state: the DynamicsManager collision-matrix row
    // for the fluid layer is cleared by BlockiverseProjectBootstrapper.ConfigureFluidLayerCollisionMatrix,
    // so each physics test pins Physics.GetIgnoreLayerCollision first to make a missing bootstrap
    // run read as the actual cause instead of a mystery fall-through failure.
    public sealed class BlockiverseWaterPhysicsPlayModeTests
    {
        static int sandboxSceneCounter;

        // Falling-rig tests are hypersensitive to leaked interaction-layer geometry, and earlier
        // fixtures in the same play session leave the Boot scene loaded (a 128x256x128 title
        // world with terrain colliders, a void safety catch floor, and a tagged MainCamera).
        // Start every test in a fresh empty sandbox scene instead, using the same
        // CreateScene/SetActiveScene/UnloadSceneAsync idiom as the locomotion fixture's Boot
        // cleanup. Tests additionally park their scenery at distinct X offsets as defence in
        // depth against anything leaked within this fixture.
        [UnitySetUp]
        public IEnumerator IsolateSandboxScene()
        {
            // GravityProvider integrates fall velocity with wall-clock Time.deltaTime every
            // Update (GravityProvider.Update -> TryProcessGravity(Time.deltaTime)), but batchmode
            // -nographics PlayMode frames run unthrottled at well under a millisecond each, so a
            // frame-count budget written against editor frame rates starves the fall of simulated
            // time on the CI gate: 600 yielded frames cover ~0.5 s of fall (~1 m) instead of 10 s.
            // Pin every yielded frame to a deterministic 60 Hz step so the frame budgets below
            // mean the same simulated time in the editor and in batchmode. Reset in teardown.
            Time.captureDeltaTime = 1.0f / 60.0f;

            yield return BlockiversePlayModeSceneTestUtility.CleanupTrackedPoseDrivers();

            Scene sandboxScene = SceneManager.CreateScene($"Water Physics Sandbox {sandboxSceneCounter++}");
            SceneManager.SetActiveScene(sandboxScene);

            var scenesToUnload = new List<Scene>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);

                // Never unload the Unity Test Framework's own scene: it hosts the test-runner
                // objects that pump this coroutine. Unloading it kills the runner mid-yield and
                // the setup never resumes — the player loop then spins forever with no test
                // progress. This only bites when the fixture runs standalone (-testFilter),
                // where the scene list is exactly InitTestScene + the sandbox; in a full-suite
                // run earlier fixtures have already replaced the scene inventory.
                if (scene.name.StartsWith("InitTestScene", System.StringComparison.Ordinal))
                    continue;

                if (scene != sandboxScene)
                    scenesToUnload.Add(scene);
            }

            foreach (Scene scene in scenesToUnload)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                if (unload != null)
                    yield return unload;
            }
        }

        [UnityTest]
        public IEnumerator PlayerFallsThroughAWaterSurfaceInsteadOfStandingOnIt()
        {
            GameObject rigObject = null;
            GameObject waterSurface = null;
            GameObject seabedFloor = null;

            try
            {
                rigObject = CreateGravityRig(out XROrigin origin, out GravityProvider gravity);

                // Root-cause pin: the runtime-configured gravity mask is solid terrain only.
                // Widening it to include the fluid layer is exactly what made the player walk
                // on water (GravityProvider grounds with a scene sphere-cast, and scene queries
                // ignore Collider.excludeLayers).
                Assert.That(gravity.sphereCastLayerMask.value, Is.EqualTo(BlockiverseProject.VoxelGroundLayerMask),
                    "Gravity grounding must only test solid voxel terrain; any extra layer can read as ground.");
                Assert.That(gravity.sphereCastLayerMask.value & BlockiverseProject.FluidLayerMask, Is.EqualTo(0),
                    "Gravity grounding must exclude the fluid layer, otherwise the player stands on water again.");

                const float waterTopY = 3.0f;
                waterSurface = CreateFluidSlab("Water Surface", new Vector3(100.0f, waterTopY - 0.5f, 0.0f), new Vector3(20.0f, 1.0f, 20.0f));
                seabedFloor = CreateInteractionFloor("Seabed Floor", new Vector3(100.0f, -0.5f, 0.0f), new Vector3(20.0f, 1.0f, 20.0f));
                rigObject.transform.position = new Vector3(100.0f, 5.0f, 0.0f);
                Physics.SyncTransforms();

                Assert.That(Physics.GetIgnoreLayerCollision(waterSurface.layer, rigObject.layer), Is.True,
                    "ProjectSettings/DynamicsManager.asset must ignore fluid-vs-player collisions (bootstrapper ConfigureFluidLayerCollisionMatrix); if this fails the bootstrap regeneration has not landed and every fall-through assertion below fails for that reason.");

                // First frames absorb gravity snap-down/CharacterController settling from scene
                // state leaked by earlier tests in the same play session (sibling-test pattern).
                yield return null;
                yield return null;

                bool crossedWaterPlane = false;
                bool landed = false;
                float previousY = origin.transform.position.y;

                for (int frame = 0; frame < 600; frame++)
                {
                    yield return null;
                    float currentY = origin.transform.position.y;

                    Assert.That(currentY, Is.LessThanOrEqualTo(previousY + 0.001f),
                        "The rig must keep descending until it lands; hovering above the seabed means the water plane is load-bearing again.");

                    if (currentY < waterTopY - 0.5f)
                        crossedWaterPlane = true;

                    if (gravity.isGrounded)
                    {
                        Assert.That(currentY, Is.LessThan(waterTopY - 1.0f),
                            "The gravity provider grounded at the water surface — fluid counted as ground (the walking-on-water regression).");
                        landed = true;
                        break;
                    }

                    previousY = currentY;
                }

                Assert.That(crossedWaterPlane, Is.True,
                    "The rig should fall straight through the fluid-layer water plane instead of standing on it.");
                Assert.That(landed, Is.True,
                    "The fall must terminate on the interaction-layer floor below the water within the frame budget.");

                yield return null;
                yield return null;

                Assert.That(origin.transform.position.y, Is.EqualTo(0.0f).Within(0.25f),
                    "After passing through the water the rig should come to rest on the solid seabed floor top.");
            }
            finally
            {
                if (waterSurface != null)
                    Object.DestroyImmediate(waterSurface);
                if (seabedFloor != null)
                    Object.DestroyImmediate(seabedFloor);
                DestroyRigImmediate(rigObject);
            }
        }

        [UnityTest]
        public IEnumerator WaterDoesNotGroundTheGravityProvider()
        {
            GameObject rigObject = null;
            GameObject waterSurface = null;

            try
            {
                rigObject = CreateGravityRig(out XROrigin origin, out GravityProvider gravity);

                // Water top at y = 0 with nothing solid beneath: the pre-fix bug grounded the
                // rig right here, on the surface.
                waterSurface = CreateFluidSlab("Bottomless Water", new Vector3(200.0f, -0.5f, 0.0f), new Vector3(20.0f, 1.0f, 20.0f));
                rigObject.transform.position = new Vector3(200.0f, 0.05f, 0.0f);
                Physics.SyncTransforms();

                Assert.That(Physics.GetIgnoreLayerCollision(waterSurface.layer, rigObject.layer), Is.True,
                    "ProjectSettings/DynamicsManager.asset must ignore fluid-vs-player collisions (bootstrapper ConfigureFluidLayerCollisionMatrix); if this fails the bootstrap regeneration has not landed.");

                for (int frame = 0; frame < 120; frame++)
                {
                    yield return null;
                    Assert.That(gravity.isGrounded, Is.False,
                        "A fluid-layer collider must never register as ground: grounding here is the walking-on-water regression.");
                }

                Assert.That(origin.transform.position.y, Is.LessThan(-1.0f),
                    "With only water beneath it the rig should sink well below the surface instead of being held up.");
            }
            finally
            {
                if (waterSurface != null)
                    Object.DestroyImmediate(waterSurface);
                DestroyRigImmediate(rigObject);
            }
        }

        [UnityTest]
        public IEnumerator FluidColliderIsQueryVisibleButNotCollidable()
        {
            GameObject waterSlab = null;
            GameObject fluidWall = null;
            GameObject capsuleObject = null;

            try
            {
                waterSlab = CreateFluidSlab("Query Probe Water", new Vector3(300.0f, 2.5f, 0.0f), new Vector3(10.0f, 1.0f, 10.0f));
                fluidWall = CreateFluidSlab("Fluid Wall", new Vector3(300.0f, 10.0f, 2.0f), new Vector3(4.0f, 4.0f, 1.0f));

                capsuleObject = new GameObject("Fluid Sweep Capsule");
                capsuleObject.transform.position = new Vector3(300.0f, 10.0f, 0.0f);
                CharacterController capsule = capsuleObject.AddComponent<CharacterController>();
                BlockiverseInputRig.ConfigureCharacterController(capsule);
                Physics.SyncTransforms();

                // The pass-through sweep below depends on the DynamicsManager collision-matrix
                // row the bootstrapper lands (the production fluid collider also sets
                // excludeLayers, mirrored by CreateFluidSlab, but the matrix is the project-wide
                // guarantee); pin it first so a missing bootstrap run reads as the actual cause.
                Assert.That(Physics.GetIgnoreLayerCollision(fluidWall.layer, capsuleObject.layer), Is.True,
                    "ProjectSettings/DynamicsManager.asset must ignore fluid-vs-player collisions (bootstrapper ConfigureFluidLayerCollisionMatrix); if this fails the bootstrap regeneration has not landed and the sweep below would report a phantom wall hit.");

                yield return null;

                // (a) Gravity-style grounding query: solid-terrain mask only, must miss fluid.
                var downwardProbe = new Ray(new Vector3(300.0f, 6.0f, 0.0f), Vector3.down);
                bool groundMaskHit = Physics.SphereCast(
                    downwardProbe,
                    0.3f,
                    out RaycastHit _,
                    10.0f,
                    BlockiverseProject.VoxelGroundLayerMask,
                    QueryTriggerInteraction.Ignore);
                Assert.That(groundMaskHit, Is.False,
                    "A sphere-cast with the voxel ground mask must not see the fluid collider; seeing it is exactly how the player walked on water.");

                // (b) Targeting/teleport query: the widened ray mask must land on the water
                // surface. Scene queries ignore Collider.excludeLayers, so this also settles
                // empirically that excludeLayers hides fluid from contacts, not from queries.
                bool targetingHit = Physics.Raycast(
                    downwardProbe.origin,
                    Vector3.down,
                    out RaycastHit surfaceHit,
                    10.0f,
                    BlockiverseProject.VrUiRaycastLayerMask);
                Assert.That(targetingHit, Is.True,
                    "The teleport/interaction ray mask must hit fluid so rays land on the water surface instead of punching through to the seabed.");
                Assert.That(surfaceHit.collider.gameObject, Is.SameAs(waterSlab),
                    "The targeting ray should report the water slab itself as the surface it landed on.");
                Assert.That(surfaceHit.point.y, Is.EqualTo(3.0f).Within(0.01f),
                    "The targeting ray should stop at the water surface plane, not inside or below it.");

                // (c) Contact sweep: a CharacterController pushed sideways through a vertical
                // fluid wall must pass through with no side collision.
                CollisionFlags sweepFlags = capsule.Move(new Vector3(0.0f, 0.0f, 4.0f));

                Assert.That(sweepFlags & CollisionFlags.Sides, Is.EqualTo(CollisionFlags.None),
                    "Sweeping through a vertical fluid collider must not report side contact; fluid is not an obstacle.");
                Assert.That(capsuleObject.transform.position.z, Is.EqualTo(4.0f).Within(0.01f),
                    "The capsule should advance its full sweep distance through the fluid wall instead of being blocked by it.");
            }
            finally
            {
                if (capsuleObject != null)
                    Object.DestroyImmediate(capsuleObject);
                if (fluidWall != null)
                    Object.DestroyImmediate(fluidWall);
                if (waterSlab != null)
                    Object.DestroyImmediate(waterSlab);
            }
        }

        [UnityTest]
        public IEnumerator DivingIntoWaterDoesNotChargeFallDamageButDivingIntoEmberflowDoes()
        {
            BlockiverseRuntimeState.Reset();

            var managerObject = new GameObject("Water Vitals World Manager");
            var syncObject = new GameObject("Water Vitals Survival Sync");
            var vitalsObject = new GameObject("Water Vitals Runtime");
            GameObject rigObject = null;
            GameObject landingFloor = null;
            GameObject controlFloor = null;

            try
            {
                CreativeWorldManager worldManager = CreateSurvivalWorldWithFluidColumns(managerObject);

                // MultiplayerSurvivalSync.Configure is not callable from this assembly (its
                // optional parameters name Blockiverse.Survival types the PlayMode asmdef
                // deliberately does not reference); the bare component self-resolves in Awake
                // and its mode switch already defaults to Survival, which is all TickFallDamage
                // needs.
                MultiplayerSurvivalSync survivalSync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                Assert.That(survivalSync.CurrentMode, Is.EqualTo(PlayerModeState.Survival),
                    "Fall damage only ticks in survival mode, so the sync must start there for this test to mean anything.");

                rigObject = CreateGravityRig(out XROrigin origin, out GravityProvider gravity);
                rigObject.AddComponent<BlockiversePlayerRigAnchor>();

                SurvivalVitalsRuntime vitalsRuntime = vitalsObject.AddComponent<SurvivalVitalsRuntime>();
                vitalsRuntime.Configure(survivalSync, worldManager);

                // Interaction-layer landing floor with its top at y = 1.05, just above the voxel
                // ground fill (whose block tops sit at y = 1), so landings are deterministic and
                // never race the renderer's throttled voxel collider bakes. Resting on it puts
                // the capsule's feet sample inside voxel cell y = 1 — the bottom cell of both
                // fluid columns.
                landingFloor = CreateInteractionFloor("Vitals Landing Floor", new Vector3(8.0f, 0.55f, 8.0f), new Vector3(40.0f, 1.0f, 40.0f));
                controlFloor = CreateInteractionFloor("Control Landing Floor", new Vector3(300.0f, 0.55f, 0.0f), new Vector3(20.0f, 1.0f, 20.0f));

                // GUARD (control dive): prove the fall-damage path is alive before testing any
                // fluid behaviour. Without this, a silently disabled vitals path (any early-out
                // gate in SurvivalVitalsRuntime.TickFallDamage) makes the freshwater assertion
                // below pass vacuously — health stays unchanged because nothing can charge
                // damage, not because water cancelled the fall. Exactly that vacuity hid the
                // GravityProvider hover-landing bug (grounding cut gravity with the capsule
                // ~5 mm off the floor, so CharacterController.isGrounded never became true).
                // The control dive lands OUTSIDE the voxel world bounds: an in-world "bare"
                // column is not bare for long — the live fluid simulation spread freshwater
                // under the old (2, 1, 2) control column mid-test and legitimately cancelled
                // the fall. Outside the bounds no cell can ever hold fluid, so this dive can
                // only be cancelled by a dead vitals path.
                rigObject.transform.position = new Vector3(300.0f, 8.0f, 0.0f);
                Physics.SyncTransforms();
                int healthBeforeControlDive = GetCurrentHealth(vitalsRuntime);

                yield return WaitForLanding(gravity);

                Assert.That(GetCurrentHealth(vitalsRuntime), Is.LessThan(healthBeforeControlDive),
                    "Control dive onto bare ground charged no fall damage: the vitals fall-damage path is dead in this harness, so the fluid assertions below would be vacuous.");

                vitalsRuntime.ResetVitalsToFull();

                // Freshwater dive: an ~7 m fall (well past the 3 m safe distance) into the
                // wade-depth freshwater at (1, 1, 1).
                rigObject.transform.position = new Vector3(1.5f, 8.0f, 1.5f);
                Physics.SyncTransforms();
                int healthBeforeWaterDive = GetCurrentHealth(vitalsRuntime);

                yield return WaitForLanding(gravity);

                int healthAfterWaterDive = GetCurrentHealth(vitalsRuntime);
                Assert.That(origin.transform.position.y, Is.EqualTo(1.05f).Within(0.25f),
                    "Sanity: the freshwater dive must actually land on the seabed floor under the water column, otherwise no fall damage was ever at stake.");
                Assert.That(healthAfterWaterDive, Is.EqualTo(healthBeforeWaterDive),
                    "Freshwater at the feet must cancel the tracked fall (ruleset section 5.6): the dive would otherwise charge impact damage on the seabed.");

                // Emberflow dive: an identical fall into the emberflow at (14, 1, 14) must keep
                // charging fall damage. It is a permanent source, and the freshwater corner is
                // beyond spread range, so this cell stays emberflow for the whole test.
                vitalsRuntime.ResetVitalsToFull();
                rigObject.transform.position = new Vector3(14.5f, 8.0f, 14.5f);
                Physics.SyncTransforms();
                yield return null;
                int healthBeforeEmberflowDive = GetCurrentHealth(vitalsRuntime);

                yield return WaitForLanding(gravity);

                int healthAfterEmberflowDive = GetCurrentHealth(vitalsRuntime);
                Assert.That(origin.transform.position.y, Is.EqualTo(1.05f).Within(0.25f),
                    "Sanity: the emberflow dive must land on the same seabed floor so the two dives are comparable.");
                Assert.That(healthAfterEmberflowDive, Is.LessThan(healthBeforeEmberflowDive),
                    "Emberflow must not break a fall (ruleset section 5.6): an identical dive has to keep charging impact damage.");
            }
            finally
            {
                if (landingFloor != null)
                    Object.DestroyImmediate(landingFloor);
                if (controlFloor != null)
                    Object.DestroyImmediate(controlFloor);
                DestroyRigImmediate(rigObject);
                Object.DestroyImmediate(vitalsObject);
                Object.DestroyImmediate(syncObject);
                // Also removes the renderer chunks and the void safety floor the world manager
                // parents under itself; leaking that interaction-layer catch floor would ground
                // the falling rigs of the other tests in this fixture.
                Object.DestroyImmediate(managerObject);
                BlockiverseRuntimeState.Reset();
            }
        }

        static IEnumerator WaitForLanding(GravityProvider gravity)
        {
            for (int frame = 0; frame < 600; frame++)
            {
                yield return null;
                if (gravity.isGrounded)
                    break;
            }

            Assert.That(gravity.isGrounded, Is.True,
                "The rig should land on the interaction-layer floor within the frame budget; never grounding means the gravity/collision wiring is broken.");

            // Let SurvivalVitalsRuntime observe the grounded CharacterController: script update
            // order between the gravity provider and the vitals runtime is unspecified.
            for (int frame = 0; frame < 5; frame++)
                yield return null;
        }

        static GameObject CreateGravityRig(out XROrigin origin, out GravityProvider gravity)
        {
            GameObject rigObject = CreateXrOrigin(out origin);
            ConfigureXriLocomotionStack(
                rigObject,
                origin,
                out XRBodyTransformer bodyTransformer,
                out LocomotionMediator mediator,
                out TeleportationProvider teleport,
                out ContinuousMoveProvider continuousMove,
                out SnapTurnProvider snapTurn);

            // The real runtime rig component: its EnsureXriLocomotionProviders auto-provisions
            // the GravityProvider with the production grounding mask, which is the behaviour
            // under test — a hand-configured provider would prove nothing.
            var inputRig = rigObject.AddComponent<BlockiverseInputRig>();
            inputRig.ConfigureLocomotion(teleport, snapTurn, null, continuousMove, mediator, bodyTransformer);

            gravity = inputRig.GravityProvider;
            Assert.That(gravity, Is.Not.Null,
                "BlockiverseInputRig should auto-provision a GravityProvider; without it there is no falling to test.");
            return rigObject;
        }

        static GameObject CreateXrOrigin(out XROrigin origin)
        {
            GameObject rigObject = new("Test XR Origin");
            rigObject.SetActive(false);

            GameObject cameraOffset = new("Camera Offset");
            cameraOffset.transform.SetParent(rigObject.transform, false);

            GameObject cameraObject = new("Main Camera");
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            // GravityProvider's grounding sphere-cast length is derived from the head height:
            // (CameraInOriginSpaceHeight + sphereCastDistanceBuffer) * scale, with a -0.05 default
            // buffer (GravityProvider.CheckGrounded). With no HMD driving the camera it would sit
            // at y = 0, making that a negative-length cast that can never report ground, so
            // isGrounded could never terminate a fall. Give the head the production standing eye
            // height, exactly what the tracked HMD (normalized by BlockiverseHeightReset) provides
            // on the real Boot rig. The head TrackedPoseDriver the rig adds preserves this: with
            // no resolved XR controls its first update captures the current localPosition as the
            // pose it keeps re-applying, so the height set here persists for the whole test.
            cameraObject.transform.localPosition =
                new Vector3(0.0f, BlockiverseComfortSettings.FixedStandingEyeHeight, 0.0f);
            Camera camera = cameraObject.AddComponent<Camera>();

            origin = rigObject.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.Camera = camera;
            rigObject.SetActive(true);

            return rigObject;
        }

        static void ConfigureXriLocomotionStack(
            GameObject rigObject,
            XROrigin origin,
            out XRBodyTransformer bodyTransformer,
            out LocomotionMediator mediator,
            out TeleportationProvider teleport,
            out ContinuousMoveProvider continuousMove,
            out SnapTurnProvider snapTurn)
        {
            CharacterController characterController = rigObject.GetComponent<CharacterController>();

            if (characterController == null)
                characterController = rigObject.AddComponent<CharacterController>();

            BlockiverseInputRig.ConfigureCharacterController(characterController);

            bodyTransformer = rigObject.AddComponent<XRBodyTransformer>();
            bodyTransformer.xrOrigin = origin;

            mediator = rigObject.AddComponent<LocomotionMediator>();
            mediator.xrOrigin = origin;

            teleport = rigObject.AddComponent<TeleportationProvider>();
            teleport.mediator = mediator;
            teleport.delayTime = 0.0f;

            continuousMove = rigObject.AddComponent<ContinuousMoveProvider>();
            continuousMove.mediator = mediator;
            continuousMove.forwardSource = origin.Camera.transform;
            continuousMove.enableStrafe = true;
            continuousMove.enableFly = false;
            continuousMove.leftHandMoveInput = CreateUnusedVector2Reader("Left Hand Move");
            continuousMove.rightHandMoveInput = CreateUnusedVector2Reader("Right Hand Move");

            snapTurn = rigObject.AddComponent<SnapTurnProvider>();
            snapTurn.mediator = mediator;
            snapTurn.enableTurnLeftRight = true;
            snapTurn.enableTurnAround = true;
            snapTurn.delayTime = 0.0f;
            snapTurn.leftHandTurnInput = CreateUnusedVector2Reader("Left Hand Snap Turn");
            snapTurn.rightHandTurnInput = CreateUnusedVector2Reader("Right Hand Snap Turn");
        }

        static void DestroyRigImmediate(GameObject rigObject)
        {
            if (rigObject == null)
                return;

            foreach (TrackedPoseDriver driver in rigObject.GetComponentsInChildren<TrackedPoseDriver>(true))
                driver.enabled = false;

            Object.DestroyImmediate(rigObject);
        }

        static XRInputValueReader<Vector2> CreateUnusedVector2Reader(string name)
        {
            return new XRInputValueReader<Vector2>(name, XRInputValueReader.InputSourceMode.Unused);
        }

        static int ResolveFluidLayerIndex()
        {
            int layer = LayerMask.NameToLayer(BlockiverseProject.FluidLayerName);
            return layer >= 0 ? layer : BlockiverseProject.FluidLayerIndex;
        }

        static GameObject CreateFluidSlab(string name, Vector3 center, Vector3 scale)
        {
            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.layer = ResolveFluidLayerIndex();
            slab.transform.localScale = scale;
            slab.transform.position = center;
            // Mirror the production fluid collider (VoxelWorldRenderer): contact collisions are
            // excluded wholesale; scene queries ignore excludeLayers by design, which is why the
            // layer-mask split and the DynamicsManager row clear matter at all.
            slab.GetComponent<Collider>().excludeLayers = ~0;
            return slab;
        }

        static GameObject CreateInteractionFloor(string name, Vector3 center, Vector3 scale)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = name;
            floor.layer = BlockiverseProject.InteractionLayerIndex;
            floor.transform.localScale = scale;
            floor.transform.position = center;
            return floor;
        }

        static CreativeWorldManager CreateSurvivalWorldWithFluidColumns(GameObject managerObject)
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            // 16x16 footprint with the two sources at opposite corners (Manhattan distance 26):
            // freshwater spreads at most 8 cells horizontally (ruleset section 5.4),
            // so the live FluidFlowService this manager ticks can flood each corner locally but
            // the fluids can never meet — no quench, and each dive's landing cell is a PERMANENT
            // source block rather than a cell the simulation rewrites mid-test. A 6x6 world made
            // the old control column (2,1,2) flood with spreading freshwater, which legitimately
            // cancelled the control dive's fall (water breaks falls) and read as zero damage.
            var settings = new WorldGenerationSettings(16, 12, 16, chunkSize: 4, seed: 59, groundHeight: 1, spawnPosition: new BlockPosition(8, 2, 8));
            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);

            for (int z = 0; z < settings.Bounds.Depth; z++)
            for (int x = 0; x < settings.Bounds.Width; x++)
                world.SetBlock(new BlockPosition(x, 0, z), BlockRegistry.MeadowTurf, trackChange: false);

            // ONE cell deep, deliberately. Both dives have to end in a real landing for the fall
            // damage comparison to mean anything, and since swim locomotion landed a column deep
            // enough to submerge the body locks gravity off and the player sinks instead of falling
            // -- correct behaviour, but it turns this test into a 25-second wait for a slow descent.
            // At one cell the feet sample lands inside fluid while the body sample stays in air, so
            // the player wades: gravity stays on, the landing is immediate, and the feet-in-fluid
            // rule this test exists to pin is exercised exactly as before. Deepening these columns
            // will hang the test rather than fail it cleanly.
            world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Freshwater, trackChange: false);
            world.SetBlock(new BlockPosition(14, 1, 14), BlockRegistry.Emberflow, trackChange: false);

            CreativeWorldManager manager = managerObject.AddComponent<CreativeWorldManager>();
            manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
            manager.SetGameMode(WorldGameMode.Survival);
            return manager;
        }

        // Blockiverse.Tests.PlayMode deliberately does not reference Blockiverse.Survival.Health,
        // so PlayerVitals cannot be named at compile time (and asmdef edits are out of scope).
        // Both members read here are public; reflection only bridges the assembly-reference gap.
        static int GetCurrentHealth(SurvivalVitalsRuntime runtime)
        {
            PropertyInfo vitalsProperty = typeof(SurvivalVitalsRuntime).GetProperty("Vitals", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(vitalsProperty, Is.Not.Null, "SurvivalVitalsRuntime.Vitals should exist for health observation.");

            object vitals = vitalsProperty.GetValue(runtime);
            Assert.That(vitals, Is.Not.Null, "SurvivalVitalsRuntime.Vitals should never be null.");

            PropertyInfo healthProperty = vitals.GetType().GetProperty("CurrentHealth", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(healthProperty, Is.Not.Null, "PlayerVitals.CurrentHealth should exist for health observation.");

            return (int)healthProperty.GetValue(vitals);
        }

        [UnityTearDown]
        public IEnumerator CleanupTrackedPoseDriversAfterTest()
        {
            // Undo the deterministic frame step pinned in IsolateSandboxScene before any other
            // fixture runs; teardown runs even when a test fails mid-yield.
            Time.captureDeltaTime = 0.0f;
            yield return BlockiversePlayModeSceneTestUtility.CleanupTrackedPoseDrivers();
        }
    }
}
