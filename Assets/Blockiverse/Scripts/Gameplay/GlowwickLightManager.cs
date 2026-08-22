using System;
using System.Collections.Generic;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public sealed class GlowwickLightManager : MonoBehaviour
    {
        // Realtime punctual-light budget. Quest fills additional lights per object, so this caps
        // total scene cost; the slots are spent on the emitters NEAREST the viewer.
        public const int MaxRuntimePointLights = 24;

        // Only the single closest emitter casts shadows. Point-light shadows are cube maps — six
        // atlas slices each — and in the 1024 additional-light atlas both 6 slices (one caster) and
        // 12 slices (two casters) pack onto a 4x4 grid at 256 px per face. A second caster therefore
        // buys no resolution at all while costing another full six-slice shadow sweep.
        public const int MaxShadowCastingLights = 1;

        // The ruleset's block-light propagation (voxel_world_environment_effects.md §5.3) is
        // `next = light - 1 - attenuation`, so a level-L source reaches L blocks through open air.
        // One block is one Unity unit, so range in world units IS the emissive level.
        const float MinimumLightRange = 4.0f;

        // Peak intensity at the canonical maximum emissive level of 15 (spark_flare). Intensity is
        // proportional to level so the canonical ladder staropal 5 < lumen_quartz 7 < glowwick 9 <
        // emberflow 10 < campfire 12 < lumen_lamp 14 < spark_flare 15 reads as brightness.
        const float MaxEmitterIntensity = 2.5f;
        const float MaxEmissiveLevel = 15.0f;

        // Re-pick which emitters own the light slots once the viewer has moved this far, so
        // walking toward a distant torch hands it a slot before it comes into view.
        const float ViewerMoveReselectDistance = 4.0f;
        const float ReselectIntervalSeconds = 0.5f;

        readonly HashSet<BlockPosition> emitterPositions = new();
        readonly Dictionary<BlockPosition, Light> lightsByPosition = new();
        readonly List<BlockPosition> selectionScratch = new();
        readonly HashSet<BlockPosition> selectedScratch = new();
        readonly List<BlockPosition> staleScratch = new();

        VoxelWorld world;
        BlockRegistry blockRegistry;
        BlockiverseAudioCuePlayer audioCuePlayer;
        BlockiverseVfxCuePlayer vfxCuePlayer;
        Transform viewer;
        Vector3 lastSelectionViewerPosition;
        float nextReselectTime;
        bool slotsDirty;
        // Suppressed during the initial full-world rebuild so loading a save does not chorus
        // dozens of ignite cues at once; only live placements crackle.
        bool igniteFeedbackEnabled;

        public int ActiveEmitterCount => emitterPositions.Count;
        public int ActiveLightCount => lightsByPosition.Count;

        public static bool IsLightEmitter(BlockId block, BlockRegistry registry)
        {
            return registry != null && registry.TryGet(block, out BlockDefinition def) && def.EmissiveLight > 0;
        }

        // Shared with the mesh builder's line-of-sight bake (VoxelLightSampler.EmitterLightOffset)
        // so what the realtime light reaches and what the bake lets it reach always agree.
        public static Vector3 GetLightPosition(BlockPosition position)
        {
            return new Vector3(position.X, position.Y, position.Z) + VoxelLightSampler.EmitterLightOffset;
        }

        public static float GetLightRange(int emissiveLight) =>
            Mathf.Max(MinimumLightRange, emissiveLight);

        public static float GetLightIntensity(int emissiveLight) =>
            MaxEmitterIntensity * (emissiveLight / MaxEmissiveLevel);

        public void Configure(VoxelWorld voxelWorld, BlockRegistry blockRegistry)
        {
            if (world != null)
                world.BlockChanged -= OnBlockChanged;

            world = voxelWorld ?? throw new ArgumentNullException(nameof(voxelWorld));
            this.blockRegistry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            world.BlockChanged += OnBlockChanged;
            igniteFeedbackEnabled = false;
            RebuildAllLights();
            igniteFeedbackEnabled = true;
        }

        public bool TryGetLight(BlockPosition position, out Light light)
        {
            return lightsByPosition.TryGetValue(position, out light);
        }

        public bool IsTrackingEmitter(BlockPosition position)
        {
            return emitterPositions.Contains(position);
        }

        void RebuildAllLights()
        {
            ClearLights();

            for (int y = 0; y < world.Bounds.Height; y++)
            {
                for (int z = 0; z < world.Bounds.Depth; z++)
                {
                    for (int x = 0; x < world.Bounds.Width; x++)
                    {
                        var position = new BlockPosition(x, y, z);
                        if (IsLightEmitter(world.GetBlock(position), blockRegistry))
                            emitterPositions.Add(position);
                    }
                }
            }

            FillLightSlots();
        }

        void OnBlockChanged(BlockChange change)
        {
            if (IsLightEmitter(change.PreviousBlock, blockRegistry))
                RemoveLight(change.Position);

            if (IsLightEmitter(change.NewBlock, blockRegistry))
                AddLight(change.Position);
        }

        void AddLight(BlockPosition position)
        {
            if (!emitterPositions.Add(position))
                return;

            // Deliberately O(budget), never O(all emitters): Emberflow is a live fluid that fires
            // BlockChanged every simulation tick, so a full distance re-sort here would rank
            // thousands of lava cells 20x a second.
            if (lightsByPosition.Count < MaxRuntimePointLights)
            {
                Light created = CreateLight(position);
                if (created != null)
                    RefreshShadowCasters();
            }
            else if (TryFindFarthestLight(ResolveViewerPosition(), out BlockPosition farthest, out float farthestDistance))
            {
                // A freshly placed emitter beats a more distant one for the last slot; one that is
                // farther than everything already lit waits for the throttled reselect.
                float candidateDistance = (GetLightPosition(position) - ResolveViewerPosition()).sqrMagnitude;
                if (candidateDistance < farthestDistance)
                {
                    DestroyLight(farthest);
                    if (CreateLight(position) != null)
                        RefreshShadowCasters();
                }
            }

            PlayIgniteFeedback(position);
        }

        bool TryFindFarthestLight(Vector3 viewerPosition, out BlockPosition farthest, out float farthestDistance)
        {
            farthest = default;
            farthestDistance = -1f;

            foreach (BlockPosition position in lightsByPosition.Keys)
            {
                float distance = (GetLightPosition(position) - viewerPosition).sqrMagnitude;
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthest = position;
                }
            }

            return farthestDistance >= 0f;
        }

        void RefreshShadowCasters()
        {
            Vector3 viewerPosition = ResolveViewerPosition();
            foreach (KeyValuePair<BlockPosition, Light> entry in lightsByPosition)
            {
                if (entry.Value != null)
                    entry.Value.shadows = ShouldCastShadows(entry.Key, viewerPosition)
                        ? LightShadows.Hard
                        : LightShadows.None;
            }
        }

        void PlayIgniteFeedback(BlockPosition position)
        {
            if (!igniteFeedbackEnabled || !Application.isPlaying)
                return;

            if (audioCuePlayer == null)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();
            if (vfxCuePlayer == null)
                vfxCuePlayer = FindFirstObjectByType<BlockiverseVfxCuePlayer>();

            Vector3 lightPosition = GetLightPosition(position);
            audioCuePlayer?.PlayCueAt(BlockiverseAudioCue.TorchIgnite, lightPosition);
            vfxCuePlayer?.PlayCue(BlockiverseVfxCue.TorchSpark, lightPosition);
        }

        void RemoveLight(BlockPosition position)
        {
            if (!emitterPositions.Remove(position))
                return;

            DestroyLight(position);
            // Refilling the freed slot needs a scan over every emitter, so defer it to the
            // throttled reselect instead of paying it on each lava tick.
            slotsDirty = true;
        }

        void ClearLights()
        {
            foreach (Light light in lightsByPosition.Values)
                DestroyLight(light);

            lightsByPosition.Clear();
            emitterPositions.Clear();
        }

        void Update()
        {
            if (world == null || blockRegistry == null)
                return;

            if (Time.time < nextReselectTime)
                return;

            nextReselectTime = Time.time + ReselectIntervalSeconds;

            Vector3 viewerPosition = ResolveViewerPosition();
            bool viewerMoved = (viewerPosition - lastSelectionViewerPosition).sqrMagnitude >=
                               ViewerMoveReselectDistance * ViewerMoveReselectDistance;

            // Re-ranking which emitters own slots only matters when the budget is oversubscribed
            // or a slot was freed.
            bool needsReselect = slotsDirty ||
                                 emitterPositions.Count > MaxRuntimePointLights ||
                                 lightsByPosition.Count != emitterPositions.Count;

            if (needsReselect && (slotsDirty || viewerMoved))
            {
                slotsDirty = false;
                FillLightSlots();
                return;
            }

            // Even when every emitter keeps its slot, walking across a lit room changes WHICH ones
            // are nearest, and only the nearest few are allowed to cast shadows.
            if (viewerMoved)
            {
                lastSelectionViewerPosition = viewerPosition;
                RefreshShadowCasters();
            }
        }

        Vector3 ResolveViewerPosition()
        {
            if (viewer == null)
            {
                Camera camera = Camera.main;
                if (camera != null)
                    viewer = camera.transform;
            }

            // With no camera (EditMode fixtures, headless tests) fall back to the manager's own
            // position so selection stays deterministic instead of silently creating no lights.
            return viewer != null ? viewer.position : transform.position;
        }

        // Chooses which emitters own the limited light slots, nearest-to-viewer first, and keeps
        // the shadow-casting subset to the closest few.
        void FillLightSlots()
        {
            if (world == null || blockRegistry == null)
                return;

            Vector3 viewerPosition = ResolveViewerPosition();
            lastSelectionViewerPosition = viewerPosition;

            selectionScratch.Clear();
            selectionScratch.AddRange(emitterPositions);

            if (selectionScratch.Count > MaxRuntimePointLights)
            {
                selectionScratch.Sort((a, b) =>
                {
                    float distanceA = (GetLightPosition(a) - viewerPosition).sqrMagnitude;
                    float distanceB = (GetLightPosition(b) - viewerPosition).sqrMagnitude;
                    return distanceA.CompareTo(distanceB);
                });

                selectionScratch.RemoveRange(
                    MaxRuntimePointLights,
                    selectionScratch.Count - MaxRuntimePointLights);
            }

            // Drop lights that lost their slot.
            if (lightsByPosition.Count > 0)
            {
                selectedScratch.Clear();
                foreach (BlockPosition position in selectionScratch)
                    selectedScratch.Add(position);

                staleScratch.Clear();
                foreach (BlockPosition position in lightsByPosition.Keys)
                {
                    if (!selectedScratch.Contains(position))
                        staleScratch.Add(position);
                }

                foreach (BlockPosition position in staleScratch)
                    DestroyLight(position);
            }

            for (int i = 0; i < selectionScratch.Count; i++)
            {
                BlockPosition position = selectionScratch[i];
                if (!lightsByPosition.ContainsKey(position))
                    CreateLight(position);
            }

            // Ranked only once the full set exists — doing it inside the creation loop would rank
            // each light against a half-built dictionary.
            RefreshShadowCasters();
        }

        bool ShouldCastShadows(BlockPosition position, Vector3 viewerPosition)
        {
            if (MaxShadowCastingLights <= 0)
                return false;

            float distance = (GetLightPosition(position) - viewerPosition).sqrMagnitude;
            int closer = 0;

            foreach (BlockPosition other in lightsByPosition.Keys)
            {
                if (other.Equals(position))
                    continue;

                if ((GetLightPosition(other) - viewerPosition).sqrMagnitude < distance && ++closer >= MaxShadowCastingLights)
                    return false;
            }

            return true;
        }

        Light CreateLight(BlockPosition position)
        {
            if (!blockRegistry.TryGet(world.GetBlock(position), out BlockDefinition definition) ||
                definition.EmissiveLight <= 0)
                return null;

            var lightObject = new GameObject($"Glowwick Light {position}");
            lightObject.transform.SetParent(transform, worldPositionStays: false);
            lightObject.transform.position = GetLightPosition(position);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            // Full strength, because the shadow map is now this light's ONLY occluder. 0.7 was
            // tuned while the baked emitterReach gate also zeroed the punctual term, so it never
            // actually governed anything -- wherever it mattered the bake had already won. The
            // shader resolves occlusion per light now (PunctualOcclusion in BlockiverseVoxelLit),
            // so this is the sole control over how dark an emitter shadow is, and 1.0 is what
            // preserves the shipped "no punctual light through a wall" contract. Back it off only
            // if shadow acne proves objectionable on device.
            light.shadowStrength = 1.0f;
            // Emitters sit inside their own voxel, so bias the near plane out past the block face
            // to stop the source cube self-shadowing into a black halo.
            light.shadowNearPlane = 0.6f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.range = GetLightRange(definition.EmissiveLight);
            light.intensity = GetLightIntensity(definition.EmissiveLight);
            light.color = LightColorForBlock(definition.Id);

            lightsByPosition[position] = light;
            return light;
        }

        void DestroyLight(BlockPosition position)
        {
            if (!lightsByPosition.TryGetValue(position, out Light light))
                return;

            lightsByPosition.Remove(position);
            DestroyLight(light);
        }

        static void DestroyLight(Light light)
        {
            if (light == null)
                return;

            GameObject lightObject = light.gameObject;
            if (Application.isPlaying)
                Destroy(lightObject);
            else
                DestroyImmediate(lightObject);
        }

        static Color LightColorForBlock(BlockId block)
        {
            if (block == BlockRegistry.LumenLamp)
                return new Color(1.0f, 0.92f, 0.64f);
            if (block == BlockRegistry.SparkFlare)
                return new Color(1.0f, 0.72f, 0.28f);
            if (block == BlockRegistry.Campfire || block == BlockRegistry.Emberflow || block == BlockRegistry.EmberflowFlow)
                return new Color(1.0f, 0.45f, 0.18f);
            if (block == BlockRegistry.LumenQuartzCluster)
                return new Color(0.54f, 0.93f, 1.0f);
            if (block == BlockRegistry.StaropalGeode)
                return new Color(0.88f, 0.68f, 1.0f);

            return new Color(1.0f, 0.78f, 0.36f);
        }

        void OnDestroy()
        {
            if (world != null)
                world.BlockChanged -= OnBlockChanged;

            ClearLights();
        }
    }
}
