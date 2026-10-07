namespace Trellis.Mediator;

/// <summary>
/// Controls how the resource-authorization pipeline surfaces authorization-related failures
/// to clients. The choice is per-resource (configured via <see cref="ResourceAuthorizationOptions"/>)
/// because different resources warrant different existence-disclosure trade-offs even within
/// a single application.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Propagate"/> is the default: <c>Forbidden</c> and
/// <c>AuthenticationRequired</c> errors flow through verbatim, and the boundary layer maps
/// them to HTTP 403 / 401 with the original problem-details payload. This is the right choice
/// for public resources where existence is not itself sensitive.
/// </para>
/// <para>
/// <see cref="HideAsNotFound"/> normalizes <c>Error.NotFound</c>, <c>Error.Forbidden</c>, and
/// <c>Error.AuthenticationRequired</c> to the same public <c>Error.NotFound</c>. The boundary
/// maps it to HTTP 404 with the configured public resource and metadata, without the original
/// code, detail, resource, or cause. Choose
/// this for resources whose mere existence reveals information (incident reports, account
/// records, security findings, internal correspondence, …).
/// </para>
/// <para>
/// Other direct-resource or via-leaf errors, including <c>Error.Unexpected</c> and
/// <c>Error.Unavailable</c>, pass through unchanged. Via intermediate/owner load failures
/// retain their collapse-to-denial behavior. Hiding <c>Error.Unexpected</c> as 404 would
/// destroy operational signal and lead clients/caches to treat transient failures as
/// permanent absence.
/// </para>
/// </remarks>
public enum AuthFailureExposurePolicy
{
    /// <summary>
    /// Preserve resource-stage error metadata. Via intermediate/owner load failures still
    /// collapse to Forbidden. This is the default policy.
    /// </summary>
    Propagate = 0,

    /// <summary>
    /// Normalize <c>Error.NotFound</c>, <c>Error.Forbidden</c>, and <c>Error.AuthenticationRequired</c>
    /// to the same public <c>Error.NotFound</c>, so missing and withheld resources share the
    /// framework-generated error representation.
    /// </summary>
    HideAsNotFound = 1,
}