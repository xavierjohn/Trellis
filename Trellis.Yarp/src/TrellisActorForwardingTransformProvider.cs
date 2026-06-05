namespace Trellis.Yarp;

using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using global::Yarp.ReverseProxy.Configuration;
using global::Yarp.ReverseProxy.Transforms;
using global::Yarp.ReverseProxy.Transforms.Builder;
using Trellis.Authorization;

/// <summary>
/// YARP <see cref="ITransformProvider"/> that, on every cluster, attaches a per-request
/// transform that resolves the current <see cref="Actor"/> and re-mints an internal
/// JWT carrying the full actor surface for the destination cluster. Captures
/// <see cref="ClusterConfig"/> at <see cref="TransformBuilderContext"/> time (the
/// per-request <see cref="RequestTransformContext"/> has no <c>Cluster</c> property —
/// verified GPT-5.5 round-3 finding 1).
/// </summary>
internal sealed class TrellisActorForwardingTransformProvider : ITransformProvider
{
    /// <inheritdoc />
    public void ValidateRoute(TransformRouteValidationContext context)
    {
        // Per-route validation is not needed for v1: the forwarding contract is per-cluster.
    }

    /// <inheritdoc />
    public void ValidateCluster(TransformClusterValidationContext context)
    {
        // Per-cluster startup validation is handled by TrellisActorForwardingOptionsValidator
        // (ValidateOnStart on options); the cluster-shape validation YARP runs here is the
        // same regardless of whether the actor-forwarding transform is attached.
    }

    /// <inheritdoc />
    public void Apply(TransformBuilderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Build-time capture of the destination cluster configuration. This closes over
        // the ClusterConfig for the lifetime of the transform pipeline so the per-request
        // transform below does not have to (and cannot) read the cluster at request time
        // (RequestTransformContext has no Cluster property).
        var cluster = context.Cluster;
        if (cluster is null) return;

        context.AddRequestTransform(requestContext =>
            TrellisActorForwardingRequestTransform.ApplyAsync(requestContext, cluster));
    }
}

/// <summary>
/// Per-request half of the actor-forwarding pipeline. Resolves
/// <see cref="IActorProvider"/> and <see cref="TrellisActorJwtMinter"/> from the
/// request scope, mints a fresh JWT carrying the actor surface for the captured
/// <see cref="ClusterConfig"/>, and overwrites the upstream <c>Authorization</c>
/// header. Fails open in the "no actor" case: an unauthenticated inbound request
/// reaches the downstream service with no <c>Authorization</c> header, and the
/// downstream policy decides whether to allow anonymous access or return 401.
/// </summary>
internal static class TrellisActorForwardingRequestTransform
{
    public static async ValueTask ApplyAsync(RequestTransformContext requestContext, ClusterConfig cluster)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(cluster);

        var httpContext = requestContext.HttpContext;
        var services = httpContext.RequestServices;
        var logger = services.GetRequiredService<ILogger<TrellisActorJwtMinter>>();
        var actorProvider = services.GetRequiredService<IActorProvider>();
        var minter = services.GetRequiredService<TrellisActorJwtMinter>();

        var actorMaybe = await actorProvider.GetCurrentActorAsync(httpContext.RequestAborted).ConfigureAwait(false);
        if (!actorMaybe.HasValue)
        {
            // No authenticated actor: clear the Authorization header so the upstream
            // (external-IDP) bearer token cannot reach the downstream service. Without
            // this clear, YARP's default behavior copies the inbound header to the
            // proxied request, which would let an external token (audience: gateway)
            // arrive at a downstream that pins audience to its internal value — fine
            // IF the downstream follows Recipe 33's strict-audience profile, but a
            // single misconfigured downstream creates an authority-confusion vector.
            // Fail closed: no actor → no Authorization on the upstream request, and
            // the downstream policy decides whether anonymous is allowed.
            requestContext.ProxyRequest.Headers.Authorization = null;
            TrellisActorForwardingLog.NoActor(logger, cluster.ClusterId);
            return;
        }

        var actor = actorMaybe.Value;
        TrellisActorMintResult mintResult;
        try
        {
            mintResult = minter.MintFor(actor, cluster);
        }
        catch (Exception ex)
        {
            // Minting failure is a genuine 500 condition (signing crypto failed, options
            // bound after startup validation, etc.). We log the exception type only (never
            // its message — it can carry PII / secret material from the key handle) and
            // re-throw so YARP returns a 502 to the inbound caller. We do NOT silently
            // drop the request with an unsigned forward.
            TrellisActorForwardingLog.MintFailed(logger, cluster.ClusterId, ex.GetType().Name);
            throw;
        }

        requestContext.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mintResult.CompactJws);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            var expUnix = mintResult.ExpiresAt.ToUnixTimeSeconds();
            // Use the PROJECTED counts from the mint result (what's actually in the JWT),
            // NOT actor.Permissions.Count / actor.ForbiddenPermissions.Count (the SOURCE
            // counts before projection). A projection callback that filters by cluster
            // namespace would make the log disagree with the token if we logged source.
            TrellisActorForwardingLog.TokenMinted(
                logger,
                cluster.ClusterId,
                mintResult.Kid,
                mintResult.Jti,
                mintResult.Issuer,
                mintResult.Audience,
                expUnix,
                mintResult.PermissionsCount,
                mintResult.ForbiddenPermissionsCount);
        }
    }
}

/// <summary>
/// Source-generated [LoggerMessage] events for the actor-forwarding transform. All
/// events are intentionally low-cardinality: cluster id, kid, counts, exception
/// TYPE names. NEVER log raw claim values, the full JWT, actor IDs, tenant IDs, or
/// other PII. (Security review finding #9, Medium.)
/// </summary>
internal static partial class TrellisActorForwardingLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        EventName = "TrellisYarpTokenMinted",
        Message = "Trellis YARP minted forward token for cluster {ClusterId} with kid {Kid}: jti={Jti}, iss={Issuer}, aud={Audience}, exp={ExpiresAtUnixSeconds}, permissions_count={PermissionsCount}, forbidden_permissions_count={ForbiddenPermissionsCount}")]
    public static partial void TokenMinted(
        ILogger logger,
        string clusterId,
        string kid,
        string jti,
        string issuer,
        string audience,
        long expiresAtUnixSeconds,
        int permissionsCount,
        int forbiddenPermissionsCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        EventName = "TrellisYarpNoActor",
        Message = "Trellis YARP forwarding skipped for cluster {ClusterId}: no authenticated actor on inbound request; downstream will receive no Authorization header")]
    public static partial void NoActor(ILogger logger, string clusterId);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        EventName = "TrellisYarpMintFailed",
        Message = "Trellis YARP forwarding failed for cluster {ClusterId}: minter threw {ExceptionType}")]
    public static partial void MintFailed(ILogger logger, string clusterId, string exceptionType);
}
