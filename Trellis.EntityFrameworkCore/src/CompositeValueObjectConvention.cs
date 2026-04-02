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
/// For composite value objects used with <see cref="Maybe{T}"/>, the convention also
/// marks all owned-type columns as nullable and fixes column naming to use the original
/// property name as prefix (instead of the source-generated <c>_camelCase</c> backing field name).
/// </para>
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
                // Split to a separate table when nested owned navigations are present.
                // In a separate table, the row's existence indicates presence — no need to
                // mark columns nullable or prefix names; MoneyConvention handles nested Money normally.
                var hasNestedOwned = navigation.TargetEntityType.GetDeclaredNavigations()
                    .Any(n => n.TargetEntityType.IsOwned());
                if (hasNestedOwned)
                {
                    navigation.TargetEntityType.Builder.HasAnnotation(
                        RelationalAnnotationNames.TableName, maybePropertyName);
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
