using System;

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
        static readonly string[] WorldSizeOptions = { "small", "medium", "large", "infinite" };
        static readonly string[] WorldPresetOptions = { "survival_terrain", "flat_builder", "void_builder" };
        static readonly string[] StartingBiomeOptions =
        {
            "balanced", "meadow", "pinewild", "wetland", "drybrush", "dunes", "tundra", "highlands",
        };

        int gameModeIndex = 0;     // survival
        int difficultyIndex = 1;   // normal
        int worldSizeIndex = 0;    // small
        int worldPresetIndex = 0;  // survival_terrain
        int startingBiomeIndex = 0; // balanced

        public NewWorldConfig(string seedText = null)
        {
            SeedText = string.IsNullOrWhiteSpace(seedText) ? "0" : seedText.Trim();
        }

        public string Name { get; private set; } = DefaultName;
        public string SeedText { get; private set; }
        public bool ExperimentalRulesEnabled { get; private set; }

        public string GameMode => GameModeOptions[gameModeIndex];
        public string Difficulty => DifficultyOptions[difficultyIndex];
        public string WorldSize => WorldSizeOptions[worldSizeIndex];
        public string WorldPreset => WorldPresetOptions[worldPresetIndex];
        public string StartingBiome => StartingBiomeOptions[startingBiomeIndex];

        // Numeric seed used by generation. A purely numeric seed is taken as-is; any other text is
        // hashed deterministically so text seeds are reproducible (§6.3 hashSeed).
        public ulong Seed => HashSeed(SeedText);

        public void SetName(string name) => Name = name ?? string.Empty;
        public void SetSeed(string seedText) => SeedText = string.IsNullOrWhiteSpace(seedText) ? "0" : seedText.Trim();

        public void RandomizeSeed(Func<ulong> randomSource = null)
        {
            ulong value = randomSource?.Invoke() ?? unchecked((ulong)Guid.NewGuid().GetHashCode() << 32 | (uint)Environment.TickCount);
            SeedText = value.ToString();
        }

        public void CycleGameMode() => gameModeIndex = Next(gameModeIndex, GameModeOptions.Length);
        public void CycleDifficulty() => difficultyIndex = Next(difficultyIndex, DifficultyOptions.Length);
        public void CycleWorldSize() => worldSizeIndex = Next(worldSizeIndex, WorldSizeOptions.Length);
        public void CycleWorldPreset() => worldPresetIndex = Next(worldPresetIndex, WorldPresetOptions.Length);
        public void CycleStartingBiome() => startingBiomeIndex = Next(startingBiomeIndex, StartingBiomeOptions.Length);
        public void ToggleExperimentalRules() => ExperimentalRulesEnabled = !ExperimentalRulesEnabled;

        // Validates settings before world creation (§6.3): the name must be non-empty after trimming.
        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                error = "World name cannot be empty.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool IsCreativeMode => string.Equals(GameMode, "creative", StringComparison.OrdinalIgnoreCase);

        static int Next(int index, int length) => (index + 1) % length;

        public static ulong HashSeed(string seedText)
        {
            if (string.IsNullOrWhiteSpace(seedText))
                return 0;

            string trimmed = seedText.Trim();
            if (ulong.TryParse(trimmed, out ulong numeric))
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
