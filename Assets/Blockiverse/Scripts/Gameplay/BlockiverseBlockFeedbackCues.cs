using Blockiverse.Voxel;

namespace Blockiverse.Gameplay
{
    public static class BlockiverseBlockFeedbackCues
    {
        public static BlockiverseAudioCue ToolHitForBlock(BlockRegistry registry, BlockId block)
        {
            if (registry == null || !registry.TryGet(block, out BlockDefinition definition))
                return BlockiverseAudioCue.ToolHitSoft;

            if (definition.Category == BlockCategory.Organic)
                return BlockiverseAudioCue.ToolHitSoft;

            return definition.Category == BlockCategory.Terrain ||
                   definition.Category == BlockCategory.Resource ||
                   definition.HardnessClass >= BlockHardnessClass.Medium
                ? BlockiverseAudioCue.ToolHitStone
                : BlockiverseAudioCue.ToolHitSoft;
        }
    }
}
