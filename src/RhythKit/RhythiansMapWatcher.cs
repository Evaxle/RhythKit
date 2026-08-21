using System.Text.Json.Nodes;

namespace RhythKit;

public static class RhythiansMapWatcher
{
    private static bool started;

    public static void Start()
    {
        if (started) return;
        started = true;
        _ = Task.Run(RunAsync);
    }

    private static async Task RunAsync()
    {
        while (true)
        {
            try { Scan(); } catch { }
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
    }

    private static void Scan()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var capo = Path.Combine(appData, "CapoRhythia");
        var maps = Path.Combine(capo, "maps");
        var cache = Path.Combine(capo, "cache", "maps");
        if (!Directory.Exists(maps)) return;

        foreach (var source in Directory.EnumerateFiles(maps, "*.sspm", SearchOption.AllDirectories))
        {
            var id = RhythiansMapIdentity.ResolveFromSspm(source);
            if (string.IsNullOrWhiteSpace(id) || !IsRhythiansDownload(source, id)) continue;
            var stem = Path.GetFileNameWithoutExtension(source);
            var metadata = FindMetadata(cache, stem);
            var title = metadata?["Title"]?.GetValue<string>() ?? metadata?["SongName"]?.GetValue<string>() ?? GetTitleFromFileName(source, id);
            RhythiansMapStore.CaptureFile(source, id, metadata, title);
        }
    }

    private static bool IsRhythiansDownload(string source, string id)
    {
        var stem = Path.GetFileNameWithoutExtension(source);
        return stem.StartsWith($"rhythians-{id}-", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetTitleFromFileName(string source, string id)
    {
        var stem = Path.GetFileNameWithoutExtension(source);
        var prefix = $"rhythians-{id}-";
        return stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? stem[prefix.Length..] : stem;
    }

    private static JsonObject? FindMetadata(string root, string stem)
    {
        var exact = Path.Combine(root, stem + ".json");
        if (File.Exists(exact))
        {
            try { return JsonNode.Parse(File.ReadAllText(exact)) as JsonObject; } catch { }
        }
        return null;
    }
}
