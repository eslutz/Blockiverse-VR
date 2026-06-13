namespace Blockiverse.Core
{
    public static class BlockiverseRuntimeState
    {
        public static bool IsGamePaused { get; private set; }
        public static bool AllowWorldInput { get; private set; } = true;

        public static void SetRouterState(bool isGamePaused, bool allowWorldInput)
        {
            IsGamePaused = isGamePaused;
            AllowWorldInput = allowWorldInput;
        }

        public static void Reset()
        {
            IsGamePaused = false;
            AllowWorldInput = true;
        }
    }
}
