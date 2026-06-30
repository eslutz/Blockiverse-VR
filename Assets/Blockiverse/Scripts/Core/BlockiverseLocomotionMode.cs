namespace Blockiverse.Core
{
    public enum BlockiverseLocomotionMode
    {
        /// <summary>Support thumbstick glides/walks; dominant thumbstick turns; dominant primary button jumps.</summary>
        Glide,

        /// <summary>
        /// Pushing either thumbstick forward shows that controller's arched teleport pointer.
        /// Releasing teleports to the landing marker. Glide movement is disabled.
        /// </summary>
        Teleport,
    }

    public enum GlideStyle
    {
        /// <summary>Constant-height continuous movement (most comfortable).</summary>
        Smooth,
        /// <summary>Subtle vertical head-bob synced to glide speed for a sense of walking.</summary>
        Bobbing,
    }
}
