using System.Diagnostics;
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
        CallDeferred(nameof(InstallLoginButton));
    }

    private static void OnNodeAdded(Node node)
    {
        if (node.GetType().Name == "MainMenu")
        {
            node.CallDeferred(nameof(InstallLoginButton));
        }

        if (node.GetType().Name == "Results")
        {
            resultQueued = false;
            node.CallDeferred(nameof(ProcessResult));
        }
    }

    private static void InstallLoginButton()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        var root = tree?.CurrentScene;
        if (root == null || root.GetType().Name != "MainMenu") return;
        var buttons = root.GetNodeOrNull("Menus/Home/Buttons");
        if (buttons == null || buttons.GetNodeOrNull("RhythianLogin") != null) return;

        var button = new Button
        {
            Name = "RhythianLogin",
            Text = token == null ? "Rhythian Login" : "Rhythian Connected",
            CustomMinimumSize = new Vector2(0, 52),
            TooltipText = "Connect Rhythia to your Rhythians account"
        };
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
        finally
        {
            button.Disabled = false;
        }
    }

    private static async void ProcessResult()
    {
        if (resultQueued || token == null || client == null) return;
        resultQueued = true;
        await Task.Yield();

        try
        {
            var legacyRunner = FindType("LegacyRunner");
            var attemptProperty = legacyRunner?.GetProperty("CurrentAttempt", BindingFlags.Public | BindingFlags.Static);
            var attemptField = legacyRunner?.GetField("CurrentAttempt", BindingFlags.Public | BindingFlags.Static);
            var attempt = attemptProperty?.GetValue(null) ?? attemptField?.GetValue(null);
            if (attempt == null) return;

            if (GetBool(attempt, "IsReplay") || !GetBool(attempt, "Alive") || !GetBool(attempt, "Qualifies")) return;

            var map = GetValue(attempt, "Map");
            var mapId = GetString(map, "ID");
            if (string.IsNullOrWhiteSpace(mapId)) return;

            var mapCheck = await new RhythiansApi(new HttpClient { BaseAddress = new Uri("https://rhythians.vercel.app/") }).CheckMapAsync(token, mapId);
            if (mapCheck == null || !mapCheck.Eligible) return;

            var score = new ScoreSubmission(
                mapId,
                GetString(attempt, "ID") ?? Guid.NewGuid().ToString("N"),
                GetNullableDouble(attempt, "Accuracy"),
                GetNullableInt(attempt, "Misses"),
                GetNullableDouble(attempt, "Speed"));

            await client.SubmitScoreAsync(token, score);
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[RhythKit] Score submission failed: {exception}");
        }
    }

    private static Type? FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes).FirstOrDefault(type => type.Name == name);

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); } catch { return []; }
    }

    private static object? GetValue(object? target, string name)
    {
        if (target == null) return null;
        var type = target.GetType();
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
    }

    private static string? GetString(object? target, string name) => GetValue(target, name)?.ToString();
    private static bool GetBool(object? target, string name) => GetValue(target, name) is bool value && value;
    private static double? GetNullableDouble(object? target, string name) => GetValue(target, name) is double value ? value : null;
    private static int? GetNullableInt(object? target, string name) => GetValue(target, name) switch { int value => value, uint value when value <= int.MaxValue => (int)value, _ => null };

    private static class TokenStore
    {
        private sealed record State(string Token);
        private static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rhythians", "rhythkit.json");

        public static string? Load()
        {
            try
            {
                if (!File.Exists(Path)) return null;
                return JsonSerializer.Deserialize<State>(File.ReadAllText(Path))?.Token;
            }
            catch { return null; }
        }

        public static void Save(string value)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(new State(value)));
        }
    }
}
