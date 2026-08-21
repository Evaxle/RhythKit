namespace RhythKit.Agent;

internal static class AgentPorts
{
    public static int For(string gameType) => gameType switch
    {
        "SspNightly" => 45873,
        "Vulnus" => 45874,
        _ => 45872
    };
}
