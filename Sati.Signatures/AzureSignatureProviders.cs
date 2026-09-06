using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Keys.Cryptography;
using Sati.Contracts.V1;

namespace Sati.Signatures;

/// <summary>
/// Server-owned transport. No redirects, HTTP retries, diagnostic body logging, or retained tokens.
/// The supplied credential should be this environment's managed identity. A handler is injectable
/// only for isolated tests; the default handler never forwards an authenticated request elsewhere.
/// </summary>
public sealed class AzureSignatureTransport : IDisposable
{
    private readonly TokenCredential _credential;
    private readonly HttpClient _http;

    public AzureSignatureTransport(TokenCredential credential, HttpMessageHandler? handler = null)
    {
        _credential = credential;
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None
        }) { Timeout = TimeSpan.FromSeconds(30) };
    }

    internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string scope, CancellationToken cancellationToken)
    {
        try
        {
            var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw Unavailable(); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Provider errors may echo addresses, document paths or invitation content.
            throw Unavailable();
        }
    }

    internal CryptographyClient CryptographyClient(Uri key)
    {
        var options = new CryptographyClientOptions(CryptographyClientOptions.ServiceVersion.V2025_07_01)
        {
            Transport = new HttpClientTransport(_http)
        };
        options.Retry.MaxRetries = 0;
        options.Diagnostics.IsLoggingEnabled = false;
        options.Diagnostics.IsLoggingContentEnabled = false;
        options.Diagnostics.IsDistributedTracingEnabled = false;
        return new CryptographyClient(key, _credential, options);
    }

    internal static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximumBytes, CancellationToken cancellationToken)
    {
        try
        {
            if (response.Content.Headers.ContentLength is long declared && (declared < 0 || declared > maximumBytes)) throw Unavailable();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumBytes - (int)output.Length + 1)), timeout.Token);
                if (count == 0) break;
                if (output.Length + count > maximumBytes) throw Unavailable();
                output.Write(buffer, 0, count);
            }
            if (response.Content.Headers.ContentLength is long expected && expected != output.Length) throw Unavailable();
            return output.ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw Unavailable(); }
        catch (Exception exception) when (exception is not OperationCanceledException and not SignatureWorkflowException) { throw Unavailable(); }
    }

    internal static SignatureWorkflowException Unavailable() => new("signature_provider_unavailable", "The signature service provider could not complete the request. Review the existing request before retrying.", 503);
    public void Dispose() => _http.Dispose();
}

internal static class SignatureAzureAddress
{
    internal static Uri Require(string value, string hostSuffix, bool rootOnly = false)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.Host.EndsWith(hostSuffix, StringComparison.OrdinalIgnoreCase) || uri.Host.Length <= hostSuffix.Length ||
            value.Any(char.IsWhiteSpace) || value.Contains('%') || value.Contains('\\') ||
            value.Contains("/../", StringComparison.Ordinal) || value.Contains("/./", StringComparison.Ordinal) ||
            (rootOnly && uri.AbsolutePath != "/")) throw AzureSignatureTransport.Unavailable();
        return uri;
    }
}

/// <summary>
/// Private Azure public-cloud container, authenticated downloads only, no SAS links.
/// Each operation verifies container privacy. Conditional PUT never overwrites prior bytes;
/// a retry against existing identical bytes is safe, and different bytes are a conflict.
/// https://learn.microsoft.com/en-us/rest/api/storageservices/put-blob
/// https://learn.microsoft.com/en-us/rest/api/storageservices/get-container-properties
/// </summary>
public sealed class AzureSignatureBlobStore(AzureSignatureTransport transport, SignatureOptions options) : ISignatureBlobStore
{
    // The source limit remains owned by SignatureRules. Allow bounded evidence-page overhead.
    public const int MaximumStoredBytes = SignatureRules.MaximumPdfBytes * 2;
    private const string Scope = "https://storage.azure.com/.default";
    private const string StorageVersion = "2023-11-03";

    public async Task WriteOnceAsync(string path, byte[] content, CancellationToken cancellationToken = default)
    {
        new SignatureFeature(options).RequireEnabled();
        if (content is null || content.Length is 0 or > MaximumStoredBytes) throw AzureSignatureTransport.Unavailable();
        var address = BlobAddress(path);
        await RequirePrivateContainerAsync(cancellationToken);
        using var request = StorageRequest(HttpMethod.Put, address);
        request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        request.Headers.Add("x-ms-blob-type", "BlockBlob");
        request.Headers.Add("x-ms-blob-content-type", "application/pdf");
        request.Headers.Add("x-ms-blob-cache-control", "no-store");
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var response = await transport.SendAsync(request, Scope, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Created) return;
        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            var existing = await ReadAsync(path, cancellationToken);
            if (CryptographicOperations.FixedTimeEquals(existing, content)) return;
            throw new SignatureWorkflowException("signature_storage_conflict", "A retained document already occupies this location with different contents.", 409);
        }
        throw AzureSignatureTransport.Unavailable();
    }

    public async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        new SignatureFeature(options).RequireEnabled();
        var address = BlobAddress(path);
        await RequirePrivateContainerAsync(cancellationToken);
        using var request = StorageRequest(HttpMethod.Get, address);
        using var response = await transport.SendAsync(request, Scope, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0) throw AzureSignatureTransport.Unavailable();
        var content = await AzureSignatureTransport.ReadBoundedAsync(response, MaximumStoredBytes, cancellationToken);
        if (content.Length == 0) throw AzureSignatureTransport.Unavailable();
        return content;
    }

    private Uri Container()
    {
        var uri = SignatureAzureAddress.Require(options.BlobContainerUri, ".blob.core.windows.net");
        if (!Regex.IsMatch(uri.AbsolutePath, "^/[a-z0-9][a-z0-9-]{1,61}[a-z0-9]/?$", RegexOptions.CultureInvariant) ||
            uri.AbsolutePath.Contains("--", StringComparison.Ordinal)) throw AzureSignatureTransport.Unavailable();
        return new Uri(uri.AbsoluteUri.TrimEnd('/'));
    }

    private Uri BlobAddress(string path)
    {
        // Never let an untrusted path become a URL, query, encoded separator, or parent traversal.
        if (string.IsNullOrEmpty(path) || path.Length > 400 || path.Split('/').Any(part =>
                part.Length == 0 || part is "." or ".." || !Regex.IsMatch(part, "^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)))
            throw new SignatureWorkflowException("signature_storage_path", "The signature document location is invalid.");
        return new Uri(Container().AbsoluteUri + "/" + path);
    }

    private async Task RequirePrivateContainerAsync(CancellationToken cancellationToken)
    {
        using var request = StorageRequest(HttpMethod.Head, new Uri(Container().AbsoluteUri + "?restype=container"));
        using var response = await transport.SendAsync(request, Scope, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK || response.Headers.Contains("x-ms-blob-public-access"))
            throw new SignatureWorkflowException("signature_storage_not_private", "Private signature storage could not be verified.", 503);
    }

    private static HttpRequestMessage StorageRequest(HttpMethod method, Uri address)
    {
        var request = new HttpRequestMessage(method, address);
        request.Headers.Add("x-ms-version", StorageVersion);
        request.Headers.Add("x-ms-date", DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture));
        return request;
    }
}

/// <summary>Separate registration prevents the portal's PIN permission from also opening invitation secrets.</summary>
public sealed class AzureSigningPinKeyWrapper(AzureSignatureTransport transport, SignatureOptions options)
    : AzureSignatureKeyWrapper(transport, options, () => options.PinKeyUri), ISigningPinKeyWrapper { }

public sealed class AzureSignatureOutboxKeyWrapper(AzureSignatureTransport transport, SignatureOptions options)
    : AzureSignatureKeyWrapper(transport, options, () => options.OutboxKeyUri), ISignatureOutboxKeyWrapper { }

public abstract class AzureSignatureKeyWrapper(AzureSignatureTransport transport, SignatureOptions options, Func<string> configuredKey)
    : IKeyWrapper
{
    public async Task<WrappedDataKey> WrapAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        new SignatureFeature(options).RequireEnabled();
        var address = TrustedKey(configuredKey());
        if (key is null || key.Length != 32) throw AzureSignatureTransport.Unavailable();
        try
        {
            var result = await transport.CryptographyClient(address).WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, key, cancellationToken);
            if (TrustedKey(result.KeyId) != address || result.EncryptedKey.Length is < 128 or > 1024) throw AzureSignatureTransport.Unavailable();
            return new(result.EncryptedKey, address.AbsoluteUri);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SignatureWorkflowException)
        { throw AzureSignatureTransport.Unavailable(); }
    }

    public async Task<byte[]> UnwrapAsync(byte[] key, string keyId, CancellationToken cancellationToken = default)
    {
        new SignatureFeature(options).RequireEnabled();
        // Reject database-controlled key IDs before constructing an SDK client or acquiring credentials.
        var address = TrustedKey(keyId);
        if (key is null || key.Length is < 128 or > 1024) throw AzureSignatureTransport.Unavailable();
        try
        {
            var result = await transport.CryptographyClient(address).UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, key, cancellationToken);
            if (TrustedKey(result.KeyId) != address || result.Key.Length != 32) throw AzureSignatureTransport.Unavailable();
            return result.Key;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SignatureWorkflowException)
        { throw AzureSignatureTransport.Unavailable(); }
    }

    private Uri TrustedKey(string value)
    {
        var expected = ParseKey(configuredKey());
        if (!string.IsNullOrEmpty(options.PinKeyUri) && !string.IsNullOrEmpty(options.OutboxKeyUri))
        {
            var pin = ParseKey(options.PinKeyUri);
            var outbox = ParseKey(options.OutboxKeyUri);
            if (string.Equals(pin.Host, outbox.Host, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pin.Segments[2], outbox.Segments[2], StringComparison.Ordinal)) throw AzureSignatureTransport.Unavailable();
        }
        var supplied = ParseKey(value);
        if (!string.Equals(supplied.Host, expected.Host, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(supplied.Segments[2], expected.Segments[2], StringComparison.Ordinal)) throw AzureSignatureTransport.Unavailable();
        // Older versions of this same key remain readable; new wrapping uses the configured version.
        return supplied;
    }

    private static Uri ParseKey(string value)
    {
        var uri = SignatureAzureAddress.Require(value, ".vault.azure.net");
        if (!Regex.IsMatch(uri.AbsolutePath, "^/keys/[A-Za-z0-9-]{1,127}/[a-fA-F0-9]{32}$", RegexOptions.CultureInvariant))
            throw AzureSignatureTransport.Unavailable();
        return uri;
    }
}

/// <summary>
/// ACS send-operation status is not recipient delivery/read evidence. Persist the caller's GUID
/// before calling SendAsync; after an uncertain result poll that GUID, never invent a new send.
/// https://learn.microsoft.com/en-us/rest/api/communication/email/email/send
/// https://learn.microsoft.com/en-us/rest/api/communication/email/email/get-send-result
/// </summary>
public sealed class AzureSignatureEmailSender(AzureSignatureTransport transport, SignatureOptions options) : ISignatureEmailSender
{
    private const string Scope = "https://communication.azure.com/.default";
    private const string ApiVersion = "2023-03-31";

    public async Task<SignatureEmailResult> SendAsync(Guid operationId, SignatureEmail email, CancellationToken cancellationToken = default)
    {
        if (!CanSend(email.Recipient)) return new("Suppressed");
        if (operationId == Guid.Empty || !ValidEmail(options.EmailSender)) throw AzureSignatureTransport.Unavailable();
        var link = ValidatedLink(email.Link);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint(), "emails:send?api-version=" + ApiVersion));
        request.Headers.Add("Operation-Id", operationId.ToString("D"));
        // Purpose is internal queue metadata; never incorporate caller-controlled text in the email.
        request.Content = JsonContent.Create(new
        {
            senderAddress = options.EmailSender,
            recipients = new { to = new[] { new { address = email.Recipient } } },
            content = new
            {
                subject = "A document is ready",
                plainText = "A document is ready for you to review. Open the private link below. Use the code established separately with your contact. " +
                    "If this message was unexpected, do not open the link; contact the person you normally work with using details you already have.\n\n" + link
            },
            userEngagementTrackingDisabled = true
        });
        using var response = await transport.SendAsync(request, Scope, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Accepted) throw AzureSignatureTransport.Unavailable();
        return await ReadOperationAsync(response, operationId, cancellationToken);
    }

    public async Task<SignatureEmailResult> GetStatusAsync(string operationId, CancellationToken cancellationToken = default)
    {
        if (!options.EmailEnabled || !new SignatureFeature(options).Enabled) return new("Suppressed");
        if (!Guid.TryParseExact(operationId, "D", out var id) || id == Guid.Empty) throw AzureSignatureTransport.Unavailable();
        // Reconstruct the address from trusted configuration. Never follow Operation-Location.
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Endpoint(), $"emails/operations/{id:D}?api-version={ApiVersion}"));
        using var response = await transport.SendAsync(request, Scope, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return new("Unknown", id.ToString("D"));
        if (response.StatusCode != HttpStatusCode.OK) throw AzureSignatureTransport.Unavailable();
        return await ReadOperationAsync(response, id, cancellationToken);
    }

    private bool CanSend(string recipient) => options.EmailEnabled && new SignatureFeature(options).Enabled &&
        ValidEmail(recipient) && options.AllowedTestRecipients?.Contains(recipient, StringComparer.Ordinal) == true;

    private static bool ValidEmail(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 254 &&
        !value.Any(char.IsWhiteSpace) && MailAddress.TryCreate(value, out var parsed) && parsed.Address == value;

    private Uri Endpoint() => SignatureAzureAddress.Require(options.EmailEndpoint, ".communication.azure.com", rootOnly: true);

    private string ValidatedLink(string link)
    {
        if (!Uri.TryCreate(options.PortalBaseUri, UriKind.Absolute, out var expected) || expected.Scheme != Uri.UriSchemeHttps ||
            expected.Port != 443 || expected.UserInfo.Length != 0 || expected.Query.Length != 0 || expected.Fragment.Length != 0 || expected.AbsolutePath != "/" ||
            !Uri.TryCreate(link, UriKind.Absolute, out var supplied) || supplied.GetLeftPart(UriPartial.Authority) != expected.GetLeftPart(UriPartial.Authority) ||
            supplied.UserInfo.Length != 0 || supplied.Query.Length != 0 || supplied.Fragment.Length != 0 ||
            !Regex.IsMatch(supplied.AbsolutePath, "^/(s|r)/[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant) || link.Any(char.IsWhiteSpace) || link.Contains('%') || link.Contains('\\'))
            throw AzureSignatureTransport.Unavailable();
        return supplied.AbsoluteUri;
    }

    private static async Task<SignatureEmailResult> ReadOperationAsync(HttpResponseMessage response, Guid expected, CancellationToken cancellationToken)
    {
        var bytes = await AzureSignatureTransport.ReadBoundedAsync(response, 16 * 1024, cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(bytes);
            if (!json.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(id.GetString(), out var actual) || actual != expected ||
                !json.RootElement.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
                throw AzureSignatureTransport.Unavailable();
            var state = status.GetString() switch
            {
                "NotStarted" => "Queued",
                "Running" => "Sending",
                "Succeeded" => "Sent",
                "Failed" => "Failed",
                "Canceled" => "Canceled",
                _ => throw AzureSignatureTransport.Unavailable()
            };
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (retry?.Date is DateTimeOffset date ? date - DateTimeOffset.UtcNow : null);
            // Preserve the provider's minimum delay. Excessively distant dates require review.
            if (delay?.TotalSeconds > 86400) throw AzureSignatureTransport.Unavailable();
            return new(state, expected.ToString("D"), delay is null ? null : (int)Math.Max(0, Math.Ceiling(delay.Value.TotalSeconds)));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { throw AzureSignatureTransport.Unavailable(); }
    }
}
