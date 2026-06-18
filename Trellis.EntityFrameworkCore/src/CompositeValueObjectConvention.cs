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
    /// After the model is built, reconciles composite value object owned navigations with EF Core's
    /// table-splitting column naming. EF Core already prefixes owned columns with the owner
    /// navigation name (e.g., <c>ShippingAddress_City</c>); this convention removes the bare column
    /// names <see cref="MaybeConvention"/> stamps on <see cref="Maybe{T}"/> scalar backing fields
    /// (which would otherwise bypass that prefix), marks <see cref="Maybe{T}"/> composites nullable,
    /// and hands nested <see cref="Money"/> navigations the chained prefix for <see cref="MoneyConvention"/>.
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
                    // Required composite: EF Core prefixes the owned columns; clear any bare
                    // Maybe<T> names and hand nested Money its chained prefix.
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
    /// Reconciles a composite owned type with EF Core's table-splitting column naming. For
    /// <b>required</b> composites that table-split into their owner (navigated by the public property
    /// name) EF Core already prefixes owned columns with the owner navigation name (e.g.,
    /// <c>ShippingAddress_Street</c>), chaining through nested owners, so this method only removes the
    /// bare column names <see cref="MaybeConvention"/> stamps on <see cref="Maybe{T}"/> scalar backing
    /// fields (which would otherwise bypass that prefix) and leaves plain scalars to EF Core. A
    /// composite that maps to its <b>own</b> table (owned collection, <c>ToTable</c>, or the
    /// nested-owned separate-table fallback) is left untouched — no prefix applies there and
    /// <see cref="MaybeConvention"/>'s clean <c>{PropertyName}</c> is already correct (clearing it
    /// would leak the raw <c>_camelCase</c> backing-field name). For <b>optional</b> table-splitting
    /// composites (<paramref name="optional"/> is <see langword="true"/>) the navigation is a
    /// <see cref="Maybe{T}"/> backing field, so EF Core would prefix with the field name; this
    /// method instead sets the clean public-name prefix explicitly and marks the columns nullable.
    /// Nested <see cref="Money"/> navigations receive the chained <paramref name="prefix"/> via an
    /// annotation that <see cref="MoneyConvention"/> reads. Removal uses the convention configuration
    /// source, so user-configured column names survive.
    /// </summary>
    private void ConfigureOwnedColumns(
        IConventionEntityType ownedEntityType, string prefix, bool optional,
        HashSet<IConventionEntityType> processed)
    {
        if (!processed.Add(ownedEntityType))
            return;

        var sharesOwnerTable = SharesTableWithOwner(ownedEntityType);

        foreach (var property in ownedEntityType.GetDeclaredProperties())
        {
            if (property.IsShadowProperty())
                continue;

            if (optional)
            {
                // Optional composites table-split through a Maybe<T> backing-field navigation, so
                // EF Core would prefix columns with the field name (e.g. _shippingAddress_City).
                // Set the clean public-name prefix explicitly and mark the column nullable.
                property.Builder.IsRequired(false);
                property.Builder.HasAnnotation(
                    RelationalAnnotationNames.ColumnName,
                    $"{prefix}_{property.Name}");
            }
            else if (sharesOwnerTable)
            {
                // Required composite that table-splits into its owner: EF Core already produces the
                // right {nav}_ prefix once any bare name is gone, so drop the bare column name
                // MaybeConvention stamps on a Maybe<T> backing field (which would otherwise bypass
                // that prefix). No-op for plain scalars (no annotation) and for user-configured
                // names (higher config source).
                property.Builder.HasNoAnnotation(RelationalAnnotationNames.ColumnName);
            }
            // else: the owned VO maps to its OWN table (owned collection, ToTable, or the
            // nested-owned separate-table fallback). EF Core's navigation prefix does not apply
            // there, and MaybeConvention's clean {PropertyName} for a Maybe<T> backing field is
            // already the correct column — leave it untouched. Clearing it would leak the raw
            // _camelCase backing-field name (e.g. _subDivisionName instead of SubDivisionName).
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

            // At a table boundary (this composite owns its own table) the column namespace resets:
            // nested Money / composites table-split into THIS table and are named relative to it,
            // not the cross-table owner chain (which would wrongly yield e.g. Contact_Fee in the
            // separate table instead of the table-local Fee).
            var nestedPrefix = sharesOwnerTable
                ? $"{prefix}_{nestedNavigation.Name}"
                : nestedNavigation.Name;

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
    /// Returns <see langword="true"/> when <paramref name="ownedEntityType"/> table-splits into its
    /// owner (shares the owner's table). Owned collections, <c>ToTable</c>-separated owned types, and
    /// the nested-owned separate-table fallback map to their own table, where EF Core's
    /// <c>{navigation}_</c> prefix does not apply and the convention must leave the existing column
    /// names (including <see cref="MaybeConvention"/>'s clean <c>{PropertyName}</c>) untouched.
    /// </summary>
    private static bool SharesTableWithOwner(IConventionEntityType ownedEntityType)
    {
        var ownership = ownedEntityType.FindOwnership();
        if (ownership is null)
            return false;

        var ownedTable = StoreObjectIdentifier.Create(ownedEntityType, StoreObjectType.Table);
        var ownerTable = StoreObjectIdentifier.Create(ownership.PrincipalEntityType, StoreObjectType.Table);
        return ownedTable is { } owned && ownerTable is { } owner && owned == owner;
    }

    /// <summary>
    /// Annotation key used to pass a chained column-name prefix for a <b>required</b> nested
    /// <see cref="Money"/> navigation from this convention to <see cref="MoneyConvention"/>,
    /// without implying optionality (unlike <see cref="MaybeConvention.MaybeOwnedPropertyNameAnnotation"/>).
    /// </summary>
    internal const string OwnedColumnPrefixAnnotation = "Trellis:OwnedColumnPrefix";
}