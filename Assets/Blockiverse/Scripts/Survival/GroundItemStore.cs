using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    public sealed class GroundItem
    {
        internal GroundItem(
            long id,
            ItemStack stack,
            float x,
            float y,
            float z,
            long spawnedTick,
            string droppedByPlayerId,
            long protectedUntilTick,
            long despawnTick)
        {
            Id = id;
            Stack = stack;
            X = x;
            Y = y;
            Z = z;
            SpawnedTick = spawnedTick;
            DroppedByPlayerId = droppedByPlayerId ?? string.Empty;
            ProtectedUntilTick = protectedUntilTick;
            DespawnTick = despawnTick;
        }

        public long Id { get; }
        public ItemStack Stack { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public long SpawnedTick { get; }
        public string DroppedByPlayerId { get; }
        public long ProtectedUntilTick { get; }
        public long DespawnTick { get; }

        public bool IsExpired(long worldTick) => worldTick >= DespawnTick;

        public bool IsProtectedFrom(string playerId, long worldTick)
        {
            return worldTick < ProtectedUntilTick &&
                   !string.IsNullOrEmpty(DroppedByPlayerId) &&
                   !string.Equals(DroppedByPlayerId, playerId ?? string.Empty, StringComparison.Ordinal);
        }

        public float DistanceSquaredTo(float x, float y, float z)
        {
            float dx = X - x;
            float dy = Y - y;
            float dz = Z - z;
            return dx * dx + dy * dy + dz * dz;
        }
    }

    public sealed class GroundItemStore
    {
        public const float PickupRadiusBlocks = 2.5f;
        public const int RecentDropProtectionSeconds = 3;
        public const int DespawnSeconds = 10 * 60;
        public const int RecentDropProtectionTicks = RecentDropProtectionSeconds * WorldConstants.TicksPerSecond;
        public const int DespawnTicks = DespawnSeconds * WorldConstants.TicksPerSecond;

        readonly List<GroundItem> items = new();
        readonly ItemRegistry itemRegistry;
        long nextId = 1;

        public GroundItemStore(ItemRegistry itemRegistry = null)
        {
            this.itemRegistry = itemRegistry ?? ItemRegistry.Default;
        }

        public IReadOnlyList<GroundItem> Items => items;
        public int Count => items.Count;

        public GroundItem Spawn(
            ItemStack stack,
            float x,
            float y,
            float z,
            long worldTick,
            string droppedByPlayerId = null)
        {
            if (stack.IsEmpty)
                throw new ArgumentException("Ground items require a non-empty stack.", nameof(stack));

            itemRegistry.Get(stack.ItemId);

            var item = new GroundItem(
                nextId++,
                stack,
                x,
                y,
                z,
                worldTick,
                droppedByPlayerId,
                string.IsNullOrEmpty(droppedByPlayerId) ? worldTick : worldTick + RecentDropProtectionTicks,
                worldTick + DespawnTicks);
            items.Add(item);
            return item;
        }

        public int RemoveExpired(long worldTick)
        {
            int removed = 0;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!items[i].IsExpired(worldTick))
                    continue;

                items.RemoveAt(i);
                removed++;
            }

            return removed;
        }

        public bool TryPickupNearest(
            Inventory inventory,
            float x,
            float y,
            float z,
            long worldTick,
            string playerId,
            out ItemStack pickedUp)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));

            pickedUp = ItemStack.Empty;
            RemoveExpired(worldTick);

            int bestIndex = -1;
            float bestDistanceSquared = PickupRadiusBlocks * PickupRadiusBlocks;
            for (int i = 0; i < items.Count; i++)
            {
                GroundItem item = items[i];
                if (item.IsProtectedFrom(playerId, worldTick))
                    continue;

                float distanceSquared = item.DistanceSquaredTo(x, y, z);
                if (distanceSquared > bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                bestIndex = i;
            }

            if (bestIndex < 0)
                return false;

            GroundItem pickup = items[bestIndex];
            if (!inventory.TryAddAll(pickup.Stack))
                return false;

            pickedUp = pickup.Stack;
            items.RemoveAt(bestIndex);
            return true;
        }
    }
}
