using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Signatures.Tests;

public sealed class AzureSignatureProviderTests
{
    private const string Version = "0123456789abcdef0123456789abcdef";
    private const string OldVersion = "abcdef0123456789abcdef0123456789";
    private const string PinKey = "https://synthetic-vault.vault.azure.net/keys/signing-pin/" + Version;
    private const string OutboxKey = "https://synthetic-vault.vault.azure.net/keys/signature-outbox/" + Version;
    private const string Token = Version + Version;
    private const string Recipient = "synthetic@example.test";

    private static SignatureOptions Enabled() => new()
    {
        Enabled = true, ExpectedEnvironment = "Testing", ExpectedDatabaseName = "SatiApiTests",
        BlobContainerUri = "https://syntheticstore.blob.core.windows.net/signatures",
        PinKeyUri = PinKey, OutboxKeyUri = OutboxKey,
        PortalBaseUri = "https://synthetic.example.test/", EmailEnabled = true,
        EmailEndpoint = "https://synthetic.communication.azure.com/",
        EmailSender = "sender@example.test", AllowedTestRecipients = [Recipient]
    };

    [Fact]
    public async Task Disabled_or_production_configuration_cannot_contact_any_provider()
    {
        var disabled = Enabled();
        disabled.Enabled = false;
        var production = Enabled();
        production.ExpectedEnvironment = "Production";
        production.ExpectedDatabaseName = "SatiProduction";
        foreach (var options in new[] { disabled, production })
        {
            var credential = new FakeCredential();
            var handler = new CaptureHandler(_ => throw new InvalidOperationException("Network must remain unused."));
            using var transport = new AzureSignatureTransport(credential, handler);
            await Assert.ThrowsAsync<SignatureWorkflowException>(() => new AzureSignatureBlobStore(transport, options).ReadAsync("source.pdf"));
            await Assert.ThrowsAsync<SignatureWorkflowException>(() => new AzureSigningPinKeyWrapper(transport, options).WrapAsync(new byte[32]));
            var sender = new AzureSignatureEmailSender(transport, options);
            Assert.Equal("Suppressed", (await sender.SendAsync(Guid.NewGuid(), new(Recipient, "https://synthetic.example.test/s/" + Token, "Invitation"))).State);
            Assert.Equal("Suppressed", (await sender.GetStatusAsync(Guid.NewGuid().ToString("D"))).State);
            Assert.Empty(handler.Requests);
            Assert.Equal(0, credential.Calls);
        }
    }

    [Fact]
    public async Task Email_remains_suppressed_until_separately_enabled()
    {
        var options = Enabled();
        options.EmailEnabled = false;
        var credential = new FakeCredential();
        var handler = new CaptureHandler(_ => Response(HttpStatusCode.Accepted));
        using var transport = new AzureSignatureTransport(credential, handler);
        var sender = new AzureSignatureEmailSender(transport, options);
        Assert.Equal("Suppressed", (await sender.SendAsync(Guid.NewGuid(), new(Recipient, "https://synthetic.example.test/s/" + Token, "Invitation"))).State);
        Assert.Equal(0, credential.Calls);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("../source.pdf")]
    [InlineData("agency/../source.pdf")]
    [InlineData("agency//source.pdf")]
    [InlineData("agency/%2e%2e/source.pdf")]
    [InlineData("agency\\source.pdf")]
    [InlineData("https://untrusted.example/source.pdf")]
    [InlineData("source.pdf?secret=hidden")]
    [InlineData("/source.pdf")]
    public async Task Blob_untrusted_paths_are_rejected_before_credentials_or_http(string path)
    {
        var credential = new FakeCredential();
        var handler = new CaptureHandler(_ => Response(HttpStatusCode.OK));
        using var transport = new AzureSignatureTransport(credential, handler);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => new AzureSignatureBlobStore(transport, Enabled()).ReadAsync(path));
        Assert.Equal(0, credential.Calls);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("blob")]
    [InlineData("container")]
    public async Task Public_container_refusal_prevents_document_upload_and_download(string publicAccess)
    {
        var handler = new CaptureHandler(_ =>
        {
            var response = Response(HttpStatusCode.OK);
            response.Headers.Add("x-ms-blob-public-access", publicAccess);
            return response;
        });
        using var transport = new AzureSignatureTransport(new FakeCredential(), handler);
        var store = new AzureSignatureBlobStore(transport, Enabled());
        Assert.Equal("signature_storage_not_private", (await Assert.ThrowsAsync<SignatureWorkflowException>(() => store.WriteOnceAsync("source.pdf", [1, 2, 3]))).Code);
        Assert.Equal("signature_storage_not_private", (await Assert.ThrowsAsync<SignatureWorkflowException>(() => store.ReadAsync("source.pdf"))).Code);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Head, request.Method));
    }

    [Fact]
    public async Task Conditional_blob_write_preserves_prior_bytes_and_accepts_only_an_identical_retry()
    {
        byte[]? retained = null;
        var handler = new CaptureHandler(request =>
        {
            if (request.Method == HttpMethod.Head) return Response(HttpStatusCode.OK);
            if (request.Method == HttpMethod.Get) return Response(HttpStatusCode.OK, retained!);
            // This fake behaves like Azure: removing If-None-Match would overwrite the evidence.
            if (retained is not null && request.Headers.TryGetValue("If-None-Match", out var conditional) && conditional == "*")
                return Response(HttpStatusCode.PreconditionFailed);
            retained = request.Body;
            return Response(HttpStatusCode.Created);
        });
        var credential = new FakeCredential();
        using var transport = new AzureSignatureTransport(credential, handler);
        var store = new AzureSignatureBlobStore(transport, Enabled());
        await store.WriteOnceAsync("agency-1/frozen/source.pdf", [1, 2, 3]);
        await store.WriteOnceAsync("agency-1/frozen/source.pdf", [1, 2, 3]);
        var conflict = await Assert.ThrowsAsync<SignatureWorkflowException>(() => store.WriteOnceAsync("agency-1/frozen/source.pdf", [4, 5, 6]));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(new byte[] { 1, 2, 3 }, retained);
        Assert.All(handler.Requests.Where(x => x.Method == HttpMethod.Put), request =>
        {
            Assert.Equal("*", request.Headers["If-None-Match"]);
            Assert.Equal("BlockBlob", request.Headers["x-ms-blob-type"]);
            Assert.Equal("no-store", request.Headers["x-ms-blob-cache-control"]);
            Assert.Equal("application/pdf", request.ContentType);
        });
        Assert.All(credential.Scopes, scope => Assert.Equal("https://storage.azure.com/.default", scope));
    }

    [Fact]
    public async Task Oversized_blob_is_rejected_before_upload_or_body_read()
    {
        var stream = new CountingStream(AzureSignatureBlobStore.MaximumStoredBytes + 1L);
        var handler = new CaptureHandler(request => request.Method == HttpMethod.Head ? Response(HttpStatusCode.OK) :
            new HttpResponseMessage(HttpStatusCode.OK) { Content = StreamContent(stream, AzureSignatureBlobStore.MaximumStoredBytes + 1L) });
        var credential = new FakeCredential();
        using var transport = new AzureSignatureTransport(credential, handler);
        var store = new AzureSignatureBlobStore(transport, Enabled());
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => store.WriteOnceAsync("source.pdf", new byte[AzureSignatureBlobStore.MaximumStoredBytes + 1]));
        Assert.Equal(0, credential.Calls);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => store.ReadAsync("source.pdf"));
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public async Task Chunked_blob_read_stops_at_the_explicit_limit()
    {
        var stream = new CountingStream(AzureSignatureBlobStore.MaximumStoredBytes + 1024L * 1024);
        var handler = new CaptureHandler(request => request.Method == HttpMethod.Head ? Response(HttpStatusCode.OK) :
            new HttpResponseMessage(HttpStatusCode.OK) { Content = StreamContent(stream) });
        using var transport = new AzureSignatureTransport(new FakeCredential(), handler);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => new AzureSignatureBlobStore(transport, Enabled()).ReadAsync("source.pdf"));
        Assert.Equal(AzureSignatureBlobStore.MaximumStoredBytes + 1L, stream.BytesRead);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Partial_or_compressed_blob_is_not_accepted_as_the_original(bool partial)
    {
        var handler = new CaptureHandler(request =>
        {
            if (request.Method == HttpMethod.Head) return Response(HttpStatusCode.OK);
            var response = Response(partial ? HttpStatusCode.PartialContent : HttpStatusCode.OK, [1, 2]);
            if (!partial) response.Content.Headers.ContentEncoding.Add("gzip");
            return response;
        });
        using var transport = new AzureSignatureTransport(new FakeCredential(), handler);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => new AzureSignatureBlobStore(transport, Enabled()).ReadAsync("source.pdf"));
    }

    [Theory]
    [InlineData("https://other-vault.vault.azure.net/keys/signing-pin/" + Version)]
    [InlineData("https://synthetic-vault.vault.azure.net/keys/signature-outbox/" + Version)]
    [InlineData("https://synthetic-vault.vault.azure.net.evil.example/keys/signing-pin/" + Version)]
    [InlineData("http://synthetic-vault.vault.azure.net/keys/signing-pin/" + Version)]
    [InlineData("https://synthetic-vault.vault.azure.net/keys/signing-pin")]
    [InlineData("https://synthetic-vault.vault.azure.net/keys/signing-pin/" + Version + "?other=key")]
    [InlineData("https://synthetic-vault.vault.azure.net/keys/signing-pin/" + Version + "#fragment")]
    [InlineData("https://synthetic-vault.vault.azure.net/keys/signing-pin/%30" + Version)]
    public async Task Untrusted_key_identifiers_are_refused_before_token_or_network_access(string keyId)
    {
        var credential = new FakeCredential();
        var handler = new CaptureHandler(_ => Response(HttpStatusCode.OK));
        using var transport = new AzureSignatureTransport(credential, handler);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => new AzureSigningPinKeyWrapper(transport, Enabled()).UnwrapAsync(new byte[256], keyId));
        Assert.Equal(0, credential.Calls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Versioned_key_wrap_and_unwrap_use_separate_keys_and_the_declared_algorithm()
    {
        var handler = KeyHandler();
        using var transport = new AzureSignatureTransport(new FakeCredential(), handler);
        var options = Enabled();
        var pin = new AzureSigningPinKeyWrapper(transport, options);
        var invitation = new AzureSignatureOutboxKeyWrapper(transport, options);
        var wrapped = await pin.WrapAsync(Enumerable.Repeat((byte)7, 32).ToArray());
        Assert.Equal(PinKey, wrapped.KeyId);
        Assert.Equal(256, wrapped.WrappedKey.Length);
        var olderKey = PinKey.Replace(Version, OldVersion, StringComparison.Ordinal);
        Assert.Equal(Enumerable.Repeat((byte)9, 32), await pin.UnwrapAsync(wrapped.WrappedKey, olderKey));
        Assert.Equal(OutboxKey, (await invitation.WrapAsync(new byte[32])).KeyId);
        var operations = handler.Requests.Where(x => x.Method == HttpMethod.Post).ToArray();
        Assert.Equal(3, operations.Length);
        Assert.Contains(operations, x => x.Uri.AbsolutePath == "/keys/signing-pin/" + OldVersion + "/unwrapKey");
        Assert.Contains(operations, x => x.Uri.AbsolutePath == "/keys/signature-outbox/" + Version + "/wrapKey");
        Assert.All(operations, request =>
        {
            using var json = JsonDocument.Parse(request.Body);
            Assert.Equal("RSA-OAEP-256", json.RootElement.GetProperty("alg").GetString());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Key_response_for_a_different_key_is_not_accepted(bool unwrap)
    {
        var handler = KeyHandler(OutboxKey);
        using var transport = new AzureSignatureTransport(new FakeCredential(), handler);
        var wrapper = new AzureSigningPinKeyWrapper(transport, Enabled());
        await Assert.ThrowsAsync<SignatureWorkflowException>(async () =>
        {
            if (unwrap) await wrapper.UnwrapAsync(new byte[256], PinKey);
            else await wrapper.WrapAsync(new byte[32]);
        });
    }

    [Fact]
    public async Task Pin_and_invitation_roles_cannot_be_configured_to_use_the_same_key()
    {
        var handler = new CaptureHandler(_ => Response(HttpStatusCode.OK));
        var credential = new FakeCredential();
        using var transport = new AzureSignatureTransport(credential, handler);
        var options = Enabled();
        options.OutboxKeyUri = PinKey.Replace(Version, OldVersion, StringComparison.Ordinal);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => new AzureSigningPinKeyWrapper(transport, options).WrapAsync(new byte[32]));
        Assert.Equal(0, credential.Calls);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("Synthetic@example.test")]
    [InlineData("synthetic@sub.example.test")]
    [InlineData("someoneelse@example.test")]
    [InlineData("Synthetic Person <synthetic@example.test>")]
    [InlineData("synthetic@example.test\r\nBcc:other@example.test")]
    public async Task Email_requires_an_exact_synthetic_allowlist_match(string recipient)
    {
        var credential = new FakeCredential();
        var handler = new CaptureHandler(_ => Response(HttpStatusCode.Accepted));
        using var transport = new AzureSignatureTransport(credential, handler);
        var result = await new AzureSignatureEmailSender(transport, Enabled()).SendAsync(Guid.NewGuid(), new(recipient, "https://synthetic.example.test/s/" + Token, "Invitation"));
        Assert.Equal("Suppressed", result.State);
        Assert.Equal(0, credential.Calls);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("https://untrusted.example/s/" + Token)]
    [InlineData("http://synthetic.example.test/s/" + Token)]
    [InlineData("https://synthetic.example.test/s/" + Token + "?pin=12345678")]
    [InlineData("https://synthetic.example.test/s/" + Token + "#hidden")]
    [InlineData("https://synthetic.example.test/s/short")]
    [InlineData("https://synthetic.example.test/other/" + Token)]
    public async Task Email_does_not_send_untrusted_or_information_bearing_links(string link)
    {
        var credential = new FakeCredential();
        var handler = new CaptureHandler(_ => Response(HttpStatusCode.Accepted));
        using var transport = new AzureSignatureTransport(credential, handler);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => new AzureSignatureEmailSender(transport, Enabled()).SendAsync(Guid.NewGuid(), new(Recipient, link, "Invitation")));
        Assert.Equal(0, credential.Calls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Email_submission_is_generic_and_polling_uses_only_the_persisted_operation_id()
    {
        var operation = Guid.NewGuid();
        var handler = new CaptureHandler(request =>
        {
            var response = Operation(request.Method == HttpMethod.Post ? HttpStatusCode.Accepted : HttpStatusCode.OK, operation,
                request.Method == HttpMethod.Post ? "Running" : "Succeeded");
            // A provider response can never redirect an authenticated request to this supplied URL.
            response.Headers.Add("Operation-Location", "https://untrusted.example/steal-token");
            return response;
        });
        var credential = new FakeCredential();
        using var transport = new AzureSignatureTransport(credential, handler);
        var sender = new AzureSignatureEmailSender(transport, Enabled());
        Assert.Equal(new SignatureEmailResult("Sending", operation.ToString("D")), await sender.SendAsync(operation,
            new(Recipient, "https://synthetic.example.test/s/" + Token, "private clinical information that must never enter email")));
        Assert.Equal(new SignatureEmailResult("Sent", operation.ToString("D")), await sender.GetStatusAsync(operation.ToString("D")));
        var post = handler.Requests[0];
        Assert.Equal(operation.ToString("D"), post.Headers["Operation-Id"]);
        Assert.DoesNotContain("clinical", Encoding.UTF8.GetString(post.Body));
        using var json = JsonDocument.Parse(post.Body);
        Assert.True(json.RootElement.GetProperty("userEngagementTrackingDisabled").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("attachments", out _));
        Assert.Equal(Recipient, Assert.Single(json.RootElement.GetProperty("recipients").GetProperty("to").EnumerateArray()).GetProperty("address").GetString());
        Assert.Equal("A document is ready", json.RootElement.GetProperty("content").GetProperty("subject").GetString());
        Assert.Equal("https://synthetic.communication.azure.com/emails/operations/" + operation.ToString("D") + "?api-version=2023-03-31", handler.Requests[1].Uri.AbsoluteUri);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer synthetic-access-token", request.Headers["Authorization"]));
        Assert.All(credential.Scopes, scope => Assert.Equal("https://communication.azure.com/.default", scope));
    }

    [Theory]
    [InlineData("NotStarted", "Queued")]
    [InlineData("Running", "Sending")]
    [InlineData("Succeeded", "Sent")]
    [InlineData("Failed", "Failed")]
    [InlineData("Canceled", "Canceled")]
    public async Task Email_operation_status_never_claims_recipient_delivery(string providerState, string expected)
    {
        var operation = Guid.NewGuid();
        var handler = new CaptureHandler(_ => Operation(HttpStatusCode.OK, operation, providerState));
        using var transport = new AzureSignatureTransport(new FakeCredential(), handler);
        Assert.Equal(expected, (await new AzureSignatureEmailSender(transport, Enabled()).GetStatusAsync(operation.ToString("D"))).State);
    }

    [Fact]
    public async Task Missing_operation_remains_unknown_and_never_causes_another_email()
    {
        var operation = Guid.NewGuid();
        var handler = new CaptureHandler(_ => Response(HttpStatusCode.NotFound));
        using var transport = new AzureSignatureTransport(new FakeCredential(), handler);
        Assert.Equal(new SignatureEmailResult("Unknown", operation.ToString("D")), await new AzureSignatureEmailSender(transport, Enabled()).GetStatusAsync(operation.ToString("D")));
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task Status_lookup_rejects_urls_before_credentials_or_network_access()
    {
        var credential = new FakeCredential();
        var handler = new CaptureHandler(_ => Response(HttpStatusCode.OK));
        using var transport = new AzureSignatureTransport(credential, handler);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => new AzureSignatureEmailSender(transport, Enabled()).GetStatusAsync("https://untrusted.example/operation"));
        Assert.Equal(0, credential.Calls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_unrelated_operation_response_cannot_be_attached_to_the_request()
    {
        var handler = new CaptureHandler(_ => Operation(HttpStatusCode.OK, Guid.NewGuid(), "Succeeded"));
        using var transport = new AzureSignatureTransport(new FakeCredential(), handler);
        await Assert.ThrowsAsync<SignatureWorkflowException>(() => new AzureSignatureEmailSender(transport, Enabled()).GetStatusAsync(Guid.NewGuid().ToString("D")));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"id\":5,\"status\":\"Succeeded\"}")]
    [InlineData("not-json synthetic secret")]
    public async Task Malformed_provider_response_is_a_neutral_failure(string responseBody)
    {
        var handler = new CaptureHandler(_ => Response(HttpStatusCode.OK, Encoding.UTF8.GetBytes(responseBody)));
        using var transport = new AzureSignatureTransport(new FakeCredential(), handler);
        var error = await Assert.ThrowsAsync<SignatureWorkflowException>(() => new AzureSignatureEmailSender(transport, Enabled()).GetStatusAsync(Guid.NewGuid().ToString("D")));
        Assert.DoesNotContain("synthetic secret", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Receipt_notification_accepts_only_the_same_bounded_private_link_format()
    {
        var operation = Guid.NewGuid();
        var handler = new CaptureHandler(_ => Operation(HttpStatusCode.Accepted, operation, "NotStarted"));
        using var transport = new AzureSignatureTransport(new FakeCredential(), handler);
        var result = await new AzureSignatureEmailSender(transport, Enabled()).SendAsync(operation, new(Recipient, "https://synthetic.example.test/r/" + Token, "Receipt"));
        Assert.Equal("Queued", result.State);
        Assert.Contains("https://synthetic.example.test/r/" + Token, Encoding.UTF8.GetString(Assert.Single(handler.Requests).Body));
    }

    [Fact]
    public async Task Uncertain_email_submission_is_not_retried_and_does_not_echo_provider_secrets()
    {
        var handler = new CaptureHandler(_ => throw new HttpRequestException("synthetic secret in provider error"));
        using var transport = new AzureSignatureTransport(new FakeCredential(), handler);
        var error = await Assert.ThrowsAsync<SignatureWorkflowException>(() => new AzureSignatureEmailSender(transport, Enabled()).SendAsync(Guid.NewGuid(), new(Recipient, "https://synthetic.example.test/s/" + Token, "Invitation")));
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("synthetic secret", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("blob", "https://untrusted.example/signatures")]
    [InlineData("blob", "https://syntheticstore.blob.core.windows.net/signatures?sig=secret")]
    [InlineData("key", "https://untrusted.example/keys/signing-pin/" + Version)]
    [InlineData("email", "https://untrusted.example/")]
    [InlineData("email", "https://synthetic.communication.azure.com/untrusted-path/")]
    public async Task Provider_configuration_must_be_an_expected_azure_address(string provider, string address)
    {
        var options = Enabled();
        var credential = new FakeCredential();
        var handler = new CaptureHandler(_ => Response(HttpStatusCode.OK));
        using var transport = new AzureSignatureTransport(credential, handler);
        await Assert.ThrowsAsync<SignatureWorkflowException>(async () =>
        {
            if (provider == "blob") { options.BlobContainerUri = address; await new AzureSignatureBlobStore(transport, options).ReadAsync("source.pdf"); }
            else if (provider == "key") { options.PinKeyUri = address; await new AzureSigningPinKeyWrapper(transport, options).WrapAsync(new byte[32]); }
            else { options.EmailEndpoint = address; await new AzureSignatureEmailSender(transport, options).GetStatusAsync(Guid.NewGuid().ToString("D")); }
        });
        Assert.Equal(0, credential.Calls);
        Assert.Empty(handler.Requests);
    }

    private static CaptureHandler KeyHandler(string? returnedKey = null) => new(request =>
    {
        if (!request.Headers.ContainsKey("Authorization"))
        {
            var challenge = Response(HttpStatusCode.Unauthorized);
            challenge.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer authorization=\"https://login.microsoftonline.com/00000000-0000-0000-0000-000000000001\", resource=\"https://vault.azure.net\"");
            return challenge;
        }
        // The identity has wrap/unwrap permission, but cannot retrieve key material. SDK falls back
        // to the remote operation rather than doing public-key wrapping on this machine.
        if (request.Method == HttpMethod.Get)
            return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { error = new { code = "Forbidden", message = "Access denied" } }) };
        var unwrap = request.Uri.AbsolutePath.EndsWith("/unwrapKey", StringComparison.Ordinal);
        var key = returnedKey ?? request.Uri.GetLeftPart(UriPartial.Path)[..request.Uri.GetLeftPart(UriPartial.Path).LastIndexOf('/')];
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { kid = key, value = Base64Url(Enumerable.Repeat((byte)9, unwrap ? 32 : 256).ToArray()) }) };
    });

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static HttpResponseMessage Response(HttpStatusCode status, byte[]? bytes = null) => new(status) { Content = new ByteArrayContent(bytes ?? []) };
    private static HttpResponseMessage Operation(HttpStatusCode status, Guid id, string state) => new(status) { Content = JsonContent.Create(new { id = id.ToString("D"), status = state }) };
    private static HttpContent StreamContent(Stream stream, long? length = null)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentLength = length;
        return content;
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, byte[] Body, string? ContentType);

    private sealed class CaptureHandler(Func<CapturedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest(request.Method, request.RequestUri!, request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value), StringComparer.OrdinalIgnoreCase),
                request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken), request.Content?.Headers.ContentType?.MediaType);
            Requests.Add(captured);
            return respond(captured);
        }
    }

    private sealed class FakeCredential : TokenCredential
    {
        public int Calls { get; private set; }
        public List<string> Scopes { get; } = [];
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            Scopes.AddRange(requestContext.Scopes);
            return new("synthetic-access-token", DateTimeOffset.UtcNow.AddHours(1));
        }
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class CountingStream(long length) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var actual = (int)Math.Min(count, length - BytesRead);
            Array.Clear(buffer, offset, actual);
            BytesRead += actual;
            return actual;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var actual = (int)Math.Min(buffer.Length, length - BytesRead);
            buffer.Span[..actual].Clear();
            BytesRead += actual;
            return ValueTask.FromResult(actual);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
