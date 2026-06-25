using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using DotCraft.Editor.AppBinding;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.RuntimeTools;
using DotCraft.Editor.Settings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace DotCraft.Editor.Tests
{
    public sealed class AppBindingTests
    {
        [Test]
        public void LocalServerParsesHandoffUrl()
        {
            var handoff = UnityAppBindingLocalServer.ParseHandoff(
                "/dotcraft/bind?app=com.dotharness.dotcraft-unity&request=bind_req_1&token=tok%2B1&endpoint=ws%3A%2F%2F127.0.0.1%3A1234%2Fappserver%3Ftoken%3Dabc");

            Assert.That(handoff.Operation, Is.EqualTo("bind"));
            Assert.That(handoff.AppId, Is.EqualTo("com.dotharness.dotcraft-unity"));
            Assert.That(handoff.RequestId, Is.EqualTo("bind_req_1"));
            Assert.That(handoff.RequestToken, Is.EqualTo("tok+1"));
            Assert.That(handoff.Endpoint, Is.EqualTo("ws://127.0.0.1:1234/appserver?token=abc"));
        }

        [Test]
        public void LocalServerRespondsToHandoffWithoutEditorUpdate()
        {
            var port = GetFreeLoopbackPort();
            UnityAppBindingLocalServer.ResetShutdownTokenForTests(port);
            using var server = CreateNoopLocalServer(port);

            server.Start();
            Assert.That(server.IsRunning, Is.True, server.LastError);

            var response = WaitForResult(SendGetAsync(
                port,
                "/dotcraft/bind?app=com.dotharness.dotcraft-unity&request=bind_req_1&token=tok&endpoint=ws%3A%2F%2F127.0.0.1%3A1234%2Fappserver"));

            Assert.That(response, Does.Contain("HTTP/1.1 200 OK"));
            Assert.That(response, Does.Contain("ok"));
        }

        [Test]
        public void LocalServerStopReleasesPort()
        {
            var port = GetFreeLoopbackPort();
            UnityAppBindingLocalServer.ResetShutdownTokenForTests(port);
            using var server = CreateNoopLocalServer(port);

            server.Start();
            Assert.That(server.IsRunning, Is.True, server.LastError);
            server.Stop();

            Assert.That(server.IsRunning, Is.False);
            AssertCanBind(port);
        }

        [Test]
        public void LocalServerRestartDoesNotLeavePortOccupied()
        {
            var port = GetFreeLoopbackPort();
            UnityAppBindingLocalServer.ResetShutdownTokenForTests(port);
            using var server = CreateNoopLocalServer(port);

            server.Start();
            Assert.That(server.IsRunning, Is.True, server.LastError);
            server.Restart();

            Assert.That(server.IsRunning, Is.True, server.LastError);
            server.Stop();
            AssertCanBind(port);
        }

        [Test]
        public void LocalServerRestartClosesAcceptedIdleClient()
        {
            var port = GetFreeLoopbackPort();
            UnityAppBindingLocalServer.ResetShutdownTokenForTests(port);
            using var server = CreateNoopLocalServer(port);
            using var client = new TcpClient();

            server.Start();
            Assert.That(server.IsRunning, Is.True, server.LastError);
            client.Connect(IPAddress.Loopback, port);
            AssertUntil(() => server.ActiveClientCountForTests == 1);

            server.Restart();

            Assert.That(server.IsRunning, Is.True, server.LastError);
            var response = WaitForResult(SendGetAsync(
                port,
                "/dotcraft/bind?app=com.dotharness.dotcraft-unity&request=bind_req_1&token=tok&endpoint=ws%3A%2F%2F127.0.0.1%3A1234%2Fappserver"));
            Assert.That(response, Does.Contain("HTTP/1.1 200 OK"));
            AssertCanBindAfterStop(server, port);
        }

        [Test]
        public void LocalServerRestartAfterCompletedHttpClientDoesNotLeavePortOccupied()
        {
            var port = GetFreeLoopbackPort();
            UnityAppBindingLocalServer.ResetShutdownTokenForTests(port);
            using var server = CreateNoopLocalServer(port);

            server.Start();
            Assert.That(server.IsRunning, Is.True, server.LastError);
            var firstResponse = WaitForResult(SendGetAsync(
                port,
                "/dotcraft/bind?app=com.dotharness.dotcraft-unity&request=bind_req_1&token=tok&endpoint=ws%3A%2F%2F127.0.0.1%3A1234%2Fappserver"));
            Assert.That(firstResponse, Does.Contain("HTTP/1.1 200 OK"));

            server.Restart();

            Assert.That(server.IsRunning, Is.True, server.LastError);
            var secondResponse = WaitForResult(SendGetAsync(
                port,
                "/dotcraft/bind?app=com.dotharness.dotcraft-unity&request=bind_req_2&token=tok&endpoint=ws%3A%2F%2F127.0.0.1%3A1234%2Fappserver"));
            Assert.That(secondResponse, Does.Contain("HTTP/1.1 200 OK"));
            AssertCanBindAfterStop(server, port);
        }

        [Test]
        public void LocalServerStopClosesAcceptedIdleClient()
        {
            var port = GetFreeLoopbackPort();
            UnityAppBindingLocalServer.ResetShutdownTokenForTests(port);
            using var server = CreateNoopLocalServer(port);
            using var client = new TcpClient();

            server.Start();
            Assert.That(server.IsRunning, Is.True, server.LastError);
            client.Connect(IPAddress.Loopback, port);
            AssertUntil(() => server.ActiveClientCountForTests == 1);

            server.Stop();

            Assert.That(server.IsRunning, Is.False);
            AssertCanBind(port);
        }

        [Test]
        public void LocalServerStartStopsStaleServerWithMatchingToken()
        {
            var port = GetFreeLoopbackPort();
            UnityAppBindingLocalServer.ResetShutdownTokenForTests(port);
            using var stale = CreateNoopLocalServer(port);
            using var replacement = CreateNoopLocalServer(port);

            stale.Start();
            Assert.That(stale.IsRunning, Is.True, stale.LastError);

            replacement.Start();

            Assert.That(replacement.IsRunning, Is.True, replacement.LastError);
            Assert.That(stale.IsRunning, Is.False);
            AssertCanBindAfterStop(replacement, port);
        }

        [Test]
        public void LocalServerReportsPortOccupiedWithoutMatchingToken()
        {
            var port = GetFreeLoopbackPort();
            UnityAppBindingLocalServer.ResetShutdownTokenForTests(port);
            using var server = CreateNoopLocalServer(port);
            var blocker = new TcpListener(IPAddress.Loopback, port)
            {
                ExclusiveAddressUse = true
            };
            try
            {
                blocker.Start();
                LogAssert.Expect(LogType.Error, $"[DotCraft] App Binding local server failed to start: Port {port} is already in use. Another Unity Editor or local process may already own the DotCraft App Binding server.");

                server.Start();

                Assert.That(server.IsRunning, Is.False);
                Assert.That(server.LastError, Does.Contain($"Port {port} is already in use"));
            }
            finally
            {
                blocker.Stop();
            }
        }

        [Test]
        public void LocalServerAdminStatusAndShutdownReleasePort()
        {
            var port = GetFreeLoopbackPort();
            UnityAppBindingLocalServer.ResetShutdownTokenForTests(port);
            using var server = CreateNoopLocalServer(port);

            server.Start();
            Assert.That(server.IsRunning, Is.True, server.LastError);
            var token = UnityAppBindingLocalServer.GetShutdownTokenForTests(port);
            Assert.That(token, Is.Not.Empty);

            var adminQuery = $"pid={Process.GetCurrentProcess().Id}&token={Uri.EscapeDataString(token)}";
            var status = WaitForResult(SendGetAsync(port, $"/dotcraft/admin/status?{adminQuery}"));
            Assert.That(status, Does.Contain("HTTP/1.1 200 OK"));
            Assert.That(status, Does.Contain($"running=True, port={port}"));
            Assert.That(status, Does.Contain("handlers="));
            Assert.That(status, Does.Contain("pumpAlive=True"));

            var shutdown = WaitForResult(SendGetAsync(port, $"/dotcraft/admin/shutdown?{adminQuery}"));
            Assert.That(shutdown, Does.Contain("HTTP/1.1 200 OK"));
            AssertUntil(() => !server.IsRunning && CanBind(port));
        }

        [Test]
        public void AgentToolAttributeStoresAppBindingMetadata()
        {
            var tool = FindTool(nameof(AppBindingMetadataTool));

            Assert.That(tool.AppBinding.Scope, Is.EqualTo("unity.read"));
            Assert.That(tool.AppBinding.Risk, Is.EqualTo("read"));
            Assert.That(tool.AppBinding.Exposure, Is.EqualTo("direct"));
        }

        [Test]
        public void ToolCatalogAdapterMapsEnabledToolsToUnityNamespace()
        {
            var readTool = FindTool(nameof(AppBindingMetadataTool));
            var editTool = FindTool(nameof(AppBindingInferredEditTool));
            var settings = new DotCraftSettings
            {
                EnableCSharpAutomation = false
            };
            settings.DynamicToolEnabledById[readTool.Id] = true;
            settings.DynamicToolEnabledById[editTool.Id] = true;

            var attachment = UnityAppBindingToolCatalogAdapter.Build(
                settings,
                new[] { "unity.read", "unity.edit" });

            var readSpec = attachment.Tools.Single(tool => tool.Name == "test_appbinding_metadata_tool");
            var readCatalog = attachment.ToolCatalog.Single(tool => tool.Name == "test_appbinding_metadata_tool");
            Assert.That(readSpec.Namespace, Is.EqualTo("unity"));
            Assert.That(readCatalog.Scope, Is.EqualTo("unity.read"));
            Assert.That(readCatalog.Risk, Is.EqualTo("read"));
            Assert.That(attachment.DirectToolNames, Does.Contain("test_appbinding_metadata_tool"));

            var editCatalog = attachment.ToolCatalog.Single(tool => tool.Name == "test_appbinding_inferred_edit_tool");
            Assert.That(editCatalog.Scope, Is.EqualTo("unity.edit"));
            Assert.That(editCatalog.Risk, Is.EqualTo("mutate"));
            Assert.That(attachment.DeferredToolNames, Does.Contain("test_appbinding_inferred_edit_tool"));
        }

        [Test]
        public void ToolCatalogAdapterFiltersByGrantedScopes()
        {
            var editTool = FindTool(nameof(AppBindingInferredEditTool));
            var settings = new DotCraftSettings
            {
                EnableCSharpAutomation = false
            };
            settings.DynamicToolEnabledById[editTool.Id] = true;

            var attachment = UnityAppBindingToolCatalogAdapter.Build(settings, new[] { "unity.read" });

            Assert.That(attachment.Tools, Is.Empty);
            Assert.That(attachment.ToolCatalog, Is.Empty);
        }

        [Test]
        public void ToolCatalogAdapterUsesSnapshotEnablement()
        {
            var readTool = FindTool(nameof(AppBindingMetadataTool));
            var snapshot = RuntimeToolCatalog.Discover();

            var enabled = UnityAppBindingToolCatalogAdapter.Build(
                snapshot,
                enableBuiltinTools: false,
                enabledPluginToolIds: new[] { readTool.Id },
                grantedScopes: new[] { "unity.read" });
            var disabled = UnityAppBindingToolCatalogAdapter.Build(
                snapshot,
                enableBuiltinTools: false,
                enabledPluginToolIds: Array.Empty<string>(),
                grantedScopes: new[] { "unity.read" });

            Assert.That(enabled.Tools.Select(tool => tool.Name), Does.Contain("test_appbinding_metadata_tool"));
            Assert.That(disabled.Tools, Is.Empty);
        }

        [Test]
        public void StatusSummaryHidesWhenServerStoppedWithoutActiveBindings()
        {
            var summary = UnityAppBindingStatusSummary.FromState(
                false,
                "http://127.0.0.1:39777/dotcraft/",
                null,
                Array.Empty<UnityAppBindingService.ActiveBinding>());

            Assert.That(summary.IsVisible, Is.False);
            Assert.That(summary.IsLocalServerRunning, Is.False);
            Assert.That(summary.ThreadCount, Is.EqualTo(0));
            Assert.That(summary.ToolCount, Is.EqualTo(0));
            Assert.That(summary.BindingCount, Is.EqualTo(0));
            Assert.That(summary.Tooltip, Is.EqualTo(string.Empty));
        }

        [Test]
        public void StatusSummaryShowsRunningServerWithoutActiveBindings()
        {
            var summary = UnityAppBindingStatusSummary.FromState(
                true,
                "http://127.0.0.1:39777/dotcraft/",
                null,
                Array.Empty<UnityAppBindingService.ActiveBinding>());

            Assert.That(summary.IsVisible, Is.True);
            Assert.That(summary.IsLocalServerRunning, Is.True);
            Assert.That(summary.BindingCount, Is.EqualTo(0));
            Assert.That(summary.ThreadCount, Is.EqualTo(0));
            Assert.That(summary.ToolCount, Is.EqualTo(0));
            Assert.That(summary.GatewayMcpUrl, Is.EqualTo("http://127.0.0.1:39777/dotcraft/mcp"));
            Assert.That(summary.Tooltip, Does.Contain("Tool Gateway"));
            Assert.That(summary.Tooltip, Does.Contain("MCP endpoint"));
        }

        [Test]
        public void StatusSummaryCountsThreadsAndToolsWhenServerRunning()
        {
            var summary = UnityAppBindingStatusSummary.FromState(
                true,
                "http://127.0.0.1:39777/dotcraft/",
                null,
                new[]
            {
                new UnityAppBindingService.ActiveBinding { BindingId = "binding_1", ThreadId = "thread_a", ToolCount = 3 },
                new UnityAppBindingService.ActiveBinding { BindingId = "binding_2", ThreadId = "thread_b", ToolCount = 5 }
            });

            Assert.That(summary.IsVisible, Is.True);
            Assert.That(summary.IsLocalServerRunning, Is.True);
            Assert.That(summary.BindingCount, Is.EqualTo(2));
            Assert.That(summary.ThreadCount, Is.EqualTo(2));
            Assert.That(summary.ToolCount, Is.EqualTo(8));
            Assert.That(summary.GatewayMcpUrl, Is.EqualTo("http://127.0.0.1:39777/dotcraft/mcp"));
            Assert.That(summary.Tooltip, Does.Contain("connected to 2 thread(s), 8 tool(s)"));
            Assert.That(summary.Tooltip, Does.Contain("MCP Tool Gateway"));
        }

        [Test]
        public void StatusBarOpenStatusPopupActionCanBeOverriddenForTests()
        {
            var opened = false;
            Rect capturedRect = default;
            UnityAppBindingStatusSummary capturedSummary = null;
            var summary = UnityAppBindingStatusSummary.FromState(
                true,
                "http://127.0.0.1:39777/dotcraft/",
                null,
                Array.Empty<UnityAppBindingService.ActiveBinding>());

            UnityAppBindingStatusBarActions.OpenStatusPopupOverride = (rect, popupSummary) =>
            {
                opened = true;
                capturedRect = rect;
                capturedSummary = popupSummary;
            };
            try
            {
                UnityAppBindingStatusBarActions.OpenStatusPopup(new Rect(1, 2, 3, 4), summary);
            }
            finally
            {
                UnityAppBindingStatusBarActions.OpenStatusPopupOverride = null;
            }

            Assert.That(opened, Is.True);
            Assert.That(capturedRect.x, Is.EqualTo(1));
            Assert.That(capturedRect.y, Is.EqualTo(2));
            Assert.That(capturedRect.width, Is.EqualTo(3));
            Assert.That(capturedRect.height, Is.EqualTo(4));
            Assert.That(capturedSummary, Is.SameAs(summary));
        }

        [Test]
        public void StatusPopupOpenAssistantActionCanBeOverriddenForTests()
        {
            var opened = false;
            UnityAppBindingStatusBarActions.OpenAssistantOverride = () => opened = true;
            try
            {
                UnityAppBindingStatusBarActions.OpenAssistant();
            }
            finally
            {
                UnityAppBindingStatusBarActions.OpenAssistantOverride = null;
            }

            Assert.That(opened, Is.True);
        }

        [Test]
        public void ActiveBindingsChangedFiresWhenBindingRemoved()
        {
            var service = UnityAppBindingService.Instance;
            var bindings = GetActiveBindingsForTests(service);
            var bindingId = $"test_binding_{Guid.NewGuid():N}";
            var fired = 0;
            var statusFired = 0;
            void OnChanged() => fired++;
            void OnStatusChanged() => statusFired++;
            service.ActiveBindingsChanged += OnChanged;
            service.StatusChanged += OnStatusChanged;
            try
            {
                bindings[bindingId] = new UnityAppBindingService.ActiveBinding
                {
                    BindingId = bindingId,
                    ThreadId = "test_thread",
                    ToolCount = 1,
                    ConnectedAt = DateTimeOffset.UtcNow
                };

                Assert.That(service.RemoveActiveBinding(bindingId), Is.True);
                Assert.That(fired, Is.EqualTo(1));
                Assert.That(statusFired, Is.EqualTo(1));
            }
            finally
            {
                service.ActiveBindingsChanged -= OnChanged;
                service.StatusChanged -= OnStatusChanged;
                bindings.TryRemove(bindingId, out _);
            }
        }

        [Test]
        public void StatusBarRightOffsetStacksAfterGenericAbsolutePeers()
        {
            var root = new VisualElement();
            var peer = new VisualElement();
            peer.style.position = Position.Absolute;
            peer.style.right = 104;
            peer.style.top = 0;
            peer.style.width = 42;
            peer.style.height = 19;
            root.Add(peer);

            Assert.That(UnityAppBindingStatusBarIndicator.ResolveRightOffset(root, null), Is.EqualTo(150));
        }

        [Test]
        public void StatusBarRightOffsetDoesNotInspectPeerNames()
        {
            var root = new VisualElement();
            var peer = new VisualElement { name = "third-party-status-indicator" };
            peer.style.position = Position.Absolute;
            peer.style.right = 104;
            peer.style.top = 0;
            peer.style.width = 42;
            peer.style.height = 19;
            var self = new VisualElement { name = UnityAppBindingStatusBarIndicator.IndicatorName };
            self.style.position = Position.Absolute;
            self.style.right = 150;
            self.style.top = 0;
            self.style.width = 24;
            self.style.height = 19;
            root.Add(peer);
            root.Add(self);

            Assert.That(UnityAppBindingStatusBarIndicator.ResolveRightOffset(root, self), Is.EqualTo(150));
        }

        [Test]
        public void StatusBarRightOffsetIgnoresNonStatusBarPeers()
        {
            var root = new VisualElement();
            var hidden = CreateStatusBarPeer(104, 42);
            hidden.style.display = DisplayStyle.None;
            var tall = CreateStatusBarPeer(104, 42, height: 40);
            var wide = CreateStatusBarPeer(104, 200);
            var lower = CreateStatusBarPeer(104, 42, top: 6);
            var relative = CreateStatusBarPeer(104, 42);
            relative.style.position = Position.Relative;
            root.Add(hidden);
            root.Add(tall);
            root.Add(wide);
            root.Add(lower);
            root.Add(relative);

            Assert.That(UnityAppBindingStatusBarIndicator.ResolveRightOffset(root, null), Is.EqualTo(104));
        }

        [Test]
        public void StatusBarRightOffsetClampsToRootWidth()
        {
            var root = new VisualElement();
            root.style.width = 120;
            root.Add(CreateStatusBarPeer(104, 80));

            Assert.That(UnityAppBindingStatusBarIndicator.ResolveRightOffset(root, null), Is.EqualTo(90));
        }

        private static RuntimeToolDefinition FindTool(string methodName)
        {
            var method = typeof(AppBindingTests).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            return RuntimeToolCatalog.Discover().Tools.Single(tool => tool.Method == method);
        }

        private static VisualElement CreateStatusBarPeer(
            float right,
            float width,
            float top = 0,
            float height = 19)
        {
            var peer = new VisualElement();
            peer.style.position = Position.Absolute;
            peer.style.right = right;
            peer.style.top = top;
            peer.style.width = width;
            peer.style.height = height;
            return peer;
        }

        private static System.Collections.Concurrent.ConcurrentDictionary<string, UnityAppBindingService.ActiveBinding>
            GetActiveBindingsForTests(UnityAppBindingService service)
        {
            var field = typeof(UnityAppBindingService).GetField(
                "_activeBindings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (System.Collections.Concurrent.ConcurrentDictionary<string, UnityAppBindingService.ActiveBinding>)
                field.GetValue(service);
        }

        private static UnityAppBindingLocalServer CreateNoopLocalServer(int port)
        {
            return new UnityAppBindingLocalServer(
                (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult("ok");
                },
                port);
        }

        private static int GetFreeLoopbackPort()
        {
            var listener = CreateRestartCompatibleListener(0);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static void AssertCanBindAfterStop(UnityAppBindingLocalServer server, int port)
        {
            server.Stop();
            AssertCanBind(port);
        }

        private static void AssertCanBind(int port)
        {
            TcpListener listener = null;
            try
            {
                listener = CreateRestartCompatibleListener(port);
                listener.Start();
            }
            finally
            {
                listener?.Stop();
            }
        }

        private static bool CanBind(int port)
        {
            TcpListener listener = null;
            try
            {
                listener = CreateRestartCompatibleListener(port);
                listener.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                listener?.Stop();
            }
        }

        private static TcpListener CreateRestartCompatibleListener(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                listener.ExclusiveAddressUse = true;
            return listener;
        }

        private static async Task<string> SendGetAsync(int port, string target)
        {
            using var client = new TcpClient();
            await Task.Factory.FromAsync(
                client.BeginConnect(IPAddress.Loopback, port, null, null),
                client.EndConnect);

            using var stream = client.GetStream();
            var request =
                $"GET {target} HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{port}\r\n" +
                "Connection: close\r\n\r\n";
            var bytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(bytes, 0, bytes.Length);

            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
            return await reader.ReadToEndAsync();
        }

        private static T WaitForResult<T>(Task<T> task, int timeoutMilliseconds = 3000)
        {
            if (!task.Wait(timeoutMilliseconds))
                Assert.Fail($"Timed out waiting for task after {timeoutMilliseconds} ms.");
            return task.GetAwaiter().GetResult();
        }

        private static void AssertUntil(Func<bool> condition, int timeoutMilliseconds = 3000)
        {
            var timeout = Stopwatch.StartNew();
            while (!condition())
            {
                if (timeout.ElapsedMilliseconds > timeoutMilliseconds)
                    Assert.Fail($"Timed out waiting for condition after {timeoutMilliseconds} ms.");
                Thread.Sleep(25);
            }
        }

        [AgentTool(
            Namespace = "custom_plugin",
            Name = "test_appbinding_metadata_tool",
            Description = "Test App Binding metadata.",
            Kind = AcpToolKind.Read,
            AppBindingScope = "unity.read",
            AppBindingRisk = "read",
            AppBindingExposure = "direct")]
        private static object AppBindingMetadataTool()
        {
            return new { ok = true };
        }

        [AgentTool(
            Name = "test_appbinding_inferred_edit_tool",
            Description = "Test inferred App Binding metadata.",
            Kind = AcpToolKind.Edit)]
        private static object AppBindingInferredEditTool()
        {
            return new { ok = true };
        }
    }
}
