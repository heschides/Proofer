using System.Collections.Concurrent;
using Sati.Contracts.V1;
using Sati.Signatures;

namespace Sati.Api.Tests;

internal sealed class SignatureTestBlobStore : ISignatureBlobStore
{
    private readonly ConcurrentDictionary<string, byte[]> content = new();
    public Task WriteOnceAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (!content.TryAdd(path, bytes.ToArray()) && !content[path].SequenceEqual(bytes)) throw new InvalidOperationException("The immutable test blob already exists.");
        return Task.CompletedTask;
    }
    public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(content[path].ToArray());
}

internal sealed class SignatureTestKeyWrapper : ISigningPinKeyWrapper, ISignatureOutboxKeyWrapper
{
    public Task<WrappedDataKey> WrapAsync(byte[] key, CancellationToken cancellationToken = default) => SatiApiFactory.TestVault.WrapAsync(key, cancellationToken);
    public Task<byte[]> UnwrapAsync(byte[] key, string keyId, CancellationToken cancellationToken = default) => SatiApiFactory.TestVault.UnwrapAsync(key, keyId, cancellationToken);
}
