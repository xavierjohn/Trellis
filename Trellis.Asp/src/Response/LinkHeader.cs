namespace Trellis.Asp;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Formats and emits RFC 8288 <c>Link</c> field values. Shared by the pagination
/// <c>next</c>/<c>prev</c> links and by consumer-configured relations
/// (<c>HttpResponseOptionsBuilder&lt;TDomain&gt;.WithLink</c>) so both produce byte-identical
/// field syntax and apply the same escaping rules.
/// </summary>
internal static class LinkHeader
{
    /// <summary>The RFC 8288 response header name.</summary>
    public const string Name = "Link";

    /// <summary>
    /// Builds a single <c>Link</c> field value, validating the relation and escaping the target.
    /// </summary>
    /// <param name="rel">A registered relation token or an absolute extension-relation URI.</param>
    /// <param name="href">The link target.</param>
    /// <returns>A field value of the form <c>&lt;target&gt;; rel="relation"</c>.</returns>
    public static string Format(string rel, string href)
    {
        var relation = NormalizeRelation(rel, nameof(rel));

        ArgumentNullException.ThrowIfNull(href, nameof(href));
        if (string.IsNullOrWhiteSpace(href))
            throw new ArgumentException("A link target must be a non-empty URI reference.", nameof(href));

        return $"<{SanitizeTarget(href)}>; rel=\"{relation}\"";
    }

    /// <summary>
    /// Validates a link relation type and returns its wire form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RFC 8288 §3.3 admits exactly two forms: a registered relation token
    /// (<c>reg-rel-type = LOALPHA *( LOALPHA | DIGIT | "." | "-" )</c>) or an extension relation
    /// expressed as an absolute URI. Anything else is rejected here rather than at emit time,
    /// so a malformed relation surfaces when the endpoint is configured instead of on every
    /// request. The ABNF is written with <c>LOALPHA</c>, but uppercase input is accepted rather
    /// than rejected: §2.1 makes registered relations case-insensitive, so the token is
    /// normalized to lowercase instead (see below).
    /// </para>
    /// <para>
    /// The rejection is a security boundary, not only a conformance one. The relation is emitted
    /// inside a quoted string, so a relation containing a double quote would close it early and
    /// append attacker-chosen link-params to the field. That is a distinct attack surface from
    /// the link *target*, which <see cref="SanitizeTarget"/> covers.
    /// </para>
    /// <para>
    /// Only the <em>shape</em> is validated. Whether a token is actually present in the IANA
    /// link-relation registry is deliberately not checked: that registry changes independently
    /// of Trellis, so an allow-list would reject a newly registered relation until the framework
    /// shipped again. Choosing a relation that clients will understand is the caller's decision —
    /// see the link-relation notes in the ASP API reference.
    /// </para>
    /// <para>
    /// Registered tokens are lowercased because RFC 8288 §2.1 defines them as case-insensitive
    /// and specifies lowercase on the wire. URI relations are returned verbatim, since the path
    /// component of a URI is case-sensitive and lowercasing it could point at a different
    /// resource.
    /// </para>
    /// </remarks>
    public static string NormalizeRelation(string rel, string paramName)
    {
        ArgumentNullException.ThrowIfNull(rel, paramName);

        if (string.IsNullOrWhiteSpace(rel))
            throw new ArgumentException("A link relation must be a non-empty token or absolute URI.", paramName);

        if (IsRegisteredRelationToken(rel))
            return rel.ToLowerInvariant();

        if (IsExtensionRelationUri(rel))
            return rel;

        throw new ArgumentException(
            $"'{rel}' is not a valid RFC 8288 link relation. Use a registered relation token " +
            "(ASCII letters, digits, '.' and '-', starting with a letter, normalized to lowercase " +
            "on the wire — for example \"describedby\" or \"service-desc\") or an absolute URI for " +
            "an extension relation.",
            paramName);
    }

    // Accepts either case even though reg-rel-type is defined with LOALPHA: RFC 8288 section 2.1
    // makes registered relations case-insensitive, so uppercase is valid input that the caller
    // lowercases for the wire rather than invalid input to reject.
    private static bool IsRegisteredRelationToken(string rel)
    {
        if (!char.IsAsciiLetter(rel[0]))
            return false;

        foreach (var c in rel)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-'))
                return false;
        }

        return true;
    }

    private static bool IsExtensionRelationUri(string rel)
    {
        foreach (var c in rel)
        {
            if (IsForbiddenInUriReference(c))
                return false;
        }

        return Uri.TryCreate(rel, UriKind.Absolute, out _);
    }

    /// <summary>
    /// Percent-encodes the characters that may not appear literally in the
    /// <c>URI-Reference</c> of an RFC 8288 <c>Link</c> field value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pagination target embeds an opaque, server-defined cursor token, and a configured
    /// target is consumer-supplied. A target containing <c>&gt;</c> would close the
    /// <c>&lt;URI-Reference&gt;</c> early and let the remainder forge additional link-params;
    /// control characters would corrupt the header frame outright. Encoding only the characters
    /// RFC 3986 already forbids in a URI leaves every well-formed URL byte-identical.
    /// </para>
    /// </remarks>
    public static string SanitizeTarget(string href)
    {
        var needsEncoding = false;
        foreach (var c in href)
        {
            if (IsForbiddenInUriReference(c))
            {
                needsEncoding = true;
                break;
            }
        }

        if (!needsEncoding)
            return href;

        var sb = new StringBuilder(href.Length + 8);
        foreach (var c in href)
        {
            if (IsForbiddenInUriReference(c))
                sb.Append('%').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends <paramref name="formattedLinks"/> to the response as a single <c>Link</c> field.
    /// </summary>
    /// <remarks>
    /// Appends rather than assigns so configured relations coexist with the pagination
    /// <c>next</c>/<c>prev</c> field emitted by <c>PagedHttpResult</c>, whichever runs first.
    /// RFC 8288 permits a relation to appear across multiple <c>Link</c> field lines.
    /// </remarks>
    public static void Append(HttpResponse response, IReadOnlyList<string>? formattedLinks)
    {
        if (formattedLinks is not { Count: > 0 })
            return;

        response.Headers.Append(Name, string.Join(", ", formattedLinks));
    }

    // RFC 3986 §2: excluded US-ASCII characters — controls, space, and the "delims"/"unwise" set.
    // Non-ASCII is left alone: an IRI-style href is already the caller's encoding decision, and
    // rewriting it here would corrupt legitimately percent-encoded UTF-8.
    private static bool IsForbiddenInUriReference(char c) =>
        c is <= '\u0020' or '\u007F' or '<' or '>' or '"' or '\\' or '^' or '`' or '{' or '}' or '|';
}
