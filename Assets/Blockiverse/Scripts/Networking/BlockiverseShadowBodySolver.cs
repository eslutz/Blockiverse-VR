using UnityEngine;

namespace Blockiverse.Networking
{
    /// <summary>
    /// One articulated block-body part: a box, in rig-local space.
    /// </summary>
    public readonly struct ShadowBodyPart
    {
        public readonly Vector3 LocalPosition;
        public readonly Quaternion LocalRotation;
        public readonly Vector3 LocalScale;

        public ShadowBodyPart(Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            LocalScale = localScale;
        }
    }

    /// <summary>
    /// The full eight-part block body: head, torso, two arms, two hands, two legs.
    /// </summary>
    public readonly struct ShadowBodyLayout
    {
        public readonly ShadowBodyPart Head;
        public readonly ShadowBodyPart Torso;
        public readonly ShadowBodyPart LeftArm;
        public readonly ShadowBodyPart RightArm;
        public readonly ShadowBodyPart LeftHand;
        public readonly ShadowBodyPart RightHand;
        public readonly ShadowBodyPart LeftLeg;
        public readonly ShadowBodyPart RightLeg;

        public ShadowBodyLayout(
            ShadowBodyPart head,
            ShadowBodyPart torso,
            ShadowBodyPart leftArm,
            ShadowBodyPart rightArm,
            ShadowBodyPart leftHand,
            ShadowBodyPart rightHand,
            ShadowBodyPart leftLeg,
            ShadowBodyPart rightLeg)
        {
            Head = head;
            Torso = torso;
            LeftArm = leftArm;
            RightArm = rightArm;
            LeftHand = leftHand;
            RightHand = rightHand;
            LeftLeg = leftLeg;
            RightLeg = rightLeg;
        }
    }

    /// <summary>
    /// Builds a full block body — legs, torso, arms, hands, head — from the only three poses VR
    /// tracking actually provides. The result exists purely to CAST A SHADOW (Eric's design,
    /// 2026-08-24): with a Meta avatar you see the avatar and a block-body shadow that follows
    /// your movements; with the proxy avatar you still see only block hands, but the shadow is
    /// the same full block body.
    /// </summary>
    /// <remarks>
    /// Why a dedicated body instead of the avatar itself casting: Meta's avatar shaders declare
    /// no ShadowCaster pass (see BlockiverseMetaAvatarEntity.ApplyShadowCasting), and the avatar
    /// is compute-skinned from external buffers, so no stock shadow pass can reproduce its pose.
    /// The tracked head and hands are the truth both bodies are driven from, so a block body laid
    /// out from them is the one silhouette that is honest in both modes.
    ///
    /// Everything is rig-local; the rig root is the floor-level tracking origin, so the floor is
    /// y = 0 and roomscale movement arrives through the head pose — which is exactly the movement
    /// the previous single-capsule caster lost by being pinned to the origin.
    ///
    /// Pure math, no scene access: the EditMode tests drive it with plain poses. Legs are rigid
    /// columns from hip to floor — no gait swing. If a walking swing is ever wanted,
    /// BlockiverseGaitCycle already owns the phase to drive it from.
    /// </remarks>
    public static class BlockiverseShadowBodySolver
    {
        // Head box matches the proxy's visible head so the two modes throw the same silhouette.
        public static readonly Vector3 HeadScale = new(0.28f, 0.24f, 0.28f);
        public static readonly Vector3 HandScale = new(0.16f, 0.16f, 0.16f);

        public const float HeadHalfHeightMeters = 0.12f;
        public const float NeckGapMeters = 0.02f;

        // Hip sits at this fraction of neck height, so crouching compresses torso and legs
        // together instead of driving either through the floor.
        public const float HipHeightFraction = 0.52f;

        public const float TorsoWidthMeters = 0.38f;
        public const float TorsoDepthMeters = 0.22f;
        public const float LegThicknessMeters = 0.16f;
        public const float LegSeparationMeters = 0.10f;

        // Eric's report (2026-08-24): the arm shadow reads as a thin line, not a rectangular arm.
        // The original 0.12 was thinner than even the hand box (0.16) it connects to -- an arm
        // narrower than the hand at its end looks wrong on its own -- and 0.12 m against the
        // Quest main-light shadowmap (1024 texels over a 30 m shadow distance, one cascade, HARD
        // shadows -- BlockiverseAndroidURPAsset.asset: m_MainLightShadowmapResolution 1024,
        // m_ShadowDistance 30, m_ShadowCascadeCount 1, m_SoftShadowsSupported 0) is only ~2.9 cm
        // per texel, i.e. about 4 texels across -- right at the edge of resolving as a filled
        // rectangle rather than a hairline, and shadow depth/normal bias erodes a box that thin
        // further still. 0.20 is thicker than the hand it meets and reads clearly at that
        // resolution (~7 texels), while staying under the torso's 0.22 depth so the torso remains
        // the widest part of the silhouette.
        public const float ArmThicknessMeters = 0.20f;
        public const float ShoulderDropMeters = 0.06f;
        public const float ShoulderHalfSpanMeters = TorsoWidthMeters * 0.5f + 0.02f;

        // A head at ankle height still yields a finite, if squat, body.
        public const float MinimumHeadHeightMeters = 0.35f;
        public const float MinimumSegmentMeters = 0.05f;

        public static ShadowBodyLayout Solve(Pose head, Pose leftHand, Pose rightHand)
        {
            float headY = Mathf.Max(head.position.y, MinimumHeadHeightMeters);
            Vector3 headXZ = new(head.position.x, 0.0f, head.position.z);

            Quaternion yaw = Quaternion.LookRotation(ResolveFacing(head.rotation), Vector3.up);

            float neckY = Mathf.Max(headY - HeadHalfHeightMeters - NeckGapMeters, MinimumSegmentMeters * 2.0f);
            float hipY = Mathf.Max(neckY * HipHeightFraction, MinimumSegmentMeters);
            float torsoHeight = Mathf.Max(neckY - hipY, MinimumSegmentMeters);
            float legHeight = Mathf.Max(hipY, MinimumSegmentMeters);

            ShadowBodyPart torso = new(
                headXZ + new Vector3(0.0f, (neckY + hipY) * 0.5f, 0.0f),
                yaw,
                new Vector3(TorsoWidthMeters, torsoHeight, TorsoDepthMeters));

            ShadowBodyPart leftLeg = BuildLeg(headXZ, yaw, hipY, legHeight, -LegSeparationMeters);
            ShadowBodyPart rightLeg = BuildLeg(headXZ, yaw, hipY, legHeight, LegSeparationMeters);

            Vector3 leftShoulder = headXZ + new Vector3(0.0f, neckY - ShoulderDropMeters, 0.0f)
                + yaw * new Vector3(-ShoulderHalfSpanMeters, 0.0f, 0.0f);
            Vector3 rightShoulder = headXZ + new Vector3(0.0f, neckY - ShoulderDropMeters, 0.0f)
                + yaw * new Vector3(ShoulderHalfSpanMeters, 0.0f, 0.0f);

            return new ShadowBodyLayout(
                head: new ShadowBodyPart(head.position, head.rotation, HeadScale),
                torso: torso,
                leftArm: BuildArm(leftShoulder, leftHand.position),
                rightArm: BuildArm(rightShoulder, rightHand.position),
                leftHand: new ShadowBodyPart(leftHand.position, leftHand.rotation, HandScale),
                rightHand: new ShadowBodyPart(rightHand.position, rightHand.rotation, HandScale),
                leftLeg: leftLeg,
                rightLeg: rightLeg);
        }

        static ShadowBodyPart BuildLeg(Vector3 headXZ, Quaternion yaw, float hipY, float legHeight, float sideOffset)
        {
            Vector3 centre = headXZ
                + new Vector3(0.0f, hipY - legHeight * 0.5f, 0.0f)
                + yaw * new Vector3(sideOffset, 0.0f, 0.0f);
            return new ShadowBodyPart(centre, yaw, new Vector3(LegThicknessMeters, legHeight, LegThicknessMeters));
        }

        static ShadowBodyPart BuildArm(Vector3 shoulder, Vector3 hand)
        {
            Vector3 span = hand - shoulder;
            float length = span.magnitude;

            // A hand resting exactly at the shoulder must not produce a zero-scale box (its
            // inverse transforms go NaN) or an undefined direction.
            Quaternion rotation = length > 1e-4f
                ? Quaternion.FromToRotation(Vector3.up, span / length)
                : Quaternion.identity;
            length = Mathf.Max(length, MinimumSegmentMeters);

            return new ShadowBodyPart(
                (shoulder + hand) * 0.5f,
                rotation,
                new Vector3(ArmThicknessMeters, length, ArmThicknessMeters));
        }

        // The body's facing. Head forward flattened to the ground plane; when the player looks
        // straight down or up that projection vanishes, and the head's UP vector (projected) is
        // what points the way the face points — negated when looking up, where up tilts backward.
        static Vector3 ResolveFacing(Quaternion headRotation)
        {
            Vector3 forward = headRotation * Vector3.forward;
            Vector3 flat = new(forward.x, 0.0f, forward.z);

            if (flat.sqrMagnitude >= 1e-4f)
                return flat.normalized;

            Vector3 up = headRotation * Vector3.up;
            Vector3 flatUp = new(up.x, 0.0f, up.z);

            if (flatUp.sqrMagnitude >= 1e-4f)
                return (forward.y < 0.0f ? flatUp : -flatUp).normalized;

            return Vector3.forward;
        }
    }
}
