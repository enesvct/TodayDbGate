using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace TodayDbGate.Application.Masking;

/// <summary>
/// Finds personal data inside values by its shape: e-mail, IBAN, payment card number, Turkish ID number (TCKN)
/// and phone number. Numeric identifiers are validated (mod-97, Luhn, TCKN checksum) to keep false positives low.
/// Every match is replaced with a partial mask, so the client still sees what kind of value was there.
/// </summary>
public static partial class PersonalDataDetector
{
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]){11,30}\b")]
    private static partial Regex Iban();

    [GeneratedRegex(@"(?<![\d*])(?:\d[ \-]?){12,18}\d(?![\d*])")]
    private static partial Regex Card();

    [GeneratedRegex(@"(?<![\d*])[1-9]\d{10}(?![\d*])")]
    private static partial Regex Tckn();

    // Turkish mobile numbers: 5xx xxx xx xx, optionally with +90 / 0 and spaces, dashes or brackets.
    [GeneratedRegex(@"(?<![\d*])(?:\+90[ \-]?|0)?\(?5\d{2}\)?[ \-]?\d{3}[ \-]?\d{2}[ \-]?\d{2}(?![\d*])")]
    private static partial Regex Phone();

    /// <summary>Masks personal data found in <paramref name="value"/>. Other values are returned unchanged.</summary>
    public static object? Mask(object? value, out bool changed)
    {
        changed = false;
        switch (value)
        {
            case string s:
                var masked = MaskText(s);
                changed = !ReferenceEquals(masked, s);
                return masked;

            // Turkish ID numbers are often stored as bigint or numeric(11).
            case long or decimal or ulong:
                var digits = Convert.ToString(value, CultureInfo.InvariantCulture)!;
                if (digits.Length == 11 && IsValidTckn(digits))
                {
                    changed = true;
                    return MaskDigits(digits);
                }
                return value;

            default:
                return value;
        }
    }

    /// <summary>Returns the same instance when nothing was found.</summary>
    public static string MaskText(string text)
    {
        // Cheap pre-check: every detector needs an '@' or a run of digits.
        if (text.Length < 6 || (!text.Contains('@') && CountDigits(text) < 10))
            return text;

        var result = text;
        result = Replace(result, Email(), m => MaskEmail(m.Value));
        result = Replace(result, Iban(), m => IsValidIban(m.Value) ? MaskIban(m.Value) : null);
        result = Replace(result, Card(), m => IsValidCard(m.Value) ? MaskCard(m.Value) : null);
        result = Replace(result, Tckn(), m => IsValidTckn(m.Value) ? MaskDigits(m.Value) : null);
        result = Replace(result, Phone(), m => MaskPhone(m.Value));
        return result;
    }

    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 0 ? "***" : email[0] + "***" + email[at..];
    }

    /// <summary>Keeps the last two digits: <c>*********46</c>.</summary>
    public static string MaskDigits(string digits) =>
        digits.Length <= 2 ? new string('*', digits.Length) : new string('*', digits.Length - 2) + digits[^2..];

    private static string MaskIban(string iban)
    {
        var compact = iban.Replace(" ", "");
        return $"{compact[..2]}** **** **** **** {compact[^4..]}";
    }

    private static string MaskCard(string card)
    {
        var digits = new string(card.Where(char.IsDigit).ToArray());
        return "**** **** **** " + digits[^4..];
    }

    private static string MaskPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return "*** *** ** " + digits[^2..];
    }

    private static string Replace(string input, Regex regex, Func<Match, string?> replacement)
    {
        var changed = false;
        var output = regex.Replace(input, m =>
        {
            var r = replacement(m);
            if (r is null) return m.Value;
            changed = true;
            return r;
        });
        return changed ? output : input;
    }

    private static int CountDigits(string s)
    {
        var n = 0;
        foreach (var c in s) if (char.IsAsciiDigit(c)) n++;
        return n;
    }

    /// <summary>T.C. Kimlik No: 11 digits, no leading zero, two check digits.</summary>
    public static bool IsValidTckn(string digits)
    {
        if (digits.Length != 11 || digits[0] == '0' || !digits.All(char.IsAsciiDigit)) return false;
        var d = digits.Select(c => c - '0').ToArray();
        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        var tenth = ((odd * 7 - even) % 10 + 10) % 10;
        var eleventh = (d.Take(10).Sum()) % 10;
        return d[9] == tenth && d[10] == eleventh;
    }

    /// <summary>Luhn checksum over 13-19 digits.</summary>
    public static bool IsValidCard(string value)
    {
        var digits = value.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length is < 13 or > 19) return false;
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var x = digits[digits.Length - 1 - i];
            if (i % 2 == 1) { x *= 2; if (x > 9) x -= 9; }
            sum += x;
        }
        return sum % 10 == 0;
    }

    /// <summary>ISO 13616 mod-97 check.</summary>
    public static bool IsValidIban(string value)
    {
        var iban = value.Replace(" ", "").ToUpperInvariant();
        if (iban.Length is < 15 or > 34) return false;
        var rearranged = iban[4..] + iban[..4];
        var numeric = string.Concat(rearranged.Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString(CultureInfo.InvariantCulture) : c.ToString()));
        return BigInteger.Parse(numeric, CultureInfo.InvariantCulture) % 97 == 1;
    }
}
