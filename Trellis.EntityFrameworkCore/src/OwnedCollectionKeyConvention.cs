namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// Convention that marks the domain-assigned primary key of an owned collection (<c>OwnsMany</c>)
/// child as <see cref="ValueGenerated.Never"/>.
/// </summary>
/// <remarks>
/// <para>
/// EF Core defaults a key to store-generated (<see cref="ValueGenerated.OnAdd"/>). For an owned
/// collection whose child carries a key the domain assigns, that default is wrong in two ways:
/// </para>
/// <list type="bullet">
/// <item><description>
/// An integer key becomes an IDENTITY column, so persisting the domain-assigned value throws
/// <c>SqlException 544</c> ("Cannot insert explicit value for identity column ... when
/// IDENTITY_INSERT is set to OFF").
/// </description></item>
/// <item><description>
/// A non-default key on a child added to an already-loaded parent is read as an existing row, so EF
/// emits <c>UPDATE ... WHERE Id = @id</c> (zero rows) and throws a spurious
/// <see cref="DbUpdateConcurrencyException"/>.
/// </description></item>
/// </list>
/// <para>
/// Marking the key <see cref="ValueGenerated.Never"/> tells EF the application supplies it, which
/// fixes both. The configuration is applied while the model is built — when ownership is established
/// and when the child's primary key is set — rather than at finalization, so provider conventions
/// (for example SQL Server's IDENTITY strategy) observe it before they decide store generation. It
/// is applied at the convention configuration source, so an explicit <c>ValueGeneratedOnAdd()</c> in
/// the model keeps store generation (opt-out preserved). EF's shadow surrogate keys (for example the
/// synthetic ordinal of a value-object collection) and the foreign-key columns that tie a child to
/// its owner are left untouched.
/// </para>
/// <para>
/// Registered automatically by <see cref="ModelConfigurationBuilderExtensions.ApplyTrellisConventions"/>.
/// </para>
/// </remarks>
internal sealed class OwnedCollectionKeyConvention
    : IForeignKeyOwnershipChangedConvention,
        IEntityTypePrimaryKeyChangedConvention,
        IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessForeignKeyOwnershipChanged(
        IConventionForeignKeyBuilder relationshipBuilder,
        IConventionContext<bool?> context) =>
        ConfigureDomainAssignedKey(relationshipBuilder.Metadata.DeclaringEntityType);

    /// <inheritdoc />
    public void ProcessEntityTypePrimaryKeyChanged(
        IConventionEntityTypeBuilder entityTypeBuilder,
        IConventionKey? newPrimaryKey,
        IConventionKey? previousPrimaryKey,
        IConventionContext<IConventionKey> context) =>
        ConfigureDomainAssignedKey(entityTypeBuilder.Metadata);

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
            ConfigureDomainAssignedKey(entityType);
    }

    private static void ConfigureDomainAssignedKey(IConventionEntityType entityType)
    {
        var ownership = entityType.FindOwnership();
        if (ownership is null || ownership.IsUnique)
            return;

        var primaryKey = entityType.FindPrimaryKey();
        if (primaryKey is null)
            return;

        foreach (var property in primaryKey.Properties)
        {
            if (property.IsShadowProperty() || ownership.Properties.Contains(property))
                continue;

            if (property.ValueGenerated == ValueGenerated.Never)
                continue;

            property.Builder.ValueGenerated(ValueGenerated.Never);
        }
    }
}