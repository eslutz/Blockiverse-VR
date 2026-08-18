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

using UnityEditor;
using UnityEngine;

namespace Meta.XR.Simulator.Editor
{
    internal class LogUtils
    {
        /// <summary>
        /// Logs an informational message to the Unity console.
        /// </summary>
        /// <param name="title">The title or source of the log message.</param>
        /// <param name="body">The body content of the log message.</param>
        public virtual void ReportInfo(string title, string body)
        {
            Debug.Log($"[{title}] {body}");
        }

        /// <summary>
        /// Logs a warning message to the Unity console.
        /// </summary>
        /// <param name="title">The title or source of the warning message.</param>
        /// <param name="body">The body content of the warning message.</param>
        public virtual void ReportWarning(string title, string body)
        {
            Debug.LogWarning($"[{title}] {body}");
        }

        /// <summary>
        /// Logs an error message to the Unity console.
        /// </summary>
        /// <param name="title">The title or source of the error message.</param>
        /// <param name="body">The body content of the error message.</param>
        public virtual void ReportError(string title, string body)
        {
            Debug.LogError($"[{title}] {body}");
        }

        /// <summary>
        /// Displays an error dialog to the user or logs an error if in batch mode.
        /// </summary>
        /// <param name="title">The title of the dialog or error message.</param>
        /// <param name="body">The body content of the dialog or error message.</param>
        /// <param name="forceHideDialog">When true, suppresses the dialog and only logs the error.</param>
        public virtual void DisplayDialogOrError(string title, string body, bool forceHideDialog = false)
        {
            if (!forceHideDialog && !Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(title, body, "Ok");
            }

            ReportError(title, body);
        }

        /// <summary>
        /// Displays a confirmation dialog with OK and Cancel buttons.
        /// </summary>
        /// <param name="title">The title of the dialog.</param>
        /// <param name="body">The body content of the dialog.</param>
        /// <param name="okButtonText">The text for the OK button.</param>
        /// <param name="cancelButtonText">The text for the Cancel button.</param>
        /// <returns>True if the user clicked OK, false if canceled or in batch mode.</returns>
        public virtual bool DisplayDialog(string title, string body, string okButtonText, string cancelButtonText)
        {
            if (!Application.isBatchMode)
            {
                return EditorUtility.DisplayDialog(title, body, okButtonText, cancelButtonText);
            }
            return false;
        }

        /// <summary>
        /// Creates a new progress indicator in the Unity Editor.
        /// </summary>
        /// <param name="title">The title displayed on the progress indicator.</param>
        /// <param name="shouldReposition">Whether to reposition the progress details window.</param>
        /// <returns>The progress identifier used to update or remove the progress indicator.</returns>
        public virtual int CreateProgress(string title, bool shouldReposition)
        {
            int progressId = Progress.Start(title);
            if (!Application.isBatchMode)
            {
                Progress.ShowDetails(shouldReposition);
            }
            return progressId;
        }
    }

}
