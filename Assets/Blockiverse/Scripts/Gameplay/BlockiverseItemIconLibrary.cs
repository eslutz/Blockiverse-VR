using System;
using System.Collections.Generic;
using Blockiverse.Survival;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Canonical item-id → icon sprite lookup for inventory/crafting UI. The bootstrapper
    // populates the parallel arrays from Assets/Blockiverse/Art/Textures/Items at editor time
    // (one sprite per canonical id, file name = id); lookups build a dictionary lazily.
    [DisallowMultipleComponent]
    public sealed class BlockiverseItemIconLibrary : MonoBehaviour
    {
        [SerializeField] string[] itemIds = Array.Empty<string>();
        [SerializeField] Sprite[] sprites = Array.Empty<Sprite>();

        Dictionary<string, Sprite> lookup;

        public int Count => itemIds != null ? itemIds.Length : 0;

        public void Configure(string[] ids, Sprite[] icons)
        {
            if (ids == null || icons == null || ids.Length != icons.Length)
                throw new ArgumentException("Icon library requires matching id/sprite arrays.");

            itemIds = ids;
            sprites = icons;
            lookup = null;
        }

        public bool TryGetIcon(ItemId itemId, out Sprite sprite)
        {
            sprite = null;
            if (itemId.IsNone)
                return false;

            EnsureLookup();
            return lookup.TryGetValue(itemId.Value, out sprite) && sprite != null;
        }

        void EnsureLookup()
        {
            if (lookup != null)
                return;

            lookup = new Dictionary<string, Sprite>(itemIds.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < itemIds.Length && i < sprites.Length; i++)
            {
                if (!string.IsNullOrEmpty(itemIds[i]) && !lookup.ContainsKey(itemIds[i]))
                    lookup.Add(itemIds[i], sprites[i]);
            }
        }
    }
}
