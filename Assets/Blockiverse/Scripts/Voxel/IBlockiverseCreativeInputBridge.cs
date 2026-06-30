using System;

namespace Blockiverse.Voxel
{
    public interface IBlockiverseCreativeInputBridge
    {
        event Action<BlockPosition, float, float> MiningProgressChanged;
        event Action MiningProgressCleared;
    }
}
