using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // The lighting controller writes the sky gradient, cloud colour and a continuously advancing
    // cloud scroll into the sky material every LateUpdate. Pointed at the generated .mat asset,
    // that means every Play-mode session leaves it dirty carrying whatever time of day and scroll
    // offset the session ended on -- and a stray `git add -A` bakes a random cloud offset into the
    // repository while losing the authored defaults.
    public sealed class SkyMaterialInstancingEditModeTests
    {
        [Test]
        public void TheGeneratedSkyAssetKeepsItsAuthoredDefaults()
        {
            Material asset = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.SkyMaterialPath);

            Assert.That(asset, Is.Not.Null, "The bootstrapper should generate the sky material.");
            Assert.That(asset.shader.name, Is.EqualTo("Blockiverse/Sky"));

            // Authored midday, cloudless, unscrolled. If any of these has drifted, runtime state
            // has leaked into the committed asset.
            Assert.That(asset.GetFloat("_CloudCoverage"), Is.EqualTo(0.0f).Within(1e-4f),
                "A non-zero coverage here is runtime state written into the asset.");
            Assert.That((Vector2)asset.GetVector("_CloudScroll"), Is.EqualTo(Vector2.zero),
                "Cloud scroll advances every frame at runtime and must never reach the asset.");
            // Per channel with a tolerance: Color.Equals is an exact field comparison, and a
            // colour that survives a round trip through the .mat's serialized floats comes back
            // bit-different while being identical to every decimal place anyone cares about.
            AssertColor(asset.GetColor("_ZenithColor"), SkyGradientSolver.DayZenith, "_ZenithColor");
            AssertColor(asset.GetColor("_HorizonColor"), SkyGradientSolver.DayHorizon, "_HorizonColor");
            AssertColor(asset.GetColor("_GroundColor"), SkyGradientSolver.DayGround, "_GroundColor");
        }

        static void AssertColor(Color actual, Color expected, string property)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-3f), $"{property}.r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-3f), $"{property}.g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-3f), $"{property}.b");
        }

        [Test]
        public void DrivingTheControllerOutsidePlayModeLeavesTheAssetUntouched()
        {
            // The regression this exists to catch, exercised directly rather than inferred: an
            // EditMode caller doing exactly what LightingCycleEditModeTests does used to write the
            // gradient and an advancing cloud scroll straight into the generated asset.
            Material asset = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.SkyMaterialPath);
            Assert.That(asset, Is.Not.Null);

            Vector4 scrollBefore = asset.GetVector("_CloudScroll");
            float coverageBefore = asset.GetFloat("_CloudCoverage");
            Color zenithBefore = asset.GetColor("_ZenithColor");

            var host = new GameObject("Sky Churn Probe");

            try
            {
                WorldTimeClock clock = host.AddComponent<WorldTimeClock>();
                clock.Configure(dayLengthSeconds: 10.0f, startNormalizedTime: 0.75f, timeScale: 1.0f);
                Light light = host.AddComponent<Light>();
                BlockiverseLightingCycleController controller =
                    host.AddComponent<BlockiverseLightingCycleController>();

                // Midnight, so any leak would be unmistakable against the authored midday values.
                controller.Configure(clock, light);
                controller.ApplyCurrentLighting();
            }
            finally
            {
                Object.DestroyImmediate(host);
            }

            Assert.That(asset.GetVector("_CloudScroll"), Is.EqualTo(scrollBefore),
                "Cloud scroll advanced in the committed asset.");
            Assert.That(asset.GetFloat("_CloudCoverage"), Is.EqualTo(coverageBefore).Within(1e-4f));
            AssertColor(asset.GetColor("_ZenithColor"), zenithBefore, "_ZenithColor");
        }

        [Test]
        public void TheControllerNeverWritesTheSharedAssetAtRuntime()
        {
            // Source-level guard, because the failure is invisible until someone commits the
            // churn: the controller must mint its own instance rather than driving the asset.
            string source = System.IO.File.ReadAllText(
                "Assets/Blockiverse/Scripts/Gameplay/BlockiverseLightingCycleController.cs");

            Assert.That(source, Does.Contain("EnsureRuntimeSkyInstance"));
            Assert.That(source, Does.Contain("RenderSettings.skybox = skyMaterial"),
                "The runtime instance has to be installed as the active skybox or it renders nothing.");
            Assert.That(source, Does.Contain("ownsSkyInstance"),
                "Destroying a material this component did not create would delete the generated asset.");
        }
    }
}
