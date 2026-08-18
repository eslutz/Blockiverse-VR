using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Presentation bridge for survival commands and multiplayer presence: plays the audio/VFX
    // cues for the local player's harvest/place/strip/till/plant/consume results (the creative
    // path has its own bridge via CreativeInteractionController.BlockMutationApplied) and the
    // join/leave stingers for remote peers. Pure feedback — no game state.
    [DisallowMultipleComponent]
    public sealed class SurvivalFeedbackBridge : MonoBehaviour
    {
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] SurvivalVitalsRuntime vitalsRuntime;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseVfxCuePlayer vfxCuePlayer;
        [SerializeField] BlockiverseSubtitleToastPanel toastPanel;

        BlockiverseNetworkSession session;
        bool subscribedToNetworking;
        bool subscribedToSync;
        bool subscribedToLoot;
        bool subscribedToVitals;

        public void ConfigureVitalsFeedback(
            SurvivalVitalsRuntime runtime,
            BlockiverseAudioCuePlayer cuePlayer)
        {
            UnsubscribeVitals();
            vitalsRuntime = runtime;
            audioCuePlayer = cuePlayer;
            SubscribeVitals();
        }

        public void ConfigureToastPanel(BlockiverseSubtitleToastPanel panel)
        {
            toastPanel = panel;
        }

        void OnEnable()
        {
            DiscoverDependencies();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void DiscoverDependencies()
        {
            if (!Application.isPlaying)
                return;

            if (survivalSync == null)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

            if (vitalsRuntime == null)
                vitalsRuntime = FindFirstObjectByType<SurvivalVitalsRuntime>(FindObjectsInactive.Include);

            if (audioCuePlayer == null)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            if (vfxCuePlayer == null)
                vfxCuePlayer = FindFirstObjectByType<BlockiverseVfxCuePlayer>();

            if (toastPanel == null)
                toastPanel = FindFirstObjectByType<BlockiverseSubtitleToastPanel>(FindObjectsInactive.Include);

            if (session == null)
                session = FindFirstObjectByType<BlockiverseNetworkSession>(FindObjectsInactive.Include);
        }

        void Subscribe()
        {
            if (survivalSync != null && !subscribedToSync)
            {
                survivalSync.CommandFeedback += OnCommandFeedback;
                subscribedToSync = true;
            }

            if (worldManager != null && !subscribedToLoot)
            {
                worldManager.ContainerLooted += OnContainerLooted;
                subscribedToLoot = true;
            }

            SubscribeVitals();

            if (session != null && !subscribedToNetworking)
            {
                session.ClientConnected += OnClientConnected;
                session.ClientDisconnected += OnClientDisconnected;
                subscribedToNetworking = true;
            }
        }

        void Unsubscribe()
        {
            if (survivalSync != null && subscribedToSync)
                survivalSync.CommandFeedback -= OnCommandFeedback;
            subscribedToSync = false;

            if (worldManager != null && subscribedToLoot)
                worldManager.ContainerLooted -= OnContainerLooted;
            subscribedToLoot = false;

            UnsubscribeVitals();

            if (session != null && subscribedToNetworking)
            {
                session.ClientConnected -= OnClientConnected;
                session.ClientDisconnected -= OnClientDisconnected;
                subscribedToNetworking = false;
            }
        }

        void SubscribeVitals()
        {
            if (subscribedToVitals || vitalsRuntime == null)
                return;

            vitalsRuntime.LocalPlayerDamaged += OnLocalPlayerDamaged;
            vitalsRuntime.LocalPlayerLowHealth += OnLocalPlayerLowHealth;
            vitalsRuntime.LocalPlayerDied += OnLocalPlayerDied;
            subscribedToVitals = true;
        }

        void UnsubscribeVitals()
        {
            if (!subscribedToVitals || vitalsRuntime == null)
            {
                subscribedToVitals = false;
                return;
            }

            vitalsRuntime.LocalPlayerDamaged -= OnLocalPlayerDamaged;
            vitalsRuntime.LocalPlayerLowHealth -= OnLocalPlayerLowHealth;
            vitalsRuntime.LocalPlayerDied -= OnLocalPlayerDied;
            subscribedToVitals = false;
        }

        void OnCommandFeedback(SurvivalCommandResult result, BlockPosition position)
        {
            Vector3 worldCenter = new(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f);

            switch (result.CommandKind)
            {
                case SurvivalCommandKind.HarvestResource:
                    if (result.Accepted)
                    {
                        audioCuePlayer?.PlayCueAt(BlockiverseAudioCue.BlockBreak, worldCenter);
                        audioCuePlayer?.PlayCue(BlockiverseAudioCue.PickupItem);
                        vfxCuePlayer?.PlayCue(BlockiverseVfxCue.BlockBreakDust, worldCenter);
                        vfxCuePlayer?.PlayCue(BlockiverseVfxCue.ResourceSpark, worldCenter);
                    }
                    else if (result.HarvestFailureReason == BlockHarvestFailureReason.InsufficientTool ||
                             result.HarvestFailureReason == BlockHarvestFailureReason.InventoryFull ||
                             result.FailureReason == SurvivalCommandFailureReason.InventoryFull)
                    {
                        audioCuePlayer?.PlayCue(BlockiverseAudioCue.ToolWrong);
                        ShowToast(DescribeHarvestRejection(result));
                    }
                    break;

                case SurvivalCommandKind.PlaceBlock:
                    if (result.Accepted)
                    {
                        audioCuePlayer?.PlayCueAt(BlockiverseAudioCue.BlockPlace, worldCenter);
                        vfxCuePlayer?.PlayCue(BlockiverseVfxCue.BlockPlacePuff, worldCenter);
                    }
                    break;

                case SurvivalCommandKind.StripLog:
                case SurvivalCommandKind.TillSoil:
                case SurvivalCommandKind.PlantSeed:
                    if (result.Accepted)
                    {
                        audioCuePlayer?.PlayCueAt(BlockiverseAudioCue.ToolHitSoft, worldCenter);
                        vfxCuePlayer?.PlayCue(BlockiverseVfxCue.BlockChipBurst, worldCenter);
                    }
                    break;

                case SurvivalCommandKind.UseConsumable:
                    if (result.Accepted)
                        audioCuePlayer?.PlayCue(BlockiverseAudioCue.PickupItem);
                    break;
            }
        }

        // Structure-loot grant: a broken crate dumped its contents into the player (§3 loot loop).
        void OnContainerLooted(BlockPosition position)
        {
            Vector3 worldCenter = new(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f);
            audioCuePlayer?.PlayCueAt(BlockiverseAudioCue.ContainerOpen, worldCenter);
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.PickupItem);
        }

        void OnClientConnected(ulong clientId)
        {
            if (session == null || clientId == session.LocalClientId)
                return;

            audioCuePlayer?.PlayCue(BlockiverseAudioCue.MultiplayerJoin);
            ShowToast("Player joined.");
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (session == null || clientId == session.LocalClientId)
                return;

            audioCuePlayer?.PlayCue(BlockiverseAudioCue.MultiplayerLeave);
            ShowToast("Player left.");
        }

        void ShowToast(string message)
        {
            toastPanel?.ShowToast(message);
        }

        static string DescribeHarvestRejection(SurvivalCommandResult result)
        {
            if (result.HarvestFailureReason == BlockHarvestFailureReason.InventoryFull ||
                result.FailureReason == SurvivalCommandFailureReason.InventoryFull)
            {
                return "Inventory full.";
            }

            return "This tool is not strong enough.";
        }

        void OnLocalPlayerDamaged(HealthChangeResult result)
        {
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.PlayerHurt);
        }

        void OnLocalPlayerLowHealth(HealthChangeResult result)
        {
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.LowHealth);
        }

        void OnLocalPlayerDied()
        {
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.PlayerDeath);
        }
    }
}
