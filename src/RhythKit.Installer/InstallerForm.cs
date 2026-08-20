using System.Diagnostics;
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
            var modDirectory = Path.Combine(path, "RhythKit");
            Directory.CreateDirectory(modDirectory);
            var sourceAssembly = Path.Combine(AppContext.BaseDirectory, "RhythKit.dll");
            if (File.Exists(sourceAssembly)) File.Copy(sourceAssembly, Path.Combine(modDirectory, "RhythKit.dll"), true);
            var manifest = new { id = "rhythkit", name = "RhythKit", version = "0.1.0", assembly = "RhythKit.dll" };
            await File.WriteAllTextAsync(Path.Combine(modDirectory, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            status.Text = "RhythKit files installed. The game integration loader must be present before the assembly can load.";
            Process.Start(new ProcessStartInfo { FileName = "https://rhythians.vercel.app", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
        }
        finally
        {
            install.Enabled = true;
        }
    }
}
