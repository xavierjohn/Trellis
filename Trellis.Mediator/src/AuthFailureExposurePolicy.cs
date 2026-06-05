namespace Trellis.Mediator;

/// <summary>
/// Controls how the resource-authorization pipeline surfaces authorization-related failures
/// to clients. The choice is per-resource (configured via <see cref="ResourceAuthorizationOptions"/>)
/// because different resources warrant different existence-disclosure trade-offs even within
/// a single application.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Propagate"/> is the default for backward compatibility: <c>Forbidden</c> and
/// <c>AuthenticationRequired</c> errors flow through verbatim, and the boundary layer maps
/// them to HTTP 403 / 401 with the original problem-details payload. This is the right choice
/// for public resources where existence is not itself sensitive.
/// </para>
/// <para>
/// <see cref="HideAsNotFound"/> translates <c>Error.Forbidden</c> and
/// <c>Error.AuthenticationRequired</c> to <c>new Error.NotFound(ResourceRef)</c> — the boundary
/// layer maps the synthetic NotFound to HTTP 404 so an unauthorized actor cannot distinguish
/// "the resource does not exist" from "the resource exists but you may not access it". Choose
/// this for resources whose mere existence reveals information (incident reports, account
/// records, security findings, internal correspondence, …).
/// </para>
/// <para>
/// Only <c>Error.Forbidden</c> and <c>Error.AuthenticationRequired</c> are translated. Other
/// errors — <c>Error.Unexpected</c>, <c>Error.Unavailable</c>, <c>Error.NotFound</c> from a
/// loader, and so on — pass through unchanged. Hiding <c>Error.Unexpected</c> as 404 would
/// destroy operational signal and lead clients/caches to treat transient failures as
/// permanent absence.
/// </para>
/// </remarks>
public enum AuthFailureExposurePolicy
{
    /// <summary>
    /// Pass <c>Error.Forbidden</c> and <c>Error.AuthenticationRequired</c> through to the
    /// boundary layer verbatim. Default for backward compatibility.
    /// </summary>
    Propagate = 0,

    /// <summary>
    /// Translate <c>Error.Forbidden</c> and <c>Error.AuthenticationRequired</c> to
    /// <c>new Error.NotFound(ResourceRef)</c> so an unauthorized actor cannot distinguish
    /// "the resource does not exist" from "the resource exists but you may not access it".
    /// </summary>
    HideAsNotFound = 1,
}
