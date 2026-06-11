using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    // Which of the player's contact cells trigger a hazard block: thornbrush hurts the body
    // (feet/head) when walked through; campfire burns when stood in or directly on.
    [Flags]
    public enum HazardContactCells
    {
        None = 0,
        Feet = 1 << 0,
        Head = 1 << 1,
        GroundBelow = 1 << 2,
    }

    public readonly struct BlockHazard
    {
        public BlockHazard(HazardVolumeDefinition hazard, HazardContactCells contactCells)
        {
            Hazard = hazard ?? throw new ArgumentNullException(nameof(hazard));
            ContactCells = contactCells;
        }

        public HazardVolumeDefinition Hazard { get; }
        public HazardContactCells ContactCells { get; }
    }

    // Data-driven map of hazardous blocks (canon §6) to their damage definitions, mirroring
    // StationProximity.StationForBlock. The vitals runtime scans the player's contact cells
    // and looks blocks up here instead of hardcoding per-hazard checks.
    public static class BlockHazards
    {
        // Balance constants — canon fixes which blocks are hazardous; the damage amounts and
        // rates are tunable.
        public const int ThornbrushDamage = 1;
        public const float ThornbrushIntervalSeconds = 0.5f;
        public const int CampfireDamage = 2;
        public const float CampfireIntervalSeconds = 0.5f;
        public const int EmberflowDamage = 3;
        public const float EmberflowIntervalSeconds = 0.5f;

        // Emberflow source and flowing cells share one hazard id, so wading between them never
        // double-applies inside a single throttle window.
        static readonly BlockHazard EmberflowHazard = new(
            new HazardVolumeDefinition(
                "emberflow",
                new HazardDamage(EmberflowDamage, HazardDamageKind.Heat, "emberflow"),
                EmberflowIntervalSeconds),
            HazardContactCells.Feet | HazardContactCells.Head | HazardContactCells.GroundBelow);

        static readonly Dictionary<BlockId, BlockHazard> HazardForBlock = new()
        {
            {
                BlockRegistry.Thornbrush,
                new BlockHazard(
                    new HazardVolumeDefinition(
                        "thornbrush",
                        new HazardDamage(ThornbrushDamage, HazardDamageKind.Environmental, "thornbrush"),
                        ThornbrushIntervalSeconds),
                    HazardContactCells.Feet | HazardContactCells.Head)
            },
            {
                BlockRegistry.Campfire,
                new BlockHazard(
                    new HazardVolumeDefinition(
                        "campfire",
                        new HazardDamage(CampfireDamage, HazardDamageKind.Heat, "campfire"),
                        CampfireIntervalSeconds),
                    HazardContactCells.Feet | HazardContactCells.GroundBelow)
            },
            { BlockRegistry.Emberflow, EmberflowHazard },
            { BlockRegistry.EmberflowFlow, EmberflowHazard },
        };

        public static bool TryGetHazard(BlockId block, out BlockHazard hazard) =>
            HazardForBlock.TryGetValue(block, out hazard);
    }
}
