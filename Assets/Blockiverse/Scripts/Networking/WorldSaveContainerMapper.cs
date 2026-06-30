using System;
using System.Collections.Generic;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;

namespace Blockiverse.Networking
{
    public static class WorldSaveContainerMapper
    {
        public static IReadOnlyList<SavedContainer> BuildSavedContainers(ContainerInventoryStore store)
        {
            if (store == null || store.Count == 0)
                return null;

            var result = new List<SavedContainer>(store.Count);
            foreach (BlockPosition position in store.Positions)
            {
                if (!store.TryGet(position, out Inventory inventory) || inventory == null)
                    continue;

                var slots = new List<SavedContainerSlot>();
                for (int index = 0; index < inventory.SlotCount; index++)
                {
                    ItemStack stack = inventory.GetSlot(index);
                    if (stack.IsEmpty)
                        continue;

                    slots.Add(new SavedContainerSlot
                    {
                        CanonicalId = stack.ItemId.Value,
                        Count = stack.Count,
                        Durability = stack.Durability
                    });
                }

                result.Add(new SavedContainer
                {
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                    Slots = slots.ToArray()
                });
            }

            return result.Count > 0 ? result : null;
        }

        public static IReadOnlyList<(BlockPosition Position, IEnumerable<(string ItemId, int Count, int Durability)> Items)> BuildRestoredContainers(
            SavedContainer[] saved,
            Action<SavedContainer, SavedContainerSlot> invalidSlot = null)
        {
            if (saved == null || saved.Length == 0)
                return null;

            var restored = new List<(BlockPosition, IEnumerable<(string, int, int)>)>(saved.Length);
            foreach (SavedContainer container in saved)
            {
                SavedContainerSlot[] slots = container.Slots ?? Array.Empty<SavedContainerSlot>();
                var items = new List<(string, int, int)>(slots.Length);
                foreach (SavedContainerSlot slot in slots)
                {
                    if (slot == null ||
                        string.IsNullOrWhiteSpace(slot.CanonicalId) ||
                        slot.Count <= 0)
                    {
                        invalidSlot?.Invoke(container, slot);
                        continue;
                    }

                    items.Add((slot.CanonicalId, slot.Count, slot.Durability));
                }

                restored.Add((new BlockPosition(container.X, container.Y, container.Z), items));
            }

            return restored;
        }
    }
}