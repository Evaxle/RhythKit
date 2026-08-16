using System.IO;

namespace RhythKit.Services;

public class SettingsService
{
    private readonly string _configPath;

    public string ColorsetsPath { get; private set; }
    public int? PlayerId { get; private set; }
    public string Username { get; private set; } = "";
    public string AvatarUrl { get; private set; } = "";
    public string ServerUrl { get; private set; } = "http://localhost:5210";
    public string ServerToken { get; private set; } = "";

    public bool HasProfile => PlayerId.HasValue && PlayerId.Value > 0;

    public SettingsService()
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RhythKit_Settings.ini");
        var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "colorsets");
        ColorsetsPath = LoadSetting("colorsets_path", defaultPath);
        if (string.IsNullOrWhiteSpace(ColorsetsPath))
            ColorsetsPath = defaultPath;
        Directory.CreateDirectory(ColorsetsPath);

        var playerId = LoadSetting("player_id", "");
        if (int.TryParse(playerId, out var id) && id > 0)
            PlayerId = id;
        Username = LoadSetting("username", "");
        AvatarUrl = LoadSetting("avatar_url", "");
        var serverUrl = LoadSetting("server_url", "");
        if (!string.IsNullOrWhiteSpace(serverUrl))
            ServerUrl = serverUrl.TrimEnd('/');
        ServerToken = LoadSetting("server_token", "");
    }

    public void SaveProfile(int playerId, string username, string? avatarUrl)
    {
        PlayerId = playerId;
        Username = username;
        AvatarUrl = avatarUrl ?? "";
        SaveSetting("player_id", playerId.ToString());
        SaveSetting("username", username);
        SaveSetting("avatar_url", AvatarUrl);
    }

    public void SaveServer(string serverUrl, string token)
    {
        ServerUrl = serverUrl.TrimEnd('/');
        ServerToken = token;
        SaveSetting("server_url", ServerUrl);
        SaveSetting("server_token", token);
    }

    public void SaveServerUrl(string serverUrl)
    {
        ServerUrl = serverUrl.TrimEnd('/');
        SaveSetting("server_url", ServerUrl);
    }

    public void ClearProfile()
    {
        PlayerId = null;
        Username = "";
        AvatarUrl = "";
        ServerToken = "";
        SaveSetting("player_id", "");
        SaveSetting("username", "");
        SaveSetting("avatar_url", "");
        SaveSetting("server_token", "");
    }

    public void SaveColorsetsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        ColorsetsPath = path;
        Directory.CreateDirectory(path);
        SaveSetting("colorsets_path", path);
    }

    private string LoadSetting(string key, string fallback)
    {
        if (!File.Exists(_configPath))
            return fallback;
        try
        {
            foreach (var line in File.ReadAllLines(_configPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                    continue;
                var idx = trimmed.IndexOf('=');
                if (idx < 0)
                    continue;
                var k = trimmed.Substring(0, idx).Trim();
                var v = trimmed.Substring(idx + 1).Trim();
                if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return v;
            }
        }
        catch
        {
        }
        return fallback;
    }

    private void SaveSetting(string key, string value)
    {
        try
        {
            var lines = new List<string>();
            if (File.Exists(_configPath))
                lines.AddRange(File.ReadAllLines(_configPath));

            var newLines = new List<string>();
            var replaced = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                {
                    newLines.Add(line);
                    continue;
                }
                var idx = trimmed.IndexOf('=');
                if (idx > 0 && trimmed.Substring(0, idx).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    newLines.Add($"{key}={value}");
                    replaced = true;
                }
                else
                {
                    newLines.Add(line);
                }
            }
            if (!replaced)
                newLines.Add($"{key}={value}");

            File.WriteAllLines(_configPath, newLines);
        }
        catch
        {
        }
    }
}
