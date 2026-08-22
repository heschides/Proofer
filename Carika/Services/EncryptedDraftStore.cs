using System.Security.Cryptography;
using System.Text;

namespace Carika.Services;

internal sealed class EncryptedDraftStore
{
    private static readonly byte[] Entropy = "Carika.PhiDraft.v1"u8.ToArray();
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sati", "Carika", "Drafts");

    public async Task SaveAsync(int userId, int personId, string narrative, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        var clear = Encoding.UTF8.GetBytes(narrative);
        try
        {
            var protectedBytes = ProtectedData.Protect(clear, Binding(userId, personId), DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(PathFor(userId, personId), protectedBytes, ct);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public async Task<string> LoadAsync(int userId, int personId, CancellationToken ct)
    {
        var path = PathFor(userId, personId);
        if (!File.Exists(path)) return string.Empty;
        var protectedBytes = await File.ReadAllBytesAsync(path, ct);
        var clear = ProtectedData.Unprotect(protectedBytes, Binding(userId, personId), DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public void Delete(int userId, int personId)
    {
        var path = PathFor(userId, personId);
        if (File.Exists(path)) File.Delete(path);
    }

    private string PathFor(int userId, int personId) => Path.Combine(_directory, $"u{userId}-p{personId}.draft");
    private static byte[] Binding(int userId, int personId) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"Carika|user:{userId}|person:{personId}|draft"));
}
