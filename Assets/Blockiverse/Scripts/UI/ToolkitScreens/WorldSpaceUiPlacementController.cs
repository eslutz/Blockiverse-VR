using Blockiverse.Core;
using UnityEngine;

namespace Blockiverse.UI
{
    // World-space placement for a UI Toolkit panel (ADR 0010 / migration matrix Phase 2:
    // the presenter's placement half, re-hosted on a UIDocument transform). All pose math
    // is BlockiversePanelPlacement, reused unchanged; this component only decides when to
    // apply it. Visibility is NOT handled here — the screen controller collapses the
    // document root and its collider (never the UIDocument) — so this component can keep
    // gliding a panel that is mid-hide without fighting the renderer.
    public sealed class WorldSpaceUiPlacementController : MonoBehaviour
    {
        // The uGUI menu constants carried over verbatim: these are headset-validated
        // physical values (metres/degrees), independent of the render framework.
        public const float MenuDistanceMeters = 0.95f;
        public const float MenuVerticalOffsetMeters = -0.38f;
        public const float MenuPitchDegrees = 10f;
        public const float HudDistanceMeters = 1.15f;
        public const float HudVerticalOffsetMeters = -0.30f;
        public const float HudPitchDegrees = 12f;

        // Project-wide world-space panel scale: 100 ppu documents at 0.1 scale, so panel
        // pixels ÷ 1000 = metres. The comfort UI scale multiplies this at apply time.
        public const float BasePanelScale = 0.1f;

        [SerializeField] float distanceMeters = MenuDistanceMeters;
        [SerializeField] float horizontalOffsetMeters;
        [SerializeField] float verticalOffsetMeters = MenuVerticalOffsetMeters;
        [SerializeField] float pitchDegrees = MenuPitchDegrees;
        [SerializeField] BlockiversePanelPlacementMode placementMode = BlockiversePanelPlacementMode.RecenterOnShow;
        [SerializeField] float followYawThresholdDegrees = BlockiversePanelPlacement.DefaultFollowYawThresholdDegrees;
        [SerializeField] float followDistanceThresholdMeters = BlockiversePanelPlacement.DefaultFollowDistanceThresholdMeters;
        [SerializeField] float followSmoothingSeconds = BlockiversePanelPlacement.DefaultFollowSmoothingSeconds;

        Transform headset;
        BlockiverseComfortSettings comfortSettings;
        Pose worldFixedPose;
        bool hasWorldFixedPose;
        bool followGliding;
        float appliedUiScale = 1.0f;

        public BlockiversePanelPlacementMode PlacementMode => placementMode;
        public bool HasWorldFixedPose => hasWorldFixedPose;
        public Pose WorldFixedPose => worldFixedPose;
        public bool IsFollowGliding => followGliding;

        public void Configure(
            Transform headsetTransform,
            float distance,
            float horizontalOffset,
            float verticalOffset,
            float pitch)
        {
            headset = headsetTransform;
            distanceMeters = distance;
            horizontalOffsetMeters = horizontalOffset;
            verticalOffsetMeters = verticalOffset;
            pitchDegrees = pitch;
        }

        public void ConfigureComfortSettings(BlockiverseComfortSettings settings) => comfortSettings = settings;

        public void SetPlacementMode(BlockiversePanelPlacementMode mode)
        {
            placementMode = mode;
            followGliding = false;
        }

        public void SetWorldFixedPose(Pose pose)
        {
            worldFixedPose = pose;
            hasWorldFixedPose = true;
        }

        // Called by the host when this panel's screen becomes the routed one. recenter=false
        // preserves the shared menu anchor: navigating between menus must never jump the
        // panel (matrix §4 item 11).
        public void OnShown(bool recenter)
        {
            ApplyScale();

            if (placementMode == BlockiversePanelPlacementMode.WorldFixed)
            {
                if (hasWorldFixedPose)
                    ApplyPose(worldFixedPose);
                else if (recenter)
                    Recenter();
                return;
            }

            if (placementMode == BlockiversePanelPlacementMode.LazyFollow)
            {
                if (recenter)
                    Recenter();
                followGliding = false;
                return;
            }

            if (recenter)
                Recenter();
        }

        // Inherit another panel's pose so a navigation step keeps the stack's shared anchor.
        public void ApplyPlacementFrom(WorldSpaceUiPlacementController source)
        {
            if (source == null)
                return;

            ApplyPose(new Pose(source.transform.position, source.transform.rotation));
            ApplyScale();
        }

        public void Recenter()
        {
            Transform head = ResolveHeadset();
            if (head == null)
                return;

            Pose target = BlockiversePanelPlacement.FollowTargetPose(
                head.position,
                head.forward,
                distanceMeters,
                horizontalOffsetMeters,
                verticalOffsetMeters);
            ApplyPose(new Pose(target.position, target.rotation * Quaternion.Euler(pitchDegrees, 0f, 0f)));
        }

        void LateUpdate()
        {
            float uiScale = comfortSettings != null ? comfortSettings.UiScale : 1.0f;
            if (!Mathf.Approximately(uiScale, appliedUiScale))
                ApplyScale();

            if (placementMode != BlockiversePanelPlacementMode.LazyFollow)
                return;

            Transform head = ResolveHeadset();
            if (head == null)
                return;

            Pose ideal = BlockiversePanelPlacement.FollowTargetPose(
                head.position,
                head.forward,
                distanceMeters,
                horizontalOffsetMeters,
                verticalOffsetMeters);
            var target = new Pose(ideal.position, ideal.rotation * Quaternion.Euler(pitchDegrees, 0f, 0f));

            if (!followGliding &&
                BlockiversePanelPlacement.ShouldRecenter(
                    new Pose(transform.position, transform.rotation),
                    head.position,
                    head.forward,
                    distanceMeters,
                    followYawThresholdDegrees,
                    followDistanceThresholdMeters))
            {
                followGliding = true;
            }

            if (!followGliding)
                return;

            Pose next = BlockiversePanelPlacement.SmoothToward(
                new Pose(transform.position, transform.rotation),
                target,
                followSmoothingSeconds,
                Time.deltaTime);
            ApplyPose(next);

            if (Vector3.Distance(next.position, target.position) < 0.01f &&
                Quaternion.Angle(next.rotation, target.rotation) < 0.5f)
            {
                ApplyPose(target);
                followGliding = false;
            }
        }

        void ApplyPose(Pose pose)
        {
            // World poses only: a panel parented under the rig would be dragged around by
            // locomotion (matrix §4 item 13 — the uGUI presenter reparents to null for the
            // same reason).
            if (transform.parent != null)
                transform.SetParent(null, worldPositionStays: true);

            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        void ApplyScale()
        {
            appliedUiScale = comfortSettings != null ? comfortSettings.UiScale : 1.0f;
            transform.localScale = Vector3.one * (BasePanelScale * appliedUiScale);
        }

        Transform ResolveHeadset()
        {
            if (headset == null && Camera.main != null)
                headset = Camera.main.transform;
            return headset;
        }
    }
}
