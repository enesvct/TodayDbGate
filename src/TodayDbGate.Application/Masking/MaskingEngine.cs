using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Models;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Application.Masking;

public sealed record MaskingOutcome(QueryResult Result, IReadOnlyList<string> BlockedTables);

/// <summary>Applies a <see cref="MaskingPolicy"/> to a query result before it leaves the gateway.</summary>
public static class MaskingEngine
{
    public const string Redacted = "[REDACTED]";

    public static MaskingOutcome Apply(QueryResult result, MaskingPolicy policy, IPseudonymizer pseudonymizer)
    {
        if (policy.IsEmpty) return new MaskingOutcome(result, []);

        var columnCount = result.Columns.Count;
        var actions = new MaskAction?[columnCount];
        var blocked = new List<string>();
        for (var i = 0; i < columnCount; i++)
        {
            var origin = result.Origins is { } o && i < o.Count ? o[i] : null;
            actions[i] = policy.Resolve(origin);
            if (actions[i] == MaskAction.Block && !blocked.Contains(origin!.Table, StringComparer.OrdinalIgnoreCase))
                blocked.Add(origin.Table);
        }
        if (blocked.Count > 0) return new MaskingOutcome(result, blocked);

        // Pseudonym prefix from the real column, so "full_name AS customer" and "full_name" give identical
        // pseudonyms and the client can match them across queries.
        var pseudonymPrefix = new string[columnCount];
        for (var i = 0; i < columnCount; i++)
            pseudonymPrefix[i] = result.Origins is { } o && i < o.Count && o[i] is { } origin ? origin.Column : result.Columns[i];

        var masked = new bool[columnCount];
        var rows = new List<object?[]>(result.Rows.Count);
        foreach (var source in result.Rows)
        {
            var row = new object?[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                var value = source[i];
                if (value is null || i >= columnCount) { row[i] = value; continue; }

                switch (actions[i])
                {
                    case MaskAction.Allow:
                        row[i] = value;
                        break;
                    case MaskAction.Partial:
                        row[i] = MaskPartial(value);
                        masked[i] = true;
                        break;
                    case MaskAction.Pseudonym:
                        row[i] = pseudonymizer.Pseudonymize(pseudonymPrefix[i], value);
                        masked[i] = true;
                        break;
                    case MaskAction.Redact:
                        row[i] = Redacted;
                        masked[i] = true;
                        break;
                    default:
                        if (policy.DetectPersonalData)
                        {
                            row[i] = PersonalDataDetector.Mask(value, out var changed);
                            masked[i] |= changed;
                        }
                        else
                        {
                            row[i] = value;
                        }
                        break;
                }
            }
            rows.Add(row);
        }

        var maskedColumns = Enumerable.Range(0, columnCount).Where(i => masked[i]).Select(i => result.Columns[i]).ToList();
        return new MaskingOutcome(result with { Rows = rows, MaskedColumns = maskedColumns }, []);
    }

    /// <summary>
    /// Keeps a hint of the value: e-mail keeps first letter and domain, numbers keep the last two digits,
    /// text keeps the first letter of each word. Dates and other types are fully hidden.
    /// </summary>
    public static object MaskPartial(object value)
    {
        switch (value)
        {
            case string s when s.Contains('@'):
                return PersonalDataDetector.MaskEmail(s.Trim());
            case string s when s.Length > 0 && s.All(c => char.IsAsciiDigit(c) || c is ' ' or '-' or '+' or '(' or ')'):
                var digits = new string(s.Where(char.IsAsciiDigit).ToArray());
                return digits.Length == 0 ? "***" : PersonalDataDetector.MaskDigits(digits);
            case string s:
                var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return words.Length == 0 ? "***" : string.Join(' ', words.Select(w => w[0] + "***"));
            case byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                return PersonalDataDetector.MaskDigits(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!.TrimStart('-'));
            default:
                return "***";
        }
    }
}
