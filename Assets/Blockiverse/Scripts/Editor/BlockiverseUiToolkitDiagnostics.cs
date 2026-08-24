using System.Collections.Generic;
using System.Text;
using Blockiverse.Core;
using Blockiverse.UI;
using UnityEditor;
using UnityEngine;

namespace Blockiverse.Editor
{
    // Play-mode diagnostics for the UI Toolkit screens.
    //
    // Exists because the obvious way to answer "does this screen hold its content" — counting
    // rows in the UXML — is wrong: a row of buttons laid out horizontally reads as N rows to a
    // parser and is one row on screen. Only a real layout pass knows, and that only happens in
    // Play mode, on a visible panel, on a later frame. Hence the frame-stepped sweep: a
    // single-shot loop would read every screen one frame too early and report zeroes.
    public static class BlockiverseUiToolkitDiagnostics
    {
        // Frames to wait after showing a screen before trusting its layout.
        const int SettleFrames = 2;

        static readonly List<(UiToolkitScreenController controller, bool visible, bool input)> Restore = new();
        static List<(string screenId, UiToolkitScreenController controller)> queue;
        static StringBuilder report;
        static int index;
        static int settle;

        [MenuItem("Blockiverse/UI Toolkit/Report Screen Content Fit")]
        public static void ReportScreenFit()
        {
            if (!Application.isPlaying)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Bootstrap,
                    "Screen overflow can only be measured in Play mode — UI Toolkit does not lay " +
                    "out a document until its panel exists.");
                return;
            }

            var host = Object.FindFirstObjectByType<UiToolkitMenuHost>(FindObjectsInactive.Include);

            if (host == null)
            {
                BlockiverseLog.Error(BlockiverseLogCategory.Bootstrap, "No UiToolkitMenuHost in the scene.");
                return;
            }

            queue = new List<(string, UiToolkitScreenController)>(host.Screens);

            // Worst case, not observed case. An action-list screen shows between four and seven
            // entries depending on save availability and CanQuit(), and a panel sized to whatever
            // happened to be on screen during the sweep clips the day it shows one more. Push the
            // richest list each screen can legally present before measuring anything.
            FillActionScreensToWorstCase(host);

            report = new StringBuilder(
                "UI Toolkit screen content fit, measured at worst-case content\n" +
                "  positive px = content spills (scrolls, or CLIPS where the screen has no ScrollView)\n" +
                "  negative px = dead space the panel could give back\n");
            Restore.Clear();

            foreach (var (_, controller) in host.Screens)
                Restore.Add((controller, controller.IsVisible, controller.AcceptsInput));

            index = 0;
            settle = 0;
            EditorApplication.update -= Step;
            EditorApplication.update += Step;
        }

        // Slack under this is not worth a resize: it is inside the noise of a single text line's
        // leading, and chasing it would churn every panel size on every font tweak.
        const float FitTolerancePixels = 24f;

        static string Verdict(float delta, bool scrolls)
        {
            if (delta > FitTolerancePixels)
                return scrolls ? $"GROW by {delta:0} (scrollbar showing)" : $"GROW by {delta:0} (CLIPPING)";

            return delta < -FitTolerancePixels ? $"shrink by {-delta:0} (dead space)" : "fits";
        }

        // Drive every action-list screen to the largest list it can legally show.
        //
        // Sizing to observed content is how a panel ends up clipping later: the title menu shows
        // five entries on device and six in the editor (CanQuit is editor-only), and the pause menu
        // varies with world permissions. Measuring whichever happened to be loaded would bake in
        // the smaller one.
        static void FillActionScreensToWorstCase(UiToolkitMenuHost host)
        {
            foreach (var (screenId, controller) in host.Screens)
            {
                if (controller is not IUiToolkitActionMenuScreen actionScreen)
                    continue;

                IReadOnlyList<MenuAction> worst = screenId switch
                {
                    MenuActions.TitleScreen => MenuActions.Title(hasLatestSave: true, hasAnySave: true, canQuit: true),
                    MenuActions.PauseScreen => MenuActions.PauseMenu(canToggleMode: true, canOpenCreativeTools: true, canQuit: true),
                    MenuActions.DeathScreen => MenuActions.Death(hasBedrollSpawn: true),
                    MenuActions.SettingsScreen => MenuActions.Settings,
                    MenuActions.WorldDetailsScreen => MenuActions.WorldDetails,
                    MenuActions.ConfirmModal => MenuActions.Confirm(),
                    MenuActions.ErrorModal => MenuActions.Error(),
                    _ => null,
                };

                // A representative title, NOT the type name. Real titles are short ("Paused",
                // "Settings", the product name); "ComfortSettingsScreenController" is 31 characters
                // and would wrap the header on the narrower panels, inflating every reading by a
                // line the shipped screen never shows.
                if (worst != null)
                    actionScreen.SetActionMenu(BlockiverseProject.ProductName, worst);
            }
        }

        static void Step()
        {
            if (!Application.isPlaying || queue == null)
            {
                Finish();
                return;
            }

            if (index >= queue.Count)
            {
                Finish();
                return;
            }

            (string screenId, UiToolkitScreenController controller) = queue[index];

            if (settle == 0)
            {
                // Shown without input: measuring must not let a hidden screen take the ray.
                controller.SetVisible(true, false);
                settle++;
                return;
            }

            if (settle < SettleFrames)
            {
                settle++;
                return;
            }

            bool measured = controller.TryMeasureContentFit(out float delta, out bool scrolls);
            report.AppendLine(measured
                ? $"  {screenId,-20} {controller.GetType().Name,-34} {delta,8:0} px  " +
                  $"{(scrolls ? "scroll" : "fixed ")}  {Verdict(delta, scrolls)}"
                : $"  {screenId,-20} {controller.GetType().Name,-34}   (no layout)");

            controller.SetVisible(false, false);
            index++;
            settle = 0;
        }

        static void Finish()
        {
            EditorApplication.update -= Step;

            foreach ((UiToolkitScreenController controller, bool visible, bool input) in Restore)
            {
                if (controller != null)
                    controller.SetVisible(visible, input);
            }

            Restore.Clear();
            queue = null;

            if (report != null)
            {
                BlockiverseLog.Info(BlockiverseLogCategory.Bootstrap, report.ToString());
                report = null;
            }
        }
    }
}
