using System.Globalization;
using System.IO;
using System.Windows.Media;
using RhythKit.Models;

namespace RhythKit.Services;

public class ColorsetService
{
    public string Save(string directory, string name, IReadOnlyList<ColorItem> colors)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Save directory is required.");

        if (string.IsNullOrWhiteSpace(name))
            name = $"colorset_{DateTime.Now:yyyyMMdd_HHmmss}";

        if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            name += ".txt";

        Directory.CreateDirectory(directory);
        var fullPath = Path.Combine(directory, name);

        var lines = colors.Select(c => $"#{c.Color.R:X2}{c.Color.G:X2}{c.Color.B:X2}");
        File.WriteAllLines(fullPath, lines);
        return fullPath;
    }

    public List<Color> Load(string filePath)
    {
        var result = new List<Color>();
        if (!File.Exists(filePath))
            return result;

        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            if (trimmed.StartsWith("#"))
                trimmed = trimmed.Substring(1);
            if (trimmed.Length == 6 &&
                byte.TryParse(trimmed.Substring(0, 2), NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(trimmed.Substring(2, 2), NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(trimmed.Substring(4, 2), NumberStyles.HexNumber, null, out var b))
            {
                result.Add(Color.FromRgb(r, g, b));
            }
        }
        return result;
    }

    public List<string> ListColorsets(string directory)
    {
        if (!Directory.Exists(directory))
            return new List<string>();
        return Directory.GetFiles(directory, "*.txt")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();
    }
}
