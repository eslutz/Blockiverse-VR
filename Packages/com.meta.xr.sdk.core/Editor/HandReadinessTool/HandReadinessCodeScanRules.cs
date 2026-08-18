/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Meta.HandReadinessTool.Editor.UI;

namespace Meta.HandReadinessTool.Editor
{
    /// <summary>
    /// Heuristic regex scan over the user's `Assets/` C# files. Runs only on the
    /// non-AI path (when `_useAI` is false) so users who opt out of AI still get
    /// some migration suggestions for the most unambiguous controller-only patterns.
    ///
    /// Rule philosophy: prefer fewer rules with very high precision over many rules
    /// that could fire on perfectly-fine code. Each rule fires only on patterns that
    /// are functionally controller-only and have no meaningful hand-tracking
    /// equivalent without a UX redesign.
    /// </summary>
    internal static class HandReadinessCodeScanRules
    {
        internal class Rule
        {
            public string Id;
            public string Title;
            public Regex Pattern;
            // Why the matched pattern is broken on a hands-only target. Rendered into
            // the popup's "Problem" section.
            public string Problem;
            // What the user should replace it with. Rendered into the popup's
            // "Recommendation" section.
            public string Recommendation;
            // Concrete migration steps. Rendered into the popup's "Steps" section.
            // May be null/empty for rules where steps are too project-specific to enumerate.
            public List<string> Steps;
        }

        /// <summary>
        /// Default rule set baked into the SDK. Used directly when the remote
        /// fetch in <see cref="HandReadinessRulesProvider"/> hasn't returned a
        /// usable payload yet, or as the fallback when that fetch fails. Kept
        /// public-within-assembly so the provider can return it from GetRules().
        /// </summary>
        internal static readonly Rule[] BakedInRules =
        {
            new Rule
            {
                Id = "ovr-controller-buttons",
                Title = "Controller button input",
                // Catches trigger/grip/thumbstick clicks plus the B (Button.Two) and Y
                // (Button.Four) face buttons. Button.One (A) and Button.Three (X) are
                // intentionally excluded because OVRInput.InjectPinchButtonMapping
                // auto-maps the right/left index pinch to RawButton.A/X (see
                // OVRInput.cs:1514) — code reading those buttons keeps working on
                // hand tracking. Button.Two/Four have no such auto-mapping, so usage
                // genuinely breaks on a hands-only device.
                Pattern = new Regex(
                    @"OVRInput\.(?:Get|GetDown|GetUp)\s*\(\s*OVRInput\.Button\." +
                    @"(?:(?:Primary|Secondary)(?:IndexTrigger|HandTrigger|Thumbstick)|Two|Four)",
                    RegexOptions.Compiled),
                Problem =
                    "OVRInput controller buttons (triggers, hand triggers, thumbstick clicks, " +
                    "or the B/Y face buttons) have no equivalent on " + HandReadinessConstants.DeviceName +
                    ", which is a hands-only device.",
                Recommendation =
                    "Replace trigger/grip presses with ISDK HandGrabInteractable for grabbing and " +
                    "PokeInteractable for press-style interactions. For thumbstick clicks or B/Y face " +
                    "buttons, redesign around hand gestures or a palm-up radial menu. Note: Button.One " +
                    "(A) and Button.Three (X) are auto-mapped to the right/left index pinch by " +
                    "OVRInput, so existing code reading those buttons continues to work without changes.",
                Steps = new List<string>
                {
                    "Identify each matched call's intent (grab, press, click, gesture).",
                    "Replace trigger/grip presses with ISDK HandGrabInteractable (grab) or PokeInteractable (press).",
                    "Redesign thumbstick clicks and B/Y face buttons as hand gestures or a palm-up radial menu.",
                    "Leave Button.One (A) and Button.Three (X) reads alone — OVRInput auto-maps index pinch into them.",
                },
            },
            new Rule
            {
                Id = "ovr-controller-pose",
                Title = "Controller pose lookup",
                Pattern = new Regex(
                    @"OVRInput\.GetLocalController(?:Position|Rotation|Velocity|" +
                    @"AngularVelocity|Acceleration|AngularAcceleration)",
                    RegexOptions.Compiled),
                Problem =
                    "OVRInput.GetLocalController* APIs query controller pose, which is unavailable on " +
                    HandReadinessConstants.DeviceName + ".",
                Recommendation =
                    "Replace with OVRHand bone transforms (e.g. OVRHand.GetBoneTransform(OVRSkeleton.BoneId.Hand_WristRoot)) " +
                    "or, with ISDK, HandRef / IHand.GetJointPose for the specific joint you need.",
                Steps = new List<string>
                {
                    "Identify which pose component the call queries (position / rotation / velocity / acceleration).",
                    "Pick the right hand replacement: OVRHand bone transforms for joints, HandRef / IHand.GetJointPose for ISDK consumers.",
                    "Handle the tracking-lost case: hand pose data drops when the hand leaves view, controllers always return the last value.",
                },
            },
            new Rule
            {
                Id = "ovr-thumbstick-axis",
                Title = "Thumbstick locomotion or input",
                // Matches both the raw form (RawAxis2D.LThumbstick/RThumbstick) and
                // the friendly form (Axis2D.PrimaryThumbstick/SecondaryThumbstick).
                // Both are unambiguously controller thumbstick and have no hand equivalent.
                Pattern = new Regex(
                    @"OVRInput\.(?:Get|GetDown|GetUp)\s*\(\s*OVRInput\." +
                    @"(?:RawAxis2D\.(?:LThumbstick|RThumbstick)|" +
                    @"Axis2D\.(?:Primary|Secondary)Thumbstick)",
                    RegexOptions.Compiled),
                Problem =
                    "Thumbstick axis input (smooth locomotion, snap-turn, menu navigation) has no hand " +
                    "equivalent on " + HandReadinessConstants.DeviceName + " and requires a UX redesign.",
                Recommendation =
                    "Common replacements: ISDK TeleportInteractable for locomotion, palm-up radial menu " +
                    "for navigation, and physical body turning instead of snap-turn.",
                Steps = new List<string>
                {
                    "Classify the gameplay role of the thumbstick read (locomotion / turning / menu navigation / aim).",
                    "Pick the matching ISDK replacement: TeleportInteractable, palm-up radial menu, or physical body turn.",
                    "Wire the new interactor's events and remove the OVRInput axis call once parity is verified.",
                },
            },
            new Rule
            {
                Id = "ovr-legacy-grab",
                Title = "Legacy OVRGrabber/OVRGrabbable",
                Pattern = new Regex(
                    @"\bOVR(?:Grabber|Grabbable)\b",
                    RegexOptions.Compiled),
                Problem =
                    "OVRGrabber and OVRGrabbable are the legacy controller-driven grab system and are " +
                    "deprecated for hand-tracking flows.",
                Recommendation =
                    "Migrate to the ISDK Grabbable + HandGrabInteractable pair, which gives you pose-aware " +
                    "finger-by-finger grab on " + HandReadinessConstants.DeviceName + ".",
                Steps = new List<string>
                {
                    "Replace OVRGrabbable on grabbable objects with ISDK Grabbable + HandGrabInteractable.",
                    "Replace OVRGrabber on hand controllers with ISDK HandGrabInteractor.",
                    "(Optional) Add HandGrabPose authored per object for finger-by-finger grip fidelity.",
                },
            },
        };

        /// <summary>
        /// Walk the project's `Assets/` tree, apply each rule once per file, and emit one
        /// IssueData per (rule × file) match. SDK and third-party folders are skipped so
        /// we do not flag Meta-shipped or external code.
        /// </summary>
        internal static List<IssueData> Scan(string assetsPath)
        {
            var issues = new List<IssueData>();
            if (string.IsNullOrEmpty(assetsPath) || !Directory.Exists(assetsPath))
            {
                return issues;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HRT] Code scan failed to enumerate {assetsPath}: {ex.Message}");
                return issues;
            }

            foreach (var file in files)
            {
                if (IsExcludedPath(file)) continue;

                string content;
                try
                {
                    content = File.ReadAllText(file);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var rule in HandReadinessRulesProvider.GetRules())
                {
                    var matches = rule.Pattern.Matches(content);
                    if (matches.Count == 0) continue;

                    issues.Add(new IssueData
                    {
                        Title = $"{rule.Title} in {Path.GetFileName(file)}",
                        // Split the rule's narrative across the same four IssueData
                        // fields the AI path populates so IssueDetailsPopup renders
                        // identical "Problem / Current implementation / Recommendation
                        // / Steps" sections for both flows.
                        Description = rule.Problem,
                        CurrentImplementation = BuildCurrentImplementation(file, content, matches),
                        HandTrackingAdaptation = rule.Recommendation,
                        ImplementationSteps = rule.Steps != null
                            ? new List<string>(rule.Steps)
                            : new List<string>(),
                        Priority = IssuePriority.High,
                        Category = IssueCategory.Manual,
                        TaskUid = $"hrt-code-scan:{rule.Id}:{Path.GetFileName(file)}",
                    });
                }
            }

            return issues;
        }

        private static bool IsExcludedPath(string fullPath)
        {
            // Normalize separators so the same denylist works on Windows and macOS.
            var path = fullPath.Replace('\\', '/');

            // Skip Meta-shipped SDK code (we don't want to flag samples shipped inside Oculus).
            if (path.Contains("/Oculus/", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.Contains("/Meta/", StringComparison.OrdinalIgnoreCase)) return true;

            // Skip third-party plugins and Unity package cache mirrors.
            if (path.Contains("/Plugins/", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.Contains("/PackageCache/", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.Contains("/ThirdParty/", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        /// <summary>
        /// Format the matched lines for the popup's "Current implementation" section.
        /// Caps at 5 examples to keep the popup compact; the count of overflow matches
        /// is appended on a final line so the user knows how many were elided.
        /// </summary>
        private static string BuildCurrentImplementation(string file, string content, MatchCollection matches)
        {
            const int maxExamples = 5;

            var sb = new StringBuilder();
            sb.AppendLine($"Found {matches.Count} occurrence(s) in {Path.GetFileName(file)}:");

            int shown = 0;
            foreach (Match match in matches)
            {
                if (shown >= maxExamples)
                {
                    sb.AppendLine($"  …and {matches.Count - shown} more");
                    break;
                }

                int lineNumber = LineNumberOf(content, match.Index);
                string lineText = LineAt(content, match.Index).Trim();
                if (lineText.Length > 120)
                {
                    lineText = lineText.Substring(0, 117) + "...";
                }
                sb.AppendLine($"  Line {lineNumber}: {lineText}");
                shown++;
            }

            return sb.ToString().TrimEnd();
        }

        private static int LineNumberOf(string content, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < content.Length; i++)
            {
                if (content[i] == '\n') line++;
            }
            return line;
        }

        private static string LineAt(string content, int index)
        {
            int start = index;
            while (start > 0 && content[start - 1] != '\n') start--;
            int end = index;
            while (end < content.Length && content[end] != '\n' && content[end] != '\r') end++;
            return content.Substring(start, end - start);
        }
    }
}
