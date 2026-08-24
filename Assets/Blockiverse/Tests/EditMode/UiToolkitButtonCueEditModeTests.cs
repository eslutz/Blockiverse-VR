using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // Eric reported (2026-08-23) that the New World screen's Create, Cancel and cycle arrows were
    // all silent on device. Six screens turned out to wire buttons and play nothing: the click
    // still worked, so nothing errored, nothing logged, and no test went red — the defect was
    // audible only in the headset.
    //
    // That is the failure mode this repo keeps hitting: a change that fails by being IGNORED
    // rather than by throwing. The fix is not "remember to add the cue" but a check that
    // ENUMERATES THE DIRECTORY, so a screen authored next month is covered without anyone
    // remembering this happened.
    public sealed class UiToolkitButtonCueEditModeTests
    {
        const string ScreensDirectory = "Assets/Blockiverse/Scripts/UI/ToolkitScreens/Screens";

        // Every wiring idiom in use: `button.clicked += Handler`, the generic
        // `RegisterCallback<ClickEvent>(Handler)` form, and non-generic callback variables of any
        // name. Deliberately broad — in this directory every RegisterCallback IS a click
        // registration (value changes go through RegisterValueChangedCallback, a different method
        // name), and a first draft of this regex that required "click" in the identifier was
        // blind to 10 of 26 screens, certifying two fully silent ones green. Over-matching fails
        // loud (a flagged screen that already cues is a 30-second look); under-matching fails
        // silent, which is the defect this test exists to end.
        static readonly Regex ButtonWiring = new(
            @"\.clicked\s*\+=|\bRegisterCallback(?:Once)?\s*[(<]", RegexOptions.Compiled);

        // KNOWN LIMIT, on purpose: this is FILE-granular. A file that wires ten buttons and cues
        // one passes, so per-button silence inside an already-cued file is invisible here — the
        // 2026-08-24 review found exactly that in five screens (Crafting/Inventory/Crate/LAN/
        // Comfort) and they were fixed by hand. Per-BUTTON coverage needs semantic analysis a
        // regex cannot honestly provide; this test's job is the cheaper, larger claim that no
        // screen is ENTIRELY silent, which is how whole new screens regress.

        // Any cue at all. This deliberately does NOT assert WHICH cue: confirm/cancel/click is a
        // design call per button (Eric's ruling: distinct cues only for real confirmations and
        // real cancels), and pinning it here would turn every future design tweak into a test
        // edit. Silence is the bug; the choice of sound is not this test's business.
        static readonly Regex CuePlayed = new(
            @"BlockiverseUiFeedback\.Play\(|\bPlayFeedback\(|\bPlayCue\(", RegexOptions.Compiled);

        [Test]
        public void EveryScreenThatWiresAButtonPlaysAFeedbackCue()
        {
            Assert.That(Directory.Exists(ScreensDirectory), Is.True,
                $"Screen directory moved: {ScreensDirectory}. Repoint this test — do not delete it.");

            string[] sources = Directory.GetFiles(ScreensDirectory, "*.cs", SearchOption.AllDirectories);
            Assert.That(sources, Is.Not.Empty, "Found no screen controllers to check — the glob is wrong.");

            List<string> silent = new();
            foreach (string path in sources)
            {
                string text = File.ReadAllText(path);
                if (ButtonWiring.IsMatch(text) && !CuePlayed.IsMatch(text))
                    silent.Add(Path.GetFileName(path));
            }

            Assert.That(silent, Is.Empty,
                "These screens wire a button click but play no audio/haptic cue, so the button is "
                + "silent in the headset while everything still 'works':\n  "
                + string.Join("\n  ", silent)
                + "\n\nAdd BlockiverseUiFeedback.Play(...) on the click callback (not on the public "
                + "Submit*/Cycle* seam — the EditMode tests drive those directly and must not need "
                + "an audio rig).");
        }

        // Guards the guard. A regex that matched nothing would let the test above pass over a
        // directory of entirely silent screens, which is precisely the false green it exists to
        // prevent — the check must be shown capable of seeing both halves of what it tests.
        [Test]
        public void TheDetectorRecognisesEveryWiringIdiomAndACue()
        {
            // One assertion per idiom that exists in the directory. The generic and
            // plain-variable cases are here because the first draft missed BOTH — the detector
            // passed green over LoadWorldScreenController's eleven silent buttons.
            Assert.That(ButtonWiring.IsMatch("closeButton.clicked += OnClosePressed;"), Is.True,
                "Failed to see the `clicked +=` idiom.");
            Assert.That(ButtonWiring.IsMatch("nextButtons[i]?.RegisterCallback(nextClickCallbacks[i]);"), Is.True,
                "Failed to see the click-named callback idiom.");
            Assert.That(ButtonWiring.IsMatch("loadButton?.RegisterCallback<ClickEvent>(OnLoadClicked);"), Is.True,
                "Failed to see the explicit-generic idiom — the blind spot that certified "
                + "LoadWorldScreenController green while all eleven of its buttons were silent.");
            Assert.That(ButtonWiring.IsMatch("entryButtons[i]?.RegisterCallback<ClickEvent, int>(OnEntryClicked, i);"),
                Is.True, "Failed to see the generic-with-args idiom.");
            Assert.That(ButtonWiring.IsMatch("button.RegisterCallback(callback);"), Is.True,
                "Failed to see a callback variable not named *click*.");
            Assert.That(ButtonWiring.IsMatch("button.RegisterCallbackOnce<ClickEvent>(OnClicked);"), Is.True,
                "Failed to see RegisterCallbackOnce — unused today, and the exact kind of new "
                + "idiom that reopens the blind spot silently.");
            Assert.That(CuePlayed.IsMatch("PlayCue(BlockiverseAudioCue.UiSelect);"), Is.True,
                "Failed to see a played cue.");
            Assert.That(CuePlayed.IsMatch(
                "BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);"), Is.True,
                "Failed to see a direct feedback call.");

            Assert.That(ButtonWiring.IsMatch("int total = list.Count;"), Is.False,
                "Matches ordinary code, so every screen would look wired.");
            Assert.That(ButtonWiring.IsMatch("protected override void OnRegisterCallbacks()"), Is.False,
                "Matches the lifecycle method declaration itself.");
            Assert.That(ButtonWiring.IsMatch("button.UnregisterCallback(callback);"), Is.False,
                "Matches teardown, so a screen could never be torn down without looking wired.");
            Assert.That(ButtonWiring.IsMatch("searchField?.RegisterValueChangedCallback(searchChangedCallback);"),
                Is.False, "Matches value-change plumbing, which needs no cue.");
            Assert.That(CuePlayed.IsMatch("closeButton.clicked += OnClosePressed;"), Is.False,
                "Matches a bare click, so a silent screen would look cued.");
            Assert.That(CuePlayed.IsMatch("// the hotbar plays BlockiverseAudioCue.UiSelect for us"), Is.False,
                "A COMMENT mentioning a cue must not count as playing one — the first draft's "
                + "cue regex accepted exactly that.");
        }
    }
}
