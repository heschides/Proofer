using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string? _accessToken;

    public void SetAccessToken(string accessToken) =>
        _accessToken = string.IsNullOrWhiteSpace(accessToken)
            ? throw new ArgumentException("An access token is required.", nameof(accessToken))
            : accessToken;

    public async Task<TResponse> PostAnonymousAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(path, request, JsonOptions, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    public Task<TResponse> GetAsync<TResponse>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Get, path, null, cancellationToken);

    /// <summary>
    /// Reads a nullable string body, treating "no content" as null rather than as
    /// a failure. <see cref="GetAsync{TResponse}"/> cannot express this: it rejects
    /// a null result as an empty response, and a route returning an unset
    /// <c>string?</c> — the consumer journal, for one — legitimately sends back an
    /// empty body. Callers of that shape must use this method or they throw on
    /// every record whose value has never been set.
    /// </summary>
    public async Task<string?> GetStringOrNullAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, path, null);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body)
            ? null
            : JsonSerializer.Deserialize<string>(body, JsonOptions);
    }

    public Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Post, path, request, cancellationToken);

    public Task<TResponse> PutAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Put, path, request, cancellationToken);

    public async Task PutAsync<TRequest>(string path, TRequest request, CancellationToken cancellationToken = default) =>
        await SendWithoutResponseAsync(HttpMethod.Put, path, request, cancellationToken);

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default) =>
        await SendWithoutResponseAsync(HttpMethod.Delete, path, null, cancellationToken);

    public async Task<byte[]> GetBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, path, null);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<byte[]> PostBytesAsync<TRequest>(
        string path,
        TRequest body,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, path, body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// A byte response together with the values of one response header.
    ///
    /// Exists for the DHHS form fill, where the body is a PDF and the server still has
    /// something to say about it — which boxes it could not fill. Putting that in a
    /// header rather than wrapping the PDF in JSON keeps the response a file the
    /// caller can hand straight to a save dialog.
    /// </summary>
    public async Task<(byte[] Bytes, IReadOnlyList<string> HeaderValues)> PostBytesWithHeaderAsync<TRequest>(
        string path,
        TRequest body,
        string headerName,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, path, body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var values = response.Headers.TryGetValues(headerName, out var found)
            ? found.ToList()
            : [];
        return (await response.Content.ReadAsByteArrayAsync(cancellationToken), values);
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    private async Task SendWithoutResponseAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body)
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
            throw new InvalidOperationException("The Demo API session is not authenticated.");

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);
        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The Demo API returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        ApiErrorDto? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            // The server may return an empty 401/403 or a proxy-generated response.
        }

        var retryAfter = GetRetryAfter(response);
        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your Demo session is invalid or has expired. Sign in again.",
            HttpStatusCode.Forbidden => "Your Demo account is not permitted to perform this action.",
            HttpStatusCode.NotFound => "The requested Demo record was not found or is outside your caseload.",
            HttpStatusCode.TooManyRequests when retryAfter.HasValue =>
                $"Too many requests were sent. Try again in about {Math.Max(1, (int)Math.Ceiling(retryAfter.Value.TotalSeconds))} seconds.",
            HttpStatusCode.TooManyRequests => "Too many requests were sent. Wait one minute and try again.",
            _ => error?.Message ?? $"The Demo API returned {(int)response.StatusCode}."
        };

        throw new CloudApiException(response.StatusCode, message, error?.CorrelationId, retryAfter, error?.Code);
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
            return delta;

        if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
        }

        return null;
    }
}

public sealed class CloudApiException(
    HttpStatusCode statusCode,
    string message,
    string? correlationId,
    TimeSpan? retryAfter = null,
    string? code = null)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? CorrelationId { get; } = correlationId;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public string? Code { get; } = code;
}
