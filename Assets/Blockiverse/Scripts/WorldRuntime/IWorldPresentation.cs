using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;

namespace Blockiverse.Gameplay
{
    // Everything CreativeWorldManager needs a renderer, scene lighting, interaction rig, or void
    // floor for. The world simulation talks to this and never names a presentation type, so the
    // simulation can live in an assembly that does not reference XRI, TextMeshPro, or uGUI.
    //
    // On a dedicated server there is no implementation in the process at all: the presentation
    // assembly is excluded from the server platform, CreativeWorldManager resolves null, and the
    // code that would need a texture atlas is never reached. See ADR 0007.
    //
    // IMPORTANT: implementations are MonoBehaviours, so a reference of this interface type does NOT
    // participate in Unity's lifetime-aware `==` overload — a destroyed implementation compares
    // non-null through the interface. Consumers must null-check the backing UnityEngine.Object, not
    // the interface reference. CreativeWorldManager.HasPresentation is the pattern.
    public interface IWorldPresentation : IVoxelWorldRenderer
    {
        // True once the spawn neighbourhood is meshed and collidable; the loading-screen gate.
        bool SpawnRegionReady { get; }

        // Binds the presentation to a world. The sky map is supplied by the simulation rather than
        // built here: sky occlusion is a simulation input (crop growth, cave detection), so the
        // manager owns it and both sides read the same instance instead of two that can drift.
        void ConfigureForWorld(
            VoxelWorld world,
            BlockRegistry registry,
            VoxelSkyLightMap skyLight,
            WorldGenerationSettings settings,
            string textureSetId,
            MultiplayerChunkAuthoritySync authoritySync,
            bool deferInitialRebuild);

        // Changes textures on a LIVE world, with no reload and no chunk re-mesh.
        //
        // Takes a token (a built-in set id or `pack:<id>`) as a string so WorldRuntime never names
        // a Gameplay type or a Texture2D. Local to this peer and never transmitted.
        void ApplyTextureSelection(string token);

        // Re-wires the interaction path when the authority sync arrives after the world.
        void ConfigureAuthority(MultiplayerChunkAuthoritySync authoritySync);

        // Teleports the player rig to the world spawn. No-ops when no rig exists.
        void PositionRigAtSpawn(BlockPosition spawnPosition);
    }
}
