using Godot;
using System.Security.Cryptography;
using System.Text.Json;

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

        var version = RhythiaResultAdapter.GetGameVersion() ?? "unknown";
        _ = ReportStateAsync(version);

        if (Engine.GetMainLoop() is not SceneTree tree) return;
        tree.NodeAdded += OnNodeAdded;
        tree.TreeChanged += OnTreeChanged;
        Callable.From(InstallGui).CallDeferred();
    }

    private static async Task ReportStateAsync(string version)
    {
        if (bridge == null) return;
        try
        {
            await bridge.ReportStateAsync(new GameConnectionState(
                true,
                true,
                true,
                "Rhythia",
                version,
                "1",
                null,
                "IntegrationLoaded"));
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[RhythKit] Agent connection failed: {exception.Message}");
        }
    }

    private static void OnNodeAdded(Node node)
    {
        if (node.GetType().Name == "MainMenu")
            Callable.From(InstallGui).CallDeferred();

        if (node.GetType().Name == "Results")
        {
            resultQueued = false;
            Callable.From(ProcessResult).CallDeferred();
        }
    }

    private static void OnTreeChanged()
    {
        if (Engine.GetMainLoop() is SceneTree tree && tree.CurrentScene?.GetType().Name == "MainMenu")
            Callable.From(InstallGui).CallDeferred();
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
        var button = new Button
        {
            Name = name,
            Text = text,
            CustomMinimumSize = new Vector2(0, 48)
        };
        parent.AddChild(button);
        return button;
    }

    private static void OnConnectPressed()
    {
        if (connectButton != null) _ = ConnectAsync(connectButton);
    }

    private static void OnTestPressed()
    {
        _ = RefreshConnectionAsync(true);
    }

    private static void OnMapsPressed()
    {
        try
        {
            RhythiansMapStore.OpenRoot();
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[RhythKit] {exception.Message}");
        }
    }

    private static async Task RefreshConnectionAsync(bool showResult = false)
    {
        if (client == null || statusButton == null) return;
        statusButton.Text = "Rhythians: Checking...";

        try
        {
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException();
            var result = await client.TestConnectionAsync(token);
            if (result?.Ok != true || result.Authenticated != true || string.IsNullOrWhiteSpace(result.Username))
                throw new InvalidOperationException();

            username = result.Username;
            statusButton.Text = $"Rhythians: Connected ({username})";
            if (connectButton != null) connectButton.Visible = false;
            if (showResult) GD.Print($"[RhythKit] Connected as {username}.");
        }
        catch
        {
            username = null;
            statusButton.Text = "Rhythians: Not connected";
            if (connectButton != null) connectButton.Visible = true;
            if (showResult) GD.PrintErr("[RhythKit] Rhythians connection failed.");
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
            GD.PrintErr($"[RhythKit] {exception.Message}");
            button.Visible = true;
            if (statusButton != null) statusButton.Text = "Rhythians: Not connected";
        }
        finally
        {
            button.Disabled = false;
        }
    }

    private static async void ProcessResult()
    {
        if (resultQueued || bridge == null) return;
        resultQueued = true;

        try
        {
            await Task.Yield();
            if (!RhythiaResultAdapter.TryReadCompletion(out var completion) || completion == null) return;
            if (!RhythiansMapStore.Contains(completion.MapId)) return;

            var version = RhythiaResultAdapter.GetGameVersion() ?? "unknown";
            await bridge.ReportEventAsync(new GameEvent(
                "MapCompleted",
                true,
                true,
                true,
                "Rhythia",
                version,
                "1",
                completion.MapId,
                completion.ResultId,
                completion.Accuracy,
                completion.Misses,
                completion.Speed,
                completion.Passed,
                completion.Qualified,
                completion.CompletedAt));

            var response = await bridge.SubmitScoreAsync(new ScoreSubmission(
                completion.MapId,
                completion.ResultId,
                completion.Accuracy,
                completion.Misses,
                completion.Speed,
                version,
                "1",
                completion.CompletedAt,
                completion.Qualified));

            var message = response.Counted
                ? "CompletionAwarded"
                : response.AlreadyCompleted || response.Duplicate
                    ? "CompletionAlreadyRecorded"
                    : "ScoreProcessed";

            await bridge.ReportEventAsync(new GameEvent(
                message,
                true,
                true,
                true,
                "Rhythia",
                version,
                "1",
                completion.MapId,
                completion.ResultId,
                completion.Accuracy,
                completion.Misses,
                completion.Speed,
                completion.Passed,
                completion.Qualified,
                completion.CompletedAt));
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[RhythKit] Score submission failed: {exception.Message}");
        }
    }

    private static class TokenStore
    {
        private sealed record State(string Value);
        private static readonly byte[] Entropy = "RhythKit"u8.ToArray();
        private static string Path => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Rhythians",
            "rhythkit.token");

        public static string? Load()
        {
            try
            {
                if (!File.Exists(Path)) return null;
                var protectedValue = File.ReadAllBytes(Path);
                var value = ProtectedData.Unprotect(protectedValue, Entropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<State>(value)?.Value;
            }
            catch
            {
                return null;
            }
        }

        public static void Save(string value)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var data = JsonSerializer.SerializeToUtf8Bytes(new State(value));
            var protectedValue = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path, protectedValue);
        }
    }
}
