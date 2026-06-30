using System;
using System.Globalization;
using Blockiverse.Core;

namespace Blockiverse.UI
{
    // Pending configuration for the New World menu (voxel_survival_menus §6.3). Selector fields cycle
    // through canonical option lists; the seed accepts text or a number (text is hashed to a numeric
    // seed). This is a pure model so the menu component and tests share the same logic.
    public sealed class NewWorldConfig
    {
        public const string DefaultName = "New World";

        static readonly string[] GameModeOptions = { "survival", "creative" };
        static readonly string[] DifficultyOptions = { "easy", "normal", "hard" };
        static readonly string[] WorldSizeOptions = { "small", "medium" };
        static readonly string[] WorldPresetOptions = WorldPresetIds.MenuOptions;
        static readonly string[] TextureSetOptions = BlockTextureSetIds.MenuOptions;
        static readonly string[] StartingBiomeOptions =
        {
            "balanced", "meadow", "pinewild", "wetland", "drybrush", "dunes", "tundra", "highlands",
        };

        int gameModeIndex = 0;     // survival
        int difficultyIndex = 1;   // normal
        int worldSizeIndex = 0;    // small
        int worldPresetIndex = 0;  // WorldPresetIds.SurvivalTerrain
        int startingBiomeIndex = 0; // balanced
        int textureSetIndex = 0;   // BlockTextureSetIds.Enhanced

        public NewWorldConfig(string seedText = null)
        {
            SeedText = string.IsNullOrWhiteSpace(seedText) ? "0" : seedText.Trim();
        }

        public string Name { get; private set; } = DefaultName;
        public string SeedText { get; private set; }

        public string GameMode => GameModeOptions[gameModeIndex];
        public string Difficulty => DifficultyOptions[difficultyIndex];
        public string WorldSize => WorldSizeOptions[worldSizeIndex];
        public string WorldPreset => WorldPresetOptions[worldPresetIndex];
        public string StartingBiome => StartingBiomeOptions[startingBiomeIndex];
        public string TextureSet => TextureSetOptions[textureSetIndex];

        // Numeric seed used by generation. A purely numeric seed is taken as-is; any other text is
        // hashed deterministically so text seeds are reproducible (§6.3 hashSeed).
        public ulong Seed => HashSeed(SeedText);

        public void SetName(string name) => Name = name ?? string.Empty;
        public void SetSeed(string seedText) => SeedText = string.IsNullOrWhiteSpace(seedText) ? "0" : seedText.Trim();

        public void RandomizeSeed(Func<ulong> randomSource = null)
        {
            ulong value = randomSource?.Invoke() ?? unchecked((ulong)Guid.NewGuid().GetHashCode() << 32 | (uint)Environment.TickCount);
            SeedText = value.ToString(CultureInfo.InvariantCulture);
        }

        public void CycleGameMode(bool forward = true) => gameModeIndex = Step(gameModeIndex, GameModeOptions.Length, forward);
        public void CycleDifficulty(bool forward = true) => difficultyIndex = Step(difficultyIndex, DifficultyOptions.Length, forward);
        public void CycleWorldSize(bool forward = true) => worldSizeIndex = Step(worldSizeIndex, WorldSizeOptions.Length, forward);
        public void CycleWorldPreset(bool forward = true) => worldPresetIndex = Step(worldPresetIndex, WorldPresetOptions.Length, forward);
        public void CycleStartingBiome(bool forward = true) => startingBiomeIndex = Step(startingBiomeIndex, StartingBiomeOptions.Length, forward);
        public void CycleTextureSet(bool forward = true) => textureSetIndex = Step(textureSetIndex, TextureSetOptions.Length, forward);

        // Validates settings before world creation (§6.3): the name must be non-empty after trimming.
        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                error = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusWorldNameEmpty);
                return false;
            }

            if (!IsCreativeMode && IsBuilderPreset(WorldPreset))
            {
                error = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.NewWorldSurvivalPresetUnsupported);
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool IsCreativeMode => string.Equals(GameMode, "creative", StringComparison.OrdinalIgnoreCase);

        static int Step(int index, int length, bool forward) =>
            forward ? (index + 1) % length : (index - 1 + length) % length;

        static bool IsBuilderPreset(string worldPreset) =>
            string.Equals(worldPreset, WorldPresetIds.FlatBuilder, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(worldPreset, WorldPresetIds.VoidBuilder, StringComparison.OrdinalIgnoreCase);

        public static ulong HashSeed(string seedText)
        {
            if (string.IsNullOrWhiteSpace(seedText))
                return 0;

            string trimmed = seedText.Trim();
            if (ulong.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out ulong numeric))
                return numeric;

            // FNV-1a 64-bit over the text so text seeds are stable across runs.
            const ulong offsetBasis = 14695981039346656037;
            const ulong prime = 1099511628211;
            ulong hash = offsetBasis;
            foreach (char c in trimmed)
            {
                hash ^= c;
                hash *= prime;
            }

            return hash;
        }
    }
}
