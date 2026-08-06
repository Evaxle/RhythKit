using System.IO;

namespace RhythKit.Services;

public class SettingsService
{
    private readonly string _configPath;

    public string ColorsetsPath { get; private set; }

    public SettingsService()
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RhythKit_Settings.ini");
        var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "colorsets");
        ColorsetsPath = LoadSetting("colorsets_path", defaultPath);
        if (string.IsNullOrWhiteSpace(ColorsetsPath))
            ColorsetsPath = defaultPath;
        Directory.CreateDirectory(ColorsetsPath);
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
            var lines = File.ReadAllLines(_configPath);
            foreach (var line in lines)
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
