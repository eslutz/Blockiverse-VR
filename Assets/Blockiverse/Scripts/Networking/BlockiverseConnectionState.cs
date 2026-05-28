namespace Blockiverse.Networking
{
    public enum BlockiverseConnectionState
    {
        Stopped = 0,
        StartingHost = 1,
        Hosting = 2,
        StartingClient = 3,
        ConnectedClient = 4,
        Disconnecting = 5,
        Disconnected = 6,
        Failed = 7
    }
}
