using System;
using System.Collections.Generic;
using System.IO;
using Blockiverse.Core;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // Scanning, manifest validation, and resolving a token against what is actually installed.
    //
    // The load-bearing assertion in here is that a MISSING pack keeps its requested token while
    // falling back for rendering. Everything else is guarding untrusted input.
    public sealed class TexturePackLibraryEditModeTests
    {
        string packRoot;
        CapturingLogSink logSink;

        [SetUp]
        public void SetUp()
        {
            packRoot = Path.Combine(Path.GetTempPath(), "blockiverse-texture-packs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(packRoot);
            BlockiverseTexturePackLibrary.SetPackRootForTesting(packRoot);

            logSink = new CapturingLogSink();
            BlockiverseLog.SetSinkForTesting(logSink);
        }

        [TearDown]
        public void TearDown()
        {
            BlockiverseLog.ResetSinkForTesting();
            BlockiverseTexturePackLibrary.ResetPackRootForTesting();

            if (!string.IsNullOrEmpty(packRoot) && Directory.Exists(packRoot))
                Directory.Delete(packRoot, recursive: true);
        }

        // ── fixtures ────────────────────────────────────────────────────────

        string WritePack(string folderName, string manifestJson, params string[] tileNames)
        {
            string directory = Path.Combine(packRoot, folderName);
            Directory.CreateDirectory(directory);

            if (manifestJson != null)
                File.WriteAllText(Path.Combine(directory, BlockiverseTexturePackLibrary.ManifestFileName), manifestJson);

            if (tileNames.Length > 0)
            {
                string tiles = Path.Combine(directory, BlockiverseTexturePackLibrary.TileDirectoryName);
                Directory.CreateDirectory(tiles);
                foreach (string tile in tileNames)
                    File.WriteAllBytes(Path.Combine(tiles, tile + ".png"), MakeTinyPng());
            }

            return directory;
        }

        static string ManifestJson(
            string packId = "mossy_stones",
            int formatVersion = 1,
            string displayName = "Mossy Stones",
            int tilePixels = 32,
            string baseTextureSet = "enhanced",
            string extra = "")
        {
            return "{"
                 + $"\"formatVersion\":{formatVersion},"
                 + $"\"packId\":\"{packId}\","
                 + $"\"displayName\":\"{displayName}\","
                 + $"\"tilePixels\":{tilePixels},"
                 + $"\"baseTextureSet\":\"{baseTextureSet}\""
                 + extra
                 + "}";
        }

        static byte[] MakeTinyPng()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            try
            {
                byte[] png = texture.EncodeToPNG();
                return png ?? new byte[] { 0x89, 0x50, 0x4E, 0x47 };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // ── resolution ──────────────────────────────────────────────────────

        [Test]
        public void AnInstalledPackResolvesAsInstalled()
        {
            WritePack("mossy_stones", ManifestJson());

            BlockiverseTextureResolution resolution = BlockiverseTexturePackLibrary.Resolve("pack:mossy_stones");

            Assert.That(resolution.Status, Is.EqualTo(BlockiverseTextureSelectionStatus.PackInstalled));
            Assert.That(resolution.EffectiveToken, Is.EqualTo("pack:mossy_stones"));
            Assert.That(resolution.FellBack, Is.False);
        }

        [Test]
        public void AMissingPackKeepsItsRequestedTokenWhileFallingBackForRendering()
        {
            // THE assertion this whole type exists for. The effective token falls back so something
            // can be drawn, but the requested token survives so the caller can write it back to the
            // save. Lose that and the next autosave erases a pack the player merely moved.
            BlockiverseTextureResolution resolution = BlockiverseTexturePackLibrary.Resolve("pack:not_installed");

            Assert.That(resolution.Status, Is.EqualTo(BlockiverseTextureSelectionStatus.PackMissing));
            Assert.That(resolution.RequestedToken, Is.EqualTo("pack:not_installed"),
                "The requested token was lost; the player's selection would be overwritten on the next save.");
            Assert.That(resolution.RequestedPackId, Is.EqualTo("not_installed"),
                "The pack id was lost, so the player could not be told WHICH pack is missing.");
            Assert.That(resolution.EffectiveToken, Is.EqualTo(BlockTextureSetIds.Default));
            Assert.That(resolution.FellBack, Is.True);
        }

        [Test]
        public void APackDirectoryWithABrokenManifestResolvesAsInvalidNotMissing()
        {
            // Distinct from missing because the player's next action differs: repair, not reinstall.
            WritePack("broken_pack", "{ this is not json");

            BlockiverseTextureResolution resolution = BlockiverseTexturePackLibrary.Resolve("pack:broken_pack");

            Assert.That(resolution.Status, Is.EqualTo(BlockiverseTextureSelectionStatus.PackInvalid));
            Assert.That(resolution.RequestedPackId, Is.EqualTo("broken_pack"));
            Assert.That(resolution.FailureDetail, Is.Not.Null.And.Not.Empty);
            Assert.That(resolution.EffectiveToken, Is.EqualTo(BlockTextureSetIds.Default));
        }

        [Test]
        public void ABuiltInTokenResolvesWithoutTouchingTheFilesystem()
        {
            foreach (string id in BlockTextureSetIds.All)
            {
                BlockiverseTextureResolution resolution = BlockiverseTexturePackLibrary.Resolve(id);
                Assert.That(resolution.Status, Is.EqualTo(BlockiverseTextureSelectionStatus.BuiltIn), id);
                Assert.That(resolution.EffectiveToken, Is.EqualTo(id));
                Assert.That(resolution.FellBack, Is.False);
            }
        }

        [Test]
        public void AMalformedPackTokenResolvesAsABuiltInRatherThanProbingTheFilesystem()
        {
            // NormalizeToken already coerced it, so nothing ever tries to open `../../etc`.
            BlockiverseTextureResolution resolution = BlockiverseTexturePackLibrary.Resolve("pack:../../etc");

            Assert.That(resolution.Status, Is.EqualTo(BlockiverseTextureSelectionStatus.BuiltIn));
            Assert.That(resolution.EffectiveToken, Is.EqualTo(BlockTextureSetIds.Default));
        }

        // ── manifest validation ─────────────────────────────────────────────

        [Test]
        public void AManifestWhosePackIdDisagreesWithItsFolderIsRejected()
        {
            // The two must match so "is this installed?" is a directory probe rather than a scan of
            // every manifest, and so two folders cannot both claim one id.
            WritePack("folder_name", ManifestJson(packId: "different_id"));

            Assert.That(
                BlockiverseTexturePackLibrary.Resolve("pack:folder_name").Status,
                Is.EqualTo(BlockiverseTextureSelectionStatus.PackInvalid));
        }

        [TestCase(0, TestName = "missing formatVersion")]
        [TestCase(2, TestName = "future formatVersion")]
        public void AnUnsupportedFormatVersionIsRejected(int formatVersion)
        {
            WritePack("mossy_stones", ManifestJson(formatVersion: formatVersion));

            BlockiverseTextureResolution resolution = BlockiverseTexturePackLibrary.Resolve("pack:mossy_stones");
            Assert.That(resolution.Status, Is.EqualTo(BlockiverseTextureSelectionStatus.PackInvalid));
            Assert.That(resolution.FailureDetail, Does.Contain("formatVersion"));
        }

        [TestCase(128, TestName = "too large")]
        [TestCase(48, TestName = "not a supported size")]
        [TestCase(0, TestName = "missing")]
        public void AnUnsupportedTileSizeIsRejectedWithItsOwnMessage(int tilePixels)
        {
            WritePack("mossy_stones", ManifestJson(tilePixels: tilePixels));

            BlockiverseTextureResolution resolution = BlockiverseTexturePackLibrary.Resolve("pack:mossy_stones");
            Assert.That(resolution.Status, Is.EqualTo(BlockiverseTextureSelectionStatus.PackInvalid));
            Assert.That(resolution.FailureDetail, Does.Contain("tilePixels"));
        }

        [Test]
        public void AMissingDisplayNameIsRejected()
        {
            WritePack("mossy_stones", ManifestJson(displayName: ""));

            Assert.That(
                BlockiverseTexturePackLibrary.Resolve("pack:mossy_stones").FailureDetail,
                Does.Contain("displayName"));
        }

        [Test]
        public void AMissingManifestFileIsReportedAsInvalid()
        {
            WritePack("mossy_stones", manifestJson: null);

            Assert.That(
                BlockiverseTexturePackLibrary.Resolve("pack:mossy_stones").FailureDetail,
                Does.Contain(BlockiverseTexturePackLibrary.ManifestFileName));
        }

        [Test]
        public void AnUnknownBaseTextureSetCoercesInsteadOfFailingTheWholePack()
        {
            // Unlike a pack id, a built-in set id has four legal values and no information worth
            // preserving in a wrong one. Refusing a whole pack over its FALLBACK choice would be
            // disproportionate.
            WritePack("mossy_stones", ManifestJson(baseTextureSet: "not_a_set"));

            Assert.That(
                BlockiverseTexturePackLibrary.Resolve("pack:mossy_stones").Status,
                Is.EqualTo(BlockiverseTextureSelectionStatus.PackInstalled));
            Assert.That(
                BlockiverseTexturePackLibrary.TryGetManifest("mossy_stones").baseTextureSet,
                Is.EqualTo(BlockTextureSetIds.Default));
        }

        [Test]
        public void PackMetadataIsSanitisedRatherThanRenderedRaw()
        {
            // Pack metadata is user data shown verbatim in the UI. A newline would break the
            // layout and corrupt any log line quoting it.
            WritePack("mossy_stones", ManifestJson(displayName: "Mossy\\nStones"));

            BlockiverseTexturePackManifest manifest = BlockiverseTexturePackLibrary.TryGetManifest("mossy_stones");
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.displayName, Does.Not.Contain("\n"));
        }

        [Test]
        public void AnOverlongDisplayNameIsTruncatedRatherThanRejected()
        {
            WritePack("mossy_stones", ManifestJson(displayName: new string('x', 400)));

            BlockiverseTexturePackManifest manifest = BlockiverseTexturePackLibrary.TryGetManifest("mossy_stones");
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.displayName.Length,
                Is.LessThanOrEqualTo(BlockiverseTexturePackManifest.MaxDisplayNameLength));
        }

        // ── scanning ────────────────────────────────────────────────────────

        [Test]
        public void InstalledListsValidPacksAndOmitsBrokenOnes()
        {
            WritePack("alpha_pack", ManifestJson(packId: "alpha_pack", displayName: "Alpha"));
            WritePack("beta_pack", ManifestJson(packId: "beta_pack", displayName: "Beta"));
            WritePack("broken_pack", "{ nope");

            IReadOnlyList<BlockiverseTexturePackInfo> installed = BlockiverseTexturePackLibrary.Installed();

            Assert.That(installed.Count, Is.EqualTo(2), "One broken pack hid the working ones.");
            Assert.That(installed[0].Manifest.displayName, Is.EqualTo("Alpha"), "Not ordered by display name.");
            Assert.That(installed[1].Manifest.displayName, Is.EqualTo("Beta"));
        }

        [Test]
        public void ScanningAnAbsentPackRootYieldsNothingRatherThanThrowing()
        {
            Directory.Delete(packRoot, recursive: true);
            Assert.That(BlockiverseTexturePackLibrary.Installed(), Is.Empty);
        }

        [Test]
        public void ADirectoryWhoseNameIsNotAValidPackIdIsIgnoredSilently()
        {
            // Not a pack folder at all, so there is nothing to warn about.
            WritePack("Not A Pack!", ManifestJson());

            Assert.That(BlockiverseTexturePackLibrary.Installed(), Is.Empty);
            Assert.That(logSink.Messages, Is.Empty, "A non-pack directory should not produce a warning.");
        }

        [Test]
        public void TileNamesAreListedLowercasedAndWithoutExtension()
        {
            WritePack("mossy_stones", ManifestJson(), "meadow_turf", "graystone");

            IReadOnlyList<string> tiles = BlockiverseTexturePackLibrary.ListTileNames("mossy_stones");

            Assert.That(tiles, Is.EquivalentTo(new[] { "meadow_turf", "graystone" }));
        }

        [Test]
        public void ReadingATileThatDoesNotExistFailsQuietly()
        {
            WritePack("mossy_stones", ManifestJson());

            Assert.That(
                BlockiverseTexturePackLibrary.TryReadTileBytes("mossy_stones", "meadow_turf", out byte[] bytes),
                Is.False);
            Assert.That(bytes, Is.Null);
        }

        [Test]
        public void ATileNameContainingPathCharactersIsRefusedBeforeItReachesTheFilesystem()
        {
            WritePack("mossy_stones", ManifestJson());

            foreach (string hostile in new[] { "../../secret", "a/b", "a.b", "" })
            {
                Assert.That(
                    BlockiverseTexturePackLibrary.TryReadTileBytes("mossy_stones", hostile, out _),
                    Is.False,
                    $"'{hostile}' was not refused; a pack could read outside its own directory.");
            }
        }

        // ── logging hygiene ─────────────────────────────────────────────────

        [Test]
        public void WarningsNeverLeakAFilesystemPath()
        {
            // The save service is already held to this rule by test; the pack library reads from a
            // user-writable directory and must be held to it too.
            WritePack("broken_pack", "{ nope");
            BlockiverseTexturePackLibrary.Installed();
            BlockiverseTexturePackLibrary.TryReadTileBytes("broken_pack", "meadow_turf", out _);

            foreach (string message in logSink.Messages)
            {
                Assert.That(message, Does.Not.Contain(Path.GetTempPath()),
                    $"A log message leaked a filesystem path: {message}");
                Assert.That(message, Does.Not.Contain(packRoot));
            }
        }

        sealed class CapturingLogSink : IBlockiverseLogSink
        {
            public List<string> Messages { get; } = new();

            public void Log(BlockiverseLogEntry entry) => Messages.Add(entry.Message ?? string.Empty);
        }
    }
}
