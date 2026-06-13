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
        public const int DefaultMaxPlayers = 2;
        public const int MaxSupportedPlayers = 4;
        public const string DefaultJoinCode = "blockiverse-lan";

        [SerializeField]
        string address;

        [SerializeField]
        string listenAddress;

        [SerializeField]
        ushort port;

        [SerializeField]
        int maxPlayers;

        [SerializeField]
        string joinCode;

        public BlockiverseNetworkConfig(string address, string listenAddress, ushort port)
            : this(address, listenAddress, port, DefaultMaxPlayers, DefaultJoinCode)
        {
        }

        public BlockiverseNetworkConfig(
            string address,
            string listenAddress,
            ushort port,
            int maxPlayers,
            string joinCode)
        {
            this.address = string.IsNullOrWhiteSpace(address) ? DefaultAddress : address;
            this.listenAddress = string.IsNullOrWhiteSpace(listenAddress) ? DefaultListenAddress : listenAddress;
            this.port = port == 0 ? DefaultPort : port;
            this.maxPlayers = ClampMaxPlayers(maxPlayers);
            this.joinCode = string.IsNullOrWhiteSpace(joinCode) ? DefaultJoinCode : joinCode.Trim();
        }

        public static BlockiverseNetworkConfig Default => new(DefaultAddress, DefaultListenAddress, DefaultPort);

        public string Address => string.IsNullOrWhiteSpace(address) ? DefaultAddress : address;
        public string ListenAddress => string.IsNullOrWhiteSpace(listenAddress) ? DefaultListenAddress : listenAddress;
        public ushort Port => port == 0 ? DefaultPort : port;
        public int MaxPlayers => ClampMaxPlayers(maxPlayers);
        public string JoinCode => string.IsNullOrWhiteSpace(joinCode) ? DefaultJoinCode : joinCode.Trim();

        public BlockiverseNetworkConfig WithAddress(string newAddress)
        {
            return new BlockiverseNetworkConfig(newAddress, ListenAddress, Port, MaxPlayers, JoinCode);
        }

        public BlockiverseNetworkConfig WithPort(ushort newPort)
        {
            return new BlockiverseNetworkConfig(Address, ListenAddress, newPort, MaxPlayers, JoinCode);
        }

        public BlockiverseNetworkConfig WithMaxPlayers(int newMaxPlayers)
        {
            return new BlockiverseNetworkConfig(Address, ListenAddress, Port, newMaxPlayers, JoinCode);
        }

        public BlockiverseNetworkConfig WithJoinCode(string newJoinCode)
        {
            return new BlockiverseNetworkConfig(Address, ListenAddress, Port, MaxPlayers, newJoinCode);
        }

        static int ClampMaxPlayers(int value) =>
            Math.Min(MaxSupportedPlayers, Math.Max(1, value <= 0 ? DefaultMaxPlayers : value));
    }
}
