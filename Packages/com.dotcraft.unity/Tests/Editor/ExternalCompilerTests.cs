using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor;
using DotCraft.Editor.Execution;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class ExternalCompilerTests
    {
        [Test]
        public async Task ExecutionEngineLoadsAndRunsTheExternalAssembly()
        {
            var compiler = new FixtureCompiler();
            var engine = new ExternalCSharpExecutionEngine(compiler);

            var result = await engine.ExecuteAsync(new ExecutionRequest(
                UnityExecutionEngines.CSharp,
                UnityExecutionModes.Editor,
                "return Args[\"value\"];",
                inputs: new JObject { ["value"] = 42 }));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.ReturnValue, Is.EqualTo(42));
            Assert.That(compiler.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ExecutionEnginePreservesCompilerProtocolFailure()
        {
            var engine = new ExternalCSharpExecutionEngine(new FixtureCompiler
            {
                FailureCode = "UnityCompilerProtocolMismatch"
            });

            var result = await engine.ExecuteAsync(new ExecutionRequest(
                UnityExecutionEngines.CSharp,
                UnityExecutionModes.Editor,
                "return 1;"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("UnityCompilerProtocolMismatch"));
        }

        [Test]
        public async Task MissingConfiguredCliReturnsCompilerUnavailableOnWindowsX64()
        {
            Assume.That(Environment.OSVersion.Platform, Is.EqualTo(PlatformID.Win32NT));
            Assume.That(Environment.Is64BitProcess, Is.True);
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "dotcraft-unity.exe");

            var result = await new ExternalCSharpCompiler(path).CompileAsync(
                "return 1;",
                "test.cs",
                Array.Empty<string>(),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("UnityCompilerUnavailable"));
        }

        private sealed class FixtureCompiler : IExternalCSharpCompiler
        {
            public int CallCount { get; private set; }
            public string FailureCode { get; set; }

            public Task<ExternalCompilationResult> CompileAsync(string code, string sourceLabel,
                IReadOnlyList<string> references, CancellationToken cancellationToken)
            {
                CallCount++;
                if (!string.IsNullOrWhiteSpace(FailureCode))
                {
                    return Task.FromResult(new ExternalCompilationResult
                    {
                        Success = false,
                        ErrorCode = FailureCode,
                        ErrorMessage = "Protocol mismatch."
                    });
                }

                return Task.FromResult(new ExternalCompilationResult
                {
                    Success = true,
                    Assembly = File.ReadAllBytes(typeof(ExternalCompilerFixture).Assembly.Location),
                    EntryType = typeof(ExternalCompilerFixture).FullName
                });
            }
        }
    }

    public static class ExternalCompilerFixture
    {
        public static Task<object> Run(JObject args, UnityExecutionContext context,
            CancellationToken cancellationToken) => Task.FromResult<object>(args["value"].Value<int>());
    }
}
