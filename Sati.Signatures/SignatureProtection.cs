using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Signatures;

public static class SignatureSecrets
{
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static bool IsToken(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    // A non-secret page/session correlation value. It cannot authenticate without the HttpOnly cookie.
    public static string PageBinding(string sessionToken) => Hash("sati-signing-page-v1|" + Hash(sessionToken));
}

public sealed class SigningPinProtector(ISigningPinKeyWrapper wrapper)
{
    public const int Iterations = 600_000;
    public async Task SetAsync(SignatureRequest request, string pin, CancellationToken ct = default)
    {
        if (!SigningPinRules.IsValid(pin)) throw new SignatureWorkflowException("invalid_pin", "Choose an 8 to 12 digit signing code with at least three different digits. Avoid counting sequences and dates of birth.");
        var pepper = RandomNumberGenerator.GetBytes(32);
        try
        {
            var wrapped = await wrapper.WrapAsync(pepper, ct);
            request.PinPepperWrapped = wrapped.WrappedKey;
            request.PinKeyId = wrapped.KeyId;
            request.PinSalt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            request.PinIterations = Iterations;
            request.PinHash = Convert.ToHexString(Derive(request, pin, pepper));
        }
        finally { CryptographicOperations.ZeroMemory(pepper); }
    }

    public async Task<bool> VerifyAsync(SignatureRequest request, string pin, CancellationToken ct = default)
    {
        if (pin.Length is < 8 or > 12 || pin.Any(c => c < '0' || c > '9')) return false;
        if (request.PinIterations is < 100_000 or > 2_000_000) throw new CryptographicException("Invalid signature protection parameters.");
        var pepper = await wrapper.UnwrapAsync(request.PinPepperWrapped, request.PinKeyId, ct);
        try
        {
            if (pepper.Length != 32) throw new CryptographicException("Invalid signature protection key.");
            var actual = Derive(request, pin, pepper);
            try { return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(request.PinHash)); }
            finally { CryptographicOperations.ZeroMemory(actual); }
        }
        finally { CryptographicOperations.ZeroMemory(pepper); }
    }

    private static byte[] Derive(SignatureRequest request, string pin, byte[] pepper)
    {
        var input = Encoding.UTF8.GetBytes($"sati-signing-pin-v1|{request.AgencyId}|{request.ClientRequestId:D}|{pin}");
        var protectedPin = HMACSHA256.HashData(pepper, input);
        try { return Rfc2898DeriveBytes.Pbkdf2(protectedPin, Convert.FromHexString(request.PinSalt), request.PinIterations, HashAlgorithmName.SHA256, 32); }
        finally { CryptographicOperations.ZeroMemory(input); CryptographicOperations.ZeroMemory(protectedPin); }
    }
}

public sealed class SignatureOutboxProtector(ISignatureOutboxKeyWrapper wrapper)
{
    private static byte[] Binding(SignatureOutbox row) => Encoding.UTF8.GetBytes($"sati-signature-mail-v1|{row.AgencyId}|{row.Id}|{row.RequestId}|{row.Purpose}|{row.Generation}");
    public async Task ProtectAsync(SignatureOutbox row, SignatureEmail value, CancellationToken ct = default)
    {
        if (row.Id <= 0 || row.PayloadCiphertext is not null) throw new InvalidOperationException("An allocated empty outbox row is required.");
        var key = RandomNumberGenerator.GetBytes(32);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        try
        {
            var wrapped = await wrapper.WrapAsync(key, ct);
            row.PayloadNonce = RandomNumberGenerator.GetBytes(12);
            row.PayloadTag = new byte[16];
            row.PayloadCiphertext = new byte[bytes.Length];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(row.PayloadNonce, bytes, row.PayloadCiphertext, row.PayloadTag, Binding(row));
            row.PayloadWrappedKey = wrapped.WrappedKey;
            row.PayloadKeyId = wrapped.KeyId;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); CryptographicOperations.ZeroMemory(key); }
    }
    public async Task<SignatureEmail> UnprotectAsync(SignatureOutbox row, CancellationToken ct = default)
    {
        var key = await wrapper.UnwrapAsync(row.PayloadWrappedKey ?? throw new CryptographicException(), row.PayloadKeyId ?? throw new CryptographicException(), ct);
        var bytes = new byte[row.PayloadCiphertext?.Length ?? throw new CryptographicException()];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(row.PayloadNonce!, row.PayloadCiphertext!, row.PayloadTag!, bytes, Binding(row));
            return JsonSerializer.Deserialize<SignatureEmail>(bytes) ?? throw new CryptographicException();
        }
        finally { CryptographicOperations.ZeroMemory(bytes); CryptographicOperations.ZeroMemory(key); }
    }
}
