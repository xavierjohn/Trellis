namespace Trellis;

/// <summary>
/// Represents an authorization error when authentication is required but not provided.
/// Use this when a user is not authenticated (not logged in) and must authenticate to access the resource.
/// Maps to HTTP 401 Unauthorized.
/// </summary>
/// <remarks>
/// <para>
/// Despite the HTTP status name, this error indicates missing or invalid authentication credentials.
/// For authenticated users who lack permission, use <see cref="ForbiddenError"/> instead.
/// </para>
/// <para>
/// Common scenarios:
/// - Missing authentication token
/// - Expired or invalid credentials
/// - Authentication session has ended
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Error.Unauthorized("Authentication required")
/// Error.Unauthorized("Invalid or expired authentication token")
/// Error.Unauthorized("Session has expired. Please log in again")
/// </code>
/// </example>
public sealed class UnauthorizedError : Error
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnauthorizedError"/> class.
    /// </summary>
    /// <param name="detail">Description of why authorization failed.</param>
    /// <param name="code">The error code identifying this type of unauthorized error.</param>
    /// <param name="instance">Optional identifier for the unauthorized request.</param>
    public UnauthorizedError(string detail, string code, string? instance = null) : base(detail, code, instance)
    {
    }
}