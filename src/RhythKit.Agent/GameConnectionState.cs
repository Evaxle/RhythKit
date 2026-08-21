namespace RhythKit.Agent;

internal static class GameConnectionState
{
    private static readonly object Sync = new();
    private static GameConnectionSnapshot state = new(false, false, false, null, null, null, null, DateTimeOffset.MinValue);

    public static GameConnectionSnapshot Get()
    {
        lock (Sync) return state;
    }

    public static void Update(GameConnectionUpdate update)
    {
        lock (Sync)
        {
            state = new GameConnectionSnapshot(
                update.Running,
                update.IntegrationConnected,
                update.MapCaptureReady,
                update.Game ?? state.Game,
                update.GameVersion ?? state.GameVersion,
                update.MapId ?? state.MapId,
                update.LastEvent ?? state.LastEvent,
                DateTimeOffset.UtcNow);
        }
    }
}

internal sealed record GameConnectionUpdate(
    bool Running,
    bool IntegrationConnected,
    bool MapCaptureReady,
    string? Game,
    string? GameVersion,
    string? MapId,
    string? LastEvent);

internal sealed record GameConnectionSnapshot(
    bool Running,
    bool IntegrationConnected,
    bool MapCaptureReady,
    string? Game,
    string? GameVersion,
    string? MapId,
    string? LastEvent,
    DateTimeOffset LastSeenAt);
