using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan[] NameResolutionRetryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(1)
    ];

    private readonly HttpClient _httpClient;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private string? _accessToken;

    public CloudApiClient(HttpClient httpClient)
        : this(httpClient, Task.Delay)
    {
    }

    internal CloudApiClient(
        HttpClient httpClient,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _httpClient = httpClient;
        _delay = delay;
    }

    public void SetAccessToken(string accessToken) =>
        _accessToken = string.IsNullOrWhiteSpace(accessToken)
            ? throw new ArgumentException("An access token is required.", nameof(accessToken))
            : accessToken;

    public async Task<TResponse> PostAnonymousAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendHttpAsync(
            HttpMethod.Post,
            path,
            request,
            authenticated: false,
            cancellationToken);
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
        using var response = await SendHttpAsync(
            HttpMethod.Get, path, null, authenticated: true, cancellationToken);
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
        using var response = await SendHttpAsync(
            HttpMethod.Get, path, null, authenticated: true, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<byte[]> PostBytesAsync<TRequest>(
        string path,
        TRequest body,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendHttpAsync(
            HttpMethod.Post, path, body, authenticated: true, cancellationToken);
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
        using var response = await SendHttpAsync(
            HttpMethod.Post, path, body, authenticated: true, cancellationToken);
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
        using var response = await SendHttpAsync(
            method, path, body, authenticated: true, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    private async Task SendWithoutResponseAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var response = await SendHttpAsync(
            method, path, body, authenticated: true, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendHttpAsync(
        HttpMethod method,
        string path,
        object? body,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = CreateRequest(method, path, body, authenticated);
            try
            {
                return await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (
                !cancellationToken.IsCancellationRequested &&
                IsNameResolutionFailure(ex) &&
                attempt < NameResolutionRetryDelays.Length)
            {
                // DNS failure proves that no connection was made, so even a write is safe to
                // retry. Do not broaden this to timeouts or connection resets: those are
                // ambiguous and could repeat a request the server already committed.
                await _delay(NameResolutionRetryDelays[attempt], cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw CloudConnectivityException.From(ex, IsNameResolutionFailure(ex));
            }
            catch (HttpRequestException ex)
            {
                throw CloudConnectivityException.From(ex, IsNameResolutionFailure(ex));
            }
        }
    }

    private static bool IsNameResolutionFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException
                {
                    HttpRequestError: HttpRequestError.NameResolutionError
                })
            {
                return true;
            }

            if (current is SocketException socket &&
                socket.SocketErrorCode is SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData)
            {
                return true;
            }
        }

        return false;
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        object? body,
        bool authenticated = true)
    {
        if (authenticated && string.IsNullOrWhiteSpace(_accessToken))
            throw new InvalidOperationException("The Demo API session is not authenticated.");

        var request = new HttpRequestMessage(method, path);
        if (authenticated)
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

public sealed class CloudConnectivityException : Exception
{
    private CloudConnectivityException(
        string message,
        Exception innerException,
        bool requestWasDefinitelyNotSent)
        : base(message, innerException)
    {
        RequestWasDefinitelyNotSent = requestWasDefinitelyNotSent;
    }

    /// <summary>
    /// True only when DNS resolution failed, which proves that no connection to the API was made.
    /// False means delivery is ambiguous and callers must not automatically repeat a write.
    /// </summary>
    public bool RequestWasDefinitelyNotSent { get; }

    internal static CloudConnectivityException From(
        Exception exception,
        bool nameResolutionFailure)
    {
        if (nameResolutionFailure)
        {
            return new CloudConnectivityException(
                "Sati could not find the Demo server on the network after three attempts. " +
                "The request was not sent. Check the internet or DNS connection and try again.",
                exception,
                requestWasDefinitelyNotSent: true);
        }

        if (exception is OperationCanceledException)
        {
            return new CloudConnectivityException(
                "The Demo server did not respond before the connection timeout. Sati did not " +
                "repeat the request because it cannot safely tell whether the server received it.",
                exception,
                requestWasDefinitelyNotSent: false);
        }

        return new CloudConnectivityException(
            "Sati lost its connection to the Demo server. Sati did not repeat the request because " +
            "it cannot safely tell whether the server received it.",
            exception,
            requestWasDefinitelyNotSent: false);
    }
}
