namespace CookbookSnippets.Recipe41;

using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.Asp.Idempotency;
using Trellis.Authorization;
using Trellis.ServiceDefaults;

public sealed class SharedClaimsActorProvider(IHttpContextAccessor accessor) : IActorProvider, IActorIdentityProvider
{
    public static Maybe<ActorId> CanonicalActorId(ClaimsPrincipal principal)
    {
        var identity = principal.Identities.FirstOrDefault(i => i.IsAuthenticated);
        var subject = identity?.FindFirst("sub")?.Value
            ?? identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrWhiteSpace(subject)
            ? Maybe<ActorId>.None
            : Maybe.From(ActorId.Create($"sample:{subject.Trim()}"));
    }

    public ValueTask<Maybe<ActorId>> GetCurrentActorIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(CanonicalActorId(CurrentPrincipal()));
    }

    public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = CurrentPrincipal();
        return Task.FromResult(CanonicalActorId(principal).Map(id => Actor.Create(id,
            principal.Identities.First(i => i.IsAuthenticated).FindAll("permissions")
                .Select(c => c.Value).ToHashSet(StringComparer.Ordinal))));
    }

    private ClaimsPrincipal CurrentPrincipal() => accessor.HttpContext?.User
        ?? throw new InvalidOperationException("Actor resolution requires an HTTP request.");
}

public sealed record ActorIdentityRateLimitMetadata;

public static class ActorRateLimitingSample
{
    private const string Policy = "actor";
    private static readonly object s_identitySnapshotKey = new();
    private sealed record IdentitySnapshot(Maybe<ActorId> Id);

    public static void ConfigureServices(IServiceCollection services, bool usePrefetch)
    {
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddTrellis(t => t.UseAsp().UseProblemDetails().UseIdempotency());
        services.AddInMemoryIdempotencyStore();
        services.AddCachingActorProvider<SharedClaimsActorProvider>();
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(Policy, context => RateLimitPartition.GetFixedWindowLimiter(
                usePrefetch ? PrefetchedPartitionKey(context) : ClaimsPartitionKey(context),
                static _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 2,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
            options.UseTrellisRejectionHandler();
        });
    }

    public static void ConfigurePipeline(WebApplication app, bool usePrefetch, bool authorizationFirst = false)
    {
        app.UseRouting();
        app.UseAuthentication();
        if (authorizationFirst)
            app.UseAuthorization();
        if (usePrefetch)
            UseIdentityPrefetch(app);
        app.UseRateLimiter();
        if (!authorizationFirst)
            app.UseAuthorization();
        app.UseTrellisIdempotency();

        app.MapGet("/limited", static () => Results.Ok())
            .WithMetadata(new ActorIdentityRateLimitMetadata()).RequireRateLimiting(Policy);
        app.MapPost("/payments", static () => Results.Ok(new { ReceiptId = Guid.NewGuid() }))
            .WithMetadata(new ActorIdentityRateLimitMetadata(), new IdempotentAttribute())
            .RequireRateLimiting(Policy).RequireAuthorization();
        app.MapGet("/ready", static () => Results.Ok()).AllowAnonymous().DisableRateLimiting();
    }

    public static void UseIdentityPrefetch(IApplicationBuilder app) =>
        app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<ActorIdentityRateLimitMetadata>() is not null)
            {
                var provider = context.RequestServices.GetRequiredService<IActorIdentityProvider>();
                var id = await provider.GetCurrentActorIdAsync(context.RequestAborted).ConfigureAwait(false);
                context.Items[s_identitySnapshotKey] = new IdentitySnapshot(id);
            }

            await next(context).ConfigureAwait(false);
        });

    public static string ClaimsPartitionKey(HttpContext context) =>
        PartitionKey(context, SharedClaimsActorProvider.CanonicalActorId(context.User));

    public static string PrefetchedPartitionKey(HttpContext context)
    {
        if (!context.Items.TryGetValue(s_identitySnapshotKey, out var value) || value is not IdentitySnapshot snapshot)
            throw new InvalidOperationException(
                "Actor identity prefetch did not run. Add the endpoint metadata and mount " +
                "UseIdentityPrefetch after routing/authentication and before UseRateLimiter.");

        return PartitionKey(context, snapshot.Id);
    }

    private static string PartitionKey(HttpContext context, Maybe<ActorId> identity) =>
        identity.Match(id => $"actor:{id.Value}", () => NetworkPartitionKey(context.Connection.RemoteIpAddress));

    private static string NetworkPartitionKey(IPAddress? address)
    {
        if (address is null)
            return "network:unknown";
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return $"network:v4:{address}";

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"network:v6:{new IPAddress(bytes)}/64";
    }
}