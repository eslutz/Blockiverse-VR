using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;

namespace Blockiverse.Gameplay
{
    /// <summary>
    /// Shared grounded signal for locomotion-driven feedback (walk bob, footsteps, fall damage).
    /// </summary>
    /// <remarks>
    /// <see cref="CharacterController.isGrounded"/> only reports true when the last
    /// <see cref="CharacterController.Move"/> ended in a downward collision. XRI's
    /// <see cref="GravityProvider"/> blocks gravity while the player is grounded, so walking across
    /// flat terrain queues horizontal-only motion and the flag reads false on most frames — which is
    /// why the bob and footsteps only appeared in random bursts as the terrain nudged the capsule
    /// down. The gravity provider sphere-casts every frame and is the stable signal, so prefer it
    /// and keep the controller flag as the fallback for rigs without one (tests, plain setups).
    /// </remarks>
    public sealed class BlockiverseGroundedProbe
    {
        CharacterController characterController;
        GravityProvider gravityProvider;

        public void Configure(CharacterController controller)
        {
            if (characterController == controller)
                return;

            characterController = controller;
            gravityProvider = null;
        }

        public bool IsGrounded
        {
            get
            {
                ResolveGravityProvider();

                if (gravityProvider != null && gravityProvider.isActiveAndEnabled)
                    return gravityProvider.isGrounded;

                return characterController != null && characterController.isGrounded;
            }
        }

        // Retries while unresolved instead of caching the first miss: the probe can be queried
        // before the rig's GravityProvider exists (runtime AddComponent fallbacks, staged setup),
        // and a cached miss would silently pin the flaky CharacterController fallback forever.
        // Rigs that genuinely lack a provider pay a per-query component lookup, which only test
        // rigs and bare setups ever do.
        void ResolveGravityProvider()
        {
            if (gravityProvider != null || characterController == null)
                return;

            gravityProvider = characterController.GetComponent<GravityProvider>();

            if (gravityProvider == null)
                gravityProvider = characterController.GetComponentInParent<GravityProvider>();
        }
    }
}
