namespace Trellis.Asp;

using System.Globalization;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Parses the RFC 7240 <c>Prefer</c> request header and exposes standard preference tokens.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Prefer</c> header allows clients to request optional server behaviors:
/// <list type="bullet">
/// <item><c>return=representation</c> — return the full resource after a write (200 OK)</item>
/// <item><c>return=minimal</c> — return a minimal response after a write (204 No Content)</item>
/// <item><c>respond-async</c> — prefer asynchronous processing (202 Accepted)</item>
/// <item><c>wait=N</c> — maximum seconds the client will wait before preferring async</item>
/// <item><c>handling=strict</c> — reject requests with any issues</item>
/// <item><c>handling=lenient</c> — process requests despite minor issues</item>
/// </list>
/// </para>
/// <para>
/// Per RFC 7240 §2, unrecognized preference tokens are silently ignored.
/// If a preference appears more than once, only the first instance is considered.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var prefer = PreferHeader.Parse(httpContext.Request);
/// if (prefer.ReturnMinimal)
///     return Results.NoContent();
/// </code>
/// </example>
public sealed class PreferHeader
{
    /// <summary>Gets whether the client prefers <c>return=representation</c> (full resource body).</summary>
    public bool ReturnRepresentation { get; }

    /// <summary>Gets whether the client prefers <c>return=minimal</c> (no body, typically 204).</summary>
    public bool ReturnMinimal { get; }

    /// <summary>Gets whether the client prefers <c>respond-async</c> (asynchronous processing).</summary>
    public bool RespondAsync { get; }

    /// <summary>Gets the <c>wait=N</c> preference value in seconds, or <c>null</c> if not specified.</summary>
    public int? Wait { get; }

    /// <summary>Gets whether the client prefers <c>handling=strict</c>.</summary>
    public bool HandlingStrict { get; }

    /// <summary>Gets whether the client prefers <c>handling=lenient</c>.</summary>
    public bool HandlingLenient { get; }

    /// <summary>Gets whether any preference was specified in the request.</summary>
    public bool HasPreferences { get; }

    private PreferHeader(
        bool returnRepresentation,
        bool returnMinimal,
        bool respondAsync,
        int? wait,
        bool handlingStrict,
        bool handlingLenient)
    {
        ReturnRepresentation = returnRepresentation;
        ReturnMinimal = returnMinimal;
        RespondAsync = respondAsync;
        Wait = wait;
        HandlingStrict = handlingStrict;
        HandlingLenient = handlingLenient;
        HasPreferences = returnRepresentation || returnMinimal || respondAsync
                         || wait.HasValue || handlingStrict || handlingLenient;
    }

    private static readonly PreferHeader s_empty = new(false, false, false, null, false, false);

    /// <summary>
    /// Parses the <c>Prefer</c> header from the given HTTP request.
    /// Returns an empty <see cref="PreferHeader"/> if the header is absent or empty.
    /// </summary>
    /// <param name="request">The HTTP request to read the <c>Prefer</c> header from.</param>
    /// <returns>A <see cref="PreferHeader"/> representing the parsed preferences.</returns>
    public static PreferHeader Parse(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var headerValues = request.Headers.GetCommaSeparatedValues("Prefer");
        if (headerValues.Length == 0)
            return s_empty;

        bool returnRepresentation = false;
        bool returnMinimal = false;
        bool respondAsync = false;
        int? wait = null;
        bool handlingStrict = false;
        bool handlingLenient = false;
        bool returnSeen = false;
        bool waitSeen = false;
        bool handlingSeen = false;

        foreach (var rawToken in headerValues)
        {
            // Strip parameters (everything after first ';') per RFC 7240 §2
            var token = rawToken.AsSpan().Trim();
            if (token.IsEmpty)
                continue;

            var semicolonIndex = token.IndexOf(';');
            if (semicolonIndex >= 0)
                token = token[..semicolonIndex].TrimEnd();

            // RFC 7240 §2: preference = token [ BWS "=" BWS word ]
            // Split on '=' allowing optional whitespace around it.
            var name = token;
            ReadOnlySpan<char> value = default;
            var equalsIndex = token.IndexOf('=');
            if (equalsIndex >= 0)
            {
                name = token[..equalsIndex].TrimEnd();
                value = token[(equalsIndex + 1)..].TrimStart();
            }

            if (name.Equals("respond-async", StringComparison.OrdinalIgnoreCase))
            {
                respondAsync = true;
            }
            else if (name.Equals("return", StringComparison.OrdinalIgnoreCase))
            {
                // RFC 7240 §2: values are case-sensitive
                if (!returnSeen)
                {
                    if (value.SequenceEqual("representation"))
                        returnRepresentation = true;
                    else if (value.SequenceEqual("minimal"))
                        returnMinimal = true;
                    returnSeen = true;
                }
            }
            else if (name.Equals("wait", StringComparison.OrdinalIgnoreCase))
            {
                // RFC 7240 §2: first occurrence wins
                if (!waitSeen)
                {
                    if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                        wait = seconds;
                    waitSeen = true;
                }
            }
            else if (name.Equals("handling", StringComparison.OrdinalIgnoreCase))
            {
                // RFC 7240 §2: values are case-sensitive
                if (!handlingSeen)
                {
                    if (value.SequenceEqual("strict"))
                        handlingStrict = true;
                    else if (value.SequenceEqual("lenient"))
                        handlingLenient = true;
                    handlingSeen = true;
                }
            }
            // Per RFC 7240 §2: unknown preferences are silently ignored
        }

        if (!returnRepresentation && !returnMinimal && !respondAsync
            && !wait.HasValue && !handlingStrict && !handlingLenient)
            return s_empty;

        return new PreferHeader(returnRepresentation, returnMinimal, respondAsync, wait, handlingStrict, handlingLenient);
    }
}
