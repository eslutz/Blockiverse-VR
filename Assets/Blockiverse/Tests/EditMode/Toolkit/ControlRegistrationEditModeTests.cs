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
    // Every interactive control a screen RESOLVES must also be REGISTERED for callbacks.
    //
    // ── The defect this exists for ───────────────────────────────────────────
    //
    // ComfortSettingsScreenController shipped `bv-comfort-place-modifier-toggle` fully wired
    // except for the one line that matters: declared as a field, resolved in OnAttach, given a
    // label, and its value read on push-down — but never passed to RegisterToggle. It rendered.
    // It was clickable. It moved when you clicked it. And it wrote nothing, because the only thing
    // that writes it is ApplyOtherControlsWithFeedback, which fires when some OTHER control
    // changes, and the Close button does not push settings.
    //
    // So a player who opened Comfort, flipped only that toggle, and closed lost the setting with no
    // error and no way to tell. It reached main through a reviewed PR.
    //
    // ── Why no existing test caught it ───────────────────────────────────────
    //
    // The screen's own suite counts registered callbacks against a hard-coded total, and that total
    // was written to match the code rather than the markup — so the count agreed with itself while
    // disagreeing with the document by one control. And a behavioural test cannot help here:
    // ChangeEvents do not dispatch on a panel-less tree in EditMode, which is why that suite drives
    // the controller's handler seams directly instead. Both routes are blind to a control that is
    // simply never connected.
    //
    // Source analysis is what is left. It is coarse, and it is the only thing that can see the gap
    // between "this control exists" and "this control does anything".
    public sealed class ControlRegistrationEditModeTests
    {
        // `field = Require<Toggle>(root, "name", ref allFound);`
        static readonly Regex ResolvedToggle = new(
            @"(\w+)\s*=\s*Require<Toggle>\s*\(\s*root\s*,\s*""([^""]+)""",
            RegexOptions.Compiled);

        // TWO idioms, because this codebase uses both and a guard that knows only one reports
        // working screens as broken. Falsifying this test surfaced exactly that: it named five
        // AudioSettingsScreenController toggles that are correctly wired through the second form.
        //
        //   RegisterToggle(field, handler)          -- the comfort screen's helper
        //   field.RegisterValueChangedCallback(...) -- the audio screen's direct form
        static readonly Regex RegisteredToggle = new(
            @"(?<!Un)RegisterToggle\s*\(\s*(\w+)|(\w+)\s*\.\s*RegisterValueChangedCallback",
            RegexOptions.Compiled);

        static readonly Regex UnregisteredToggle = new(
            @"UnregisterToggle\s*\(\s*(\w+)|(\w+)\s*\.\s*UnregisterValueChangedCallback",
            RegexOptions.Compiled);

        static HashSet<string> FieldsIn(Regex pattern, string source) =>
            pattern.Matches(source)
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToHashSet(StringComparer.Ordinal);

        static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
        static readonly Regex LineComment = new(@"//[^\n]*", RegexOptions.Compiled);

        static IEnumerable<(Type type, string source)> ScreenSources()
        {
            foreach (Type type in TypeCache.GetTypesDerivedFrom<UiToolkitScreenController>())
            {
                if (type.IsAbstract)
                    continue;

                string path = SourcePathFor(type);

                if (path == null || !File.Exists(path))
                    continue;

                // Comments stripped: this suite's own explanatory prose names RegisterToggle and
                // Require<Toggle> repeatedly, and so do the controllers'. Matching those would make
                // the check report on documentation rather than on code.
                string text = LineComment.Replace(BlockComment.Replace(File.ReadAllText(path), " "), " ");
                yield return (type, text);
            }
        }

        [Test]
        public void EveryResolvedToggleIsRegisteredForChangeCallbacks()
        {
            var offenders = new List<string>();
            int screensChecked = 0;
            int togglesChecked = 0;

            foreach ((Type type, string source) in ScreenSources())
            {
                MatchCollection resolved = ResolvedToggle.Matches(source);

                if (resolved.Count == 0)
                    continue;

                screensChecked++;

                HashSet<string> registered = FieldsIn(RegisteredToggle, source);

                // A screen that registers NOTHING is reading its toggles on demand rather than
                // pushing on change, which is a legitimate design — LanMultiplayerScreenController
                // reads its encryption toggle at the moment the player presses Host or Join, so
                // there is nothing to apply until then and a change callback would have nothing to
                // do. The defect is INCONSISTENCY: a screen that registers most of its toggles and
                // silently misses one, which is what shipped on the comfort screen.
                if (registered.Count == 0)
                    continue;

                foreach (Match match in resolved)
                {
                    togglesChecked++;
                    string field = match.Groups[1].Value;
                    string elementName = match.Groups[2].Value;

                    if (!registered.Contains(field))
                    {
                        offenders.Add(
                            $"{type.Name}: '{elementName}' is resolved into '{field}', but this " +
                            "screen registers its other toggles and never registers this one — it " +
                            "renders, it is clickable, and changing it does nothing. Add a " +
                            "RegisterToggle or RegisterValueChangedCallback, whichever this screen " +
                            "already uses.");
                    }
                }
            }

            // Positive control. With no screens or no toggles found, the loop above proves nothing
            // and would report green on a broken regex.
            Assert.That(screensChecked, Is.GreaterThan(0),
                "No screen resolved a Toggle — the source scan found nothing to check.");
            Assert.That(togglesChecked, Is.GreaterThan(10),
                $"Only {togglesChecked} toggles found across {screensChecked} screens; the comfort " +
                "screen alone has more than that, so the scan is not seeing what it should.");

            Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
        }

        // The other half of the same balance. A registered callback with no matching unregister
        // leaks across a re-attach and double-fires afterwards — the failure the comfort screen's
        // own ReattachUnregistersEverythingItRegistered test guards for that one screen, checked
        // here for all of them.
        [Test]
        public void EveryRegisteredToggleIsAlsoUnregistered()
        {
            var offenders = new List<string>();

            foreach ((Type type, string source) in ScreenSources())
            {
                HashSet<string> registered = FieldsIn(RegisteredToggle, source);

                if (registered.Count == 0)
                    continue;

                HashSet<string> unregistered = FieldsIn(UnregisteredToggle, source);

                foreach (string field in registered.Except(unregistered))
                {
                    offenders.Add(
                        $"{type.Name}: '{field}' is registered but never unregistered — its " +
                        "callback survives a re-attach and fires twice afterwards.");
                }
            }

            Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
        }

        static string SourcePathFor(Type type)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{type.Name} t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (Path.GetFileNameWithoutExtension(path) == type.Name)
                    return path;
            }

            return null;
        }
    }
}
