using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Blockiverse.UI;
using NUnit.Framework;
using UnityEditor;

namespace Blockiverse.Tests.EditMode.Toolkit
{
    // Every USS class a screen uses must be defined in a sheet that screen actually loads.
    //
    // ── The failure this exists for ──────────────────────────────────────────
    //
    // The bootstrapper resolves a screen's own sheet by DOCUMENT NAME:
    // Styles/Screens/<documentName>.uss, plus Tokens and Base. The per-screen sheet is optional
    // by design, so a document whose sheet is missing loads Tokens + Base and renders anyway —
    // with every one of its own classes silently inert. Nothing throws, nothing logs, and the
    // panel reports itself healthy through every counter.
    //
    // Splitting one document into two is exactly how it happens: the markup moves, the rules stay
    // behind in the original sheet, and the new document's name no longer matches the sheet that
    // holds its styles. That is a live defect on this branch at the time this test was written —
    // GameplayStats.uxml carries the gh-* classes and GameplayStats.uss does not exist, so its
    // health bar has no height and no fill colour.
    //
    // ── Why the existing tests did not catch it ──────────────────────────────
    //
    // HudFamilyEditModeTests asserts fill.style.width — the INLINE value the controller writes.
    // Inline styles resolve with or without a stylesheet, so the assertion passes while the
    // player sees nothing. Height and colour come from USS and are never asserted.
    //
    // ── The rule ─────────────────────────────────────────────────────────────
    //
    // A class used by a document and defined in SOME project sheet, but not in one this document
    // loads, is a violation. A class defined in NO sheet is fine — those are marker classes
    // toggled from C# via EnableInClassList, which carry state rather than style.
    public sealed class ScreenStyleSheetReachabilityEditModeTests
    {
        const string StylesRoot = "Assets/Blockiverse/UI/Styles";
        const string ScreenStylesRoot = StylesRoot + "/Screens";

        static readonly Regex ClassAttribute = new("class=\"([^\"]+)\"", RegexOptions.Compiled);

        // Matches ".name" in a selector position. Deliberately loose: it over-collects rather than
        // under-collects, and over-collecting only ever makes a class count as DEFINED, which is
        // the safe direction for a test that reports undefined-here classes.
        static readonly Regex ClassSelector = new(@"\.([A-Za-z_][A-Za-z0-9_-]*)", RegexOptions.Compiled);

        // Block comments are stripped first. Without that, every "Tokens.uss" written in prose
        // reads as a selector for a class called "uss", and this file's own header comments are
        // dense with sheet names — the rule would report a phantom class on most sheets in the
        // project and bury the real finding under it.
        static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);

        static HashSet<string> ClassesDefinedIn(string sheetPath)
        {
            if (!File.Exists(sheetPath))
                return new HashSet<string>(StringComparer.Ordinal);

            string css = BlockComment.Replace(File.ReadAllText(sheetPath), " ");

            return ClassSelector.Matches(css)
                .Select(m => m.Groups[1].Value)
                // Unity's own control classes come from the runtime theme. A sheet may legitimately
                // target them (unity-base-field__label) without its document ever naming one.
                .Where(name => !name.StartsWith("unity-", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);
        }

        static IEnumerable<UiToolkitScreenAttribute> AllScreens() =>
            TypeCache.GetTypesWithAttribute<UiToolkitScreenAttribute>()
                .Select(t => (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                    t, typeof(UiToolkitScreenAttribute)))
                .Where(a => a != null);

        [Test]
        public void EveryClassAScreenUsesIsDefinedInASheetThatScreenLoads()
        {
            HashSet<string> tokens = ClassesDefinedIn(StylesRoot + "/Tokens.uss");
            HashSet<string> baseSheet = ClassesDefinedIn(StylesRoot + "/Base.uss");

            // Everything any screen sheet defines, so "defined elsewhere" is distinguishable from
            // "a marker class nothing styles".
            var definedSomewhere = new HashSet<string>(StringComparer.Ordinal);

            foreach (string sheet in Directory.GetFiles(ScreenStylesRoot, "*.uss"))
                definedSomewhere.UnionWith(ClassesDefinedIn(sheet));

            definedSomewhere.UnionWith(tokens);
            definedSomewhere.UnionWith(baseSheet);

            UiToolkitScreenAttribute[] screens = AllScreens().ToArray();
            Assert.That(screens, Is.Not.Empty, "No screens found — positive control failed.");

            var violations = new List<string>();

            foreach (UiToolkitScreenAttribute screen in screens)
            {
                if (!File.Exists(screen.DocumentAssetPath))
                    continue;

                string documentName = Path.GetFileNameWithoutExtension(screen.DocumentAssetPath);

                HashSet<string> reachable = ClassesDefinedIn(
                    ScreenStylesRoot + "/" + documentName + ".uss");
                reachable.UnionWith(tokens);
                reachable.UnionWith(baseSheet);

                foreach (Match match in ClassAttribute.Matches(File.ReadAllText(screen.DocumentAssetPath)))
                {
                    foreach (string used in match.Groups[1].Value.Split(
                                 ' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        // Unity's own control classes come from the runtime theme, not from here.
                        if (used.StartsWith("unity-", StringComparison.Ordinal))
                            continue;

                        // Defined nowhere at all: a state marker toggled from C#, not a styling bug.
                        if (!definedSomewhere.Contains(used))
                            continue;

                        if (!reachable.Contains(used))
                        {
                            violations.Add(
                                $"{documentName}.uxml uses '{used}', which is defined in another " +
                                $"screen's sheet but not in {documentName}.uss, Tokens.uss or Base.uss");
                        }
                    }
                }
            }

            Assert.That(violations.Distinct(), Is.Empty,
                "These classes silently do nothing — the panel renders unstyled and reports " +
                "healthy:\n" + string.Join("\n", violations.Distinct()));
        }

        // The blind spot in the test above, closed.
        //
        // That test exempts any class defined in NO sheet, because a C#-toggled state marker
        // legitimately has no rule. But DELETING a screen's whole stylesheet makes every one of its
        // classes undefined-everywhere at once, so all of them take the exemption and the guard
        // passes — which is precisely the failure it was written for. GameplayStats shipped exactly
        // that way: the sheet did not exist, the health bar had no height or fill colour, and
        // nothing complained.
        //
        // So: a document carrying classes that no SHARED sheet defines must have its own sheet on
        // disk. Prefixes are per-screen by convention (gs-, dbg-, hb-), so "uses non-shared classes
        // but has no sheet" is unambiguous.
        [Test]
        public void EveryScreenUsingNonSharedClassesHasItsOwnStyleSheet()
        {
            HashSet<string> shared = ClassesDefinedIn(StylesRoot + "/Tokens.uss");
            shared.UnionWith(ClassesDefinedIn(StylesRoot + "/Base.uss"));

            var missing = new List<string>();
            int checkedScreens = 0;

            foreach (UiToolkitScreenAttribute screen in AllScreens())
            {
                if (!File.Exists(screen.DocumentAssetPath))
                    continue;

                string documentName = Path.GetFileNameWithoutExtension(screen.DocumentAssetPath);
                string sheetPath = ScreenStylesRoot + "/" + documentName + ".uss";

                if (File.Exists(sheetPath))
                    continue;

                checkedScreens++;

                var unexplained = new List<string>();

                foreach (Match match in ClassAttribute.Matches(File.ReadAllText(screen.DocumentAssetPath)))
                {
                    foreach (string used in match.Groups[1].Value.Split(
                                 ' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (used.StartsWith("unity-", StringComparison.Ordinal))
                            continue;

                        if (!shared.Contains(used))
                            unexplained.Add(used);
                    }
                }

                if (unexplained.Count > 0)
                {
                    missing.Add(
                        $"{documentName}.uxml has no {documentName}.uss, yet uses " +
                        $"{string.Join(", ", unexplained.Distinct())} — none of which Base.uss or " +
                        "Tokens.uss define. Those elements render unstyled.");
                }
            }

            Assert.That(missing, Is.Empty, string.Join("\n", missing));

            // Not a positive control for the assertion (a project where every screen has a sheet
            // legitimately checks nothing), but it records which case ran, so a future reader can
            // tell "nothing to check" from "checked and clean".
            TestContext.WriteLine($"Screens without their own sheet: {checkedScreens}");
        }

        // The other half of the same mistake: rules left behind in a sheet whose own document no
        // longer uses them. Harmless to render, but it is the fingerprint of a split that moved
        // markup without moving styles, so it points at the bug above rather than being one.
        [Test]
        public void NoScreenSheetDefinesClassesItsOwnDocumentNeverUses()
        {
            var orphans = new List<string>();

            foreach (UiToolkitScreenAttribute screen in AllScreens())
            {
                if (!File.Exists(screen.DocumentAssetPath))
                    continue;

                string documentName = Path.GetFileNameWithoutExtension(screen.DocumentAssetPath);
                string sheetPath = ScreenStylesRoot + "/" + documentName + ".uss";

                if (!File.Exists(sheetPath))
                    continue;

                string markup = File.ReadAllText(screen.DocumentAssetPath);
                string source = ControllerSourceFor(documentName);

                foreach (string defined in ClassesDefinedIn(sheetPath))
                {
                    // Modifier classes are applied from C# and never appear in markup.
                    if (markup.Contains(defined, StringComparison.Ordinal) ||
                        source.Contains(defined, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    orphans.Add($"{documentName}.uss defines '{defined}', unused by its document");
                }
            }

            Assert.That(orphans, Is.Empty, string.Join("\n", orphans));
        }

        // Best-effort: controllers apply modifier classes by name, so their source counts as usage.
        static string ControllerSourceFor(string documentName)
        {
            string[] matches = Directory.GetFiles(
                "Assets/Blockiverse/Scripts/UI", documentName + "Controller.cs",
                SearchOption.AllDirectories);

            return matches.Length > 0 ? File.ReadAllText(matches[0]) : string.Empty;
        }
    }
}
