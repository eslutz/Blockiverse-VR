using System;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Blockiverse.Tests.EditMode
{
    public sealed class EnvironmentDynamicsEditModeTests
    {
        // ── Biome-less fixture world ──────────────────────────────────────────
        // A flat creative world has no biomes, so every column resolves to the temperate
        // (Meadow) base of 18 °C. Altitude is then the only thing that can freeze it, which is
        // exactly the "cold place under a rain sky" case: at y = 210 the lapse rate gives
        // 18 - 0.15 × (210 - 64) = -3.9 °C. The fixture is deliberately taller than the shipped
        // 128-block world so that arithmetic is reachable without depending on generated terrain;
        // in shipped worlds the rain-to-snow conversion band is real but thin — tundra at any
        // altitude, plus high pinewild/highlands ground at night inside the 0-48 relief band.
        const int BiomelessWidth = 16;
        const int BiomelessHeight = 224;
        const int BiomelessDepth = 16;
        const int BiomelessGroundHeight = 4;
        const int FreezingSurfaceY = 210;
        const int FreezingColumnX = 8;
        const int FreezingColumnZ = 8;
        // The flat ground surface (y = GroundHeight - 1) sits far below sea level: no altitude
        // cooling at all, so this column stays at the full 18 °C base.
        const int WarmColumnX = 12;
        const int WarmColumnZ = 4;
        const int WarmSurfaceY = BiomelessGroundHeight - 1;

        // ── Biome fixture world ───────────────────────────────────────────────
        const int BiomeWidth = 64;
        const int BiomeHeight = 32;
        const int BiomeDepth = 64;
        const int BiomeGroundHeight = 4;
        const int BiomeSurfaceY = BiomeGroundHeight - 1;
        // Columns are searched from a margin so every candidate clears the spawn exclusion
        // radius around the fixture's corner spawn.
        const int BiomeScanMargin = 16;
        const int BiomeScanStep = 8;
        const int BiomeScanSeedLimit = 512;

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
        public void InherentlySnowyStatesAreTheThreeSnowStatesOnly()
        {
            Assert.That(EnvironmentDynamicsController.IsInherentlySnowyState(WeatherState.LightSnow), Is.True);
            Assert.That(EnvironmentDynamicsController.IsInherentlySnowyState(WeatherState.HeavySnow), Is.True);
            Assert.That(EnvironmentDynamicsController.IsInherentlySnowyState(WeatherState.Blizzard), Is.True);

            Assert.That(EnvironmentDynamicsController.IsInherentlySnowyState(WeatherState.Clear), Is.False);
            Assert.That(EnvironmentDynamicsController.IsInherentlySnowyState(WeatherState.Fog), Is.False);

            // Rain states are not INHERENTLY snowy, but they still fall as snow where the column
            // is at or below freezing — which is why this predicate is not the accumulation gate.
            Assert.That(EnvironmentDynamicsController.IsInherentlySnowyState(WeatherState.Thunderstorm), Is.False);
            Assert.That(EnvironmentDynamicsController.IsInherentlySnowyState(WeatherState.HeavyRain), Is.False);
            Assert.That(
                WeatherService.ResolvePrecipitationKind(WeatherState.Thunderstorm, -1f),
                Is.EqualTo(PrecipitationKind.Snow),
                "A thunderstorm over freezing ground falls as snow, so accumulation cannot be gated on the snow states alone.");
        }

        [Test]
        public void AccumulationSamplingIsGatedOnSomethingFalling()
        {
            // TickDynamics opens the per-column sampling loop on this predicate, so it must let
            // every precipitating state through (rain included) and cost nothing under a dry sky.
            Assert.That(WeatherService.IsPrecipitating(WeatherState.LightRain), Is.True);
            Assert.That(WeatherService.IsPrecipitating(WeatherState.HeavyRain), Is.True);
            Assert.That(WeatherService.IsPrecipitating(WeatherState.Thunderstorm), Is.True);
            Assert.That(WeatherService.IsPrecipitating(WeatherState.LightSnow), Is.True);
            Assert.That(WeatherService.IsPrecipitating(WeatherState.HeavySnow), Is.True);
            Assert.That(WeatherService.IsPrecipitating(WeatherState.Blizzard), Is.True);

            Assert.That(WeatherService.IsPrecipitating(WeatherState.Clear), Is.False,
                "A clear sky must not cost a temperature evaluation per sampled column.");
            Assert.That(WeatherService.IsPrecipitating(WeatherState.PartlyCloudy), Is.False);
            Assert.That(WeatherService.IsPrecipitating(WeatherState.Overcast), Is.False);
            Assert.That(WeatherService.IsPrecipitating(WeatherState.Fog), Is.False);
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

        // The headline case the global weather-state gate used to miss: a rain sky over ground
        // that is itself below freezing. The precipitation model already reported snow there;
        // now the world shows it.
        [Test]
        public void FreezingNonTundraColumnUnderARainStateAccumulatesSnowpack()
        {
            using (var fixture = new SnowAccumulationFixture(BiomelessSettings(), CreativeWorldGenerationPreset.FlatCreative))
            {
                fixture.RaiseColumnTo(FreezingColumnX, FreezingColumnZ, FreezingSurfaceY);
                fixture.Manager.SetWeather(WeatherState.HeavyRain);

                var surface = new BlockPosition(FreezingColumnX, FreezingSurfaceY, FreezingColumnZ);
                Assert.That(fixture.Manager.TryEvaluateEnvironment(surface, out EnvironmentState environment), Is.True);
                Assert.That(environment.Precipitation, Is.EqualTo(PrecipitationKind.Snow),
                    "The column's own altitude puts it below freezing, so the rain state falls there as snow.");
                // Published temperature (pre-modifier -3.9 °C plus the -2.8 °C snow modifier at
                // heavy-rain intensity 0.7 = -6.7 °C) is what the §12 settle rule reads; it must
                // sit at or below freezing for the accumulation assert below to be the rule's
                // success case rather than a coincidence.
                Assert.That(environment.Temperature, Is.LessThanOrEqualTo(WeatherService.FreezingTemperatureC));

                Assert.That(fixture.Dynamics.TryAccumulateSnowAt(fixture.World, FreezingColumnX, FreezingColumnZ), Is.True,
                    "Snow that falls on a column must settle there, whatever the sky-wide weather state is called.");
                Assert.That(
                    fixture.World.GetBlock(new BlockPosition(FreezingColumnX, FreezingSurfaceY + 1, FreezingColumnZ)),
                    Is.EqualTo(BlockRegistry.Snowpack),
                    "The layer settles on the block above the column's surface.");
            }
        }

        [Test]
        public void WarmColumnUnderTheSameRainStateStaysBare()
        {
            using (var fixture = new SnowAccumulationFixture(BiomelessSettings(), CreativeWorldGenerationPreset.FlatCreative))
            {
                fixture.Manager.SetWeather(WeatherState.HeavyRain);

                var surface = new BlockPosition(WarmColumnX, WarmSurfaceY, WarmColumnZ);
                Assert.That(fixture.Manager.TryEvaluateEnvironment(surface, out EnvironmentState environment), Is.True);
                Assert.That(environment.Precipitation, Is.EqualTo(PrecipitationKind.Rain),
                    "A temperate column well above freezing gets rain, not snow.");

                Assert.That(fixture.Dynamics.TryAccumulateSnowAt(fixture.World, WarmColumnX, WarmColumnZ), Is.False,
                    "Rain must not lay down snowpack.");
                Assert.That(
                    fixture.World.GetBlock(new BlockPosition(WarmColumnX, WarmSurfaceY + 1, WarmColumnZ)),
                    Is.EqualTo(BlockRegistry.Air),
                    "Nothing may be written to a column that is only being rained on.");
            }
        }

        // Regression guard for the behaviour the old tundra-only gate provided.
        [Test]
        public void TundraColumnUnderASnowStateStillAccumulates()
        {
            Assert.That(
                TryFindBiomeColumn(SurvivalBiomeResolver.TundraBiomeIndex, out int seed, out int x, out int z),
                Is.True,
                "The deterministic biome resolver should classify some column of some seed as tundra.");

            using (var fixture = new SnowAccumulationFixture(BiomeSettings(seed), CreativeWorldGenerationPreset.SurvivalLite))
            {
                fixture.Manager.SetWeather(WeatherState.LightSnow);

                Assert.That(fixture.Dynamics.TryAccumulateSnowAt(fixture.World, x, z), Is.True,
                    "Snowfall on tundra must still build snowpack after the per-column routing change.");
                Assert.That(
                    fixture.World.GetBlock(new BlockPosition(x, BiomeSurfaceY + 1, z)),
                    Is.EqualTo(BlockRegistry.Snowpack));
            }
        }

        // §6.3 and §12 compose because falling is not settling: the three snow states fall as
        // snow everywhere, but a layer only STICKS where the local temperature is at or below
        // freezing. The dunes base is 34 °C and even a full blizzard's -4 °C modifier leaves the
        // column at 30 °C, far above freezing, so the player sees snowfall (Precipitation ==
        // Snow) over ground that stays bare. The rain check first proves the temperature is
        // actually being consulted rather than the biome name.
        [Test]
        public void HotBiomeColumnUnderABlizzardShowsSnowfallButStaysBare()
        {
            Assert.That(
                TryFindBiomeColumn(SurvivalBiomeResolver.DunesBiomeIndex, out int seed, out int x, out int z),
                Is.True,
                "The deterministic biome resolver should classify some column of some seed as dunes.");

            using (var fixture = new SnowAccumulationFixture(BiomeSettings(seed), CreativeWorldGenerationPreset.SurvivalLite))
            {
                fixture.Manager.SetWeather(WeatherState.HeavyRain);
                Assert.That(fixture.Dynamics.TryAccumulateSnowAt(fixture.World, x, z), Is.False,
                    "Rain over the dunes stays rain: the biome base is far above freezing.");

                fixture.Manager.SetWeather(WeatherState.Blizzard);

                var surface = new BlockPosition(x, BiomeSurfaceY, z);
                Assert.That(fixture.Manager.TryEvaluateEnvironment(surface, out EnvironmentState environment), Is.True);
                Assert.That(environment.Precipitation, Is.EqualTo(PrecipitationKind.Snow),
                    "A blizzard falls as snow regardless of local temperature (§6.3) — snow VFX drift over the dunes.");
                Assert.That(environment.Temperature, Is.GreaterThan(WeatherService.FreezingTemperatureC),
                    "Fixture guard: dunes under a full blizzard sit at 34 - 4 = 30 °C, nowhere near the settle threshold.");

                Assert.That(fixture.Dynamics.TryAccumulateSnowAt(fixture.World, x, z), Is.False,
                    "Falling is not settling (§12): snow only accumulates where the local temperature is at or below freezing.");
                Assert.That(
                    fixture.World.GetBlock(new BlockPosition(x, BiomeSurfaceY + 1, z)),
                    Is.EqualTo(BlockRegistry.Air),
                    "The dune column stays bare under the blizzard.");
            }
        }

        [Test]
        public void ClearWeatherAccumulatesNothingEvenOnFreezingGround()
        {
            using (var fixture = new SnowAccumulationFixture(BiomelessSettings(), CreativeWorldGenerationPreset.FlatCreative))
            {
                fixture.RaiseColumnTo(FreezingColumnX, FreezingColumnZ, FreezingSurfaceY);
                fixture.Manager.SetWeather(WeatherState.Clear);

                Assert.That(fixture.Dynamics.TryAccumulateSnowAt(fixture.World, FreezingColumnX, FreezingColumnZ), Is.False,
                    "Cold alone is not snowfall: nothing is falling under a clear sky.");
                Assert.That(
                    fixture.World.GetBlock(new BlockPosition(FreezingColumnX, FreezingSurfaceY + 1, FreezingColumnZ)),
                    Is.EqualTo(BlockRegistry.Air));
            }
        }

        // ── Lightning ring fixture ───────────────────────────────────────────
        // Wide enough that a 96-block ring centred on the middle stays in bounds, and flat, so
        // every candidate column has a surface to strike and the only rejection left is the
        // exclusion rule under test.
        const int RingWidth = 256;
        const int RingHeight = 24;
        const int RingDepth = 256;
        const int RingGroundHeight = 4;
        const int RingAnchorX = 128;
        const int RingAnchorZ = 128;

        [Test]
        public void StrikesLandInTheRingAroundTheAnchor()
        {
            using (var fixture = new SnowAccumulationFixture(RingSettings(), CreativeWorldGenerationPreset.FlatCreative))
            {
                var struck = new System.Collections.Generic.List<BlockPosition>();
                fixture.Dynamics.LightningStruck += struck.Add;

                for (int i = 0; i < 200; i++)
                    fixture.Dynamics.TryStrikeNearAnchor(fixture.World, RingAnchorX, RingAnchorZ);

                Assert.That(struck.Count, Is.GreaterThan(190),
                    "Retrying candidates should recover almost every check rather than wasting the interval.");

                foreach (BlockPosition strike in struck)
                {
                    double distance = Math.Sqrt(
                        (strike.X - (double)RingAnchorX) * (strike.X - (double)RingAnchorX) +
                        (strike.Z - (double)RingAnchorZ) * (strike.Z - (double)RingAnchorZ));

                    Assert.That(distance, Is.GreaterThan(EnvironmentDynamicsController.StrikePlayerExclusionRadius),
                        "A strike must never land inside the player comfort exclusion.");
                    Assert.That(distance, Is.LessThan(LightningStrikeSelector.MaxRingRadius + 1.0));
                }
            }
        }

        [Test]
        public void StrikesNeverLandInsideTheSpawnExclusion()
        {
            using (var fixture = new SnowAccumulationFixture(RingSettings(), CreativeWorldGenerationPreset.FlatCreative))
            {
                var struck = new System.Collections.Generic.List<BlockPosition>();
                fixture.Dynamics.LightningStruck += struck.Add;

                // Anchored right on spawn, so the spawn exclusion is the rule actually being
                // exercised rather than something the ring geometry avoids for free.
                BlockPosition spawn = RingSettings().SpawnPosition;
                for (int i = 0; i < 200; i++)
                    fixture.Dynamics.TryStrikeNearAnchor(fixture.World, spawn.X, spawn.Z);

                Assert.That(struck, Is.Not.Empty, "Fixture guard: some strike must land to make this meaningful.");

                foreach (BlockPosition strike in struck)
                {
                    Assert.That(
                        LightningStrikeSelector.IsInsideExclusion(
                            strike.X, strike.Z, spawn.X, spawn.Z,
                            EnvironmentDynamicsController.StrikeSpawnExclusionRadius),
                        Is.False);
                }
            }
        }

        [Test]
        public void AStruckMeadowTurfColumnStillScorches()
        {
            using (var fixture = new SnowAccumulationFixture(RingSettings(), CreativeWorldGenerationPreset.FlatCreative))
            {
                // The scorch path is unchanged by ring selection, but it runs through the same
                // TryApplyLightningStrike the selector now calls repeatedly -- worth holding.
                var surface = new BlockPosition(RingAnchorX + 30, RingGroundHeight - 1, RingAnchorZ);
                fixture.World.SetBlock(surface, BlockRegistry.MeadowTurf, trackChange: false);

                Assert.That(fixture.Dynamics.TryApplyLightningStrike(fixture.World, surface.X, surface.Z), Is.True);
                Assert.That(fixture.World.GetBlock(surface), Is.EqualTo(BlockRegistry.DryTurf));
            }
        }

        static WorldGenerationSettings RingSettings() =>
            new(
                width: RingWidth,
                height: RingHeight,
                depth: RingDepth,
                chunkSize: 16,
                seed: 90210,
                groundHeight: RingGroundHeight,
                spawnPosition: new BlockPosition(16, RingGroundHeight + 1, 16));

        static WorldGenerationSettings BiomelessSettings() =>
            new(
                width: BiomelessWidth,
                height: BiomelessHeight,
                depth: BiomelessDepth,
                chunkSize: 16,
                seed: 4242,
                groundHeight: BiomelessGroundHeight,
                // Corner spawn: every column the tests touch clears the spawn exclusion radius.
                spawnPosition: new BlockPosition(0, BiomelessGroundHeight + 1, 0));

        static WorldGenerationSettings BiomeSettings(int seed) =>
            new(
                width: BiomeWidth,
                height: BiomeHeight,
                depth: BiomeDepth,
                chunkSize: 16,
                seed: seed,
                groundHeight: BiomeGroundHeight,
                spawnPosition: new BlockPosition(0, BiomeGroundHeight + 1, 0));

        // Finds a (seed, column) pair the deterministic biome resolver classifies as `biomeIndex`,
        // building the resolver exactly the way CreativeWorldManager builds its own for a survival
        // world (world seed + world height). Searching seeds keeps the test off any one seed's
        // terrain while staying completely deterministic.
        static bool TryFindBiomeColumn(int biomeIndex, out int seed, out int x, out int z)
        {
            for (int candidateSeed = 1; candidateSeed <= BiomeScanSeedLimit; candidateSeed++)
            {
                var resolver = new SurvivalBiomeResolver(candidateSeed, BiomeHeight);
                for (int columnX = BiomeScanMargin; columnX < BiomeWidth; columnX += BiomeScanStep)
                {
                    for (int columnZ = BiomeScanMargin; columnZ < BiomeDepth; columnZ += BiomeScanStep)
                    {
                        if (resolver.BiomeIndexAt(columnX, columnZ) != biomeIndex)
                            continue;

                        seed = candidateSeed;
                        x = columnX;
                        z = columnZ;
                        return true;
                    }
                }
            }

            seed = 0;
            x = 0;
            z = 0;
            return false;
        }

        // The smallest world that can answer a snow-accumulation question: a scene clock (so the
        // manager builds its weather service), a generated world, a chunk-authority sync left on
        // its default host boundary (no NetworkManager, so this peer owns mutations and broadcasts
        // nothing), and the dynamics controller wired to both.
        sealed class SnowAccumulationFixture : IDisposable
        {
            readonly GameObject clockObject;
            readonly GameObject worldObject;
            readonly GameObject authorityObject;
            readonly GameObject dynamicsObject;
            readonly Material blockMaterial;
            readonly Texture2D atlasTexture;

            public SnowAccumulationFixture(WorldGenerationSettings settings, CreativeWorldGenerationPreset preset)
            {
                clockObject = new GameObject("Snow Accumulation World Time Clock");
                WorldTimeClock clock = clockObject.AddComponent<WorldTimeClock>();
                // Midday: IsNight is false, so no night modifier perturbs the column temperatures.
                clock.Configure(dayLengthSeconds: 1200.0f, startNormalizedTime: 0.25f, timeScale: 1.0f);

                worldObject = new GameObject("Snow Accumulation Creative World");
                worldObject.SetActive(false);
                Manager = worldObject.AddComponent<CreativeWorldManager>();

                atlasTexture = new Texture2D(
                    BlockVisualAtlas.AtlasWidthPixels,
                    BlockVisualAtlas.AtlasHeightPixels,
                    TextureFormat.RGBA32,
                    mipChain: false)
                {
                    name = BlockVisualAtlas.AuthoredAtlasName
                };
                blockMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = atlasTexture };
                Manager.Configure(blockMaterial, -1);

                BlockRegistry registry = BlockRegistry.CreateDefault();
                World = new FlatBuilderPreset(registry, settings).Generate();
                Manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, World, preset));

                authorityObject = new GameObject("Snow Accumulation Chunk Authority");
                authorityObject.SetActive(false);
                MultiplayerChunkAuthoritySync authoritySync = authorityObject.AddComponent<MultiplayerChunkAuthoritySync>();
                authoritySync.Configure(null, Manager);

                dynamicsObject = new GameObject("Snow Accumulation Environment Dynamics");
                dynamicsObject.SetActive(false);
                Dynamics = dynamicsObject.AddComponent<EnvironmentDynamicsController>();
                Dynamics.Configure(Manager, authoritySync);
            }

            public CreativeWorldManager Manager { get; }
            public EnvironmentDynamicsController Dynamics { get; }
            public VoxelWorld World { get; }

            // Puts the column's topmost block at `surfaceY` so the accumulation path reads that
            // altitude — the fixture stand-in for a peak.
            public void RaiseColumnTo(int x, int z, int surfaceY)
            {
                World.SetBlock(new BlockPosition(x, surfaceY, z), BlockRegistry.Graystone, trackChange: false);
                Assert.That(EnvironmentDynamicsController.FindTopBlockY(World, x, z), Is.EqualTo(surfaceY),
                    "The raised block must be the column's top for the altitude to be the one under test.");
            }

            public void Dispose()
            {
                if (dynamicsObject != null)
                    Object.DestroyImmediate(dynamicsObject);
                if (authorityObject != null)
                    Object.DestroyImmediate(authorityObject);
                if (worldObject != null)
                    Object.DestroyImmediate(worldObject);
                if (clockObject != null)
                    Object.DestroyImmediate(clockObject);
                if (blockMaterial != null)
                    Object.DestroyImmediate(blockMaterial);
                if (atlasTexture != null)
                    Object.DestroyImmediate(atlasTexture);

                GameObject sunObject = GameObject.Find(BlockiverseLightingRuntime.SunObjectName);
                if (sunObject != null)
                    Object.DestroyImmediate(sunObject);
            }
        }
    }
}
