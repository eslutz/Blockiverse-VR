using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Blockiverse.Networking
{
    public static class BlockiverseLanAddressUtility
    {
        public static IReadOnlyList<string> GetLocalIPv4Addresses()
        {
            var addresses = new List<string>();

            try
            {
                foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                        networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    foreach (UnicastIPAddressInformation addressInfo in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        IPAddress address = addressInfo.Address;
                        if (IsUsableLanIPv4(address))
                            AddUnique(addresses, address.ToString());
                    }
                }
            }
            catch (NetworkInformationException)
            {
                AddHostEntryFallback(addresses);
            }
            catch (SocketException)
            {
                AddHostEntryFallback(addresses);
            }

            if (addresses.Count == 0)
                AddHostEntryFallback(addresses);

            return addresses;
        }

        public static string DescribeLocalIPv4Addresses(string fallbackAddress = BlockiverseNetworkConfig.DefaultAddress)
        {
            IReadOnlyList<string> addresses = GetLocalIPv4Addresses();
            return addresses.Count > 0 ? string.Join(", ", addresses) : fallbackAddress;
        }

        public static bool IsWildcardListenAddress(string address) =>
            string.IsNullOrWhiteSpace(address) ||
            address == BlockiverseNetworkConfig.DefaultListenAddress ||
            address == "::" ||
            address == "0:0:0:0:0:0:0:0";

        static void AddHostEntryFallback(List<string> addresses)
        {
            try
            {
                foreach (IPAddress address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (IsUsableLanIPv4(address))
                        AddUnique(addresses, address.ToString());
                }
            }
            catch (SocketException)
            {
                // Keep the list empty; callers provide a deterministic fallback.
            }
        }

        static bool IsUsableLanIPv4(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
                return false;

            byte[] bytes = address.GetAddressBytes();
            return !IPAddress.IsLoopback(address) &&
                bytes.Length == 4 &&
                !(bytes[0] == 169 && bytes[1] == 254);
        }

        static void AddUnique(List<string> addresses, string address)
        {
            if (!string.IsNullOrWhiteSpace(address) && !addresses.Contains(address))
                addresses.Add(address);
        }
    }
}
