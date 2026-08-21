namespace RhythKit.Agent;

internal static class GameConnectionState
{
    private static readonly object Sync = new();
    private static readonly Queue<GameDebugEvent> History = new();
    private static GameConnectionSnapshot state = new(false, false, false, null, null, null, null, DateTimeOffset.MinValue);

    public static GameConnectionSnapshot Get()
    {
        lock (Sync) return state;
    }

    public static GameDebugEvent[] GetHistory()
    {
        lock (Sync) return History.ToArray();
    }

    public static void AddDebug(string eventName, string message, string? mapId = null, object? data = null)
    {
        lock (Sync)
        {
            History.Enqueue(new GameDebugEvent(DateTimeOffset.UtcNow, eventName, message, state.Game, state.GameVersion, mapId ?? state.MapId, data));
            while (History.Count > 500) History.Dequeue();
        }
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
            if (!string.IsNullOrWhiteSpace(update.LastEvent))
            {
                History.Enqueue(new GameDebugEvent(DateTimeOffset.UtcNow, update.LastEvent!, "Game integration event", state.Game, state.GameVersion, update.MapId ?? state.MapId, null));
                while (History.Count > 500) History.Dequeue();
            }
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

internal sealed record GameDebugEvent(
    DateTimeOffset Timestamp,
    string Event,
    string Message,
    string? Game,
    string? GameVersion,
    string? MapId,
    object? Data);
