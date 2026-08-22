using System.Collections.Generic;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    /// <summary>
    /// Material-aware block audio, per-surface footsteps, and the Classic Block
    /// Sounds setting (voxel_audio_vfx_ruleset.md §5, §13).
    /// </summary>
    public sealed class BlockiverseMaterialAudioEditModeTests
    {
        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        // ── Family classification ───────────────────────────────────────────

        [TestCase("meadow_turf", BlockiverseMaterialFamily.Soil)]
        [TestCase("loose_loam", BlockiverseMaterialFamily.Soil)]
        [TestCase("graystone", BlockiverseMaterialFamily.Stone)]
        [TestCase("dark_slate", BlockiverseMaterialFamily.Stone)]
        [TestCase("pale_sand", BlockiverseMaterialFamily.GravelSand)]
        [TestCase("shingle_gravel", BlockiverseMaterialFamily.GravelSand)]
        [TestCase("branchwood_log", BlockiverseMaterialFamily.Wood)]
        [TestCase("work_plank", BlockiverseMaterialFamily.Wood)]
        [TestCase("storage_crate", BlockiverseMaterialFamily.Wood)]
        [TestCase("leafmoss", BlockiverseMaterialFamily.Leaf)]
        [TestCase("reedgrass", BlockiverseMaterialFamily.Leaf)]
        [TestCase("clearpane_glass", BlockiverseMaterialFamily.Glass)]
        [TestCase("lumen_quartz_cluster", BlockiverseMaterialFamily.Crystal)]
        [TestCase("staropal_geode", BlockiverseMaterialFamily.Crystal)]
        [TestCase("embercoal_seam", BlockiverseMaterialFamily.OreMetal)]
        [TestCase("rustcore_ore", BlockiverseMaterialFamily.OreMetal)]
        [TestCase("snowpack", BlockiverseMaterialFamily.Snow)]
        [TestCase("snowcap_turf", BlockiverseMaterialFamily.Snow)]
        public void CanonicalBlockIdsMapToTheirAcousticFamily(string canonicalId,
                                                              BlockiverseMaterialFamily expected)
        {
            Assert.That(BlockRegistry.Default.TryGetByCanonicalId(canonicalId, out BlockDefinition definition),
                Is.True, $"{canonicalId} should be registered.");
            Assert.That(BlockiverseBlockFeedbackCues.FamilyForBlock(BlockRegistry.Default, definition.Id),
                Is.EqualTo(expected));
        }

        [Test]
        public void EveryRegisteredSolidBlockResolvesToAFamily()
        {
            // The table is keyed on canonical IDs, so a newly registered block that
            // nobody remembered to map still has to land somewhere sensible rather
            // than throwing or going silent.
            // CachedDefinitions is a fixed-size lookup array, so unused slots are null.
            foreach (BlockDefinition definition in BlockRegistry.Default.CachedDefinitions)
            {
                if (definition == null || definition.Id == BlockRegistry.Air || FluidBlocks.IsFluid(definition.Id))
                    continue;

                BlockiverseMaterialFamily family =
                    BlockiverseBlockFeedbackCues.FamilyForBlock(BlockRegistry.Default, definition.Id);
                Assert.That(System.Enum.IsDefined(typeof(BlockiverseMaterialFamily), family), Is.True,
                    $"{definition.CanonicalId} resolved to an undefined family.");
            }
        }

        [Test]
        public void UnmappedBlocksFallBackWithoutThrowing()
        {
            // BlockId values outside the registry happen in practice (a save from a
            // future registry, a corrupt chunk). Feedback must degrade, not crash.
            var unknown = new BlockId(30000);
            Assert.That(() => BlockiverseBlockFeedbackCues.FamilyForBlock(BlockRegistry.Default, unknown),
                Throws.Nothing);
            Assert.That(() => BlockiverseBlockFeedbackCues.SurfaceForBlock(BlockRegistry.Default, unknown),
                Throws.Nothing);
        }

        // ── Surface classification ──────────────────────────────────────────

        [TestCase("meadow_turf", BlockiverseSurfaceFamily.Soil)]
        [TestCase("graystone", BlockiverseSurfaceFamily.Stone)]
        [TestCase("shingle_gravel", BlockiverseSurfaceFamily.GravelSand)]
        [TestCase("work_plank", BlockiverseSurfaceFamily.Wood)]
        [TestCase("leafmoss", BlockiverseSurfaceFamily.Leaf)]
        [TestCase("snowpack", BlockiverseSurfaceFamily.Snow)]
        // Hard, non-porous materials are indistinguishable underfoot.
        [TestCase("clearpane_glass", BlockiverseSurfaceFamily.Stone)]
        [TestCase("rustcore_ore", BlockiverseSurfaceFamily.Stone)]
        public void BlocksMapToTheSurfaceTheySoundLikeUnderfoot(string canonicalId,
                                                                BlockiverseSurfaceFamily expected)
        {
            Assert.That(BlockRegistry.Default.TryGetByCanonicalId(canonicalId, out BlockDefinition definition), Is.True);
            Assert.That(BlockiverseBlockFeedbackCues.SurfaceForBlock(BlockRegistry.Default, definition.Id),
                Is.EqualTo(expected));
        }

        [Test]
        public void FluidsResolveToTheWaterSurface()
        {
            foreach (BlockId fluid in new[] { BlockRegistry.Freshwater, BlockRegistry.Brine, BlockRegistry.Emberflow })
            {
                Assert.That(BlockiverseBlockFeedbackCues.SurfaceForBlock(BlockRegistry.Default, fluid),
                    Is.EqualTo(BlockiverseSurfaceFamily.Water));
            }
        }

        // ── Clip resolution ─────────────────────────────────────────────────

        [Test]
        public void BreakCueUsesTheClipForTheBlocksMaterialFamily()
        {
            BlockiverseAudioCuePlayer player = CreateCuePlayer();
            player.ConfigureClip(BlockiverseAudioCue.BlockBreak, CreateClip("block_break"));
            player.ConfigureMaterialBanks(new[]
            {
                Bank(BlockiverseMaterialFamily.Stone, "block_break_stone", "block_place_stone"),
                Bank(BlockiverseMaterialFamily.Wood, "block_break_wood", "block_place_wood"),
            });

            Assert.That(player.ResolveMaterialClip(BlockiverseAudioCue.BlockBreak, BlockiverseMaterialFamily.Stone).name,
                Is.EqualTo("block_break_stone"));
            Assert.That(player.ResolveMaterialClip(BlockiverseAudioCue.BlockPlace, BlockiverseMaterialFamily.Wood).name,
                Is.EqualTo("block_place_wood"));
        }

        [Test]
        public void UnmappedFamilyFallsBackToTheGenericBlockCue()
        {
            BlockiverseAudioCuePlayer player = CreateCuePlayer();
            player.ConfigureClip(BlockiverseAudioCue.BlockBreak, CreateClip("block_break"));
            player.ConfigureMaterialBanks(new[]
            {
                Bank(BlockiverseMaterialFamily.Stone, "block_break_stone", "block_place_stone"),
            });

            Assert.That(player.ResolveMaterialClip(BlockiverseAudioCue.BlockBreak, BlockiverseMaterialFamily.Snow).name,
                Is.EqualTo("block_break"));
        }

        [Test]
        public void FootstepClipsRotateWithinTheirSurfaceBank()
        {
            BlockiverseAudioCuePlayer player = CreateCuePlayer();
            player.ConfigureFootstepBanks(new[]
            {
                new BlockiverseFootstepBank
                {
                    Surface = BlockiverseSurfaceFamily.Snow,
                    Clips = new[] { CreateClip("snow_a"), CreateClip("snow_b") },
                },
                new BlockiverseFootstepBank
                {
                    Surface = BlockiverseSurfaceFamily.Wood,
                    Clips = new[] { CreateClip("wood_a") },
                },
            });

            Assert.That(player.ResolveSurfaceFootstepClip(BlockiverseSurfaceFamily.Snow).name, Is.EqualTo("snow_a"));
            Assert.That(player.ResolveSurfaceFootstepClip(BlockiverseSurfaceFamily.Snow).name, Is.EqualTo("snow_b"));
            Assert.That(player.ResolveSurfaceFootstepClip(BlockiverseSurfaceFamily.Snow).name, Is.EqualTo("snow_a"));

            // Rotation is tracked per surface: stepping onto wood must not consume
            // snow's position in its own bank.
            Assert.That(player.ResolveSurfaceFootstepClip(BlockiverseSurfaceFamily.Wood).name, Is.EqualTo("wood_a"));
            Assert.That(player.ResolveSurfaceFootstepClip(BlockiverseSurfaceFamily.Snow).name, Is.EqualTo("snow_b"));
        }

        [Test]
        public void SurfaceWithNoBankResolvesToNullSoTheCallerCanFallBack()
        {
            BlockiverseAudioCuePlayer player = CreateCuePlayer();
            player.ConfigureFootstepBanks(new[]
            {
                new BlockiverseFootstepBank
                {
                    Surface = BlockiverseSurfaceFamily.Snow,
                    Clips = new[] { CreateClip("snow_a") },
                },
            });

            Assert.That(player.ResolveSurfaceFootstepClip(BlockiverseSurfaceFamily.Water), Is.Null);
        }

        // ── Classic Block Sounds ────────────────────────────────────────────

        [Test]
        public void ClassicBlockSoundsOverridesMaterialVariants()
        {
            BlockiverseAudioCuePlayer player = CreateCuePlayer();
            BlockiverseFeedbackSettings settings = player.gameObject.AddComponent<BlockiverseFeedbackSettings>();
            player.ConfigureFeedbackSettings(settings);
            player.ConfigureClip(BlockiverseAudioCue.BlockBreak, CreateClip("block_break"));
            player.ConfigureClip(BlockiverseAudioCue.BlockPlace, CreateClip("block_place"));
            player.ConfigureMaterialBanks(new[]
            {
                Bank(BlockiverseMaterialFamily.Stone, "block_break_stone", "block_place_stone"),
            });
            player.ConfigureClassicBlockClips(CreateClip("classic_block_break"), CreateClip("classic_block_place"));

            // Off: the material variant wins.
            settings.ClassicBlockSoundsEnabled = false;
            Assert.That(player.ResolveMaterialClip(BlockiverseAudioCue.BlockBreak, BlockiverseMaterialFamily.Stone).name,
                Is.EqualTo("block_break_stone"));

            // On: the original cue wins for every family, which is the whole point
            // of the setting — hearing the two sounds the game used to make.
            settings.ClassicBlockSoundsEnabled = true;
            Assert.That(player.ResolveMaterialClip(BlockiverseAudioCue.BlockBreak, BlockiverseMaterialFamily.Stone).name,
                Is.EqualTo("classic_block_break"));
            Assert.That(player.ResolveMaterialClip(BlockiverseAudioCue.BlockPlace, BlockiverseMaterialFamily.Stone).name,
                Is.EqualTo("classic_block_place"));
        }

        [Test]
        public void ClassicBlockSoundsLeavesEveryOtherCueAlone()
        {
            BlockiverseAudioCuePlayer player = CreateCuePlayer();
            BlockiverseFeedbackSettings settings = player.gameObject.AddComponent<BlockiverseFeedbackSettings>();
            player.ConfigureFeedbackSettings(settings);
            settings.ClassicBlockSoundsEnabled = true;

            player.ConfigureClip(BlockiverseAudioCue.UiConfirm, CreateClip("ui_confirm"));
            player.ConfigureClassicBlockClips(CreateClip("classic_block_break"), CreateClip("classic_block_place"));
            player.ConfigureFootstepBanks(new[]
            {
                new BlockiverseFootstepBank
                {
                    Surface = BlockiverseSurfaceFamily.Soil,
                    Clips = new[] { CreateClip("footstep_soil_01") },
                },
            });

            var played = new List<string>();
            player.CuePlayed += (_, clip) => played.Add(clip.name);
            player.PlayCue(BlockiverseAudioCue.UiConfirm);

            Assert.That(played, Is.EqualTo(new[] { "ui_confirm" }));
            Assert.That(player.ResolveSurfaceFootstepClip(BlockiverseSurfaceFamily.Soil).name,
                Is.EqualTo("footstep_soil_01"));
        }

        [Test]
        public void ClassicBlockSoundsDefaultsOff()
        {
            var gameObject = new GameObject("Feedback Settings");
            objectsToDestroy.Add(gameObject);
            BlockiverseFeedbackSettings settings = gameObject.AddComponent<BlockiverseFeedbackSettings>();

            Assert.That(settings.ClassicBlockSoundsEnabled, Is.False,
                "production audio should be the default; classic is opt-in.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        static BlockiverseMaterialBank Bank(BlockiverseMaterialFamily family, string breakName, string placeName)
        {
            return new BlockiverseMaterialBank
            {
                Family = family,
                BreakClip = CreateClip(breakName),
                PlaceClip = CreateClip(placeName),
            };
        }

        BlockiverseAudioCuePlayer CreateCuePlayer()
        {
            var gameObject = new GameObject("Audio Cue Player");
            objectsToDestroy.Add(gameObject);
            gameObject.AddComponent<AudioSource>();
            return gameObject.AddComponent<BlockiverseAudioCuePlayer>();
        }

        static AudioClip CreateClip(string name)
        {
            return AudioClip.Create(name, 16, 1, 44100, false);
        }
    }
}
