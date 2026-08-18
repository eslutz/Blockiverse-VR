using System;
using Blockiverse.Core;
using Blockiverse.Voxel;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Networking
{
    [DisallowMultipleComponent]
    public sealed class MultiplayerEnvironmentRelay : MonoBehaviour
    {
        const string LightningStrikeMessage = "Blockiverse.Environment.LightningStrike";

        [SerializeField] BlockiverseNetworkSession session;

        NetworkManager subscribedNetworkManager;
        bool messageHandlerRegistered;

        public event Action<BlockPosition> LightningStruck;

        void OnEnable()
        {
            RegisterMessageHandler();
        }

        void OnDisable()
        {
            UnregisterMessageHandler();
        }

        void Update()
        {
            if (!messageHandlerRegistered)
                RegisterMessageHandler();
        }

        public void BroadcastLightningStrike(BlockPosition strike)
        {
            NetworkManager networkManager = session != null ? session.NetworkManager : NetworkManager.Singleton;
            if (networkManager == null ||
                !networkManager.IsListening ||
                !networkManager.IsServer ||
                networkManager.CustomMessagingManager == null)
            {
                return;
            }

            var writer = new FastBufferWriter(sizeof(int) * 3, Allocator.Temp);
            try
            {
                writer.WriteValueSafe(strike.X);
                writer.WriteValueSafe(strike.Y);
                writer.WriteValueSafe(strike.Z);

                foreach (ulong clientId in networkManager.ConnectedClientsIds)
                {
                    if (clientId != networkManager.LocalClientId)
                        networkManager.CustomMessagingManager.SendNamedMessage(LightningStrikeMessage, clientId, writer);
                }
            }
            finally
            {
                writer.Dispose();
            }
        }

        void RegisterMessageHandler()
        {
            NetworkManager networkManager = session != null ? session.NetworkManager : NetworkManager.Singleton;
            if (networkManager == null)
                return;

            if (subscribedNetworkManager != null && subscribedNetworkManager != networkManager)
                UnregisterMessageHandler();

            if (messageHandlerRegistered || networkManager.CustomMessagingManager == null)
                return;

            subscribedNetworkManager = networkManager;
            subscribedNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                LightningStrikeMessage,
                HandleLightningStrikeMessage);
            messageHandlerRegistered = true;
        }

        void UnregisterMessageHandler()
        {
            if (!messageHandlerRegistered ||
                subscribedNetworkManager == null ||
                subscribedNetworkManager.CustomMessagingManager == null)
            {
                subscribedNetworkManager = null;
                messageHandlerRegistered = false;
                return;
            }

            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(LightningStrikeMessage);
            subscribedNetworkManager = null;
            messageHandlerRegistered = false;
        }

        void HandleLightningStrikeMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (senderClientId != NetworkManager.ServerClientId)
                return;

            reader.ReadValueSafe(out int x);
            reader.ReadValueSafe(out int y);
            reader.ReadValueSafe(out int z);
            
            LightningStruck?.Invoke(new BlockPosition(x, y, z));
        }
    }
}