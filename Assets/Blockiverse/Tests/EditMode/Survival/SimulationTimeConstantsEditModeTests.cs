using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.Survival.EditMode
{
    public sealed class SimulationTimeConstantsEditModeTests
    {
        [Test]
        public void PublicTimeConstantsUseTheSharedSimulationClock()
        {
            Assert.That(WorldConstants.TicksPerSecond, Is.EqualTo(SimulationTime.TicksPerSecond));
            Assert.That(SmeltingModel.TicksPerSecond, Is.EqualTo(SimulationTime.TicksPerSecond));
            Assert.That(MiningFormula.TicksPerSecond, Is.EqualTo(SimulationTime.TicksPerSecond));
            Assert.That(WorldConstants.TicksPerDay, Is.EqualTo(SimulationTime.TicksPerDay));
            Assert.That(FarmingService.TicksPerGameDay, Is.EqualTo(SimulationTime.TicksPerDay));
        }
    }
}
