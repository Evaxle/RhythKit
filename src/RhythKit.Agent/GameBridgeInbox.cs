using System.Text.Json;

namespace RhythKit.Agent;

internal sealed class GameBridgeInbox : IDisposable
{
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CapoRhythia", "Rhythians", "bridge", "events.jsonl");
    private readonly CancellationTokenSource cancellation = new();
    private readonly Func<GameBridgeMessage, Task> handler;
    private Task? task;

    public GameBridgeInbox(Func<GameBridgeMessage, Task> handler)
    {
        this.handler = handler;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Start()
    {
        task = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(path))
                {
                    var lines = await File.ReadAllLinesAsync(path, cancellation.Token);
                    File.WriteAllText(path, string.Empty);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var message = JsonSerializer.Deserialize<GameBridgeMessage>(line);
                            if (message != null) await handler(message);
                        }
                        catch { }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            await Task.Delay(250, cancellation.Token).ContinueWith(_ => { });
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        try { task?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        cancellation.Dispose();
    }
}

internal sealed record GameBridgeMessage(
    string Event,
    bool? Running,
    bool? IntegrationConnected,
    bool? MapCaptureReady,
    string? Game,
    string? GameVersion,
    string? MapId,
    string? ClientScoreId,
    double? Accuracy,
    int? Misses,
    double? Speed,
    DateTimeOffset? CompletedAt,
    bool? ResultQualified);
