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
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        [SerializeField, Range(1, 32)] int poolSize = 16;
        [SerializeField] Material particleMaterial;
        [SerializeField] Sprite blockDustParticle;
        [SerializeField] Sprite blockPuffParticle;
        [SerializeField] Sprite resourceSparkParticle;
        [SerializeField] Sprite craftSparkParticle;
        [SerializeField] Sprite rainSplashParticle;
        [SerializeField] Sprite snowflakeParticle;
        [SerializeField] Sprite fogWispParticle;
        [SerializeField] Sprite emberParticle;

        readonly List<ParticleSystem> systems = new();
        readonly ParticleSystem.Burst[] burstScratch = new ParticleSystem.Burst[1];
        readonly MaterialPropertyBlock propertyBlock = new();
        int nextIndex;

        public int PrewarmedCount => systems.Count;
        public int PlayCount { get; private set; }
        public float LastIntensity { get; private set; }
        public Material ParticleMaterial => particleMaterial;

        public void ConfigureForTests(int poolSize)
        {
            this.poolSize = Mathf.Clamp(poolSize, 1, 32);
            Prewarm();
        }

        public void ConfigureParticleMaterial(Material material)
        {
            particleMaterial = material;
            foreach (ParticleSystem system in systems)
                ApplyParticleMaterial(system);
        }

        public void ConfigureParticleSprites(
            Sprite blockDust,
            Sprite blockPuff,
            Sprite resourceSpark,
            Sprite craftSpark,
            Sprite rainSplash,
            Sprite snowflake,
            Sprite fogWisp,
            Sprite ember)
        {
            blockDustParticle = blockDust;
            blockPuffParticle = blockPuff;
            resourceSparkParticle = resourceSpark;
            craftSparkParticle = craftSpark;
            rainSplashParticle = rainSplash;
            snowflakeParticle = snowflake;
            fogWispParticle = fogWisp;
            emberParticle = ember;
        }

        public bool HasSpriteForCue(BlockiverseVfxCue cue) => SpriteForCue(cue) != null;

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
            ApplyParticleMaterial(system);
            particleObject.SetActive(true);
            return system;
        }

        void ConfigureParticleSystem(ParticleSystem system, BlockiverseVfxCue cue, Color tint, float intensity)
        {
            ApplyParticleMaterial(system);
            ApplyParticleSprite(system, SpriteForCue(cue));

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
            burstScratch[0] = new ParticleSystem.Burst(0f, (short)main.maxParticles);
            emission.SetBursts(burstScratch);
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

        void ApplyParticleMaterial(ParticleSystem system)
        {
            if (system == null)
                return;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (particleMaterial != null)
                renderer.sharedMaterial = particleMaterial;
            else if (renderer.sharedMaterial == null)
                renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        void ApplyParticleSprite(ParticleSystem system, Sprite sprite)
        {
            if (system == null || sprite == null || sprite.texture == null)
                return;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            propertyBlock.Clear();
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture(BaseMapId, sprite.texture);
            propertyBlock.SetTexture(MainTexId, sprite.texture);
            renderer.SetPropertyBlock(propertyBlock);
        }

        Sprite SpriteForCue(BlockiverseVfxCue cue)
        {
            return cue switch
            {
                BlockiverseVfxCue.BlockBreakDust or BlockiverseVfxCue.BlockChipBurst => blockDustParticle,
                BlockiverseVfxCue.BlockPlacePuff or BlockiverseVfxCue.CraftFailPuff or
                    BlockiverseVfxCue.TeleportArrivalPuff => blockPuffParticle,
                BlockiverseVfxCue.ResourceSpark => resourceSparkParticle,
                BlockiverseVfxCue.CraftSuccessSpark or BlockiverseVfxCue.InventoryPanelPulse or
                    BlockiverseVfxCue.SelectionPulse => craftSparkParticle,
                BlockiverseVfxCue.RainSplash => rainSplashParticle,
                BlockiverseVfxCue.SnowflakeDrift => snowflakeParticle,
                BlockiverseVfxCue.FogWisp or BlockiverseVfxCue.LightningFlash => fogWispParticle,
                BlockiverseVfxCue.TorchSpark or BlockiverseVfxCue.CampfireEmber => emberParticle,
                _ => null
            };
        }
    }
}
