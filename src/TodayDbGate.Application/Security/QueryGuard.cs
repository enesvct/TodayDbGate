using System.Text;
using TodayDbGate.Application.Exceptions;
using TodayDbGate.Domain.Enums;

namespace TodayDbGate.Application.Security;

/// <summary>
/// A cheap pre-check before a query reaches the database: one statement per call, and for read-only grants
/// the statement must start with a reading keyword.
///
/// This is defence in depth only. SQL text checks can be fooled, so the real protection is
///   (1) a database user with read-only rights, and
///   (2) the read-only, always-rolled-back transaction the adapters use.
/// When the guard is unsure it rejects rather than allows.
/// </summary>
public static class QueryGuard
{
    private static readonly HashSet<string> ReadOnlyKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "WITH", "SHOW", "EXPLAIN", "DESCRIBE", "DESC", "VALUES", "TABLE"
    };

    /// <exception cref="QueryRejectedException">The query is not allowed.</exception>
    public static void EnsureAllowed(string sql, AccessLevel accessLevel, DatabaseType databaseType)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new QueryRejectedException("The query is empty.");

        var code = StripCommentsAndLiterals(sql, databaseType).Trim().TrimEnd(';').TrimEnd();
        if (code.Length == 0)
            throw new QueryRejectedException("The query contains no SQL statement.");

        if (code.Contains(';'))
            throw new QueryRejectedException("Only a single SQL statement is allowed per call.");

        if (accessLevel == AccessLevel.ReadOnly)
        {
            var keyword = FirstKeyword(code);
            if (!ReadOnlyKeywords.Contains(keyword))
                throw new QueryRejectedException(
                    $"This token has read-only access to this connection; '{keyword}' statements are not allowed.");
        }
    }

    private static string FirstKeyword(string code)
    {
        var i = 0;
        while (i < code.Length && (char.IsWhiteSpace(code[i]) || code[i] == '(')) i++;
        var start = i;
        while (i < code.Length && char.IsLetter(code[i])) i++;
        return code[start..i];
    }

    /// <summary>
    /// Replaces comments, string literals and quoted identifiers with spaces so that semicolons and keywords
    /// inside them are ignored. Follows each database's quoting rules; anything unusual is left visible.
    /// </summary>
    internal static string StripCommentsAndLiterals(string sql, DatabaseType databaseType)
    {
        var isMySql = databaseType == DatabaseType.MySql;
        var isPostgres = databaseType == DatabaseType.PostgreSql;
        var sb = new StringBuilder(sql.Length);
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            // -- line comment. MySQL needs whitespace after "--" ("1--1" is arithmetic there).
            if (c == '-' && next == '-' && (!isMySql || i + 2 >= sql.Length || char.IsWhiteSpace(sql[i + 2])))
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                sb.Append(' ');
                continue;
            }

            // /* block comment */. MySQL runs the body of /*! ... */, so keep those visible.
            if (c == '/' && next == '*' && !(isMySql && i + 2 < sql.Length && sql[i + 2] == '!'))
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 2;
                sb.Append(' ');
                continue;
            }

            // PostgreSQL dollar quoting: $$...$$ or $tag$...$tag$
            if (isPostgres && c == '$' && TryReadDollarTag(sql, i, out var tag))
            {
                var end = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + tag.Length;
                sb.Append(' ');
                continue;
            }

            if (c is '\'' or '"' || (isMySql && c == '`'))
            {
                i = SkipQuoted(sql, i, c, closing: c, backslashEscapes: isMySql && c != '`');
                sb.Append(' ');
                continue;
            }

            if (c == '[' && databaseType == DatabaseType.SqlServer)
            {
                i = SkipQuoted(sql, i, c, closing: ']', backslashEscapes: false);
                sb.Append(' ');
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>Returns the index just after the closing quote. A doubled closing quote is an escaped quote.</summary>
    private static int SkipQuoted(string sql, int start, char opening, char closing, bool backslashEscapes)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (backslashEscapes && c == '\\')
            {
                i += 2;
                continue;
            }
            if (c == closing)
            {
                if (i + 1 < sql.Length && sql[i + 1] == closing)
                {
                    i += 2;
                    continue;
                }
                return i + 1;
            }
            i++;
        }
        return sql.Length;
    }

    private static bool TryReadDollarTag(string sql, int start, out string tag)
    {
        // $ followed by an optional identifier and another $. "$1" (a parameter) is not a tag.
        var i = start + 1;
        if (i < sql.Length && (char.IsLetter(sql[i]) || sql[i] == '_'))
        {
            i++;
            while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
        }
        if (i < sql.Length && sql[i] == '$')
        {
            tag = sql[start..(i + 1)];
            return true;
        }
        tag = "";
        return false;
    }
}
