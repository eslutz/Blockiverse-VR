using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Glide-locomotion footsteps and landing feedback. Footstep cues come from the shared
    // BlockiverseGaitCycle so they land on the same walk cycle the camera bob is drawn from; the
    // landing cue is this component's own, fired when the rig touches down after a fall. Lives on
    // the XR rig next to the CharacterController.
    [DisallowMultipleComponent]
    public sealed class BlockiverseLocomotionFeedback : MonoBehaviour
    {
        // Falls shorter than this land silently (stepping off a single block stays quiet).
        public const float LandingMinFallSpeed = 3.0f;

        [SerializeField] CharacterController characterController;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseGaitCycle gaitCycle;

        Vector3 lastPosition;
        bool wasGrounded;
        float lastVerticalSpeed;
        bool subscribed;

        public void Configure(CharacterController controller, BlockiverseAudioCuePlayer cuePlayer, BlockiverseGaitCycle gait = null)
        {
            Unsubscribe();
            characterController = controller;
            audioCuePlayer = cuePlayer;

            if (gait != null)
                gaitCycle = gait;

            Subscribe();
        }

        void OnEnable()
        {
            ResolveReferences();
            lastPosition = transform.position;
            wasGrounded = gaitCycle != null && gaitCycle.IsGrounded;
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void ResolveReferences()
        {
            if (characterController == null)
                characterController = GetComponent<CharacterController>() ?? GetComponentInParent<CharacterController>();

            if (audioCuePlayer == null)
                audioCuePlayer = GetComponent<BlockiverseAudioCuePlayer>() ?? FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            if (gaitCycle == null)
                gaitCycle = GetComponent<BlockiverseGaitCycle>() ?? GetComponentInParent<BlockiverseGaitCycle>();

            // Rigs generated before the gait cycle existed still need one; the bootstrapper puts it
            // on the prefab, this only covers a stale prefab at runtime.
            if (gaitCycle == null && Application.isPlaying)
            {
                gaitCycle = gameObject.AddComponent<BlockiverseGaitCycle>();
                gaitCycle.Configure(characterController);
            }
        }

        void Subscribe()
        {
            if (subscribed || gaitCycle == null)
                return;

            gaitCycle.Footfall += OnFootfall;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || gaitCycle == null)
                return;

            gaitCycle.Footfall -= OnFootfall;
            subscribed = false;
        }

        void OnFootfall()
        {
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.Footstep);
        }

        void Update()
        {
            if (gaitCycle == null)
                return;

            Vector3 position = transform.position;
            Vector3 delta = position - lastPosition;
            lastPosition = position;

            bool grounded = gaitCycle.IsGrounded;
            float verticalSpeed = Time.deltaTime > 0f ? delta.y / Time.deltaTime : 0f;

            // Landing: grounded after airborne with meaningful downward speed last frame.
            if (grounded && !wasGrounded && lastVerticalSpeed < -LandingMinFallSpeed)
                audioCuePlayer?.PlayCue(BlockiverseAudioCue.Footstep);

            wasGrounded = grounded;
            lastVerticalSpeed = verticalSpeed;
        }
    }
}
