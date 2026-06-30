using System;
using Blockiverse.Survival;
using UnityEngine;

namespace Blockiverse.Networking
{
    public enum PlayerModeState
    {
        Survival = 0,
        Creative = 1,
    }

    public sealed class SurvivalCreativeModeSwitch
    {
        PlayerModeState mode = PlayerModeState.Survival;
        ItemStack[] survivalSnapshot;
        int survivalSnapshotSlotCount;

        public PlayerModeState CurrentMode => mode;
        public bool HasSurvivalSnapshot => survivalSnapshot != null;

        // The stashed survival slots while in creative mode (null otherwise) — persistence
        // saves these as the player's real inventory instead of the creative scratch slots.
        public System.Collections.Generic.IReadOnlyList<ItemStack> SurvivalSnapshotSlots => survivalSnapshot;

        public bool SwitchToCreative(Inventory activeInventory)
        {
            if (activeInventory == null) throw new ArgumentNullException(nameof(activeInventory));

            if (mode == PlayerModeState.Creative)
                return false;

            survivalSnapshotSlotCount = activeInventory.SlotCount;
            survivalSnapshot = new ItemStack[survivalSnapshotSlotCount];

            for (int i = 0; i < survivalSnapshotSlotCount; i++)
                survivalSnapshot[i] = activeInventory.GetSlot(i);

            for (int i = 0; i < activeInventory.SlotCount; i++)
                activeInventory.ClearSlot(i);

            mode = PlayerModeState.Creative;
            return true;
        }

        public bool SwitchToSurvival(Inventory activeInventory)
        {
            if (activeInventory == null) throw new ArgumentNullException(nameof(activeInventory));

            if (mode == PlayerModeState.Survival)
                return false;

            for (int i = 0; i < activeInventory.SlotCount; i++)
                activeInventory.ClearSlot(i);

            if (survivalSnapshot != null)
            {
                int restoreCount = Math.Min(activeInventory.SlotCount, survivalSnapshotSlotCount);
                for (int i = 0; i < restoreCount; i++)
                {
                    if (!survivalSnapshot[i].IsEmpty)
                        activeInventory.SetSlot(i, survivalSnapshot[i]);
                }

                // Snapshot slots beyond the current inventory size are merged best-effort
                // instead of silently dropped (a smaller inventory keeps whatever fits).
                for (int i = restoreCount; i < survivalSnapshotSlotCount; i++)
                {
                    if (survivalSnapshot[i].IsEmpty)
                        continue;

                    ItemStack remainder = activeInventory.Add(survivalSnapshot[i]);
                    if (!remainder.IsEmpty)
                        Debug.LogWarning(
                            $"Survival snapshot restore lost {remainder.Count}x {remainder.ItemId}: inventory is full.");
                }
            }

            survivalSnapshot = null;
            mode = PlayerModeState.Survival;
            return true;
        }
    }
}