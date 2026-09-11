using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Unity;

internal static class AttachHandshake
{
    public static async Task<JsonObject> WaitAsync(
        Func<CancellationToken, Task<JsonObject>> probe,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                try { return await probe(deadline.Token); }
                catch (Exception error) when (error is IOException or SocketException or TimeoutException or JsonException) { }
                await Task.Delay(100, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UnityTargetException(
                "UnityAttachHandshakeTimeout",
                "Bootstrap loaded but Unity did not complete its main-thread handshake within 60 seconds.");
        }
    }
}
