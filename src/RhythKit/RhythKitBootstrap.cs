using System.Reflection;
using System.Text.Json;
using Godot;

namespace RhythKit;

public static class RhythKitBootstrap
{
    private static RhythiansClient? client;
    private static RhythKitAgentBridge? bridge;
    private static string? token;
    private static string? username;
    private static bool initialized;
    private static bool resultQueued;
    private static Button? statusButton;
    private static Button? connectButton;
    private static Button? testButton;

    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        client = RhythiansClient.CreateDefault();
        bridge = new RhythKitAgentBridge();
        token = TokenStore.Load();
        RhythiansMapStore.Initialize();
        RhythiansMapWatcher.Start();
        _ = bridge.ReportStateAsync(new GameConnectionState(true, true, true, "Rhythia", GetGameVersion(), null, "IntegrationLoaded"));
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;
        tree.NodeAdded += OnNodeAdded;
        Callable.From(InstallGui).CallDeferred();
    }

    private static void OnNodeAdded(Node node)
    {
        if (node.GetType().Name == "MainMenu") Callable.From(InstallGui).CallDeferred();
        if (node.GetType().Name == "Results")
        {
            resultQueued = false;
            Callable.From(ProcessResult).CallDeferred();
        }
    }

    private static void InstallGui()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        var root = tree?.CurrentScene;
        if (root == null || root.GetType().Name != "MainMenu") return;
        var buttons = root.GetNodeOrNull("Menus/Home/Buttons");
        if (buttons == null) return;

        statusButton ??= CreateButton(buttons, "RhythiansStatus", "Rhythians: Checking...");
        connectButton ??= CreateButton(buttons, "RhythiansConnect", "Login to Rhythians");
        testButton ??= CreateButton(buttons, "RhythiansTest", "Test Connection");
        var mapsButton = buttons.GetNodeOrNull<Button>("RhythiansMaps") ?? CreateButton(buttons, "RhythiansMaps", "See Rhythians Maps");

        connectButton.Pressed -= OnConnectPressed;
        connectButton.Pressed += OnConnectPressed;
        testButton.Pressed -= OnTestPressed;
        testButton.Pressed += OnTestPressed;
        mapsButton.Pressed -= OnMapsPressed;
        mapsButton.Pressed += OnMapsPressed;
        _ = RefreshConnectionAsync();
    }

    private static Button CreateButton(Node parent, string name, string text)
    {
        var existing = parent.GetNodeOrNull<Button>(name);
        if (existing != null) return existing;
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new Vector2(0, 48) };
        parent.AddChild(button);
        return button;
    }

    private static void OnConnectPressed() { if (connectButton != null) _ = ConnectAsync(connectButton); }
    private static void OnTestPressed() { _ = RefreshConnectionAsync(true); }
    private static void OnMapsPressed() { try { RhythiansMapStore.OpenRoot(); } catch (Exception exception) { GD.PrintErr($"[RhythKit] {exception}"); } }

    private static async Task RefreshConnectionAsync(bool showResult = false)
    {
        if (client == null || statusButton == null) return;
        statusButton.Text = "Rhythians: Checking...";
        try
        {
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException();
            var result = await client.TestConnectionAsync(token);
            if (result?.Ok != true || result.Authenticated != true || string.IsNullOrWhiteSpace(result.Username)) throw new InvalidOperationException();
            username = result.Username;
            statusButton.Text = $"Rhythians: Connected ({username})";
            if (connectButton != null) connectButton.Visible = false;
            if (showResult) GD.Print($"[RhythKit] Rhythians connection test passed for {username}.");
        }
        catch
        {
            username = null;
            statusButton.Text = "Rhythians: Not connected";
            if (connectButton != null) connectButton.Visible = true;
            if (showResult) GD.PrintErr("[RhythKit] Rhythians connection test failed.");
        }
    }

    private static async Task ConnectAsync(Button button)
    {
        if (client == null) return;
        button.Disabled = true;
        try
        {
            token = await new RhythKitConnection(client).ConnectAsync();
            TokenStore.Save(token);
            await RefreshConnectionAsync();
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[RhythKit] {exception}");
            button.Visible = true;
            statusButton!.Text = "Rhythians: Not connected";
        }
        finally { button.Disabled = false; }
    }

    private static async void ProcessResult()
    {
        if (resultQueued || bridge == null) return;
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
            if (rhythiansMapId == null && mapPath != null) rhythiansMapId = RhythiansMapIdentity.ResolveFromSspm(mapPath) ?? RhythiansMapIdentity.ResolveFromRhm(mapPath);
            if (string.IsNullOrWhiteSpace(rhythiansMapId)) return;
            RhythiansMapStore.Capture(map, rhythiansMapId);
            var accuracy = GetNullableDouble(attempt, "Accuracy");
            var misses = GetNullableInt(attempt, "Misses");
            if (accuracy == null || misses == null) return;
            await bridge.ReportEventAsync(new GameEvent("MapCompleted", true, true, true, "Rhythia", GetGameVersion(), rhythiansMapId));
            var score = new ScoreSubmission(
                rhythiansMapId,
                GetString(attempt, "ID") ?? Guid.NewGuid().ToString("N"),
                accuracy,
                misses,
                GetNullableDouble(attempt, "Speed"),
                GetGameVersion(),
                "1",
                DateTimeOffset.UtcNow);
            var response = await bridge.SubmitScoreAsync(score);
            var message = response.Counted ? "CompletionAwarded" : response.Duplicate ? "CompletionAlreadyRecorded" : "ScoreProcessed";
            await bridge.ReportEventAsync(new GameEvent(message, true, true, true, "Rhythia", GetGameVersion(), rhythiansMapId));
            GD.Print($"[RhythKit] {message}: {response.Points} RHP, total {response.Rhp} RHP.");
        }
        catch (Exception exception) { GD.PrintErr($"[RhythKit] Score submission failed: {exception}"); }
    }

    private static string GetGameVersion()
    {
        var version = typeof(RhythKitBootstrap).Assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
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
