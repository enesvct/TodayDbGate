using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TodayDbGate.Application.Abstractions;

namespace TodayDbGate.Infrastructure.Security;

/// <summary>
/// Pseudonyms like <c>email_3f9a1c2b7d</c>: HMAC-SHA256 of the value with a server-side key.
/// The same value gives the same pseudonym in every column and query (so joins, GROUP BY and COUNT DISTINCT
/// still work on the client side), but it cannot be reversed or recomputed without the key.
/// </summary>
public sealed class HmacPseudonymizer : IPseudonymizer
{
    private readonly byte[] _key;

    public HmacPseudonymizer(byte[] key)
    {
        if (key.Length < 32) throw new ArgumentException("Masking key must be at least 32 bytes.", nameof(key));
        _key = key;
    }

    public string Pseudonymize(string columnName, object value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        // E-mail addresses are case-insensitive in practice; normalise so A@x.com and a@x.com match.
        var normalised = text.Contains('@') ? text.Trim().ToLowerInvariant() : text.Trim();
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(normalised));
        return $"{Prefix(columnName)}_{Convert.ToHexStringLower(hash)[..10]}";
    }

    private static string Prefix(string columnName)
    {
        var sb = new StringBuilder();
        foreach (var c in columnName.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
            if (sb.Length == 20) break;
        }
        var prefix = sb.ToString().Trim('_');
        return prefix.Length == 0 ? "value" : prefix;
    }
}
