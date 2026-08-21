namespace RhythKit.Agent;

internal static class AgentPorts
{
    public static int For(string gameType) => gameType switch
    {
        "SspNightly" or "SSP" or "Ssp" or "SoundSpacePlus" => 45873,
        "Vulnus" => 45874,
        _ => 45872
    };
}
