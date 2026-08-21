namespace RhythKit;

public static class RhythiaIntegrationContract
{
    public const string IntegrationName = "RhythKit";
    public const string IntegrationVersion = "1.0";
    public const string MinimumSupportedGameMajor = "0";
    public const string MaximumSupportedGameMajor = "0";

    public static bool IsSupportedGameVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        var value = version.Trim();
        var separator = value.IndexOf('.');
        var majorText = separator < 0 ? value : value[..separator];

        return int.TryParse(majorText, out var major)
            && int.TryParse(MinimumSupportedGameMajor, out var minimum)
            && int.TryParse(MaximumSupportedGameMajor, out var maximum)
            && major >= minimum
            && major <= maximum;
    }

    public static bool IsCompletionEvent(string? eventName)
    {
        return string.Equals(eventName, "MapCompleted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventName, "ScoreCompleted", StringComparison.OrdinalIgnoreCase);
    }
}
