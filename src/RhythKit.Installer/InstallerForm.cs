using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;

namespace RhythKit.Installer;

enum RhythiaTarget
{
    Unknown,
    SspNightly,
    RhythiaSteam,
    Vulnus,
    RewriteRhythia,
    LegacyManaged
}

public sealed class InstallerForm : Form
{
    private readonly TextBox gamePath = new() { Dock = DockStyle.Fill };
    private readonly Label detected = new() { AutoSize = true, Text = "Detected: unknown" };
    private readonly Label status = new() { AutoSize = true, Text = "Choose your game folder or executable." };
    private readonly Button install = new() { Text = "Install RhythKit", AutoSize = true, Enabled = false };
    private readonly ComboBox manualTarget = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private bool suppressDetection;

    public InstallerForm()
    {
        Text = "RhythKit Installer";
        Width = 820;
        Height = 360;
        StartPosition = FormStartPosition.CenterScreen;
        SetLogoIcon();
        manualTarget.Items.AddRange(new object[] { "Automatic detection", "Rhythia Steam", "Sound Space Plus", "Vulnus", "Rewrite Rhythia" });
        manualTarget.SelectedIndex = 0;
        manualTarget.SelectedIndexChanged += (_, _) => Detect();
        var browseFolder = new Button { Text = "Browse Folder", AutoSize = true };
        var browseExe = new Button { Text = "Browse EXE", AutoSize = true };
        browseFolder.Click += (_, _) => BrowseFolder();
        browseExe.Click += (_, _) => BrowseExecutable();
        install.Click += async (_, _) => await InstallAsync();
        gamePath.TextChanged += (_, _) => { if (!suppressDetection) Detect(); };

        var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16), ColumnCount = 4 };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(gamePath, 0, 0);
        pathRow.Controls.Add(browseFolder, 1, 0);
        pathRow.Controls.Add(browseExe, 2, 0);
        pathRow.Controls.Add(manualTarget, 3, 0);

        var info = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 0, 16, 8), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        info.Controls.Add(detected);
        info.Controls.Add(status);
        info.Controls.Add(new Label { AutoSize = true, Text = "If automatic detection fails, select the correct game from the dropdown and install it manually." });
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
            .SelectMany(path => new[]
            {
                Path.Combine(path, "steamapps", "common", "Rhythia"),
                Path.Combine(path, "steamapps", "common", "Vulnus")
            })
            .Concat(GetDefaultCandidates())
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var target = GameDetector.Detect(candidate);
            if (target != RhythiaTarget.Unknown)
            {
                SetPath(candidate);
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
            Path.Combine(programFilesX86, "Vulnus"),
            Path.Combine(local, "Sound Space Plus"),
            Path.Combine(local, "Rewrite Rhythia")
        };
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select the Rhythia, SSP, Vulnus, or Rewrite Rhythia game folder" };
        if (dialog.ShowDialog(this) == DialogResult.OK) SetPath(dialog.SelectedPath);
    }

    private void BrowseExecutable()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select the game executable",
            Filter = "Game executable (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) SetPath(Path.GetDirectoryName(dialog.FileName) ?? string.Empty);
    }

    private void SetPath(string path)
    {
        suppressDetection = true;
        try
        {
            gamePath.Text = path;
            manualTarget.SelectedIndex = 0;
        }
        finally { suppressDetection = false; }
        Detect();
    }

    private void Detect()
    {
        var detectedTarget = GameDetector.Detect(gamePath.Text.Trim());
        var selectedTarget = manualTarget.SelectedIndex switch
        {
            1 => RhythiaTarget.RhythiaSteam,
            2 => RhythiaTarget.SspNightly,
            3 => RhythiaTarget.Vulnus,
            4 => RhythiaTarget.RewriteRhythia,
            _ => RhythiaTarget.Unknown
        };
        var target = selectedTarget == RhythiaTarget.Unknown ? detectedTarget : selectedTarget;
        detected.Text = target switch
        {
            RhythiaTarget.SspNightly => "Detected: Sound Space Plus",
            RhythiaTarget.RhythiaSteam => "Detected: Rhythia Steam",
            RhythiaTarget.Vulnus => "Detected: Vulnus",
            RhythiaTarget.RewriteRhythia => "Detected: Rewrite Rhythia",
            RhythiaTarget.LegacyManaged => "Detected: legacy Rhythia",
            _ => "Detected: unknown"
        };
        install.Enabled = target != RhythiaTarget.Unknown && Directory.Exists(gamePath.Text.Trim());
    }

    private async Task InstallAsync()
    {
        var path = gamePath.Text.Trim();
        if (!Directory.Exists(path))
        {
            MessageBox.Show(this, "Select a valid game folder or executable.", "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var detectedTarget = GameDetector.Detect(path);
        var manualTargetValue = manualTarget.SelectedIndex switch
        {
            1 => RhythiaTarget.RhythiaSteam,
            2 => RhythiaTarget.SspNightly,
            3 => RhythiaTarget.Vulnus,
            4 => RhythiaTarget.RewriteRhythia,
            _ => RhythiaTarget.Unknown
        };
        var target = manualTargetValue == RhythiaTarget.Unknown ? detectedTarget : manualTargetValue;
        if (target == RhythiaTarget.Unknown)
        {
            MessageBox.Show(this, "RhythKit could not identify this game build. Select the correct game from the manual selection dropdown.", "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!GameDetector.Validate(path, target, out var validationError))
        {
            MessageBox.Show(this, validationError, "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            await File.WriteAllBytesAsync(Path.Combine(modDirectory, "RhythKit.Agent.exe"), agentPayload);
            await File.WriteAllBytesAsync(Path.Combine(modDirectory, "RhythKit.Uninstaller.exe"), uninstallerPayload);
            status.Text = $"Installing RhythKit for {target}...";
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
                case RhythiaTarget.RewriteRhythia:
                    InstallRewrite(path);
                    break;
            }
            var manifest = new
            {
                id = "rhythkit",
                name = "RhythKit",
                version = "0.6.0",
                game = target.ToString(),
                gameDirectory = path,
                executable = GameDetector.FindGameExecutable(path, target),
                installedAt = DateTimeOffset.UtcNow
            };
            await File.WriteAllTextAsync(Path.Combine(modDirectory, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            InstallAgentStartup(Path.Combine(modDirectory, "RhythKit.Agent.exe"), path, target);
            StartAgent(Path.Combine(modDirectory, "RhythKit.Agent.exe"), path, target);
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
        WriteBridge(gameDirectory, "RhythiaSteam", SteamRhythiaLayout.ExecutablePath(gameDirectory));
    }

    private static async Task InstallSspNightlyAsync(string gameDirectory)
    {
        var exe = GameDetector.FindExecutable(gameDirectory, "soundspaceplus.exe", "Sound Space Plus.exe", "SSP.exe");
        if (exe == null) throw new FileNotFoundException("Sound Space Plus executable was not found.");
        var pck = Directory.EnumerateFiles(gameDirectory, "*.pck", SearchOption.TopDirectoryOnly).FirstOrDefault(x => Path.GetFileName(x).Contains("soundspace", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(x).Contains("ssp", StringComparison.OrdinalIgnoreCase));
        if (pck == null) throw new FileNotFoundException("Sound Space Plus .pck file was not found.");
        WriteBridge(gameDirectory, "SspNightly", exe, pck);
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "RhythKit", "SSPIntegration.json"), JsonSerializer.Serialize(new { executable = exe, pck, mapsFormat = "SSPM", scoreSource = "Godot user://bests" }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void InstallVulnus(string gameDirectory)
    {
        var exe = GameDetector.FindExecutable(gameDirectory, "Vulnus.exe");
        if (exe == null) throw new FileNotFoundException("Vulnus.exe was not found.");
        WriteBridge(gameDirectory, "Vulnus", exe, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vulnus", "maps"));
    }

    private static void InstallRewrite(string gameDirectory)
    {
        var exe = GameDetector.FindExecutable(gameDirectory, "rhythia.exe", "Rhythia.exe");
        if (exe == null) throw new FileNotFoundException("Rewrite Rhythia executable was not found.");
        var dataDirectory = Path.Combine(gameDirectory, "data_Rhythia_windows_x86_64");
        if (!Directory.Exists(dataDirectory)) throw new DirectoryNotFoundException("Rewrite Rhythia data_Rhythia_windows_x86_64 directory was not found.");
        var dllCount = Directory.EnumerateFiles(dataDirectory, "*.dll", SearchOption.TopDirectoryOnly).Count();
        if (dllCount < 7) throw new InvalidOperationException($"Rewrite Rhythia data directory was found but only {dllCount} DLL files were detected; expected at least 7.");
        WriteBridge(gameDirectory, "RewriteRhythia", exe, dataDirectory);
    }

    private static void WriteBridge(string gameDirectory, string target, string executable, string? secondaryPath = null)
    {
        var bridge = Path.Combine(gameDirectory, "RhythKit", "RhythKitBridge.json");
        var state = new { target, executable, secondaryPath, integration = "agent", installedAt = DateTimeOffset.UtcNow };
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
    public static RhythiaTarget Detect(string path)
    {
        if (!Directory.Exists(path)) return RhythiaTarget.Unknown;
        var rewrite = Path.Combine(path, "data_Rhythia_windows_x86_64");
        if (File.Exists(Path.Combine(path, "rhythia.exe")) && Directory.Exists(rewrite) && Directory.EnumerateFiles(rewrite, "*.dll", SearchOption.TopDirectoryOnly).Count() >= 7) return RhythiaTarget.RewriteRhythia;
        if (SteamRhythiaLayout.Matches(path)) return RhythiaTarget.RhythiaSteam;
        if (File.Exists(Path.Combine(path, "Vulnus.exe")) || (File.Exists(Path.Combine(path, "project.godot")) && Directory.EnumerateFiles(path, "*.pck", SearchOption.TopDirectoryOnly).Any(x => Path.GetFileName(x).Contains("Vulnus", StringComparison.OrdinalIgnoreCase)))) return RhythiaTarget.Vulnus;
        if (File.Exists(Path.Combine(path, "soundspaceplus.exe")) || File.Exists(Path.Combine(path, "Sound Space Plus.exe")) || File.Exists(Path.Combine(path, "SSP.exe"))) return RhythiaTarget.SspNightly;
        if (File.Exists(Path.Combine(path, "Rhythia.dll")) || Directory.EnumerateFiles(path, "Rhythia.dll", SearchOption.AllDirectories).Any()) return RhythiaTarget.LegacyManaged;
        return RhythiaTarget.Unknown;
    }

    public static bool Validate(string path, RhythiaTarget target, out string error)
    {
        error = string.Empty;
        var executable = FindGameExecutable(path, target);
        if (executable == null)
        {
            error = $"The selected folder does not contain the expected executable for {target}.";
            return false;
        }
        if (target == RhythiaTarget.RewriteRhythia)
        {
            var data = Path.Combine(path, "data_Rhythia_windows_x86_64");
            if (!Directory.Exists(data) || Directory.EnumerateFiles(data, "*.dll", SearchOption.TopDirectoryOnly).Count() < 7)
            {
                error = "Rewrite Rhythia requires rhythia.exe and data_Rhythia_windows_x86_64 with at least 7 DLL files.";
                return false;
            }
        }
        if (target == RhythiaTarget.SspNightly && !Directory.EnumerateFiles(path, "*.pck", SearchOption.TopDirectoryOnly).Any())
        {
            error = "Sound Space Plus requires its .pck file in the game directory.";
            return false;
        }
        return true;
    }

    public static string? FindGameExecutable(string directory, RhythiaTarget target) => target switch
    {
        RhythiaTarget.RhythiaSteam => SteamRhythiaLayout.ExecutablePath(directory),
        RhythiaTarget.SspNightly => FindExecutable(directory, "soundspaceplus.exe", "Sound Space Plus.exe", "SSP.exe"),
        RhythiaTarget.Vulnus => FindExecutable(directory, "Vulnus.exe"),
        RhythiaTarget.RewriteRhythia => FindExecutable(directory, "rhythia.exe", "Rhythia.exe"),
        RhythiaTarget.LegacyManaged => FindExecutable(directory, "Rhythia.exe", "rhythia.exe"),
        _ => null
    };

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
