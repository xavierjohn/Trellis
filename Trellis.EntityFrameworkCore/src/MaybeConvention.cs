namespace Trellis.EntityFrameworkCore;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

/// <summary>
/// Convention that automatically maps <see cref="Maybe{T}"/> properties by discovering their
/// source-generated private nullable storage members (<c>_camelCase</c>) and configuring
/// EF Core to use the storage member as a nullable column.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Maybe{T}"/> is a <c>readonly struct</c>, which means EF Core cannot mark it
/// as nullable directly (<c>IsRequired(false)</c> throws <c>InvalidOperationException</c>).
/// This convention works with the <c>MaybePartialPropertyGenerator</c> source generator,
/// which emits private nullable storage members for <c>partial Maybe&lt;T&gt;</c> properties.
/// </para>
/// <para>
/// For each CLR property of type <c>Maybe&lt;T&gt;</c> found on an entity type, this convention:
/// </para>
/// <list type="number">
/// <item>Ignores the <c>Maybe&lt;T&gt;</c> CLR property (EF Core cannot map structs as nullable)</item>
/// <item>Maps the private <c>_camelCase</c> storage member as an EF property</item>
/// <item>Marks the storage member as optional (<c>IsRequired(false)</c>)</item>
/// <item>Configures field-only access mode</item>
/// <item>Sets the column name to the original property name (e.g., <c>Phone</c> instead of <c>_phone</c>)</item>
/// </list>
/// <para>
/// When <c>T</c> is a composite owned type (e.g., <c>Money</c>), the convention creates
/// an optional ownership navigation via the backing field instead of a scalar column.
/// All columns in the owned type are marked nullable, and column names use the original
/// property name as the prefix (matching <see cref="MoneyConvention"/> naming).
/// </para>
/// <para>
/// User code with the source generator:
/// </para>
/// <code>
/// public partial class Customer
/// {
///     public CustomerId Id { get; set; } = null!;
///     public partial Maybe&lt;PhoneNumber&gt; Phone { get; set; }
///     public partial Maybe&lt;Money&gt; Discount { get; set; }
/// }
/// </code>
/// <para>
/// No <c>MaybeProperty()</c> call is needed in <c>OnModelCreating</c> — the convention handles
/// everything automatically.
/// </para>
/// </remarks>
internal sealed class MaybeConvention : IModelFinalizingConvention
{
    /// <summary>
    /// After the model is built, discovers all <see cref="Maybe{T}"/> CLR properties on entity types
    /// and configures their generated storage members as nullable database columns.
    /// </summary>
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            if (entityType.ClrType is null)
                continue;

            foreach (var maybeProperty in MaybePropertyResolver.GetMaybeProperties(entityType.ClrType))
            {
                // Verify the generated storage member exists (source generator should have created it)
                var storageMember = MaybePropertyResolver.FindStorageMember(entityType.ClrType, maybeProperty);

                if (storageMember is null)
                    throw new InvalidOperationException(
                        $"Cannot map Maybe<T> property '{maybeProperty.PropertyName}' on entity '{entityType.ClrType.Name}'. " +
                        $"Expected generated storage member '{maybeProperty.StorageMemberName}' was not found. " +
                        "Declare the property as partial so the Trellis.EntityFrameworkCore.Generator can emit the storage member, or configure the storage-member property explicitly before model finalization.");

                // Owned types (e.g., Money) need ownership navigations, not scalar columns.
                if (modelBuilder.Metadata.IsOwned(maybeProperty.InnerType))
                {
                    ConfigureOwnedMaybe(entityType, maybeProperty, storageMember);
                    continue;
                }

                // Always ignore the Maybe<T> CLR property — EF Core cannot map structs as nullable
                entityType.Builder.Ignore(maybeProperty.PropertyName);

                // Reuse an existing property if earlier model-building steps created it (for example via HasIndex).
                var existingBackingProp = entityType.FindProperty(maybeProperty.StorageMemberName);

                // Map or fetch the storage member as a nullable property.
                var propertyBuilder = existingBackingProp?.Builder
                    ?? entityType.Builder.Property(maybeProperty.StoreType, maybeProperty.StorageMemberName);
                if (propertyBuilder is null)
                    throw new InvalidOperationException(
                        $"Cannot map Maybe<T> property '{maybeProperty.PropertyName}' on entity '{entityType.ClrType.Name}'. " +
                        $"Storage member '{maybeProperty.StorageMemberName}' exists but EF Core could not map it as store type '{maybeProperty.StoreType.Name}'.");

                propertyBuilder.UsePropertyAccessMode(PropertyAccessMode.Field);
                propertyBuilder.IsRequired(false);
                propertyBuilder.HasAnnotation(RelationalAnnotationNames.ColumnName, maybeProperty.PropertyName);
            }
        }
    }

    /// <summary>
    /// Configures a <c>Maybe&lt;T&gt;</c> property where <c>T</c> is an owned type.
    /// Creates an optional ownership navigation via the source-generated backing field.
    /// </summary>
    private static void ConfigureOwnedMaybe(
        IConventionEntityType entityType,
        MaybePropertyDescriptor maybeProperty,
        FieldInfo storageMember)
    {
        // Ignore the Maybe<T> CLR property — EF Core cannot navigate through a struct
        entityType.Builder.Ignore(maybeProperty.PropertyName);

        // Create ownership navigation via the backing field (e.g., Money? _monetaryFinePaid).
        // Column naming and nullable marking are handled by MoneyConvention (registered after
        // MaybeConvention) using the PropertyName annotation we store here.
        var fkBuilder = entityType.Builder.HasOwnership(maybeProperty.InnerType, storageMember);
        if (fkBuilder is null)
            return;

        // Store the original property name so MoneyConvention can use it for column naming
        var navigation = entityType.FindNavigation(storageMember.Name);
        navigation?.Builder.HasAnnotation(MaybeOwnedPropertyNameAnnotation, maybeProperty.PropertyName);
    }

    /// <summary>
    /// Annotation key used to pass the original property name from <see cref="MaybeConvention"/>
    /// to <see cref="MoneyConvention"/> for correct column naming of <c>Maybe&lt;Money&gt;</c> properties.
    /// </summary>
    internal const string MaybeOwnedPropertyNameAnnotation = "Trellis:MaybeOwnedPropertyName";
}