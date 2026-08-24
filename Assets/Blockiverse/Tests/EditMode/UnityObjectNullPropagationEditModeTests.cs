using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class UnityObjectNullPropagationEditModeTests
    {
        static readonly (string Path, string[] Patterns)[] GuardedSources =
        {
            (
                "Assets/Blockiverse/Scripts/WorldRuntime/CreativeWorldManager.cs",
                new[]
                {
                    // The `presentation` FIELD must never be dereferenced directly: only the
                    // `Presentation` property applies the Unity lifetime check, and an
                    // interface-typed reference to a destroyed MonoBehaviour compares non-null.
                    @"\bpresentation\s*\?\.",
                    @"\bpresentation\s*\?\?",
                    @"Shader\.Find\([^\r\n]+\)\s*\?\?",
                }),
            (
                "Assets/Blockiverse/Scripts/Gameplay/BlockiverseWorldPresentation.cs",
                new[]
                {
                    @"\bworldRenderer\s*\?\.",
                    @"\b(interactionController|hotbar|placementPreview|voidSafetyFloor|glowwickLightManager)\s*\?\.",
                    @"Shader\.Find\([^\r\n]+\)\s*\?\?",
                }),
            (
                "Assets/Blockiverse/Scripts/Networking/MultiplayerChunkAuthoritySync.cs",
                new[]
                {
                    @"\bworldManager\s*\?\.",
                    @"\bworldManager\.Renderer\s*\?\.",
                    @"ResolveNetworkManagerOrNull\(\)\s*\?\?",
                }),
            (
                "Assets/Blockiverse/Scripts/Networking/MultiplayerSurvivalSync.cs",
                new[]
                {
                    @"\bworldManager\s*\?\.",
                    @"\bavatarRig\s*\?\.",
                    @"ResolveNetworkManagerOrNull\(\)\s*\?\?",
                    @"\bchunkAuthoritySync\s*\?\?",
                }),
            // Was Scripts/UI/SurvivalHudController.cs, re-pointed at the UI Toolkit HUD that
            // replaced it. Note the field list is deliberately short: this rule is about
            // UnityEngine.Object lifetime, so it covers the MonoBehaviour references and the
            // interface-typed one (an interface reference to a destroyed MonoBehaviour compares
            // non-null, same trap as the CreativeWorldManager entry above) — NOT the Label /
            // VisualElement fields, which are plain C# objects where `?.` is correct.
            //
            // Keep this list in sync by hand. Nothing about a UIDocument screen makes it immune;
            // the HUD resolves a cue player and a vitals runtime exactly like its predecessor.
            (
                "Assets/Blockiverse/Scripts/UI/ToolkitScreens/Screens/GameplayHudController.cs",
                new[]
                {
                    @"\b(audioCuePlayer|vitalsRuntime|interactionHaptics)\s*\?\.",
                    @"\b(audioCuePlayer|vitalsRuntime|interactionHaptics)\s*\?\?=",
                }),
        };

        // File.ReadAllText below has no existence check, so a path that goes stale throws
        // FileNotFoundException from the middle of a loop rather than failing an assertion —
        // which reads as a broken test rather than a broken guard list. Fail on it deliberately.
        [Test]
        public void EveryGuardedSourceStillExists()
        {
            foreach ((string path, string[] _) in GuardedSources)
                Assert.That(File.Exists(path), Is.True, $"Guarded source no longer exists: {path}");
        }

        [Test]
        public void RuntimeUnityObjectReferencesUseExplicitUnityNullChecks()
        {
            foreach ((string path, string[] patterns) in GuardedSources)
            {
                string source = File.ReadAllText(path);

                foreach (string pattern in patterns)
                {
                    Assert.That(
                        Regex.IsMatch(source, pattern),
                        Is.False,
                        $"{path} must not use null propagation/coalescing on UnityEngine.Object-derived references. Pattern: {pattern}");
                }
            }
        }
    }
}
