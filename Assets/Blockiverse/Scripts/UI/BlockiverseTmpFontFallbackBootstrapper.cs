using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Blockiverse.UI
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseTmpFontFallbackBootstrapper : MonoBehaviour
    {
        static readonly string[] PreferredFontNames =
        {
            // Android/Quest families first; desktop families help editor validation.
            "Noto Sans CJK JP",
            "Noto Sans CJK SC",
            "Noto Sans Arabic",
            "Noto Naskh Arabic",
            "Noto Sans Thai",
            "Noto Sans Devanagari",
            "Noto Sans",
            "sans-serif",
            "Arial Unicode MS",
            "Arial Unicode"
        };

        static bool configured;

        public static IReadOnlyList<string> PreferredOsFontNames => PreferredFontNames;

        void Awake()
        {
            EnsureGlobalFallbacks();
        }

        public static int EnsureGlobalFallbacks()
        {
            if (configured)
                return 0;

            configured = true;

            List<TMP_FontAsset> fallbacks = TMP_Settings.fallbackFontAssets;
            if (fallbacks == null)
                return 0;

            int added = 0;
            foreach (string fontName in PreferredFontNames)
            {
                TMP_FontAsset fontAsset = TryCreateDynamicFallback(fontName);
                if (fontAsset == null)
                    continue;

                fallbacks.Add(fontAsset);
                added++;
            }

            return added;
        }

        static TMP_FontAsset TryCreateDynamicFallback(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return null;

            try
            {
                Font font = Font.CreateDynamicFontFromOSFont(fontName, 90);
                if (font == null)
                    return null;

                TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(font);
                if (fontAsset == null)
                    return null;

                fontAsset.name = $"Runtime TMP Fallback - {fontName}";
                fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                fontAsset.isMultiAtlasTexturesEnabled = true;
                return fontAsset;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
