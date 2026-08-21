using System.Text.Json;

namespace RhythKit.Agent;

internal static class RankedMapStore
{
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CapoRhythia", "Rhythians", "maps");

    public static bool Contains(string id)
    {
        if (!Guid.TryParse(id, out _)) return false;
        var path = Path.Combine(Root, id, "map.json");
        if (!File.Exists(path)) return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("RhythiansId", out var value) &&
                   string.Equals(value.GetString(), id, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
