using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
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
    private readonly SemaphoreSlim _sessionRenewalGate = new(1, 1);
    private readonly Lock _tokenLock = new();
    private string? _accessToken;
    private DateTimeOffset? _accessTokenExpiresAtUtc;
    private int _sessionEnded;

    /// <summary>
    /// How long before expiry a token is replaced. Public because the keep-alive
    /// schedules against it: a caller that guesses its own interval can step over
    /// the window entirely and wake up holding a token the server has already
    /// stopped accepting.
    /// </summary>
    public static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(5);

    /// <summary>When the current token expires, or null when no session is open.</summary>
    public DateTimeOffset? AccessTokenExpiresAtUtc
    {
        get { lock (_tokenLock) return _accessTokenExpiresAtUtc; }
    }

    /// <summary>
    /// Raised once when the session is over and renewal can no longer revive it.
    /// Signing in again clears the state, so a handler may prompt for credentials.
    /// </summary>
    public event EventHandler? SessionEnded;

    public event EventHandler? AccessTokenChanged;

    /// <summary>True once renewal has been refused; every authenticated call then fails fast.</summary>
    public bool HasSessionEnded => Volatile.Read(ref _sessionEnded) == 1;

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
        SetAccessToken(accessToken, null);

    public void SetAccessToken(string accessToken, DateTimeOffset? expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("An access token is required.", nameof(accessToken));

        // The keep-alive reads these from a background loop while sign-in and renewal
        // write them, so the pair moves under a lock rather than being torn apart.
        lock (_tokenLock)
        {
            _accessToken = accessToken;
            _accessTokenExpiresAtUtc = expiresAtUtc;
        }

        // A fresh credential revives the client. Without this an ended session would
        // stay latched shut after the user signed back in.
        Volatile.Write(ref _sessionEnded, 0);
        AccessTokenChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Renews now if the token has entered its <see cref="RenewalMargin"/>, and does
    /// nothing otherwise. The keep-alive calls this on the token's own schedule so a
    /// session survives a quiet stretch that produces no requests of its own.
    /// </summary>
    public Task EnsureSessionRenewedAsync(CancellationToken cancellationToken = default) =>
        RenewSessionIfNeededAsync(cancellationToken);

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

    // Passive chat refresh must not extend a session. The activity-aware keep-alive
    // owns background renewal; deliberate user writes retain the normal path.
    internal async Task<TResponse> GetWithoutRenewalAsync<TResponse>(string path, CancellationToken cancellationToken = default)
    {
        using var response = await SendHttpAsync(HttpMethod.Get, path, null, authenticated: true,
            cancellationToken: cancellationToken, renewSession: false);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

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

    public Task<TResponse> DeleteAsync<TResponse>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(HttpMethod.Delete, path, null, cancellationToken);

    internal static Uri ChatSocketAddress(Uri? baseAddress)
    {
        if (baseAddress is null || baseAddress.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Team chat requires a secure connection.");
        return new UriBuilder(new Uri(baseAddress, "/api/v1/chat/stream")) { Scheme = "wss" }.Uri;
    }

    internal async Task<ClientWebSocket> OpenChatSocketAsync(CancellationToken cancellationToken)
    {
        if (HasSessionEnded) throw new CloudSessionEndedException();
        var address = ChatSocketAddress(_httpClient.BaseAddress);
        var socket = new ClientWebSocket();
        try
        {
            lock (_tokenLock)
            {
                if (string.IsNullOrWhiteSpace(_accessToken))
                    throw new InvalidOperationException("Sign in to use team chat.");
                socket.Options.SetRequestHeader("Authorization", $"Bearer {_accessToken}");
            }
            await socket.ConnectAsync(address, cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

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
            : response.Content.Headers.TryGetValues(headerName, out var contentHeaders) ? contentHeaders.ToList() : [];
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
        CancellationToken cancellationToken,
        bool renewSession = true)
    {
        // Renewal needs a token the server still accepts, so once it has been refused
        // the session cannot be recovered by trying again. Failing here stops one dead
        // session from turning every later screen into its own rejected round trip.
        if (authenticated && HasSessionEnded)
            throw new CloudSessionEndedException();

        if (authenticated && renewSession)
            await RenewSessionIfNeededAsync(cancellationToken);

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

    private async Task RenewSessionIfNeededAsync(CancellationToken cancellationToken)
    {
        if (!NeedsSessionRenewal())
            return;

        await _sessionRenewalGate.WaitAsync(cancellationToken);
        try
        {
            if (!NeedsSessionRenewal())
                return;

            using var response = await SendHttpAsync(
                HttpMethod.Post,
                "/api/v1/auth/renew",
                body: null,
                authenticated: true,
                cancellationToken: cancellationToken,
                renewSession: false);

            // A refused renewal is terminal, not transient: either the token was already
            // too old to authenticate the renewal itself, or the twelve-hour cap from
            // credential entry has passed. Only a new sign-in restores the session, and
            // the caller has to be told that rather than shown an empty screen.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw MarkSessionEnded();

            var renewal = await ReadAsync<SessionRenewalResponse>(response, cancellationToken);
            SetAccessToken(renewal.AccessToken, renewal.ExpiresAtUtc);
        }
        finally
        {
            _sessionRenewalGate.Release();
        }
    }

    private CloudSessionEndedException MarkSessionEnded()
    {
        if (Interlocked.Exchange(ref _sessionEnded, 1) == 0)
            SessionEnded?.Invoke(this, EventArgs.Empty);
        return new CloudSessionEndedException();
    }

    private bool NeedsSessionRenewal() =>
        AccessTokenExpiresAtUtc is DateTimeOffset expiresAt &&
        expiresAt <= DateTimeOffset.UtcNow.Add(RenewalMargin);

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
        bool hasToken;
        lock (_tokenLock) hasToken = !string.IsNullOrWhiteSpace(_accessToken);
        if (authenticated && !hasToken)
            throw new InvalidOperationException("The Demo API session is not authenticated.");

        var request = new HttpRequestMessage(method, path);
        if (authenticated)
        {
            string? token;
            lock (_tokenLock) token = _accessToken;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
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
        string? validationMessage = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("errors", out var errors) &&
                    errors.ValueKind == JsonValueKind.Object)
                {
                    validationMessage = string.Join(
                        " ",
                        errors.EnumerateObject()
                            .SelectMany(property => property.Value.ValueKind == JsonValueKind.Array
                                ? property.Value.EnumerateArray()
                                    .Where(value => value.ValueKind == JsonValueKind.String)
                                    .Select(value => value.GetString())
                                : [])
                            .Where(message => !string.IsNullOrWhiteSpace(message))
                            .Distinct(StringComparer.Ordinal));
                }
                else
                {
                    error = JsonSerializer.Deserialize<ApiErrorDto>(body, JsonOptions);
                }
            }
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
            HttpStatusCode.BadRequest when !string.IsNullOrWhiteSpace(validationMessage) => validationMessage,
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

public class CloudApiException(
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

/// <summary>
/// The signed-in session is over; no further request can succeed until the user signs
/// in again. Derives from <see cref="CloudApiException"/> carrying 401 so that existing
/// unauthorized handling â the agenda's session-expiry warning among it â keeps working,
/// while a caller that can offer a sign-in prompt is able to catch this case alone.
/// </summary>
public sealed class CloudSessionEndedException()
    : CloudApiException(
        HttpStatusCode.Unauthorized,
        "Your Demo session has expired. Sign in again to continue.",
        correlationId: null)
{
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
