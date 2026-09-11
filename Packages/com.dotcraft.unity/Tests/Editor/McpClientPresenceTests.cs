using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DotCraft.Editor.ToolGateway;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class McpClientPresenceTests
    {
        private const string Token = "secret";
        private static readonly DateTime Now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

        private McpClientSessionRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = McpClientSessionRegistry.Instance;
            _registry.Clear();
        }

        [TearDown]
        public void TearDown() => _registry.Clear();

        [Test]
        public async Task SessionRouteRequiresToken()
        {
            using var server = StartServer();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            using var request = SessionRequest(server, PresenceBody(), token: null);
            using var response = await client.SendAsync(request);

            Assert.That((int)response.StatusCode, Is.EqualTo(401));
        }

        [Test]
        public async Task SessionRouteRejectsNonLoopbackHost()
        {
            using var server = StartServer();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            using var request = SessionRequest(server, PresenceBody());
            request.Headers.Host = "example.com";
            using var response = await client.SendAsync(request);

            Assert.That((int)response.StatusCode, Is.EqualTo(403));
        }

        [Test]
        public async Task SessionUpsertRegistersTheClientAndAcksTheHeartbeatInterval()
        {
            using var server = StartServer();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            using var request = SessionRequest(server, PresenceBody());
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(JObject.Parse(body).Value<int>("heartbeatSeconds"), Is.GreaterThan(0));
            Assert.That(body, Does.Not.Contain(Token), "The acknowledgement must not echo the token.");

            var session = _registry.Snapshot(DateTime.UtcNow).Single();
            Assert.That(session.DisplayName, Is.EqualTo("Claude Code"));
            Assert.That(session.ClientVersion, Is.EqualTo("2.0.31"));
        }

        [Test]
        public async Task ClosingStateRemovesTheSession()
        {
            using var server = StartServer();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            using var online = SessionRequest(server, PresenceBody());
            using var onlineResponse = await client.SendAsync(online);
            Assert.That((int)onlineResponse.StatusCode, Is.EqualTo(200));
            Assert.That(_registry.ActiveCount, Is.EqualTo(1));

            var closing = PresenceObject();
            closing["state"] = "closing";
            using var closingRequest = SessionRequest(server, closing.ToString());
            using var closingResponse = await client.SendAsync(closingRequest);

            Assert.That((int)closingResponse.StatusCode, Is.EqualTo(200));
            Assert.That(_registry.ActiveCount, Is.Zero);
        }

        [Test]
        public async Task CallRouteRefreshesActivityFromTheSessionHeader()
        {
            using var server = StartServer();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            using var presence = SessionRequest(server, PresenceBody());
            using var presenceResponse = await client.SendAsync(presence);
            Assert.That((int)presenceResponse.StatusCode, Is.EqualTo(200));

            var before = _registry.Snapshot(DateTime.UtcNow).Single().LastSeenUtc;
            await Task.Delay(20);

            using var call = new HttpRequestMessage(HttpMethod.Post, server.Endpoint + "/call");
            call.Headers.Add(UnityToolGatewayState.TokenHeader, Token);
            call.Headers.Add(UnityToolGatewayState.SessionHeader, "session-abcdef01");
            call.Content = new StringContent(
                "{\"name\":\"unity_execute_csharp\",\"arguments\":{\"code\":\"return 1;\"}}",
                Encoding.UTF8,
                "application/json");
            using var callResponse = await client.SendAsync(call);

            Assert.That((int)callResponse.StatusCode, Is.EqualTo(200));
            Assert.That(_registry.Snapshot(DateTime.UtcNow).Single().LastSeenUtc, Is.GreaterThan(before));
        }

        [Test]
        public void HeartbeatsOnlyReportUiVisibleChanges()
        {
            Assert.That(_registry.Upsert(Session("session-abcdef01"), Now), Is.True);
            Assert.That(
                _registry.Upsert(Session("session-abcdef01"), Now.AddSeconds(15)),
                Is.False,
                "A plain heartbeat must not repaint the status bar.");
            Assert.That(
                _registry.Upsert(Session("session-abcdef01", name: "codex"), Now.AddSeconds(30)),
                Is.True);

            var session = _registry.Snapshot(Now.AddSeconds(30)).Single();
            Assert.That(session.ConnectedAtUtc, Is.EqualTo(Now), "Connected time survives heartbeats.");
            Assert.That(session.LastSeenUtc, Is.EqualTo(Now.AddSeconds(30)));
        }

        [Test]
        public void ExpiredSessionsDisappearFromSnapshotAndSweep()
        {
            _registry.Upsert(Session("session-abcdef01"), Now);

            Assert.That(_registry.Snapshot(Now + McpClientSessionRegistry.Ttl - TimeSpan.FromSeconds(1)).Count,
                Is.EqualTo(1));

            // Lazy expiry is the correctness guarantee; the sweep tick only bounds latency.
            var expired = Now + McpClientSessionRegistry.Ttl + TimeSpan.FromSeconds(1);
            Assert.That(_registry.Snapshot(expired).Count, Is.Zero);
            Assert.That(_registry.Sweep(expired), Is.True);
        }

        [Test]
        public void DeadGatewayProcessIsPrunedBeforeTtl()
        {
            _registry.Upsert(Session("session-abcdef01", processId: 999999999), Now);

            Assert.That(_registry.Sweep(Now.AddSeconds(1)), Is.True);
            Assert.That(_registry.Snapshot(Now).Count, Is.Zero);
        }

        [Test]
        public void ClientStringsAreTruncatedAndStrippedOfControlCharacters()
        {
            var body = PresenceObject();
            body["client"] = new JObject
            {
                ["name"] = new string('x', 4096),
                ["title"] = "Claude\nCode"
            };

            Assert.That(McpClientSessionRegistry.TryParse(body, out var session, out _), Is.True);
            Assert.That(session.ClientName.Length, Is.EqualTo(McpClientSessionRegistry.MaxStringLength));
            Assert.That(session.ClientTitle, Is.EqualTo("ClaudeCode"));
        }

        [Test]
        public void RegistryIsSafeUnderConcurrentUpsertsAndSnapshots()
        {
            var now = DateTime.UtcNow;
            var ids = Enumerable.Range(0, 10).Select(index => $"session-{index:D8}").ToArray();

            Assert.DoesNotThrow(() => Parallel.For(0, 100, index =>
            {
                _registry.Upsert(Session(ids[index % ids.Length]), now);
                _registry.Snapshot(now);
            }));

            Assert.That(_registry.Snapshot(now).Count, Is.EqualTo(ids.Length));
        }

        [Test]
        public void SessionsRoundTripThroughSerializeAndRestore()
        {
            var now = DateTime.UtcNow;
            _registry.Upsert(Session("session-abcdef01", name: "claude-code"), now);
            _registry.Upsert(
                Session("session-abcdef02"),
                now - McpClientSessionRegistry.Ttl - TimeSpan.FromSeconds(5));
            var serialized = _registry.Serialize();

            _registry.Clear();
            _registry.Restore(serialized, now);

            var sessions = _registry.Snapshot(now);
            Assert.That(sessions.Count, Is.EqualTo(1), "Sessions that expired during a reload must not resurface.");
            Assert.That(sessions[0].ClientName, Is.EqualTo("claude-code"));
        }

        [Test]
        public void DisplayNamePrefersTitleThenNameThenFallback()
        {
            Assert.That(
                new McpClientSession { ClientTitle = "Claude Code", ClientName = "claude-code" }.DisplayName,
                Is.EqualTo("Claude Code"));
            Assert.That(new McpClientSession { ClientName = "codex" }.DisplayName, Is.EqualTo("codex"));
            Assert.That(new McpClientSession().DisplayName, Is.EqualTo("Unknown client"));
        }

        private static UnityToolGatewayServer StartServer()
        {
            var server = new UnityToolGatewayServer(new UnityToolGatewayHandler(Token));
            server.Start();
            return server;
        }

        private static HttpRequestMessage SessionRequest(
            UnityToolGatewayServer server,
            string body,
            string token = Token)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, server.Endpoint + "/session");
            if (token != null)
                request.Headers.Add(UnityToolGatewayState.TokenHeader, token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return request;
        }

        private static string PresenceBody() => PresenceObject().ToString();

        private static JObject PresenceObject() => new()
        {
            ["state"] = "online",
            ["sessionId"] = "session-abcdef01",
            ["processId"] = System.Diagnostics.Process.GetCurrentProcess().Id,
            ["client"] = new JObject
            {
                ["name"] = "claude-code",
                ["title"] = "Claude Code",
                ["version"] = "2.0.31"
            }
        };

        private static McpClientSession Session(string sessionId, string name = "claude-code", int processId = 0)
        {
            return new McpClientSession
            {
                SessionId = sessionId,
                ClientName = name,
                ClientVersion = "1.0",
                ProcessId = processId == 0 ? System.Diagnostics.Process.GetCurrentProcess().Id : processId
            };
        }
    }
}
