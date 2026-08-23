namespace Blockiverse.Networking
{
    // New values are appended so existing persisted and logged values keep their meaning.
    public enum BlockiverseConnectionState
    {
        Stopped = 0,
        StartingHost = 1,
        Hosting = 2,
        StartingClient = 3,
        ConnectedClient = 4,
        Disconnecting = 5,
        Disconnected = 6,
        Failed = 7,
        StartingServer = 8,
        Serving = 9
    }
}
