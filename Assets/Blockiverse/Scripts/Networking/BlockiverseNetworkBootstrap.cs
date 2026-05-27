using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Blockiverse.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    [RequireComponent(typeof(UnityTransport))]
    [RequireComponent(typeof(BlockiverseNetworkSession))]
    public sealed class BlockiverseNetworkBootstrap : MonoBehaviour
    {
        [SerializeField]
        BlockiverseNetworkSession session;

        public BlockiverseNetworkSession Session => ResolveSession();

        void Awake()
        {
            ResolveSession();
        }

        BlockiverseNetworkSession ResolveSession()
        {
            if (session == null)
                session = GetComponent<BlockiverseNetworkSession>();

            return session;
        }
    }
}
