using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class EnvironmentDynamicsEditModeTests
    {
        [Test]
        public void ScorchRuleCharsTurfAndBurnsLeafmossOnly()
        {
            Assert.That(EnvironmentDynamicsController.TryGetScorchResult(BlockRegistry.MeadowTurf, out BlockId turfResult), Is.True);
            Assert.That(turfResult, Is.EqualTo(BlockRegistry.DryTurf));

            Assert.That(EnvironmentDynamicsController.TryGetScorchResult(BlockRegistry.Leafmoss, out BlockId leafResult), Is.True);
            Assert.That(leafResult, Is.EqualTo(BlockRegistry.Air));

            Assert.That(EnvironmentDynamicsController.TryGetScorchResult(BlockRegistry.Graystone, out _), Is.False);
            Assert.That(EnvironmentDynamicsController.TryGetScorchResult(BlockRegistry.SnowcapTurf, out _), Is.False);
        }

        [Test]
        public void SnowLayerRuleAllowsOneLayerAndNeverOnFluid()
        {
            Assert.That(EnvironmentDynamicsController.CanHoldSnowLayer(BlockRegistry.SnowcapTurf), Is.True);
            Assert.That(EnvironmentDynamicsController.CanHoldSnowLayer(BlockRegistry.Graystone), Is.True);
            Assert.That(EnvironmentDynamicsController.CanHoldSnowLayer(BlockRegistry.Leafmoss), Is.True);

            Assert.That(EnvironmentDynamicsController.CanHoldSnowLayer(BlockRegistry.Snowpack), Is.False, "Snow must not stack on snowpack.");
            Assert.That(EnvironmentDynamicsController.CanHoldSnowLayer(BlockRegistry.Freshwater), Is.False);
            Assert.That(EnvironmentDynamicsController.CanHoldSnowLayer(BlockRegistry.Brine), Is.False);
        }

        [Test]
        public void IsSnowingCoversTheThreeSnowStatesOnly()
        {
            Assert.That(EnvironmentDynamicsController.IsSnowing(WeatherState.LightSnow), Is.True);
            Assert.That(EnvironmentDynamicsController.IsSnowing(WeatherState.HeavySnow), Is.True);
            Assert.That(EnvironmentDynamicsController.IsSnowing(WeatherState.Blizzard), Is.True);

            Assert.That(EnvironmentDynamicsController.IsSnowing(WeatherState.Clear), Is.False);
            Assert.That(EnvironmentDynamicsController.IsSnowing(WeatherState.Thunderstorm), Is.False);
            Assert.That(EnvironmentDynamicsController.IsSnowing(WeatherState.HeavyRain), Is.False);
            Assert.That(EnvironmentDynamicsController.IsSnowing(WeatherState.Fog), Is.False);
        }

        [Test]
        public void FindTopBlockYReturnsTopmostNonAirCell()
        {
            var world = new VoxelWorld(new WorldBounds(8, 16, 8), chunkSize: 8, seed: 1);
            world.SetBlock(new BlockPosition(2, 3, 2), BlockRegistry.Graystone, trackChange: false);
            world.SetBlock(new BlockPosition(2, 7, 2), BlockRegistry.Snowpack, trackChange: false);

            Assert.That(EnvironmentDynamicsController.FindTopBlockY(world, 2, 2), Is.EqualTo(7));
            Assert.That(EnvironmentDynamicsController.FindTopBlockY(world, 3, 3), Is.EqualTo(-1), "Empty columns report -1.");
            Assert.That(EnvironmentDynamicsController.FindTopBlockY(world, -1, 0), Is.EqualTo(-1), "Out-of-range columns report -1.");
        }
    }
}
