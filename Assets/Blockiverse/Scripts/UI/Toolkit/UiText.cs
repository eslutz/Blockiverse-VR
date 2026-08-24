using System;
using System.Collections.Generic;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace Blockiverse.UI.Toolkit
{
    // Plan Phase 4: the localization surface for UI Toolkit screens.
    //
    // Static labels do NOT use this class — they bind in UXML via the package's native
    // LocalizedString binding, which UI Builder authors and which updates on locale change by
    // itself. This class exists for DYNAMIC text only: counts, statuses, names resolved at
    // runtime.
    //
    // House rules, enforced by review and the enforcement suite:
    //  - New-screen table entries use NAMED Smart arguments ({count}, {host}) — positional {0}
    //    is the legacy dialect and appears only in migrated entries.
    //  - Quantities are passed as numerics (the locale formats them); identifiers — seeds,
    //    ports, addresses, coordinates — are pre-stringified invariant by the caller, because
    //    port 8080 must never render as "8,080".
    public static class UiText
    {
        const string TableName = "UI";

        static StringTable cachedTable;
        static bool localeHookInstalled;

        public static string Get(string key)
        {
            StringTableEntry entry = ResolveEntry(key);
            return entry?.Value ?? key;
        }

        // TRANSITIONAL (dies at the uGUI cutover): positional formatting over the migrated
        // legacy entries, byte-parity with the uGUI shim's Format. The migrated ui.status.*
        // entries are Smart-off positional patterns; re-authoring ~60 of them as named-Smart
        // twins while both backends ship the same copy would double the table for the
        // coexistence window. New entries still use named Smart arguments via Get(key, args);
        // identifiers must be pre-stringified invariant by the caller, same as everywhere.
        public static string Format(string key, params object[] args)
        {
            StringTableEntry entry = ResolveEntry(key);

            if (entry == null)
                return key;

            return string.Format(System.Globalization.CultureInfo.InvariantCulture, entry.Value, args);
        }

        public static string Get(string key, params (string name, object value)[] args)
        {
            StringTableEntry entry = ResolveEntry(key);

            if (entry == null)
                return key;

            var named = new Dictionary<string, object>(args.Length, StringComparer.Ordinal);

            foreach ((string name, object value) in args)
                named[name] = value;

            // Smart entries format named args through the package's SmartFormat pipeline with
            // the entry's own locale; migrated Smart-off entries would ignore the dictionary,
            // which is why new-screen entries must be authored Smart.
            return entry.GetLocalizedString(new object[] { named });
        }

        static StringTableEntry ResolveEntry(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            if (cachedTable == null)
            {
                // Spike findings (pinned by the LocalizationSpike tests): initialization must be
                // forced, and the locale must be passed explicitly — no selection pass runs
                // outside Play mode.
                LocalizationSettings.InitializationOperation.WaitForCompletion();

                Locale locale = LocalizationSettings.SelectedLocale
                    ?? LocalizationSettings.ProjectLocale
                    ?? LocalizationSettings.AvailableLocales.GetLocale("en");

                if (locale == null)
                    return null;

                cachedTable = LocalizationSettings.StringDatabase
                    .GetTableAsync(TableName, locale)
                    .WaitForCompletion();

                if (!localeHookInstalled)
                {
                    LocalizationSettings.SelectedLocaleChanged += _ => cachedTable = null;
                    localeHookInstalled = true;
                }
            }

            return cachedTable?.GetEntry(key);
        }
    }
}
