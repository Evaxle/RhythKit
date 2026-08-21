using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RhythKit;

public static class RhythiansMapStore
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CapoRhythia", "Rhythians");
    public static string MapsRoot => Path.Combine(Root, "maps");

    public static void Initialize() => Directory.CreateDirectory(MapsRoot);

    public static void OpenRoot()
    {
        Initialize();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = Root, UseShellExecute = true });
    }

    public static bool Contains(string rhythiansId)
    {
        return Find(rhythiansId) != null;
    }

    public static JsonObject? Find(string rhythiansId)
    {
        if (string.IsNullOrWhiteSpace(rhythiansId) || !Directory.Exists(MapsRoot)) return null;
        foreach (var folder in Directory.EnumerateDirectories(MapsRoot))
        {
            var metadataPath = Path.Combine(folder, "map.json");
            if (!File.Exists(metadataPath)) continue;
            try
            {
                if (JsonNode.Parse(File.ReadAllText(metadataPath)) is JsonObject json && string.Equals(json["RhythiansId"]?.GetValue<string>(), rhythiansId, StringComparison.OrdinalIgnoreCase)) return json;
            }
            catch { }
        }
        return null;
    }

    public static void Capture(object? map, string rhythiansId)
    {
        if (map == null || string.IsNullOrWhiteSpace(rhythiansId)) return;
        try
        {
            Initialize();
            var source = ReadString(map, "Path") ?? ReadString(map, "FilePath") ?? ReadString(map, "SourcePath");
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return;
            var extension = Path.GetExtension(source);
            if (!extension.Equals(".sspm", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".rhm", StringComparison.OrdinalIgnoreCase)) return;
            var title = ReadString(map, "Title") ?? ReadString(map, "SongName") ?? ReadString(map, "Name") ?? "Rhythians Map";
            CaptureFile(source, rhythiansId, FindCacheMetadata(map, title), title, map);
        }
        catch { }
    }

    public static void CaptureFile(string source, string rhythiansId, JsonObject? metadata = null, string? title = null, object? map = null)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source) || string.IsNullOrWhiteSpace(rhythiansId)) return;
        try
        {
            Initialize();
            var extension = Path.GetExtension(source);
            if (!extension.Equals(".sspm", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".rhm", StringComparison.OrdinalIgnoreCase)) return;
            title ??= metadata?["Title"]?.GetValue<string>() ?? metadata?["SongName"]?.GetValue<string>() ?? "Rhythians Map";
            var folder = CreateFolder(title, rhythiansId);
            var destination = Path.Combine(folder, Path.GetFileName(source));
            if (!File.Exists(destination) || !FilesEqual(source, destination)) File.Copy(source, destination, true);
            metadata ??= map != null ? BuildMetadata(map, rhythiansId, source) : new JsonObject();
            metadata["RhythiansId"] = rhythiansId;
            metadata["SourcePath"] = source;
            metadata["ImportedAt"] = DateTimeOffset.UtcNow;
            metadata["Sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination)));
            metadata["StoredFile"] = destination;
            File.WriteAllText(Path.Combine(folder, "map.json"), metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static bool FilesEqual(string first, string second)
    {
        try
        {
            var a = new FileInfo(first);
            var b = new FileInfo(second);
            if (a.Length != b.Length) return false;
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(File.ReadAllBytes(first)), SHA256.HashData(File.ReadAllBytes(second)));
        }
        catch { return false; }
    }

    private static JsonObject BuildMetadata(object map, string id, string source)
    {
        return new JsonObject
        {
            ["RhythiansId"] = id,
            ["OnlineId"] = ReadString(map, "OnlineId"),
            ["OnlineStatus"] = ReadString(map, "OnlineStatus"),
            ["LegacyId"] = ReadString(map, "LegacyId"),
            ["SongName"] = ReadString(map, "SongName"),
            ["Mappers"] = ReadArray(map, "Mappers") ?? ReadArray(map, "CachedMappers"),
            ["Title"] = ReadString(map, "Title") ?? ReadString(map, "Name"),
            ["Duration"] = ReadNumber(map, "Duration"),
            ["Difficulty"] = ReadNumber(map, "Difficulty"),
            ["CustomDifficultyName"] = ReadString(map, "CustomDifficultyName"),
            ["StarRating"] = ReadNumber(map, "StarRating"),
            ["SourcePath"] = source
        };
    }

    private static JsonObject? FindCacheMetadata(object map, string title)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CapoRhythia", "cache", "maps");
        if (!Directory.Exists(root)) return null;
        var names = new[] { ReadString(map, "Name"), title }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Sanitize(x!)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var directory = Path.Combine(root, name);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(file)) is JsonObject json) return json;
                }
                catch { }
            }
        }
        return null;
    }

    private static string CreateFolder(string title, string id)
    {
        var baseName = Sanitize(title);
        var path = Path.Combine(MapsRoot, baseName);
        if (!Directory.Exists(path)) return path;
        try
        {
            var existing = JsonNode.Parse(File.ReadAllText(Path.Combine(path, "map.json"))) as JsonObject;
            if (existing?["RhythiansId"]?.GetValue<string>() == id) return path;
        }
        catch { }
        return Path.Combine(MapsRoot, $"{baseName} [{id}]");
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? "Rhythians Map" : result;
    }

    private static string? ReadString(object target, string name)
    {
        try { return target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(target)?.ToString(); }
        catch { return null; }
    }

    private static JsonArray? ReadArray(object target, string name)
    {
        try
        {
            var value = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(target);
            if (value is not System.Collections.IEnumerable enumerable || value is string) return null;
            var result = new JsonArray();
            foreach (var item in enumerable) result.Add(item?.ToString());
            return result;
        }
        catch { return null; }
    }

    private static JsonNode? ReadNumber(object target, string name)
    {
        try
        {
            var value = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(target);
            return value switch
            {
                byte v => v,
                short v => v,
                int v => v,
                long v => v,
                float v => v,
                double v => v,
                decimal v => (double)v,
                _ => null
            };
        }
        catch { return null; }
    }
}
