using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Glide-locomotion footsteps and landing feedback: stride-timed footstep cues while the
    // character controller moves on the ground, and a landing cue when it touches down after a
    // fall. Lives on the XR rig next to the CharacterController.
    [DisallowMultipleComponent]
    public sealed class BlockiverseLocomotionFeedback : MonoBehaviour
    {
        // One footstep per stride length of horizontal travel (~walking cadence at glide speed).
        public const float StrideMeters = 1.8f;
        // Falls shorter than this land silently (stepping off a single block stays quiet).
        public const float LandingMinFallSpeed = 3.0f;

        [SerializeField] CharacterController characterController;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;

        Vector3 lastPosition;
        float strideAccumulator;
        bool wasGrounded;
        float lastVerticalSpeed;

        public void Configure(CharacterController controller, BlockiverseAudioCuePlayer cuePlayer)
        {
            characterController = controller;
            audioCuePlayer = cuePlayer;
        }

        void OnEnable()
        {
            ResolveReferences();
            lastPosition = transform.position;
            wasGrounded = characterController != null && characterController.isGrounded;
        }

        void ResolveReferences()
        {
            if (characterController == null)
                characterController = GetComponent<CharacterController>() ?? GetComponentInParent<CharacterController>();

            if (audioCuePlayer == null)
                audioCuePlayer = GetComponent<BlockiverseAudioCuePlayer>() ?? FindFirstObjectByType<BlockiverseAudioCuePlayer>();
        }

        void Update()
        {
            if (characterController == null)
                return;

            Vector3 position = transform.position;
            Vector3 delta = position - lastPosition;
            lastPosition = position;

            bool grounded = characterController.isGrounded;
            float verticalSpeed = Time.deltaTime > 0f ? delta.y / Time.deltaTime : 0f;

            // Landing: grounded after airborne with meaningful downward speed last frame.
            if (grounded && !wasGrounded && lastVerticalSpeed < -LandingMinFallSpeed)
            {
                audioCuePlayer?.PlayCue(BlockiverseAudioCue.Footstep);
                strideAccumulator = 0f;
            }

            // Footsteps: accumulate horizontal travel while grounded; teleports (large frame
            // jumps) reset instead of machine-gunning steps.
            if (grounded)
            {
                float horizontal = new Vector2(delta.x, delta.z).magnitude;
                if (horizontal > 2.0f)
                {
                    strideAccumulator = 0f;
                }
                else if (horizontal > 0.001f)
                {
                    strideAccumulator += horizontal;
                    if (strideAccumulator >= StrideMeters)
                    {
                        strideAccumulator -= StrideMeters;
                        audioCuePlayer?.PlayCue(BlockiverseAudioCue.Footstep);
                    }
                }
            }
            else
            {
                strideAccumulator = 0f;
            }

            wasGrounded = grounded;
            lastVerticalSpeed = verticalSpeed;
        }
    }
}
