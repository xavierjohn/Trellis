namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Trellis;

/// <summary>
/// Resolves the HTTP status code for a binder-level value-object / scalar validation failure — the
/// <see cref="Error.InvalidInput"/> error class — from the ambient <see cref="TrellisAspOptions"/>
/// error map, so binder-, filter-, and handler-level validation of the same logical error stay on a
/// single configurable status.
/// </summary>
/// <remarks>
/// <para>
/// The default is <c>422</c> (Unprocessable Content), matching the status a domain handler emits for
/// <see cref="Error.InvalidInput"/> via <c>ResponseFailureWriter</c>. A host that calls
/// <c>MapError&lt;Error.InvalidInput&gt;(status)</c> on <see cref="TrellisAspOptions"/> changes every
/// validation seam uniformly — the scalar/JSON binder no longer pins a hardcoded 422 that the error
/// map could not override.
/// </para>
/// <para>
/// Only the <em>semantic</em> validation failure (well-formed bytes whose values were rejected by a
/// value object's <c>TryCreate</c>) routes through here. Syntactically malformed JSON stays
/// <c>400 Bad Request</c> per RFC 9110 §15.5.1 and is intentionally not remapped.
/// </para>
/// </remarks>
internal static class ScalarValidationStatus
{
    // Only the runtime type (Error.InvalidInput) drives the status lookup; the reason code is
    // irrelevant, so a single shared probe avoids a per-request allocation.
    private static readonly Error s_invalidInputProbe = Error.InvalidInput.ForRule("binder.validation");

    /// <summary>
    /// Resolves the configured HTTP status code for <see cref="Error.InvalidInput"/> (default
    /// <c>422</c>) using the ambient <see cref="TrellisAspOptions"/> from the request services.
    /// </summary>
    /// <param name="httpContext">The current request context, used to resolve the ambient options.</param>
    /// <returns>The configured status code for <see cref="Error.InvalidInput"/>.</returns>
    public static int Resolve(HttpContext httpContext) =>
        ErrorStatusCodeResolver.Resolve(httpContext, s_invalidInputProbe, errorMapper: null, errorOverrides: null);
}
