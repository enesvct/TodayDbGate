using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace TodayDbGate.Infrastructure.Security;

/// <summary>
/// A 256-bit secret kept in a file, encrypted with Data Protection, created on first use.
/// Used for the token hash key (losing it invalidates every token) and the masking key
/// (losing it changes every pseudonym).
/// </summary>
public sealed class ProtectedKeyFile
{
    private readonly Lazy<byte[]> _key;

    public ProtectedKeyFile(IDataProtectionProvider provider, string keyFilePath, string purpose)
    {
        var protector = provider.CreateProtector(purpose);
        _key = new Lazy<byte[]>(() => LoadOrCreate(protector, keyFilePath));
    }

    public byte[] GetKey() => _key.Value;

    private static byte[] LoadOrCreate(IDataProtector protector, string path)
    {
        if (File.Exists(path))
            return protector.Unprotect(Convert.FromBase64String(File.ReadAllText(path)));

        var key = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Convert.ToBase64String(protector.Protect(key)));
        return key;
    }
}
