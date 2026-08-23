using UnityEngine;

namespace Blockiverse.Gameplay
{
    /// <summary>
    /// Pure math for the avatar mirror (issue #340). The mirror shows the player's
    /// loopback avatar as a reflection: the player's pose is expressed in the mirror
    /// pane's frame, reflected across the pane plane, and re-expressed in the studio's
    /// frame — the pocket where the loopback entity and its render-texture camera live.
    /// Handedness is completed by sampling the render texture X-flipped: a reflection is
    /// an improper transform, so the geometric half here (position + yaw) plus the image
    /// flip together read as a true mirror.
    /// </summary>
    public static class MirrorPoseMath
    {
        /// <summary>
        /// Reflect a world-space pose across the mirror plane and express it relative to
        /// the pane frame. paneNormal must be unit length and point out of the pane toward
        /// the viewer; paneBasis is the rotation whose forward is paneNormal.
        /// </summary>
        public static void ReflectIntoPaneFrame(
            Vector3 paneCenter,
            Quaternion paneBasis,
            Vector3 worldPosition,
            Vector3 worldForward,
            out Vector3 paneLocalPosition,
            out Vector3 paneLocalForward)
        {
            Quaternion inversePane = Quaternion.Inverse(paneBasis);
            Vector3 local = inversePane * (worldPosition - paneCenter);
            Vector3 localForward = inversePane * worldForward;

            // The pane plane is the local XY plane; reflection negates the local Z.
            paneLocalPosition = new Vector3(local.x, local.y, -local.z);
            paneLocalForward = new Vector3(localForward.x, localForward.y, -localForward.z);
        }

        /// <summary>
        /// Compose the studio-space pose for the loopback entity root: the reflected pane-
        /// local pose re-expressed in the studio frame. The studio's forward corresponds to
        /// the pane's outward normal, so an avatar standing in front of the mirror appears
        /// in front of the studio camera.
        /// </summary>
        public static void ComposeStudioPose(
            Vector3 studioCenter,
            Quaternion studioBasis,
            Vector3 paneLocalPosition,
            Vector3 paneLocalForward,
            out Vector3 studioPosition,
            out Quaternion studioRotation)
        {
            studioPosition = studioCenter + studioBasis * paneLocalPosition;

            Vector3 forward = paneLocalForward;
            forward.y = 0.0f;
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.forward;

            studioRotation = studioBasis * Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        /// <summary>
        /// Pick the horizontal face of the mirror block the viewer is looking at: the
        /// axis-aligned XZ normal with the largest projection toward the viewer whose
        /// neighbouring cell is open. Returns false when the viewer is above/below or all
        /// horizontal neighbours are blocked (the mirror stays dark).
        /// </summary>
        public static bool TryChooseVisibleFace(
            Vector3 blockCenter,
            Vector3 viewerPosition,
            System.Func<Vector3Int, bool> isFaceOpen,
            out Vector3Int faceNormal)
        {
            Vector3 toViewer = viewerPosition - blockCenter;
            Vector3Int[] candidates =
            {
                new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1)
            };

            float bestDot = 0.0f;
            faceNormal = default;
            bool found = false;
            foreach (Vector3Int candidate in candidates)
            {
                float dot = toViewer.x * candidate.x + toViewer.z * candidate.z;
                if (dot <= bestDot || !isFaceOpen(candidate))
                    continue;

                bestDot = dot;
                faceNormal = candidate;
                found = true;
            }

            return found;
        }
    }
}
