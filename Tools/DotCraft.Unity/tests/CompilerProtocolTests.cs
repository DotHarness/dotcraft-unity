using System.Reflection;
using System.Text.Json;

namespace UnityEngine { public static class Debug { } }
namespace UnityEditor { public static class EditorApplication { } }
namespace DotCraft.Editor { public sealed class UnityExecutionContext { } }

namespace DotCraft.Unity.Cli.Tests
{
    public sealed class CompilerProtocolTests
    {
        [Fact]
        public void CompilesSnippetAndMapsDiagnosticsToTheOriginalSource()
        {
            var success = CompilerRunner.Compile(Request("return 42;"));

            Assert.True(success.Success);
            Assert.NotEmpty(Convert.FromBase64String(success.AssemblyBase64!));
            Assert.StartsWith("DotCraft.Editor.Execution.Generated.Snippet_", success.EntryType);

            var failure = CompilerRunner.Compile(Request("return missingName;"));

            Assert.False(failure.Success);
            Assert.Equal("CompilationFailed", failure.ErrorCode);
            var diagnostic = Assert.Single(failure.Diagnostics, item => item.Id == "CS0103");
            Assert.Equal(1, diagnostic.Line);
            Assert.Contains("missingName", diagnostic.Message);
        }

        [Fact]
        public async Task HiddenCompilerCommandUsesOneJsonRequestAndResponse()
        {
            using var fixture = new CliFixture();
            var request = JsonSerializer.Serialize(Request("return 42;"));

            var output = await fixture.RunWithInputAsync(request, "compiler");

            Assert.Equal(0, output.ExitCode);
            Assert.True(output.Json.GetProperty("success").GetBoolean());
            Assert.Equal(GatewayConstants.ProtocolVersion, output.Json.GetProperty("protocolVersion").GetInt32());
            Assert.DoesNotContain("compiler", (await fixture.RunAsync()).Stdout, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RejectsProtocolMismatchWithoutCompiling()
        {
            var request = Request("return 42;");
            request.ProtocolVersion++;

            var result = CompilerRunner.Compile(request);

            Assert.False(result.Success);
            Assert.Equal("UnityCompilerProtocolMismatch", result.ErrorCode);
        }

        private static CompilerRequest Request(string code) => new()
        {
            ProtocolVersion = GatewayConstants.ProtocolVersion,
            Code = code,
            SourceLabel = "snippet.cs",
            References = TrustedPlatformAssemblies()
                .Append(Assembly.GetExecutingAssembly().Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        private static IEnumerable<string> TrustedPlatformAssemblies() =>
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
    }
}
