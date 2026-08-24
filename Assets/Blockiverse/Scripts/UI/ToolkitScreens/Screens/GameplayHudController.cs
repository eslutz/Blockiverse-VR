using Blockiverse.Core;
using Blockiverse.Gameplay;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // The action bar of the gameplay HUD (matrix row 21): panel-open shortcuts into the routed
    // inventory / crafting / shared-crate / block-catalog screens.
    //
    // These four buttons are the ONLY way into inventory and crafting — neither has a controller
    // binding, which is why the Controller Map popup does not mention them. That makes this the
    // one HUD panel that must stay reachable by the XRI ray.
    //
    // The health readout used to live here too, in one panel at the Hud profile's default
    // placement: dead centre, 1.15 m ahead, eye height. Eric's report was that the HUD sat in the
    // middle of his view and made things hard to see. It is now two panels — GameplayStatsController
    // top-right, this bar low and tilted up — because a HUD belongs at the perimeter and one
    // document cannot be in two corners.
    //
    // Both halves keep the same screen id: the host maps many controllers onto one route, which is
    // how MiningProgress and StatusToast already share it.
    [UiToolkitScreen(MenuActions.GameplayHudScreen, "Assets/Blockiverse/UI/Documents/GameplayHud.uxml",
        590, 150, UiToolkitPlacementProfile.Hud, HudLocalX = 0f, HudLocalY = -0.40f, HudLocalZ = 1.05f, HudPitchDegrees = 25f)]
    public sealed class GameplayHudController : UiToolkitScreenController
    {
        Button openInventoryButton;
        Button openCraftingButton;
        Button openCrateButton;
        Button openCatalogButton;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        public override string ScreenId => MenuActions.GameplayHudScreen;

        // Public click seams: EditMode cannot deliver a ClickEvent without a runtime panel.
        public void OpenInventory()
        {
            BlockiverseMenuController controller = MenuController;
            if (controller == null)
                return;

            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            controller.OpenInventoryScreen();
        }

        public void OpenCrafting()
        {
            BlockiverseMenuController controller = MenuController;
            if (controller == null)
                return;

            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            controller.OpenCraftingScreen();
        }

        // No Open verb exists for the crate screen (uGUI wires only CloseStationCrateScreen),
        // so the canonical route is pushed directly, shaped exactly like OpenInventoryScreen.
        public void OpenCrate()
        {
            BlockiverseMenuController controller = MenuController;
            if (controller == null || controller.Router == null)
                return;

            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            controller.Router.PushScreen(new ScreenRoute(MenuActions.StationCrateScreen));
        }

        public void OpenCatalog()
        {
            BlockiverseMenuController controller = MenuController;
            if (controller == null)
                return;

            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            controller.OpenCatalogScreen();
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            openInventoryButton = Require<Button>(root, "bv-hud-open-inventory", ref allFound);
            openCraftingButton = Require<Button>(root, "bv-hud-open-crafting", ref allFound);
            openCrateButton = Require<Button>(root, "bv-hud-open-crate", ref allFound);
            openCatalogButton = Require<Button>(root, "bv-hud-open-catalog", ref allFound);
            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            if (openInventoryButton != null)
                openInventoryButton.clicked += OpenInventory;

            if (openCraftingButton != null)
                openCraftingButton.clicked += OpenCrafting;

            if (openCrateButton != null)
                openCrateButton.clicked += OpenCrate;

            if (openCatalogButton != null)
                openCatalogButton.clicked += OpenCatalog;

            // No locale subscription here any more: every label on this panel is a static UXML
            // binding, which updates natively. Only the stats readout writes dynamic text.
        }

        protected override void OnUnregisterCallbacks()
        {
            if (openInventoryButton != null)
                openInventoryButton.clicked -= OpenInventory;

            if (openCraftingButton != null)
                openCraftingButton.clicked -= OpenCrafting;

            if (openCrateButton != null)
                openCrateButton.clicked -= OpenCrate;

            if (openCatalogButton != null)
                openCatalogButton.clicked -= OpenCatalog;
        }

        protected override void OnDetach()
        {
            openInventoryButton = null;
            openCraftingButton = null;
            openCrateButton = null;
            openCatalogButton = null;
        }
    }
}
