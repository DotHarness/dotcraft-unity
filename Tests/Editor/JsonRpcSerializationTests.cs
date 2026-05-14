using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DotCraft.Editor.Connection;
using DotCraft.Editor.Protocol;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotCraft.Editor.Tests
{
    public sealed class JsonRpcSerializationTests
    {
        [Test]
        public void NotificationSerializationOmitsNullParams()
        {
            var json = DotCraftJson.Serialize(new JsonRpcNotification
            {
                Method = "session/cancel"
            });

            Assert.That(json, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"method\":\"session/cancel\"}"));
        }

        [Test]
        public void ProcessMessageSkipsNonJsonStdoutLines()
        {
            var transport = new AcpTransportClient();

            Assert.DoesNotThrow(() => transport.ProcessMessage("DotCraft starting..."));
        }

        [Test]
        public void ProcessMessageRaisesNotificationWithJTokenParams()
        {
            var transport = new AcpTransportClient();
            string method = null;
            JToken parameters = null;
            transport.OnNotification += (m, p) =>
            {
                method = m;
                parameters = p;
            };

            transport.ProcessMessage(
                "{\"jsonrpc\":\"2.0\",\"method\":\"session/update\",\"params\":{\"sessionId\":\"s\",\"update\":{\"sessionUpdate\":\"agent_message_chunk\",\"content\":{\"text\":\"hi\"}}}}");

            Assert.That(method, Is.EqualTo("session/update"));
            Assert.That(parameters?["update"]?["content"]?["text"]?.ToObject<string>(), Is.EqualTo("hi"));
        }

        [Test]
        public async Task SendRequestCompletesPendingRequestOnNumericIdResponse()
        {
            var transport = new AcpTransportClient();
            using var input = new MemoryStream();
            using var output = new MemoryStream();
            transport.Initialize(input, output);

            var request = transport.SendRequestAsync(
                "initialize",
                new { protocolVersion = 1 },
                timeout: TimeSpan.FromSeconds(1));

            transport.ProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":true}}");

            var result = await request;
            Assert.That(result?["ok"]?.ToObject<bool>(), Is.True);

            var written = Encoding.UTF8.GetString(output.ToArray()).Trim();
            Assert.That(written, Does.Contain("\"method\":\"initialize\""));
            Assert.That(written, Does.Contain("\"params\":{\"protocolVersion\":1}"));
        }
    }
}
