using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using Blockiverse.Core;
using Blockiverse.Editor;
using Blockiverse.MetaPlatform;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;
using Object = UnityEngine.Object;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseBootstrapEditModeTests
    {
        const string BootScenePath = "Assets/Blockiverse/Scenes/Boot.unity";
        const string XrRigPrefabPath = "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab";
        const string AndroidUrpAssetPath = "Assets/Blockiverse/Settings/BlockiverseAndroidURPAsset.asset";
        const string VoxelShaderPath = "Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader";
        const string PerformanceStatsOverlayPath = "Assets/Blockiverse/Scripts/Gameplay/PerformanceStatsOverlay.cs";
        const string BlockiverseInputRigPath = "Assets/Blockiverse/Scripts/VR/BlockiverseInputRig.cs";
        const string BlockiverseControllerHapticsPath = "Assets/Blockiverse/Scripts/VR/BlockiverseControllerHaptics.cs";
        const string AndroidActivityTitleRuntimePath = "Assets/Blockiverse/Scripts/Core/BlockiverseAndroidActivityTitle.cs";
        const string AndroidManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        const string OculusProjectConfigPath = "Assets/Oculus/OculusProjectConfig.asset";
        const string BlockiverseXrRigPrefabPath = "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab";
        const string LegacyAndroidResourcePath = "Assets/Plugins/Android/res";
        const string OculusRuntimeSettingsPath = "Assets/Resources/OculusRuntimeSettings.asset";
        const string VersionSettingsPath = "ProjectSettings/ProjectVersion.txt";
        const string NetcodeProjectSettingsPath = "ProjectSettings/NetcodeForGameObjects.asset";
        const string PlayerProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";
        const string ManifestPath = "Packages/manifest.json";
        const string XrGeneralSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        const string BuildSmokePath = "Assets/Blockiverse/Scripts/Editor/BlockiverseBuildSmoke.cs";
        const string ProjectBootstrapperPath = "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs";
        const string SceneBootstrapperPath = "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Scenes.cs";
        const string MenuBootstrapperPath = "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.Menus.cs";
        const string XrRigBootstrapperPath = "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.XrRig.cs";
        static readonly string[] EngineFreeAsmdefPaths =
        {
            "Assets/Blockiverse/Scripts/Voxel/Blockiverse.Voxel.asmdef",
            "Assets/Blockiverse/Scripts/Survival/Blockiverse.Survival.asmdef",
            "Assets/Blockiverse/Scripts/SurvivalHealth/Blockiverse.Survival.Health.asmdef",
            "Assets/Blockiverse/Scripts/WorldGen/Blockiverse.WorldGen.asmdef",
        };

        [Test]
        public void UnityVersionIsPinnedToUnity6()
        {
            string versionSettings = File.ReadAllText(VersionSettingsPath);

            StringAssert.Contains("m_EditorVersion: 6000.", versionSettings);
        }

        [Test]
        public void RequiredPackagesAreDeclared()
        {
            string manifest = File.ReadAllText(ManifestPath);

            StringAssert.Contains("\"com.unity.render-pipelines.universal\"", manifest);
            StringAssert.Contains("\"com.unity.xr.openxr\"", manifest);
            StringAssert.Contains("\"com.unity.xr.meta-openxr\"", manifest);
            StringAssert.Contains("\"com.meta.xr.sdk.core\"", manifest);
            StringAssert.Contains("\"com.unity.inputsystem\"", manifest);
        }

        [Test]
        public void NetcodeDefaultNetworkPrefabGenerationIsDisabled()
        {
            Assert.That(File.Exists(NetcodeProjectSettingsPath), Is.True);
            string settings = File.ReadAllText(NetcodeProjectSettingsPath);

            StringAssert.Contains("GenerateDefaultNetworkPrefabs: 0", settings);
        }

        [Test]
        public void RepositoryUsesVisibleMetaFilesAndTextSerialization()
        {
            Assert.That(VersionControlSettings.mode, Is.EqualTo("Visible Meta Files"));
            Assert.That(EditorSettings.serializationMode, Is.EqualTo(SerializationMode.ForceText));
        }

        [Test]
        public void EngineFreeSimulationAssembliesRejectUnityEngineReferences()
        {
            foreach (string asmdefPath in EngineFreeAsmdefPaths)
            {
                string asmdef = File.ReadAllText(asmdefPath);
                Assert.That(asmdef, Does.Contain("\"noEngineReferences\": true"), asmdefPath);
            }
        }

        [Test]
        public void BootstrapAssetsExist()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(XrRigPrefabPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AndroidUrpAssetPath), Is.Not.Null);
        }

        [Test]
        public void FluidLayerIsPinnedToIndexThirteenWithTheCanonicalName()
        {
            Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");

            Assert.That(tagManagerAssets, Is.Not.Null.And.Not.Empty, "TagManager settings asset must be available.");

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            tagManager.UpdateIfRequiredOrScript();
            SerializedProperty layers = tagManager.FindProperty("layers");

            Assert.That(layers, Is.Not.Null, "TagManager must expose its layers array.");
            Assert.That(BlockiverseProject.FluidLayerIndex, Is.InRange(8, layers.arraySize - 1),
                "The fluid layer must live in the user-assignable layer range.");
            Assert.That(layers.GetArrayElementAtIndex(BlockiverseProject.FluidLayerIndex).stringValue,
                Is.EqualTo(BlockiverseProject.FluidLayerName),
                "The bootstrapper must pin the fluid layer name at the canonical index so name lookups and the cleared collision-matrix row address the same layer.");
        }

        [Test]
        public void PassableLayerIsPinnedToIndexFifteenWithTheCanonicalName()
        {
            Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");

            Assert.That(tagManagerAssets, Is.Not.Null.And.Not.Empty, "TagManager settings asset must be available.");

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            tagManager.UpdateIfRequiredOrScript();
            SerializedProperty layers = tagManager.FindProperty("layers");

            Assert.That(layers, Is.Not.Null, "TagManager must expose its layers array.");
            Assert.That(BlockiverseProject.PassableLayerIndex, Is.InRange(8, layers.arraySize - 1),
                "The passable layer must live in the user-assignable layer range.");
            // VoxelWorldRenderer resolves this layer BY NAME and silently falls back to the raw
            // index when the lookup fails. Without this assertion an unnamed layer 15 would let
            // the fallback mask the fact that the bootstrapper never registered it.
            Assert.That(layers.GetArrayElementAtIndex(BlockiverseProject.PassableLayerIndex).stringValue,
                Is.EqualTo(BlockiverseProject.PassableLayerName),
                "The bootstrapper must pin the passable layer name at the canonical index so LayerMask.NameToLayer, the culling mask, and the cleared collision-matrix row all address the same layer.");
        }

        [Test]
        public void InteractionRayMaskIncludesPassableAndTeleportMaskDoesNot()
        {
            Assert.That(BlockiverseProject.PassableLayerMask,
                Is.EqualTo(1 << BlockiverseProject.PassableLayerIndex),
                "The passable mask must be the single bit for its layer index.");

            // The asymmetry IS the contract: plants must be mineable, and a teleport arc must pass
            // through them to the ground. One shared mask cannot express both.
            Assert.That(BlockiverseProject.VoxelInteractionRaycastLayerMask & BlockiverseProject.PassableLayerMask,
                Is.Not.EqualTo(0),
                "The interaction ray must reach passable vegetation or no plant can be targeted, mined or harvested.");
            Assert.That(BlockiverseProject.VrUiRaycastLayerMask & BlockiverseProject.PassableLayerMask,
                Is.EqualTo(0),
                "The teleport/UI ray must NOT reach passable vegetation, or arcs land on top of grass instead of the ground beneath it.");
            Assert.That(BlockiverseProject.VoxelGroundLayerMask & BlockiverseProject.PassableLayerMask,
                Is.EqualTo(0),
                "Gravity's ground mask must never see passable geometry, or the player stands on grass.");
        }

        [Test]
        public void FluidLayerCollidesWithNothingInThePhysicsMatrix()
        {
            Object[] dynamicsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset");

            Assert.That(dynamicsAssets, Is.Not.Null.And.Not.Empty, "DynamicsManager settings asset must be available.");

            var dynamicsManager = new SerializedObject(dynamicsAssets[0]);
            dynamicsManager.UpdateIfRequiredOrScript();
            SerializedProperty collisionMatrix = dynamicsManager.FindProperty("m_LayerCollisionMatrix");

            Assert.That(collisionMatrix, Is.Not.Null, "DynamicsManager must expose m_LayerCollisionMatrix.");
            Assert.That(collisionMatrix.arraySize, Is.GreaterThan(BlockiverseProject.FluidLayerIndex),
                "The collision matrix must cover the fluid layer's row.");

            uint fluidRow = unchecked((uint)collisionMatrix
                .GetArrayElementAtIndex(BlockiverseProject.FluidLayerIndex).longValue);

            Assert.That(fluidRow, Is.EqualTo(0u),
                "The fluid layer's collision row must be empty so the player's CharacterController sweeps through water instead of standing on it.");

            // Passable vegetation is exempt for the same reason fluid is, so the "every other bit
            // must survive" half of this test has to allow BOTH bits to be clear — otherwise it
            // fails on every row the moment a second pass-through layer exists.
            uint fluidBit = 1u << BlockiverseProject.FluidLayerIndex;
            uint passThroughBits = fluidBit | (1u << BlockiverseProject.PassableLayerIndex);

            uint passableRow = unchecked((uint)collisionMatrix
                .GetArrayElementAtIndex(BlockiverseProject.PassableLayerIndex).longValue);

            Assert.That(passableRow, Is.EqualTo(0u),
                "The passable layer's collision row must be empty so the player walks through vegetation instead of into it.");

            for (int layer = 0; layer < collisionMatrix.arraySize; layer++)
            {
                if (layer == BlockiverseProject.FluidLayerIndex ||
                    layer == BlockiverseProject.PassableLayerIndex)
                    continue;

                uint mask = unchecked((uint)collisionMatrix.GetArrayElementAtIndex(layer).longValue);

                Assert.That(mask & passThroughBits, Is.EqualTo(0u),
                    $"Layer {layer} keeps a collision pair with a pass-through layer; the matrix is symmetric, so any surviving bit re-solidifies water or vegetation.");

                // The half this test originally missed (caught in PR review): clearing the fluid
                // bit must not disturb ANY other pair. An all-zero matrix satisfies the fluid
                // assertions above while silently letting players walk through walls — the
                // bootstrapper once produced exactly that when SerializedProperty.intValue
                // clamped its negative unsigned write to zero. Every non-fluid bit of every
                // non-fluid row must remain set.
                Assert.That(mask | passThroughBits, Is.EqualTo(uint.MaxValue),
                    $"Layer {layer} lost collision pairs (row 0x{mask:X8}); clearing the pass-through layers must leave every other layer pair colliding, or solid terrain stops being solid.");
            }
        }

        [Test]
        public void FluidLayerMaskConstantsKeepGroundAndRayTargetingRolesSeparate()
        {
            Assert.That(BlockiverseProject.FluidLayerMask, Is.EqualTo(1 << BlockiverseProject.FluidLayerIndex),
                "The fluid mask must address exactly the pinned fluid layer index.");
            Assert.That(BlockiverseProject.VrUiRaycastLayerMask,
                Is.EqualTo(BlockiverseProject.InteractionLayerMask | BlockiverseProject.FluidLayerMask),
                "Ray targeting must cover terrain plus fluid: block edits, drink/bucket fill, and teleport landing all hit water.");
            Assert.That(BlockiverseProject.VoxelGroundLayerMask & BlockiverseProject.FluidLayerMask, Is.EqualTo(0),
                "Ground detection must never include fluid; widening this mask reintroduces walking on water.");
        }

        [Test]
        public void AndroidUrpAssetUsesQuestMobileRenderDefaults()
        {
            string asset = File.ReadAllText(AndroidUrpAssetPath);

            AssertUrpValue(asset, "m_RequireDepthTexture", "0");
            AssertUrpValue(asset, "m_RequireOpaqueTexture", "0");
            AssertUrpValue(asset, "m_SupportsHDR", "0");
            AssertUrpValue(asset, "m_MSAA", "4");
            AssertUrpValue(asset, "m_RenderScale", "1");
            AssertUrpValue(asset, "m_UseAdaptivePerformance", "1");
        }

        [Test]
        public void AndroidUrpAssetEnablesQuestBudgetedShadowsAndAdditionalLights()
        {
            string asset = File.ReadAllText(AndroidUrpAssetPath);

            // Placed emitters (glowwick, lumen lamp, campfire, spark flare) only produce light
            // when the pipeline renders additional lights at all — 0 here means every point light
            // in the scene is silently discarded.
            AssertUrpValue(asset, "m_AdditionalLightsRenderingMode", "1");
            AssertUrpValue(
                asset,
                "m_AdditionalLightsPerObjectLimit",
                BlockiverseProjectBootstrapper.QuestAdditionalLightsPerObject.ToString());
            AssertUrpValue(asset, "m_AdditionalLightShadowsSupported", "1");

            AssertUrpValue(asset, "m_MainLightShadowsSupported", "1");
            AssertUrpValue(asset, "m_MainLightShadowmapResolution", "1024");
            AssertUrpValue(asset, "m_AdditionalLightsShadowmapResolution", "1024");

            // A zero shadow distance disables shadows just as completely as turning them off.
            AssertUrpValue(
                asset,
                "m_ShadowDistance",
                BlockiverseProjectBootstrapper.QuestShadowDistanceMeters.ToString("0.###"));
            AssertUrpValue(asset, "m_ShadowCascadeCount", "1");

            // Hard shadows only: Unity flags soft shadows as a significant cost on tile-based
            // mobile/XR GPUs, and Meta's mobile-VR guidance says hard-or-none.
            AssertUrpValue(asset, "m_SoftShadowsSupported", "0");

            // Unused atlases that only cost memory and shader variants.
            AssertUrpValue(asset, "m_SupportsLightCookies", "0");
            AssertUrpValue(asset, "m_ReflectionProbeAtlas", "0");
        }

        // Exact-value assertion. StringAssert.Contains("m_ShadowDistance: 0") also matches
        // "m_ShadowDistance: 0.5", so substring checks cannot guard these numbers.
        static void AssertUrpValue(string asset, string key, string expected)
        {
            Match match = Regex.Match(asset, $@"^\s*{Regex.Escape(key)}:\s*(\S+)\s*$", RegexOptions.Multiline);

            Assert.That(match.Success, Is.True, $"{key} is missing from the generated URP asset.");
            Assert.That(match.Groups[1].Value, Is.EqualTo(expected), $"{key} has an unexpected value.");
        }

        [Test]
        public void VoxelShaderCompilesRealtimeLightAndShadowPasses()
        {
            string shader = File.ReadAllText(VoxelShaderPath);

            // Without these keywords URP never compiles the shadow-receiving or additional-light
            // variants, so torches emit nothing and terrain receives no shadow.
            Assert.That(shader, Does.Contain("_MAIN_LIGHT_SHADOWS"),
                "The voxel shader must declare the main-light shadow keywords to receive sun/moon shadows.");
            Assert.That(shader, Does.Contain("_ADDITIONAL_LIGHTS"),
                "The voxel shader must declare the additional-light keywords for placed emitters.");
            Assert.That(shader, Does.Contain("_ADDITIONAL_LIGHT_SHADOWS"),
                "Placed emitters must be able to cast shadows onto voxel terrain.");
            Assert.That(shader, Does.Contain("_CLUSTER_LIGHT_LOOP"),
                "URP 17 replaced _FORWARD_PLUS with _CLUSTER_LIGHT_LOOP; the clustered path must stay supported.");

            // A forward pass alone cannot cast: geometry is only written into a shadow map by a
            // pass tagged ShadowCaster.
            Assert.That(shader, Does.Contain("\"LightMode\" = \"ShadowCaster\""),
                "The voxel shader needs a ShadowCaster pass or chunks never cast shadows.");
            Assert.That(shader, Does.Contain("GetAdditionalLight"),
                "The fragment stage must actually loop additional lights, not just declare the keywords.");

            // Each punctual light gets exactly ONE occlusion term: its own shadow map if it owns a
            // shadow slice, the baked per-face emitterReach gate if it does not.
            Assert.That(shader, Does.Contain("GetAdditionalLightShadowParams"),
                "Occlusion is chosen per light from the shadow slice index, so the shader must ask "
                + "URP which lights actually own a shadow map.");
            Assert.That(shader, Does.Contain("GetPerObjectLightIndex"),
                "The shipped renderer is Forward, where LIGHT_LOOP_BEGIN yields a loop counter "
                + "rather than a light index. Without this mapping the shadow-slice lookup reads "
                + "the wrong light on device.");

            // The regression this guards is the reported one: emitterReach is a 1 m per-face term
            // and the shadow map is a ~4 cm one, so multiplying the summed punctual total by the
            // bake let the coarser term zero the finer one and stepped every emitter shadow onto
            // block boundaries.
            Assert.That(shader, Does.Not.Contain("additional *= emitterReach"),
                "The baked gate must not be applied to the summed punctual total again after the "
                + "light loop; that double-gates the one emitter that already has a shadow map.");

            // The handoff to the bake has to crossfade the RAW shadow sample. URP has already
            // mixed the fade into Light.shadowAttenuation, so a fully shadowed texel reads back as
            // `fade`, and combining two fade-lifted envelopes reopens pixels both terms call
            // occluded -- half the punctual light through a wall at the middle of the band.
            Assert.That(shader, Does.Contain("AdditionalLightRealtimeShadow"),
                "Emitter occlusion must sample the unfaded realtime shadow directly rather than "
                + "reuse the fade-mixed Light.shadowAttenuation.");
        }

        [Test]
        public void BootSceneIsFirstEnabledBuildScene()
        {
            Assert.That(EditorBuildSettings.scenes, Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(BootScenePath));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
            Assert.That(
                EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path),
                Is.EqualTo(new[] { BootScenePath }),
                "Player builds must enable only the generated Boot scene; test scenes stay editor-only.");
        }

        [Test]
        public void BootSceneDoesNotPersistCompositionLayerPlaneMeshOverrides()
        {
            string bootScene = File.ReadAllText(BootScenePath);
            string sceneBootstrapper = File.ReadAllText(SceneBootstrapperPath);

            Assert.That(sceneBootstrapper, Does.Contain("DestroyImmediate(rig)"),
                "The generated Boot rig must be replaced from the prefab so stale composition-layer scene overrides are dropped.");
            Assert.That(sceneBootstrapper, Does.Contain("RemoveStaleRootCompositionLayers(scene)"),
                "The generated Boot scene must also delete package/test-created composition layer roots that are outside the generated rig.");
            Assert.That(bootScene, Does.Not.Contain("m_Name: Composition Layer Plane"),
                "Boot.unity must not persist generated composition-layer meshes; old pixel-sized meshes trigger large-triangle warnings on scene import.");
            Assert.That(bootScene, Does.Not.Contain("m_Name: Composition Render Scale Surface"),
                "Boot.unity must not persist root composition layers with invalid layer data because they submit black compositor surfaces on Quest.");
            Assert.That(bootScene, Does.Not.Contain("m_Extent: {x: 430, y: 430, z: 0}"));
        }

        [Test]
        public void RayDiagnosticWorldsAndBuildEntrypointsAreRemoved()
        {
            string sceneBootstrapper = File.ReadAllText(SceneBootstrapperPath);
            string buildSmoke = File.ReadAllText(BuildSmokePath);
            string project = File.ReadAllText("Assets/Blockiverse/Scripts/Core/BlockiverseProject.cs");

            Assert.That(AssetDatabase.FindAssets("RayDiagnostic t:Scene", new[] { "Assets/Blockiverse/Scenes" }), Is.Empty);
            Assert.That(AssetDatabase.FindAssets("BlockiverseXrUiInteractionLab t:Script", new[] { "Assets/Blockiverse/Scripts/VR" }), Is.Empty);
            Assert.That(AssetDatabase.FindAssets("BlockiverseMenuDiagnosticStartup t:Script", new[] { "Assets/Blockiverse/Scripts/VR" }), Is.Empty);
            Assert.That(project, Does.Not.Contain("RayDiagnostic"));
            Assert.That(project, Does.Not.Contain("UseXrUiInteractionLabStartupOverride"));
            Assert.That(sceneBootstrapper, Does.Not.Contain("RayDiagnostic"));
            Assert.That(sceneBootstrapper, Does.Not.Contain("XrUiInteractionLab"));
            Assert.That(sceneBootstrapper, Does.Not.Contain("MenuDiagnosticStartup"));
            Assert.That(buildSmoke, Does.Not.Contain("BuildRayDiagnostic"));
            Assert.That(
                EditorBuildSettings.scenes.Select(scene => scene.path),
                Has.None.Matches<string>(path => path.Contains("RayDiagnostic")),
                "Diagnostic stub scenes must not be part of the generated build scene list.");
        }

        [Test]
        public void LocalDevelopmentBuildMetadataDoesNotFallBackToProjectSettingsVersionCode()
        {
            string buildSmoke = File.ReadAllText(BuildSmokePath);
            string buildScript = File.ReadAllText("scripts/unity/build-development-apk.sh");
            var sampleUtc = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

            Assert.That(BlockiverseBuildSmoke.CreateLocalDevelopmentVersionName(sampleUtc),
                Is.EqualTo("0.1.0-dev.local.20260621120000"));
            Assert.That(BlockiverseBuildSmoke.CreateAndroidVersionCode(
                    new DateTime(2020, 1, 1, 0, 0, 1, DateTimeKind.Utc)),
                Is.EqualTo(1));
            Assert.That(BlockiverseBuildSmoke.CreateAndroidVersionCode(sampleUtc), Is.GreaterThan(1));
            StringAssert.Contains("CreateLocalDevelopmentVersionName(utcNow)", buildSmoke);
            StringAssert.Contains("CreateAndroidVersionCode(utcNow)", buildSmoke);
            StringAssert.Contains("1577836800", buildScript);
            StringAssert.Contains("-blockiverseBuildVersionName \"$UNITY_ANDROID_VERSION_NAME\"", buildScript);
            StringAssert.Contains("-blockiverseBuildVersionCode \"$UNITY_ANDROID_VERSION_CODE\"", buildScript);
        }

        [Test]
        public void BootSceneContainsOneMetaUserAgeCategoryService()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);
                BlockiverseUserAgeCategoryService[] services = scene.GetRootGameObjects()
                    .SelectMany(sceneRoot => sceneRoot.GetComponentsInChildren<BlockiverseUserAgeCategoryService>(true))
                    .ToArray();

                Assert.That(services, Has.Length.EqualTo(1));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void GeneratedMenuLabelsAutoSizeForLocalizedText()
        {
            string menuBootstrapper = File.ReadAllText(MenuBootstrapperPath);

            StringAssert.Contains("text.enableAutoSizing = true", menuBootstrapper);
            StringAssert.Contains("text.fontSizeMin =", menuBootstrapper);
            StringAssert.Contains("TextOverflowModes.Ellipsis", menuBootstrapper);
            StringAssert.Contains("TextWrappingModes.Normal", menuBootstrapper);
            Assert.That(menuBootstrapper, Does.Not.Contain("enableWordWrapping"));
            Assert.That(menuBootstrapper, Does.Not.Contain("TextOverflowModes.Truncate"));
        }

        [Test]
        public void GeneratedUiDoesNotDependOnRemovedBuiltinSkinSprites()
        {
            string[] bootstrapperFiles = Directory
                .GetFiles("Assets/Blockiverse/Scripts/Editor", "BlockiverseProjectBootstrapper*.cs")
                .OrderBy(path => path)
                .ToArray();
            string bootstrapperSource = string.Join("\n", bootstrapperFiles.Select(File.ReadAllText));

            Assert.That(bootstrapperSource, Does.Not.Contain("Resources.GetBuiltinResource<Sprite>(\"UI/Skin/"),
                "Unity 6000.3 no longer ships the legacy UI/Skin PSD sprites used by old UGUI examples.");
            StringAssert.Contains("checkbox_check", bootstrapperSource);
            StringAssert.Contains("slider_knob", bootstrapperSource);
        }

        [Test]
        public void GeneratedControllerMappingMentionsBothTeleportSticks()
        {
            string gameMenuBootstrapper = File.ReadAllText("Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.GameMenus.cs");

            StringAssert.Contains("Either stick hold up: teleport aim, release to land", gameMenuBootstrapper);
            StringAssert.Contains("Support stick: move", gameMenuBootstrapper);
            StringAssert.Contains("Dominant stick click: toggle block editing", gameMenuBootstrapper);
            StringAssert.Contains("Dominant primary button: jump / swim up", gameMenuBootstrapper);
            StringAssert.Contains("Dominant secondary button: crouch / swim down", gameMenuBootstrapper);
            Assert.That(gameMenuBootstrapper, Does.Not.Contain("Right stick hold up: teleport aim"));
            Assert.That(gameMenuBootstrapper, Does.Not.Contain("Right A: jump"));
        }

        [Test]
        public void GeneratedXrRigRegistersPlayerRigAnchor()
        {
            string xrRigBootstrapper = File.ReadAllText(XrRigBootstrapperPath);

            StringAssert.Contains("rig.AddComponent<BlockiversePlayerRigAnchor>()", xrRigBootstrapper);
            StringAssert.Contains("EnsureComponent<BlockiversePlayerRigAnchor>(rig)", xrRigBootstrapper);
        }

        [Test]
        public void GeneratedXrRigWiresVerboseTraceController()
        {
            string xrRigBootstrapper = File.ReadAllText(XrRigBootstrapperPath);

            StringAssert.Contains("EnsureComponent<BlockiverseVerboseTraceController>(rig)", xrRigBootstrapper);
            StringAssert.Contains("verboseTrace.Configure(inputRig, null, controller, audioCuePlayer, vfxCuePlayer, musicController, interactionHaptics)", xrRigBootstrapper);
        }

        [Test]
        public void GeneratedWorldWiresPerformanceStatsOverlay()
        {
            string sceneBootstrapper = File.ReadAllText(SceneBootstrapperPath);

            StringAssert.Contains("EnsureComponent<PerformanceStatsOverlay>(worldObject)", sceneBootstrapper);
            StringAssert.Contains("performanceOverlay.Configure(renderer)", sceneBootstrapper);
        }

        [Test]
        public void GeneratedBootWorldInitializesDefaultWorldOnAwake()
        {
            string sceneBootstrapper = File.ReadAllText(SceneBootstrapperPath);
            string bootScene = File.ReadAllText(BootScenePath);

            StringAssert.Contains("manager.InitializeDefaultWorldOnAwake = true", sceneBootstrapper);
            StringAssert.Contains("initializeDefaultWorldOnAwake: 1", bootScene);
        }

        [Test]
        public void BootSceneBootstrapperRemovesDuplicateGeneratedRoots()
        {
            string sceneBootstrapper = File.ReadAllText(SceneBootstrapperPath);

            StringAssert.Contains(
                "EnsureSingleRootGameObject(scene, BlockiverseProject.XrRigRootName)",
                sceneBootstrapper);
            StringAssert.Contains(
                "EnsureSingleRootGameObject(scene, BlockiverseProject.CreativeWorldRootName)",
                sceneBootstrapper);
            StringAssert.Contains("EnsureSingleRootGameObject(scene, eventSystemName)", sceneBootstrapper);
            StringAssert.Contains("EnsureSingleRootGameObject(scene, NetworkManagerRootName)", sceneBootstrapper);
        }

        [Test]
        public void GeneratedXrRigVignetteExcludesStartupGravityAndJump()
        {
            string xrRigBootstrapper = File.ReadAllText(XrRigBootstrapperPath);

            Assert.That(
                xrRigBootstrapper,
                Does.Not.Contain("AddVignetteProvider(controller, rig.GetComponent<GravityProvider>())"),
                "Gravity can report active while the rig settles, so it must not close the startup/menu vignette.");
            Assert.That(
                xrRigBootstrapper,
                Does.Not.Contain("AddVignetteProvider(controller, rig.GetComponent<JumpProvider>())"),
                "Jump arcs should not be wired as startup/menu vignette triggers.");
        }

        [Test]
        public void XrRigTurnProvidersAreSuppressedWhileToolHandHoversUi()
        {
            string inputRig = File.ReadAllText(BlockiverseInputRigPath);

            StringAssert.Contains("leftInteractionRay", inputRig);
            StringAssert.Contains("rightInteractionRay", inputRig);
            StringAssert.Contains("UpdateTurnProviderEnabledState()", inputRig);
            StringAssert.Contains("IsActiveTurnRayOverUi()", inputRig);
            StringAssert.Contains("GetToolHand()", inputRig);
            StringAssert.Contains("interactionRay.IsOverUIGameObject()", inputRig);
            StringAssert.Contains("!smoothTurn && !suppressTurnForUi", inputRig);
            StringAssert.Contains("smoothTurn && !suppressTurnForUi", inputRig);
        }

        [Test]
        public void ProjectOwnedScriptingDefinesArePresentOnBothTargets()
        {
            // The project-owned defines must be present on both build targets. Whichever target is
            // active when an editor hook runs used to decide where a define landed, so the two lists
            // drifted and every invocation dirtied ProjectSettings. Package-managed defines
            // (SENTIS_ANALYTICS_ENABLED, APP_UI_EDITOR_ONLY) are deliberately not asserted: their
            // owners move them per active target, so the full lists are allowed to differ.
            Assert.That(File.Exists(PlayerProjectSettingsPath), Is.True);
            string settings = File.ReadAllText(PlayerProjectSettingsPath);

            // Scoped to the defines block: ProjectSettings has several other "Android:" keys.
            Match block = Regex.Match(
                settings,
                @"^  scriptingDefineSymbols:\r?\n(?<body>(?:    [^\r\n]*\r?\n)+)",
                RegexOptions.Multiline);
            Assert.That(block.Success, Is.True, "scriptingDefineSymbols block not found.");

            string body = block.Groups["body"].Value;
            Match android = Regex.Match(body, @"^    Android: (.*)$", RegexOptions.Multiline);
            Match standalone = Regex.Match(body, @"^    Standalone: (.*)$", RegexOptions.Multiline);

            Assert.That(android.Success, Is.True, "Android scripting defines not found.");
            Assert.That(standalone.Success, Is.True, "Standalone scripting defines not found.");

            string[] required =
            {
                "OVR_DISABLE_HAND_PINCH_BUTTON_MAPPING",
                "USE_INPUT_SYSTEM_POSE_CONTROL",
                "USE_STICK_CONTROL_THUMBSTICKS",
            };

            // Split-and-compare rather than substring matching, so a symbol mutating into a
            // superstring (FOO_LEGACY replacing FOO) cannot satisfy the assertion.
            var androidSymbols = android.Groups[1].Value.Trim().Split(';');
            var standaloneSymbols = standalone.Groups[1].Value.Trim().Split(';');

            foreach (string symbol in required)
            {
                Assert.That(androidSymbols, Does.Contain(symbol));
                Assert.That(standaloneSymbols, Does.Contain(symbol));
            }
        }

        [Test]
        public void MetaProjectConfigStaysControllersOnly()
        {
            // The roadmap targets Quest controllers only for the initial release; hand tracking is a
            // V2 feature. The bootstrapper asserts ControllersOnly, so a committed 1 here means an
            // SDK upgrade drifted the asset and the next bootstrap run would revert it.
            Assert.That(File.Exists(OculusProjectConfigPath), Is.True);
            string config = File.ReadAllText(OculusProjectConfigPath);

            StringAssert.Contains("handTrackingSupport: 0", config);
        }

        [Test]
        public void NoUguiCreativeToolsWiringSurvivesInGeneratedAssets()
        {
            // This used to assert each handler was wired EXACTLY ONCE, guarding a real bug: bare
            // AddPersistentListener over an existing prefab stacked another copy on every
            // bootstrap run, and the prefab had reached 83 copies, so one click ran each region
            // operation 83 times.
            //
            // UI Toolkit made that failure structurally impossible — there are no serialized
            // UnityEvent listeners to stack, and the equivalent hazard (registering a callback
            // twice without unregistering) is covered by CallbackRegistrationBalance, asserted for
            // this very screen at CatalogCreativeScreensEditModeTests.cs:101.
            //
            // So the assertion inverts rather than retires. The bootstrapper only ever *ensures*
            // objects, which means deleting EnsureCreativeToolsMenuPanel did NOT remove the panel
            // from a prefab that already had it — only the explicit RetiredUguiMenuPanelNames
            // removal pass does that. Zero here is what proves the removal pass actually ran, and
            // it stays as a tripwire against a uGUI creative-tools panel returning by accident.
            string[] methods =
            {
                "SetCornerA", "SetCornerB", "FillRegion", "ReplaceRegion", "DeleteRegion",
                "CopyRegion", "PasteRegion", "UndoEdit", "RedoEdit", "CycleWeather",
            };

            foreach (string generatedAsset in new[] { BlockiverseXrRigPrefabPath, "Assets/Blockiverse/Scenes/Boot.unity" })
            {
                Assert.That(File.Exists(generatedAsset), Is.True, $"{generatedAsset} is missing.");
                string contents = File.ReadAllText(generatedAsset);

                foreach (string method in methods)
                {
                    Assert.That(
                        Regex.Matches(contents, $@"m_MethodName: {method}$", RegexOptions.Multiline).Count,
                        Is.Zero,
                        $"{method} is still wired in {generatedAsset}; the uGUI creative tools " +
                        "panel was not removed from the generated asset.");
                }
            }
        }

        [Test]
        public void LocomotionFeedbackUsesTeleportAndSnapTurnHapticsWithoutPerCallDeviceListAllocation()
        {
            string inputRig = File.ReadAllText(BlockiverseInputRigPath);
            string haptics = File.ReadAllText(BlockiverseControllerHapticsPath);

            StringAssert.Contains("teleportationProvider.locomotionEnded += teleportEndedHandler", inputRig);
            StringAssert.Contains("snapTurnProvider.locomotionEnded += snapTurnEndedHandler", inputRig);
            StringAssert.Contains("BlockiverseHapticPattern.TeleportLand", inputRig);
            StringAssert.Contains("BlockiverseHapticPattern.SnapTurn", inputRig);
            StringAssert.Contains("static readonly System.Collections.Generic.List<InputDevice> DeviceScratch", haptics);
            Assert.That(haptics, Does.Not.Contain("new System.Collections.Generic.List<InputDevice>()"));
        }

        [Test]
        public void PerformanceOverlayImguiPathIsDevelopmentOnly()
        {
            string overlaySource = File.ReadAllText(PerformanceStatsOverlayPath);

            StringAssert.Contains("#if !DEVELOPMENT_BUILD && !UNITY_EDITOR", overlaySource);
            StringAssert.Contains("enabled = false;", overlaySource);
            StringAssert.Contains("#if DEVELOPMENT_BUILD || UNITY_EDITOR", overlaySource);
            StringAssert.Contains("void OnGUI()", overlaySource);
            Assert.That(overlaySource, Does.Not.Contain("Debug.isDebugBuild"));
        }

        [Test]
        public void AndroidOpenXrSettingsAreConfiguredForQuest()
        {
            OpenXRSettings androidSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);

            Assert.That(androidSettings, Is.Not.Null);
            Assert.That(androidSettings.renderMode, Is.EqualTo(OpenXRSettings.RenderMode.SinglePassInstanced));
            Assert.That(androidSettings.GetFeatures(), Has.Some.Matches<UnityEngine.XR.OpenXR.Features.OpenXRFeature>(
                feature => feature.enabled && feature.GetType().Name == "MetaQuestFeature"));

            UnityEngine.XR.OpenXR.Features.OpenXRFeature metaQuestFeature = androidSettings.GetFeatures()
                .FirstOrDefault(feature => feature.GetType().Name == "MetaQuestFeature");
            Assert.That(metaQuestFeature, Is.Not.Null);

            var serializedFeature = new SerializedObject(metaQuestFeature);
            SerializedProperty keyboardProperty = serializedFeature.FindProperty("enableSystemKeyboard");
            Assert.That(keyboardProperty, Is.Not.Null, "Meta Quest OpenXR feature must expose the system keyboard setting.");
            Assert.That(keyboardProperty.boolValue, Is.True,
                "Quest builds must enable the OpenXR system keyboard overlay for TouchScreenKeyboard.");
        }

        [Test]
        public void MetaProjectConfigRequiresSystemKeyboard()
        {
            string projectConfig = File.ReadAllText(OculusProjectConfigPath);
            string bootstrapper = File.ReadAllText(ProjectBootstrapperPath);

            StringAssert.Contains("requiresSystemKeyboard: 1", projectConfig);
            Assert.That(bootstrapper, Does.Not.Contain("projectConfig.focusAware"),
                "Meta XR now requires focus awareness by default; writing the obsolete field keeps compiler warnings alive.");
        }

        [Test]
        public void AppBrandingUsesNamedBuildTargetIconsApi()
        {
            string bootstrapper = File.ReadAllText(ProjectBootstrapperPath);

            StringAssert.Contains("PlayerSettings.SetIcons(NamedBuildTarget.Android", bootstrapper);
            StringAssert.Contains("PlayerSettings.SetIcons(NamedBuildTarget.Unknown", bootstrapper);
            Assert.That(bootstrapper, Does.Not.Contain("SetIconsForTargetGroup"));
        }

        [Test]
        public void AndroidXrManagerStartsOpenXrAutomatically()
        {
            // Select the Android manager explicitly: activating the Meta XR Simulator
            // adds a Standalone manager sub-asset (with automatic loading off, which is
            // correct for the desktop editor), so FirstOrDefault over all managers is
            // order-dependent and can grab the wrong build target.
            Object managerSettings = AssetDatabase
                .LoadAllAssetsAtPath(XrGeneralSettingsPath)
                .FirstOrDefault(asset => asset.GetType().Name == "XRManagerSettings"
                    && asset.name.StartsWith("Android"));

            Assert.That(managerSettings, Is.Not.Null);

            var serializedManager = new SerializedObject(managerSettings);
            Assert.That(serializedManager.FindProperty("m_AutomaticLoading")?.boolValue, Is.True);
            Assert.That(serializedManager.FindProperty("m_AutomaticRunning")?.boolValue, Is.False,
                "Automatic running is disabled in the Editor to prevent 'StopSubsystems without an initialized manager' warnings on domain reload. XR is started manually at runtime.");

            SerializedProperty loaders = serializedManager.FindProperty("m_Loaders");
            Assert.That(loaders, Is.Not.Null);
            Assert.That(loaders.arraySize, Is.GreaterThan(0));
            Assert.That(
                Enumerable.Range(0, loaders.arraySize)
                    .Select(index => loaders.GetArrayElementAtIndex(index).objectReferenceValue)
                    .Any(loader => loader != null && loader.GetType().Name == "OpenXRLoader"),
                Is.True);
        }

        [Test]
        public void AndroidManifestUsesSingleGameActivityEntry()
        {
            Assert.That(File.Exists(AndroidManifestPath), Is.True);

            var manifest = new XmlDocument();
            manifest.Load(AndroidManifestPath);

            var namespaceManager = new XmlNamespaceManager(manifest.NameTable);
            namespaceManager.AddNamespace("android", "http://schemas.android.com/apk/res/android");

            XmlNode internetPermission = manifest.SelectSingleNode(
                "/manifest/uses-permission[@android:name='android.permission.INTERNET']",
                namespaceManager);
            Assert.That(internetPermission, Is.Not.Null);

            XmlNodeList activityNodes = manifest.SelectNodes("/manifest/application/activity", namespaceManager);
            Assert.That(activityNodes, Is.Not.Null);
            Assert.That(activityNodes, Has.Count.EqualTo(1));

            string activityName = activityNodes[0].Attributes["android:name"]?.Value;
            // Classic Activity: GameActivity breaks Unity's soft-keyboard handshake on Quest.
            Assert.That(activityName, Is.EqualTo("com.unity3d.player.UnityPlayerActivity"));
            Assert.That(
                activityNodes[0].Attributes["android:theme"]?.Value,
                Is.EqualTo("@style/UnityThemeSelector"),
                "AppCompat themes only link under the GameActivity entry point; a classic Activity "
                    + "build fails Android resource linking if the theme is left behind.");
            Assert.That(
                activityNodes[0].Attributes["android:label"]?.Value,
                Is.EqualTo("@string/app_name"),
                "Quest shell quit and unknown-source surfaces should resolve the activity title to Blockiverse VR.");

            XmlNode supportedDevicesNode = manifest.SelectSingleNode(
                "/manifest/application/meta-data[@android:name='com.oculus.supportedDevices']",
                namespaceManager);
            Assert.That(supportedDevicesNode, Is.Not.Null);
            Assert.That(
                supportedDevicesNode.Attributes["android:value"]?.Value,
                Is.EqualTo("quest3|quest3s"));

            XmlNode keyboardFeatureNode = manifest.SelectSingleNode(
                "/manifest/uses-feature[@android:name='oculus.software.overlay_keyboard']",
                namespaceManager);
            Assert.That(keyboardFeatureNode, Is.Not.Null,
                "Quest system keyboard support must be declared in the Android manifest.");
            Assert.That(keyboardFeatureNode.Attributes["android:required"]?.Value, Is.EqualTo("false"));
        }

        [Test]
        public void AndroidAppIdentityAndBrandingAssetsAreConfigured()
        {
            Assert.That(PlayerSettings.productName, Is.EqualTo(BlockiverseProject.ProductName));
            Assert.That(File.Exists(BlockiverseProject.AndroidAppStringsPath), Is.True);
            Assert.That(File.ReadAllText(BlockiverseProject.AndroidAppStringsPath), Does.Contain(BlockiverseProject.ProductName));
            Assert.That(File.Exists(BlockiverseProject.AndroidBrandingLibraryPath + "/AndroidManifest.xml"), Is.True);
            Assert.That(
                File.ReadAllText(BlockiverseProject.AndroidBrandingLibraryPath + "/build.gradle"),
                Does.Contain("namespace 'dev.ericslutz.blockiversevr.branding'"));
            Assert.That(Directory.Exists(LegacyAndroidResourcePath), Is.False);

            string[] requiredBrandingAssets =
            {
                BlockiverseProject.AppIconPath,
                BlockiverseProject.LaunchArtworkPath
            };

            foreach (string assetPath in requiredBrandingAssets)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                Assert.That(texture, Is.Not.Null, $"Missing branding texture: {assetPath}");
                Assert.That(File.Exists($"{assetPath}.meta"), Is.True, $"Missing branding texture meta: {assetPath}.meta");
            }
        }

        [Test]
        public void AndroidRuntimeTaskTitleUsesProductName()
        {
            Assert.That(File.Exists(AndroidActivityTitleRuntimePath), Is.True,
                "The Quest quit panel can read the live Android activity/task title even when the launcher manifest label is correct.");

            Type titleType = Type.GetType("Blockiverse.Core.BlockiverseAndroidActivityTitle, Blockiverse.Core");
            Assert.That(titleType, Is.Not.Null);

            FieldInfo titleField = titleType.GetField("Title", BindingFlags.Public | BindingFlags.Static);
            Assert.That(titleField, Is.Not.Null);
            Assert.That(titleField.GetRawConstantValue(), Is.EqualTo(BlockiverseProject.ProductName));

            string source = File.ReadAllText(AndroidActivityTitleRuntimePath);
            StringAssert.Contains("RuntimeInitializeOnLoadMethod", source);
            StringAssert.Contains("setTitle", source);
            StringAssert.Contains("ActivityManager$TaskDescription", source);
            StringAssert.Contains("setTaskDescription", source);
        }

        [Test]
        public void MetaRuntimeSettingsDoNotRequestUnusedFaceTracking()
        {
            var runtimeSettings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(OculusRuntimeSettingsPath);
            Assert.That(runtimeSettings, Is.Not.Null);

            var serializedSettings = new SerializedObject(runtimeSettings);
            Assert.That(GetBool(serializedSettings, "requestsVisualFaceTracking"), Is.False);
            Assert.That(GetBool(serializedSettings, "requestsAudioFaceTracking"), Is.False);
            Assert.That(GetBool(serializedSettings, "enableFaceTrackingVisemesOutput"), Is.False);
        }

        [Test]
        public void AndroidBuildTreatsUnusedMetaAvatarSamplePresetsAsOptional()
        {
            string buildSmokeSource = File.ReadAllText(BuildSmokePath);

            StringAssert.Contains("PrepareOptionalMetaAvatarSamplePresets()", buildSmokeSource);
            StringAssert.Contains(".blockiverse-no-sample-presets", buildSmokeSource);
            StringAssert.Contains("loadFallbackPreset: 1", buildSmokeSource);

            foreach (string assetPath in EnumerateBlockiverseSerializedAssets())
            {
                Assert.That(
                    File.ReadAllText(assetPath),
                    Does.Not.Contain("loadFallbackPreset: 1"),
                    $"{assetPath} enables Meta sample preset avatars; either disable it or intentionally add packaged Quest preset assets.");
            }
        }

        static bool GetBool(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null);
            return property.boolValue;
        }

        static string[] EnumerateBlockiverseSerializedAssets()
        {
            return Directory
                .GetFiles("Assets/Blockiverse", "*.*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    string extension = Path.GetExtension(path);
                    return extension == ".asset" || extension == ".prefab" || extension == ".unity";
                })
                .ToArray();
        }
    }
}
