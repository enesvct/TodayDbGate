using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace TodayDbGate.Infrastructure.Security;

/// <summary>
/// Loads the HMAC key for token hashing from disk, creating it on first run.
/// The key file is itself encrypted with Data Protection.
/// Losing this file invalidates every issued token.
/// </summary>
public sealed class TokenHashKeyProvider
{
    private readonly Lazy<byte[]> _key;

    public TokenHashKeyProvider(IDataProtectionProvider provider, string keyFilePath)
    {
        var protector = provider.CreateProtector("TodayDbGate.TokenHashKey.v1");
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
