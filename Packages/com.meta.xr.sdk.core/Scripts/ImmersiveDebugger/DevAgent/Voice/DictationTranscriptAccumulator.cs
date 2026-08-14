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
using System.Text;

namespace Meta.XR.ImmersiveDebugger.DevAgent
{
    /// <summary>
    /// Builds the dictation transcript from the Voice SDK's partial/full events.
    /// Handles two failure modes of raw accumulation:
    ///  - A final that repeats the previous one is dropped: the dictation session can re-emit the
    ///    same final segment on deactivation/keep-alive, which would otherwise duplicate the text
    ///    (e.g. "Bounce both objects. Bounce both objects.").
    ///  - If the user releases push-to-talk before the current sentence is finalized, the in-progress
    ///    partial is appended to the committed finals (or used on its own when nothing was finalized)
    ///    so the trailing sentence the user was still speaking isn't dropped.
    /// Kept free of any Voice SDK dependency so it is unit-testable without a device.
    /// </summary>
    internal sealed class DictationTranscriptAccumulator
    {
        private readonly StringBuilder _committed = new StringBuilder();
        private string _lastFull = "";
        // The most recent partial that has not yet been finalized into a full — i.e. the sentence
        // still in progress. Held so a trailing sentence released before its final arrives is sent
        // rather than dropped; cleared once a final supersedes it.
        private string _pendingPartial = "";

        internal void Reset()
        {
            _committed.Clear();
            _lastFull = "";
            _pendingPartial = "";
        }

        /// <summary>
        /// Record the current partial and return the live display (committed segments + partial).
        /// </summary>
        internal string OnPartial(string partial)
        {
            _pendingPartial = partial ?? "";
            return _committed.ToString() + _pendingPartial;
        }

        /// <summary>Commit a finalized segment, ignoring empty or repeated re-emissions.</summary>
        internal void CommitFull(string full)
        {
            if (string.IsNullOrEmpty(full))
            {
                return;
            }

            var trimmed = full.Trim();
            if (trimmed.Length == 0)
            {
                return;
            }

            // This final supersedes the in-progress partial for the same sentence, so drop it — it
            // must not also be appended as a trailing fragment.
            _pendingPartial = "";

            // Drop a re-emitted final that matches the previous one ignoring trailing punctuation
            // and whitespace, so deactivation/keep-alive re-emits (incl. "X" then "X.") don't
            // duplicate the text.
            if (string.Equals(NormalizeForDedupe(trimmed), NormalizeForDedupe(_lastFull), StringComparison.Ordinal))
            {
                return;
            }

            _lastFull = trimmed;
            _committed.Append(trimmed);
            // Separate segments with a sentence break, preserving punctuation the SDK already
            // provided so we don't produce e.g. "What is this?. ".
            if (!EndsWithSentencePunctuation(trimmed))
            {
                _committed.Append('.');
            }
            _committed.Append(' ');
        }

        private static string NormalizeForDedupe(string s)
        {
            return s.TrimEnd(' ', '\t', '\n', '\r', '.', '!', '?', ',');
        }

        private static bool EndsWithSentencePunctuation(string s)
        {
            if (s.Length == 0)
            {
                return false;
            }

            var c = s[s.Length - 1];
            return c == '.' || c == '!' || c == '?';
        }

        /// <summary>
        /// The transcript to send when dictation stops: the committed final segments plus the
        /// in-progress sentence the user was still speaking when they released (so it isn't dropped).
        /// When nothing was finalized this is just that trailing partial; empty only when nothing was
        /// spoken.
        /// </summary>
        internal string GetFinalTranscript()
        {
            var committed = _committed.ToString();
            var pending = _pendingPartial.Trim();

            if (pending.Length == 0)
            {
                return committed;
            }

            // Don't re-append a partial that merely repeats the last committed final (e.g. a stray
            // re-emitted partial after its final already landed).
            if (string.Equals(NormalizeForDedupe(pending), NormalizeForDedupe(_lastFull), StringComparison.Ordinal))
            {
                return committed;
            }

            return committed + pending;
        }
    }
}
