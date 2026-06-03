namespace Blockiverse.VR
{
    public enum BlockiverseLocomotionMode
    {
        /// <summary>Left thumbstick glides/walks; right thumbstick turns; right A jumps.</summary>
        Glide,

        /// <summary>
        /// Pushing either thumbstick forward shows that controller's arched teleport pointer.
        /// Releasing teleports to the landing marker. Glide movement is disabled.
        /// </summary>
        Teleport,
    }
}
