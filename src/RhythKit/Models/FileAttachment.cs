using System.IO;

namespace RhythKit.Models;

public class FileAttachment
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }

    public string Extension
    {
        get
        {
            var ext = Path.GetExtension(FileName);
            return string.IsNullOrEmpty(ext) ? "file" : ext.TrimStart('.').ToUpperInvariant();
        }
    }

    public string DisplaySize => FormatSize(SizeBytes);

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
