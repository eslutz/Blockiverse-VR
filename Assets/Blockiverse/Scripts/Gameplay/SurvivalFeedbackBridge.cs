using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Unity.Netcode;
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
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseVfxCuePlayer vfxCuePlayer;

        BlockiverseNetworkSession session;
        NetworkManager subscribedNetworkManager;
        bool subscribedToSync;

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

            if (audioCuePlayer == null)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            if (vfxCuePlayer == null)
                vfxCuePlayer = FindFirstObjectByType<BlockiverseVfxCuePlayer>();

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

            NetworkManager networkManager = session != null ? session.NetworkManager : null;
            if (networkManager != null && subscribedNetworkManager != networkManager)
            {
                subscribedNetworkManager = networkManager;
                subscribedNetworkManager.OnClientConnectedCallback += OnClientConnected;
                subscribedNetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        void Unsubscribe()
        {
            if (survivalSync != null && subscribedToSync)
                survivalSync.CommandFeedback -= OnCommandFeedback;
            subscribedToSync = false;

            if (subscribedNetworkManager != null)
            {
                subscribedNetworkManager.OnClientConnectedCallback -= OnClientConnected;
                subscribedNetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                subscribedNetworkManager = null;
            }
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
                    else if (result.HarvestFailureReason == BlockHarvestFailureReason.InsufficientTool)
                    {
                        audioCuePlayer?.PlayCue(BlockiverseAudioCue.ToolWrong);
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

        void OnClientConnected(ulong clientId)
        {
            if (subscribedNetworkManager == null || clientId == subscribedNetworkManager.LocalClientId)
                return;

            audioCuePlayer?.PlayCue(BlockiverseAudioCue.MultiplayerJoin);
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (subscribedNetworkManager == null || clientId == subscribedNetworkManager.LocalClientId)
                return;

            audioCuePlayer?.PlayCue(BlockiverseAudioCue.MultiplayerLeave);
        }
    }
}
