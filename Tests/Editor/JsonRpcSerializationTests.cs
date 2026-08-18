using System;
using System.IO;
using System.Text;
using System.Threading;
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

        [Test]
        public void InitializeReplacesClosedStreamWrappersAndPublishesNewTransport()
        {
            using var transport = new AcpTransportClient();
            var oldInput = new ThrowingDisposeStream();
            var oldOutput = new ThrowingDisposeStream();
            transport.Initialize(oldInput, oldOutput);
            oldInput.DisposeException = new IOException("Redirected stdout pipe is closed.");
            oldOutput.DisposeException = new ObjectDisposedException(
                nameof(ThrowingDisposeStream),
                "Redirected stdin stream has been closed.");

            using var newInput = new MemoryStream();
            using var newOutput = new MemoryStream();
            Assert.DoesNotThrow(() => transport.Initialize(newInput, newOutput));

            Assert.That(oldInput.DisposeAttempted, Is.True);
            Assert.That(oldOutput.DisposeAttempted, Is.True);
            Assert.DoesNotThrow(() => transport.SendNotification("test/ping", new { value = 1 }));
            Assert.That(
                Encoding.UTF8.GetString(newOutput.ToArray()),
                Does.Contain("\"method\":\"test/ping\""));
        }

        [Test]
        public async Task InitializeStillRejectsReplacementWhileReaderLoopIsActive()
        {
            using var transport = new AcpTransportClient();
            using var input = new BlockingReadStream();
            using var output = new MemoryStream();
            transport.Initialize(input, output);
            transport.StartReaderLoop();

            var readStarted = await Task.WhenAny(
                input.ReadStarted,
                Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.That(readStarted, Is.SameAs(input.ReadStarted));

            using var replacementInput = new MemoryStream();
            using var replacementOutput = new MemoryStream();
            Assert.Throws<InvalidOperationException>(() =>
                transport.Initialize(replacementInput, replacementOutput));

            input.Release();
            await transport.StopReaderLoopAsync();
        }

        private sealed class ThrowingDisposeStream : MemoryStream
        {
            public Exception DisposeException { get; set; }

            public bool DisposeAttempted { get; private set; }

            protected override void Dispose(bool disposing)
            {
                DisposeAttempted = true;
                if (disposing && DisposeException != null)
                    throw DisposeException;
                base.Dispose(disposing);
            }
        }

        private sealed class BlockingReadStream : Stream
        {
            private readonly TaskCompletionSource<bool> _readStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<int> _release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task ReadStarted => _readStarted.Task;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public void Release()
            {
                _release.TrySetResult(0);
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                _readStarted.TrySetResult(true);
                return _release.Task.GetAwaiter().GetResult();
            }

            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                _readStarted.TrySetResult(true);
                return _release.Task;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    Release();
                base.Dispose(disposing);
            }
        }
    }
}
