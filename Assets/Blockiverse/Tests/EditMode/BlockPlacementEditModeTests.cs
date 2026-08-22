using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // Pins what a placed block may overwrite. Both the local creative path and the
    // host-authoritative survival path read this one predicate; two copies would drift, and a
    // client that thought a cell was placeable while the host did not produces a rejected mutation
    // and a visible rubber-band.
    public sealed class BlockPlacementEditModeTests
    {
        [Test]
        public void AirIsReplaceable()
        {
            Assert.That(BlockPlacement.IsReplaceable(BlockRegistry.Air), Is.True);
        }

        [Test]
        public void EveryFluidIsReplaceable()
        {
            // The bug this fixes: placement required Air, so a lake read as solid to the builder --
            // no pier footings, no damming a channel, no filling a pool. The creative ruleset
            // (section 8.3) specifies replace-placement over fluids.
            foreach (BlockId fluid in new[]
                     {
                         BlockRegistry.Freshwater,
                         BlockRegistry.FreshwaterFlow,
                         BlockRegistry.Brine,
                         BlockRegistry.BrineFlow,
                         BlockRegistry.Emberflow,
                         BlockRegistry.EmberflowFlow,
                     })
            {
                Assert.That(FluidBlocks.IsFluid(fluid), Is.True, "Fixture guard.");
                Assert.That(BlockPlacement.IsReplaceable(fluid), Is.True, $"{fluid} should be replaceable.");
            }
        }

        [Test]
        public void SolidBlocksAreNotReplaceable()
        {
            foreach (BlockId solid in new[]
                     {
                         BlockRegistry.Graystone,
                         BlockRegistry.MeadowTurf,
                         BlockRegistry.Snowpack,
                         BlockRegistry.BranchwoodLog,
                     })
            {
                Assert.That(BlockPlacement.IsReplaceable(solid), Is.False, $"{solid} must not be overwritten.");
            }
        }

        [Test]
        public void ReplaceabilityAgreesWithTheFluidPredicateAcrossTheRegistry()
        {
            // Swept rather than spot-checked, so a fluid added later cannot quietly become
            // unplaceable-into on one path and placeable on the other.
            BlockRegistry registry = BlockRegistry.CreateDefault();

            foreach (BlockDefinition definition in registry.CachedDefinitions)
            {
                // The cache is indexed by BlockId, so unused ids are null holes.
                if (definition == null)
                    continue;

                bool expected = definition.Id == BlockRegistry.Air || FluidBlocks.IsFluid(definition.Id);
                Assert.That(
                    BlockPlacement.IsReplaceable(definition.Id),
                    Is.EqualTo(expected),
                    $"{definition.CanonicalId} disagrees with the fluid predicate.");
            }
        }
    }
}
