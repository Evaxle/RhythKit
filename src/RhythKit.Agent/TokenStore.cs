using System.Text.Json;

namespace RhythKit.Agent;

internal static class TokenStore
{
    private sealed record State(string? Token, string? Username, string? Game);

    private static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rhythians");
    private static string StatePath => Path.Combine(DirectoryPath, "rhythkit.json");
    private static string SettingsPath => Path.Combine(DirectoryPath, "rhythkit-agent-settings.json");

    public static AgentState? Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            return JsonSerializer.Deserialize<AgentState>(File.ReadAllText(StatePath));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string token, string? username, string? game)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(new AgentState(token, username, game)));
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(StatePath)) File.Delete(StatePath);
        }
        catch { }
    }

    public static void SaveGame(string game)
    {
        var state = Load();
        if (state == null)
        {
            Save(string.Empty, null, game);
            return;
        }
        Save(state.Token ?? string.Empty, state.Username, game);
    }

    public static AgentSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AgentSettings();
            return JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(SettingsPath)) ?? new AgentSettings();
        }
        catch
        {
            return new AgentSettings();
        }
    }

    public static void SaveSettings(AgentSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
    }
}

internal sealed record AgentState(string? Token, string? Username, string? Game);
