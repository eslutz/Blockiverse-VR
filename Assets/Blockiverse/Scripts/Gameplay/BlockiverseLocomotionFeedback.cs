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
        float peakFallSpeed;
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
            if (!subscribed)
                return;

            // Clear the flag even when the gait is gone (destroyed components read as null), or a
            // later Configure with a fresh gait would see subscribed still true and silently never
            // resubscribe.
            subscribed = false;

            if (gaitCycle != null)
                gaitCycle.Footfall -= OnFootfall;
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

            // Track the fastest downward speed of the whole fall rather than the previous frame's
            // sample: the impact frame's delta is truncated to the remaining gap above the ground,
            // and the grounded flag arrives with a frame of lag that depends on unspecified script
            // order — a single-frame sample misses real landings on both counts.
            if (!grounded && verticalSpeed < 0f)
                peakFallSpeed = Mathf.Max(peakFallSpeed, -verticalSpeed);

            // Landing: grounded after airborne with a meaningful fall behind it. The gait
            // suppression gate keeps creative flight's ground-skimming and menu-focused states
            // silent; footstep cues need no equivalent check because a suppressed gait raises no
            // Footfall events at all.
            if (grounded && !wasGrounded && !gaitCycle.IsSuppressed && peakFallSpeed > LandingMinFallSpeed)
                audioCuePlayer?.PlayCue(BlockiverseAudioCue.Footstep);

            if (grounded)
                peakFallSpeed = 0f;

            wasGrounded = grounded;
        }
    }
}
