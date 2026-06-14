namespace Blockiverse.VR
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
}
