using Sati.Contracts.V1;

namespace Sati.Signatures;

public sealed class SignatureOptions
{
    public bool Enabled { get; set; }
    public bool WorkersEnabled { get; set; }
    public string ExpectedEnvironment { get; set; } = "";
    public string ExpectedDatabaseName { get; set; } = "";
    public string BlobContainerUri { get; set; } = "";
    public string PinKeyUri { get; set; } = "";
    public string OutboxKeyUri { get; set; } = "";
    public string PortalBaseUri { get; set; } = "";
    public bool EmailEnabled { get; set; }
    public string EmailEndpoint { get; set; } = "";
    public string EmailSender { get; set; } = "";
    public string[] AllowedTestRecipients { get; set; } = [];
}

public sealed class SignatureFeature(SignatureOptions options)
{
    public bool Enabled => options.Enabled &&
        ((options.ExpectedEnvironment == "Demo" && options.ExpectedDatabaseName == "SatiDemo") ||
         (options.ExpectedEnvironment == "Testing" && options.ExpectedDatabaseName == "SatiApiTests"));
    public void RequireEnabled()
    {
        if (!Enabled) throw new SignatureWorkflowException("signature_unavailable", "Electronic signing is not enabled for this environment.", 404);
    }
}

public class SignatureWorkflowException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public interface ISignatureBlobStore
{
    Task WriteOnceAsync(string path, byte[] content, CancellationToken cancellationToken = default);
    Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default);
}

// Separate key roles: the portal can verify a PIN but cannot decrypt an invitation.
public interface ISigningPinKeyWrapper : IKeyWrapper { }
public interface ISignatureOutboxKeyWrapper : IKeyWrapper { }

public sealed record SignatureEmail(string Recipient, string Link, string Purpose);
public sealed record SignatureEmailResult(string State, string? OperationId = null, int? RetryAfterSeconds = null);
public interface ISignatureEmailSender
{
    Task<SignatureEmailResult> SendAsync(Guid operationId, SignatureEmail email, CancellationToken cancellationToken = default);
    Task<SignatureEmailResult> GetStatusAsync(string operationId, CancellationToken cancellationToken = default);
}

public sealed class DisabledSignatureEmailSender : ISignatureEmailSender
{
    public Task<SignatureEmailResult> SendAsync(Guid operationId, SignatureEmail email, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SignatureEmailResult("Suppressed"));
    public Task<SignatureEmailResult> GetStatusAsync(string operationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SignatureEmailResult("Suppressed"));
}

public sealed class UnconfiguredSignatureBlobStore : ISignatureBlobStore
{
    public Task WriteOnceAsync(string path, byte[] content, CancellationToken cancellationToken = default) =>
        throw new SignatureWorkflowException("signature_storage_unavailable", "Signature document storage has not been configured.", 503);
    public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default) =>
        throw new SignatureWorkflowException("signature_storage_unavailable", "Signature document storage has not been configured.", 503);
}

public sealed class UnconfiguredSigningKeyWrapper : ISigningPinKeyWrapper, ISignatureOutboxKeyWrapper
{
    public Task<WrappedDataKey> WrapAsync(byte[] key, CancellationToken cancellationToken = default) =>
        throw new SignatureWorkflowException("signature_key_unavailable", "Signature protection keys have not been configured.", 503);
    public Task<byte[]> UnwrapAsync(byte[] key, string keyId, CancellationToken cancellationToken = default) =>
        throw new SignatureWorkflowException("signature_key_unavailable", "Signature protection keys have not been configured.", 503);
}
