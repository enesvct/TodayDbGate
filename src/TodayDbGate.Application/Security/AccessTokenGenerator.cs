using System.Security.Cryptography;

namespace TodayDbGate.Application.Security;

public static class AccessTokenGenerator
{
    public const string Prefix = "tdg_";
    public const int DisplayPrefixLength = 12;

    /// <summary>Creates a new token: "tdg_" followed by 32 random bytes in base64url.</summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var body = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Prefix + body;
    }

    public static string GetDisplayPrefix(string token) =>
        token.Length <= DisplayPrefixLength ? token : token[..DisplayPrefixLength];
}
