using Microsoft.Win32;
using System.Diagnostics;

namespace RhythKit.Uninstaller;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form
        {
            Text = "Uninstall RhythKit",
            Width = 420,
            Height = 190,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };
        var label = new Label
        {
            Text = "Remove RhythKit from this game installation?",
            AutoSize = true,
            Left = 24,
            Top = 24
        };
        var uninstall = new Button
        {
            Text = "Uninstall",
            AutoSize = true,
            Left = 24,
            Top = 75
        };
        var cancel = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            Left = 125,
            Top = 75,
            DialogResult = DialogResult.Cancel
        };
        uninstall.Click += (_, _) =>
        {
            var gameDirectory = Directory.GetParent(AppContext.BaseDirectory)?.FullName;
            if (string.IsNullOrWhiteSpace(gameDirectory)) return;
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                key?.DeleteValue("RhythKit", false);
            }
            var script = Path.Combine(Path.GetTempPath(), $"rhythkit-uninstall-{Guid.NewGuid():N}.cmd");
            var self = Environment.ProcessPath ?? "";
            var escapedSelf = self.Replace("\"", "\"\"");
            var escapedDirectory = Path.Combine(gameDirectory, "RhythKit").Replace("\"", "\"\"");
            File.WriteAllText(script, $"@echo off\r\ntimeout /t 2 /nobreak >nul\r\ntaskkill /f /im RhythKit.Agent.exe >nul 2>&1\r\nrmdir /s /q \"{escapedDirectory}\"\r\ndel /f /q \"{escapedSelf}\" >nul 2>&1\r\ndel /f /q \"%~f0\" >nul 2>&1\r\n");
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            form.Close();
        };
        form.Controls.Add(label);
        form.Controls.Add(uninstall);
        form.Controls.Add(cancel);
        form.CancelButton = cancel;
        Application.Run(form);
    }
}
