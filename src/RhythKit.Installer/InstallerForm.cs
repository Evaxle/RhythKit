using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace RhythKit.Installer;

public sealed class InstallerForm : Form
{
    private readonly TextBox gamePath = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = true, Text = "Choose your Rhythia game folder." };
    private readonly Button install = new() { Text = "Install RhythKit", AutoSize = true };

    public InstallerForm()
    {
        Text = "RhythKit Installer";
        Width = 680;
        Height = 260;
        StartPosition = FormStartPosition.CenterScreen;

        var browse = new Button { Text = "Browse", AutoSize = true };
        browse.Click += (_, _) => Browse();
        install.Click += async (_, _) => await InstallAsync();

        var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16), ColumnCount = 2 };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(gamePath, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 0, 16, 16) };
        buttons.Controls.Add(install);

        Controls.Add(status);
        Controls.Add(buttons);
        Controls.Add(pathRow);
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select the Rhythia game folder" };
        if (dialog.ShowDialog(this) == DialogResult.OK) gamePath.Text = dialog.SelectedPath;
    }

    private async Task InstallAsync()
    {
        var path = gamePath.Text.Trim();
        if (!Directory.Exists(path))
        {
            MessageBox.Show(this, "Select a valid Rhythia game folder.", "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        install.Enabled = false;
        try
        {
            status.Text = "Locating Rhythia.dll...";
            var assemblyPath = RhythiaPatcher.FindRhythiaAssembly(path);
            var assemblyDirectory = Path.GetDirectoryName(assemblyPath)!;
            var modDirectory = Path.Combine(path, "RhythKit");
            Directory.CreateDirectory(modDirectory);

            var payload = RhythKitPayload.Data;
            if (payload.Length == 0) throw new InvalidOperationException("The installer was not built with the RhythKit payload. Run build.ps1.");

            var modAssembly = Path.Combine(assemblyDirectory, "RhythKit.dll");
            await File.WriteAllBytesAsync(modAssembly, payload);
            var hash = Convert.ToHexString(SHA256.HashData(payload));

            status.Text = "Patching Rhythia...";
            RhythiaPatcher.Patch(path);
            VerifyInstalled(path);

            var manifest = new
            {
                id = "rhythkit",
                name = "RhythKit",
                version = "0.1.0",
                assembly = "RhythKit.dll",
                assemblySha256 = hash,
                installedAt = DateTimeOffset.UtcNow
            };
            await File.WriteAllTextAsync(Path.Combine(modDirectory, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            status.Text = "RhythKit installed and verified. Start Rhythia and use Rhythian Login.";
            Process.Start(new ProcessStartInfo { FileName = "https://rhythians.vercel.app", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
            MessageBox.Show(this, ex.ToString(), "RhythKit installation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            install.Enabled = true;
        }
    }

    private static void VerifyInstalled(string gameDirectory)
    {
        var assemblyPath = RhythiaPatcher.FindRhythiaAssembly(gameDirectory);
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath)!;
        if (!File.Exists(Path.Combine(assemblyDirectory, "RhythKit.dll"))) throw new InvalidOperationException("RhythKit.dll was not installed beside Rhythia.dll.");
        if (!RhythiaPatcher.IsPatched(assemblyPath)) throw new InvalidOperationException("Rhythia.dll was not patched successfully.");
    }
}
