using System;
using Blockiverse.Survival;

namespace Blockiverse.Gameplay
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
            }

            mode = PlayerModeState.Survival;
            return true;
        }
    }
}
