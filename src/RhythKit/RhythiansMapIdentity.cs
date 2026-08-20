using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RhythKit;

public static class RhythiansMapIdentity
{
    private const string SspmField = "rhythians_id_enc_v1";
    private static readonly byte[] SspmKey = Convert.FromBase64String("Jxr47SL1pmalG27ETO4hN8JR1VIHx4JfmgogLHM9t84=");

    public static string? Resolve(object? map)
    {
        var embedded = ReadProperty(map, "RhythiansId") ?? ReadProperty(map, "rhythiansId");
        if (IsGuid(embedded)) return embedded;
        var path = ReadProperty(map, "Path") ?? ReadProperty(map, "FilePath") ?? ReadProperty(map, "SourcePath");
        if (path != null)
        {
            var sspm = ResolveFromSspm(path);
            if (sspm != null) return sspm;
            var rhm = ResolveFromRhm(path);
            if (rhm != null) return rhm;
        }
        var filenameId = ExtractGuidFromPath(path);
        if (filenameId != null) return filenameId;
        var legacyId = ReadProperty(map, "LegacyId");
        return IsGuid(legacyId) ? legacyId : null;
    }

    public static string? ResolveFromSspm(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 128 || BitConverter.ToUInt32(data, 0) != 0x6d2b5353 || BitConverter.ToUInt16(data, 4) != 2) return null;
            var customOffset = checked((long)BitConverter.ToUInt64(data, 48));
            var customLength = checked((long)BitConverter.ToUInt64(data, 56));
            if (customLength < 2 || customOffset < 0 || customOffset + customLength > data.Length) return null;
            var offset = checked((int)customOffset);
            var end = checked(offset + (int)customLength);
            var fields = BitConverter.ToUInt16(data, offset);
            offset += 2;
            for (var index = 0; index < fields && offset < end; index++)
            {
                if (offset + 2 > end) return null;
                var idLength = BitConverter.ToUInt16(data, offset);
                offset += 2;
                if (offset + idLength + 1 > end) return null;
                var id = Encoding.UTF8.GetString(data, offset, idLength);
                offset += idLength;
                var type = data[offset++];
                if (id == SspmField && type == 0x08)
                {
                    if (offset + 2 > end) return null;
                    var length = BitConverter.ToUInt16(data, offset);
                    offset += 2;
                    if (offset + length > end) return null;
                    return DecryptSspmIdentity(data.AsSpan(offset, length));
                }
                offset = SkipValue(data, offset, end, type);
            }
        }
        catch { }
        return null;
    }

    public static string? ResolveFromRhm(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            var entry = archive.GetEntry("map") ?? archive.GetEntry("Map");
            if (entry == null) return null;
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("RhythiansId", out var id) && IsGuid(id.GetString())) return id.GetString();
            if (document.RootElement.TryGetProperty("rhythiansId", out id) && IsGuid(id.GetString())) return id.GetString();
        }
        catch { }
        return null;
    }

    private static string? DecryptSspmIdentity(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 30 || payload[0] != 1) return null;
        var nonce = payload.Slice(1, 12);
        var tag = payload.Slice(13, 16);
        var ciphertext = payload.Slice(29);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(SspmKey, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        var value = Encoding.UTF8.GetString(plaintext);
        return IsGuid(value) ? value : null;
    }

    private static int SkipValue(byte[] data, int offset, int end, byte type) => type switch
    {
        0x01 => CheckedAdvance(offset, 1, end),
        0x02 => CheckedAdvance(offset, 2, end),
        0x03 or 0x05 => CheckedAdvance(offset, 4, end),
        0x04 or 0x06 => CheckedAdvance(offset, 8, end),
        0x07 => SkipPosition(data, offset, end),
        0x08 or 0x09 => SkipShortBuffer(data, offset, end),
        0x0a or 0x0b => SkipLongBuffer(data, offset, end),
        0x0c => SkipArray(data, offset, end),
        _ => throw new InvalidDataException("Unsupported SSPM custom field type.")
    };

    private static int SkipPosition(byte[] data, int offset, int end) => data[offset] == 0 ? CheckedAdvance(offset, 3, end) : CheckedAdvance(offset, 9, end);

    private static int SkipShortBuffer(byte[] data, int offset, int end)
    {
        if (offset + 2 > end) throw new InvalidDataException();
        return CheckedAdvance(offset + 2, BitConverter.ToUInt16(data, offset), end);
    }

    private static int SkipLongBuffer(byte[] data, int offset, int end)
    {
        if (offset + 4 > end) throw new InvalidDataException();
        return CheckedAdvance(offset + 4, checked((int)BitConverter.ToUInt32(data, offset)), end);
    }

    private static int SkipArray(byte[] data, int offset, int end)
    {
        if (offset + 4 > end) throw new InvalidDataException();
        return CheckedAdvance(offset + 4, checked((int)BitConverter.ToUInt32(data, offset)), end);
    }

    private static int CheckedAdvance(int offset, int amount, int end)
    {
        var next = checked(offset + amount);
        if (next > end) throw new InvalidDataException();
        return next;
    }

    private static string? ReadProperty(object? target, string name)
    {
        if (target == null) return null;
        try
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return property?.GetValue(target)?.ToString();
        }
        catch { return null; }
    }

    private static string? ExtractGuidFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var filename = System.IO.Path.GetFileName(path);
        var match = System.Text.RegularExpressions.Regex.Match(filename, @"rhythians-([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool IsGuid(string? value) => Guid.TryParse(value, out _);
}
