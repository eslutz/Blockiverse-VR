using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Blockiverse.Core
{
    /// <summary>One installed pack: its validated manifest plus where it lives.</summary>
    public sealed class BlockiverseTexturePackInfo
    {
        public BlockiverseTexturePackInfo(string packId, string directoryPath, BlockiverseTexturePackManifest manifest)
        {
            PackId = packId;
            DirectoryPath = directoryPath;
            Manifest = manifest;
        }

        public string PackId { get; }

        /// <summary>Absolute path. Deliberately never logged — see the note on logging below.</summary>
        public string DirectoryPath { get; }

        public BlockiverseTexturePackManifest Manifest { get; }

        public string Token => BlockiverseTextureSelection.ForPack(PackId);
    }

    /// <summary>
    /// Finds user-supplied texture packs on disk and answers whether a token can be honoured.
    ///
    /// Packs live in <c>&lt;persistentDataPath&gt;/TexturePacks/&lt;pack_id&gt;/</c>, which on Quest is
    /// <c>/sdcard/Android/data/&lt;package&gt;/files/TexturePacks</c> — reachable over MTP and
    /// `hzdb push` with no root, which is the only realistic way a headset user installs one.
    ///
    /// This type returns BYTES, never a <c>Texture2D</c>. Decoding is a rendering concern and lives
    /// in Gameplay; keeping it out means the whole library is testable headlessly and the dedicated
    /// server (which excludes Gameplay) can still reference Core.
    ///
    /// EVERYTHING HERE IS UNTRUSTED INPUT. The pack root is a directory the player writes to, so
    /// every read is bounded by an explicit limit and every failure is a logged fallback rather
    /// than an exception escaping into a world load.
    ///
    /// Logging never includes a path — only the pack id, which
    /// <see cref="BlockiverseTextureSelection"/> has already constrained to [a-z0-9_] and is
    /// therefore safe to interpolate. This matches the rule the save service is already held to by
    /// test (log lines must not leak filesystem paths).
    /// </summary>
    public static class BlockiverseTexturePackLibrary
    {
        public const string PackRootDirectoryName = "TexturePacks";
        public const string ManifestFileName = "blockiverse-pack.json";
        public const string TileDirectoryName = "blocks";
        public const string TileFileExtension = ".png";

        /// <summary>Cap on directories examined in one scan, so a pathological pack root cannot
        /// stall a world load.</summary>
        public const int MaxScannedPackDirectories = 64;

        public const int MaxManifestBytes = 64 * 1024;
        public const int MaxTileFiles = 256;
        public const int MaxTilePngBytes = 4 * 1024 * 1024;

        static string packRootOverride;

        /// <summary>
        /// Where packs are read from. Computed once because
        /// <see cref="Application.persistentDataPath"/> is fixed for the life of the process and
        /// is not cheap.
        /// </summary>
        public static string PackRoot =>
            packRootOverride ?? Path.Combine(Application.persistentDataPath, PackRootDirectoryName);

        /// <summary>
        /// Points the library at a temporary directory. Mirrors
        /// <c>BlockiverseTrace.SetDiagnosticsDirectoryForTesting</c> so that no test ever writes
        /// into the real <see cref="Application.persistentDataPath"/>.
        /// </summary>
        public static void SetPackRootForTesting(string path) => packRootOverride = path;

        public static void ResetPackRootForTesting() => packRootOverride = null;

        /// <summary>
        /// Creates the pack directory if it does not exist, so a player can find where to put a
        /// pack before they own one. Best effort: failing to create it is not worth interrupting
        /// anything, because every read below tolerates the directory being absent.
        /// </summary>
        public static void EnsurePackRootExists()
        {
            try
            {
                if (!Directory.Exists(PackRoot))
                    Directory.CreateDirectory(PackRoot);
            }
            catch (Exception exception) when (IsExpectedIoFailure(exception))
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Assets,
                    $"Could not create the texture pack directory: {exception.Message}");
            }
        }

        /// <summary>
        /// Every installed pack with a valid manifest, ordered by display name so the settings
        /// screen is stable between scans. Packs that fail validation are logged and omitted
        /// rather than throwing — one broken pack must not hide the others.
        /// </summary>
        public static IReadOnlyList<BlockiverseTexturePackInfo> Installed()
        {
            var found = new List<BlockiverseTexturePackInfo>();

            string root = PackRoot;
            if (!SafeDirectoryExists(root))
                return found;

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(root);
            }
            catch (Exception exception) when (IsExpectedIoFailure(exception))
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Assets,
                    $"Could not list texture packs: {exception.Message}");
                return found;
            }

            int scanned = 0;
            foreach (string directory in directories)
            {
                if (scanned >= MaxScannedPackDirectories)
                {
                    // Say what was dropped. A silent cap reads as "you have no more packs".
                    BlockiverseLog.Warning(
                        BlockiverseLogCategory.Assets,
                        $"Stopped scanning texture packs at {MaxScannedPackDirectories} directories; "
                        + $"{directories.Length - scanned} were not examined.");
                    break;
                }

                scanned++;

                string packId = Path.GetFileName(directory);
                if (!BlockiverseTextureSelection.IsValidPackId(packId))
                    continue;   // Not a pack folder at all; nothing to report.

                if (TryReadManifest(packId, directory, out BlockiverseTexturePackManifest manifest, out string error))
                {
                    found.Add(new BlockiverseTexturePackInfo(packId.ToLowerInvariant(), directory, manifest));
                }
                else
                {
                    BlockiverseLog.Warning(
                        BlockiverseLogCategory.Assets,
                        $"Ignoring texture pack '{packId}': {error}");
                }
            }

            found.Sort(static (left, right) => string.Compare(
                left.Manifest.displayName, right.Manifest.displayName, StringComparison.CurrentCultureIgnoreCase));

            return found;
        }

        /// <summary>
        /// Decides what a token means right now.
        ///
        /// A built-in always resolves. A pack token resolves against the filesystem, and the two
        /// ways that can fail are kept distinct because they need different messages and different
        /// player actions: <see cref="BlockiverseTextureSelectionStatus.PackMissing"/> means
        /// reinstall it, <see cref="BlockiverseTextureSelectionStatus.PackInvalid"/> means repair
        /// it.
        ///
        /// In every failure case the REQUESTED token is preserved on the result. Callers must
        /// write that back to the save rather than the effective one, or a temporarily absent pack
        /// becomes a permanently lost selection.
        /// </summary>
        public static BlockiverseTextureResolution Resolve(string token)
        {
            string normalized = BlockiverseTextureSelection.NormalizeToken(token);

            if (!BlockiverseTextureSelection.TryGetPackId(normalized, out string packId))
                return BlockiverseTextureResolution.BuiltIn(normalized);

            string directory = Path.Combine(PackRoot, packId);
            if (!SafeDirectoryExists(directory))
                return BlockiverseTextureResolution.PackMissing(normalized, packId);

            return TryReadManifest(packId, directory, out BlockiverseTexturePackManifest _, out string error)
                ? BlockiverseTextureResolution.PackInstalled(normalized, packId)
                : BlockiverseTextureResolution.PackInvalid(normalized, packId, error);
        }

        /// <summary>The validated manifest for an installed pack, or null.</summary>
        public static BlockiverseTexturePackManifest TryGetManifest(string packId)
        {
            if (!BlockiverseTextureSelection.IsValidPackId(packId))
                return null;

            string directory = Path.Combine(PackRoot, packId);
            return TryReadManifest(packId, directory, out BlockiverseTexturePackManifest manifest, out string _)
                ? manifest
                : null;
        }

        /// <summary>
        /// The canonical tile names a pack supplies, lowercased and without extension. Bounded by
        /// <see cref="MaxTileFiles"/>; the caller decides which of them it recognises.
        /// </summary>
        public static IReadOnlyList<string> ListTileNames(string packId)
        {
            var names = new List<string>();

            if (!BlockiverseTextureSelection.IsValidPackId(packId))
                return names;

            string tileDirectory = Path.Combine(PackRoot, packId, TileDirectoryName);
            if (!SafeDirectoryExists(tileDirectory))
                return names;

            string[] files;
            try
            {
                // TopDirectoryOnly: a pack's tiles are a flat set, and refusing to recurse keeps a
                // nested tree from turning one scan into an unbounded walk.
                files = Directory.GetFiles(tileDirectory, "*" + TileFileExtension, SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (IsExpectedIoFailure(exception))
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Assets,
                    $"Could not list tiles in texture pack '{packId}': {exception.Message}");
                return names;
            }

            if (files.Length > MaxTileFiles)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Assets,
                    $"Texture pack '{packId}' contains {files.Length} tiles; only the first {MaxTileFiles} are read.");
            }

            int limit = Math.Min(files.Length, MaxTileFiles);
            for (int i = 0; i < limit; i++)
                names.Add(Path.GetFileNameWithoutExtension(files[i]).ToLowerInvariant());

            return names;
        }

        /// <summary>
        /// Reads one tile's PNG bytes. False when absent, oversized, or unreadable — all of which
        /// are per-tile failures that leave the rest of the pack usable.
        /// </summary>
        public static bool TryReadTileBytes(string packId, string tileName, out byte[] bytes)
        {
            bytes = null;

            if (!BlockiverseTextureSelection.IsValidPackId(packId) || !IsSafeTileName(tileName))
                return false;

            string path = Path.Combine(PackRoot, packId, TileDirectoryName, tileName + TileFileExtension);

            try
            {
                if (!File.Exists(path))
                    return false;

                var info = new FileInfo(path);
                if (info.Length > MaxTilePngBytes || info.Length == 0)
                {
                    BlockiverseLog.Warning(
                        BlockiverseLogCategory.Assets,
                        $"Skipping tile '{tileName}' in texture pack '{packId}': size is out of range.");
                    return false;
                }

                bytes = File.ReadAllBytes(path);
                return true;
            }
            catch (Exception exception) when (IsExpectedIoFailure(exception))
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Assets,
                    $"Could not read tile '{tileName}' in texture pack '{packId}': {exception.Message}");
                return false;
            }
        }

        static bool TryReadManifest(
            string packId,
            string directory,
            out BlockiverseTexturePackManifest manifest,
            out string error)
        {
            manifest = null;
            string path = Path.Combine(directory, ManifestFileName);

            try
            {
                if (!File.Exists(path))
                {
                    error = $"missing {ManifestFileName}";
                    return false;
                }

                var info = new FileInfo(path);
                if (info.Length > MaxManifestBytes)
                {
                    error = $"{ManifestFileName} is larger than {MaxManifestBytes / 1024} KiB";
                    return false;
                }

                string json = File.ReadAllText(path);
                manifest = JsonUtility.FromJson<BlockiverseTexturePackManifest>(json);
            }
            catch (ArgumentException)
            {
                // JsonUtility throws ArgumentException on malformed JSON. Caught separately from
                // IO failure because it means "this pack is broken", not "the disk is unhappy".
                error = $"{ManifestFileName} is not valid JSON";
                return false;
            }
            catch (Exception exception) when (IsExpectedIoFailure(exception))
            {
                error = $"could not read {ManifestFileName} ({exception.Message})";
                return false;
            }

            if (manifest == null)
            {
                error = $"{ManifestFileName} is empty";
                return false;
            }

            return manifest.TryValidate(packId, out error);
        }

        // A tile name reaches Path.Combine, so it is held to the same character rule as a pack id:
        // no separators, no dots, no traversal. Rejecting here is what lets the caller pass a name
        // straight through from a table without sanitising it again.
        static bool IsSafeTileName(string tileName)
        {
            if (string.IsNullOrEmpty(tileName) || tileName.Length > 64)
                return false;

            foreach (char character in tileName)
            {
                bool allowed = (character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9')
                    || character == '_';

                if (!allowed)
                    return false;
            }

            return true;
        }

        static bool SafeDirectoryExists(string path)
        {
            try
            {
                return Directory.Exists(path);
            }
            catch (Exception exception) when (IsExpectedIoFailure(exception))
            {
                return false;
            }
        }

        static bool IsExpectedIoFailure(Exception exception) =>
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is ArgumentException
            || exception is NotSupportedException;
    }
}
