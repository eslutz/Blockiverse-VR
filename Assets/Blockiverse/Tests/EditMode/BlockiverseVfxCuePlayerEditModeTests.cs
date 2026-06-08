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
    }
}
