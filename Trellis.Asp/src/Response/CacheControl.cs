namespace Trellis.Asp;

using System;
using System.Net.Http.Headers;

/// <summary>
/// Common <see cref="CacheControlHeaderValue"/> presets for use with
/// <see cref="HttpResponseOptionsBuilder{TDomain}.WithCacheControl(CacheControlHeaderValue)"/>
/// and <see cref="HttpResponseOptionsBuilder.WithCacheControl(CacheControlHeaderValue)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every helper returns a <strong>fresh</strong> <see cref="CacheControlHeaderValue"/> on each
/// call. <see cref="CacheControlHeaderValue"/> is mutable, so mutating an instance returned by
/// one of these helpers does not leak into a later call.
/// </para>
/// <para>
/// Use directly: <c>opts.WithCacheControl(CacheControl.NoStore())</c>, or pick the BCL type
/// directly when you need a directive that isn't represented as a property on
/// <see cref="CacheControlHeaderValue"/>.
/// </para>
/// </remarks>
public static class CacheControl
{
    /// <summary><c>Cache-Control: no-store</c> — the response must not be stored anywhere.</summary>
    /// <remarks>
    /// Use for responses that may contain personal data, secrets, or per-user state. Applies to
    /// both success and failure responses through <see cref="HttpResponseOptionsBuilder{TDomain}.WithCacheControl(CacheControlHeaderValue)"/>
    /// — a 404 from a sensitive endpoint is no less sensitive than the 200.
    /// </remarks>
    public static CacheControlHeaderValue NoStore() => new() { NoStore = true };

    /// <summary><c>Cache-Control: no-cache</c> — caches must revalidate with the origin before serving.</summary>
    /// <remarks>Different from <c>no-store</c>: <c>no-cache</c> allows storage but requires revalidation.</remarks>
    public static CacheControlHeaderValue NoCache() => new() { NoCache = true };

    /// <summary><c>Cache-Control: public, max-age={seconds}</c> — shared caches may store the response.</summary>
    /// <param name="maxAge">Freshness lifetime; emitted as <c>max-age</c> in seconds (truncated to whole seconds). Must be non-negative — RFC 9111 <c>delta-seconds</c> is unsigned.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAge"/> is negative.</exception>
    public static CacheControlHeaderValue Public(TimeSpan maxAge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAge, TimeSpan.Zero);
        return new() { Public = true, MaxAge = maxAge };
    }

    /// <summary><c>Cache-Control: private, max-age={seconds}</c> — only the requesting user agent may store the response.</summary>
    /// <param name="maxAge">Freshness lifetime; emitted as <c>max-age</c> in seconds (truncated to whole seconds). Must be non-negative — RFC 9111 <c>delta-seconds</c> is unsigned.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAge"/> is negative.</exception>
    public static CacheControlHeaderValue Private(TimeSpan maxAge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAge, TimeSpan.Zero);
        return new() { Private = true, MaxAge = maxAge };
    }

    /// <summary>
    /// <c>Cache-Control: public, max-age={seconds}, immutable</c> — the response will not change for
    /// its freshness lifetime, so clients should not revalidate. The <c>immutable</c> directive
    /// (RFC 8246) is appended via the <see cref="CacheControlHeaderValue.Extensions"/> collection
    /// because the BCL type has no dedicated property for it.
    /// </summary>
    /// <param name="maxAge">Freshness lifetime; emitted as <c>max-age</c> in seconds. Must be non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAge"/> is negative.</exception>
    public static CacheControlHeaderValue Immutable(TimeSpan maxAge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAge, TimeSpan.Zero);
        var cc = new CacheControlHeaderValue { Public = true, MaxAge = maxAge };
        cc.Extensions.Add(new NameValueHeaderValue("immutable"));
        return cc;
    }
}