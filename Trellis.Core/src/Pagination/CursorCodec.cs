namespace Trellis;

using System.Globalization;
using System.Text;

/// <summary>A typed continuation codec. Parsing client input returns failures; encoding invalid server state throws.</summary>
/// <typeparam name="TState">The complete continuation state, including query context when required.</typeparam>
public interface ICursorCodec<TState> where TState : notnull
{
    /// <summary>Encodes a valid boundary. Implementations must preserve its seek semantics.</summary>
    Cursor Encode(TState state);

    /// <summary>Decodes and validates client state, returning field-specific failures for invalid tokens.</summary>
    Result<TState> TryDecode(Cursor? cursor, string? fieldName = null);
}

/// <summary>
/// AOT-friendly, versioned continuation codecs. Tokens are opaque, not signed.
/// Existing unversioned tokens are deliberately rejected. Authorization must still filter every query.
/// </summary>
public static class CursorCodec
{
    /// <summary>Maximum encoded token size for built-in codecs, enforced on both paths.</summary>
    public const int MaxEncodedTokenLength = 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Creates an invariant scalar codec. Custom types must implement invariant formatting and parsing.</summary>
    public static ICursorCodec<T> Scalar<T>() where T : notnull, IParsable<T>
    {
        if (typeof(T) != typeof(string) && !typeof(IFormattable).IsAssignableFrom(typeof(T)))
            throw new NotSupportedException($"Cursor key type '{typeof(T).FullName}' must implement IFormattable or be string.");
        return new TextCodec<T>("s", FormatInvariant, ParseScalar<T>);
    }

    /// <summary>Creates a two-component codec using built-in scalar codecs.</summary>
    public static ICursorCodec<(TPrimary Primary, TSecondary Secondary)> Composite<TPrimary, TSecondary>()
        where TPrimary : notnull, IParsable<TPrimary>
        where TSecondary : notnull, IParsable<TSecondary> =>
        Composite(Scalar<TPrimary>(), Scalar<TSecondary>());

    /// <summary>Composes arbitrary component codecs using unambiguous length-prefixed framing.</summary>
    public static ICursorCodec<(TPrimary Primary, TSecondary Secondary)> Composite<TPrimary, TSecondary>(
        ICursorCodec<TPrimary> primary, ICursorCodec<TSecondary> secondary)
        where TPrimary : notnull
        where TSecondary : notnull
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(secondary);
        return new TextCodec<(TPrimary Primary, TSecondary Secondary)>(
            "c",
            state =>
            {
                var first = primary.Encode(state.Primary).Token;
                var second = secondary.Encode(state.Secondary).Token;
                return string.Concat(first.Length.ToString(CultureInfo.InvariantCulture), ":", first, second);
            },
            (payload, field) =>
            {
                var separator = payload.IndexOf(':');
                if (separator <= 0
                    || !int.TryParse(payload.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                    || length <= 0 || length >= payload.Length - separator - 1)
                    return Fail<(TPrimary, TSecondary)>(field, "Cursor has invalid composite framing.");
                var first = new Cursor(payload.Substring(separator + 1, length));
                var second = new Cursor(payload[(separator + 1 + length)..]);
                var parsed = EnsureDecodedState(primary.TryDecode(first, field));
                if (!parsed.TryGetValue(out var primaryValue, out var error))
                    return Result.Fail<(TPrimary, TSecondary)>(error);
                return EnsureDecodedState(secondary.TryDecode(second, field)).Map(value => (primaryValue, value));
            });
    }

    /// <summary>
    /// Creates a codec with an explicit schema identifier and round-trip formatter/parser.
    /// The parser must validate domain bounds and context and return InvalidInput for bad client state.
    /// It also runs during encoding to reject server state that cannot round-trip.
    /// </summary>
    public static ICursorCodec<TState> Create<TState>(
        string schema,
        Func<TState, string> format,
        Func<string, string?, Result<TState>> parse)
        where TState : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        if (schema.Length > 64 || schema.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Schema must contain at most 64 ASCII letters, digits or hyphens.", nameof(schema));
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(parse);
        return new TextCodec<TState>("x-" + schema, format, parse);
    }

    /// <summary>
    /// Maps a wire codec to named validated state without hand-writing serialization.
    /// Validation runs on inbound state and on the round-trip of every encoded boundary.
    /// </summary>
    public static ICursorCodec<TState> Map<TWire, TState>(
        ICursorCodec<TWire> wireCodec,
        Func<TState, TWire> toWire,
        Func<TWire, string?, Result<TState>> fromWire)
        where TWire : notnull
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(wireCodec);
        ArgumentNullException.ThrowIfNull(toWire);
        ArgumentNullException.ThrowIfNull(fromWire);
        return new MappedCodec<TWire, TState>(wireCodec, toWire, fromWire);
    }

    /// <summary>Encodes a scalar using the built-in codec.</summary>
    public static Cursor Encode<T>(T id) where T : notnull, IParsable<T> => Scalar<T>().Encode(id);

    /// <summary>Decodes a scalar using the built-in codec.</summary>
    public static Result<T> TryDecode<T>(Cursor? cursor, string? fieldName = null)
        where T : notnull, IParsable<T> => Scalar<T>().TryDecode(cursor, fieldName);

    /// <summary>
    /// Decodes optional continuation state. Only an absent token means the first page.
    /// A codec returning successful null state violates its contract and throws InvalidOperationException.
    /// </summary>
    public static Result<Maybe<TState>> TryDecodeOptional<TState>(
        Cursor? cursor, ICursorCodec<TState> codec, string? fieldName = null)
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (cursor is null)
            return Result.Ok(Maybe<TState>.None);
        return EnsureDecodedState(codec.TryDecode(cursor, fieldName)).Map(Maybe.From);
    }

    /// <summary>Encodes arbitrary primary and secondary scalar keys.</summary>
    public static Cursor Encode<TPrimary, TSecondary>(TPrimary primary, TSecondary secondary)
        where TPrimary : notnull, IParsable<TPrimary>
        where TSecondary : notnull, IParsable<TSecondary> =>
        Composite<TPrimary, TSecondary>().Encode((primary, secondary));

    /// <summary>Decodes arbitrary primary and secondary scalar keys.</summary>
    public static Result<(TPrimary Primary, TSecondary Secondary)> TryDecodeComposite<TPrimary, TSecondary>(
        Cursor? cursor, string? fieldName = null)
        where TPrimary : notnull, IParsable<TPrimary>
        where TSecondary : notnull, IParsable<TSecondary> =>
        Composite<TPrimary, TSecondary>().TryDecode(cursor, fieldName);

    /// <summary>Convenience decoder for timestamp and ID state, using the versioned composite codec.</summary>
    public static Result<(DateTimeOffset CreatedAt, TKey Id)> TryDecodeComposite<TKey>(
        Cursor? cursor, string? fieldName = null)
        where TKey : notnull, IParsable<TKey> =>
        TryDecodeComposite<DateTimeOffset, TKey>(cursor, fieldName);

    private static string FormatInvariant<T>(T state) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsFinite(state))
            throw new ArgumentException("Cursor numbers must be finite.", nameof(state));
        return state switch
        {
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            Half number => number.ToString("R", CultureInfo.InvariantCulture),
            string text => text,
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            _ => throw new NotSupportedException($"Cursor key type '{typeof(T).FullName}' requires an explicit codec.")
        };
    }

    private static Result<T> ParseScalar<T>(string payload, string? field) where T : notnull, IParsable<T>
    {
        if (payload.Length == 0)
            return Fail<T>(field, "Cursor scalar must not be empty.");
        // DateTime.TryParse without RoundtripKind normalizes UTC to local time.
        if (typeof(T) == typeof(DateTime))
            return DateTime.TryParseExact(payload, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
                ? Result.Ok((T)(object)date)
                : Fail<T>(field, "Cursor timestamp is invalid.");
        if (typeof(T) == typeof(DateTimeOffset))
            return DateTimeOffset.TryParseExact(payload, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset)
                ? Result.Ok((T)(object)offset)
                : Fail<T>(field, "Cursor timestamp is invalid.");
        if (!T.TryParse(payload, CultureInfo.InvariantCulture, out var value) || value is null || !IsFinite(value))
            return Fail<T>(field, $"Cursor scalar could not be parsed as {typeof(T).Name}.");
        return Result.Ok(value);
    }

    private static bool IsFinite<T>(T value) => value switch
    {
        double number => double.IsFinite(number),
        float number => float.IsFinite(number),
        Half number => Half.IsFinite(number),
        _ => true
    };

    private static Cursor EncodePayload(string payload)
    {
        if (StrictUtf8.GetByteCount(payload) > MaxEncodedTokenLength / 4 * 3)
            throw new ArgumentException("Cursor state exceeds the encoded token limit.", nameof(payload));
        var token = Convert.ToBase64String(StrictUtf8.GetBytes(payload)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        if (token.Length > MaxEncodedTokenLength)
            throw new ArgumentException("Cursor state exceeds the encoded token limit.", nameof(payload));
        return new Cursor(token);
    }

    private static bool TryPayload(Cursor? cursor, out string payload)
    {
        payload = string.Empty;
        if (cursor is null || cursor.Token.Length > MaxEncodedTokenLength)
            return false;
        var token = cursor.Token;
        if (token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_') || token.Length % 4 == 1)
            return false;
        var standard = token.Replace('-', '+').Replace('_', '/');
        standard = standard.PadRight((standard.Length + 3) / 4 * 4, '=');
        try
        {
            payload = StrictUtf8.GetString(Convert.FromBase64String(standard));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static Result<T> Fail<T>(string? field, string detail) =>
        Result.Fail<T>(Error.InvalidInput.ForField(field ?? "cursor", ValidationCodes.CursorMalformed, detail));

    private static void EnsureRoundTrip<T>(T state, Result<T> parsed)
    {
        parsed = EnsureDecodedState(parsed);
        if (!parsed.TryGetValue(out var decoded) || !EqualityComparer<T>.Default.Equals(state, decoded))
            throw new ArgumentException("Server continuation state failed codec validation or did not round-trip.", nameof(state));
    }

    private static Result<T> EnsureDecodedState<T>(Result<T> parsed)
    {
        if (parsed.TryGetValue(out var state) && state is null)
            throw new InvalidOperationException("Cursor codec returned successful null continuation state.");
        return parsed;
    }

    private sealed class TextCodec<T>(
        string schema,
        Func<T, string> format,
        Func<string, string?, Result<T>> parse) : ICursorCodec<T> where T : notnull
    {
        private readonly string _prefix = "1:" + schema + ":";

        public Cursor Encode(T state)
        {
            ArgumentNullException.ThrowIfNull(state);
            var payload = format(state) ?? throw new InvalidOperationException("Cursor formatter returned null.");
            var cursor = EncodePayload(_prefix + payload);
            EnsureRoundTrip(state, parse(payload, null));
            return cursor;
        }

        public Result<T> TryDecode(Cursor? cursor, string? fieldName = null) =>
            TryPayload(cursor, out var payload) && payload.StartsWith(_prefix, StringComparison.Ordinal)
                ? EnsureDecodedState(parse(payload[_prefix.Length..], fieldName))
                : Fail<T>(fieldName, "Cursor is malformed or has an unsupported format version.");
    }

    private sealed class MappedCodec<TWire, TState>(
        ICursorCodec<TWire> wireCodec,
        Func<TState, TWire> toWire,
        Func<TWire, string?, Result<TState>> fromWire) : ICursorCodec<TState>
        where TWire : notnull
        where TState : notnull
    {
        public Cursor Encode(TState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            var cursor = wireCodec.Encode(toWire(state));
            EnsureRoundTrip(state, TryDecode(cursor));
            return cursor;
        }

        public Result<TState> TryDecode(Cursor? cursor, string? fieldName = null) =>
            EnsureDecodedState(wireCodec.TryDecode(cursor, fieldName))
                .Bind(value => EnsureDecodedState(fromWire(value, fieldName)));
    }
}
