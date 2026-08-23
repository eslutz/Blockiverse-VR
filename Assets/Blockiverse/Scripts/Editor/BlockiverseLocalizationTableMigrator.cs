using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Blockiverse.Core;
using Blockiverse.UI;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Tables;

namespace Blockiverse.Editor
{
    // Plan Phase 2: one-shot, idempotent migration of the compiled English dictionary in
    // BlockiverseLocalization into the "UI" String Table Collection, plus the frozen JSON
    // snapshot that the round-trip test pivots to once the dictionary is deleted (Phase 3b).
    //
    // The dictionary is private static by design and InternalsVisibleTo is forbidden project-wide
    // (CLAUDE.md), so this reads it via reflection — acceptable for a one-shot editor tool where
    // a rename fails loudly at run time rather than silently at a call site.
    public static class BlockiverseLocalizationTableMigrator
    {
        public const string TableName = "UI";
        const string TablesFolder = "Assets/Blockiverse/Localization/Tables";
        public const string SnapshotPath =
            "Assets/Blockiverse/Tests/EditMode/Fixtures/localization-en-snapshot.json";

        // The one deliberate addition beyond the dictionary: replaces the hardcoded
        // "Handcraft" special case in DisplayName(CraftingStation) (Phase 3 deletes the code path).
        const string HandcraftKey = "ui.value.crafting_station.none";
        const string HandcraftValue = "Handcraft";

        [MenuItem("Blockiverse/Localization/Migrate English Table")]
        public static void Run()
        {
            IReadOnlyDictionary<string, string> english = ReadEnglishDictionary();
            IReadOnlyDictionary<string, string> keyToConst = MapKeysToConstNames();

            StringTableCollection collection =
                LocalizationEditorSettings.GetStringTableCollection(TableName)
                ?? LocalizationEditorSettings.CreateStringTableCollection(TableName, TablesFolder);

            Locale english_locale = LocalizationEditorSettings.GetLocale("en");

            if (english_locale == null)
                throw new InvalidOperationException(
                    "en locale missing — run Blockiverse/Localization/Spike Scaffold first.");

            var table = (StringTable)collection.GetTable(english_locale.Identifier);

            // Deterministic order keeps the asset diff reviewable and re-runs byte-stable.
            var allEntries = english
                .Concat(new[] { new KeyValuePair<string, string>(HandcraftKey, HandcraftValue) })
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToList();

            foreach (KeyValuePair<string, string> kv in allEntries)
            {
                StringTableEntry entry = table.GetEntry(kv.Key) ?? table.AddEntry(kv.Key, kv.Value);
                entry.Value = kv.Value;

                // S6 finding: positional {0} patterns format correctly through the non-Smart
                // (string.Format) path, so migrated legacy entries stay Smart-off. New screens
                // author named Smart entries directly.
                entry.IsSmart = false;

                EnsureComment(entry.SharedEntry, BuildComment(kv.Key, kv.Value, keyToConst));
            }

            EditorUtility.SetDirty(table);
            EditorUtility.SetDirty(table.SharedData);
            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssets();

            WriteSnapshot(allEntries, BuildReverseWinners(english));

            BlockiverseLog.Info(
                BlockiverseLogCategory.Bootstrap,
                $"Localization migration complete: {allEntries.Count} entries in '{TableName}', " +
                $"snapshot at {SnapshotPath}.");
        }

        // Public so the round-trip test compares the table against the same source this tool
        // migrated from, with no second reflection implementation to drift.
        public static IReadOnlyDictionary<string, string> ReadEnglishDictionary()
        {
            FieldInfo field = typeof(BlockiverseLocalization).GetField(
                "English", BindingFlags.NonPublic | BindingFlags.Static);

            if (field == null)
                throw new InvalidOperationException(
                    "BlockiverseLocalization.English not found — the dictionary was renamed or " +
                    "already deleted (Phase 3b). This tool's migration job is done; use the snapshot.");

            return (Dictionary<string, string>)field.GetValue(null);
        }

        static IReadOnlyDictionary<string, string> MapKeysToConstNames()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (FieldInfo f in typeof(BlockiverseLocalization.Keys)
                         .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                         .Where(f => f.IsLiteral && f.FieldType == typeof(string)))
            {
                map[(string)f.GetRawConstantValue()] = f.Name;
            }

            return map;
        }

        static string BuildComment(string key, string value, IReadOnlyDictionary<string, string> keyToConst)
        {
            var sb = new StringBuilder();

            sb.Append(keyToConst.TryGetValue(key, out string constName)
                ? $"Keys.{constName}."
                : key == HandcraftKey
                    ? "Crafting without a station (DisplayName(CraftingStation.None))."
                    : "Raw-key entry (no Keys const).");

            // Placeholder inventory so a translator knows what may move. Enriched by hand later;
            // the enforcement test only requires non-empty.
            var placeholders = new List<string>();
            for (int i = 0; i <= 9; i++)
            {
                if (value.Contains("{" + i + "}"))
                    placeholders.Add("{" + i + "}");
            }

            if (placeholders.Count > 0)
                sb.Append(" Placeholders: ").Append(string.Join(", ", placeholders)).Append('.');

            return sb.ToString();
        }

        static void EnsureComment(SharedTableData.SharedTableEntry shared, string text)
        {
            Comment comment = shared.Metadata.GetMetadata<Comment>();

            if (comment == null)
            {
                comment = new Comment();
                shared.Metadata.AddMetadata(comment);
            }

            // Only overwrite tool-shaped comments; a hand-enriched comment survives re-runs.
            if (string.IsNullOrEmpty(comment.CommentText) ||
                comment.CommentText.StartsWith("Keys.", StringComparison.Ordinal) ||
                comment.CommentText.StartsWith("Raw-key", StringComparison.Ordinal))
            {
                comment.CommentText = text;
            }
        }

        // The byte-identical twin of the facade's BuildEnglishKeys: first-wins over the SAME
        // dictionary in the SAME enumeration order. Frozen into the snapshot because rebuilding
        // it from table enumeration (alphabetical) would silently flip two collision winners —
        // 'Return to Title' and 'Settings' — on the next bootstrapper regeneration.
        static IReadOnlyList<KeyValuePair<string, string>> BuildReverseWinners(
            IReadOnlyDictionary<string, string> english)
        {
            var winners = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> kv in english)
            {
                if (!string.IsNullOrWhiteSpace(kv.Value) && !winners.ContainsKey(kv.Value))
                    winners.Add(kv.Value, kv.Key);
            }

            return winners.ToList();
        }

        static void WriteSnapshot(
            IReadOnlyList<KeyValuePair<string, string>> entries,
            IReadOnlyList<KeyValuePair<string, string>> reverseWinners)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath));

            var sb = new StringBuilder();
            sb.Append("{\n  \"entries\": [\n");

            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append("    { \"key\": ").Append(Json(entries[i].Key))
                  .Append(", \"value\": ").Append(Json(entries[i].Value)).Append(" }");
                sb.Append(i < entries.Count - 1 ? ",\n" : "\n");
            }

            sb.Append("  ],\n  \"reverseWinners\": [\n");

            for (int i = 0; i < reverseWinners.Count; i++)
            {
                sb.Append("    { \"english\": ").Append(Json(reverseWinners[i].Key))
                  .Append(", \"key\": ").Append(Json(reverseWinners[i].Value)).Append(" }");
                sb.Append(i < reverseWinners.Count - 1 ? ",\n" : "\n");
            }

            sb.Append("  ]\n}\n");
            File.WriteAllText(SnapshotPath, sb.ToString());
            AssetDatabase.ImportAsset(SnapshotPath);
        }

        static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
