namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Trellis.Primitives;

/// <summary>
/// Convention that automatically maps <see cref="Money"/> properties as owned types
/// with standardized column naming.
/// </summary>
/// <remarks>
/// <para>
/// For a property named <c>{Name}</c> of type <see cref="Money"/>, two columns are generated:
/// </para>
/// <list type="bullet">
/// <item><c>{Name}</c> column — <c>decimal(18,3)</c> for the monetary amount (scale 3 covers all ISO 4217 minor units)</item>
/// <item><c>{Name}Currency</c> column — <c>nvarchar(3)</c> for the ISO 4217 currency code</item>
/// </list>
/// <para>
/// Money is registered as an owned type during model initialization
/// (<see cref="IModelInitializedConvention"/>), which instructs EF Core to treat all
/// Money properties as ownership navigations from the start.
/// Column naming and precision are applied during model finalization
/// (<see cref="IModelFinalizingConvention"/>).
/// </para>
/// <para>
/// This applies to <see cref="Money"/> properties declared directly on entities and to
/// <see cref="Money"/> properties declared on owned entity types, including items inside
/// <c>OwnsMany</c> collections.
/// </para>
/// <para>
/// Explicit <c>OwnsOne</c> configuration in <c>OnModelCreating</c> takes precedence;
/// convention-level annotations never override explicit-level configuration.
/// </para>
/// </remarks>
internal sealed class MoneyConvention : IModelInitializedConvention, IModelFinalizingConvention
{
    private static readonly Type s_moneyType = typeof(Money);

    /// <summary>
    /// Marks <see cref="Money"/> as an owned type so that EF Core's built-in
    /// <c>NavigationDiscoveryConvention</c> creates ownership relationships
    /// instead of regular navigations.
    /// </summary>
    public void ProcessModelInitialized(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context) =>
        modelBuilder.Owned(s_moneyType);

    /// <summary>
    /// After the model is built, configures column names, precision, and max-length
    /// for all owned <see cref="Money"/> entity types.
    /// </summary>
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            foreach (var navigation in entityType.GetDeclaredNavigations())
            {
                if (navigation.TargetEntityType.ClrType != s_moneyType || !navigation.TargetEntityType.IsOwned())
                    continue;

                // Check if this navigation was created by MaybeConvention for a Maybe<Money> property.
                // If so, use the original property name for column naming and mark columns nullable.
                var maybePropertyName = navigation.FindAnnotation(MaybeConvention.MaybeOwnedPropertyNameAnnotation)?.Value as string;
                var columnPrefix = maybePropertyName ?? navigation.Name;

                ConfigureMoneyColumns(navigation.TargetEntityType, columnPrefix, isOptional: maybePropertyName is not null);
            }
        }
    }

    private static void ConfigureMoneyColumns(IConventionEntityType entityType, string navigationName, bool isOptional)
    {
        // Amount → column "{NavigationName}", decimal(18,3)
        // Scale 3 accommodates all ISO 4217 minor units: 0 (JPY), 2 (USD), 3 (BHD/KWD/OMR/TND)
        var amount = entityType.FindProperty(nameof(Money.Amount));
        if (amount is not null)
        {
            amount.Builder.HasAnnotation(RelationalAnnotationNames.ColumnName, navigationName);
            amount.Builder.HasPrecision(18);
            amount.Builder.HasScale(3);
            if (isOptional)
                amount.Builder.IsRequired(false);
        }

        // Currency → column "{NavigationName}Currency", max-length 3
        var currency = entityType.FindProperty(nameof(Money.Currency));
        if (currency is not null)
        {
            currency.Builder.HasAnnotation(RelationalAnnotationNames.ColumnName, navigationName + "Currency");
            currency.Builder.HasMaxLength(3);
            if (isOptional)
                currency.Builder.IsRequired(false);
        }
    }
}