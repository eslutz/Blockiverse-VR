using System;
using UnityEngine;

namespace Blockiverse.Networking
{
    [Serializable]
    public struct BlockiverseNetworkConfig
    {
        public const string DefaultAddress = "127.0.0.1";
        public const string DefaultListenAddress = "0.0.0.0";
        public const ushort DefaultPort = 7777;

        [SerializeField]
        string address;

        [SerializeField]
        string listenAddress;

        [SerializeField]
        ushort port;

        public BlockiverseNetworkConfig(string address, string listenAddress, ushort port)
        {
            this.address = string.IsNullOrWhiteSpace(address) ? DefaultAddress : address;
            this.listenAddress = string.IsNullOrWhiteSpace(listenAddress) ? DefaultListenAddress : listenAddress;
            this.port = port == 0 ? DefaultPort : port;
        }

        public static BlockiverseNetworkConfig Default => new(DefaultAddress, DefaultListenAddress, DefaultPort);

        public string Address => string.IsNullOrWhiteSpace(address) ? DefaultAddress : address;
        public string ListenAddress => string.IsNullOrWhiteSpace(listenAddress) ? DefaultListenAddress : listenAddress;
        public ushort Port => port == 0 ? DefaultPort : port;

        public BlockiverseNetworkConfig WithAddress(string newAddress)
        {
            return new BlockiverseNetworkConfig(newAddress, ListenAddress, Port);
        }

        public BlockiverseNetworkConfig WithPort(ushort newPort)
        {
            return new BlockiverseNetworkConfig(Address, ListenAddress, newPort);
        }
    }
}
