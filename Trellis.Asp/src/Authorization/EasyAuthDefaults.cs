namespace Trellis.Asp.Authorization;

/// <summary>
/// Default scheme name and request-header names for Azure App Service / Azure Container Apps
/// built-in authentication ("Easy Auth"), consumed by <see cref="EasyAuthAuthenticationHandler"/>
/// and <see cref="EasyAuthClaimsActorProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Trust precondition.</b> These headers are only trustworthy when the app is reachable
/// exclusively through the Easy Auth front end. The platform strips any client-supplied copies
/// of the <c>X-MS-CLIENT-PRINCIPAL*</c> headers at the boundary, so an authenticated request
/// carries a principal the platform vouched for. If the app is also reachable by a path that
/// does not go through Easy Auth (a misconfigured ingress, a side-car port, or local
/// development), a client can forge these headers and impersonate any actor. Enable the Easy
/// Auth handler only when that boundary holds.
/// </para>
/// </remarks>
public static class EasyAuthDefaults
{
    /// <summary>The default authentication scheme name registered by <c>AddEasyAuth</c>.</summary>
    public const string AuthenticationScheme = "EasyAuth";

    /// <summary>
    /// The header carrying the base64-encoded JSON client principal
    /// (<c>X-MS-CLIENT-PRINCIPAL</c>).
    /// </summary>
    public const string PrincipalHeaderName = "X-MS-CLIENT-PRINCIPAL";

    /// <summary>The header carrying the principal id (<c>X-MS-CLIENT-PRINCIPAL-ID</c>).</summary>
    public const string PrincipalIdHeaderName = "X-MS-CLIENT-PRINCIPAL-ID";

    /// <summary>The header carrying the principal name (<c>X-MS-CLIENT-PRINCIPAL-NAME</c>).</summary>
    public const string PrincipalNameHeaderName = "X-MS-CLIENT-PRINCIPAL-NAME";

    /// <summary>
    /// The header carrying the identity provider name (<c>X-MS-CLIENT-PRINCIPAL-IDP</c>).
    /// </summary>
    public const string PrincipalIdpHeaderName = "X-MS-CLIENT-PRINCIPAL-IDP";

    /// <summary>
    /// The request headers whose values determine the resolved actor for an Easy Auth request.
    /// Emitted as <c>Vary</c> entries by
    /// <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/> via
    /// <see cref="EasyAuthClaimsActorProvider.VaryByHeaders"/>, so an intermediate cache never
    /// serves one actor's response to another. Unlike the bearer-based providers (which vary by
    /// <c>Authorization</c>), the Easy Auth actor is derived from the platform principal headers.
    /// </summary>
    public static IReadOnlyCollection<string> PrincipalHeaders { get; } =
    [
        PrincipalHeaderName,
        PrincipalIdHeaderName,
        PrincipalNameHeaderName,
        PrincipalIdpHeaderName,
    ];
}