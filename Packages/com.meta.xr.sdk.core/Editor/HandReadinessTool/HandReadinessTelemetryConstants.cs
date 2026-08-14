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

namespace Meta.HandReadinessTool.Editor
{
    /// <summary>
    /// Falco event names, annotation keys, and controlled-value strings for
    /// HandReadinessTool telemetry. See TELEMETRY.md for the full event schema.
    /// </summary>
    internal static class HandReadinessTelemetryConstants
    {
        internal static class FalcoEventName
        {
            // Session lifecycle
            public const string Opened = "hrt_opened";
            public const string Closed = "hrt_closed";
            public const string StateRestored = "hrt_state_restored";

            // Funnel — one event per state entry.
            public const string ScreenViewed = "hrt_screen_viewed";

            // Per-screen primary actions.
            public const string WelcomeGetStartedClicked = "hrt_welcome_get_started_clicked";
            public const string CheckTypeSelected = "hrt_check_type_selected";
            public const string CheckTypeBackClicked = "hrt_check_type_back_clicked";
            public const string ProjectDescriptionSubmitted = "hrt_project_description_submitted";
            public const string ProjectDescriptionBackClicked = "hrt_project_description_back_clicked";
            public const string ProjectDescriptionFileUploaded = "hrt_project_description_file_uploaded";
            public const string ProjectDescriptionFileRemoved = "hrt_project_description_file_removed";

            // AgentBridge upsell — start of the conversion funnel.
            public const string AgentBridgeSetupClicked = "hrt_agentbridge_setup_clicked";

            // Scan lifecycle.
            public const string ScanStarted = "hrt_scan_started";
            public const string ScanCompleted = "hrt_scan_completed";

            // Per-issue actions.
            public const string FixStarted = "hrt_fix_started";
            public const string FixCompleted = "hrt_fix_completed";
            public const string ApplyAllStarted = "hrt_apply_all_started";
            public const string ApplyAllCompleted = "hrt_apply_all_completed";

            // Issue details popup.
            public const string IssueDetailsOpened = "hrt_issue_details_opened";
            public const string IssueDetailsNavigated = "hrt_issue_details_navigated";
            public const string IssueDetailsClosed = "hrt_issue_details_closed";
            public const string IssueCopiedToClipboard = "hrt_issue_copied_to_clipboard";
            public const string IssueMarkedComplete = "hrt_issue_marked_complete";

            // Footer actions.
            public const string ReportExported = "hrt_report_exported";
            public const string RescanClicked = "hrt_rescan_clicked";

            // Confirmation screen (reached when every recommendation is resolved).
            public const string ConfirmReadinessClicked = "hrt_confirm_readiness_clicked";
            public const string ConfirmationClosed = "hrt_confirmation_closed";
            public const string ConfirmationExploreClicked = "hrt_confirmation_explore_clicked";

            // Remote rules.
            public const string RulesFetched = "hrt_rules_fetched";

            // Remote AI-path prompt.
            public const string PromptFetched = "hrt_prompt_fetched";
        }

        internal static class AnnotationType
        {
            // Ambient (set by HandReadinessTelemetry.SendEvent on every event).
            public const string SessionId = "session_id";
            public const string AgentBridgeEnabled = "agentbridge_enabled";
            public const string AgentBridgeProvider = "agentbridge_provider";
            public const string AgentBridgeValidated = "agentbridge_validated";

            // Common.
            public const string Success = "success";
            public const string ErrorKind = "error_kind";
            public const string ErrorMessage = "error_message";
            public const string DurationMs = "duration_ms";
            public const string EntryPoint = "entry_point";
            public const string Source = "source";
            public const string Screen = "screen";
            public const string FromScreen = "from_screen";
            public const string LastScreen = "last_screen";
            public const string CompletedScan = "completed_scan";
            public const string MarkedCompleteCount = "marked_complete_count";

            // CheckType / scan-flow.
            public const string IntendedScanType = "intended_scan_type";
            public const string ActualScanType = "actual_scan_type";
            public const string FellBackToStandard = "fell_back_to_standard";
            public const string IsRescan = "is_rescan";
            public const string PriorIssueCount = "prior_issue_count";
            public const string SetupTaskCount = "setup_task_count";
            public const string AiSubscanSuccess = "ai_subscan_success";

            // Scan outcome counts.
            public const string IssuesFoundTotal = "issues_found_total";
            public const string IssuesHigh = "issues_high";
            public const string IssuesMedium = "issues_medium";
            public const string IssuesLow = "issues_low";
            public const string IssuesFromSetupTasks = "issues_from_setup_tasks";
            public const string IssuesFromRegex = "issues_from_regex";
            public const string IssuesFromAi = "issues_from_ai";
            public const string IssuesResolvedFromPrior = "issues_resolved_from_prior";

            // Cancellation.
            public const string CancelledFromScreen = "cancelled_from_screen";
            public const string CancelReason = "cancel_reason";

            // ProjectDescription.
            public const string DescriptionChars = "description_chars";
            public const string FileCount = "file_count";
            public const string HasText = "has_text";
            public const string AgentBridgeUpsellShown = "agentbridge_upsell_shown";
            public const string FileExtension = "file_extension";
            public const string FileSizeBytes = "file_size_bytes";

            // Remote rules.
            public const string RuleCount = "rule_count";
            public const string RuleSource = "rule_source";
            public const string SchemaVersion = "schema_version";

            // Remote AI-path prompt.
            public const string PromptSource = "prompt_source";
            public const string SystemPromptChars = "system_prompt_chars";
            public const string ReferenceCount = "reference_count";

            // Per-issue actions.
            public const string IssueUid = "issue_uid";
            public const string Priority = "priority";
            public const string Category = "category";
            public const string PayloadChars = "payload_chars";

            // Bulk apply.
            public const string TotalCount = "total_count";
            public const string SuccessCount = "success_count";
            public const string FailCount = "fail_count";

            // Issue details nav.
            public const string Direction = "direction";
            public const string FromIssueUid = "from_issue_uid";
            public const string ToIssueUid = "to_issue_uid";
            public const string MarkedComplete = "marked_complete";

            // Export.
            public const string Format = "format";
            public const string IssueCount = "issue_count";
            public const string UnfixedCount = "unfixed_count";
            public const string Cancelled = "cancelled";

            // Restore.
            public const string RestoredState = "restored_state";
            public const string RestoredIssueCount = "restored_issue_count";
        }

        internal static class ScanType
        {
            public const string Ai = "ai";
            public const string Standard = "standard";
        }

        internal static class ScreenName
        {
            public const string Welcome = "welcome";
            public const string CheckType = "check_type";
            public const string ProjectDescription = "project_description";
            public const string Scanning = "scanning";
            public const string Results = "results";
            public const string Confirmation = "confirmation";
        }

        internal static class EntryPoint
        {
            public const string Menu = "menu";
            public const string Unknown = "unknown";
        }

        internal static class Source
        {
            public const string CheckReadinessButton = "check_readiness_button";
            public const string ReanalyzeLink = "reanalyze_link";
            public const string RowButton = "row_button";
        }

        internal static class CancelReason
        {
            public const string WindowClose = "window_close";
            public const string BackClicked = "back_clicked";
            public const string RescanClicked = "rescan_clicked";
        }

        internal static class Direction
        {
            public const string Next = "next";
            public const string Back = "back";
        }

        internal static class Priority
        {
            public const string High = "high";
            public const string Medium = "medium";
            public const string Low = "low";
        }

        internal static class IssueCategoryName
        {
            public const string Ai = "ai";
            public const string Manual = "manual";
            public const string Automation = "automation";
        }

        internal static class RuleSourceValue
        {
            public const string Remote = "remote";
            public const string Local = "local";
        }

        internal static class PromptSourceValue
        {
            public const string Remote = "remote";
            public const string Local = "local";
        }

        internal static class ErrorKind
        {
            // AI subscan failure modes.
            public const string AiKnowledgeMissing = "ai_knowledge_missing";
            public const string AiSendReturnedFalse = "ai_send_returned_false";
            public const string AiSendThrew = "ai_send_threw";
            public const string AiNoAssistantMessage = "ai_no_assistant_message";
            public const string AiResponseEmpty = "ai_response_empty";
            public const string AiParseFailed = "ai_parse_failed";
            public const string AiTimeout = "ai_timeout";

            // Standard subscan failure modes.
            public const string CodeScanIoError = "code_scan_io_error";
            public const string SetupTaskCheckThrew = "setup_task_check_threw";

            // Remote rules failure modes.
            public const string RulesFetchFailed = "rules_fetch_failed";
            public const string RulesSchemaMismatch = "rules_schema_mismatch";
            public const string RulesAllInvalid = "rules_all_invalid";
            public const string RulesEmptyPayload = "rules_empty_payload";

            // Remote AI-path prompt failure modes.
            public const string PromptFetchFailed = "prompt_fetch_failed";
            public const string PromptSchemaMismatch = "prompt_schema_mismatch";
            public const string PromptEmptySystem = "prompt_empty_system";

            // Fix failure modes.
            public const string FixThrew = "fix_threw";
            public const string FixDidntResolve = "fix_didnt_resolve";
            public const string TaskLookupMissed = "task_lookup_missed";

            // Cancellation.
            public const string Cancelled = "cancelled";

            // Catch-all.
            public const string Unknown = "unknown";

            // File upload.
            public const string UnsupportedFormat = "unsupported_format";
            public const string ReadFailed = "read_failed";
        }
    }
}
