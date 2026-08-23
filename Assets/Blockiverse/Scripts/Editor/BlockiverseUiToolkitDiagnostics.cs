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

        [MenuItem("Blockiverse/UI Toolkit/Report Screen Content Overflow")]
        public static void ReportScreenOverflow()
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
            report = new StringBuilder("UI Toolkit screen content overflow (positive px = it scrolls)\n");
            Restore.Clear();

            foreach (var (_, controller) in host.Screens)
                Restore.Add((controller, controller.IsVisible, controller.AcceptsInput));

            index = 0;
            settle = 0;
            EditorApplication.update -= Step;
            EditorApplication.update += Step;
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

            bool measured = controller.TryMeasureContentOverflow(out float overflow);
            report.AppendLine(measured
                ? $"  {screenId,-20} {controller.GetType().Name,-34} {overflow,8:0} px"
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
