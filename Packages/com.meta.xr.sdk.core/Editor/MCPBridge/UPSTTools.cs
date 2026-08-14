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
using System.Linq;
using System.Text;
using Meta.MCPBridge.Attributes;
using Meta.MCPBridge.Services;
using UnityEditor;
using UnityEngine;

namespace MCPServices.Tools
{
    [Tool(
        "Validate and fix the Unity project's configuration for Meta Quest deployment. Detects setup issues such as wrong build settings, missing entitlements, incorrect graphics APIs, Android manifest problems, and other configuration errors that would prevent a successful Quest build or cause runtime issues. Can automatically fix most issues.",
        "WHEN TO USE: Use when the user wants to check if their Unity project is correctly configured for Quest, diagnose build or deployment problems, fix project settings, or ensure their project is ready to build and run on a Quest headset.",
        "WORKFLOW: 1) GetStatus() for a quick health check 2) GetReport() for a detailed list of specific issues 3) PreviewFix(taskUid) to understand what a fix will change before applying it 4) FixTask(taskUid) / FixByLevel(level) / FixAll() to apply fixes.",
        "IMPORTANT: Issues are categorized by severity — Required (must fix or the build will fail), Recommended (should fix for best results), and Optional (nice-to-have improvements). Always preview a fix before applying it to understand the impact."
    )]
    internal class UPSTTools : SingletonService<UPSTTools>
    {
        [Tool(Description = "Get a quick health check of the Unity project's Quest configuration. Shows how many setup issues exist at each severity level (Required, Recommended, Optional) for the current build target.",
            Returns = "Health summary with issue counts by severity and overall project readiness")]
        internal string GetStatus()
        {
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var status = OVRProjectSetupStatus.ComputeStatus(buildTargetGroup);

            var sb = new StringBuilder();
            sb.AppendLine($"=== UPST STATUS ({buildTargetGroup}) ===");
            sb.AppendLine();
            sb.AppendLine(OVRProjectSetupStatus.ComputeStatusMessage(status));
            sb.AppendLine(OVRProjectSetupStatus.ComputeSubtitleMessage(status));
            sb.AppendLine();

            if (status.OutstandingTasks != null && status.TotalOutstandingCount > 0)
            {
                var required = OVRProjectSetupStatus.GetCountAtLevel(
                    status.OutstandingTasks, buildTargetGroup, OVRProjectSetup.TaskLevel.Required);
                var recommended = OVRProjectSetupStatus.GetCountAtLevel(
                    status.OutstandingTasks, buildTargetGroup, OVRProjectSetup.TaskLevel.Recommended);
                var optional = OVRProjectSetupStatus.GetCountAtLevel(
                    status.OutstandingTasks, buildTargetGroup, OVRProjectSetup.TaskLevel.Optional);

                sb.AppendLine("Breakdown:");
                sb.AppendLine($"  Required:    {required}");
                sb.AppendLine($"  Recommended: {recommended}");
                sb.AppendLine($"  Optional:    {optional}");
            }

            return sb.ToString();
        }

        [Tool(Description = "Get a detailed report of all outstanding Quest project configuration issues. Optionally filter by category (group) and/or severity level. Each issue includes a UID needed for previewing or applying fixes.",
            Returns = "Detailed list of issues with UID, category, severity, description, and whether an automatic fix is available")]
        internal string GetReport(string group = null, string level = null)
        {
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var tasks = OVRProjectSetup.GetTasks(buildTargetGroup);

            // Filter to outstanding tasks only
            var outstanding = tasks
                .Where(task => OVRProjectSetupStatus.IsOutstanding(task, buildTargetGroup));

            // Filter by level if specified
            if (!string.IsNullOrEmpty(level) && Enum.TryParse<OVRProjectSetup.TaskLevel>(level, true, out var taskLevel))
            {
                outstanding = outstanding.Where(task =>
                    task.Level.GetValue(buildTargetGroup) == taskLevel);
            }

            // Filter by group if specified
            if (!string.IsNullOrEmpty(group) && Enum.TryParse<OVRProjectSetup.TaskGroup>(group, true, out var taskGroup))
            {
                outstanding = outstanding.Where(task => task.Group == taskGroup);
            }

            var taskList = outstanding.ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"=== UPST REPORT ({buildTargetGroup}) ===");
            if (!string.IsNullOrEmpty(group)) sb.AppendLine($"Filter: Group={group}");
            if (!string.IsNullOrEmpty(level)) sb.AppendLine($"Filter: Level={level}");
            sb.AppendLine($"Total: {taskList.Count} outstanding tasks");
            sb.AppendLine();

            if (taskList.Count == 0)
            {
                sb.AppendLine("No outstanding tasks found matching the criteria.");
                return sb.ToString();
            }

            foreach (var task in taskList)
            {
                var taskLevelValue = task.Level.GetValue(buildTargetGroup);
                var hasAutoFix = task.FixAction != null || task.AsyncFixAction != null;
                var isManual = OVRProjectSetupStatus.IsManuallyFixable(task);

                sb.AppendLine($"  [{taskLevelValue}] [{task.Group}] {task.Message.GetValue(buildTargetGroup)}");
                sb.AppendLine($"    UID: {task.Uid}");
                sb.AppendLine($"    Auto-fixable: {(hasAutoFix ? "Yes" : "No")}{(isManual ? " (Manual)" : "")}");

                var fixMessage = task.FixMessage?.GetValue(buildTargetGroup);
                if (!string.IsNullOrEmpty(fixMessage))
                {
                    sb.AppendLine($"    Fix: {fixMessage}");
                }

                sb.AppendLine();
            }

            // Append available group and level values for discoverability
            sb.AppendLine("Available groups: " + string.Join(", ",
                Enum.GetNames(typeof(OVRProjectSetup.TaskGroup)).Where(n => n != "All")));
            sb.AppendLine("Available levels: " + string.Join(", ",
                Enum.GetNames(typeof(OVRProjectSetup.TaskLevel))));

            return sb.ToString();
        }

        [Tool(Description = "Preview what a configuration fix will change BEFORE applying it. Shows what settings will be modified, the current vs expected state, and links to relevant documentation. Always call this before FixTask() so the impact is understood.",
            Returns = "Detailed preview of the fix: what it changes, current vs expected state, severity, and documentation link")]
        internal string PreviewFix(string taskUid)
        {
            if (string.IsNullOrEmpty(taskUid))
            {
                return "Error: taskUid is required. Use GetReport() to find task UIDs.";
            }

            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);

            Hash128 uid;
            try
            {
                uid = Hash128.Parse(taskUid);
            }
            catch (Exception)
            {
                return $"Error: Invalid UID format '{taskUid}'. UIDs are 128-bit hashes from GetReport().";
            }

            var task = OVRProjectSetup.Registry.GetTask(uid);
            if (task == null)
            {
                return $"Error: No task found with UID '{taskUid}'.";
            }

            var message = task.Message.GetValue(buildTargetGroup);
            var level = task.Level.GetValue(buildTargetGroup);
            var fixMessage = task.FixMessage?.GetValue(buildTargetGroup);
            var url = task.URL?.GetValue(buildTargetGroup);
            var isDone = task.IsDone(buildTargetGroup);
            var hasAutoFix = task.FixAction != null || task.AsyncFixAction != null;
            var isManual = OVRProjectSetupStatus.IsManuallyFixable(task);

            var sb = new StringBuilder();
            sb.AppendLine($"=== FIX PREVIEW ===");
            sb.AppendLine();
            sb.AppendLine($"Task: {message}");
            sb.AppendLine($"Severity: {level}");
            sb.AppendLine($"Group: {task.Group}");
            sb.AppendLine($"Status: {(isDone ? "Already done" : "Outstanding")}");
            sb.AppendLine();

            if (isDone)
            {
                sb.AppendLine("This task is already satisfied. No fix needed.");
                return sb.ToString();
            }

            sb.AppendLine("--- What the fix will change ---");
            if (!string.IsNullOrEmpty(fixMessage))
            {
                sb.AppendLine(fixMessage);
            }
            else if (hasAutoFix)
            {
                sb.AppendLine("(No fix description available — an automatic fix exists but its details are not documented)");
            }
            else
            {
                sb.AppendLine("No automatic fix available. This task requires manual intervention.");
            }

            sb.AppendLine();
            sb.AppendLine($"Auto-fixable: {(hasAutoFix ? "Yes" : "No")}{(isManual ? " (Manual guided setup available)" : "")}");

            if (!string.IsNullOrEmpty(url))
            {
                sb.AppendLine($"Documentation: {url}");
            }

            if (hasAutoFix && !isDone)
            {
                sb.AppendLine();
                sb.AppendLine($"To apply this fix, call FixTask(\"{taskUid}\")");
            }

            return sb.ToString();
        }

        [Tool(Description = "Fix a specific Quest project configuration issue by its UID. Use GetReport() to find issue UIDs and PreviewFix() to understand what will change before applying.",
            Returns = "Result of the fix attempt - success or failure with details")]
        internal string FixTask(string taskUid)
        {
            if (string.IsNullOrEmpty(taskUid))
            {
                return "Error: taskUid is required. Use GetReport() to find task UIDs.";
            }

            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);

            Hash128 uid;
            try
            {
                uid = Hash128.Parse(taskUid);
            }
            catch (Exception)
            {
                return $"Error: Invalid UID format '{taskUid}'. UIDs are 128-bit hashes from GetReport().";
            }

            var task = OVRProjectSetup.Registry.GetTask(uid);
            if (task == null)
            {
                return $"Error: No task found with UID '{taskUid}'.";
            }

            if (task.IsDone(buildTargetGroup))
            {
                return $"Task '{task.Message.GetValue(buildTargetGroup)}' is already done.";
            }

            if (task.FixAction == null && task.AsyncFixAction == null)
            {
                return $"Task '{task.Message.GetValue(buildTargetGroup)}' has no automatic fix available.";
            }

            var result = task.Fix(buildTargetGroup);
            var message = task.Message.GetValue(buildTargetGroup);

            return result
                ? $"Successfully fixed: {message}"
                : $"Fix attempted but task is still not done: {message}. Manual intervention may be needed.";
        }

        [Tool(Description = "Fix all outstanding Quest project configuration issues at a given severity level. Levels: 'Required' (build-blocking), 'Recommended' (should fix), 'Optional' (nice-to-have).",
            Returns = "Summary of fix results with count of remaining issues")]
        internal string FixByLevel(string level)
        {
            if (string.IsNullOrEmpty(level))
            {
                return "Error: level is required. Valid values: Required, Recommended, Optional.";
            }

            if (!Enum.TryParse<OVRProjectSetup.TaskLevel>(level, true, out var taskLevel))
            {
                return $"Error: Invalid level '{level}'. Valid values: Required, Recommended, Optional.";
            }

            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);

            OVRProjectSetup.FixTasks(
                buildTargetGroup,
                filter: tasks => tasks
                    .Where(task => !task.IsDone(buildTargetGroup)
                        && !task.IsIgnored(buildTargetGroup)
                        && task.Level.GetValue(buildTargetGroup) == taskLevel)
                    .ToList(),
                logMessages: OVRProjectSetup.LogMessages.Summary,
                blocking: true);

            // Re-check status after fix
            var status = OVRProjectSetupStatus.ComputeStatus(buildTargetGroup);
            var remaining = status.OutstandingTasks != null
                ? OVRProjectSetupStatus.GetCountAtLevel(status.OutstandingTasks, buildTargetGroup, taskLevel)
                : 0;

            return remaining == 0
                ? $"All {taskLevel} tasks have been fixed."
                : $"Fix completed. {remaining} {taskLevel} task(s) still outstanding (may require manual intervention).";
        }

        [Tool(Description = "Fix all outstanding auto-fixable Quest project configuration issues across all severity levels. This is the quickest way to resolve all known setup problems at once.",
            Returns = "Summary of fix results with count of remaining issues that need manual intervention")]
        internal string FixAll()
        {
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);

            OVRProjectSetup.FixTasks(
                buildTargetGroup,
                filter: tasks => tasks
                    .Where(task => !task.IsDone(buildTargetGroup)
                        && !task.IsIgnored(buildTargetGroup))
                    .ToList(),
                logMessages: OVRProjectSetup.LogMessages.Summary,
                blocking: true);

            // Re-check status after fix
            var status = OVRProjectSetupStatus.ComputeStatus(buildTargetGroup);

            var sb = new StringBuilder();
            sb.AppendLine("=== FIX ALL COMPLETE ===");
            sb.AppendLine();
            sb.AppendLine(OVRProjectSetupStatus.ComputeStatusMessage(status));
            sb.AppendLine(OVRProjectSetupStatus.ComputeSubtitleMessage(status));

            if (status.TotalOutstandingCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"{status.TotalOutstandingCount} task(s) still outstanding (may require manual intervention).");
            }

            return sb.ToString();
        }
    }
}
