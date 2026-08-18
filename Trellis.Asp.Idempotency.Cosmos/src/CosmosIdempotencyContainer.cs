namespace Trellis.Asp.Idempotency.Cosmos;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Provisioning helper for the container <see cref="CosmosIdempotencyStore"/> expects.
/// </summary>
public static class CosmosIdempotencyContainer
{
    /// <summary>Partition key path the store requires.</summary>
    public const string PartitionKeyPath = "/scope";

    /// <summary>
    /// Creates the idempotency container if it does not already exist, with the partition key and
    /// TTL configuration the store requires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-item <c>ttl</c> is ignored unless the container enables TTL, so a container created
    /// without <see cref="ContainerProperties.DefaultTimeToLive"/> would accumulate idempotency
    /// entries forever. This helper sets it to <c>-1</c>, which enables TTL but expires nothing by
    /// default, leaving each item's own <c>ttl</c> in control.
    /// </para>
    /// <para>
    /// Partitioning by scope keeps every operation for one key inside a single logical partition,
    /// which is what makes the store's create-or-conditionally-replace protocol atomic. Because
    /// scope is derived from the actor or tenant, keys spread evenly across partitions in
    /// multi-tenant hosts. A host that resolves every request to one shared scope — for example by
    /// mounting the middleware before authentication, so everything falls back to
    /// <c>anonymous</c> — would concentrate all traffic on one partition and hit its throughput
    /// limit.
    /// </para>
    /// </remarks>
    /// <param name="database">Database to create the container in.</param>
    /// <param name="containerId">Container id. Defaults to <c>idempotency</c>.</param>
    /// <param name="throughput">
    /// Optional manual throughput in RU/s. Leave <c>null</c> to inherit database-level or
    /// autoscale throughput.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The existing or newly created container.</returns>
    public static async Task<Container> CreateIfNotExistsAsync(
        Database database,
        string containerId = "idempotency",
        int? throughput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        var properties = new ContainerProperties(containerId, PartitionKeyPath)
        {
            DefaultTimeToLive = -1,
        };

        var response = await database.CreateContainerIfNotExistsAsync(
            properties, throughput, cancellationToken: cancellationToken).ConfigureAwait(false);

        return response.Container;
    }
}