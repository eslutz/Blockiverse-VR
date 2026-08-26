using System;
using System.Collections.Generic;
using System.Linq;
using Blockiverse.Core;
using Blockiverse.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode.Toolkit
{
    // The generated scene must still agree with the attributes it was generated from.
    //
    // ── Why this exists ──────────────────────────────────────────────────────
    //
    // `[UiToolkitScreen(...)]` is NOT read at runtime. BlockiverseProjectBootstrapper.Run() copies
    // its numbers into the scene once — `document.worldSpaceSize = new Vector2(WidthPixels,
    // HeightPixels)` — and from then on the SCENE is what ships. Change an attribute without
    // re-running the bootstrapper and the two disagree silently, with the built game following the
    // scene and every EditMode test following the attribute.
    //
    // That is not hypothetical. HotbarStrip was authored at 1000x130, bootstrapped, then changed to
    // 1000x110 *specifically to fix a 5 mm overlap with the mining bar* — and the bootstrapper was
    // never re-run. The attribute carried the fix; the shipped scene carried the bug.
    // HudPanelOverlapEditModeTests computed its clearance from the attribute, found the intended
    // 5 mm gap, and passed, while the actual build overlapped by 5 mm. It took a live query of the
    // running UI tree to find, because both the source and the suite agreed with each other and
    // both were looking at the wrong number.
    //
    // Every geometry assertion in this suite inherits that blind spot. This test is what makes them
    // mean something: it pins the one place where source and shipped artefact can drift apart.
    //
    // ── Why SIZE only, and not position ──────────────────────────────────────
    //
    // Not an oversight. HudLocalX/Y/Z are applied at RUNTIME — UiToolkitMenuHost sets
    // `panel.localPosition` from the attribute every time it places a screen — so a changed
    // position takes effect on the next play without regenerating anything and cannot go stale.
    // Only WidthPixels/HeightPixels are copied into the scene, and only they can drift. If a future
    // change starts baking position, this test must grow to cover it.
    //
    // ── Fixing a failure ─────────────────────────────────────────────────────
    //
    // Do NOT hand-edit Boot.unity. Re-run Blockiverse -> Bootstrap Unity Quest Project (or
    // BlockiverseProjectBootstrapper.Run()), which regenerates the scene from the attributes.
    public sealed class ScreenSceneBakeParityEditModeTests
    {
        readonly struct Baked
        {
            public Baked(string name, Vector2 size, bool hasCollider, Vector3 colliderSize)
            {
                Name = name;
                Size = size;
                HasCollider = hasCollider;
                ColliderSize = colliderSize;
            }

            public string Name { get; }
            public Vector2 Size { get; }
            public bool HasCollider { get; }
            public Vector3 ColliderSize { get; }
        }

        static Dictionary<Type, Baked> ReadBootScene()
        {
            var baked = new Dictionary<Type, Baked>();

            Scene scene = EditorSceneManager.OpenScene(
                BlockiverseProject.BootScenePath, OpenSceneMode.Single);

            foreach (UiToolkitScreenController controller in scene.GetRootGameObjects()
                         .SelectMany(root => root.GetComponentsInChildren<UiToolkitScreenController>(true)))
            {
                var document = controller.GetComponent<UIDocument>();

                if (document == null)
                    continue;

                var collider = controller.GetComponent<BoxCollider>();

                baked[controller.GetType()] = new Baked(
                    controller.gameObject.name,
                    document.worldSpaceSize,
                    collider != null,
                    collider != null ? collider.size : Vector3.zero);
            }

            return baked;
        }

        static UiToolkitScreenAttribute AttributeOf(Type type) =>
            (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                type, typeof(UiToolkitScreenAttribute));

        [Test]
        public void EveryScreenPanelInTheSceneMatchesItsAttributeSize()
        {
            try
            {
                Dictionary<Type, Baked> baked = ReadBootScene();

                Assert.That(baked, Is.Not.Empty,
                    "No screen panels found in the Boot scene — positive control failed, so the " +
                    "comparison below would pass vacuously.");

                var drift = new List<string>();

                foreach ((Type type, Baked panel) in baked)
                {
                    UiToolkitScreenAttribute attribute = AttributeOf(type);

                    if (attribute == null)
                        continue;

                    var expected = new Vector2(attribute.WidthPixels, attribute.HeightPixels);

                    if (panel.Size != expected)
                    {
                        drift.Add(
                            $"{type.Name}: attribute says {expected.x}x{expected.y}, " +
                            $"scene '{panel.Name}' ships {panel.Size.x}x{panel.Size.y}");
                    }
                }

                Assert.That(drift, Is.Empty,
                    "The generated scene has drifted from the attributes it was generated from. " +
                    "The SCENE is what ships and the ATTRIBUTE is what every other test reads, so " +
                    "these panels are validated at one size and rendered at another.\n" +
                    "Fix by re-running the bootstrapper — never by editing Boot.unity:\n" +
                    string.Join("\n", drift));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        // The collider is generated from the same two numbers, so it drifts the same way — and a
        // stale collider is worse than a stale panel: it is an invisible volume that intercepts
        // interaction rays at a size nothing on screen corresponds to.
        [Test]
        public void InteractiveScreenCollidersMatchTheirAttributeSize()
        {
            try
            {
                Dictionary<Type, Baked> baked = ReadBootScene();
                var drift = new List<string>();

                foreach ((Type type, Baked panel) in baked)
                {
                    UiToolkitScreenAttribute attribute = AttributeOf(type);

                    if (attribute == null)
                        continue;

                    if (attribute.NonInteractive)
                    {
                        if (panel.HasCollider)
                        {
                            drift.Add(
                                $"{type.Name} is NonInteractive but ships a BoxCollider — it would " +
                                "intercept rays meant for the world.");
                        }

                        continue;
                    }

                    if (!panel.HasCollider)
                    {
                        drift.Add($"{type.Name} is interactive but ships no BoxCollider.");
                        continue;
                    }

                    // BlockiverseProjectBootstrapper.UiToolkitPixelsPerUnit
                    const float pixelsPerUnit = 100f;
                    float expectedX = attribute.WidthPixels / pixelsPerUnit;
                    float expectedY = attribute.HeightPixels / pixelsPerUnit;

                    if (!Mathf.Approximately(panel.ColliderSize.x, expectedX) ||
                        !Mathf.Approximately(panel.ColliderSize.y, expectedY))
                    {
                        drift.Add(
                            $"{type.Name}: collider is {panel.ColliderSize.x}x{panel.ColliderSize.y}, " +
                            $"attribute implies {expectedX}x{expectedY}");
                    }
                }

                Assert.That(drift, Is.Empty,
                    "Re-run the bootstrapper:\n" + string.Join("\n", drift));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
