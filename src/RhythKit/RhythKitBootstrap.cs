using System.Reflection;
using System.Text.Json;
using Godot;

namespace RhythKit;

public static class RhythKitBootstrap
{
    private static RhythiansClient? client;
    private static string? token;
    private static bool initialized;
    private static bool resultQueued;

    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        client = RhythiansClient.CreateDefault();
        token = TokenStore.Load();
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;
        tree.NodeAdded += OnNodeAdded;
        Callable.From(InstallLoginButton).CallDeferred();
    }

    private static void OnNodeAdded(Node node)
    {
        if (node.GetType().Name == "MainMenu") Callable.From(InstallLoginButton).CallDeferred();
        if (node.GetType().Name == "Results")
        {
            resultQueued = false;
            Callable.From(ProcessResult).CallDeferred();
        }
    }

    private static void InstallLoginButton()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        var root = tree?.CurrentScene;
        if (root == null || root.GetType().Name != "MainMenu") return;
        var buttons = root.GetNodeOrNull("Menus/Home/Buttons");
        if (buttons == null || buttons.GetNodeOrNull("RhythianLogin") != null) return;
        var button = new Button { Name = "RhythianLogin", Text = token == null ? "Rhythian Login" : "Rhythian Connected", CustomMinimumSize = new Vector2(0, 52), TooltipText = "Connect Rhythia to your Rhythians account" };
        button.Pressed += () => _ = ConnectAsync(button);
        buttons.AddChild(button);
    }

    private static async Task ConnectAsync(Button button)
    {
        if (client == null) return;
        button.Disabled = true;
        try
        {
            token = await new RhythKitConnection(client).ConnectAsync();
            TokenStore.Save(token);
            button.Text = "Rhythian Connected";
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[RhythKit] {exception}");
            button.Text = "Rhythian Login";
        }
        finally { button.Disabled = false; }
    }

    private static async void ProcessResult()
    {
        if (resultQueued || token == null || client == null) return;
        resultQueued = true;
        await Task.Yield();
        try
        {
            var legacyRunner = FindType("LegacyRunner");
            var attempt = legacyRunner?.GetProperty("CurrentAttempt", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ?? legacyRunner?.GetField("CurrentAttempt", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (attempt == null || GetBool(attempt, "IsReplay") || !GetBool(attempt, "Alive") || !GetBool(attempt, "Qualifies")) return;
            var map = GetValue(attempt, "Map");
            var rhythiansMapId = RhythiansMapIdentity.Resolve(map);
            var mapPath = GetString(map, "Path") ?? GetString(map, "FilePath") ?? GetString(map, "SourcePath");
            if (rhythiansMapId == null && mapPath != null)
            {
                rhythiansMapId = RhythiansMapIdentity.ResolveFromSspm(mapPath) ?? RhythiansMapIdentity.ResolveFromRhm(mapPath);
            }
            var lookupId = rhythiansMapId ?? GetString(map, "Name");
            if (string.IsNullOrWhiteSpace(lookupId)) return;
            var mapCheck = await new RhythiansApi(new System.Net.Http.HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app/") }).CheckMapAsync(token, lookupId);
            if (mapCheck == null || !mapCheck.Eligible) return;
            var score = new ScoreSubmission(mapCheck.Id, GetString(attempt, "ID") ?? Guid.NewGuid().ToString("N"), GetNullableDouble(attempt, "Accuracy"), GetNullableInt(attempt, "Misses"), GetNullableDouble(attempt, "Speed"));
            await client.SubmitScoreAsync(token, score);
        }
        catch (Exception exception) { GD.PrintErr($"[RhythKit] Score submission failed: {exception}"); }
    }

    private static Type? FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes).FirstOrDefault(type => type.Name == name);
    private static IEnumerable<Type> SafeTypes(Assembly assembly) { try { return assembly.GetTypes(); } catch { return []; } }
    private static object? GetValue(object? target, string name)
    {
        if (target == null) return null;
        var type = target.GetType();
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
    }
    private static string? GetString(object? target, string name) => GetValue(target, name)?.ToString();
    private static bool GetBool(object? target, string name) => GetValue(target, name) is bool value && value;
    private static double? GetNullableDouble(object? target, string name) => GetValue(target, name) switch { double value => value, float value => value, _ => null };
    private static int? GetNullableInt(object? target, string name) => GetValue(target, name) switch { int value => value, uint value when value <= int.MaxValue => (int)value, _ => null };

    private static class TokenStore
    {
        private sealed record State(string Token);
        private static string Path => System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "Rhythians", "rhythkit.json");
        public static string? Load()
        {
            try { return File.Exists(Path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(Path))?.Token : null; } catch { return null; }
        }
        public static void Save(string value)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(new State(value)));
        }
    }
}
