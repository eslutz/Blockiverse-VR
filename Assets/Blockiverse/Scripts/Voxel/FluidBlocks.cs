namespace Blockiverse.Voxel
{
    public enum FluidFamily
    {
        Freshwater = 0,
        Brine = 1,
        Emberflow = 2,
    }

    // Shared fluid classification: maps source/flow block ids onto their family and exposes the
    // canonical §5.4 flow behavior (horizontal distance + tick cadence) per family. Sources are
    // permanent world blocks; flow cells are spread and retracted by the flow simulation.
    public static class FluidBlocks
    {
        public const int FamilyCount = 3;

        // §5.4 fluid behavior table.
        static readonly int[] FlowDistanceByFamily = { 8, 6, 4 };
        static readonly int[] TickCadenceByFamily = { 5, 6, 12 };

        public static BlockId SourceOf(FluidFamily family) => family switch
        {
            FluidFamily.Freshwater => BlockRegistry.Freshwater,
            FluidFamily.Brine => BlockRegistry.Brine,
            _ => BlockRegistry.Emberflow,
        };

        public static BlockId FlowOf(FluidFamily family) => family switch
        {
            FluidFamily.Freshwater => BlockRegistry.FreshwaterFlow,
            FluidFamily.Brine => BlockRegistry.BrineFlow,
            _ => BlockRegistry.EmberflowFlow,
        };

        public static int FlowDistance(FluidFamily family) => FlowDistanceByFamily[(int)family];

        public static int TickCadence(FluidFamily family) => TickCadenceByFamily[(int)family];

        public static bool IsSource(BlockId id) =>
            id == BlockRegistry.Freshwater || id == BlockRegistry.Brine || id == BlockRegistry.Emberflow;

        public static bool IsFlow(BlockId id) =>
            id == BlockRegistry.FreshwaterFlow || id == BlockRegistry.BrineFlow || id == BlockRegistry.EmberflowFlow;

        public static bool IsFluid(BlockId id) => IsSource(id) || IsFlow(id);

        public static bool TryGetFamily(BlockId id, out FluidFamily family)
        {
            if (id == BlockRegistry.Freshwater || id == BlockRegistry.FreshwaterFlow)
            {
                family = FluidFamily.Freshwater;
                return true;
            }

            if (id == BlockRegistry.Brine || id == BlockRegistry.BrineFlow)
            {
                family = FluidFamily.Brine;
                return true;
            }

            if (id == BlockRegistry.Emberflow || id == BlockRegistry.EmberflowFlow)
            {
                family = FluidFamily.Emberflow;
                return true;
            }

            family = default;
            return false;
        }

        // Drinkable / irrigating water (source or flowing freshwater).
        public static bool IsFreshwater(BlockId id) =>
            id == BlockRegistry.Freshwater || id == BlockRegistry.FreshwaterFlow;
    }
}
