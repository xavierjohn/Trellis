namespace Trellis.Asp.Idempotency;

using System;

/// <summary>
/// Parses an <c>Idempotency-Key</c> header using Trellis's compatibility grammar: a nonempty
/// RFC 7230 token (<c>1*tchar</c>) or a nonempty quoted printable-ASCII string (0x20-0x7E)
/// with only <c>\\</c> and <c>\"</c> escapes. This is not strict RFC 8941
/// <c>sf-string</c> or <c>sf-token</c> parsing.
/// </summary>
public static class IdempotencyKeyParser
{
    // Hard upper bound applied before any allocation or scanning. IdempotencyOptions.MaxKeyLength
    // remains the consumer-facing parsed-key limit; this guard rejects malformed raw headers first.
    private const int AbsoluteMaxRawLength = 4 * 1024;

    /// <summary>Attempts to parse a header value into a normalized idempotency key.</summary>
    /// <param name="raw">The raw header value as received from the request.</param>
    /// <param name="headerName">The configured header name, used in diagnostic messages.</param>
    /// <param name="key">The parsed key with quoting and escaping removed.</param>
    /// <param name="error">An English diagnostic message when parsing fails.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> otherwise.</returns>
    public static bool TryParse(string? raw, string headerName, out string key, out string? error)
    {
        key = string.Empty;
        error = null;

        if (string.IsNullOrEmpty(raw))
        {
            error = $"{headerName} header value is empty.";
            return false;
        }

        if (raw.Length > AbsoluteMaxRawLength)
        {
            error = $"{headerName} header value is invalid.";
            return false;
        }

        if (raw[0] == '"')
        {
            return TryParseQuoted(raw, headerName, out key, out error);
        }

        return TryParseToken(raw, headerName, out key, out error);
    }

    private static bool TryParseToken(string raw, string headerName, out string key, out string? error)
    {
        key = string.Empty;
        error = null;

        for (var i = 0; i < raw.Length; i++)
        {
            if (!IsTokenChar(raw[i]))
            {
                error = $"{headerName} contains invalid character at position {i}.";
                return false;
            }
        }

        key = raw;
        return true;
    }

    private static bool TryParseQuoted(string raw, string headerName, out string key, out string? error)
    {
        key = string.Empty;
        error = null;

        if (raw.Length < 2 || raw[^1] != '"')
        {
            error = $"{headerName} quoted value is not terminated.";
            return false;
        }

        var sb = new System.Text.StringBuilder(raw.Length - 2);
        for (var i = 1; i < raw.Length - 1; i++)
        {
            var c = raw[i];
            if (c == '\\')
            {
                if (i + 1 >= raw.Length - 1)
                {
                    error = $"{headerName} trailing escape is incomplete.";
                    return false;
                }

                var next = raw[i + 1];
                if (next is not '"' and not '\\')
                {
                    error = $"{headerName} invalid escape sequence at position {i}.";
                    return false;
                }

                sb.Append(next);
                i++;
                continue;
            }

            if (c is < (char)0x20 or >= (char)0x7F)
            {
                error = $"{headerName} contains non-printable ASCII at position {i}.";
                return false;
            }

            if (c == '"')
            {
                error = $"{headerName} contains unescaped quote at position {i}; embedded quotes must be escaped as \\\".";
                return false;
            }

            sb.Append(c);
        }

        if (sb.Length == 0)
        {
            error = $"{headerName} quoted value is empty.";
            return false;
        }

        key = sb.ToString();
        return true;
    }

    private static bool IsTokenChar(char c)
    {
        if (c is >= '0' and <= '9')
        {
            return true;
        }

        if (c is >= 'A' and <= 'Z')
        {
            return true;
        }

        if (c is >= 'a' and <= 'z')
        {
            return true;
        }

        return c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-'
            or '.' or '^' or '_' or '`' or '|' or '~';
    }
}