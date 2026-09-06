using System.Net.WebSockets;
using System.Text.Json;

namespace Sati.Data.Cloud;

/// <summary>Only a content-free hint. Every read still passes through the authorized HTTP route.</summary>
public sealed class ChatStreamConnection(CloudApiClient api)
{
    public async Task RunAsync(Action changed, CancellationToken cancellationToken)
    {
        var retrySeconds = 1;
        while (!cancellationToken.IsCancellationRequested && !api.HasSessionEnded)
        {
            using var lease = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            void EndLease(object? sender, EventArgs args)
            {
                try { lease.Cancel(); } catch (ObjectDisposedException) { }
            }
            api.AccessTokenChanged += EndLease;
            api.SessionEnded += EndLease;
            try
            {
                using var socket = await api.OpenChatSocketAsync(lease.Token).ConfigureAwait(false);
                retrySeconds = 1;
                var buffer = new byte[256];
                while (!lease.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(buffer.AsMemory(), lease.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    // No content or identifiers are accepted. Oversized/fragmented frames
                    // are protocol failures, not a reason to allocate an unbounded buffer.
                    if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage) break;
                    using var json = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
                    if (json.RootElement.ValueKind != JsonValueKind.Object ||
                        json.RootElement.EnumerateObject().Count() != 1 ||
                        !json.RootElement.TryGetProperty("type", out var type) ||
                        type.GetString() != "changed") break;
                    changed();
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or CloudApiException or CloudConnectivityException or JsonException or InvalidOperationException)
            {
                // The independently awaited HTTP loop remains usable; no message
                // contents, bearer values, or network exception details are logged.
            }
            finally
            {
                api.AccessTokenChanged -= EndLease;
                api.SessionEnded -= EndLease;
            }
            if (cancellationToken.IsCancellationRequested || api.HasSessionEnded) break;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(retrySeconds) + TimeSpan.FromMilliseconds(Random.Shared.Next(250)), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            retrySeconds = Math.Min(30, retrySeconds * 2);
        }
    }
}
