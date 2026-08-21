using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace RhythKit.Agent;

internal static class TokenStore
{
    private static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rhythians");
    private static string StatePath => Path.Combine(DirectoryPath, "rhythkit.json");
    private static string SettingsPath => Path.Combine(DirectoryPath, "rhythkit-agent-settings.json");

    public static AgentState? Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            var stored = JsonSerializer.Deserialize<StoredState>(File.ReadAllText(StatePath));
            if (stored == null) return null;
            return new AgentState(string.IsNullOrWhiteSpace(stored.Token) ? string.Empty : Unprotect(stored.Token), stored.Username, stored.Game);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string token, string? username, string? game)
    {
        Directory.CreateDirectory(DirectoryPath);
        var stored = new StoredState(string.IsNullOrWhiteSpace(token) ? string.Empty : Protect(token), username, game);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(stored));
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(StatePath)) File.Delete(StatePath);
        }
        catch { }
    }

    public static void SaveGame(string game)
    {
        var state = Load();
        if (state == null)
        {
            Save(string.Empty, null, game);
            return;
        }
        Save(state.Token ?? string.Empty, state.Username, game);
    }

    public static AgentSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AgentSettings();
            return JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(SettingsPath)) ?? new AgentSettings();
        }
        catch
        {
            return new AgentSettings();
        }
    }

    public static void SaveSettings(AgentSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
    }

    private static string Protect(string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        var encrypted = CryptProtect(data);
        return Convert.ToBase64String(encrypted);
    }

    private static string Unprotect(string value)
    {
        var data = Convert.FromBase64String(value);
        return Encoding.UTF8.GetString(CryptUnprotect(data));
    }

    private static byte[] CryptProtect(byte[] data)
    {
        return Transform(data, true);
    }

    private static byte[] CryptUnprotect(byte[] data)
    {
        return Transform(data, false);
    }

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new DataBlob(data);
        var output = new DataBlob();
        try
        {
            var success = protect
                ? CryptProtectData(ref input.Blob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output.Blob)
                : CryptUnprotectData(ref input.Blob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output.Blob);
            if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Blob.cbData];
            Marshal.Copy(output.Blob.pbData, result, 0, result.Length);
            return result;
        }
        finally
        {
            input.Dispose();
            output.Dispose();
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB dataIn, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DATA_BLOB dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DATA_BLOB dataOut);

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    private sealed class DataBlob : IDisposable
    {
        public DATA_BLOB Blob;

        public DataBlob()
        {
        }

        public DataBlob(byte[] data)
        {
            Blob.cbData = data.Length;
            Blob.pbData = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, Blob.pbData, data.Length);
        }

        public void Dispose()
        {
            if (Blob.pbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Blob.pbData);
                Blob.pbData = IntPtr.Zero;
            }
        }
    }

    private sealed record StoredState(string Token, string? Username, string? Game);
}

internal sealed record AgentState(string? Token, string? Username, string? Game);
