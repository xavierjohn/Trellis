namespace Trellis.EntityFrameworkCore;

using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

/// <summary>
/// Convention that maps constructor-bound (get-only) Trellis value object properties (scalar and symbolic enum).
/// </summary>
/// <remarks>
/// <para>
/// EF Core's property discovery only maps <em>writable</em> members — its <c>PropertyDiscoveryConvention</c>
/// treats a property as a scalar candidate only when it has a usable setter (<c>IsCandidateProperty</c> with
/// <c>needsWrite: true</c>). A <em>settable</em> value-object property is therefore discovered and the scalar
/// converter registered by <see cref="ModelConfigurationBuilderExtensions.ApplyTrellisConventions"/> applies.
/// A <em>get-only</em> property supplied only through the constructor (the idiomatic aggregate/entity shape,
/// e.g. <c>public CustomerId CustomerId { get; }</c> set in the constructor) is skipped by discovery —
/// regardless of assembly or value-converter registration — so EF Core's constructor-binding convention
/// later fails with <c>"Cannot bind '&lt;param&gt;' in '&lt;Type&gt;(...)'"</c>.
/// </para>
/// <para>
/// This convention closes that gap: for every entity type it inspects the constructor parameters whose
/// CLR type is a Trellis value object — scalar (e.g. <c>RequiredGuid</c>) or symbolic enum (<c>RequiredEnum</c>)
/// — and explicitly adds the matching property, so the
/// pre-convention scalar converter applies and the constructor parameter binds — the automated equivalent
/// of an explicit <c>builder.Property(x =&gt; x.CustomerId).HasConversion(...)</c> in <c>OnModelCreating</c>.
/// </para>
/// <para>
/// Registered automatically by <see cref="ModelConfigurationBuilderExtensions.ApplyTrellisConventions"/>.
/// Runs at entity-type-added time, before EF Core's constructor-binding convention finalizes the model.
/// </para>
/// </remarks>
internal sealed class ScalarValueObjectPropertyConvention : IEntityTypeAddedConvention
{
    private const BindingFlags ConstructorFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private const BindingFlags PropertyFlags = BindingFlags.Public | BindingFlags.Instance;

    /// <inheritdoc />
    public void ProcessEntityTypeAdded(
        IConventionEntityTypeBuilder entityTypeBuilder,
        IConventionContext<IConventionEntityTypeBuilder> context)
    {
        var entityType = entityTypeBuilder.Metadata;
        var clrType = entityType.ClrType;

        foreach (var constructor in clrType.GetConstructors(ConstructorFlags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                if (parameter.Name is null)
                    continue;
                if (TrellisTypeScanner.FindValueObject(parameter.ParameterType) is null)
                    continue;

                var property = FindMatchingProperty(clrType, parameter.Name, parameter.ParameterType);
                if (property is null)
                    continue;
                if (entityType.FindProperty(property.Name) is not null)
                    continue;

                entityTypeBuilder.Property(property.PropertyType, property.Name);
            }
        }
    }

    // EF Core binds a constructor parameter to a same-named readable property (case-insensitive).
    private static PropertyInfo? FindMatchingProperty(Type clrType, string parameterName, Type parameterType)
    {
        foreach (var property in clrType.GetProperties(PropertyFlags))
        {
            if (property.PropertyType == parameterType
                && property.GetIndexParameters().Length == 0
                && string.Equals(property.Name, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                return property;
            }
        }

        return null;
    }
}
