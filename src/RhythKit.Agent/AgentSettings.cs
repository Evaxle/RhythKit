namespace RhythKit.Agent;

internal sealed class AgentSettings
{
    public string? GameDirectory { get; set; }
    public string? GameType { get; set; }
    public bool StartWithGame { get; set; } = true;
}
