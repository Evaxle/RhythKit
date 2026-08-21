using Microsoft.Win32;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace RhythKit.Installer;

enum RhythiaTarget
{
    Unknown,
    SspNightly,
    RhythiaSteam,
    Vulnus,
    LegacyManaged
}

public sealed class InstallerForm : Form
{
    private readonly TextBox gamePath = new() { Dock = DockStyle.Fill };
    private readonly Label detected = new() { AutoSize = true, Text = "Detected: unknown" };
    private readonly Label status = new() { AutoSize = true, Text = "Choose your game folder." };
    private readonly Button install = new() { Text = "Install RhythKit", AutoSize = true };

    public InstallerForm()
    {
        Text = "RhythKit Installer";
        Width = 760;
        Height = 300;
        StartPosition = FormStartPosition.CenterScreen;
        SetLogoIcon();
        var browse = new Button { Text = "Browse", AutoSize = true };
        browse.Click += (_, _) => Browse();
        install.Click += async (_, _) => await InstallAsync();
        gamePath.TextChanged += (_, _) => Detect();
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16), ColumnCount = 2 };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(gamePath, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);
        var info = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 0, 16, 8) };
        info.Controls.Add(detected);
        info.Controls.Add(status);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 0, 16, 16) };
        buttons.Controls.Add(install);
        Controls.Add(buttons);
        Controls.Add(info);
        Controls.Add(pathRow);
        AutoDetect();
    }

    private void SetLogoIcon()
    {
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
    }

    private void AutoDetect()
    {
        var candidates = SteamLibraryDetector.FindSteamLibraryPaths()
            .Select(path => Path.Combine(path, "steamapps", "common", "Rhythia"))
            .Concat(SteamLibraryDetector.FindSteamLibraryPaths().Select(path => Path.Combine(path, "steamapps", "common", "Vulnus")))
            .Concat(GetDefaultCandidates())
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (GameDetector.Detect(candidate) != RhythiaTarget.Unknown)
            {
                gamePath.Text = candidate;
                Detect();
                return;
            }
        }
    }

    private static IEnumerable<string> GetDefaultCandidates()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return new[]
        {
            Path.Combine(local, "Rhythia"),
            Path.Combine(programFiles, "Rhythia"),
            Path.Combine(programFilesX86, "Rhythia"),
            Path.Combine(local, "Vulnus"),
            Path.Combine(roaming, "Vulnus"),
            Path.Combine(programFiles, "Vulnus"),
            Path.Combine(programFilesX86, "Vulnus")
        };
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select the Rhythia, SSP Nightly, or Vulnus game folder" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            gamePath.Text = dialog.SelectedPath;
            Detect();
        }
    }

    private void Detect()
    {
        var target = GameDetector.Detect(gamePath.Text.Trim());
        detected.Text = target switch
        {
            RhythiaTarget.SspNightly => "Detected: SSP Nightly",
            RhythiaTarget.RhythiaSteam => "Detected: Rhythia Steam",
            RhythiaTarget.Vulnus => "Detected: Vulnus",
            RhythiaTarget.LegacyManaged => "Detected: legacy Rhythia",
            _ => "Detected: unknown"
        };
        install.Enabled = target != RhythiaTarget.Unknown;
    }

    private async Task InstallAsync()
    {
        var path = gamePath.Text.Trim();
        if (!Directory.Exists(path))
        {
            MessageBox.Show(this, "Select a valid game folder.", "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var target = GameDetector.Detect(path);
        if (target == RhythiaTarget.Unknown)
        {
            MessageBox.Show(this, "RhythKit could not identify this game build. No files were changed.", "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        install.Enabled = false;
        try
        {
            var modDirectory = Path.Combine(path, "RhythKit");
            Directory.CreateDirectory(modDirectory);
            var agentPayload = RhythKitAgentPayload.Data;
            var uninstallerPayload = RhythKitUninstallerPayload.Data;
            if (agentPayload.Length == 0) throw new InvalidOperationException("The installer was not built with the RhythKit Agent payload. Run the build script again.");
            if (uninstallerPayload.Length == 0) throw new InvalidOperationException("The installer was not built with the RhythKit Uninstaller payload. Run the build script again.");
            string? hash = null;
            if (target == RhythiaTarget.LegacyManaged)
            {
                var payload = RhythKitPayload.Data;
                if (payload.Length == 0) throw new InvalidOperationException("The installer was not built with the RhythKit payload. Run the build script again.");
                hash = Convert.ToHexString(SHA256.HashData(payload));
                await File.WriteAllBytesAsync(Path.Combine(modDirectory, "RhythKit.dll"), payload);
            }
            var agentPath = Path.Combine(modDirectory, "RhythKit.Agent.exe");
            var uninstallerPath = Path.Combine(modDirectory, "RhythKit.Uninstaller.exe");
            await File.WriteAllBytesAsync(agentPath, agentPayload);
            await File.WriteAllBytesAsync(uninstallerPath, uninstallerPayload);
            status.Text = target == RhythiaTarget.LegacyManaged ? "Installing legacy Rhythia integration..." : "Installing RhythKit integration...";
            switch (target)
            {
                case RhythiaTarget.LegacyManaged:
                    InstallLegacy(path, RhythKitPayload.Data);
                    break;
                case RhythiaTarget.SspNightly:
                    await InstallSspNightlyAsync(path);
                    break;
                case RhythiaTarget.RhythiaSteam:
                    InstallSteam(path);
                    break;
                case RhythiaTarget.Vulnus:
                    InstallVulnus(path);
                    break;
            }
            var manifest = new
            {
                id = "rhythkit",
                name = "RhythKit",
                version = "0.5.0",
                game = target.ToString(),
                gameDirectory = path,
                agentPath,
                uninstallerPath,
                assemblySha256 = hash,
                installedAt = DateTimeOffset.UtcNow
            };
            await File.WriteAllTextAsync(Path.Combine(modDirectory, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            InstallAgentStartup(agentPath, path, target);
            StartAgent(agentPath, path, target);
            status.Text = $"RhythKit installed for {target}.";
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
            MessageBox.Show(this, ex.ToString(), "RhythKit installation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Detect(); }
    }

    private static void InstallLegacy(string gameDirectory, byte[] payload)
    {
        var assemblyPath = RhythiaPatcher.FindRhythiaAssembly(gameDirectory);
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath)!;
        File.WriteAllBytes(Path.Combine(assemblyDirectory, "RhythKit.dll"), payload);
        RhythiaPatcher.Patch(gameDirectory);
        if (!RhythiaPatcher.IsPatched(assemblyPath)) throw new InvalidOperationException("Legacy Rhythia was not patched successfully.");
    }

    private static void InstallSteam(string gameDirectory)
    {
        if (!SteamRhythiaLayout.Matches(gameDirectory))
        {
            var missing = SteamRhythiaLayout.FindMissingFile(gameDirectory);
            throw new InvalidOperationException(missing == null ? "The selected folder is not a supported Steam Rhythia installation." : $"Steam Rhythia is missing {missing}.");
        }
        var bridge = Path.Combine(gameDirectory, "RhythKit", "RhythKitBridge.json");
        var state = new { target = "RhythiaSteam", executable = SteamRhythiaLayout.ExecutablePath(gameDirectory), integration = "agent", installedAt = DateTimeOffset.UtcNow };
        File.WriteAllText(bridge, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task InstallSspNightlyAsync(string gameDirectory)
    {
        var exe = GameDetector.FindExecutable(gameDirectory, "sound-space-plus.exe", "Sound Space Plus.exe", "SSP.exe");
        if (exe == null) throw new FileNotFoundException("SSP Nightly executable was not found.");
        var bridge = Path.Combine(gameDirectory, "RhythKit", "RhythKitBridge.txt");
        await File.WriteAllTextAsync(bridge, "SSP Nightly integration target detected.\n" + exe + "\nScore source: Godot user://bests");
    }

    private static void InstallVulnus(string gameDirectory)
    {
        var exe = GameDetector.FindExecutable(gameDirectory, "Vulnus.exe");
        if (exe == null) throw new FileNotFoundException("Vulnus.exe was not found.");
        var bridge = Path.Combine(gameDirectory, "RhythKit", "RhythKitBridge.json");
        var state = new { target = "Vulnus", executable = exe, integration = "agent", mapsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vulnus", "maps"), installedAt = DateTimeOffset.UtcNow };
        File.WriteAllText(bridge, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void InstallAgentStartup(string agentPath, string gameDirectory, RhythiaTarget target)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        key!.SetValue($"RhythKit-{target}", $"\"{agentPath}\" --game-dir \"{gameDirectory}\" --game-type {target}");
    }

    private static void StartAgent(string agentPath, string gameDirectory, RhythiaTarget target)
    {
        var psi = new ProcessStartInfo { FileName = agentPath, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("--game-dir");
        psi.ArgumentList.Add(gameDirectory);
        psi.ArgumentList.Add("--game-type");
        psi.ArgumentList.Add(target.ToString());
        Process.Start(psi);
    }
}

static class SteamLibraryDetector
{
    public static IEnumerable<string> FindSteamLibraryPaths()
    {
        var roots = new[]
        {
            Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath")?.ToString(),
            Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("InstallPath")?.ToString(),
            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")?.GetValue("InstallPath")?.ToString(),
            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam")?.GetValue("InstallPath")?.ToString()
        };
        foreach (var root in roots.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!))
        {
            yield return root;
            var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile)) continue;
            foreach (var path in ParseLibraryFolders(libraryFile)) yield return path;
        }
    }
    private static IEnumerable<string> ParseLibraryFolders(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;
            var value = parts[^1].Replace("\\\\", "\\");
            if (Directory.Exists(value) && value.Contains(":\\", StringComparison.OrdinalIgnoreCase)) yield return value;
        }
    }
}

static class GameDetector
{
    public static RhythiaTarget Detect(string directory)
    {
        if (!Directory.Exists(directory)) return RhythiaTarget.Unknown;
        if (SteamRhythiaLayout.Matches(directory)) return RhythiaTarget.RhythiaSteam;
        if (File.Exists(Path.Combine(directory, "project.godot")) && (FindExecutable(directory, "Vulnus.exe") != null || Directory.EnumerateFiles(directory, "*.pck", SearchOption.TopDirectoryOnly).Any(x => Path.GetFileName(x).Contains("Vulnus", StringComparison.OrdinalIgnoreCase)))) return RhythiaTarget.Vulnus;
        if (FindExecutable(directory, "sound-space-plus.exe", "Sound Space Plus.exe", "SSP.exe") != null || Directory.EnumerateFiles(directory, "*.pck", SearchOption.TopDirectoryOnly).Any(x => Path.GetFileName(x).Contains("ssp", StringComparison.OrdinalIgnoreCase))) return RhythiaTarget.SspNightly;
        if (File.Exists(Path.Combine(directory, "Rhythia.dll")) || Directory.EnumerateFiles(directory, "Rhythia.dll", SearchOption.AllDirectories).Any()) return RhythiaTarget.LegacyManaged;
        return RhythiaTarget.Unknown;
    }
    public static string? FindExecutable(string directory, params string[] names)
    {
        foreach (var name in names)
        {
            var direct = Path.Combine(directory, name);
            if (File.Exists(direct)) return direct;
        }
        return null;
    }
}
