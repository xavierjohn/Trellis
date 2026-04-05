namespace Trellis.Testing;

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

/// <summary>
/// Reflection-based helpers for setting source-generated <see cref="Maybe{T}"/>
/// backing fields in test scenarios. Works on the CLR object directly —
/// no DbContext needed for the mutation itself.
/// </summary>
/// <remarks>
/// <para>Use in integration tests to backdate time-dependent properties (e.g., <c>SubmittedAt</c>)
/// without raw SQL. Also usable in unit tests with <see cref="Trellis.Testing.Fakes.FakeRepository{TAggregate,TId}"/>.</para>
/// <para>These helpers are test-only. Do NOT use in production code.</para>
/// </remarks>
public static class AggregateTestMutator
{
    /// <summary>
    /// Sets the source-generated backing field of a <see cref="Maybe{T}"/> property via reflection.
    /// Returns the entity for fluent chaining.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TValue">The inner value type of the Maybe.</typeparam>
    /// <param name="entity">The entity to mutate.</param>
    /// <param name="propertySelector">A lambda selecting the <see cref="Maybe{T}"/> property (e.g., <c>o => o.SubmittedAt</c>).</param>
    /// <param name="value">The value to set, or <c>null</c> to set to <see cref="Maybe{T}.None"/>.</param>
    /// <returns>The same entity for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// order.SetMaybeField(o => o.SubmittedAt, DateTime.UtcNow.AddDays(-8))
    ///      .SetMaybeField(o => o.ShippedAt, DateTime.UtcNow.AddDays(-5));
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("Uses reflection to set source-generated backing fields. Not AOT-compatible — test-only.")]
    public static TEntity SetMaybeField<TEntity, TValue>(
        this TEntity entity,
        Expression<Func<TEntity, Maybe<TValue>>> propertySelector,
        TValue? value)
        where TEntity : class
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(propertySelector);
        var fieldName = ResolveMaybeBackingFieldName(propertySelector);
        var field = FindField(entity.GetType(), fieldName)
            ?? throw new InvalidOperationException(
                $"Backing field '{fieldName}' not found on '{entity.GetType().Name}'. " +
                "Ensure the Maybe<T> property is declared as 'partial' so the source generator emits the backing field.");
        field.SetValue(entity, value);
        return entity;
    }

    /// <summary>
    /// Clears a <see cref="Maybe{T}"/> property's backing field to <c>null</c>
    /// (equivalent to <see cref="Maybe{T}.None"/>).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TValue">The inner value type of the Maybe.</typeparam>
    /// <param name="entity">The entity to mutate.</param>
    /// <param name="propertySelector">A lambda selecting the <see cref="Maybe{T}"/> property.</param>
    /// <returns>The same entity for fluent chaining.</returns>
    [RequiresUnreferencedCode("Uses reflection to set source-generated backing fields. Not AOT-compatible — test-only.")]
    public static TEntity ClearMaybeField<TEntity, TValue>(
        this TEntity entity,
        Expression<Func<TEntity, Maybe<TValue>>> propertySelector)
        where TEntity : class
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(propertySelector);
        var fieldName = ResolveMaybeBackingFieldName(propertySelector);
        var field = FindField(entity.GetType(), fieldName)
            ?? throw new InvalidOperationException(
                $"Backing field '{fieldName}' not found on '{entity.GetType().Name}'.");
        field.SetValue(entity, null);
        return entity;
    }

    // Same _camelCase convention as MaybePartialPropertyGenerator / MaybeFieldNaming
    private static string ResolveMaybeBackingFieldName<TEntity, TValue>(
        Expression<Func<TEntity, Maybe<TValue>>> selector)
        where TValue : notnull
    {
        var body = selector.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u)
            body = u.Operand;

        if (body is not MemberExpression { Member: PropertyInfo property })
            throw new ArgumentException(
                "Expression must be a direct Maybe<T> property access, e.g. o => o.SubmittedAt.",
                nameof(selector));

        var name = property.Name;
        return name.Length == 1
            ? $"_{char.ToLowerInvariant(name[0])}"
            : $"_{char.ToLowerInvariant(name[0])}{name[1..]}";
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Test-only helper. Backing fields are generated code, always present at runtime.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only helper. Backing fields are generated code, always present at runtime.")]
    private static FieldInfo? FindField([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicFields)] Type type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f is not null) return f;
        }

        return null;
    }
}
