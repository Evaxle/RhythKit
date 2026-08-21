using System.Diagnostics;

namespace RhythKit.Agent;

internal static class VulnusConverterLauncher
{
    public static async Task<(bool Success, string? OutputPath, string Message)> ConvertAsync(string gameDirectory, string sourcePath)
    {
        var mapsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vulnus", "maps");
        Directory.CreateDirectory(mapsDirectory);
        var destination = Path.Combine(mapsDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destination, true);
        var executable = FindExecutable(gameDirectory);
        if (executable == null) return (false, null, "Vulnus.exe was not found in the selected installation.");
        var startedAt = DateTime.UtcNow;
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"--rhythkit-convert-sspm \"{destination.Replace("\"", "\\\"")}\""
        });
        if (process == null) return (false, null, "Vulnus could not be started for conversion.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) return (false, null, "Vulnus rejected the SSPM conversion.");
        var outputs = Directory.EnumerateFiles(mapsDirectory, "rhythians_*.vul", SearchOption.TopDirectoryOnly)
            .Where(path => File.GetLastWriteTimeUtc(path) >= startedAt.AddSeconds(-1))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        return outputs.Length == 0 ? (false, null, "Vulnus completed without producing a map.") : (true, outputs[0], "Conversion complete.");
    }

    private static string? FindExecutable(string directory)
    {
        if (File.Exists(Path.Combine(directory, "Vulnus.exe"))) return Path.Combine(directory, "Vulnus.exe");
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "Vulnus.exe", SearchOption.AllDirectories).FirstOrDefault() : null;
    }
}
