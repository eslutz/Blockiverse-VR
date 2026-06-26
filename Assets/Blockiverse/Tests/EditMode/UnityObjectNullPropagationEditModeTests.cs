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
                "Assets/Blockiverse/Scripts/Gameplay/CreativeWorldManager.cs",
                new[]
                {
                    @"\bRenderer\s*\?\.",
                    @"Shader\.Find\([^\r\n]+\)\s*\?\?",
                }),
            (
                "Assets/Blockiverse/Scripts/Gameplay/MultiplayerChunkAuthoritySync.cs",
                new[]
                {
                    @"\bworldManager\s*\?\.",
                    @"\bworldManager\.Renderer\s*\?\.",
                    @"ResolveNetworkManagerOrNull\(\)\s*\?\?",
                }),
            (
                "Assets/Blockiverse/Scripts/Gameplay/MultiplayerSurvivalSync.cs",
                new[]
                {
                    @"\bworldManager\s*\?\.",
                    @"\bavatarRig\s*\?\.",
                    @"ResolveNetworkManagerOrNull\(\)\s*\?\?",
                    @"\bchunkAuthoritySync\s*\?\?",
                }),
            (
                "Assets/Blockiverse/Scripts/UI/SurvivalHudController.cs",
                new[]
                {
                    @"\b(panel|worldManager|survivalSync|inputBridge|healthLabel|healthSlider|healthStateLabel|statusLabel|miningProgressSlider)\s*\?\.",
                    @"\b(healthLabel|healthSlider|healthStateLabel|statusLabel|miningProgressSlider)\s*\?\?=",
                }),
        };

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
