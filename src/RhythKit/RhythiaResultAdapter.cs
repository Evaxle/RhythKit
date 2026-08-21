using System.Reflection;

namespace RhythKit;

public sealed record RhythiaCompletion(
    string MapId,
    string ResultId,
    double Accuracy,
    int Misses,
    double Speed,
    bool Passed,
    bool Qualified,
    DateTimeOffset CompletedAt);

public static class RhythiaResultAdapter
{
    private static readonly BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase;
    private static readonly BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
    private static Type? legacyRunnerType;

    public static bool TryReadCompletion(out RhythiaCompletion? completion)
    {
        completion = null;
        var runner = FindLegacyRunner();
        var attempt = GetMember(runner, "CurrentAttempt");
        if (attempt == null || GetBool(attempt, "IsReplay") || !GetBool(attempt, "Alive") || !GetBool(attempt, "Qualifies")) return false;

        var passed = GetOptionalBool(attempt, "Passed") ?? GetBool(attempt, "Alive") && GetBool(attempt, "Qualifies");
        if (!passed) return false;

        var map = GetMember(attempt, "Map");
        var mapId = RhythiansMapIdentity.Resolve(map);
        var path = GetString(map, "Path") ?? GetString(map, "FilePath") ?? GetString(map, "SourcePath");
        if (mapId == null && path != null)
            mapId = RhythiansMapIdentity.ResolveFromSspm(path) ?? RhythiansMapIdentity.ResolveFromRhm(path);
        if (!Guid.TryParse(mapId, out _)) return false;

        var accuracy = GetDouble(attempt, "Accuracy");
        var misses = GetInt(attempt, "Misses");
        var speed = GetDouble(attempt, "Speed") ?? 1d;
        if (accuracy is null || misses is null || accuracy < 0 || accuracy > 100 || misses < 0 || speed <= 0) return false;

        var resultId = GetString(attempt, "ID") ?? GetString(attempt, "Id");
        if (string.IsNullOrWhiteSpace(resultId)) resultId = Guid.NewGuid().ToString("N");

        completion = new RhythiaCompletion(
            mapId,
            resultId,
            accuracy.Value,
            misses.Value,
            speed.Value,
            passed,
            true,
            DateTimeOffset.UtcNow);
        return true;
    }

    public static string? GetGameVersion()
    {
        var assembly = FindLegacyRunner()?.Assembly;
        var version = assembly?.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(version) ? null : version;
    }

    private static Type? FindLegacyRunner()
    {
        if (legacyRunnerType != null) return legacyRunnerType;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                legacyRunnerType = assembly.GetTypes().FirstOrDefault(type => type.Name == "LegacyRunner");
                if (legacyRunnerType != null) return legacyRunnerType;
            }
            catch { }
        }
        return null;
    }

    private static object? GetMember(object? target, string name)
    {
        if (target == null) return null;
        var type = target.GetType();
        try
        {
            return type.GetProperty(name, InstanceFlags)?.GetValue(target) ?? type.GetField(name, InstanceFlags)?.GetValue(target);
        }
        catch { return null; }
    }

    private static object? GetMember(Type? target, string name)
    {
        if (target == null) return null;
        try
        {
            return target.GetProperty(name, StaticFlags)?.GetValue(null) ?? target.GetField(name, StaticFlags)?.GetValue(null);
        }
        catch { return null; }
    }

    private static string? GetString(object? target, string name) => GetMember(target, name)?.ToString();

    private static bool GetBool(object? target, string name) => GetMember(target, name) is bool value && value;

    private static bool? GetOptionalBool(object? target, string name) => GetMember(target, name) switch
    {
        bool value => value,
        _ => null
    };

    private static double? GetDouble(object? target, string name) => GetMember(target, name) switch
    {
        double value => value,
        float value => value,
        decimal value => (double)value,
        int value => value,
        uint value => value,
        long value => value,
        _ => null
    };

    private static int? GetInt(object? target, string name) => GetMember(target, name) switch
    {
        int value => value,
        uint value when value <= int.MaxValue => (int)value,
        long value when value >= 0 && value <= int.MaxValue => (int)value,
        _ => null
    };
}
