namespace Trellis.Asp.Idempotency;

using System;
using System.Text;
using System.Text.Unicode;

/// <summary>
/// Parses an <c>Idempotency-Key</c> header as an RFC 9651 Item whose value is a String.
/// Strings contain zero or more printable-ASCII characters (0x20-0x7E), with only
/// <c>\\</c> and <c>\"</c> escapes. Surrounding spaces and valid Item parameters are accepted;
/// parameters, including Dates and Display Strings, do not contribute to the parsed key.
/// </summary>
public static class IdempotencyKeyParser
{
    // Hard upper bound applied before any allocation or scanning. IdempotencyOptions.MaxKeyLength
    // remains the consumer-facing parsed-key limit; this guard rejects malformed raw headers first.
    private const int AbsoluteMaxRawLength = 4 * 1024;

    /// <summary>Attempts to parse a header value into a normalized idempotency key.</summary>
    /// <param name="raw">The raw header value as received from the request.</param>
    /// <param name="headerName">The configured header name, used in diagnostic messages.</param>
    /// <param name="key">The String value with quoting and escaping removed; may be empty.</param>
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

        var input = raw.AsSpan();
        var position = 0;
        SkipSpaces(input, ref position);

        if (!TryReadString(input, ref position, out var content)
            || !TryReadParameters(input, ref position))
        {
            error = $"{headerName} must be an RFC 9651 String-valued Item.";
            return false;
        }

        SkipSpaces(input, ref position);
        if (position != input.Length)
        {
            error = $"{headerName} contains invalid trailing content at position {position}.";
            return false;
        }

        key = UnescapeString(input[content]);
        return true;
    }

    private static bool TryReadString(ReadOnlySpan<char> input, ref int position, out Range content)
    {
        content = default;
        if (position >= input.Length || input[position] != '"')
            return false;

        var start = ++position;
        while (position < input.Length)
        {
            var character = input[position++];
            if (character == '"')
            {
                content = start..(position - 1);
                return true;
            }

            if (character == '\\')
            {
                if (position >= input.Length || input[position] is not '"' and not '\\')
                    return false;

                position++;
            }
            else if (character is < (char)0x20 or > (char)0x7E)
                return false;
        }

        return false;
    }

    private static string UnescapeString(ReadOnlySpan<char> content)
    {
        if (!content.Contains('\\'))
            return content.ToString();

        var builder = new StringBuilder(content.Length);
        for (var position = 0; position < content.Length; position++)
        {
            if (content[position] == '\\')
                position++;

            builder.Append(content[position]);
        }

        return builder.ToString();
    }

    private static bool TryReadParameters(ReadOnlySpan<char> input, ref int position)
    {
        while (position < input.Length && input[position] == ';')
        {
            position++;
            SkipSpaces(input, ref position);
            if (position >= input.Length || !IsParameterKeyStart(input[position]))
                return false;

            position++;
            while (position < input.Length && IsParameterKeyChar(input[position]))
                position++;

            if (position < input.Length && input[position] == '=')
            {
                position++;
                if (!TryReadBareItem(input, ref position))
                    return false;
            }
        }

        return true;
    }

    private static bool TryReadBareItem(ReadOnlySpan<char> input, ref int position)
    {
        if (position >= input.Length)
            return false;

        var character = input[position];
        if (character == '"')
            return TryReadString(input, ref position, out _);

        if (character == '-' || char.IsAsciiDigit(character))
            return TryReadNumber(input, ref position);

        if (character == '@')
        {
            position++;
            return TryReadNumber(input, ref position, allowDecimal: false);
        }

        if (character == '%')
            return TryReadDisplayString(input, ref position);

        if (character == ':')
            return TryReadByteSequence(input, ref position);

        if (character == '?')
        {
            if (position + 1 >= input.Length || input[position + 1] is not '0' and not '1')
                return false;

            position += 2;
            return true;
        }

        if (!char.IsAsciiLetter(character) && character != '*')
            return false;

        position++;
        while (position < input.Length && IsTokenChar(input[position]))
            position++;

        return true;
    }

    private static bool TryReadNumber(ReadOnlySpan<char> input, ref int position, bool allowDecimal = true)
    {
        if (position >= input.Length)
            return false;

        if (input[position] == '-')
            position++;

        var integerStart = position;
        while (position < input.Length && char.IsAsciiDigit(input[position]))
            position++;

        var integerDigits = position - integerStart;
        if (integerDigits == 0)
            return false;

        if (position >= input.Length || input[position] != '.')
            return integerDigits <= 15;

        if (!allowDecimal || integerDigits > 12)
            return false;

        var fractionStart = ++position;
        while (position < input.Length && char.IsAsciiDigit(input[position]))
            position++;

        return position - fractionStart is >= 1 and <= 3;
    }

    private static bool TryReadDisplayString(ReadOnlySpan<char> input, ref int position)
    {
        if (position + 1 >= input.Length || input[position + 1] != '"')
            return false;

        position += 2;
        Span<byte> decoded = stackalloc byte[input.Length - position];
        var written = 0;
        while (position < input.Length)
        {
            var character = input[position++];
            if (character is < (char)0x20 or > (char)0x7E)
                return false;

            if (character == '"')
                return Utf8.IsValid(decoded[..written]);

            if (character == '%')
            {
                if (position + 1 >= input.Length)
                    return false;

                var high = LowercaseHexValue(input[position++]);
                var low = LowercaseHexValue(input[position++]);
                if (high < 0 || low < 0)
                    return false;

                decoded[written++] = (byte)((high * 16) + low);
            }
            else
                decoded[written++] = (byte)character;
        }

        return false;
    }

    private static bool TryReadByteSequence(ReadOnlySpan<char> input, ref int position)
    {
        var start = ++position;
        while (position < input.Length && input[position] != ':')
        {
            var character = input[position];
            if (!char.IsAsciiLetterOrDigit(character) && character is not '+' and not '/' and not '=')
                return false;

            position++;
        }

        if (position >= input.Length)
            return false;

        var encoded = input[start..position];
        position++;
        var paddedLength = (encoded.Length + 3) / 4 * 4;
        Span<char> padded = stackalloc char[paddedLength];
        encoded.CopyTo(padded);
        padded[encoded.Length..].Fill('=');
        Span<byte> decoded = stackalloc byte[paddedLength / 4 * 3];
        return Convert.TryFromBase64Chars(padded, decoded, out _);
    }

    private static void SkipSpaces(ReadOnlySpan<char> input, ref int position)
    {
        while (position < input.Length && input[position] == ' ')
            position++;
    }

    private static bool IsParameterKeyStart(char character) =>
        character is (>= 'a' and <= 'z') or '*';

    private static int LowercaseHexValue(char character) =>
        character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'a' and <= 'f' => character - 'a' + 10,
            _ => -1
        };

    private static bool IsParameterKeyChar(char character) =>
        IsParameterKeyStart(character) || char.IsAsciiDigit(character)
        || character is '_' or '-' or '.';

    private static bool IsTokenChar(char character) =>
        char.IsAsciiLetterOrDigit(character)
        || character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-'
            or '.' or '^' or '_' or '`' or '|' or '~' or ':' or '/';
}