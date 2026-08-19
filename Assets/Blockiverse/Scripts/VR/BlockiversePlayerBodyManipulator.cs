using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace Blockiverse.VR
{
    // XRI's stock CharacterControllerBodyManipulator rewrites the collision capsule to the
    // tracked camera height on every move (CharacterControllerBodyManipulator.MoveBody), so the
    // player's collision volume became a property of whoever was wearing the headset: a tall
    // player got a tall capsule and could not fit through two-block openings, and crouch could
    // never shrink the capsule because the next move overwrote it.
    //
    // In Blockiverse the player's size is a game rule (voxel_survival_ruleset): two blocks tall
    // standing, one block tall crouched, regardless of real-world height. This manipulator keeps
    // XRI's capsule-follows-the-body behavior for position but owns the height.
    public sealed class BlockiversePlayerBodyManipulator : CharacterControllerBodyManipulator
    {
        // Slightly under two blocks so the capsule clears a two-block opening with the skin
        // width and floating-point slack the CharacterController needs.
        public const float DefaultStandingCapsuleHeight = 1.8f;

        // Slightly under one block so crouching clears a one-block opening.
        public const float DefaultCrouchCapsuleHeight = 0.9f;

        // Guards against a degenerate capsule if tracking reports a nonsense camera height.
        public const float MinTrackedCapsuleHeight = 0.4f;
        public const float MaxTrackedCapsuleHeight = 2.4f;

        [SerializeField] float standingCapsuleHeight = DefaultStandingCapsuleHeight;
        [SerializeField] float crouchCapsuleHeight = DefaultCrouchCapsuleHeight;

        public bool Crouching { get; set; }

        // False (default): every player is the same size in the world. True: the capsule
        // follows the player's real tracked height, so tall players must duck or crouch where
        // shorter players walk through.
        public bool UseRealPlayerHeight { get; set; }

        // Latest tracked camera height above the body's ground position, refreshed on each move.
        public float TrackedCapsuleHeight { get; private set; } = DefaultStandingCapsuleHeight;

        public float StandingCapsuleHeightMeters => standingCapsuleHeight;
        public float CrouchCapsuleHeightMeters => crouchCapsuleHeight;

        // The capsule height the player currently occupies.
        public float CapsuleHeight => ResolveCapsuleHeight(
            UseRealPlayerHeight, Crouching, standingCapsuleHeight, crouchCapsuleHeight, TrackedCapsuleHeight);

        public void Configure(float standingHeight, float crouchHeight)
        {
            standingCapsuleHeight = Mathf.Max(0.1f, standingHeight);
            crouchCapsuleHeight = Mathf.Clamp(crouchHeight, 0.1f, standingCapsuleHeight);
        }

        public static float ResolveCapsuleHeight(
            bool useRealPlayerHeight,
            bool crouching,
            float standingHeight,
            float crouchHeight,
            float trackedHeight)
        {
            float clampedCrouch = Mathf.Min(crouchHeight, standingHeight);

            if (!useRealPlayerHeight)
                return Mathf.Max(0.1f, crouching ? clampedCrouch : standingHeight);

            // Real-height mode: the player's own height defines the capsule. Physically ducking
            // shrinks it through the tracked height; the crouch toggle still offers a
            // crouch without kneeling, and never makes the player taller than they are.
            float tracked = Mathf.Clamp(trackedHeight, MinTrackedCapsuleHeight, MaxTrackedCapsuleHeight);
            return Mathf.Max(0.1f, crouching ? Mathf.Min(clampedCrouch, tracked) : tracked);
        }

        // Mirrors CharacterControllerBodyManipulator.MoveBody, except the capsule height comes
        // from the game's player size instead of the tracked camera height.
        public override CollisionFlags MoveBody(Vector3 motion)
        {
            if (linkedBody == null || characterController == null)
                return CollisionFlags.None;

            Vector3 bodyGroundPosition = linkedBody.GetBodyGroundLocalPosition();
            TrackedCapsuleHeight = linkedBody.xrOrigin.CameraInOriginSpaceHeight - bodyGroundPosition.y;
            float capsuleHeight = CapsuleHeight;
            characterController.height = capsuleHeight;
            characterController.center = new Vector3(
                bodyGroundPosition.x,
                bodyGroundPosition.y + capsuleHeight * 0.5f + characterController.skinWidth,
                bodyGroundPosition.z);

            // Avoid "CharacterController.Move called on inactive controller".
            if (characterController.enabled)
                return characterController.Move(motion);

            linkedBody.originTransform.position += motion;
            return CollisionFlags.None;
        }
    }
}
