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
using System.Reflection;
using System.Threading.Tasks;
using Meta.XR.Editor.RemoteContent;
using Meta.XR.Editor.Utils;
using Meta.XR.Telemetry;
using UnityEditor;
using UnityEngine;
#if USING_XR_SDK_OPENXR
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace Meta.XR.Editor
{
    [InitializeOnLoad]
    internal static class FeatureActiveRules
    {
        private static readonly string MetaXRDirectory = Path.Combine("Assets", "MetaXR");

        static FeatureActiveRules()
        {
            RegisterAppSpaceWarpTask();
            RegisterOVRCameraRigTask();
            RegisterOVRManagerTask();
            RegisterHandTrackingPrefabTask();
            RegisterEyeLevelOrFloorLevelTrackingTask();
            RegisterFloorLevelTrackingTask();
            RegisterHandTrackingTask();
            RegisterControllerTrackingTask();
            RegisterEyeTrackingTask();
            RegisterFoveatedRenderingTask();
            RegisterPassthroughTask();
            RegisterPassthroughOverlayTask();
            RegisterSpatialAnchorsTask();
            RegisterSharedSpatialAnchorsTask();
            RegisterSceneUnderstandingTask();
            RegisterTrackedKeyboardTask();
            RegisterBodyTrackingTask();
            RegisterFaceTrackingTask();
            RegisterRenderModelTask();
            RegisterBoundaryVisibilityTask();
            RegisterColocationSessionTask();
        }

        private static void RegisterAppSpaceWarpTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Optional,
                platform: BuildTargetGroup.Android,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: IsAppSpaceWarpEnabled,
                message: "Enable Application SpaceWarp for improved rendering performance",
                fix: FixAppSpaceWarp,
                fixMessage: "Enable the Application SpaceWarp OpenXR feature"
            );
        }

        private static bool IsAppSpaceWarpEnabled(BuildTargetGroup buildTargetGroup)
        {
#if USING_XR_SDK_OPENXR
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
            if (settings == null) return false;

#if UNITY_OPENXR_PLUGIN_1_15_0_OR_NEWER && UNITY_6000_1_OR_NEWER
            var spaceWarpFeature = settings.GetFeature<SpaceWarpFeature>();
            if (spaceWarpFeature != null && spaceWarpFeature.enabled) return true;
#endif

            var metaSpaceWarpFeature = settings.GetFeature<Meta.XR.MetaXRSpaceWarp>();
            if (metaSpaceWarpFeature != null && metaSpaceWarpFeature.enabled) return true;
#endif
            return false;
        }

        private static void FixAppSpaceWarp(BuildTargetGroup buildTargetGroup)
        {
#if USING_XR_SDK_OPENXR
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
            if (settings == null) return;

#if UNITY_OPENXR_PLUGIN_1_15_0_OR_NEWER && UNITY_6000_1_OR_NEWER
            var spaceWarpFeature = settings.GetFeature<SpaceWarpFeature>();
            if (spaceWarpFeature != null)
            {
                spaceWarpFeature.enabled = true;
                FeatureHelpers.RefreshFeatures(buildTargetGroup);
                return;
            }
#endif

            var metaSpaceWarpFeature = settings.GetFeature<Meta.XR.MetaXRSpaceWarp>();
            if (metaSpaceWarpFeature != null)
            {
                metaSpaceWarpFeature.enabled = true;
                FeatureHelpers.RefreshFeatures(buildTargetGroup);
            }
#endif
        }

        private static void RegisterOVRCameraRigTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ => OVRProjectSetupUtils.FindComponentInScene<OVRCameraRig>() != null,
                message: "Use OVRCameraRig for VR camera setup with head tracking and stereo rendering"
            );
        }

        private static void RegisterOVRManagerTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ => OVRProjectSetupUtils.FindComponentInScene<OVRManager>() != null,
                message: "Use OVRManager for central VR system and device state management"
            );
        }

        private static void RegisterHandTrackingPrefabTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ => OVRProjectSetupUtils.FindComponentInScene<OVRHand>() != null,
                message: "Use OVRHandPrefab for hand tracking with physics capsules and gesture support"
            );
        }

        private static void RegisterEyeLevelOrFloorLevelTrackingTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                {
                    var ovrManager = OVRProjectSetupUtils.FindComponentInScene<OVRManager>();
                    return ovrManager != null &&
                           (ovrManager.trackingOriginType == OVRManager.TrackingOrigin.EyeLevel ||
                            ovrManager.trackingOriginType == OVRManager.TrackingOrigin.FloorLevel);
                },
                message: "Use eye-level or floor-level tracking origin"
            );
        }

        private static void RegisterFloorLevelTrackingTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ =>
                {
                    var ovrManager = OVRProjectSetupUtils.FindComponentInScene<OVRManager>();
                    return ovrManager != null &&
                           ovrManager.trackingOriginType != OVRManager.TrackingOrigin.EyeLevel;
                },
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                {
                    var ovrManager = OVRProjectSetupUtils.FindComponentInScene<OVRManager>();
                    return ovrManager != null &&
                           ovrManager.trackingOriginType == OVRManager.TrackingOrigin.FloorLevel;
                },
                message: "Use floor-level tracking origin"
            );
        }

        private static void RegisterHandTrackingTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.handTrackingSupport !=
                    OVRProjectConfig.HandTrackingSupport.ControllersOnly,
                message: "Enable hand tracking support"
            );
        }

        private static void RegisterControllerTrackingTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.handTrackingSupport !=
                    OVRProjectConfig.HandTrackingSupport.HandsOnly,
                message: "Enable controller tracking support"
            );
        }

        private static void RegisterEyeTrackingTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.eyeTrackingSupport !=
                    OVRProjectConfig.FeatureSupport.None,
                message: "Enable eye tracking support"
            );
        }

        private static void RegisterFoveatedRenderingTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => OVRProjectSetupUtils.FindComponentInScene<OVRManager>() != null,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRManager.foveatedRenderingLevel != OVRManager.FoveatedRenderingLevel.Off,
                message: "Enable foveated rendering for performance optimization"
            );
        }

        private static void RegisterPassthroughTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.insightPassthroughSupport !=
                    OVRProjectConfig.FeatureSupport.None ||
                    OVRProjectSetupUtils.FindComponentInScene<OVRPassthroughLayer>() != null,
                message: "Enable Passthrough for mixed reality camera access"
            );
        }

        private static void RegisterPassthroughOverlayTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ =>
                    OVRProjectSetupUtils.FindComponentInScene<OVRPassthroughLayer>() != null,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                {
                    var layers = OVRProjectSetupUtils.FindComponentsInScene<OVRPassthroughLayer>();
#pragma warning disable CS0618 // Intentional use of deprecated overlayType for feature detection
                    return layers.Any(layer => layer.overlayType == OVROverlay.OverlayType.Overlay);
#pragma warning restore CS0618
                },
                message: "Use Passthrough overlay for passthrough compositing"
            );
        }

        private static void RegisterSpatialAnchorsTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.anchorSupport ==
                    OVRProjectConfig.AnchorSupport.Enabled ||
                    OVRProjectSetupUtils.FindComponentInScene<OVRSpatialAnchor>() != null,
                message: "Enable Spatial Anchors for persistent, world-locked content"
            );
        }

        private static void RegisterSharedSpatialAnchorsTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.sharedAnchorSupport !=
                    OVRProjectConfig.FeatureSupport.None,
                message: "Enable Shared Spatial Anchors for multi-user scenarios"
            );
        }

        private static void RegisterSceneUnderstandingTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.sceneSupport !=
                    OVRProjectConfig.FeatureSupport.None ||
#pragma warning disable CS0618 // Intentional use of deprecated OVRSceneManager for feature detection
                    OVRProjectSetupUtils.FindComponentInScene<OVRSceneManager>() != null,
#pragma warning restore CS0618
                message:
                    "Enable Scene Understanding for plane detection, room layout, and scene model generation"
            );
        }

        private static void RegisterTrackedKeyboardTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.trackedKeyboardSupport !=
                    OVRProjectConfig.TrackedKeyboardSupport.None,
                message: "Enable Tracked Keyboard SDK for physical keyboard tracking and integration"
            );
        }

        private static void RegisterBodyTrackingTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.bodyTrackingSupport !=
                    OVRProjectConfig.FeatureSupport.None,
                message: "Enable Body Tracking for full body pose estimation"
            );
        }

        private static void RegisterFaceTrackingTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.faceTrackingSupport !=
                    OVRProjectConfig.FeatureSupport.None,
                message: "Enable Face Tracking for facial expression detection"
            );
        }

        private static void RegisterRenderModelTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.renderModelSupport !=
                    OVRProjectConfig.RenderModelSupport.Disabled,
                message: "Enable Render Model support for controller visualization"
            );
        }

        private static void RegisterBoundaryVisibilityTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.boundaryVisibilitySupport !=
                    OVRProjectConfig.FeatureSupport.None,
                message: "Enable Boundary Visibility for guardian boundary control"
            );
        }

        private static void RegisterColocationSessionTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => true,
                level: OVRProjectSetup.TaskLevel.Hidden,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                    OVRProjectConfig.CachedProjectConfig.colocationSessionSupport !=
                    OVRProjectConfig.FeatureSupport.None,
                message: "Enable Colocation Session support for co-located multiplayer"
            );
        }

    }
}
