namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// Convention that automatically configures the <see cref="IAggregate.Version"/> property
/// as a concurrency token on all aggregate entity types.
/// </summary>
/// <remarks>
/// <para>
/// This convention enables optimistic concurrency control for all aggregates. When EF Core
/// generates an <c>UPDATE</c> or <c>DELETE</c> statement, it includes
/// <c>WHERE Version = @originalVersion</c>. If another process modified the aggregate
/// since it was loaded, the statement affects zero rows and EF Core throws
/// <see cref="DbUpdateConcurrencyException"/>, which
/// <see cref="DbContextExtensions.SaveChangesResultAsync(DbContext, CancellationToken)"/>
/// maps to <see cref="ConflictError"/>.
/// </para>
/// <para>
/// Registered automatically by <see cref="ModelConfigurationBuilderExtensions.ApplyTrellisConventions"/>.
/// </para>
/// </remarks>
internal sealed class AggregateVersionConvention : IModelFinalizingConvention
{
    private const string VersionPropertyName = nameof(IAggregate.Version);

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (!typeof(IAggregate).IsAssignableFrom(entityType.ClrType))
                continue;

            var property = entityType.FindProperty(VersionPropertyName);
            if (property is null)
                continue;

            property.Builder.IsConcurrencyToken(true);
        }
    }
}
