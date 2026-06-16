using System;
using System.Collections.Generic;
using Unity.Profiling;

namespace Blockiverse.UI
{
    // A single screen on the UI stack (voxel_survival_menus §2.3). PauseGame and AllowWorldInput
    // describe how the screen interacts with the running simulation.
    public readonly struct ScreenRoute : IEquatable<ScreenRoute>
    {
        public ScreenRoute(string screenId, bool pauseGame = false, bool allowWorldInput = false)
        {
            if (string.IsNullOrWhiteSpace(screenId))
                throw new ArgumentException("Screen ids must be non-empty.", nameof(screenId));

            ScreenId = screenId;
            PauseGame = pauseGame;
            AllowWorldInput = allowWorldInput;
        }

        public string ScreenId { get; }
        public bool PauseGame { get; }
        public bool AllowWorldInput { get; }

        public bool Equals(ScreenRoute other) =>
            string.Equals(ScreenId, other.ScreenId, StringComparison.OrdinalIgnoreCase) &&
            PauseGame == other.PauseGame &&
            AllowWorldInput == other.AllowWorldInput;

        public override bool Equals(object obj) => obj is ScreenRoute other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ScreenId?.ToLowerInvariant(), PauseGame, AllowWorldInput);
        public override string ToString() => ScreenId;
    }

    // Stack-based UI router (voxel_survival_menus §2.2/§2.3/§8.1). The screen stack always holds at
    // least the root screen; modals layer on top and take input priority. Input/pause/world-input
    // routing is derived from the current top of the modal or screen stack.
    public sealed class UiScreenRouter
    {
        static readonly ProfilerMarker PushScreenMarker = new("Blockiverse.UiScreenRouter.PushScreen");
        static readonly ProfilerMarker PopScreenMarker = new("Blockiverse.UiScreenRouter.PopScreen");
        static readonly ProfilerMarker ReplaceScreenMarker = new("Blockiverse.UiScreenRouter.ReplaceScreen");
        static readonly ProfilerMarker ClearToRootMarker = new("Blockiverse.UiScreenRouter.ClearToRoot");
        static readonly ProfilerMarker PushModalMarker = new("Blockiverse.UiScreenRouter.PushModal");
        static readonly ProfilerMarker PopModalMarker = new("Blockiverse.UiScreenRouter.PopModal");

        readonly List<ScreenRoute> screenStack = new();
        readonly List<string> modalStack = new();

        public event Action Changed;

        public UiScreenRouter(ScreenRoute root)
        {
            screenStack.Add(root);
        }

        public ScreenRoute ActiveScreen => screenStack[screenStack.Count - 1];
        public IReadOnlyList<ScreenRoute> ScreenStack => screenStack;
        public IReadOnlyList<string> ModalStack => modalStack;
        public int ScreenDepth => screenStack.Count;
        public bool HasModal => modalStack.Count > 0;

        // §2.2: input goes to the top modal when one is open, otherwise the active screen.
        public string InputTarget => HasModal ? modalStack[modalStack.Count - 1] : ActiveScreen.ScreenId;

        // §10.1: the simulation is paused when the active screen requests it. A modal alone does not
        // change the pause state (a confirm dialog over gameplay leaves the game running unless the
        // underlying screen pauses it).
        public bool IsGamePaused => ActiveScreen.PauseGame;

        // World (block) input is allowed only when no modal is open and the active screen permits it.
        public bool AllowWorldInput => !HasModal && ActiveScreen.AllowWorldInput;

        public void PushScreen(ScreenRoute route)
        {
            using ProfilerMarker.AutoScope scope = PushScreenMarker.Auto();

            screenStack.Add(route);
            Changed?.Invoke();
        }

        // Pops the top screen. The root screen cannot be popped; returns false when already at root.
        public bool PopScreen()
        {
            if (screenStack.Count <= 1)
                return false;

            using ProfilerMarker.AutoScope scope = PopScreenMarker.Auto();

            screenStack.RemoveAt(screenStack.Count - 1);
            Changed?.Invoke();
            return true;
        }

        public void ReplaceScreen(ScreenRoute route)
        {
            using ProfilerMarker.AutoScope scope = ReplaceScreenMarker.Auto();

            screenStack[screenStack.Count - 1] = route;
            Changed?.Invoke();
        }

        // Clears the entire stack down to a single new root (e.g. returning to the title menu).
        public void ClearToRoot(ScreenRoute root)
        {
            using ProfilerMarker.AutoScope scope = ClearToRootMarker.Auto();

            screenStack.Clear();
            screenStack.Add(root);
            modalStack.Clear();
            Changed?.Invoke();
        }

        public void PushModal(string modalId)
        {
            if (string.IsNullOrWhiteSpace(modalId))
                throw new ArgumentException("Modal ids must be non-empty.", nameof(modalId));

            using ProfilerMarker.AutoScope scope = PushModalMarker.Auto();

            modalStack.Add(modalId);
            Changed?.Invoke();
        }

        // Pops the top modal; returns false when no modal is open.
        public bool PopModal()
        {
            if (modalStack.Count == 0)
                return false;

            using ProfilerMarker.AutoScope scope = PopModalMarker.Auto();

            modalStack.RemoveAt(modalStack.Count - 1);
            Changed?.Invoke();
            return true;
        }
    }
}
