namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// Convention that automatically excludes transient base-class properties from the
/// EF Core model for all entity types that implement <see cref="IAggregate"/>.
/// </summary>
/// <remarks>
/// <para>
/// Aggregate types implement <see cref="IAggregate"/>, and <see cref="IAggregate"/>
/// inherits <see cref="System.ComponentModel.IChangeTracking"/>, which declares
/// <see cref="System.ComponentModel.IChangeTracking.IsChanged"/>. This property reflects
/// in-memory state (uncommitted domain events) and must not be persisted to the database.
/// While EF Core currently skips expression-bodied read-only properties by convention,
/// this convention provides an explicit, version-safe guarantee that transient properties
/// are never mapped — even if a derived aggregate hides the inherited member with
/// <c>new bool IsChanged { get; set; }</c>, which EF Core could otherwise convention-map.
/// </para>
/// <para>
/// Registered automatically by <see cref="ModelConfigurationBuilderExtensions.ApplyTrellisConventions"/>.
/// </para>
/// </remarks>
internal sealed class AggregateTransientPropertyConvention : IModelFinalizingConvention
{
    private const string IsChangedPropertyName = nameof(IAggregate.IsChanged);

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (!typeof(IAggregate).IsAssignableFrom(entityType.ClrType))
                continue;

            entityType.Builder.Ignore(IsChangedPropertyName);
        }
    }
}
