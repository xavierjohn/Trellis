namespace Trellis.Authorization;

/// <summary>
/// Well-known attribute keys for <see cref="Actor.Attributes"/> used in
/// attribute-based access control (ABAC) checks.
/// Claim-based keys align with Azure Entra ID v2.0 access token claims.
/// Use these constants instead of raw strings to prevent key-casing mismatches.
/// </summary>
/// <remarks>
/// <para>
/// Attributes sourced from JWT claims should be mapped during Actor hydration in
/// <see cref="IActorProvider"/>. Non-claim attributes (e.g., <see cref="IpAddress"/>,
/// <see cref="MfaAuthenticated"/>) are derived from the request context.
/// </para>
/// <para>
/// For the full v2.0 claims reference, see
/// <see href="https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference"/>.
/// </para>
/// </remarks>
public static class ActorAttributes
{
    /// <summary>
    /// Tenant identifier. Corresponds to the <c>tid</c> claim in Azure Entra ID v2.0 tokens.
    /// The GUID of the Microsoft Entra tenant that issued the token.
    /// </summary>
    public const string TenantId = "tid";

    /// <summary>
    /// The primary human-readable identifier for the user (usually an email or UPN).
    /// Corresponds to the <c>preferred_username</c> claim.
    /// Suitable for display and audit logging — do not use for authorization logic
    /// as this value can change (e.g., when a user is renamed).
    /// </summary>
    public const string PreferredUsername = "preferred_username";

    /// <summary>
    /// The Application ID of the client application that requested the token.
    /// Corresponds to the <c>azp</c> (Authorized Party) claim.
    /// Use to restrict operations to specific client applications.
    /// </summary>
    public const string AuthorizedParty = "azp";

    /// <summary>
    /// Indicates how the client application authenticated.
    /// Corresponds to the <c>azpacr</c> claim.
    /// Values: <c>"0"</c> = public client (no secret), <c>"1"</c> = client secret,
    /// <c>"2"</c> = certificate.
    /// </summary>
    public const string AuthorizedPartyAcr = "azpacr";

    /// <summary>
    /// Authentication context class reference for Conditional Access step-up authentication.
    /// Corresponds to the <c>acrs</c> claim.
    /// Use to enforce that a specific Conditional Access authentication context was satisfied
    /// (e.g., requiring MFA within the last 10 minutes for sensitive operations).
    /// </summary>
    public const string AuthContextClassReference = "acrs";

    /// <summary>
    /// Client IP address. Not a v2.0 JWT claim — populate from
    /// <c>HttpContext.Connection.RemoteIpAddress</c> during Actor hydration.
    /// </summary>
    public const string IpAddress = "ip_address";

    /// <summary>
    /// Whether the actor authenticated with multi-factor authentication.
    /// Not a direct v2.0 claim — derive from the <c>amr</c> (Authentication Methods References)
    /// claim during Actor hydration. Expected values: <c>"true"</c>, <c>"false"</c>.
    /// </summary>
    public const string MfaAuthenticated = "mfa";
}