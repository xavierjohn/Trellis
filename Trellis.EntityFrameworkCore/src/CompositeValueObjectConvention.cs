namespace Trellis.EntityFrameworkCore;

using System.Reflection;
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
    : IModelInitializedConvention, INavigationAddedConvention, IModelFinalizingConvention
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
    /// Fails fast with an actionable <see cref="TrellisPersistenceMappingException"/> when a composite
    /// value object reached by an ownership navigation has no parameterless constructor for EF Core to
    /// materialize, replacing EF Core's cryptic "No suitable constructor was found" model-build error.
    /// </summary>
    public void ProcessNavigationAdded(
        IConventionNavigationBuilder navigationBuilder,
        IConventionContext<IConventionNavigationBuilder> context)
    {
        var target = navigationBuilder.Metadata.TargetEntityType;
        if (!target.IsOwned())
            return;

        var targetType = target.ClrType;
        if (targetType == s_moneyType || !compositeTypes.Contains(targetType) || HasParameterlessConstructor(targetType))
            return;

        throw new TrellisPersistenceMappingException(MissingConstructorMessage(targetType));
    }

    private static bool HasParameterlessConstructor(Type type) =>
        type.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null) is not null;

    private static string MissingConstructorMessage(Type type) =>
        $"The composite value object '{type.FullName}' is mapped as an EF Core owned type by Trellis " +
        "conventions, but it has no parameterless constructor for EF Core to materialize it. Either " +
        "annotate it with [OwnedEntity] (Trellis.EntityFrameworkCore generates a private parameterless " +
        "constructor), or declare a private parameterless constructor on the type yourself — the latter " +
        "keeps the value object free of any Entity Framework Core dependency. See the composite " +
        "value object recipe (Recipe 13) in trellis-api-cookbook.md.";

    /// <summary>
    /// After the model is built, configures the correct column-name prefix for composite value
    /// object owned navigations so that table-shared composites use the owner navigation name as
    /// the column prefix (e.g., <c>ShippingAddress_City</c>). <see cref="Maybe{T}"/> composites are
    /// additionally marked nullable. This matches EF Core's owned-type table-splitting, explicit
    /// <c>OwnsOne</c>, and avoids bare-name column collisions between two same-type composites.
    /// </summary>
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        var processed = new HashSet<IConventionEntityType>();

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

                // Already handled by a parent composite's recursive prefixing
                if (processed.Contains(navigation.TargetEntityType))
                    continue;

                // Check if this is a Maybe<T> navigation (created by MaybeConvention).
                var maybePropertyName = navigation.FindAnnotation(
                    MaybeConvention.MaybeOwnedPropertyNameAnnotation)?.Value as string;

                if (maybePropertyName is null)
                {
                    // Required composite: prefix owned columns with the navigation name.
                    ConfigureOwnedColumns(
                        navigation.TargetEntityType, navigation.Name, optional: false, processed);
                    continue;
                }

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

                ConfigureOwnedColumns(
                    navigation.TargetEntityType, maybePropertyName, optional: true, processed);
            }
        }
    }

    /// <summary>
    /// Sets owned-type column names using <paramref name="prefix"/> as the navigation prefix
    /// (e.g., <c>ShippingAddress_Street</c>), recursing through nested owned navigations so the
    /// prefix chains through the whole owned graph. When <paramref name="optional"/> is
    /// <see langword="true"/> the columns are also marked nullable (table-splitting for
    /// <see cref="Maybe{T}"/>); otherwise nullability is left untouched. Nested <see cref="Money"/>
    /// navigations receive the chained prefix via an annotation that <see cref="MoneyConvention"/>
    /// reads.
    /// </summary>
    private void ConfigureOwnedColumns(
        IConventionEntityType ownedEntityType, string prefix, bool optional,
        HashSet<IConventionEntityType> processed)
    {
        if (!processed.Add(ownedEntityType))
            return;

        foreach (var property in ownedEntityType.GetDeclaredProperties())
        {
            if (property.IsShadowProperty())
                continue;

            if (optional)
                property.Builder.IsRequired(false);
            property.Builder.HasAnnotation(
                RelationalAnnotationNames.ColumnName,
                $"{prefix}_{property.Name}");
        }

        foreach (var nestedNavigation in ownedEntityType.GetDeclaredNavigations())
        {
            if (!nestedNavigation.TargetEntityType.IsOwned())
                continue;

            // A nested Maybe<owned> navigation already carries MaybeConvention's annotation and is
            // mapped (nullable columns / separate-table fallback / Maybe<Money>) by the outer loop
            // and the dedicated conventions. Leave it to them — do not force it through the parent's
            // required/optional flag or add it to the processed set.
            if (nestedNavigation.FindAnnotation(MaybeConvention.MaybeOwnedPropertyNameAnnotation)?.Value is not null)
                continue;

            var nestedPrefix = $"{prefix}_{nestedNavigation.Name}";

            if (nestedNavigation.TargetEntityType.ClrType == s_moneyType)
            {
                // MoneyConvention runs after this convention and reads the prefix annotation.
                // For optional graphs the Maybe annotation additionally marks Money nullable.
                nestedNavigation.Builder.HasAnnotation(
                    optional
                        ? MaybeConvention.MaybeOwnedPropertyNameAnnotation
                        : OwnedColumnPrefixAnnotation,
                    nestedPrefix);
                continue;
            }

            if (compositeTypes.Contains(nestedNavigation.TargetEntityType.ClrType))
                ConfigureOwnedColumns(nestedNavigation.TargetEntityType, nestedPrefix, optional, processed);
        }
    }

    /// <summary>
    /// Annotation key used to pass a chained column-name prefix for a <b>required</b> nested
    /// <see cref="Money"/> navigation from this convention to <see cref="MoneyConvention"/>,
    /// without implying optionality (unlike <see cref="MaybeConvention.MaybeOwnedPropertyNameAnnotation"/>).
    /// </summary>
    internal const string OwnedColumnPrefixAnnotation = "Trellis:OwnedColumnPrefix";
}