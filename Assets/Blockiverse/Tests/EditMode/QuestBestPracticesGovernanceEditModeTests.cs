using System.IO;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class QuestBestPracticesGovernanceEditModeTests
    {
        [Test]
        public void QuestRuntimePackagesAreDirectDependencies()
        {
            string manifest = File.ReadAllText("Packages/manifest.json");
            string lockFile = File.ReadAllText("Packages/packages-lock.json");
            string bootstrapper = File.ReadAllText("Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.cs");

            StringAssert.Contains("MobileTextureSubtarget.ASTC", bootstrapper);
            StringAssert.Contains("\"com.unity.xr.compositionlayers\": \"2.5.0\"", manifest);
            StringAssert.Contains("\"com.unity.addressables\": \"3.1.0\"", manifest);
            string compositionLayerBlock = PackageBlock(lockFile, "com.unity.xr.compositionlayers");
            string addressablesBlock = PackageBlock(lockFile, "com.unity.addressables");
            StringAssert.Contains("\"depth\": 0", compositionLayerBlock);
            StringAssert.Contains("\"version\": \"3.1.0\"", addressablesBlock);
            StringAssert.Contains("\"depth\": 0", addressablesBlock);
        }

        [Test]
        public void QuestPerformanceHotPathsKeepProfilerMarkers()
        {
            string renderer = File.ReadAllText("Assets/Blockiverse/Scripts/Gameplay/VoxelWorldRenderer.cs");
            string meshBuilder = File.ReadAllText("Assets/Blockiverse/Scripts/Gameplay/ChunkMeshBuilder.cs");
            string persistence = File.ReadAllText("Assets/Blockiverse/Scripts/Persistence/WorldSaveService.cs");
            string worldSession = File.ReadAllText("Assets/Blockiverse/Scripts/UI/BlockiverseWorldSessionController.cs");
            string uiRouter = File.ReadAllText("Assets/Blockiverse/Scripts/UI/UiScreenRouter.cs");
            string chunkAuthority = File.ReadAllText("Assets/Blockiverse/Scripts/Networking/MultiplayerChunkAuthoritySync.cs");

            StringAssert.Contains("Blockiverse.VoxelWorldRenderer.RebuildDirty", renderer);
            StringAssert.Contains("Blockiverse.VoxelWorldRenderer.RebuildChunk", renderer);
            StringAssert.Contains("Blockiverse.ChunkMeshBuilder.Build", meshBuilder);
            StringAssert.Contains("Blockiverse.WorldSaveService.Save", persistence);
            StringAssert.Contains("Blockiverse.WorldSaveService.Load", persistence);
            StringAssert.Contains("Blockiverse.WorldSession.ApplyLoadedWorld", worldSession);
            StringAssert.Contains("Blockiverse.UiScreenRouter.PushScreen", uiRouter);
            StringAssert.Contains("Blockiverse.ChunkAuthority.HandleMutationRequest", chunkAuthority);
            StringAssert.Contains("Blockiverse.ChunkAuthority.ApplyBufferedChunkDeltas", chunkAuthority);
            StringAssert.Contains("ProfilerMarker", renderer);
            StringAssert.Contains("ProfilerMarker", meshBuilder);
            StringAssert.Contains("ProfilerMarker", persistence);
            StringAssert.Contains("ProfilerMarker", worldSession);
            StringAssert.Contains("ProfilerMarker", uiRouter);
            StringAssert.Contains("ProfilerMarker", chunkAuthority);
        }

        static string PackageBlock(string lockFile, string packageName)
        {
            int start = lockFile.IndexOf($"\"{packageName}\"");
            Assert.That(start, Is.GreaterThanOrEqualTo(0), packageName);
            int next = lockFile.IndexOf("\n    \"", start + packageName.Length + 2);
            if (next < 0)
                next = lockFile.Length;
            return lockFile.Substring(start, next - start);
        }

        [Test]
        public void QuestRenderingAndAssetPoliciesAreDocumented()
        {
            string menuRules = File.ReadAllText("docs/rulesets/voxel_survival_menus.md");
            string testingReadme = File.ReadAllText("docs/testing/README.md");
            string adr = File.ReadAllText("docs/adr/0006-quest-openxr-rendering-and-asset-policy.md");
            string standards = File.ReadAllText("docs/architecture/quest-runtime-engineering-standards.md");

            // These two previously pinned the shared-Quad composition-menu model -- which ADR 0006's
            // own 2026-08-13 amendment reversed after on-headset failures, and which ADR 0010
            // supersedes entirely. The suite was defending doctrine the architecture had already
            // abandoned, and the device-validation checklist was sending testers to look for a
            // cursor that no longer exists. They now pin the current policy, and forbid the old one
            // coming back by name.
            StringAssert.Contains("direct world-space surfaces", menuRules);
            StringAssert.Contains("only** for the startup splash", menuRules);
            Assert.That(menuRules, Does.Not.Contain("composition menu cursor"));
            StringAssert.Contains("no shared composition Quad", testingReadme);
            StringAssert.Contains("com.unity.addressables", adr);
            StringAssert.Contains("No recurring managed allocations", standards);
            StringAssert.Contains("Addressables", standards);
            StringAssert.Contains("Meta Avatars", standards);
            Assert.That(menuRules, Does.Not.Contain("Projection Eye Rig"));
        }
    }
}
