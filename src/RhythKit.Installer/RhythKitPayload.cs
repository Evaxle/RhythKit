using System.Reflection;

namespace RhythKit.Installer;

internal static class RhythKitPayload
{
    public static byte[] Data => Read("RhythKit.dll");

    private static byte[] Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded payload: {name}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
