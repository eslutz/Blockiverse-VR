namespace Blockiverse.Networking
{
    public static class CreativePermissionPolicy
    {
        public static bool CanUseCreativeMode(WorldGameMode worldMode, bool isClientOnly)
        {
            return worldMode == WorldGameMode.Creative && !isClientOnly;
        }

        public static bool CanTogglePlayerMode(
            WorldGameMode worldMode,
            PlayerModeState currentPlayerMode,
            bool isClientOnly)
        {
            if (isClientOnly)
                return false;

            return worldMode == WorldGameMode.Creative ||
                   currentPlayerMode == PlayerModeState.Creative;
        }

        public static bool CanSubmitDirectCreativeMutation(WorldGameMode worldMode)
        {
            return worldMode == WorldGameMode.Creative;
        }
    }
}