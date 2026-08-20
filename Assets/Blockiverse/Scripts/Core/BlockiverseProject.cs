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
        // Ground detection: solid chunk colliders and the void safety floor only. This is the one
        // mask that deliberately excludes fluid — widening it reintroduces walking on water.
        public const int VoxelGroundLayerMask = InteractionLayerMask;
        // Ray targeting: block interaction (place/mine), drink/bucket fill on water, and teleport
        // landing. Water is a valid target for all three, so it is included here.
        public const int VrUiRaycastLayerMask = InteractionLayerMask | FluidLayerMask;
    }
}
