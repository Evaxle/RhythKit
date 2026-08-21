using System.Reflection;

namespace RhythKit.Installer;

internal static class RhythKitUninstallerPayload
{
    public static byte[] Data => Read("RhythKit.Uninstaller.exe");

    private static byte[] Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded payload: {name}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
