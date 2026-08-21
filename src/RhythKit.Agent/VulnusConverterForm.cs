using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace RhythKit.Agent;

internal sealed class VulnusConverterForm : Form
{
    private readonly HttpClient client;
    private readonly Label selected = new() { AutoSize = true, Text = "No SSPM file selected." };
    private readonly Button convert = new() { Text = "Convert to Vulnus", AutoSize = true, Enabled = false };
    private string? selectedPath;

    public VulnusConverterForm()
    {
        client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:45874/"), Timeout = TimeSpan.FromMinutes(2) };
        Text = "RhythKit Vulnus Map Converter";
        Width = 560;
        Height = 220;
        StartPosition = FormStartPosition.CenterScreen;
        var title = new Label { Text = "Vulnus Map Converter", AutoSize = true, Font = new Font(Font.FontFamily, 16, FontStyle.Bold) };
        var choose = new Button { Text = "Select SSPM", AutoSize = true };
        choose.Click += (_, _) => SelectFile();
        convert.Click += async (_, _) => await ConvertAsync();
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        panel.Controls.Add(title);
        panel.Controls.Add(selected);
        panel.Controls.Add(choose);
        panel.Controls.Add(convert);
        Controls.Add(panel);
        FormClosed += (_, _) => client.Dispose();
    }

    private void SelectFile()
    {
        using var dialog = new OpenFileDialog { Filter = "Sound Space Plus maps (*.sspm)|*.sspm" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        selectedPath = dialog.FileName;
        selected.Text = Path.GetFileName(selectedPath);
        convert.Enabled = true;
    }

    private async Task ConvertAsync()
    {
        if (string.IsNullOrWhiteSpace(selectedPath)) return;
        convert.Enabled = false;
        try
        {
            using var form = new MultipartFormDataContent();
            await using var stream = File.OpenRead(selectedPath);
            using var file = new StreamContent(stream);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", Path.GetFileName(selectedPath));
            using var response = await client.PostAsync("vulnus/convert", form);
            var result = await response.Content.ReadFromJsonAsync<ConvertResponse>();
            if (!response.IsSuccessStatusCode || result?.Ok != true)
            {
                MessageBox.Show(this, result?.Error ?? "Vulnus conversion failed.", "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            MessageBox.Show(this, $"Conversion complete.\n\n{result.Path}", "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "RhythKit", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { convert.Enabled = true; }
    }

    private sealed record ConvertResponse(bool Ok, string? Message, string? Path, string? Error);
}
