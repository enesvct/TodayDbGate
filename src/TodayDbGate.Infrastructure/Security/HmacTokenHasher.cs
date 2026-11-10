using System.Security.Cryptography;
using System.Text;
using TodayDbGate.Application.Abstractions;

namespace TodayDbGate.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 with a server-side secret key. Tokens are 256-bit random values,
/// so a fast keyed hash is enough; a stolen database alone is not enough to check guesses.
/// </summary>
public sealed class HmacTokenHasher : ITokenHasher
{
    private readonly byte[] _key;

    public HmacTokenHasher(byte[] key)
    {
        if (key.Length < 32) throw new ArgumentException("Token hash key must be at least 32 bytes.", nameof(key));
        _key = key;
    }

    public string Hash(string token) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(token)));
}
