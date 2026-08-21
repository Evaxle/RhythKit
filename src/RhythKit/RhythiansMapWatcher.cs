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
        if (!Directory.Exists(maps) || !Directory.Exists(cache)) return;

        foreach (var source in Directory.EnumerateFiles(maps, "*.sspm", SearchOption.AllDirectories))
        {
            var id = RhythiansMapIdentity.ResolveFromSspm(source);
            if (string.IsNullOrWhiteSpace(id)) continue;
            var stem = Path.GetFileNameWithoutExtension(source);
            var metadata = FindMetadata(cache, stem);
            var title = metadata?["Title"]?.GetValue<string>() ?? metadata?["SongName"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(source);
            var folder = Path.Combine(RhythiansMapStore.MapsRoot, Sanitize(title));
            if (Directory.Exists(folder))
            {
                try
                {
                    var existing = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "map.json"))) as JsonObject;
                    if (existing?["RhythiansId"]?.GetValue<string>() != id) folder = Path.Combine(RhythiansMapStore.MapsRoot, $"{Sanitize(title)} [{id}]");
                }
                catch { }
            }
            Directory.CreateDirectory(folder);
            var destination = Path.Combine(folder, Path.GetFileName(source));
            if (!File.Exists(destination)) File.Copy(source, destination);
            metadata ??= new JsonObject();
            metadata["RhythiansId"] = id;
            metadata["FilePath"] = source;
            File.WriteAllText(Path.Combine(folder, "map.json"), metadata.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
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

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? "Rhythians Map" : result;
    }
}