using System.Security.Cryptography;
using System.Text;

namespace Sati.Api.Security;

internal sealed class PasswordVerifier
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    // A fixed, real-shaped credential used only to spend the same work on a
    // sign-in attempt for a username that does not exist. It must never match
    // any password: the derived key is compared against a constant that was not
    // produced from any password, so VerifyMissingUser always returns false.
    private static readonly string DecoySalt = Convert.ToBase64String(new byte[SaltSize]);
    private static readonly string DecoyHash = Convert.ToBase64String(new byte[KeySize]);

    public (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public bool Verify(string password, string storedHash, string storedSalt)
    {
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(storedHash) ||
            string.IsNullOrWhiteSpace(storedSalt))
            return false;

        try
        {
            var salt = Convert.FromBase64String(storedSalt);
            var expected = Convert.FromBase64String(storedHash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Performs the same key derivation as <see cref="Verify"/> and always
    /// returns false.
    ///
    /// Sign-in must not reveal whether a username exists. Returning early when
    /// no user row is found would skip 100,000 PBKDF2 iterations, so a missing
    /// account would answer measurably faster than a wrong password and an
    /// attacker could enumerate valid clinician accounts by timing alone. Call
    /// this on the not-found path so both outcomes cost the same.
    /// </summary>
    public bool VerifyMissingUser(string? password) =>
        Verify(password ?? string.Empty, DecoyHash, DecoySalt);
}
