namespace Trellis.Asp.Idempotency.Cosmos;

using System;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// Service-registration extensions for the Cosmos DB idempotency store.
/// </summary>
/// <remarks>
/// These are store registrations, not composition-root features, so they deliberately have no
/// matching <c>TrellisServiceBuilder.UseXxx()</c> slot — exactly like
/// <c>AddInMemoryIdempotencyStore()</c>. Choosing a backing store is a one-line decision an
/// application makes alongside <c>AddTrellisIdempotency()</c>; it does not participate in pipeline
/// ordering, so a builder slot would add surface without removing a decision.
/// </remarks>
public static class CosmosIdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CosmosIdempotencyStore"/> as the <see cref="IIdempotencyStore"/>,
    /// resolving its container through <paramref name="containerFactory"/>.
    /// </summary>
    /// <remarks>
    /// Use this overload when the container is provisioned at startup, shared with other
    /// components, or configured in a way the id-based overload cannot express. The container must
    /// be partitioned on <see cref="CosmosIdempotencyContainer.PartitionKeyPath"/> with TTL
    /// enabled; <see cref="CosmosIdempotencyContainer.CreateIfNotExistsAsync"/> does both.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="containerFactory">Resolves the container to store entries in.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCosmosIdempotencyStore(
        this IServiceCollection services,
        Func<IServiceProvider, Container> containerFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(containerFactory);

        services.AddSingleton<IIdempotencyStore>(sp => new CosmosIdempotencyStore(
            containerFactory(sp),
            sp.GetRequiredService<IOptions<IdempotencyOptions>>().Value,
            sp.GetService<TimeProvider>()));

        return services;
    }

    /// <summary>
    /// Registers <see cref="CosmosIdempotencyStore"/> against a container addressed by database
    /// and container id, resolving <see cref="CosmosClient"/> from the container.
    /// </summary>
    /// <remarks>
    /// This overload only addresses the container; it does not create it. Cosmos DB returns an
    /// existing container handle without a network call, so a container that was never provisioned
    /// — or was provisioned without TTL enabled — surfaces as a failure on the first idempotent
    /// request rather than at startup. Call
    /// <see cref="CosmosIdempotencyContainer.CreateIfNotExistsAsync"/> during startup, or deploy
    /// the container as infrastructure.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="databaseId">Cosmos DB database id.</param>
    /// <param name="containerId">Container id. Defaults to <c>idempotency</c>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCosmosIdempotencyStore(
        this IServiceCollection services,
        string databaseId,
        string containerId = "idempotency")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        return services.AddCosmosIdempotencyStore(sp =>
            sp.GetRequiredService<CosmosClient>().GetContainer(databaseId, containerId));
    }
}