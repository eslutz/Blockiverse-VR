namespace Blockiverse.Voxel
{
    /// <summary>
    /// Per-block state bits (save schema v1, vegetation ruleset §5).
    ///
    /// Stored as an int so the format has room to grow without another schema bump, but ONLY bits
    /// with a live consumer are declared here. Reserving named bits for features that do not exist
    /// yet produces exactly the trap this project already hit once with an unreachable render
    /// shape: a declaration that reads as supported and silently does nothing.
    ///
    /// The next bits this is expected to carry are `treeVariant` (which species' leaves/wood a
    /// block came from, so a pine forest and an oak meadow read differently rather than sharing one
    /// tile) and `axis` (log orientation, for horizontal beams). Neither is declared until it has
    /// art and a consumer.
    /// </summary>
    public static class BlockState
    {
        /// <summary>No state. What every block has unless something deliberately sets otherwise,
        /// and what every pre-v5 save loads as.</summary>
        public const int Default = 0;

        /// <summary>Player-placed: exempt from leaf decay.
        ///
        /// Leaf decay searches for a nearby log and removes leaves that have none. That is correct
        /// for a felled tree and wrong for a hedge someone built, which today rots for no reason
        /// the player can see. This bit is what separates the two.</summary>
        public const int Persistent = 1 << 0;

        public static bool IsPersistent(int state) => (state & Persistent) != 0;
    }
}
