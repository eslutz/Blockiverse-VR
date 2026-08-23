using System.IO;
using Blockiverse.Core;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace Blockiverse.Editor
{
    // SPIKE (plan Phase 0) — throwaway scaffolding for the com.unity.localization adoption gates.
    // Creates the minimum assets the S1/S6 EditMode tests and the S3 device check need:
    // an English locale, LocalizationSettings, and a 3-entry string table covering a plain
    // string, a positional {0} pattern, and a named Smart pattern.
    public static class LocalizationSpikeScaffold
    {
        const string RootFolder = "Assets/Blockiverse/Localization";
        const string SettingsPath = RootFolder + "/LocalizationSettings.asset";
        const string LocalePath = RootFolder + "/Locales/en.asset";
        const string TablesFolder = RootFolder + "/Tables";
        public const string TableName = "UI_Spike";

        public const string PlainKey = "spike.plain";
        public const string PlainValue = "Hello from the table";
        public const string PositionalKey = "spike.positional";
        public const string PositionalPattern = "Slots {0}-{1} / {2}";
        public const string SmartKey = "spike.smart";
        public const string SmartPattern = "{count} items in {place}";

        [MenuItem("Blockiverse/Localization/Spike Scaffold")]
        public static void Run()
        {
            Directory.CreateDirectory(RootFolder);
            Directory.CreateDirectory(RootFolder + "/Locales");
            Directory.CreateDirectory(TablesFolder);
            AssetDatabase.Refresh();

            // Settings first, so locale/table registration lands in it.
            LocalizationSettings settings =
                AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsPath);

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                settings.name = "LocalizationSettings";
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            LocalizationEditorSettings.ActiveLocalizationSettings = settings;

            Locale english = AssetDatabase.LoadAssetAtPath<Locale>(LocalePath);

            if (english == null)
            {
                english = Locale.CreateLocale(SystemLanguage.English);
                AssetDatabase.CreateAsset(english, LocalePath);
                LocalizationEditorSettings.AddLocale(english);
            }

            // Edit Mode has no locale-selection pass, so the no-argument resolve path needs the
            // project locale as its fallback. Whether that suffices (vs. passing the locale
            // explicitly) is exactly what gates S1's two test variants.
            LocalizationSettings.ProjectLocale = english;
            EditorUtility.SetDirty(settings);

            StringTableCollection collection =
                LocalizationEditorSettings.GetStringTableCollection(TableName);

            if (collection == null)
                collection = LocalizationEditorSettings.CreateStringTableCollection(
                    TableName, TablesFolder);

            var table = (StringTable)collection.GetTable(english.Identifier);

            SetEntry(table, PlainKey, PlainValue, smart: false);
            SetEntry(table, PositionalKey, PositionalPattern, smart: false);
            SetEntry(table, SmartKey, SmartPattern, smart: true);

            EditorUtility.SetDirty(table);
            EditorUtility.SetDirty(table.SharedData);
            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssets();

            BlockiverseLog.Info(
                BlockiverseLogCategory.Bootstrap,
                $"Localization spike scaffold complete: locale en, table {TableName}, 3 entries.");
        }

        static void SetEntry(StringTable table, string key, string value, bool smart)
        {
            StringTableEntry entry = table.GetEntry(key) ?? table.AddEntry(key, value);
            entry.Value = value;
            entry.IsSmart = smart;
        }
    }
}
