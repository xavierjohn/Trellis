namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Trellis.Primitives;

/// <summary>
/// Convention that detects common misuses of <see cref="Money"/> and <see cref="Maybe{T}"/>
/// in <c>OnModelCreating</c> and throws a clear, actionable
/// <see cref="InvalidOperationException"/> at startup time instead of leaving them to fail
/// with cryptic errors deep in EF Core's model-validation pipeline.
/// </summary>
/// <remarks>
/// <para>Detected misuses:</para>
/// <list type="bullet">
/// <item>
///   Scalar EF properties whose CLR type is <see cref="Money"/>. <see cref="MoneyConvention"/>
///   maps Money as an owned type; configuring it as a scalar (typically via an explicit
///   <c>builder.Property(x =&gt; x.SomeMoney)</c> call) is never correct.
/// </item>
/// <item>
///   Scalar EF properties whose CLR type is <c>Maybe&lt;T&gt;</c>. <see cref="MaybeConvention"/>
///   maps the source-generated nullable backing field; configuring the public
///   <c>Maybe&lt;T&gt;</c> property directly bypasses that mapping.
/// </item>
/// </list>
/// <para>
/// Runs in <see cref="IModelFinalizingConvention"/> and is registered after
/// <see cref="MaybeConvention"/> and <see cref="MoneyConvention"/> so that any property
/// still bearing one of these CLR types as a scalar at this point reflects an explicit
/// user misuse rather than an in-flight convention transformation.
/// </para>
/// </remarks>
internal sealed class ValueObjectMappingGuardConvention : IModelFinalizingConvention
{
    private static readonly Type s_moneyType = typeof(Money);
    private static readonly Type s_maybeOpenGeneric = typeof(Maybe<>);

    /// <summary>
    /// Walks every entity's declared scalar properties and throws when a
    /// <see cref="Money"/> or <c>Maybe&lt;T&gt;</c> property remains mapped as a scalar
    /// after the Money and Maybe conventions have finished.
    /// </summary>
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var property in entityType.GetDeclaredProperties())
            {
                var clrType = property.ClrType;

                if (clrType == s_moneyType)
                {
                    throw new InvalidOperationException(
                        $"Entity '{entityType.ClrType.Name}' has property '{property.Name}' of type Money mapped as a scalar. " +
                        "Money is mapped automatically by MoneyConvention as an owned type with two columns " +
                        $"('{property.Name}' for Amount, '{property.Name}Currency' for Currency). " +
                        $"Remove any explicit '.Property(x => x.{property.Name})' call from OnModelCreating " +
                        "(the convention handles it). For custom column names, use " +
                        $"'.OwnsOne(x => x.{property.Name}, m => {{ ... }})' explicitly. " +
                        "See: docs/api_reference/trellis-api-efcore.md#money");
                }

                if (clrType.IsGenericType && clrType.GetGenericTypeDefinition() == s_maybeOpenGeneric)
                {
                    var innerName = clrType.GetGenericArguments()[0].Name;
                    throw new InvalidOperationException(
                        $"Entity '{entityType.ClrType.Name}' has property '{property.Name}' of type Maybe<{innerName}> mapped as a scalar. " +
                        "Maybe<T> properties are mapped automatically by MaybeConvention against a source-generated nullable backing field. " +
                        $"Remove any explicit '.Property(x => x.{property.Name})' call from OnModelCreating, " +
                        $"and ensure the property is declared as 'public partial Maybe<{innerName}> {property.Name} {{ get; set; }}' " +
                        "on a 'partial' entity class so the Trellis.EntityFrameworkCore.Generator can emit the storage member. " +
                        "See: docs/api_reference/trellis-api-efcore.md#maybe-properties");
                }
            }
        }
    }
}
