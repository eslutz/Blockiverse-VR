using System;
using System.Collections.Generic;
using System.Linq;
using Blockiverse.Voxel;

namespace Blockiverse.Gameplay
{
    public enum CreativeCatalogCategory
    {
        Terrain   = 0,
        Stone     = 1,
        DeepRock  = 2,
        Wood      = 3,
        Foliage   = 4,
        Crops     = 5,
        Saplings  = 6,
        Ores      = 7,
        Minerals  = 8,
        Crafted   = 9,
        Lighting  = 10,
        Storage   = 11,
        Stations  = 12,
    }

    public sealed class CreativeCatalogEntry
    {
        public CreativeCatalogEntry(BlockId blockId, CreativeCatalogCategory category)
        {
            BlockId = blockId;
            Category = category;
        }

        public BlockId BlockId { get; }
        public CreativeCatalogCategory Category { get; }
    }

    public sealed class CreativeCatalog
    {
        readonly List<CreativeCatalogEntry> entries;

        CreativeCatalog(List<CreativeCatalogEntry> entries)
        {
            this.entries = entries;
        }

        public IReadOnlyList<CreativeCatalogEntry> All => entries;

        public IEnumerable<CreativeCatalogEntry> InCategory(CreativeCatalogCategory category)
        {
            return entries.Where(e => e.Category == category);
        }

        public static CreativeCatalog CreateDefault(BlockRegistry blockRegistry = null)
        {
            blockRegistry ??= BlockRegistry.CreateDefault();

            var list = new List<CreativeCatalogEntry>
            {
                // Terrain
                new(BlockRegistry.MeadowTurf,     CreativeCatalogCategory.Terrain),
                new(BlockRegistry.LooseLoam,       CreativeCatalogCategory.Terrain),
                new(BlockRegistry.DryTurf,         CreativeCatalogCategory.Terrain),
                new(BlockRegistry.SnowcapTurf,     CreativeCatalogCategory.Terrain),
                new(BlockRegistry.Rootsoil,        CreativeCatalogCategory.Terrain),
                new(BlockRegistry.Claybed,         CreativeCatalogCategory.Terrain),
                new(BlockRegistry.RiverSilt,       CreativeCatalogCategory.Terrain),
                new(BlockRegistry.PaleSand,        CreativeCatalogCategory.Terrain),
                new(BlockRegistry.ShingleGravel,   CreativeCatalogCategory.Terrain),
                new(BlockRegistry.TendedSoil,      CreativeCatalogCategory.Terrain),

                // Stone
                new(BlockRegistry.Graystone,       CreativeCatalogCategory.Stone),
                new(BlockRegistry.DarkSlate,       CreativeCatalogCategory.Stone),
                new(BlockRegistry.WarmGranite,     CreativeCatalogCategory.Stone),
                new(BlockRegistry.WhiteLimestone,  CreativeCatalogCategory.Stone),
                new(BlockRegistry.BlackBasalt,     CreativeCatalogCategory.Stone),

                // DeepRock
                new(BlockRegistry.Worldroot,       CreativeCatalogCategory.DeepRock),
                new(BlockRegistry.Deepmantle,      CreativeCatalogCategory.DeepRock),
                new(BlockRegistry.Snowpack,        CreativeCatalogCategory.DeepRock),
                new(BlockRegistry.Frostglass,      CreativeCatalogCategory.DeepRock),

                // Wood
                new(BlockRegistry.BranchwoodLog,   CreativeCatalogCategory.Wood),
                new(BlockRegistry.WorkPlank,       CreativeCatalogCategory.Wood),
                new(BlockRegistry.ResinKnot,       CreativeCatalogCategory.Wood),

                // Foliage
                new(BlockRegistry.Leafmoss,        CreativeCatalogCategory.Foliage),
                new(BlockRegistry.Thornbrush,      CreativeCatalogCategory.Foliage),
                new(BlockRegistry.Reedgrass,       CreativeCatalogCategory.Foliage),
                new(BlockRegistry.Reedgrass_S1,    CreativeCatalogCategory.Foliage),

                // Crops
                new(BlockRegistry.GrainStalk,      CreativeCatalogCategory.Crops),
                new(BlockRegistry.GrainStalk_S1,   CreativeCatalogCategory.Crops),
                new(BlockRegistry.GrainStalk_S2,   CreativeCatalogCategory.Crops),
                new(BlockRegistry.Berrybush,       CreativeCatalogCategory.Crops),
                new(BlockRegistry.Berrybush_S1,    CreativeCatalogCategory.Crops),
                new(BlockRegistry.Berrybush_S2,    CreativeCatalogCategory.Crops),

                // Saplings
                new(BlockRegistry.Sapling,         CreativeCatalogCategory.Saplings),
                new(BlockRegistry.Sapling_S1,      CreativeCatalogCategory.Saplings),
                new(BlockRegistry.Sapling_S2,      CreativeCatalogCategory.Saplings),

                // Ores
                new(BlockRegistry.LumenQuartzCluster, CreativeCatalogCategory.Ores),
                new(BlockRegistry.EmbercoalSeam,    CreativeCatalogCategory.Ores),
                new(BlockRegistry.RosycopperBloom,  CreativeCatalogCategory.Ores),
                new(BlockRegistry.RustcoreOre,      CreativeCatalogCategory.Ores),
                new(BlockRegistry.UmbraliteNode,    CreativeCatalogCategory.Ores),
                new(BlockRegistry.StaropalGeode,    CreativeCatalogCategory.Ores),

                // Minerals
                new(BlockRegistry.SurfacePebbles,   CreativeCatalogCategory.Minerals),
                new(BlockRegistry.FlintyShingle,    CreativeCatalogCategory.Minerals),
                new(BlockRegistry.PaletinThread,    CreativeCatalogCategory.Minerals),
                new(BlockRegistry.SunmetalFleck,    CreativeCatalogCategory.Minerals),
                new(BlockRegistry.NiterstonePocket, CreativeCatalogCategory.Minerals),
                new(BlockRegistry.BrightsaltCrust,  CreativeCatalogCategory.Minerals),
                new(BlockRegistry.ShellgritBed,     CreativeCatalogCategory.Minerals),

                // Crafted
                new(BlockRegistry.CutstoneBlock,    CreativeCatalogCategory.Crafted),
                new(BlockRegistry.FiredBrickBlock,  CreativeCatalogCategory.Crafted),
                new(BlockRegistry.ClearpaneGlass,   CreativeCatalogCategory.Crafted),

                // Lighting
                new(BlockRegistry.Glowwick,         CreativeCatalogCategory.Lighting),
                new(BlockRegistry.LumenLamp,        CreativeCatalogCategory.Lighting),
                new(BlockRegistry.SparkFlare,       CreativeCatalogCategory.Lighting),

                // Storage
                new(BlockRegistry.StorageCrate,     CreativeCatalogCategory.Storage),

                // Stations
                new(BlockRegistry.BuildTable,       CreativeCatalogCategory.Stations),
                new(BlockRegistry.Campfire,         CreativeCatalogCategory.Stations),
                new(BlockRegistry.ClayKiln,         CreativeCatalogCategory.Stations),
                new(BlockRegistry.BellowsForge,     CreativeCatalogCategory.Stations),
                new(BlockRegistry.PrepBoard,        CreativeCatalogCategory.Stations),
                new(BlockRegistry.MendBench,        CreativeCatalogCategory.Stations),
            };

            foreach (CreativeCatalogEntry entry in list)
                blockRegistry.Get(entry.BlockId);

            return new CreativeCatalog(list);
        }
    }
}
