namespace Blockiverse.Core
{
    public static class BlockiverseRuntimeState
    {
        public static bool IsGamePaused { get; private set; }
        public static bool AllowWorldInput { get; private set; } = true;
        public static bool MenuInputActive { get; private set; }

        public static void SetRouterState(bool isGamePaused, bool allowWorldInput, bool menuInputActive = false)
        {
            IsGamePaused = isGamePaused;
            AllowWorldInput = allowWorldInput;
            MenuInputActive = menuInputActive;
        }

        public static void Reset()
        {
            IsGamePaused = false;
            AllowWorldInput = true;
            MenuInputActive = false;
        }
    }
}
