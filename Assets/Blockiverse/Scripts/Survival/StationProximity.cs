using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    // The set of crafting stations within reach of a player, packed as a bitmask. A recipe with
    // RequiredStation == None is always craftable, so Contains(None) is always true.
    public readonly struct CraftingStationSet : System.IEquatable<CraftingStationSet>
    {
        readonly int mask;

        CraftingStationSet(int mask) => this.mask = mask;

        public bool Equals(CraftingStationSet other) => mask == other.mask;

        public override bool Equals(object obj) => obj is CraftingStationSet other && Equals(other);

        public override int GetHashCode() => mask;

        public static CraftingStationSet None => default;

        public static CraftingStationSet Of(CraftingStation station) =>
            station == CraftingStation.None ? None : new CraftingStationSet(1 << (int)station);

        public CraftingStationSet With(CraftingStation station) =>
            station == CraftingStation.None ? this : new CraftingStationSet(mask | (1 << (int)station));

        public bool Contains(CraftingStation station) =>
            station == CraftingStation.None || (mask & (1 << (int)station)) != 0;

        public bool IsEmpty => mask == 0;
    }

    // Maps placed station blocks to the CraftingStation they provide and scans the world around a
    // player for stations in reach (voxel_survival_ruleset §8: crafting requires proximity to the
    // station). Used by the crafting UI to gate recipes and by the host to validate craft requests.
    public static class StationProximity
    {
        // Station interaction reach in blocks (cube radius around the player's feet/head position).
        public const int DefaultRadius = 4;

        static readonly Dictionary<BlockId, CraftingStation> StationForBlock = new()
        {
            { BlockRegistry.BuildTable,   CraftingStation.BuildTable },
            { BlockRegistry.ClayKiln,     CraftingStation.ClayKiln },
            { BlockRegistry.BellowsForge, CraftingStation.BellowsForge },
            { BlockRegistry.PrepBoard,    CraftingStation.PrepBoard },
            { BlockRegistry.MendBench,    CraftingStation.MendBench },
            { BlockRegistry.Campfire,     CraftingStation.Campfire },
        };

        public static bool TryGetStationForBlock(BlockId block, out CraftingStation station) =>
            StationForBlock.TryGetValue(block, out station);

        // Scans the cube of `radius` blocks around `center` and returns every station found.
        public static CraftingStationSet ScanNearby(VoxelWorld world, BlockPosition center, int radius = DefaultRadius)
        {
            CraftingStationSet found = CraftingStationSet.None;
            if (world == null || radius < 0)
                return found;

            WorldBounds bounds = world.Bounds;
            int minX = System.Math.Max(0, center.X - radius);
            int maxX = System.Math.Min(bounds.Width - 1, center.X + radius);
            int minY = System.Math.Max(0, center.Y - radius);
            int maxY = System.Math.Min(bounds.Height - 1, center.Y + radius);
            int minZ = System.Math.Max(0, center.Z - radius);
            int maxZ = System.Math.Min(bounds.Depth - 1, center.Z + radius);

            for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                if (StationForBlock.TryGetValue(world.GetBlock(new BlockPosition(x, y, z)), out CraftingStation station))
                    found = found.With(station);
            }

            return found;
        }

        // True when `claimed` is None or actually present near `center` — the host-side check for
        // client craft/repair requests so a client cannot claim a station it is not standing at.
        public static bool ValidateClaim(VoxelWorld world, BlockPosition center, CraftingStation claimed, int radius = DefaultRadius)
        {
            if (claimed == CraftingStation.None)
                return true;
            return ScanNearby(world, center, radius).Contains(claimed);
        }
    }
}
