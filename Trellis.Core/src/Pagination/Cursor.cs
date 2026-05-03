namespace Trellis;

using System;

/// <summary>
/// Opaque pagination cursor exchanged between server and client. Servers MUST treat the
/// <see cref="Token"/> contents as their own private encoding; clients MUST treat it as a
/// black box and only echo it back on the next request.
/// </summary>
/// <remarks>
/// <para>
/// Cursors are the canonical "continue from here" primitive for collection pagination
/// in Trellis (see <see cref="Page{T}"/>). They are intentionally opaque to keep the
/// server free to change its sort order, encoding, signing, or skip strategy without
/// breaking clients.
/// </para>
/// <para>
/// Absence of a cursor is represented by <c>null</c> at the use site
/// (<c>Cursor?</c> or <c>Page&lt;T&gt;.Next is null</c>). There is no "empty cursor"
/// — a constructed <see cref="Cursor"/> always carries a non-empty token.
/// </para>
/// </remarks>
public readonly record struct Cursor
{
    private readonly string? _token;

    /// <summary>
    /// Creates a cursor with the supplied opaque token.
    /// </summary>
    /// <param name="token">The opaque continuation token. Must be non-null and non-empty.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="token"/> is null or empty.</exception>
    public Cursor(string token)
    {
        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("Cursor token must be a non-empty string.", nameof(token));
        _token = token;
    }

    /// <summary>The opaque continuation token. Server-defined encoding; never parsed by the client.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessed on a <c>default(Cursor)</c> instance. Cursors must be constructed via
    /// <see cref="Cursor(string)"/> with a non-empty token; default-construction bypasses validation
    /// and reading the property surfaces the violation rather than returning a misleading empty string.
    /// </exception>
    public string Token => _token ?? throw new InvalidOperationException(
        "Cursor was default-constructed. Use new Cursor(token) with a non-empty token; default(Cursor) is not a valid value.");
}