using System.Reflection;
using System.Text.Json;

namespace RhythKit;

public static class RhythiansMapIdentity
{
    public static string? Resolve(object? map)
    {
        var embedded = ReadProperty(map, "RhythiansId") ?? ReadProperty(map, "rhythiansId");
        if (IsGuid(embedded)) return embedded;

        var path = ReadProperty(map, "Path") ?? ReadProperty(map, "FilePath") ?? ReadProperty(map, "SourcePath");
        var filenameId = ExtractGuidFromPath(path);
        if (filenameId != null) return filenameId;

        var legacyId = ReadProperty(map, "LegacyId");
        if (IsGuid(legacyId)) return legacyId;

        return null;
    }

    public static string? ResolveFromRhm(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            var entry = archive.GetEntry("map") ?? archive.GetEntry("Map");
            if (entry == null) return null;
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("RhythiansId", out var id) && IsGuid(id.GetString())) return id.GetString();
            if (document.RootElement.TryGetProperty("rhythiansId", out id) && IsGuid(id.GetString())) return id.GetString();
        }
        catch { }
        return null;
    }

    private static string? ReadProperty(object? target, string name)
    {
        if (target == null) return null;
        try
        {
            var type = target.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var value = property?.GetValue(target);
            return value?.ToString();
        }
        catch { return null; }
    }

    private static string? ExtractGuidFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var filename = System.IO.Path.GetFileName(path);
        var match = System.Text.RegularExpressions.Regex.Match(filename, @"rhythians-([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool IsGuid(string? value) => Guid.TryParse(value, out _);
}
