using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blockiverse.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode.Toolkit
{
    // Two whole-family contracts that no per-screen test can express, both written after a defect
    // that shipped past a green suite.
    //
    // ── 1. Unity message hiding ──────────────────────────────────────────────
    //
    // UiToolkitScreenController owns its document lifecycle through PRIVATE Unity messages:
    // `void Awake()`, `void OnEnable() => Attach()`, `void OnDisable() => Detach()`. Unity
    // dispatches lifecycle messages by NAME to the most-derived declaration only — it is not
    // virtual dispatch, and C# raises no warning because the base members are private and
    // therefore not even visible to hide.
    //
    // So a subclass that declares its own OnEnable silently replaces the base's, Attach() never
    // runs, OnAttach never runs, every cached element stays null, and the screen renders nothing
    // for the rest of the session. GameplayDebugController did exactly this to start a
    // ProfilerRecorder. Nothing failed: the EditMode tests call AttachForTest directly, so they
    // never travel through OnEnable at all.
    //
    // ── 2. Inline style versus USS class ─────────────────────────────────────
    //
    // SetVisible writes an INLINE style.display onto bv-screen-root. In UI Toolkit an inline style
    // outranks every stylesheet rule and USS has no !important, so a `display: none` class applied
    // to that same element is silently discarded once the router shows the screen. Two new screens
    // hid themselves that way and would have shipped permanently visible — a blank diagnostic
    // plate, and a dot at the exact centre of vision.
    public sealed class ScreenLifecycleContractEditModeTests
    {
        // Declared privately on the base and dispatched by name. A subclass declaring any of these
        // takes over the message completely.
        static readonly string[] ReservedUnityMessages = { "Awake", "OnEnable", "OnDisable" };

        static IEnumerable<Type> ScreenControllerTypes() =>
            TypeCache.GetTypesDerivedFrom<UiToolkitScreenController>()
                .Where(type => !type.IsAbstract);

        [Test]
        public void NoScreenDeclaresALifecycleMessageTheBaseClassOwns()
        {
            Type[] screens = ScreenControllerTypes().ToArray();

            Assert.That(screens, Is.Not.Empty, "No screen controllers found — positive control failed.");

            var offenders = new List<string>();

            foreach (Type screen in screens)
            {
                foreach (string message in ReservedUnityMessages)
                {
                    // DeclaredOnly: an inherited match is the base's own and is what we want.
                    MethodInfo declared = screen.GetMethod(
                        message,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);

                    if (declared != null)
                        offenders.Add($"{screen.Name} declares {message}()");
                }
            }

            Assert.That(offenders, Is.Empty,
                "These screens hide a Unity message UiToolkitScreenController needs for its own " +
                "document lifecycle. Unity calls only the most-derived declaration, so the base's " +
                "Attach/Detach never runs and the screen renders nothing — with no compiler " +
                "warning and no failing assertion anywhere else. Use the OnAwake/OnShown/OnHidden " +
                "seams, or OnDestroy, which the base does not declare:\n" +
                string.Join("\n", offenders));
        }

        // Positive control for the test above: if the base ever stops owning these messages, the
        // guard is meaningless and should be deleted rather than left passing vacuously.
        [Test]
        public void TheBaseClassStillOwnsTheReservedMessages()
        {
            foreach (string message in ReservedUnityMessages)
            {
                MethodInfo declared = typeof(UiToolkitScreenController).GetMethod(
                    message,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);

                Assert.That(declared, Is.Not.Null,
                    $"UiToolkitScreenController no longer declares {message}() — the hiding guard " +
                    "above now protects nothing and should be re-derived from whatever replaced it.");
            }
        }

        // The element SetVisible writes its inline display onto. Any class that collapses a screen
        // must target something else.
        [Test]
        public void NoScreenCollapsesTheElementSetVisibleWritesInlineDisplayOnto()
        {
            var offenders = new List<string>();

            foreach (Type screen in ScreenControllerTypes())
            {
                var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                    screen, typeof(UiToolkitScreenAttribute));

                if (attribute == null)
                    continue;

                var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(attribute.DocumentAssetPath);

                if (tree == null)
                    continue;

                VisualElement root = tree.Instantiate();
                VisualElement screenRoot = root.Q<VisualElement>("bv-screen-root");

                if (screenRoot == null)
                    continue;

                // Every display-collapsing class this project defines, resolved against the sheets
                // that actually apply to this document.
                foreach (string collapsing in CollapsingClassesFor(attribute.DocumentAssetPath))
                {
                    if (screenRoot.ClassListContains(collapsing))
                    {
                        offenders.Add(
                            $"{screen.Name}: bv-screen-root carries '{collapsing}', which sets " +
                            "display:none");
                    }
                }
            }

            Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
        }

        // Static analysis of the C# is what actually catches this, because the class is applied at
        // runtime rather than authored into the UXML. A controller that names bv-screen-root (or
        // ScreenRootElementName) and also applies a *--hidden class is the shape of the defect.
        [Test]
        public void NoControllerAppliesAHiddenClassToTheScreenRootItResolved()
        {
            var offenders = new List<string>();

            foreach (Type screen in ScreenControllerTypes())
            {
                string source = SourcePathFor(screen);

                if (source == null || !System.IO.File.Exists(source))
                    continue;

                string text = System.IO.File.ReadAllText(source);

                // The field the controller caches bv-screen-root into, if it does.
                var rootFieldMatch = System.Text.RegularExpressions.Regex.Match(
                    text,
                    @"(\w+)\s*=\s*Require<VisualElement>\(\s*root\s*,\s*ScreenRootElementName");

                if (!rootFieldMatch.Success)
                    continue;

                string rootField = rootFieldMatch.Groups[1].Value;

                // ...then toggles a display-collapsing class on that same field.
                bool collapsesIt = System.Text.RegularExpressions.Regex.IsMatch(
                    text,
                    $@"{System.Text.RegularExpressions.Regex.Escape(rootField)}\s*\.\s*" +
                    @"(EnableInClassList|AddToClassList)\s*\(\s*HiddenClass");

                if (collapsesIt)
                {
                    offenders.Add(
                        $"{screen.Name} applies HiddenClass to '{rootField}', which is " +
                        "bv-screen-root — the element SetVisible writes an inline display onto. " +
                        "The class will never apply. Collapse a child instead.");
                }
            }

            Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
        }

        static IEnumerable<string> CollapsingClassesFor(string documentAssetPath)
        {
            string documentName = System.IO.Path.GetFileNameWithoutExtension(documentAssetPath);
            string[] sheets =
            {
                "Assets/Blockiverse/UI/Styles/Base.uss",
                $"Assets/Blockiverse/UI/Styles/Screens/{documentName}.uss",
            };

            foreach (string sheet in sheets)
            {
                if (!System.IO.File.Exists(sheet))
                    continue;

                string css = System.IO.File.ReadAllText(sheet);

                foreach (System.Text.RegularExpressions.Match rule in
                    System.Text.RegularExpressions.Regex.Matches(
                        css, @"\.([A-Za-z0-9_-]+)\s*\{([^}]*)\}"))
                {
                    if (rule.Groups[2].Value.Contains("display: none"))
                        yield return rule.Groups[1].Value;
                }
            }
        }

        static string SourcePathFor(Type screen)
        {
            string[] guids = AssetDatabase.FindAssets($"{screen.Name} t:MonoScript");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (System.IO.Path.GetFileNameWithoutExtension(path) == screen.Name)
                    return path;
            }

            return null;
        }
    }
}
