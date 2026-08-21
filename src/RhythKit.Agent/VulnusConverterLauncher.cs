using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace RhythKit.Agent;

internal static class VulnusConverterLauncher
{
    public static async Task<(bool Success, string? OutputPath, string Message)> ConvertAsync(string gameDirectory, string sourcePath)
    {
        var vulnusMapsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vulnus", "maps");
        var gameMapsDirectory = Path.Combine(gameDirectory, "maps");
        Directory.CreateDirectory(vulnusMapsDirectory);
        Directory.CreateDirectory(gameMapsDirectory);
        var executable = FindExecutable(gameDirectory);
        if (executable == null) return (false, null, "Vulnus.exe was not found in the selected installation.");
        var startedAt = DateTime.UtcNow;
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "--rhythkit-convert-sspm", sourcePath }
        });
        if (process == null) return (false, null, "Vulnus could not be started for conversion.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) return (false, null, "Vulnus rejected the SSPM conversion.");
        var output = Directory.EnumerateFiles(vulnusMapsDirectory, "rhythians_*.vul", SearchOption.TopDirectoryOnly)
            .Where(path => File.GetLastWriteTimeUtc(path) >= startedAt.AddSeconds(-1))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (output == null) return (false, null, "Vulnus completed without producing a map.");
        var folderName = Path.GetFileNameWithoutExtension(output);
        var destination = Path.Combine(gameMapsDirectory, folderName);
        if (Directory.Exists(destination)) Directory.Delete(destination, true);
        Directory.CreateDirectory(destination);
        try
        {
            ExtractMapArchive(output, destination);
            NormalizeMapAudio(destination);
            var metaPath = Path.Combine(destination, "meta.json");
            if (!File.Exists(metaPath)) throw new InvalidDataException("The converted map is missing meta.json.");
            File.Delete(output);
            return (true, destination, "Conversion complete. The map was extracted into the Vulnus game maps folder.");
        }
        catch
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            throw;
        }
    }

    private static void ExtractMapArchive(string archivePath, string destination)
    {
        using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The converted map contains an invalid archive path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private static void NormalizeMapAudio(string destination)
    {
        var bin = Path.Combine(destination, "music.bin");
        var mp3 = Path.Combine(destination, "music.mp3");
        if (File.Exists(bin))
        {
            if (File.Exists(mp3)) File.Delete(mp3);
            File.Move(bin, mp3);
        }
        var metaPath = Path.Combine(destination, "meta.json");
        if (!File.Exists(metaPath)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(metaPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("_music", out var music) || !string.Equals(music.GetString(), "music.bin", StringComparison.OrdinalIgnoreCase)) return;
        var values = new Dictionary<string, object?>();
        foreach (var property in root.EnumerateObject()) values[property.Name] = property.NameEquals("_music") ? "music.mp3" : property.Value.Clone();
        File.WriteAllText(metaPath, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string? FindExecutable(string directory)
    {
        if (File.Exists(Path.Combine(directory, "Vulnus.exe"))) return Path.Combine(directory, "Vulnus.exe");
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "Vulnus.exe", SearchOption.AllDirectories).FirstOrDefault() : null;
    }
}
