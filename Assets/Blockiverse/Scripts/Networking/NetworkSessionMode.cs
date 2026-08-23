namespace Blockiverse.Networking
{
    public enum NetworkSessionMode
    {
        Offline = 0,
        Host = 1,
        Client = 2,
        // Dedicated server: authoritative, with no local player. Shares every authority power with
        // Host and differs only in owning no player object. Authority checks must therefore test
        // IsServer, never IsHost -- a host is a server that additionally has a client attached.
        Server = 3
    }
}
