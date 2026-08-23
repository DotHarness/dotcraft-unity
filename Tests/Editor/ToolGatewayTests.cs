using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DotCraft.Editor.ToolGateway;
using DotCraft.Editor.Settings;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class ToolGatewayTests
    {
        private string _projectRoot;
        private bool _enableCSharpAutomation;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(
                Path.GetTempPath(),
                "dotcraft-unity-tool-gateway-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectRoot);
            _enableCSharpAutomation = DotCraftSettings.Instance.EnableCSharpAutomation;
            DotCraftSettings.Instance.EnableCSharpAutomation = true;
        }

        [TearDown]
        public void TearDown()
        {
            DotCraftSettings.Instance.EnableCSharpAutomation = _enableCSharpAutomation;
            if (Directory.Exists(_projectRoot))
                Directory.Delete(_projectRoot, true);
        }

        [Test]
        public void GatewayListsExecuteCSharpWithStableSchema()
        {
            var tool = UnityToolRegistry.Instance.ListTools()
                .Single(spec => spec.Name == "unity_execute_csharp");

            Assert.That(tool.Description, Is.Not.Empty);
            Assert.That(tool.InputSchema.Value<string>("type"), Is.EqualTo("object"));
            Assert.That(tool.InputSchema["required"]?.Values<string>(), Contains.Item("code"));
        }

        [Test]
        public void ToolGatewayTokensAreRandom256BitBase64UrlValues()
        {
            var first = UnityToolGatewayState.CreateToken();
            var second = UnityToolGatewayState.CreateToken();

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first, Has.Length.EqualTo(43));
            Assert.That(first, Does.Not.Contain("+"));
            Assert.That(first, Does.Not.Contain("/"));
            Assert.That(first, Does.Not.Contain("="));
        }

        [Test]
        public void ManifestIsSortedAndRevisionIsStable()
        {
            var state = new UnityToolGatewayState(_projectRoot);

            var first = state.RefreshManifest();
            var second = state.RefreshManifest();

            Assert.That(second.Revision, Is.EqualTo(first.Revision));
            Assert.That(first.Revision, Does.StartWith("sha256:"));
            Assert.That(
                first.Tools.Select(tool => tool.Name).ToArray(),
                Is.EqualTo(first.Tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray()));
            Assert.That(File.Exists(state.ManifestPath), Is.True);
        }

        [Test]
        public void DiscoveryPublishesRequiredIdentityAndRemovesOnlyMatchingToken()
        {
            var state = new UnityToolGatewayState(_projectRoot);
            var discovery = state.PublishDiscovery(
                "http://127.0.0.1:49152/dotcraft-unity",
                "token-a");

            Assert.That(discovery.SchemaVersion, Is.EqualTo(1));
            Assert.That(discovery.ProcessId, Is.EqualTo(Process.GetCurrentProcess().Id));
            Assert.That(File.Exists(state.DiscoveryPath), Is.True);

            state.RemoveDiscovery("token-b");
            Assert.That(File.Exists(state.DiscoveryPath), Is.True);
            state.RemoveDiscovery("token-a");
            Assert.That(File.Exists(state.DiscoveryPath), Is.False);
        }

        [Test]
        public async Task ServerRejectsNonLoopbackHostAndOrigin()
        {
            var handler = new UnityToolGatewayHandler("secret");
            using var server = new UnityToolGatewayServer(handler);
            server.Start();

            using var hostRequest = new HttpRequestMessage(HttpMethod.Post, server.Endpoint + "/call");
            hostRequest.Headers.Host = "example.com";
            hostRequest.Headers.Add(UnityToolGatewayState.TokenHeader, "secret");
            using var hostClient = new HttpClient();
            using var hostResponse = await hostClient.SendAsync(hostRequest);
            Assert.That((int)hostResponse.StatusCode, Is.EqualTo(403));

            using var originRequest = new HttpRequestMessage(HttpMethod.Post, server.Endpoint + "/call");
            originRequest.Headers.Add("Origin", "https://example.com");
            originRequest.Headers.Add(UnityToolGatewayState.TokenHeader, "secret");
            using var originClient = new HttpClient();
            using var originResponse = await originClient.SendAsync(originRequest);
            Assert.That((int)originResponse.StatusCode, Is.EqualTo(403));
        }

        [Test]
        public async Task ServerRequiresTokenAndCallsEnabledTool()
        {
            var handler = new UnityToolGatewayHandler("secret");
            using var server = new UnityToolGatewayServer(handler);
            server.Start();

            Assert.That(server.Port, Is.GreaterThan(0));
            Assert.That(server.Endpoint, Does.StartWith("http://127.0.0.1:"));

            using var unauthenticatedRequest = new HttpRequestMessage(HttpMethod.Post, server.Endpoint + "/call");
            unauthenticatedRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var unauthenticated = await client.SendAsync(unauthenticatedRequest);
            Assert.That((int)unauthenticated.StatusCode, Is.EqualTo(401));

            using var request = new HttpRequestMessage(HttpMethod.Post, server.Endpoint + "/call");
            request.Headers.Add(UnityToolGatewayState.TokenHeader, "secret");
            request.Content = new StringContent(
                "{\"name\":\"unity_execute_csharp\",\"arguments\":{\"code\":\"return 42;\"}}",
                Encoding.UTF8,
                "application/json");
            using var response = await client.SendAsync(request);
            var result = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(result.Value<bool>("success"), Is.True, result.ToString());
            Assert.That(result["result"]?["returnValue"]?.Value<int>(), Is.EqualTo(42));
        }
    }
}
