using System.Net;
using System.Text;

namespace DotCraft.Unity.McpGateway.Tests;

public sealed class GatewayTransportTests
{
    [Fact]
    public async Task SubsequentCallReadsNewEndpointAndToken()
    {
        using var first = new CliFixture();
        using var second = new CliFixture();
        var client = new UnityToolGatewayClient(new ProjectStateStore(first.Root));
        var reply = first.ReplyOnceAsync();
        Assert.True((await client.CallAsync("custom", null, TestContext.Current.CancellationToken)).Success);
        await reply;
        first.Token = second.Token;
        first.WriteDiscovery(endpoint: $"http://127.0.0.1:{second.Port}/dotcraft-unity");
        reply = second.ReplyOnceAsync();
        Assert.True((await client.CallAsync("custom", null, TestContext.Current.CancellationToken)).Success);
        await reply;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeadlineCoversResponseHeadersAndBodyWithoutReplay(bool headersReceived)
    {
        using var fixture = new CliFixture();
        var handler = new HangingHandler(headersReceived);
        using var http = new HttpClient(handler);
        var client = new UnityToolGatewayClient(new ProjectStateStore(fixture.Root), http, TimeSpan.FromMilliseconds(150));
        var result = await client.CallAsync("custom", null, TestContext.Current.CancellationToken);
        Assert.Equal("UnityTimeout", result.ErrorCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfReportingTimeout()
    {
        using var fixture = new CliFixture();
        var handler = new HangingHandler(false);
        using var http = new HttpClient(handler);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(100);
        var client = new UnityToolGatewayClient(new ProjectStateStore(fixture.Root), http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CallAsync("custom", null, cancellation.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DisconnectProducesStructuredFailure()
    {
        using var fixture = new CliFixture();
        var pending = fixture.Listener.GetContextAsync();
        var call = fixture.RunAsync("call", "custom", "--json");
        var request = await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        request.Response.StatusCode = 200;
        request.Response.ContentLength64 = 1000;
        await request.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{"), TestContext.Current.CancellationToken);
        request.Response.Abort();
        var output = await call;
        Assert.Equal(1, output.ExitCode);
        Assert.Equal("UnityDisconnected", output.Json.GetProperty("errorCode").GetString());
    }

    private sealed class HangingHandler(bool headersReceived) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (!headersReceived) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream()) };
        }
    }

    private sealed class HangingStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
