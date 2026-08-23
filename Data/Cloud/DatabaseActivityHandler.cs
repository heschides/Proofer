using Sati.Services;
using System.Net.Http;

namespace Sati.Data.Cloud;

/// <summary>
/// Tracks the complete HTTP exchange. HttpClient's default completion mode buffers the response
/// body before this lease ends, so JSON and file retrieval remain visibly active until received.
/// </summary>
public sealed class DatabaseActivityHandler(IDatabaseActivityTracker tracker) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var activity = tracker.Begin();
        return await base.SendAsync(request, cancellationToken);
    }
}
