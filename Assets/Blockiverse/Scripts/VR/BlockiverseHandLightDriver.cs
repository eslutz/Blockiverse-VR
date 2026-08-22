using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.VR
{
    // Lights the first-person hands from the world they are actually standing in.
    //
    // "Sealed rooms are black" is a property of ONE shader plus baked vertex colour, not of the
    // scene lighting: BlockiverseLightingCycleController never dims the directional light or ambient
    // for enclosure, it just gates them per face in the voxel shader. So any lit renderer that is
    // NOT on the voxel shader ignores caves entirely -- which is why the hands stayed brightly lit,
    // with visible directional shading on their top faces, inside a pitch-black room.
    //
    // The fix is to scale the hands' ALBEDO by the same light the voxel world uses. Multiplying
    // the albedo rather than replacing the shading keeps the lit material's directional gradient --
    // which is what makes the hands read as objects rather than flat cut-outs -- while still
    // driving them dark in a sealed room, because a near-black albedo stays near black however
    // bright the scene's directional light is.
    //
    // This lives in VR rather than beside the hands because BlockiverseNetworkAvatarRig is in the
    // Networking assembly, which does not reference Gameplay where VoxelLightSampler lives. It
    // reaches the hands through the rig's already-public anchors.
    [DisallowMultipleComponent]
    public sealed class BlockiverseHandLightDriver : MonoBehaviour
    {
        // Hands never go fully black. Losing sight of your own hands in a dark cave is
        // disorienting in a way the world going dark is not -- they are the player's only body.
        public const float MinimumHandLight = 0.08f;

        // SampleAirLight walks up to six directions for several steps, so it is not a per-frame,
        // per-hand cost worth paying. A tenth of a second is far finer than a hand can cross a
        // lighting boundary.
        public const float SampleIntervalSeconds = 0.1f;

        // Seconds to cross most of the way to a newly sampled level. Without this, stepping over a
        // cave threshold pops both hands between two brightnesses in a single frame.
        public const float BlendSeconds = 0.15f;

        BlockiverseNetworkAvatarRig avatarRig;
        CreativeWorldManager worldManager;
        Renderer leftHandRenderer;
        Renderer rightHandRenderer;
        MaterialPropertyBlock propertyBlock;
        Color baseColor = Color.white;
        bool baseColorResolved;
        float nextSampleTime;
        float leftLight = 1.0f;
        float rightLight = 1.0f;

        public float LeftHandLight => leftLight;

        public float RightHandLight => rightLight;

        public void Configure(BlockiverseNetworkAvatarRig rig, CreativeWorldManager manager)
        {
            if (rig != null)
                avatarRig = rig;
            if (manager != null)
                worldManager = manager;
        }

        void LateUpdate()
        {
            ResolveReferences();

            if (avatarRig == null)
                return;

            bool sample = Time.time >= nextSampleTime;

            if (sample)
                nextSampleTime = Time.time + SampleIntervalSeconds;

            leftLight = Advance(leftLight, avatarRig.LeftHandAnchor, ref leftHandRenderer, sample);
            rightLight = Advance(rightLight, avatarRig.RightHandAnchor, ref rightHandRenderer, sample);
        }

        float Advance(float current, Transform anchor, ref Renderer handRenderer, bool sample)
        {
            if (anchor == null)
                return current;

            if (handRenderer == null)
                handRenderer = anchor.GetComponentInChildren<Renderer>(includeInactive: true);

            if (handRenderer == null)
                return current;

            if (sample)
            {
                float target = SampleLightAt(anchor.position);
                current = Mathf.Lerp(current, target, Mathf.Clamp01(SampleIntervalSeconds / BlendSeconds));
            }

            ApplyTint(handRenderer, current);
            return current;
        }

        float SampleLightAt(Vector3 worldPosition)
        {
            VoxelWorld world = worldManager != null ? worldManager.World : null;

            // No world means the title screen or a scene without one; full brightness is the
            // honest answer there rather than black hands.
            if (world == null || worldManager.Registry == null)
                return 1.0f;

            BlockPosition cell = CreativeInteractionController.ToBlockPosition(worldPosition);

            if (!world.Bounds.Contains(cell))
                return 1.0f;

            VoxelSkyLightMap skyLight = worldManager.Renderer != null ? worldManager.Renderer.SkyLight : null;
            float light = VoxelLightSampler.SampleAirLight(world, worldManager.Registry, cell, skyLight: skyLight);

            return Mathf.Max(light, MinimumHandLight);
        }

        void ApplyTint(Renderer handRenderer, float light)
        {
            if (!baseColorResolved)
            {
                Material shared = handRenderer.sharedMaterial;

                if (shared != null)
                {
                    baseColor = shared.HasProperty(BaseColorId) ? shared.GetColor(BaseColorId) : shared.color;
                    baseColorResolved = true;
                }
            }

            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            handRenderer.GetPropertyBlock(propertyBlock);

            var tinted = new Color(baseColor.r * light, baseColor.g * light, baseColor.b * light, baseColor.a);
            propertyBlock.SetColor(BaseColorId, tinted);
            propertyBlock.SetColor(ColorId, tinted);
            handRenderer.SetPropertyBlock(propertyBlock);
        }

        void ResolveReferences()
        {
            if (avatarRig == null)
                avatarRig = GetComponentInParent<BlockiverseNetworkAvatarRig>() ??
                            FindFirstObjectByType<BlockiverseNetworkAvatarRig>(FindObjectsInactive.Include);

            // Read live, never cached past a frame: New World and Load replace the VoxelWorld
            // instance whole, and a stale world would light the hands from the old cave.
            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
    }
}
