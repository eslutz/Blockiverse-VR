using System;

namespace Blockiverse.MetaPlatform
{
    public readonly struct BlockiverseUserAgeCategoryState : IEquatable<BlockiverseUserAgeCategoryState>
    {
        public BlockiverseUserAgeCategoryState(
            BlockiverseUserAgeCategory category,
            BlockiverseUserAgeCategorySource source,
            long unixSeconds,
            string message)
        {
            Category = category;
            Source = source;
            UnixSeconds = unixSeconds;
            Message = message ?? string.Empty;
        }

        public BlockiverseUserAgeCategory Category { get; }
        public BlockiverseUserAgeCategorySource Source { get; }
        public long UnixSeconds { get; }
        public string Message { get; }

        public bool HasKnownCategory =>
            Category == BlockiverseUserAgeCategory.Child ||
            Category == BlockiverseUserAgeCategory.Teen ||
            Category == BlockiverseUserAgeCategory.Adult;

        public static BlockiverseUserAgeCategoryState Unknown(
            BlockiverseUserAgeCategorySource source,
            string message)
        {
            return new BlockiverseUserAgeCategoryState(
                BlockiverseUserAgeCategory.Unknown,
                source,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                message);
        }

        public bool Equals(BlockiverseUserAgeCategoryState other) =>
            Category == other.Category &&
            Source == other.Source &&
            UnixSeconds == other.UnixSeconds &&
            Message == other.Message;

        public override bool Equals(object obj) =>
            obj is BlockiverseUserAgeCategoryState other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Category, Source, UnixSeconds, Message);
    }
}
