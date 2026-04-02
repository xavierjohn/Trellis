namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Trellis.Primitives;

/// <summary>
/// Convention that automatically registers composite <see cref="ValueObject"/> types
/// (types deriving from <see cref="ValueObject"/> but not implementing <c>IScalarValue</c>)
/// as EF Core owned types.
/// </summary>
/// <remarks>
/// <para>
/// Composite value objects discovered during assembly scanning are registered as owned types
/// during model initialization (<see cref="IModelInitializedConvention"/>). This enables
/// EF Core to automatically create ownership navigations for properties of these types
/// without requiring explicit <c>OwnsOne</c> configuration.
/// </para>
/// <para>
/// For composite value objects used with <see cref="Maybe{T}"/>, two storage strategies are used:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Table-splitting</b> (no nested owned navigations): all owned-type columns are marked nullable
/// and column names use the original property name as prefix. Optionality is expressed via all-null columns.
/// </item>
/// <item>
/// <b>Separate table</b> (nested owned navigations present): the owned type is mapped to its own table
/// named <c>{OwnerTypeName}_{PropertyName}</c>. Columns remain NOT NULL; optionality is expressed by
/// the presence or absence of a row. This avoids EF Core's restriction on optional dependents with
/// nested owned types in table-splitting.
/// </item>
/// </list>
/// <para>
/// <see cref="Money"/> is a composite value object but has its own dedicated
/// <see cref="MoneyConvention"/> with specialized column naming and precision.
/// This convention skips Money during finalization to avoid conflicting with it.
/// </para>
/// <para>
/// Explicit <c>OwnsOne</c> configuration in <c>OnModelCreating</c> takes precedence;
/// convention-level annotations never override explicit-level configuration.
/// </para>
/// </remarks>
internal sealed class CompositeValueObjectConvention(IReadOnlySet<Type> compositeTypes)
    : IModelInitializedConvention, IModelFinalizingConvention
{
    private static readonly Type s_moneyType = typeof(Money);

    /// <summary>
    /// Registers all discovered composite value object types as owned so that EF Core's
    /// built-in <c>NavigationDiscoveryConvention</c> creates ownership relationships
    /// instead of regular navigations.
    /// </summary>
    public void ProcessModelInitialized(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var type in compositeTypes)
            modelBuilder.Owned(type);
    }

    /// <summary>
    /// After the model is built, configures nullable columns and correct column-name prefix
    /// for <see cref="Maybe{T}"/> properties where T is a composite value object.
    /// Required composite value object properties use EF Core's default owned-type column naming.
    /// </summary>
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            foreach (var navigation in entityType.GetDeclaredNavigations())
            {
                if (!navigation.TargetEntityType.IsOwned())
                    continue;

                // Only act on types we discovered as composite VOs
                if (!compositeTypes.Contains(navigation.TargetEntityType.ClrType))
                    continue;

                // Skip Money — MoneyConvention handles it with specialized column naming
                if (navigation.TargetEntityType.ClrType == s_moneyType)
                    continue;

                // Check if this is a Maybe<T> navigation (created by MaybeConvention).
                // Required composites use EF Core's default owned-type column naming.
                var maybePropertyName = navigation.FindAnnotation(
                    MaybeConvention.MaybeOwnedPropertyNameAnnotation)?.Value as string;
                if (maybePropertyName is null)
                    continue;

                // EF Core validation rejects optional dependents with nested owned types in
                // table-splitting because all-null columns make entity existence ambiguous.
                // Also, IsRequired(false) throws on non-nullable value-type properties.
                // In both cases, split to a separate table where the row's existence
                // indicates presence — no need to mark columns nullable.
                var hasNestedOwned = navigation.TargetEntityType.GetDeclaredNavigations()
                    .Any(n => n.TargetEntityType.IsOwned());
                var hasNonNullableValueType = navigation.TargetEntityType.GetDeclaredProperties()
                    .Any(p => !p.IsShadowProperty() && p.ClrType.IsValueType && Nullable.GetUnderlyingType(p.ClrType) is null);
                if (hasNestedOwned || hasNonNullableValueType)
                {
                    var ownerTypeName = entityType.ClrType?.Name ?? entityType.Name;
                    var tableName = $"{ownerTypeName}_{maybePropertyName}";
                    navigation.TargetEntityType.Builder.HasAnnotation(
                        RelationalAnnotationNames.TableName, tableName);
                    continue;
                }

                ConfigureOptionalOwnedColumns(navigation.TargetEntityType, maybePropertyName, visited: null);
            }
        }
    }

    /// <summary>
    /// Marks all declared properties on the owned type as nullable and sets column names
    /// using the original property name as the prefix (e.g., <c>ShippingAddress_Street</c>
    /// instead of <c>_shippingAddress_Street</c>). Recurses into nested owned navigations
    /// to propagate optionality through the entire owned graph.
    /// </summary>
    private void ConfigureOptionalOwnedColumns(
        IConventionEntityType ownedEntityType, string propertyName,
        HashSet<IConventionEntityType>? visited)
    {
        visited ??= [];
        if (!visited.Add(ownedEntityType))
            return;

        foreach (var property in ownedEntityType.GetDeclaredProperties())
        {
            if (property.IsShadowProperty())
                continue;

            property.Builder.IsRequired(false);
            property.Builder.HasAnnotation(
                RelationalAnnotationNames.ColumnName,
                $"{propertyName}_{property.Name}");
        }

        // Propagate optionality to nested owned navigations
        foreach (var nestedNavigation in ownedEntityType.GetDeclaredNavigations())
        {
            if (!nestedNavigation.TargetEntityType.IsOwned())
                continue;

            var nestedPrefix = $"{propertyName}_{nestedNavigation.Name}";

            // Propagate the Maybe annotation so MoneyConvention (and other dedicated
            // conventions) see the nested navigation as optional and use the chained prefix.
            nestedNavigation.Builder.HasAnnotation(
                MaybeConvention.MaybeOwnedPropertyNameAnnotation, nestedPrefix);

            // For non-Money composites, recurse directly. Money is handled by MoneyConvention
            // which reads the propagated annotation.
            if (nestedNavigation.TargetEntityType.ClrType != s_moneyType
                && compositeTypes.Contains(nestedNavigation.TargetEntityType.ClrType))
            {
                ConfigureOptionalOwnedColumns(nestedNavigation.TargetEntityType, nestedPrefix, visited);
            }
        }
    }
}
