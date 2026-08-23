using System.Collections.Generic;
using Blockiverse.Editor;
using NUnit.Framework;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Blockiverse.Tests.EditMode
{
    // Plan Phase 2 round-trip proof: every key in the compiled English dictionary resolves
    // BYTE-IDENTICALLY through the new table. This is the parity contract for the shim — if it
    // holds, the ten literal-English test files cannot tell the storage moved.
    //
    // Compares against the SAME reflection read the migrator used, so there is no second source
    // to drift. After Phase 3b deletes the dictionary, this pivots to the frozen JSON snapshot.
    public sealed class LocalizationTableMigrationEditModeTests
    {
        static Locale English()
        {
            // S1 finding: initialization must be forced; no selection pass runs in EditMode.
            LocalizationSettings.InitializationOperation.WaitForCompletion();
            Locale en = LocalizationSettings.AvailableLocales.GetLocale("en");
            Assert.That(en, Is.Not.Null, "en locale not registered");
            return en;
        }

        [Test]
        public void EveryDictionaryEntryResolvesIdenticallyThroughTheTable()
        {
            IReadOnlyDictionary<string, string> english =
                BlockiverseLocalizationTableMigrator.ReadEnglishDictionary();
            Locale en = English();

            Assert.That(english.Count, Is.GreaterThanOrEqualTo(238),
                "Dictionary shrank unexpectedly — migration source is not what was surveyed.");

            var mismatches = new List<string>();

            foreach (KeyValuePair<string, string> kv in english)
            {
                string resolved = LocalizationSettings.StringDatabase
                    .GetLocalizedStringAsync(
                        BlockiverseLocalizationTableMigrator.TableName, kv.Key, en)
                    .WaitForCompletion();

                if (resolved != kv.Value)
                    mismatches.Add($"{kv.Key}: expected '{kv.Value}', table gave '{resolved}'");
            }

            Assert.That(mismatches, Is.Empty,
                $"{mismatches.Count} of {english.Count} entries drifted:\n" +
                string.Join("\n", mismatches.GetRange(0, System.Math.Min(mismatches.Count, 12))));
        }

        // The 50 placeholder patterns must FORMAT identically, not just store identically —
        // Smart-off entries route through string.Format semantics (S6). Formatting every
        // pattern with a fixed arg set exercises the whole pipeline the shim will rely on.
        [Test]
        public void PlaceholderPatternsFormatIdenticallyToStringFormat()
        {
            IReadOnlyDictionary<string, string> english =
                BlockiverseLocalizationTableMigrator.ReadEnglishDictionary();
            Locale en = English();

            object[] sampleArgs = { 7, "Sample", 3, "Deep", 42, "End" };
            int patternCount = 0;
            var mismatches = new List<string>();

            foreach (KeyValuePair<string, string> kv in english)
            {
                if (!kv.Value.Contains("{0}"))
                    continue;

                patternCount++;

                string viaTable = LocalizationSettings.StringDatabase
                    .GetLocalizedStringAsync(
                        BlockiverseLocalizationTableMigrator.TableName, kv.Key, en,
                        arguments: sampleArgs)
                    .WaitForCompletion();
                string viaStringFormat = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture, kv.Value, sampleArgs);

                if (viaTable != viaStringFormat)
                    mismatches.Add($"{kv.Key}: '{viaStringFormat}' vs '{viaTable}'");
            }

            Assert.That(patternCount, Is.GreaterThanOrEqualTo(50),
                "Fewer placeholder patterns than surveyed — positive control failed.");
            Assert.That(mismatches, Is.Empty, string.Join("\n", mismatches));
        }

        // The one deliberate addition beyond the dictionary.
        [Test]
        public void HandcraftEntryExists()
        {
            string resolved = LocalizationSettings.StringDatabase
                .GetLocalizedStringAsync(
                    BlockiverseLocalizationTableMigrator.TableName,
                    "ui.value.crafting_station.none", English())
                .WaitForCompletion();

            Assert.That(resolved, Is.EqualTo("Handcraft"));
        }
    }
}
