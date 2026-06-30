using System;
using System.Collections.Generic;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;

namespace Blockiverse.Networking
{
    // Maps between the survival sync's neutral station state and the save schema's station file
    // records. Shared by the single-player session controller and the multiplayer host
    // persistence so the two save paths cannot drift.
    public static class WorldSaveStateMapper
    {
        public static VxlwStation[] ToSavedStations(IReadOnlyList<MultiplayerSurvivalSync.StationPersistentState> states)
        {
            if (states == null || states.Count == 0)
                return null;

            var result = new VxlwStation[states.Count];
            for (int i = 0; i < states.Count; i++)
            {
                MultiplayerSurvivalSync.StationPersistentState state = states[i];
                var inputs = new SavedContainerSlot[state.Inputs?.Length ?? 0];
                for (int s = 0; s < inputs.Length; s++)
                    inputs[s] = ToSavedSlot(state.Inputs[s]);

                result[i] = new VxlwStation
                {
                    X = state.Position.X,
                    Y = state.Position.Y,
                    Z = state.Position.Z,
                    StationType = state.StationType.ToString(),
                    Inputs = inputs,
                    Fuel = ToSavedSlot(state.Fuel),
                    Output = ToSavedSlot(state.Output),
                    ActiveRecipeOutputId = state.ActiveRecipeOutput.IsNone ? string.Empty : state.ActiveRecipeOutput.Value,
                    ProgressTicks = state.ProgressTicks
                };
            }

            return result;
        }

        public static List<MultiplayerSurvivalSync.StationPersistentState> FromSavedStations(VxlwStation[] saved)
        {
            var states = new List<MultiplayerSurvivalSync.StationPersistentState>(saved?.Length ?? 0);
            if (saved == null)
                return states;

            foreach (VxlwStation station in saved)
            {
                if (station == null ||
                    !Enum.TryParse(station.StationType, ignoreCase: true, out CraftingStation stationType) ||
                    !SmeltingStationModel.IsTimedStation(stationType))
                {
                    continue;
                }

                var inputs = new ItemStack[station.Inputs?.Length ?? 0];
                for (int i = 0; i < inputs.Length; i++)
                    inputs[i] = FromSavedSlot(station.Inputs[i]);

                states.Add(new MultiplayerSurvivalSync.StationPersistentState(
                    new BlockPosition(station.X, station.Y, station.Z),
                    stationType,
                    inputs,
                    FromSavedSlot(station.Fuel),
                    FromSavedSlot(station.Output),
                    string.IsNullOrEmpty(station.ActiveRecipeOutputId) ? ItemId.None : new ItemId(station.ActiveRecipeOutputId),
                    station.ProgressTicks));
            }

            return states;
        }

        public static SavedContainerSlot ToSavedSlot(ItemStack stack) => new()
        {
            CanonicalId = stack.IsEmpty ? string.Empty : stack.ItemId.Value,
            Count = stack.IsEmpty ? 0 : stack.Count,
            Durability = stack.IsEmpty ? 0 : stack.Durability
        };

        public static ItemStack FromSavedSlot(SavedContainerSlot slot) =>
            slot == null || string.IsNullOrEmpty(slot.CanonicalId) || slot.Count <= 0
                ? ItemStack.Empty
                : WithDurability(new ItemStack(new ItemId(slot.CanonicalId), slot.Count), slot.Durability);

        static ItemStack WithDurability(ItemStack stack, int durability) =>
            durability > 0 ? stack.WithDurability(durability) : stack;
    }
}