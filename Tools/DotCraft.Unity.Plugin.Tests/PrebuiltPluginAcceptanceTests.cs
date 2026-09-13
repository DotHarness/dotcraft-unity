using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Tools;
using Xunit;
using static DotCraft.Unity.Plugin.Tests.PluginHostFixture;

namespace DotCraft.Unity.Plugin.Tests;

/// <summary>Acceptance entry point for externally built tool plugins using the real host.</summary>
public sealed class PrebuiltPluginAcceptanceTests
{
    [PrebuiltPluginFact]
    public async Task GeneratedToolsPreserveSchemaPolicyAndModeBehavior()
    {
        using var harness = new PluginHostFixture();
        var source = Environment.GetEnvironmentVariable("DOTCRAFT_PREBUILT_PLUGIN")!;
        var id = PluginManifestParser.Load(source).Manifest!.Id;
        var deployer = new BuiltInPluginDeployer(
            Path.Combine(harness.Workspace, ".craft", "plugins"), [source], harness.Config.Plugins, harness.Root);
        Assert.DoesNotContain(deployer.DeployPlugin(id), diagnostic =>
            diagnostic.Severity == PluginDiagnosticSeverity.Error);

        await using var host = harness.CreateHost();
        await host.StartAsync();
        await host.Coordinator.TrustAsync(id);
        AssertState(ObservePlugin(host, id), PluginDotnetRuntimeState.Active);

        var snapshot = await BuildSnapshotAsync(host.ToolSource, 1);
        var registrations = snapshot.Registrations.Values.ToDictionary(
            registration => registration.Definition.Name.ToString(), StringComparer.Ordinal);
        Assert.Equal(
            ["unity.connect", "unity.disconnect", "unity.execute", "unity.list", "unity.status", "unity.wait"],
            registrations.Keys.Order(StringComparer.Ordinal));
        Assert.All(registrations.Values, registration =>
        {
            Assert.Equal(ToolSourceKind.PluginNative, registration.Definition.Id.Kind);
            Assert.Equal(id, registration.Definition.Id.SourceId);
            Assert.Null(registration.Definition.Presentation);
            var schema = JsonNode.Parse(registration.Definition.InputSchema.GetRawText())!.AsObject();
            var properties = schema["properties"]!.AsObject();
            Assert.False(properties.ContainsKey("context"));
            Assert.False(properties.ContainsKey("cancellationToken"));
        });

        Assert.Equal(
            ["unity.connect", "unity.disconnect", "unity.execute"],
            registrations.Values
                .Where(registration => registration.Definition.PolicyHints.RequiresApproval)
                .Select(registration => registration.Definition.Name.ToString())
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            ["unity.list", "unity.status"],
            registrations.Values
                .Where(registration => registration.Definition.PolicyHints.ReadOnly)
                .Select(registration => registration.Definition.Name.ToString())
                .Order(StringComparer.Ordinal));

        var executeSchema = JsonNode.Parse(registrations["unity.execute"].Definition.InputSchema.GetRawText())!.AsObject();
        var executeProperties = executeSchema["properties"]!.AsObject();
        Assert.Equal(["code", "path", "args", "runInBackground", "yieldTimeMs"], executeProperties.Select(property => property.Key));
        Assert.Equal(1000, executeProperties["yieldTimeMs"]!["default"]!.GetValue<int>());
        Assert.Equal(0, executeProperties["yieldTimeMs"]!["minimum"]!.GetValue<int>());
        Assert.Equal(30000, executeProperties["yieldTimeMs"]!["maximum"]!.GetValue<int>());
        var waitSchema = JsonNode.Parse(registrations["unity.wait"].Definition.InputSchema.GetRawText())!.AsObject();
        Assert.Equal("executionId", Assert.Single(waitSchema["required"]!.AsArray())!.GetValue<string>());

        var missingInput = new JsonObject();
        var missingResult = await new ToolDispatcher(approvalEvaluator: new ScenarioApproval("unity.execute", missingInput))
            .DispatchAsync(snapshot, registrations["unity.execute"].Definition.Name, missingInput, Request("typed-missing-input"));
        Assert.Equal(ToolErrorCodes.InputInvalid, missingResult.Error?.Code);
        var duplicateInput = new JsonObject { ["code"] = "return;", ["path"] = "script.cs" };
        var duplicateResult = await new ToolDispatcher(approvalEvaluator: new ScenarioApproval("unity.execute", duplicateInput))
            .DispatchAsync(snapshot, registrations["unity.execute"].Definition.Name, duplicateInput, Request("typed-duplicate-input"));
        Assert.Equal(ToolErrorCodes.InputInvalid, duplicateResult.Error?.Code);

        var connectArguments = new JsonObject();
        var connect = await new ToolDispatcher(approvalEvaluator: new ScenarioApproval("unity.connect", connectArguments))
            .DispatchAsync(snapshot, registrations["unity.connect"].Definition.Name, connectArguments, Request("typed-domain-error"));
        Assert.Equal("UnityTargetRequired", connect.Error?.Code);

        var planSnapshot = await BuildSnapshotAsync(host.ToolSource, 2, "plan");
        var planRegistrations = planSnapshot.Registrations.Values.ToDictionary(
            registration => registration.Definition.Name.ToString(), StringComparer.Ordinal);
        foreach (var (name, arguments) in new[]
                 {
                     ("unity.connect", new JsonObject()),
                     ("unity.execute", new JsonObject { ["code"] = "return;" }),
                     ("unity.disconnect", new JsonObject())
                 })
        {
            var result = await new ToolDispatcher(approvalEvaluator: new ScenarioApproval(name, arguments))
                .DispatchAsync(planSnapshot, planRegistrations[name].Definition.Name, arguments, Request("plan-" + name));
            Assert.Equal("UnityModeDenied", result.Error?.Code);
        }

        var terminatingWait = new JsonObject { ["executionId"] = "missing", ["terminate"] = true };
        var terminatingResult = await new ToolDispatcher().DispatchAsync(
            planSnapshot, planRegistrations["unity.wait"].Definition.Name, terminatingWait, Request("plan-terminating-wait"));
        Assert.Equal("UnityModeDenied", terminatingResult.Error?.Code);
        var observingWait = new JsonObject { ["executionId"] = "missing", ["yieldTimeMs"] = 0 };
        var observingResult = await new ToolDispatcher().DispatchAsync(
            planSnapshot, planRegistrations["unity.wait"].Definition.Name, observingWait, Request("plan-observing-wait"));
        Assert.True(observingResult.Success, observingResult.Content);
        Assert.Equal("lost", observingResult.StructuredContent!.Value.GetProperty("state").GetString());

        var defaultAfterPlan = await new ToolDispatcher(approvalEvaluator: new ScenarioApproval("unity.connect", connectArguments))
            .DispatchAsync(snapshot, registrations["unity.connect"].Definition.Name, connectArguments, Request("default-after-plan"));
        Assert.Equal("UnityTargetRequired", defaultAfterPlan.Error?.Code);
    }

    [PrebuiltPluginFact]
    public async Task ReplacingBundledVersionPreservesSettingsAndRequiresTrustForNewBinary()
    {
        using var harness = new PluginHostFixture();
        var source = Environment.GetEnvironmentVariable("DOTCRAFT_PREBUILT_PLUGIN")!;
        var manifest = PluginManifestParser.Load(source).Manifest!;
        var id = manifest.Id;
        var oldSource = Path.Combine(harness.Root, "bundled");
        var predecessor = Environment.GetEnvironmentVariable("DOTCRAFT_PREBUILT_PREDECESSOR");
        CopyBundle(predecessor ?? source, oldSource);
        Assert.Equal(id, PluginManifestParser.Load(oldSource).Manifest!.Id);
        if (predecessor is null)
        {
            using var binary = new FileStream(Path.GetFullPath(manifest.Dotnet!.EntryAssembly, oldSource), FileMode.Append);
            binary.WriteByte(0);
        }
        harness.Config.Plugins.DisableDefaultPluginRegistry = true;
        var pluginsRoot = Path.Combine(harness.Workspace, ".craft", "plugins");
        var bundled = new BuiltInPluginDeployer(pluginsRoot, [oldSource], harness.Config.Plugins, harness.Root);
        Assert.DoesNotContain(bundled.DeployPlugin(id), d => d.Severity == PluginDiagnosticSeverity.Error);
        var oldFingerprint = BundleContents(harness.PluginRoot(id));
        await using (var oldHost = harness.CreateHost())
        {
            await oldHost.StartAsync();
            await oldHost.Coordinator.TrustAsync(id);
            AssertState(ObservePlugin(oldHost, id), PluginDotnetRuntimeState.Active);
        }
        harness.Config.Plugins.DisabledPlugins.Add(id);
        var settings = harness.SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        var savedSettings = JsonSerializer.Serialize(new Dictionary<string, object> { [id] = new { retained = true } });
        File.WriteAllText(settings, savedSettings);

        var sourceRemoved = new BuiltInPluginDeployer(pluginsRoot, [], harness.Config.Plugins, harness.Root);
        sourceRemoved.Deploy();
        Assert.Equal(oldFingerprint, BundleContents(harness.PluginRoot(id)));
        var replacement = new BuiltInPluginDeployer(pluginsRoot, [source], harness.Config.Plugins, harness.Root);
        Assert.DoesNotContain(replacement.DeployPlugin(id), d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotEqual(oldFingerprint, BundleContents(harness.PluginRoot(id)));
        Assert.Equal(savedSettings, File.ReadAllText(settings));
        Assert.Contains(id, harness.Config.Plugins.DisabledPlugins);

        await using var manager = harness.CreateHost();
        await manager.StartAsync();
        Assert.NotEqual(PluginDotnetRuntimeState.Active, ObservePlugin(manager, id).State);
        await manager.Coordinator.SetEnabledAsync(id, true);
        Assert.Equal(PluginDotnetTrustStatus.Modified, ObservePlugin(manager, id).TrustStatus);
        Assert.Empty(await manager.ToolSource.GetRegistrationsAsync(PlanningContext(1)));
        await manager.Coordinator.TrustAsync(id);
        AssertState(ObservePlugin(manager, id), PluginDotnetRuntimeState.Active);
        Assert.Single(manager.Coordinator.Snapshot.Plugins, p => p.PluginId == id);
        var registrations = await manager.ToolSource.GetRegistrationsAsync(PlanningContext(2));
        Assert.NotEmpty(registrations);
        Assert.Equal(registrations.Count, registrations.Select(r => r.Definition.Name).Distinct().Count());
    }

    [PrebuiltPluginFact]
    public async Task MarketplaceBundleInstallsActivatesAndKeepsInstallationWhenSourceDisappears()
    {
        using var harness = new PluginHostFixture();
        var source = Environment.GetEnvironmentVariable("DOTCRAFT_PREBUILT_PLUGIN")!;
        var parsed = PluginManifestParser.Load(source);
        Assert.NotNull(parsed.Manifest);
        Assert.DoesNotContain(parsed.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var id = parsed.Manifest!.Id;
        var registry = Path.Combine(harness.Root, "registry");
        CopyBundle(source, Path.Combine(registry, "plugins", id));
        Directory.CreateDirectory(Path.Combine(registry, ".craft", "plugins"));
        File.WriteAllText(Path.Combine(registry, ".craft", "plugins", "marketplace.json"), JsonSerializer.Serialize(new
        {
            name = "acceptance",
            plugins = new[] { new { name = id,
                source = new { source = "local", path = "./plugins/" + id },
                policy = new { installation = "AVAILABLE", authentication = "ON_INSTALL" }, category = "Engineering" } }
        }));
        harness.Config.Plugins.DisableDefaultPluginRegistry = true;
        harness.Config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig { Url = registry });
        var deployer = new BuiltInPluginDeployer(Path.Combine(harness.Workspace, ".craft", "plugins"),
            [], harness.Config.Plugins, harness.Root);
        var installed = deployer.DeployPlugin(id);
        Assert.DoesNotContain(installed, d => d.Severity == PluginDiagnosticSeverity.Error);
        var fingerprint = BundleContents(harness.PluginRoot(id));

        await using (var manager = harness.CreateHost())
        {
            await manager.StartAsync();
            await manager.Coordinator.TrustAsync(id);
            AssertState(ObservePlugin(manager, id), PluginDotnetRuntimeState.Active);
            var snapshot = await BuildSnapshotAsync(manager.ToolSource, 1);
            Assert.NotEmpty(snapshot.Registrations);
            if (Environment.GetEnvironmentVariable("DOTCRAFT_PREBUILT_READ_TOOL") is { Length: > 0 } tool)
            {
                var registration = Assert.Single(snapshot.Registrations.Values,
                    entry => entry.Definition.Name.ToString() == tool);
                Assert.True(registration.Definition.PolicyHints.ReadOnly);
                var result = await new ToolDispatcher().DispatchAsync(snapshot, registration.Definition.Name,
                    new JsonObject(), Request("acceptance-read"));
                Assert.True(result.Success, result.Content);
            }
            if (Environment.GetEnvironmentVariable("DOTCRAFT_PREBUILT_SCENARIO") is { Length: > 0 } scenario)
            {
                foreach (var step in JsonNode.Parse(scenario)!.AsArray())
                {
                    var name = step!["name"]!.GetValue<string>();
                    var registration = Assert.Single(snapshot.Registrations.Values,
                        entry => entry.Definition.Name.ToString() == name);
                    var arguments = step["arguments"]!.AsObject();
                    if (registration.Definition.PolicyHints.RequiresApproval)
                    {
                        var unapproved = await new ToolDispatcher().DispatchAsync(snapshot, registration.Definition.Name,
                            arguments, Request("acceptance-unapproved-" + name));
                        Assert.Equal(ToolErrorCodes.ApprovalRejected, unapproved.Error?.Code);
                    }
                    var dispatcher = new ToolDispatcher(approvalEvaluator: new ScenarioApproval(name, arguments));
                    var result = await dispatcher.DispatchAsync(snapshot, registration.Definition.Name,
                        arguments, Request("acceptance-scenario-" + name));
                    Assert.True(result.Success, $"{name}: {result.Error?.Code}: {result.Error?.Message} | {result.Content}");
                    if (step["expectedContent"] is { } expected)
                        Assert.Contains(expected.GetValue<string>(), result.Content);
                }
            }
            await manager.Coordinator.SetEnabledAsync(id, false);
            Assert.Empty(await manager.ToolSource.GetRegistrationsAsync(PlanningContext(2)));
            await manager.Coordinator.SetEnabledAsync(id, true);
            AssertState(ObservePlugin(manager, id), PluginDotnetRuntimeState.Active);
        }

        harness.Config.Plugins.PluginRegistries.Clear();
        Assert.DoesNotContain(deployer.Deploy(), d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.Equal(fingerprint, BundleContents(harness.PluginRoot(id)));
        var assembly = Path.GetFullPath(parsed.Manifest.Dotnet!.EntryAssembly, harness.PluginRoot(id));
        using (var changedAssembly = new FileStream(assembly, FileMode.Append, FileAccess.Write))
            changedAssembly.WriteByte(0);
        Assert.NotEqual(fingerprint, BundleContents(harness.PluginRoot(id)));
        await using var untrusted = harness.CreateHost();
        await untrusted.StartAsync();
        Assert.Equal(PluginDotnetTrustStatus.Modified, ObservePlugin(untrusted, id).TrustStatus);
        Assert.NotEqual(PluginDotnetRuntimeState.Active, ObservePlugin(untrusted, id).State);
    }

    private static void CopyBundle(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }
    }

    private sealed class ScenarioApproval(string name, JsonObject expectedArguments) : IToolApprovalEvaluator
    {
        public ValueTask<ToolDispatchDecision> RequestAsync(ToolInvocationContext context,
            ToolRegistration registration, JsonObject arguments, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(context.ToolName.ToString() == name && JsonNode.DeepEquals(arguments, expectedArguments)
                ? ToolDispatchDecision.Allow
                : ToolDispatchDecision.Deny(ToolErrorCodes.ApprovalRejected, "Call is outside the explicit acceptance scenario."));
    }

    private sealed class PrebuiltPluginFactAttribute : FactAttribute
    {
        public PrebuiltPluginFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTCRAFT_PREBUILT_PLUGIN")))
                Skip = "Set DOTCRAFT_PREBUILT_PLUGIN to a prebuilt plugin bundle.";
        }
    }
}
