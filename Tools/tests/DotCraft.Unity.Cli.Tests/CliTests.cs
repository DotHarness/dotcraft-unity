namespace DotCraft.Unity.Cli.Tests;

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
        Assert.Equal(UnityTools.ProductVersion, version.Json.GetProperty("version").GetString());
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
}
