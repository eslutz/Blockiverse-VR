using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // Routed world-loading surface (matrix row 7). Deliberately inert: the Overlay placement
    // profile means the host never enables its input, the router shows it during world
    // create/load transitions, and EnterGameplay/ShowTitleScreen dismiss it. The startup
    // splash and its 2.25 s auto-hide timer are a separate uGUI object
    // (BlockiverseStartupOverlay) and stay uGUI-side this PR — no timer belongs here.
    [UiToolkitScreen(MenuActions.WorldLoadingScreen, "Assets/Blockiverse/UI/Documents/WorldLoadingScreen.uxml",
        900, 500, UiToolkitPlacementProfile.Overlay)]
    public sealed class WorldLoadingScreenController : UiToolkitScreenController
    {
        Label titleLabel;

        public override string ScreenId => MenuActions.WorldLoadingScreen;

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            // The title is static bound text; it is queried only so a missing or drifted
            // document fails IsBound loudly instead of presenting as a healthy blank panel.
            titleLabel = Require<Label>(root, "bv-loading-title", ref allFound);
            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
        }

        protected override void OnUnregisterCallbacks()
        {
        }

        protected override void OnDetach()
        {
            titleLabel = null;
        }
    }
}
