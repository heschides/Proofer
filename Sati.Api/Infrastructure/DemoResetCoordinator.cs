using System.Net.Http.Json;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal sealed class DemoResetOptions
{
    public const string SectionName = "DemoReset";
    public string FunctionEndpoint { get; init; } = string.Empty;
    public string FunctionKey { get; init; } = string.Empty;
}

internal sealed class DemoResetCoordinator(
    HttpClient client,
    Microsoft.Extensions.Options.IOptions<DemoResetOptions> options,
    ILogger<DemoResetCoordinator> logger)
{
    public async Task<DemoResetResultDto> RequestAsync(int actorUserId, CancellationToken cancellationToken)
    {
        var configured = options.Value.FunctionEndpoint;
        var functionKey = options.Value.FunctionKey;
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(functionKey))
            throw new InvalidOperationException("The Demo reset service is not configured.");

        var requestId = Guid.NewGuid();
        logger.LogInformation(
            "Full Demo reset {RequestId} requested by user {ActorUserId}.",
            requestId,
            actorUserId);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { requestId, actorUserId })
        };
        request.Headers.Add("x-functions-key", functionKey);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        logger.LogInformation("Full Demo reset {RequestId} completed.", requestId);
        return new DemoResetResultDto(requestId, DateTime.UtcNow, "Reset completed");
    }
}
