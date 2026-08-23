using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.MetaAvatars;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    /// <summary>
    /// Presents placed mirror_pane blocks (issue #340). Tracks every mirror in the world
    /// (full scan on configure, incremental via BlockChanged — the GlowwickLightManager
    /// shape), activates the single nearest mirror the viewer can see, and drives its
    /// surface: a pooled quad on the pane face showing the render texture from
    /// <see cref="BlockiverseMirrorAvatarView"/> — the loopback copy of the player's own
    /// avatar. The texture is sampled X-flipped, which together with the reflected pose
    /// reads as a true mirror. One mirror is ever active: the studio camera is the single
    /// most expensive thing this feature owns, so the budget is explicit and small.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockiverseMirrorSurfaceManager : MonoBehaviour
    {
        const float ActivationRangeMeters = 6.0f;
        const float ReselectIntervalSeconds = 0.5f;
        const float PaneInset = 0.002f;
        const float PaneScale = 0.96f;

        VoxelWorld world;
        BlockRegistry registry;
        readonly HashSet<BlockPosition> mirrorPositions = new();
        readonly HashSet<BlockId> mirrorBlockSet = new() { BlockRegistry.MirrorPane };
        readonly List<BlockPosition> scanScratch = new();

        BlockiverseMirrorAvatarView view;
        GameObject paneObject;
        Renderer paneRenderer;
        Material paneMaterial;

        BlockPosition activeMirror;
        Vector3Int activeFaceNormal;
        bool hasActiveMirror;
        float nextReselectTime;

        public bool HasActiveMirror => hasActiveMirror;
        public int MirrorCount => mirrorPositions.Count;

        public void Configure(VoxelWorld voxelWorld, BlockRegistry blockRegistry)
        {
            if (world != null)
                world.BlockChanged -= OnBlockChanged;

            world = voxelWorld;
            registry = blockRegistry;

            mirrorPositions.Clear();
            DeactivateMirror();

            if (world == null || registry == null)
                return;

            world.BlockChanged += OnBlockChanged;

            scanScratch.Clear();
            world.CollectBlockPositions(mirrorBlockSet, scanScratch);
            foreach (BlockPosition position in scanScratch)
                mirrorPositions.Add(position);
            scanScratch.Clear();
        }

        void OnBlockChanged(BlockChange change)
        {
            if (change.PreviousBlock == BlockRegistry.MirrorPane)
            {
                mirrorPositions.Remove(change.Position);
                if (hasActiveMirror && change.Position.Equals(activeMirror))
                    DeactivateMirror();
            }

            if (change.NewBlock == BlockRegistry.MirrorPane)
                mirrorPositions.Add(change.Position);

            // A neighbour change can open or block the active face; let the next
            // reselect resolve it promptly.
            nextReselectTime = 0.0f;
        }

        void LateUpdate()
        {
            if (world == null)
                return;

            Camera viewerCamera = Camera.main;
            if (viewerCamera == null)
            {
                DeactivateMirror();
                return;
            }

            if (Time.unscaledTime >= nextReselectTime)
            {
                nextReselectTime = Time.unscaledTime + ReselectIntervalSeconds;
                SelectMirror(viewerCamera);
            }

            if (hasActiveMirror)
                DriveActiveMirror(viewerCamera);
        }

        void SelectMirror(Camera viewerCamera)
        {
            Vector3 viewerPosition = viewerCamera.transform.position;
            BlockPosition best = default;
            Vector3Int bestNormal = default;
            float bestDistanceSq = ActivationRangeMeters * ActivationRangeMeters;
            bool found = false;

            foreach (BlockPosition position in mirrorPositions)
            {
                Vector3 center = BlockCenter(position);
                float distanceSq = (center - viewerPosition).sqrMagnitude;
                if (distanceSq > bestDistanceSq)
                    continue;

                if (!MirrorPoseMath.TryChooseVisibleFace(
                        center, viewerPosition, normal => IsFaceOpen(position, normal), out Vector3Int normal))
                {
                    continue;
                }

                // Distance and an open neighbour only prove the pane COULD show, not that this
                // viewer can currently see it: a nearer pane behind the player, or behind an
                // intervening wall, would otherwise win the single active-mirror slot over a
                // farther pane actually in view.
                if (!IsPaneVisibleToViewer(viewerCamera, center))
                    continue;

                best = position;
                bestNormal = normal;
                bestDistanceSq = distanceSq;
                found = true;
            }

            if (!found)
            {
                DeactivateMirror();
                return;
            }

            if (!hasActiveMirror || !best.Equals(activeMirror) || bestNormal != activeFaceNormal)
                ActivateMirror(best, bestNormal);
        }

        // Front-hemisphere check (not the camera's actual FOV — deliberately generous, since a
        // pane at the edge of view still visibly updates) plus a solid-geometry occlusion test.
        // The mirror pane block itself never appears in this hit test: only solid blocks carry a
        // collider on this layer (VoxelWorldRenderer's collider mesh is fed by the solid mesh
        // only), and mirror_pane is isSolid: false.
        static bool IsPaneVisibleToViewer(Camera viewerCamera, Vector3 paneCenter)
        {
            Transform viewerTransform = viewerCamera.transform;
            Vector3 toPane = paneCenter - viewerTransform.position;
            if (Vector3.Dot(viewerTransform.forward, toPane) <= 0.0f)
                return false;

            return !Physics.Linecast(
                viewerTransform.position, paneCenter, BlockiverseProject.VoxelGroundLayerMask);
        }

        bool IsFaceOpen(BlockPosition position, Vector3Int normal)
        {
            var neighbor = new BlockPosition(position.X + normal.x, position.Y + normal.y, position.Z + normal.z);
            if (!world.Bounds.Contains(neighbor))
                return false;

            BlockDefinition definition = registry.Get(world.GetBlock(neighbor));
            return definition == null || !definition.IsSolid;
        }

        void ActivateMirror(BlockPosition position, Vector3Int faceNormal)
        {
            EnsureView();
            EnsurePane();

            activeMirror = position;
            activeFaceNormal = faceNormal;
            hasActiveMirror = true;

            Vector3 normal = new(faceNormal.x, faceNormal.y, faceNormal.z);
            Vector3 paneCenter = BlockCenter(position) + normal * (0.5f + PaneInset);

            // Unity's Quad primitive shows its front toward -Z, so +Z points away from
            // the viewer: aim +Z along the inward normal.
            paneObject.transform.SetPositionAndRotation(paneCenter, Quaternion.LookRotation(-normal, Vector3.up));
            paneObject.transform.localScale = new Vector3(PaneScale, PaneScale, 1.0f);
            paneObject.SetActive(true);

            // The studio floats above the pane: close enough that avatar LOD stays high,
            // culled from the main camera by its layer either way.
            view.StudioRoot.SetPositionAndRotation(paneCenter + Vector3.up * 12.0f, Quaternion.identity);
            view.SetMirrorActive(true);
        }

        void DriveActiveMirror(Camera viewerCamera)
        {
            Transform rig = viewerCamera.transform.root;
            Vector3 normal = new(activeFaceNormal.x, activeFaceNormal.y, activeFaceNormal.z);
            Vector3 paneCenter = BlockCenter(activeMirror) + normal * 0.5f;
            Quaternion paneBasis = Quaternion.LookRotation(normal, Vector3.up);

            MirrorPoseMath.ReflectIntoPaneFrame(
                paneCenter, paneBasis, rig.position, rig.forward,
                out Vector3 paneLocalPosition, out Vector3 paneLocalForward);
            MirrorPoseMath.ComposeStudioPose(
                view.StudioRoot.position, view.StudioRoot.rotation,
                paneLocalPosition, paneLocalForward,
                out Vector3 studioPosition, out Quaternion studioRotation);

            view.TickMirror(studioPosition, studioRotation);
        }

        void DeactivateMirror()
        {
            hasActiveMirror = false;

            if (paneObject != null)
                paneObject.SetActive(false);

            if (view != null)
                view.SetMirrorActive(false);
        }

        void EnsureView()
        {
            if (view != null)
                return;

            view = BlockiverseMirrorAvatarView.Create(BlockiverseProject.MirrorAvatarLayerIndex);
        }

        void EnsurePane()
        {
            if (paneObject != null)
                return;

            paneObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            paneObject.name = "Mirror Surface";
            paneObject.transform.SetParent(transform, false);

            Collider paneCollider = paneObject.GetComponent<Collider>();
            if (paneCollider != null)
            {
                if (Application.isPlaying)
                    Destroy(paneCollider);
                else
                    DestroyImmediate(paneCollider);
            }

            paneRenderer = paneObject.GetComponent<MeshRenderer>();
            paneMaterial = CreatePaneMaterial(view.Texture);
            paneRenderer.sharedMaterial = paneMaterial;
            paneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            paneRenderer.receiveShadows = false;
            paneObject.SetActive(false);
        }

        static Material CreatePaneMaterial(Texture texture)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            var material = new Material(shader)
            {
                mainTexture = texture,
                // A camera view of the loopback avatar is the "another person" image;
                // sampling it X-flipped completes the reflection's handedness.
                mainTextureScale = new Vector2(-1.0f, 1.0f),
                mainTextureOffset = new Vector2(1.0f, 0.0f),
            };
            return material;
        }

        static Vector3 BlockCenter(BlockPosition position)
        {
            return new Vector3(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f);
        }

        void OnDestroy()
        {
            if (world != null)
                world.BlockChanged -= OnBlockChanged;

            if (paneMaterial != null)
                Destroy(paneMaterial);

            if (view != null)
                Destroy(view.gameObject);
        }
    }
}
