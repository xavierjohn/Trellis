namespace Trellis;

using System;
using System.Globalization;
using System.Text;

/// <summary>
/// Helpers that encode and decode opaque <see cref="Cursor"/> tokens for seek-style
/// pagination. Two overload pairs are provided: a single-key form for pure <c>Id</c>-keyed
/// pagination (the simplest, matches single-column indexes), and a composite
/// <c>(CreatedAt, Id)</c> form for stable time-ordered seeks across non-unique
/// primary sort keys.
/// </summary>
/// <remarks>
/// <para>
/// <b>Wire format:</b> The token is URL-safe Base64 (RFC 4648 §5) of a UTF-8 string.
/// For single-key cursors, the payload is the key's invariant-culture string form. For
/// composite cursors, the payload is <c>"{createdAt:O}|{id}"</c> where the date is
/// formatted with the round-trip <c>"O"</c> specifier in invariant culture. Decoding
/// splits at the FIRST <c>|</c> only, so an Id that happens to contain a pipe character
/// is still unambiguous.
/// </para>
/// <para>
/// <b>AOT-friendly:</b> No JSON, no reflection. Encoding uses <see cref="Convert.ToBase64String(byte[])"/>
/// followed by URL-safe substitution; decoding inverts the substitution and parses with
/// <see cref="IParsable{TSelf}.TryParse(string, IFormatProvider, out TSelf)"/>.
/// </para>
/// <para>
/// <b>Opacity, not anti-tamper:</b> Cursors are server-opaque to discourage clients from
/// reverse-engineering the sort key, but the encoding is not signed. Services that need
/// to defend against tampering must wrap or replace this codec with one that signs the
/// payload; authorization filtering must always apply to the underlying query.
/// </para>
/// <para>
/// <b>Supported TKey:</b>
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Encode</b> accepts any <c>TKey</c> that implements
///     <see cref="IFormattable"/> (so it can be formatted in
///     <see cref="System.Globalization.CultureInfo.InvariantCulture"/>) or that is a
///     <see cref="string"/>.
///   </description></item>
///   <item><description>
///     <b>TryDecode</b> additionally requires <c>TKey</c> to
///     implement <see cref="IParsable{TSelf}"/>.
///   </description></item>
/// </list>
/// <para>
/// .NET primitives such as <see cref="Guid"/>, <see cref="long"/>, <see cref="int"/>,
/// and <see cref="string"/> satisfy both. Trellis source-generated value-object wrappers
/// (e.g. <c>partial class CustomerId : RequiredGuid&lt;CustomerId&gt;</c>) inherit
/// <see cref="IFormattable"/> from <c>ScalarValueObject</c> and gain <see cref="IParsable{TSelf}"/>
/// from the source generator, so they round-trip through the codec directly. Hand-written
/// value objects that do not implement <see cref="IParsable{TSelf}"/> must project to the
/// underlying primitive (<c>.Value</c>) for cursors.
/// </para>
/// </remarks>
public static class CursorCodec
{
    private const string DateFormat = "O";
    private const char Separator = '|';
    private const string SeparatorString = "|";

    // A valid cursor encodes a single seek key — typically <128 bytes raw, ~170
    // chars base64. 1 KiB is a generous cap that accommodates any realistic key
    // type while rejecting adversarial inputs before allocation. The cap is a
    // belt-and-suspenders DoS guard, not a normative cursor-length rule.
    private const int MaxEncodedTokenLength = 1024;

    // ───── Single-key ──────────────────────────────────────────────────────────

    /// <summary>
    /// Encodes a single-key cursor for pure Id-based seek pagination.
    /// </summary>
    /// <typeparam name="TKey">The key type. Trellis primitives such as <see cref="Guid"/>,
    /// <see cref="long"/>, <see cref="int"/>, and <see cref="string"/> are supported.</typeparam>
    /// <param name="id">The Id of the boundary item.</param>
    /// <returns>An opaque <see cref="Cursor"/> token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> formats to an empty invariant-culture string (for example, an empty <see cref="string"/> key).</exception>
    public static Cursor Encode<TKey>(TKey id)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(id);

        var payload = FormatInvariant(id);
        if (payload.Length == 0)
            throw new ArgumentException("Cursor key formatted to an empty payload; supply a non-empty key.", nameof(id));
        return new Cursor(ToBase64Url(payload));
    }

    /// <summary>
    /// Attempts to decode a single-key cursor.
    /// </summary>
    /// <typeparam name="TKey">The key type. Must implement <see cref="IParsable{TSelf}"/>.</typeparam>
    /// <param name="cursor">The cursor to decode.</param>
    /// <param name="fieldName">Optional field name for the failure error; defaults to <c>"cursor"</c>.</param>
    /// <returns>
    /// A <see cref="Result{TKey}"/> containing the decoded key on success or
    /// <see cref="Error.InvalidInput"/> on a malformed token.
    /// </returns>
    public static Result<TKey> TryDecode<TKey>(Cursor cursor, string? fieldName = null)
        where TKey : IParsable<TKey>
    {
        if (cursor.Equals(default(Cursor)))
            return Fail<TKey>(fieldName, "cursor.malformed", "Cursor is default-constructed and has no token.");

        if (!TryFromBase64Url(cursor.Token, out var payload))
            return Fail<TKey>(fieldName, "cursor.malformed", "Cursor is not a valid URL-safe base64 token.");

        if (!TKey.TryParse(payload, CultureInfo.InvariantCulture, out var parsed) || parsed is null)
            return Fail<TKey>(fieldName, "cursor.malformed", $"Cursor payload could not be parsed as {typeof(TKey).Name}.");

        return Result.Ok(parsed);
    }

    // ───── Composite (CreatedAt, Id) ───────────────────────────────────────────

    /// <summary>
    /// Encodes a composite <c>(CreatedAt, Id)</c> cursor for stable time-ordered seek pagination.
    /// </summary>
    /// <typeparam name="TKey">The Id type. Trellis primitives such as <see cref="Guid"/>,
    /// <see cref="long"/>, <see cref="int"/>, and <see cref="string"/> are supported.</typeparam>
    /// <param name="createdAt">The creation timestamp of the boundary item.</param>
    /// <param name="id">The Id of the boundary item, used as the secondary sort key.</param>
    /// <returns>An opaque <see cref="Cursor"/> token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> formats to an empty invariant-culture string (for example, an empty <see cref="string"/> key).</exception>
    public static Cursor Encode<TKey>(DateTimeOffset createdAt, TKey id)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(id);

        var datePart = createdAt.ToString(DateFormat, CultureInfo.InvariantCulture);
        var idPart = FormatInvariant(id);
        if (idPart.Length == 0)
            throw new ArgumentException("Cursor key formatted to an empty payload; supply a non-empty key.", nameof(id));
        var payload = string.Concat(datePart, SeparatorString, idPart);
        return new Cursor(ToBase64Url(payload));
    }

    /// <summary>
    /// Attempts to decode a composite <c>(CreatedAt, Id)</c> cursor.
    /// </summary>
    /// <typeparam name="TKey">The Id type. Must implement <see cref="IParsable{TSelf}"/>.</typeparam>
    /// <param name="cursor">The cursor to decode.</param>
    /// <param name="fieldName">Optional field name for the failure error; defaults to <c>"cursor"</c>.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing the decoded <c>(CreatedAt, Id)</c> pair on success
    /// or <see cref="Error.InvalidInput"/> on a malformed token.
    /// </returns>
    public static Result<(DateTimeOffset CreatedAt, TKey Id)> TryDecodeComposite<TKey>(
        Cursor cursor, string? fieldName = null)
        where TKey : IParsable<TKey>
    {
        if (cursor.Equals(default(Cursor)))
            return Fail<(DateTimeOffset, TKey)>(fieldName, "cursor.malformed", "Cursor is default-constructed and has no token.");

        if (!TryFromBase64Url(cursor.Token, out var payload))
            return Fail<(DateTimeOffset, TKey)>(fieldName, "cursor.malformed", "Cursor is not a valid URL-safe base64 token.");

        var pipe = payload.IndexOf(Separator, StringComparison.Ordinal);
        if (pipe < 0)
            return Fail<(DateTimeOffset, TKey)>(fieldName, "cursor.malformed", "Cursor payload is missing the composite separator.");

        var datePart = payload.AsSpan(0, pipe);
        var idPart = payload.AsSpan(pipe + 1);

        if (!DateTimeOffset.TryParseExact(datePart, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
            return Fail<(DateTimeOffset, TKey)>(fieldName, "cursor.malformed", "Cursor payload date segment could not be parsed.");

        if (!TKey.TryParse(idPart.ToString(), CultureInfo.InvariantCulture, out var id) || id is null)
            return Fail<(DateTimeOffset, TKey)>(fieldName, "cursor.malformed", $"Cursor payload id segment could not be parsed as {typeof(TKey).Name}.");

        return Result.Ok((createdAt, id));
    }

    // ───── Internals ───────────────────────────────────────────────────────────

    private static string FormatInvariant<TKey>(TKey id)
        where TKey : notnull =>
        id switch
        {
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            string s => s,
            _ => throw new NotSupportedException(
                $"Cursor key type '{typeof(TKey).FullName}' must implement IFormattable or be string; " +
                "a culture-sensitive ToString would break cursor round-trip.")
        };

    private static string ToBase64Url(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var standard = Convert.ToBase64String(bytes);
        return standard.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static bool TryFromBase64Url(string token, out string payload)
    {
        payload = string.Empty;
        if (string.IsNullOrEmpty(token))
            return false;

        if (token.Length > MaxEncodedTokenLength)
            return false;

        var standard = token.Replace('-', '+').Replace('_', '/');
        switch (standard.Length % 4)
        {
            case 2: standard += "=="; break;
            case 3: standard += "="; break;
            case 1: return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(standard);
            payload = StrictUtf8.GetString(bytes);
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

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static Result<T> Fail<T>(string? fieldName, string reasonCode, string detail) =>
        Result.Fail<T>(Error.InvalidInput.ForField(fieldName ?? "cursor", reasonCode, detail));
}