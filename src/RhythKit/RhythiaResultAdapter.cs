using System.Reflection;

namespace RhythKit;

public static class RhythiaResultAdapter
{
    private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.Static;
    private static Type? legacyRunnerType;

    public static bool TryReadCompletion(object? attempt, out RhythiaCompletion? completion)
    {
        completion = null;
        if (attempt == null) return false;

        var alive = GetBool(attempt, "Alive");
        var qualifies = GetBool(attempt, "Qualifies");
        var isReplay = GetBool(attempt, "IsReplay");
        var passed = GetOptionalBool(attempt, "Passed") ?? qualifies;
        if (!alive || !qualifies || isReplay) return false;

        var map = GetMember(attempt, "Map");
        var mapId = GetString(attempt, "MapID") ?? GetString(attempt, "MapId");
        if (string.IsNullOrWhiteSpace(mapId)) mapId = RhythiansMapIdentity.Resolve(map);
        var path = GetString(map, "Path") ?? GetString(map, "FilePath") ?? GetString(map, "SourcePath");
        if (string.IsNullOrWhiteSpace(mapId) && path != null)
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
            speed,
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
    private static bool? GetOptionalBool(object? target, string name) => GetMember(target, name) switch { bool value => value, _ => null };
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
        short value => value,
        ushort value => value,
        long value when value is >= int.MinValue and <= int.MaxValue => (int)value,
        _ => null
    };
}
