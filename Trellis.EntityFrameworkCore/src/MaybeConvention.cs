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
/// User code with the source generator:
/// </para>
/// <code>
/// public partial class Customer
/// {
///     public CustomerId Id { get; set; } = null!;
///     public partial Maybe&lt;PhoneNumber&gt; Phone { get; set; }
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
}