namespace Trellis.Asp.Authorization;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// <see cref="ClaimsActorProvider"/> for requests authenticated by Azure App Service /
/// Container Apps built-in authentication ("Easy Auth"). Actor resolution is inherited
/// verbatim from <see cref="ClaimsActorProvider"/> — it maps <c>HttpContext.User</c> claims
/// (populated by <see cref="EasyAuthAuthenticationHandler"/>) to the actor id and permissions
/// using <see cref="ClaimsActorOptions"/>.
/// </summary>
/// <remarks>
/// The only behavioral difference from <see cref="ClaimsActorProvider"/> is
/// <see cref="VaryByHeaders"/>: an Easy Auth actor is derived from the platform principal
/// headers, not <c>Authorization</c>. Reusing the base provider unchanged would emit
/// <c>Vary: Authorization</c> and let an intermediate cache serve one actor's response to
/// another when an endpoint calls
/// <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/>.
/// </remarks>
public sealed class EasyAuthClaimsActorProvider : ClaimsActorProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EasyAuthClaimsActorProvider"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">Provides the current HTTP context.</param>
    /// <param name="options">Claim mapping options.</param>
    /// <param name="logger">Optional logger for the base provider's claim-shape diagnostics.</param>
    public EasyAuthClaimsActorProvider(
        IHttpContextAccessor httpContextAccessor,
        IOptions<ClaimsActorOptions> options,
        ILogger<ClaimsActorProvider>? logger = null)
        : base(httpContextAccessor, options, logger)
    {
    }

    /// <summary>
    /// The request headers whose values determine the resolved actor. Overrides the base
    /// <c>["Authorization"]</c> so <c>VaryForActor()</c> partitions intermediate caches by the
    /// Easy Auth platform principal headers rather than the (unused) <c>Authorization</c> header.
    /// </summary>
    public override IReadOnlyCollection<string> VaryByHeaders { get; } = EasyAuthDefaults.PrincipalHeaders;
}