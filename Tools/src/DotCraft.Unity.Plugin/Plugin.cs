using System.Runtime.Loader;
using DotCraft.Plugins;
using DotCraft.Tools;

namespace DotCraft.Unity;

public sealed class Plugin : IDotCraftPlugin
{
    /// <inheritdoc />
    public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Unity attach requires Windows x64.");
        // The host may carry the same NuGet compiler; pin these private assemblies to this bundle.
        var loadContext = AssemblyLoadContext.GetLoadContext(typeof(Plugin).Assembly)!;
        foreach (var name in new[] { "Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll" })
            loadContext.LoadFromAssemblyPath(Path.Combine(context.ContentRoot, "lib", name));
        var service = UnityAttachService.CreateDefault();
        context.Lifetime.OwnAsync(service);
        context.Contributions.Add<IToolSource>(new UnityToolSource(service, context.WorkspaceRoot));
        return ValueTask.CompletedTask;
    }
}
