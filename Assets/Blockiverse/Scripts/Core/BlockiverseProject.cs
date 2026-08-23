namespace Blockiverse.Core
{
    public static class BlockiverseProject
    {
        public const string ProductName = "Blockiverse VR";
        public const string CompanyName = "Eric Slutz";
        public const string AndroidApplicationIdentifier = "dev.ericslutz.blockiversevr";
        public const string XrRigRootName = "BlockiverseXRRig";
        public const string CreativeWorldRootName = "Creative World";
        public const string BootScenePath = "Assets/Blockiverse/Scenes/Boot.unity";
        public const string MultiplayerTestScenePath = "Assets/Blockiverse/Scenes/MultiplayerTest.unity";
        public const string ServerScenePath = "Assets/Blockiverse/Scenes/Server.unity";
        public const string XrRigPrefabPath = "Assets/Blockiverse/Prefabs/BlockiverseXRRig.prefab";
        public const string NetworkManagerPrefabPath = "Assets/Blockiverse/Prefabs/Networking/BlockiverseNetworkManager.prefab";
        public const string NetworkPlayerPrefabPath = "Assets/Blockiverse/Prefabs/Networking/BlockiverseNetworkPlayer.prefab";
        public const string AndroidUrpAssetPath = "Assets/Blockiverse/Settings/BlockiverseAndroidURPAsset.asset";
        public const string AndroidUrpRendererPath = "Assets/Blockiverse/Settings/BlockiverseAndroidUniversalRenderer.asset";
        public const string InputActionsAssetPath = "Assets/Blockiverse/Settings/BlockiverseInputActions.inputactions";
        public const string InputActionReferencesFolderPath = "Assets/Blockiverse/Settings/InputActionReferences";
        public const string BrandingArtFolderPath = "Assets/Blockiverse/Art/Sprites/Branding";
        public const string AppIconPath = BrandingArtFolderPath + "/blockiverse_app_icon.png";
        public const string LaunchArtworkPath = BrandingArtFolderPath + "/blockiverse_launch_landscape_named.png";
        public const string LaunchArtworkPlainPath = BrandingArtFolderPath + "/blockiverse_launch_landscape.png";
        public const string AndroidBrandingLibraryPath = "Assets/Plugins/Android/BlockiverseBranding.androidlib";
        public const string AndroidAppStringsPath = AndroidBrandingLibraryPath + "/res/values/strings.xml";
        public const string PointerLineMaterialPath = "Assets/Blockiverse/Materials/BlockiversePointerLine.mat";
        public const string VfxParticleMaterialPath = "Assets/Blockiverse/Materials/BlockiverseVfxParticle.mat";
        public const string ChunkAtlasMaterialPath = "Assets/Blockiverse/Materials/BlockiverseChunkAtlas.mat";
        public const string SkyMaterialPath = "Assets/Blockiverse/Materials/BlockiverseSky.mat";
        public const string InteractionLayerName = "BlockiverseInteractable";
        public const int InteractionLayerIndex = 10;
        public const int InteractionLayerMask = 1 << InteractionLayerIndex;
        public const string CompositionUiLayerName = "BlockiverseCompositionUI";
        public const int CompositionUiLayerIndex = 11;
        public const int CompositionUiLayerMask = 1 << CompositionUiLayerIndex;
        public const string XrVisualProjectionLayerName = "BlockiverseXrVisuals";
        public const int XrVisualProjectionLayerIndex = 12;
        public const int XrVisualProjectionLayerMask = 1 << XrVisualProjectionLayerIndex;
        // Fluid chunk geometry lives on its own layer so the player falls through water instead of
        // standing on it. Contact exclusion alone is not enough: XRI's GravityProvider resolves
        // "grounded" with a PhysicsScene.SphereCast, and scene queries ignore Collider.excludeLayers,
        // so a fluid collider on the interaction layer reads as solid ground to gravity.
        public const string FluidLayerName = "BlockiverseFluid";
        public const int FluidLayerIndex = 13;
        public const int FluidLayerMask = 1 << FluidLayerIndex;
        // The mirror "studio": a pocket holding the loopback avatar entity and its
        // render-texture camera (issue #340). Culled from the main camera — only the
        // mirror camera sees this layer, and it sees nothing else.
        public const string MirrorAvatarLayerName = "BlockiverseMirrorAvatar";
        public const int MirrorAvatarLayerIndex = 14;
        public const int MirrorAvatarLayerMask = 1 << MirrorAvatarLayerIndex;
        // Passable block geometry: rendered and ray-targetable, but never obstructing movement.
        // Vegetation is the first user; the name is deliberately general (not "foliage") because
        // the layer index is baked into TagManager.asset and renaming it later is expensive, and
        // open doorways or decorative props would want exactly this behaviour.
        //
        // Same reasoning as fluid above, and it must be enforced the same way: contact exclusion
        // alone is not enough, because GravityProvider's ground sphere-cast is a scene query and
        // scene queries ignore Collider.excludeLayers.
        public const string PassableLayerName = "BlockiversePassable";
        public const int PassableLayerIndex = 15;
        public const int PassableLayerMask = 1 << PassableLayerIndex;
        // Ground detection: solid chunk colliders and the void safety floor only. This is the one
        // mask that deliberately excludes fluid — widening it reintroduces walking on water. It
        // excludes the passable layer for free, by never naming it.
        public const int VoxelGroundLayerMask = InteractionLayerMask;
        // Ray targeting for the VR UI/teleport ray: block interaction, drink/bucket fill on water,
        // and teleport landing. Water is a valid target for all three, so it is included here.
        //
        // Passable geometry is deliberately ABSENT: a teleport arc must pass THROUGH grass and
        // land on the ground beneath it (vegetation ruleset §4a.4), which is the opposite of the
        // deliberate water behaviour. Do not widen this constant to add vegetation — the
        // bootstrapper bakes it into the rig prefab's teleport ray as well as the interaction ray.
        public const int VrUiRaycastLayerMask = InteractionLayerMask | FluidLayerMask;
        // Ray targeting for block interaction only (mine/place/harvest). Identical to the mask
        // above plus passable geometry, so a plant can be targeted and harvested even though the
        // teleport ray ignores it. This split is the whole reason it is a separate constant.
        public const int VoxelInteractionRaycastLayerMask = VrUiRaycastLayerMask | PassableLayerMask;
    }
}
