namespace DotCraft.Unity.McpGateway.Tests;

public sealed class CliTests
{
    [Fact]
    public async Task HelpAndVersionDoNotRequireAProject()
    {
        using var fixture = new CliFixture { WorkingDirectory = Path.GetTempPath() };
        var help = await fixture.RunAsync();
        Assert.Equal(0, help.ExitCode);
        Assert.Contains("Usage: dotcraft-unity", help.Stdout);
        var version = await fixture.RunAsync("version", "--json");
        Assert.Equal(0, version.ExitCode);
        Assert.Equal(GatewayConstants.PackageVersion, version.Json.GetProperty("version").GetString());
        Assert.Empty(version.Stderr);
    }

    [Theory]
    [InlineData("--project-root", ".")]
    [InlineData("mcp")]
    [InlineData("exec", "--code", "return 1;", "--path", "x.cs")]
    [InlineData("exec", "--stdin", "--args-file", "-")]
    [InlineData("exec", "--mode", "invalid", "--code", "return 1;")]
    [InlineData("call", "custom", "--arguments", "{}", "--arguments-file", "x.json")]
    [InlineData("call", "custom", "--arguments", "[]")]
    [InlineData("call", "custom", "--arguments", "{broken}")]
    [InlineData("exec", "--code", "return 1;", "--unknown")]
    public async Task InvalidInputsHaveStructuredErrors(params string[] args)
    {
        using var fixture = new CliFixture();
        var output = await fixture.RunAsync([.. args, "--json"]);
        Assert.Equal(2, output.ExitCode);
        Assert.Equal("InvalidArguments", output.Json.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task InlineCodeForwardsArgumentsAndPreservesGatewayEnvelope()
    {
        using var fixture = new CliFixture();
        var reply = fixture.ReplyOnceAsync();
        var output = await fixture.RunAsync("exec", "--code", "return Args[\"text\"];", "--args", "{\"text\":\"引号\\\"\\\\\"}", "--json");
        var forwarded = (await reply).GetProperty("arguments");
        Assert.Equal("return Args[\"text\"];", forwarded.GetProperty("code").GetString());
        Assert.Equal("editor", forwarded.GetProperty("mode").GetString());
        Assert.Equal("引号\"\\", forwarded.GetProperty("args").GetProperty("text").GetString());
        Assert.Equal(0, output.ExitCode);
        Assert.Equal(7, output.Json.GetProperty("durationMs").GetInt32());
        Assert.Equal("测试", output.Json.GetProperty("result").GetProperty("returnValue").GetProperty("label").GetString());
        Assert.True(output.Json.GetProperty("success").GetBoolean());
        Assert.Empty(output.Stderr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScriptPathsAreForwardedWithoutResolvingAgainstShellDirectory(bool absolutePath)
    {
        using var fixture = new CliFixture();
        var script = absolutePath ? Path.Combine(fixture.Root, "Agent Skills", "read.cs") : ".craft/scripts/read.cs";
        fixture.WorkingDirectory = Path.Combine(fixture.Root, "Assets", "Nested");
        File.WriteAllText(Path.Combine(fixture.WorkingDirectory, "args.json"), "{\"maxRows\":3}");
        var reply = fixture.ReplyOnceAsync();
        var output = await fixture.RunAsync("exec", "--path", script, "--args-file", "args.json", "--mode", "playmode", "--json");
        var arguments = (await reply).GetProperty("arguments");
        Assert.Equal(script, arguments.GetProperty("path").GetString());
        Assert.Equal("playmode", arguments.GetProperty("mode").GetString());
        Assert.Equal(3, arguments.GetProperty("args").GetProperty("maxRows").GetInt32());
        Assert.Equal(0, output.ExitCode);
    }

    [Fact]
    public async Task StdinSupportsCodeAndGenericToolArguments()
    {
        using var fixture = new CliFixture();
        var code = "var label = \"你好\";\nreturn label;";
        var reply = fixture.ReplyOnceAsync();
        Assert.Equal(0, (await fixture.RunWithInputAsync(code, "exec", "--stdin", "--json")).ExitCode);
        Assert.Equal(code, (await reply).GetProperty("arguments").GetProperty("code").GetString());
        reply = fixture.ReplyOnceAsync();
        Assert.Equal(0, (await fixture.RunWithInputAsync("{\"value\":123}", "call", "custom_tool", "--arguments-file", "-", "--json")).ExitCode);
        var request = await reply;
        Assert.Equal("custom_tool", request.GetProperty("name").GetString());
        Assert.Equal(123, request.GetProperty("arguments").GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task ExplicitProjectRootWinsAndMissingProjectFails()
    {
        using var fixture = new CliFixture();
        fixture.WorkingDirectory = Path.GetTempPath();
        var output = await fixture.RunAsync("tools", "list", "--json");
        Assert.Equal(2, output.ExitCode);
        output = await fixture.RunAsync("tools", "list", "--project-root", fixture.Root, "--json");
        Assert.Equal(0, output.ExitCode);
        Assert.Equal(fixture.Root, output.Json.GetProperty("projectRoot").GetString());
    }

    [Fact]
    public async Task ToolsDistinguishFallbackAndCachedSchemasWithoutClaimingLiveAvailability()
    {
        using var fixture = new CliFixture();
        var output = await fixture.RunAsync("tools", "list", "--json");
        Assert.Equal("default", output.Json.GetProperty("source").GetString());
        fixture.WriteJson(GatewayConstants.ManifestRelativePath, new
        {
            schemaVersion = 1, packageVersion = GatewayConstants.PackageVersion, revision = "test:1",
            tools = new[] { new { name = "custom", description = "Test", inputSchema = new { type = "object" } } }
        });
        output = await fixture.RunAsync("tools", "describe", "custom", "--json");
        Assert.Equal("cache", output.Json.GetProperty("source").GetString());
        Assert.Equal("object", output.Json.GetProperty("tools")[0].GetProperty("inputSchema").GetProperty("type").GetString());
        Assert.Equal(1, (await fixture.RunAsync("tools", "describe", "missing", "--json")).ExitCode);
    }

    [Fact]
    public async Task StatusRedactsTokenAndReportsVersionMismatchAndDeadProcess()
    {
        using var fixture = new CliFixture();
        var output = await fixture.RunAsync("status", "--json");
        Assert.Equal(0, output.ExitCode);
        Assert.True(output.Json.GetProperty("tcpReachable").GetBoolean());
        Assert.DoesNotContain(fixture.Token, output.Stdout + output.Stderr);
        fixture.WriteDiscovery(version: "0.0.0");
        output = await fixture.RunAsync("status", "--json");
        Assert.Equal(1, output.ExitCode);
        Assert.Contains("Version mismatch", output.Json.GetProperty("message").GetString());
        Assert.Equal("0.0.0", output.Json.GetProperty("packageVersion").GetString());
        fixture.WriteDiscovery(pid: int.MaxValue);
        output = await fixture.RunAsync("status", "--json");
        Assert.Equal(1, output.ExitCode);
        Assert.False(output.Json.GetProperty("discoveryValid").GetBoolean());
    }

    [Fact]
    public async Task ToolFailureReturnsNonzeroAndKeepsErrorDetails()
    {
        using var fixture = new CliFixture();
        var reply = fixture.ReplyOnceAsync(success: false);
        var output = await fixture.RunAsync("exec", "--code", "bad", "--json");
        await reply;
        Assert.Equal(1, output.ExitCode);
        Assert.Equal("CompilationFailed", output.Json.GetProperty("errorCode").GetString());
        Assert.Equal("bad C#", output.Json.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task UnavailableAndStaleAuthenticationDoNotReplay()
    {
        using var fixture = new CliFixture();
        fixture.WriteDiscovery(version: "0.0.0");
        var output = await fixture.RunAsync("call", "custom", "--json");
        Assert.Equal("UnityUnavailable", output.Json.GetProperty("errorCode").GetString());
        Assert.Contains(GatewayConstants.PackageVersion, output.Json.GetProperty("errorMessage").GetString());
        fixture.WriteDiscovery();
        var reply = fixture.ReplyOnceAsync(status: 401);
        output = await fixture.RunAsync("call", "custom", "--json");
        await reply;
        Assert.Equal(1, output.ExitCode);
        Assert.Equal("UnityUnavailable", output.Json.GetProperty("errorCode").GetString());
    }
}
