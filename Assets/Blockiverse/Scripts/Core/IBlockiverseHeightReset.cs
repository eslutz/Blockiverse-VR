namespace Blockiverse.Core
{
    public interface IBlockiverseHeightReset
    {
        void ResetHeight();
        void ApplyStandingEyeHeight(float standingEyeHeight);
    }
}
