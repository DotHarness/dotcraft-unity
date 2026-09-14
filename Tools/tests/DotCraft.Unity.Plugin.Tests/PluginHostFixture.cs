using System.Security.Cryptography;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DotCraft.Unity.Plugin.Tests;

internal sealed class PluginHostFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "dotcraft-unity-host-" + Guid.NewGuid().ToString("N"));
    public string Workspace => Path.Combine(Root, "workspace");
    public AppConfig Config { get; } = new();
    public string PluginRoot(string id) => Path.Combine(Workspace, ".craft", "plugins", id);
    public string SettingsPath => Path.Combine(Workspace, ".craft", PluginConfigStore.FileName);

    public PluginHostFixture()
    {
        Directory.CreateDirectory(Workspace);
        Config.GlobalConfigPath = Path.Combine(Root, "user-data", "config.json");
        Config.Plugins.DisableDefaultPluginRegistry = true;
    }

    public HostSession CreateHost()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new PluginDiscoveryService(builtInPluginSourceRoots: [], craftHome: Root));
        services.AddDotCraftRuntime(new DotCraftRuntimeOptions
        {
            Config = Config,
            WorkspacePath = Workspace,
            UserDataPath = Path.Combine(Root, "user-data")
        });
        return new HostSession(services.BuildServiceProvider());
    }

    internal sealed class HostSession(ServiceProvider services) : IAsyncDisposable
    {
        public IPluginDotnetRuntimeCoordinator Coordinator { get; } =
            services.GetRequiredService<IPluginDotnetRuntimeCoordinator>();

        public IToolSource ToolSource => services.GetServices<IToolSource>()
            .Single(source => source.SourceId == "dotnet-plugins");

        public Task StartAsync() => ((IHostedService)Coordinator).StartAsync(CancellationToken.None);
        public ValueTask DisposeAsync() => services.DisposeAsync();
    }

    public static PluginDotnetRuntimeInfo ObservePlugin(HostSession host, string id) =>
        Assert.Single(host.Coordinator.Snapshot.Plugins, plugin => plugin.PluginId == id);

    public static void AssertState(PluginDotnetRuntimeInfo plugin, PluginDotnetRuntimeState expected) =>
        Assert.True(plugin.State == expected,
            $"Expected {expected} for '{plugin.PluginId}', observed {plugin.State}: "
            + string.Join(" | ", plugin.Blockers.Select(blocker => $"{blocker.Code}: {blocker.Message}")));

    public static ToolPlanningContext PlanningContext(long revision, string mode = "default") => new(
        threadId: "thread-1", turnId: "turn-1", workspacePath: Path.GetTempPath(), dataPath: Path.GetTempPath(),
        mode: mode, profile: null, providerCapabilities: null, revision: revision);

    public static ValueTask<EffectiveToolSnapshot> BuildSnapshotAsync(
        IToolSource source,
        long revision,
        string mode = "default") =>
        new EffectiveToolSnapshotBuilder().BuildAsync([source], PlanningContext(revision, mode));

    public static ToolInvocationRequest Request(string callId) =>
        new("thread-1", "turn-1", callId, ToolInvocationAudience.Model);

    public static string BundleContents(string root) => string.Join('\n',
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path =>
        {
            using var stream = File.OpenRead(path);
            return Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(stream));
        }));

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
