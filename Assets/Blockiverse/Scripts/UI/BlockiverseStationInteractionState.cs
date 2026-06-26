using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.UI
{
    // Runtime state/action endpoint for timed station menus. UI Toolkit owns display and XRI
    // interaction; this component owns the open station model and routes transfers through the
    // host-authoritative survival sync.
    public sealed class BlockiverseStationInteractionState : MonoBehaviour
    {
        [SerializeField] MultiplayerSurvivalSync survivalSync;

        ItemRegistry itemRegistry;
        SmeltingStationModel station;
        BlockPosition stationPosition;
        string currentStatusText = string.Empty;

        public bool IsOpen => station != null;
        public BlockPosition OpenPosition => stationPosition;
        public SmeltingStationModel CurrentStation => station;
        public string CurrentStatusText => currentStatusText;

        public void ConfigureSurvivalSync(MultiplayerSurvivalSync sync) => survivalSync = sync;

        public void ConfigureItemRegistry(ItemRegistry registry) => itemRegistry = registry;

        public void ResolveRuntimeReferences()
        {
            if (survivalSync == null && Application.isPlaying)
                survivalSync = FindAnyObjectByType<MultiplayerSurvivalSync>();
        }

        public void Open(SmeltingStationModel model, BlockPosition position, string displayTitle = null)
        {
            ResolveRuntimeReferences();
            station = model;
            stationPosition = position;
            if (model != null)
                SetStatus(model.IsActive
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonActive)
                    : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StationIdle));
        }

        public void Close() => station = null;

        public void DepositHeldInput() => SubmitHeldItemTransfer(isFuel: false);

        public void DepositHeldFuel() => SubmitHeldItemTransfer(isFuel: true);

        public void WithdrawInput() => SubmitStationWithdrawal(isFuel: false);

        public void WithdrawFuel() => SubmitStationWithdrawal(isFuel: true);

        void SubmitHeldItemTransfer(bool isFuel)
        {
            if (station == null || !DiscoverSurvivalSync())
                return;

            ItemStack held = survivalSync.EquippedItem;
            if (held.IsEmpty)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StationHoldItem));
                return;
            }

            bool sentToHost;
            SurvivalCommandResult result = isFuel
                ? survivalSync.TrySubmitStationDepositFuel(stationPosition, held.ItemId, 1, out sentToHost)
                : survivalSync.TrySubmitStationDepositInput(stationPosition, held.ItemId, 1, out sentToHost);

            SetStatus(result.Accepted
                ? isFuel
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StationFuelAdded)
                    : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StationInputAdded)
                : sentToHost
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonSending)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.StationCannotDeposit,
                        BlockiverseLocalization.DisplayName(result.FailureReason)));
        }

        void SubmitStationWithdrawal(bool isFuel)
        {
            if (station == null || !DiscoverSurvivalSync())
                return;

            ItemStack target = isFuel ? station.Fuel : FirstInputStack();
            if (target.IsEmpty)
            {
                SetStatus(BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.StationCannotWithdraw,
                    BlockiverseLocalization.DisplayName(SurvivalCommandFailureReason.StationRejected)));
                return;
            }

            bool sentToHost;
            SurvivalCommandResult result = isFuel
                ? survivalSync.TrySubmitStationWithdrawFuel(stationPosition, target.ItemId, target.Count, out sentToHost)
                : survivalSync.TrySubmitStationWithdrawInput(stationPosition, target.ItemId, target.Count, out sentToHost);

            SetStatus(result.Accepted
                ? BlockiverseLocalization.Format(BlockiverseLocalization.Keys.StationWithdrew, FormatStack(result.Item))
                : sentToHost
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonSending)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.StationCannotWithdraw,
                        BlockiverseLocalization.DisplayName(result.FailureReason)));
        }

        public void CollectOutput()
        {
            if (station == null || !DiscoverSurvivalSync())
                return;

            SurvivalCommandResult result = survivalSync.TrySubmitStationCollect(stationPosition, out bool sentToHost);
            SetStatus(result.Accepted
                ? BlockiverseLocalization.Format(BlockiverseLocalization.Keys.StationCollected, FormatStack(result.Item))
                : sentToHost
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonSending)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.StationCannotCollect,
                        BlockiverseLocalization.DisplayName(result.FailureReason)));
        }

        ItemStack FirstInputStack()
        {
            if (station == null)
                return ItemStack.Empty;

            for (int i = 0; i < station.InputSlotCount; i++)
            {
                ItemStack input = station.GetInput(i);
                if (!input.IsEmpty)
                    return input;
            }

            return ItemStack.Empty;
        }

        bool DiscoverSurvivalSync()
        {
            if (survivalSync == null && Application.isPlaying)
                survivalSync = FindAnyObjectByType<MultiplayerSurvivalSync>();

            return survivalSync != null;
        }

        void SetStatus(string status)
        {
            currentStatusText = status ?? string.Empty;
        }

        // Player-facing labels use registry display names ("Iron Ingot"), never raw canonical
        // ids ("iron_ingot"). Falls back to the default registry when no shared instance was
        // injected via ConfigureItemRegistry.
        string FormatStack(ItemStack stack)
        {
            itemRegistry ??= ItemRegistry.Default;
            return BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.StationStack,
                itemRegistry.Get(stack.ItemId).Name,
                stack.Count);
        }
    }
}
