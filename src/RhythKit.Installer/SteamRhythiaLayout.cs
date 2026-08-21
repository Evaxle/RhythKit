namespace RhythKit.Installer;

public static class SteamRhythiaLayout
{
    private static readonly string[] RequiredFiles =
    [
        "rhythia.exe",
        "steam_api64.dll",
        "rsign.dll",
        "rosu_pp.dll",
        "e_sqlite3.dll"
    ];

    public static bool Matches(string directory)
    {
        if (!Directory.Exists(directory)) return false;
        if (File.Exists(Path.Combine(directory, "Rhythia.dll"))) return false;
        return RequiredFiles.All(file => File.Exists(Path.Combine(directory, file)));
    }

    public static string? FindMissingFile(string directory)
    {
        return RequiredFiles.FirstOrDefault(file => !File.Exists(Path.Combine(directory, file)));
    }

    public static string ExecutablePath(string directory) => Path.Combine(directory, "rhythia.exe");
}
