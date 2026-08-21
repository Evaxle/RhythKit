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
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
    }

    private static void Scan()
    {
        RhythiansMapStore.Initialize();
        foreach (var root in GetMapRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var source in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(source);
                if (!extension.Equals(".sspm", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".rhm", StringComparison.OrdinalIgnoreCase)) continue;
                var id = extension.Equals(".sspm", StringComparison.OrdinalIgnoreCase)
                    ? RhythiansMapIdentity.ResolveFromSspm(source)
                    : RhythiansMapIdentity.ResolveFromRhm(source);
                if (string.IsNullOrWhiteSpace(id)) continue;
                var stem = Path.GetFileNameWithoutExtension(source);
                var metadata = FindMetadata(root, stem);
                var title = metadata?["Title"]?.GetValue<string>() ?? metadata?["SongName"]?.GetValue<string>() ?? GetTitleFromFileName(source, id);
                RhythiansMapStore.CaptureFile(source, id, metadata, title);
            }
        }
    }

    private static IEnumerable<string> GetMapRoots()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(appData, "CapoRhythia", "maps");
        yield return Path.Combine(appData, "Rhythia", "maps");
        yield return Path.Combine(appData, "Rhythia", "Maps");
    }

    private static string GetTitleFromFileName(string source, string id)
    {
        var stem = Path.GetFileNameWithoutExtension(source);
        var prefix = $"rhythians-{id}-";
        return stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? stem[prefix.Length..] : stem;
    }

    private static JsonObject? FindMetadata(string root, string stem)
    {
        var candidates = new[]
        {
            Path.Combine(root, stem + ".json"),
            Path.Combine(Path.GetDirectoryName(root) ?? root, "cache", "maps", stem + ".json")
        };
        foreach (var file in candidates)
        {
            if (!File.Exists(file)) continue;
            try { return JsonNode.Parse(File.ReadAllText(file)) as JsonObject; } catch { }
        }
        return null;
    }
}
