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
using System.IO;

using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor;

using UnityEngine;


/// <summary>
/// Build processor that writes build metadata (timestamp, revision hash) to a runtime-loadable text asset and
/// manages input action binding files in StreamingAssets. Prepares bindings before build and cleans up after.
/// </summary>
public class OVRDumpBuildInfo : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    /// <summary>
    /// Gets the execution order for this build preprocessor callback.
    /// </summary>
    public int callbackOrder => 0;

    /// <summary>
    /// Called before a build. Writes build info (internal builds only) and prepares runtime input action bindings on disk.
    /// </summary>
    /// <param name="report">The build report containing build target and configuration details.</param>
    public void OnPreprocessBuild(BuildReport report)
    {
        PrepareRuntimeActionBindings();
    }

    /// <summary>
    /// Writes runtime input action bindings to the StreamingAssets directory so they are available at runtime.
    /// </summary>
    public static void PrepareRuntimeActionBindings()
    {
        // Save to streaming assets dir.
        Meta.XR.InputActions.RuntimeSettings.UpdateBindingsOnDisk();
    }

    /// <summary>
    /// Called after a build completes. Cleans up generated binding files from StreamingAssets and copies them to the standalone build output on PC.
    /// </summary>
    /// <param name="report">The build report containing the output path and platform group.</param>
    public void OnPostprocessBuild(BuildReport report)
    {
        // Copy path from streaming assets folder to root directory.
        // We don't do this on android since on android we can get the apk path & access them that way,
        // but on windows there's no central owner to provide access to the data path.

        var pcPath = report.summary.platformGroup == BuildTargetGroup.Standalone ? report.summary.outputPath
                                                                                 : null;

        // Clean up jsons since we generated them in OnPreprocessBuild,
        // and they have no reason to be persisted in the project.

        Meta.XR.InputActions.RuntimeSettings.UpdateBindingsOnDisk(clean: true, buildPath: pcPath);

        // (Allow any exceptions above to bubble up so builds fail and buildmasters get a clear signal.)
        // ((Reason: If an app depends on these input bindings, then it's a broken build without them.))
    }
}
