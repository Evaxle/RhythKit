using System.Security.Cryptography;
using System.Text;

namespace RhythKit.Agent;

public sealed class SspScoreWatcher
{
    readonly string? gameDirectory;
    readonly HashSet<string> seenRecords = new(StringComparer.OrdinalIgnoreCase);
    readonly CancellationToken cancellationToken;

    public SspScoreWatcher(string? gameDirectory, CancellationToken cancellationToken)
    {
        this.gameDirectory = gameDirectory;
        this.cancellationToken = cancellationToken;
    }

    public async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory)) return;
        foreach (var directory in FindBestDirectories()) Seed(directory);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                foreach (var directory in FindBestDirectories())
                    await ScanAsync(directory).ConfigureAwait(false);
            }
            catch { }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
    }

    IEnumerable<string> FindBestDirectories()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var godot = Path.Combine(appData, "Godot", "app_userdata");
        if (!Directory.Exists(godot)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(godot, "*", SearchOption.AllDirectories))
        {
            var normalized = directory.Replace('\\', '/');
            var name = Path.GetFileName(directory);
            if (!name.Contains("ssp", StringComparison.OrdinalIgnoreCase) && !name.Contains("sound space", StringComparison.OrdinalIgnoreCase)) continue;
            var bests = Path.Combine(directory, "bests");
            if (Directory.Exists(bests)) yield return bests;
        }
    }

    void Seed(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (!TryRead(file, out var mapId, out var records)) continue;
            foreach (var record in records) seenRecords.Add(CreateRecordKey(mapId, record));
        }
    }

    async Task ScanAsync(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (!TryRead(file, out var mapId, out var records)) continue;
            foreach (var record in records.Where(x => x.Passed && x.TotalNotes > 0))
            {
                var recordKey = CreateRecordKey(mapId, record);
                if (!seenRecords.Add(recordKey)) continue;
                var accuracy = Math.Clamp(record.HitNotes * 100.0 / record.TotalNotes, 0, 100);
                var misses = Math.Max(0, record.TotalNotes - record.HitNotes);
                await SubmitAsync(mapId, CreateScoreId(recordKey), accuracy, misses).ConfigureAwait(false);
            }
        }
    }

    static string CreateRecordKey(string mapId, PbRecord record)
    {
        return $"{mapId}:{record.Key}:{record.Passed}:{record.Pauses}:{record.HitNotes}:{record.TotalNotes}:{record.Position}:{record.Length}";
    }

    static string CreateScoreId(string recordKey)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ssp:" + recordKey))).ToLowerInvariant();
    }

    static bool TryRead(string file, out string mapId, out List<PbRecord> records)
    {
        mapId = Path.GetFileName(file);
        records = [];
        try
        {
            using var stream = File.OpenRead(file);
            using var reader = new BinaryReader(stream, Encoding.UTF8, false);
            var signature = reader.ReadBytes(5);
            if (!signature.SequenceEqual(new byte[] { 0x53, 0x53, 0x2B, 0x70, 0x42 })) return false;
            var version = reader.ReadUInt16();
            if (version > 3) return false;
            var count = reader.ReadUInt64();
            if (count > 10000) return false;
            for (ulong i = 0; i < count; i++)
            {
                var key = ReadGodotLine(reader);
                var passed = reader.ReadByte() != 0;
                var pauses = reader.ReadUInt16();
                var hits = reader.ReadUInt32();
                var total = reader.ReadUInt32();
                var position = reader.ReadUInt32();
                var length = reader.ReadUInt32();
                if (version >= 3) reader.ReadUInt16();
                records.Add(new PbRecord(key, passed, pauses, hits, total, position, length));
            }
            return true;
        }
        catch { return false; }
    }

    static string ReadGodotLine(BinaryReader reader)
    {
        var bytes = new List<byte>();
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var value = reader.ReadByte();
            if (value == 10) break;
            if (value != 13) bytes.Add(value);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    static async Task SubmitAsync(string mapId, string clientScoreId, double accuracy, int misses)
    {
        var state = TokenStore.Load();
        if (state == null || string.IsNullOrWhiteSpace(state.Token)) return;
        using var client = new HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app/") };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", state.Token);
        var body = new { challengeMapId = mapId, clientScoreId, accuracy, misses, speed = (double?)null };
        try
        {
            await client.PostAsJsonAsync("api/rhythkit/scores", body).ConfigureAwait(false);
        }
        catch { }
    }

    readonly record struct PbRecord(string Key, bool Passed, ushort Pauses, uint HitNotes, uint TotalNotes, uint Position, uint Length);
}
