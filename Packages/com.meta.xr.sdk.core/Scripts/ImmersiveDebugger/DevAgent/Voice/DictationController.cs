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

#if HAS_META_VOICE_SDK
using Meta.XR.ImmersiveDebugger;
using Oculus.Voice.Dictation;
using System;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.DevAgent
{
    internal class DictationController : MonoBehaviour
    {
        [SerializeField]
        internal AppDictationExperience experience;

        [DebugMember(Category = "Voice Integration")]
        private string _full;
        [DebugMember(Category = "Voice Integration")]
        private string _partial;
        [DebugMember(Category = "Voice Integration")]
        private string _concatenatedFull = "";
        private readonly DictationTranscriptAccumulator _accumulator = new DictationTranscriptAccumulator();
        // Tracks the logical push-to-talk session, independent of experience.Active, so a release
        // still finalizes after the SDK auto-stops (timeout/inactivity) and so transcription
        // callbacks that arrive between sessions are ignored.
        private bool _sessionActive;
        [DebugMember(Category = "Voice Integration")]
        private bool micActive => experience.MicActive;
        [DebugMember(Category = "Voice Integration")]
        private bool experienceActive => experience.Active;
        [DebugMember(Category = "Voice Integration")]
        private bool requestActive => experience.IsRequestActive;

        [DebugMember(Category = "Voice Integration")]
        private float _currentMicLevel;

        // Events for live transcription
        internal event Action<string> OnPartialTranscriptionUpdate;
        internal event Action<string> OnTranscriptionFinalized;
        // Fired when the dictation service / Wit request fails (e.g. auth/quota/network).
        internal event Action<string> OnDictationError;

        [DebugMember(Category = "Voice Integration")]
        private string _lastEvent;
        private string LastEvent
        {
            get => _lastEvent;
            set
            {
                _lastEvent = value;
            }
        }
        private void Start()
        {
            experience.TranscriptionEvents.OnPartialTranscription.AddListener(OnPartialTranscription);
            experience.TranscriptionEvents.OnFullTranscription.AddListener(OnFullTranscription);
            experience.DictationEvents.OnDictationSessionStarted.AddListener(_ => { LastEvent = "OnDictationSessionStarted"; });
            experience.DictationEvents.OnDictationSessionStopped.AddListener(_ => { LastEvent = "OnDictationSessionStopped"; });
            experience.DictationEvents.OnStartListening.AddListener(() => { LastEvent = "OnStartListening"; });
            experience.DictationEvents.OnStoppedListening.AddListener(() => { LastEvent = "OnStoppedListening"; });
            experience.DictationEvents.OnMicStartedListening.AddListener(() => { LastEvent = "OnMicStartedListening"; });
            experience.DictationEvents.OnMicStoppedListening.AddListener(() => { LastEvent = "OnMicStoppedListening"; });
            experience.DictationEvents.OnStoppedListeningDueToDeactivation.AddListener(() => { LastEvent = "OnStoppedListeningDueToDeactivation"; });
            experience.DictationEvents.OnStoppedListeningDueToInactivity.AddListener(() => { LastEvent = "OnStoppedListeningDueToInactivity"; });
            experience.DictationEvents.OnStoppedListeningDueToTimeout.AddListener(() => { LastEvent = "OnStoppedListeningDueToTimeout"; });
            experience.DictationEvents.OnMicAudioLevelChanged.AddListener(OnMicAudioLevelChanged);
            experience.DictationEvents.OnError.AddListener(OnDictationServiceError);
        }

        private void OnMicAudioLevelChanged(float level)
        {
            _currentMicLevel = level;
        }

        private void OnDictationServiceError(string errorType, string errorMessage)
        {
            LastEvent = "OnError";
            // The request failed (auth/quota/network) — end the logical session so a later release
            // doesn't try to finalize, and surface the reason to the UI.
            _sessionActive = false;
            _accumulator.Reset();
            var detail = !string.IsNullOrEmpty(errorMessage) ? errorMessage : errorType;
            OnDictationError?.Invoke(detail);
        }

        [DebugMember(Category = "Voice Integration")]
        internal void Toggle()
        {
            Toggle(!_sessionActive);
        }

        internal void Toggle(bool activate)
        {
            if (activate)
            {
                if (_sessionActive)
                {
                    return;
                }
                _sessionActive = true;
                // activating it, resetting all transcriptions
                _full = "";
                _partial = "";
                _concatenatedFull = "";
                _accumulator.Reset();
                if (!experience.Active)
                {
                    experience.ActivateImmediately();
                }
            }
            else
            {
                if (!_sessionActive)
                {
                    return;
                }
                _sessionActive = false;
                if (experience.Active)
                {
                    experience.Deactivate();
                }
                // stopping, finalize the transcription (falls back to the last partial if no final
                // arrived before release, so a quick utterance is sent rather than dropped). Driven by
                // the logical session, not experience.Active, so a release after the SDK auto-stops
                // (timeout/inactivity) still finalizes instead of leaving the prompt stuck.
                OnTranscriptionFinalized?.Invoke(_accumulator.GetFinalTranscript());
            }
        }

        /// <summary>
        /// Abandon the current session without emitting a transcript — used when the conversation is
        /// cleared or the headset is doffed mid-dictation, so a stale utterance is not sent and the
        /// session does not stay stuck active.
        /// </summary>
        internal void Cancel()
        {
            if (!_sessionActive)
            {
                return;
            }
            _sessionActive = false;
            if (experience.Active)
            {
                experience.Deactivate();
            }
            _accumulator.Reset();
        }

        private void OnPartialTranscription(string transcription)
        {
            // Ignore callbacks that arrive outside an active session (e.g. a late emission from the
            // previous utterance after a quick re-press) so they don't leak into the next prompt.
            if (!_sessionActive)
            {
                return;
            }

            _partial = transcription;

            // Notify listeners of partial transcription update
            // This will be the full message with all previous full concatenated + current partial
            string combined = _accumulator.OnPartial(transcription);
            OnPartialTranscriptionUpdate?.Invoke(combined);
        }

        private void OnFullTranscription(string transcription)
        {
            if (!_sessionActive)
            {
                return;
            }

            _full = transcription;
            _accumulator.CommitFull(transcription);
            _concatenatedFull = _accumulator.GetFinalTranscript();
        }
    }
}
#endif
