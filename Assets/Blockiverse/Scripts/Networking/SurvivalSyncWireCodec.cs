using System;
using System.Text;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Networking
{
    // Pure serialization helpers for MultiplayerSurvivalSync's named-message protocol.
    // Keeping this out of the MonoBehaviour leaves the sync focused on authority/session logic.
    public static class SurvivalSyncWireCodec
    {
        public const int MaxNetworkItemIdChars = 128;

        public static void WriteCommandResult(ref FastBufferWriter writer, SurvivalCommandResult result)
        {
            writer.WriteValueSafe(result.Accepted);
            writer.WriteValueSafe(result.PendingHostValidation);
            writer.WriteValueSafe(result.IsDuplicate);
            writer.WriteValueSafe((int)result.CommandKind);
            writer.WriteValueSafe((int)result.FailureReason);
            writer.WriteValueSafe(result.RequestId);
            WriteItemStack(ref writer, result.Item);
            writer.WriteValueSafe((int)result.HarvestFailureReason);
            writer.WriteValueSafe((int)result.CraftingFailureReason);
        }

        public static SurvivalCommandResult ReadCommandResult(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool accepted);
            reader.ReadValueSafe(out bool pendingHostValidation);
            reader.ReadValueSafe(out bool duplicate);
            reader.ReadValueSafe(out int commandKind);
            reader.ReadValueSafe(out int failureReason);
            reader.ReadValueSafe(out uint requestId);
            ItemStack item = ReadItemStack(ref reader);
            reader.ReadValueSafe(out int harvestFailureReason);
            reader.ReadValueSafe(out int craftingFailureReason);

            return new SurvivalCommandResult(
                accepted,
                pendingHostValidation,
                duplicate,
                (SurvivalCommandKind)commandKind,
                (SurvivalCommandFailureReason)failureReason,
                requestId,
                item,
                (BlockHarvestFailureReason)harvestFailureReason,
                (CraftingFailureReason)craftingFailureReason);
        }

        public static void WriteInventorySnapshot(ref FastBufferWriter writer, Inventory inventory)
        {
            writer.WriteValueSafe(inventory.SlotCount);
            writer.WriteValueSafe(inventory.HotbarSlotCount);

            for (int index = 0; index < inventory.SlotCount; index++)
                WriteItemStack(ref writer, inventory.GetSlot(index));
        }

        public static void ApplyInventorySnapshot(Inventory inventory, ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out int slotCount);
            reader.ReadValueSafe(out int hotbarSlotCount);

            if (inventory.SlotCount != slotCount || inventory.HotbarSlotCount != hotbarSlotCount)
            {
                // A malformed or version-mismatched snapshot must not crash the message pump;
                // drop it and keep the local mirror unchanged — the next accepted command's
                // snapshot resynchronizes the inventory.
                Debug.LogWarning(
                    $"Ignoring inventory snapshot with mismatched shape: got {slotCount}/{hotbarSlotCount} slots/hotbar, expected {inventory.SlotCount}/{inventory.HotbarSlotCount}.");
                return;
            }

            for (int index = 0; index < slotCount; index++)
            {
                if (!TryReadItemStack(ref reader, out ItemStack stack))
                    return;
                if (stack.IsEmpty)
                    inventory.ClearSlot(index);
                else
                    inventory.SetSlot(index, stack);
            }
        }

        public static void WriteItemStack(ref FastBufferWriter writer, ItemStack stack)
        {
            writer.WriteValueSafe(stack.ItemId.Value);
            writer.WriteValueSafe(stack.Count);
            writer.WriteValueSafe(stack.Durability);
        }

        public static ItemStack ReadItemStack(ref FastBufferReader reader)
        {
            return TryReadItemStack(ref reader, out ItemStack stack) ? stack : ItemStack.Empty;
        }

        public static bool TryReadItemStack(ref FastBufferReader reader, out ItemStack stack)
        {
            stack = ItemStack.Empty;
            if (!TryReadBoundedNetworkString(ref reader, MaxNetworkItemIdChars, out string itemId))
                return false;

            if (!reader.TryBeginRead(sizeof(int) * 2))
                return false;

            reader.ReadValueSafe(out int count);
            reader.ReadValueSafe(out int durability);
            // A malformed payload (positive count with an empty id) must not throw inside the
            // message pump — the ItemId constructor rejects empty ids, so degrade to Empty.
            if (count <= 0 || string.IsNullOrWhiteSpace(itemId))
                return true;

            stack = new ItemStack(new ItemId(itemId), count);
            if (durability > 0)
                stack = stack.WithDurability(durability);
            return true;
        }

        public static void WriteBlockPosition(ref FastBufferWriter writer, BlockPosition position)
        {
            writer.WriteValueSafe(position.X);
            writer.WriteValueSafe(position.Y);
            writer.WriteValueSafe(position.Z);
        }

        public static BlockPosition ReadBlockPosition(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out int x);
            reader.ReadValueSafe(out int y);
            reader.ReadValueSafe(out int z);
            return new BlockPosition(x, y, z);
        }

        public static bool TryReadBoundedNetworkString(
            ref FastBufferReader reader,
            int maxChars,
            out string value)
        {
            value = string.Empty;

            if (maxChars < 0)
                throw new ArgumentOutOfRangeException(nameof(maxChars), "String bounds must be non-negative.");

            if (!reader.TryBeginRead(sizeof(int)))
                return false;

            reader.ReadValueSafe(out int length);
            if (length < 0 || length > maxChars)
            {
                reader.Seek(reader.Length);
                return false;
            }

            int byteLength = length * sizeof(char);
            if (!reader.TryBeginRead(byteLength))
            {
                reader.Seek(reader.Length);
                return false;
            }

            if (byteLength == 0)
                return true;

            var bytes = new byte[byteLength];
            reader.ReadBytesSafe(ref bytes, byteLength);
            value = Encoding.Unicode.GetString(bytes);
            return true;
        }
    }
}