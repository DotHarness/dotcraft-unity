using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotCraft.Editor.Connection;
using DotCraft.Editor.Extensions;
using DotCraft.Editor.RuntimeTools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DotCraft.Editor.Tests
{
    public sealed class AcpRuntimeToolResultTests
    {
        [Test]
        public async Task ExecuteCSharpSuccessUsesPrivateRuntimeToolEnvelope()
        {
            var response = await InvokeExecuteCSharpAsync("return 40 + 2;");
            var result = response["result"];

            Assert.That(result?["success"]?.Value<bool>(), Is.True);
            Assert.That(result?["contentItems"]?[0]?["text"]?.Value<string>(), Does.Contain("42"));
            Assert.That(result?["structuredContent"]?["success"]?.Value<bool>(), Is.True);
            Assert.That(result?["structuredContent"]?["returnValue"]?.Value<int>(), Is.EqualTo(42));
        }

        [Test]
        public async Task ExecuteCSharpCompilationFailureStaysJsonRpcResultAndPreservesDiagnostics()
        {
            var response = await InvokeExecuteCSharpAsync("return ;");
            var result = response["result"];

            Assert.That(response["error"], Is.Null);
            Assert.That(result?["success"]?.Value<bool>(), Is.False);
            Assert.That(result?["errorCode"]?.Value<string>(), Is.EqualTo("CompilationFailed"));
            Assert.That(result?["errorMessage"]?.Value<string>(), Is.EqualTo("C# compilation failed."));
            Assert.That(result?["structuredContent"]?["diagnostics"]?.Any(), Is.True);
        }

        [Test]
        public async Task UnknownCustomMethodReturnsMethodNotFoundError()
        {
            var router = CreateRouter();
            var response = await InvokeTransportAsync(router, "_unity/not_registered", new JObject());

            Assert.That(response["result"], Is.Null);
            Assert.That(response["error"]?["code"]?.Value<int>(), Is.EqualTo(-32601));
        }

        [Test]
        public async Task InvalidRuntimeToolArgumentsReturnInvalidParamsError()
        {
            var router = CreateRouter();
            LogAssert.Expect(LogType.Exception, new Regex("AcpRequestException: Invalid parameters"));
            var response = await InvokeTransportAsync(
                router,
                FindTool(nameof(IntegerArgumentTool)),
                new JObject { ["value"] = "not-an-integer" });

            Assert.That(response["result"], Is.Null);
            Assert.That(response["error"]?["code"]?.Value<int>(), Is.EqualTo(-32602));
        }

        [Test]
        public async Task UnexpectedExtensionFailureReturnsInternalError()
        {
            var router = new ExtensionMethodRouter();
            router.RegisterHandler(
                "_unity/throws",
                new Func<JToken, object>(_ => throw new InvalidOperationException("broken router")));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: broken router"));
            var response = await InvokeTransportAsync(router, "_unity/throws", new JObject());

            Assert.That(response["error"]?["code"]?.Value<int>(), Is.EqualTo(-32603));
            Assert.That(response["error"]?["message"]?.Value<string>(), Does.Contain("broken router"));
        }

        [AgentTool(Name = "integer_argument_tool", Description = "Accept an integer.")]
        private static object IntegerArgumentTool(int value)
        {
            return new { value };
        }

        private static Task<JObject> InvokeExecuteCSharpAsync(string code)
        {
            var router = CreateRouter();
            return InvokeTransportAsync(
                router,
                "_unity/execute_csharp",
                new JObject { ["code"] = code, ["mode"] = "editor" });
        }

        private static ExtensionMethodRouter CreateRouter()
        {
            var router = new ExtensionMethodRouter();
            router.RegisterRuntimeTools(RuntimeToolCatalog.Discover().Tools);
            return router;
        }

        private static string FindTool(string methodName)
        {
            var method = typeof(AcpRuntimeToolResultTests).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            return RuntimeToolCatalog.Discover().Tools.Single(tool => tool.Method == method).Descriptor.AcpMethod;
        }

        private static async Task<JObject> InvokeTransportAsync(
            ExtensionMethodRouter router,
            string method,
            JObject parameters)
        {
            using var input = new MemoryStream();
            using var output = new MemoryStream();
            using var transport = new AcpTransportClient();
            transport.Initialize(input, output);
            transport.RegisterExtensionHandler("_unity/", router.HandleAsync);
            transport.ProcessMessage(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = method,
                ["params"] = parameters
            }.ToString(Newtonsoft.Json.Formatting.None));

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (output.Length == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Assert.That(output.Length, Is.GreaterThan(0), "ACP transport did not write a response.");
            var json = Encoding.UTF8.GetString(output.ToArray()).Trim();
            return JObject.Parse(json);
        }
    }
}
