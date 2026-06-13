using System.IO;
using Blockiverse.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseVfxCuePlayerEditModeTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null)
                Object.DestroyImmediate(root);
        }

        [Test]
        public void VfxPoolPrewarmsAndReusesOneShotParticleSystems()
        {
            root = new GameObject("VFX Root");
            BlockiverseVfxPool pool = root.AddComponent<BlockiverseVfxPool>();
            pool.ConfigureForTests(poolSize: 2);

            Assert.That(pool.PrewarmedCount, Is.EqualTo(2));

            pool.Play(BlockiverseVfxCue.BlockBreakDust, Vector3.zero, Color.white, 1.0f);
            pool.Play(BlockiverseVfxCue.BlockPlacePuff, Vector3.one, Color.gray, 1.0f);
            pool.Play(BlockiverseVfxCue.ResourceSpark, Vector3.up, Color.cyan, 1.0f);

            Assert.That(pool.PrewarmedCount, Is.EqualTo(2));
            Assert.That(pool.PlayCount, Is.EqualTo(3));
        }

        [Test]
        public void VfxCuePlayerUsesReducedParticleSetting()
        {
            root = new GameObject("VFX Root");
            BlockiverseFeedbackSettings settings = root.AddComponent<BlockiverseFeedbackSettings>();
            BlockiverseVfxPool pool = root.AddComponent<BlockiverseVfxPool>();
            BlockiverseVfxCuePlayer player = root.AddComponent<BlockiverseVfxCuePlayer>();

            pool.ConfigureForTests(poolSize: 4);
            settings.ReducedParticles = true;
            player.Configure(pool, settings);

            player.PlayCue(BlockiverseVfxCue.BlockBreakDust, Vector3.zero);

            Assert.That(pool.LastIntensity, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void VfxCuePlayerSuppressesLightningWhenReducedFlashIsEnabled()
        {
            root = new GameObject("VFX Root");
            BlockiverseFeedbackSettings settings = root.AddComponent<BlockiverseFeedbackSettings>();
            BlockiverseVfxPool pool = root.AddComponent<BlockiverseVfxPool>();
            BlockiverseVfxCuePlayer player = root.AddComponent<BlockiverseVfxCuePlayer>();

            pool.ConfigureForTests(poolSize: 2);
            settings.ReducedFlash = true;
            player.Configure(pool, settings);

            player.PlayCue(BlockiverseVfxCue.LightningFlash, Vector3.zero);

            Assert.That(pool.PlayCount, Is.Zero, "Reduced Flash should suppress lightning VFX rather than attenuate it.");

            player.PlayCue(BlockiverseVfxCue.RainSplash, Vector3.zero);

            Assert.That(pool.PlayCount, Is.EqualTo(1), "Reduced Flash should not suppress non-flash weather VFX.");
        }

        [Test]
        public void VfxPoolMapsGeneratedSpritesToRuntimeCues()
        {
            root = new GameObject("VFX Root");
            BlockiverseVfxPool pool = root.AddComponent<BlockiverseVfxPool>();

            Sprite blockDust = CreateSprite();
            Sprite blockPuff = CreateSprite();
            Sprite resourceSpark = CreateSprite();
            Sprite craftSpark = CreateSprite();
            Sprite rainSplash = CreateSprite();
            Sprite snowflake = CreateSprite();
            Sprite fogWisp = CreateSprite();
            Sprite ember = CreateSprite();

            pool.ConfigureParticleSprites(
                blockDust,
                blockPuff,
                resourceSpark,
                craftSpark,
                rainSplash,
                snowflake,
                fogWisp,
                ember);

            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.BlockBreakDust), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.BlockPlacePuff), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.ResourceSpark), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.CraftSuccessSpark), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.RainSplash), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.SnowflakeDrift), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.FogWisp), Is.True);
            Assert.That(pool.HasSpriteForCue(BlockiverseVfxCue.CampfireEmber), Is.True);
        }

        [Test]
        public void VfxPoolReusesPerPlayParticleConfigurationScratch()
        {
            string source = File.ReadAllText("Assets/Blockiverse/Scripts/Gameplay/BlockiverseVfxPool.cs");

            Assert.That(source, Does.Contain("readonly ParticleSystem.Burst[] burstScratch"));
            Assert.That(source, Does.Contain("readonly MaterialPropertyBlock propertyBlock"));
            Assert.That(source, Does.Not.Contain("SetBursts(new[]"));
            Assert.That(source, Does.Not.Contain("new MaterialPropertyBlock()"));
        }

        static Sprite CreateSprite()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            return Sprite.Create(texture, new Rect(0.0f, 0.0f, 2.0f, 2.0f), new Vector2(0.5f, 0.5f));
        }
    }
}
