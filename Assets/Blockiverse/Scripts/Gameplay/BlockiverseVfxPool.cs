using System.Collections.Generic;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public enum BlockiverseVfxCue
    {
        BlockBreakDust,
        BlockPlacePuff,
        BlockChipBurst,
        ResourceSpark,
        CraftSuccessSpark,
        CraftFailPuff,
        InventoryPanelPulse,
        SelectionPulse,
        TeleportArrivalPuff,
        RainSplash,
        SnowflakeDrift,
        FogWisp,
        LightningFlash,
        TorchSpark,
        CampfireEmber
    }

    [DisallowMultipleComponent]
    public sealed class BlockiverseVfxPool : MonoBehaviour
    {
        [SerializeField, Range(1, 32)] int poolSize = 16;
        [SerializeField] Material particleMaterial;

        readonly List<ParticleSystem> systems = new();
        int nextIndex;

        public int PrewarmedCount => systems.Count;
        public int PlayCount { get; private set; }
        public float LastIntensity { get; private set; }

        public void ConfigureForTests(int poolSize)
        {
            this.poolSize = Mathf.Clamp(poolSize, 1, 32);
            Prewarm();
        }

        public void Prewarm()
        {
            while (systems.Count < poolSize)
                systems.Add(CreateSystem(systems.Count));
        }

        public void Play(BlockiverseVfxCue cue, Vector3 position, Color tint, float intensity)
        {
            Prewarm();

            ParticleSystem system = systems[nextIndex];
            nextIndex = (nextIndex + 1) % systems.Count;
            LastIntensity = Mathf.Clamp01(intensity);
            PlayCount++;

            system.transform.position = position;
            ConfigureParticleSystem(system, cue, tint, LastIntensity);
            system.Play(withChildren: false);
        }

        void Awake()
        {
            Prewarm();
        }

        ParticleSystem CreateSystem(int index)
        {
            var particleObject = new GameObject($"VFX Particle {index + 1:00}");
            particleObject.transform.SetParent(transform, worldPositionStays: false);
            var system = particleObject.AddComponent<ParticleSystem>();
            var renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = particleMaterial != null
                ? particleMaterial
                : new Material(Shader.Find("Sprites/Default"));
            particleObject.SetActive(true);
            return system;
        }

        static void ConfigureParticleSystem(ParticleSystem system, BlockiverseVfxCue cue, Color tint, float intensity)
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = cue == BlockiverseVfxCue.CampfireEmber ? 0.6f : 0.35f;
            main.startSpeed = cue == BlockiverseVfxCue.BlockBreakDust ? 0.45f : 0.25f;
            main.startSize = cue == BlockiverseVfxCue.LightningFlash ? 0.08f : 0.045f;
            main.startColor = tint;
            main.maxParticles = Mathf.Max(1, Mathf.RoundToInt(DefaultParticleCount(cue) * Mathf.Clamp01(intensity)));

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)main.maxParticles)
            });
        }

        static int DefaultParticleCount(BlockiverseVfxCue cue)
        {
            return cue switch
            {
                BlockiverseVfxCue.BlockBreakDust => 12,
                BlockiverseVfxCue.BlockPlacePuff => 8,
                BlockiverseVfxCue.ResourceSpark => 8,
                BlockiverseVfxCue.CraftSuccessSpark => 10,
                BlockiverseVfxCue.CraftFailPuff => 5,
                BlockiverseVfxCue.TeleportArrivalPuff => 10,
                BlockiverseVfxCue.RainSplash => 2,
                BlockiverseVfxCue.TorchSpark => 3,
                BlockiverseVfxCue.CampfireEmber => 8,
                _ => 4
            };
        }
    }
}
