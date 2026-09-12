namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;

/// <summary>
/// Defines ordering, boundary extraction and lexicographic seeking together.
/// Keys must be non-null and stable, and the final ordering must be unique.
/// Provider translation and comparison semantics must be verified for the selected keys.
/// </summary>
public sealed class SeekDefinition<T, TState> where T : class where TState : notnull
{
    private readonly Func<IQueryable<T>, IOrderedQueryable<T>> _order;
    private readonly Expression<Func<T, TState>> _extract;
    private readonly Func<ParameterExpression, TState, Expression> _after;
    private readonly Func<ParameterExpression, TState, Expression> _equal;

    internal SeekDefinition(
        Func<IQueryable<T>, IOrderedQueryable<T>> order,
        Expression<Func<T, TState>> extract,
        Func<ParameterExpression, TState, Expression> after,
        Func<ParameterExpression, TState, Expression> equal,
        ICursorCodec<TState> codec)
    {
        _order = order;
        _extract = extract;
        _after = after;
        _equal = equal;
        Codec = codec;
    }

    /// <summary>The codec for this definition's complete boundary state.</summary>
    public ICursorCodec<TState> Codec { get; }

    /// <summary>Replaces serialization without changing ordering, for example to bind query context or protect tokens.</summary>
    public SeekDefinition<T, TState> WithCodec(ICursorCodec<TState> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        return new(_order, _extract, _after, _equal, codec);
    }

    /// <summary>Adds an ascending key using its built-in scalar codec.</summary>
    public SeekDefinition<T, (TState Primary, TKey Secondary)> ThenAscending<TKey>(Expression<Func<T, TKey>> key)
        where TKey : notnull, IComparable<TKey>, IParsable<TKey> =>
        ThenAscending(key, CursorCodec.Scalar<TKey>());

    /// <summary>Adds an ascending key using an explicit codec.</summary>
    public SeekDefinition<T, (TState Primary, TKey Secondary)> ThenAscending<TKey>(
        Expression<Func<T, TKey>> key, ICursorCodec<TKey> codec)
        where TKey : notnull, IComparable<TKey> => Then(key, codec, descending: false);

    /// <summary>Adds a descending key using its built-in scalar codec.</summary>
    public SeekDefinition<T, (TState Primary, TKey Secondary)> ThenDescending<TKey>(Expression<Func<T, TKey>> key)
        where TKey : notnull, IComparable<TKey>, IParsable<TKey> =>
        ThenDescending(key, CursorCodec.Scalar<TKey>());

    /// <summary>Adds a descending key using an explicit codec.</summary>
    public SeekDefinition<T, (TState Primary, TKey Secondary)> ThenDescending<TKey>(
        Expression<Func<T, TKey>> key, ICursorCodec<TKey> codec)
        where TKey : notnull, IComparable<TKey> => Then(key, codec, descending: true);

    private SeekDefinition<T, (TState Primary, TKey Secondary)> Then<TKey>(
        Expression<Func<T, TKey>> key, ICursorCodec<TKey> codec, bool descending)
        where TKey : notnull, IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(codec);
        return new(
            source => descending ? _order(source).ThenByDescending(key) : _order(source).ThenBy(key),
            SeekDefinition.Join(_extract, key),
            (parameter, state) => Expression.OrElse(
                _after(parameter, state.Primary),
                Expression.AndAlso(
                    _equal(parameter, state.Primary),
                    SeekDefinition.Compare(key, parameter, state.Secondary, descending))),
            (parameter, state) => Expression.AndAlso(
                _equal(parameter, state.Primary),
                SeekDefinition.Equal(key, parameter, state.Secondary)),
            CursorCodec.Composite(Codec, codec));
    }

    internal IQueryable<T> Apply(IQueryable<T> source, Maybe<TState> boundary)
    {
        var ordered = _order(source);
        if (!boundary.TryGetValue(out var state))
            return ordered;
        var parameter = Expression.Parameter(typeof(T), "row");
        return ordered.Where(Expression.Lambda<Func<T, bool>>(_after(parameter, state), parameter));
    }

    internal IQueryable<SeekRow<T, TState>> Project(IQueryable<T> source)
    {
        Expression<Func<T, TState, SeekRow<T, TState>>> factory = (item, state) => new SeekRow<T, TState>(item, state);
        var body = SeekDefinition.Substitute(factory.Body, factory.Parameters[0], _extract.Parameters[0]);
        body = SeekDefinition.Substitute(body, factory.Parameters[1], _extract.Body);
        return source.Select(Expression.Lambda<Func<T, SeekRow<T, TState>>>(body, _extract.Parameters));
    }
}

internal sealed record SeekRow<T, TState>(T Item, TState State);

/// <summary>Factories for storage-translatable seek definitions. Does not perform client-side query fallback.</summary>
public static class SeekDefinition
{
    /// <summary>Starts an ascending ordering with a built-in scalar codec.</summary>
    public static SeekDefinition<T, TKey> Ascending<T, TKey>(Expression<Func<T, TKey>> key)
        where T : class
        where TKey : notnull, IComparable<TKey>, IParsable<TKey> =>
        Ascending(key, CursorCodec.Scalar<TKey>());

    /// <summary>Starts an ascending ordering with an explicit codec.</summary>
    public static SeekDefinition<T, TKey> Ascending<T, TKey>(Expression<Func<T, TKey>> key, ICursorCodec<TKey> codec)
        where T : class
        where TKey : notnull, IComparable<TKey> => Create(key, codec, descending: false);

    /// <summary>Starts a descending ordering with a built-in scalar codec.</summary>
    public static SeekDefinition<T, TKey> Descending<T, TKey>(Expression<Func<T, TKey>> key)
        where T : class
        where TKey : notnull, IComparable<TKey>, IParsable<TKey> =>
        Descending(key, CursorCodec.Scalar<TKey>());

    /// <summary>Starts a descending ordering with an explicit codec.</summary>
    public static SeekDefinition<T, TKey> Descending<T, TKey>(Expression<Func<T, TKey>> key, ICursorCodec<TKey> codec)
        where T : class
        where TKey : notnull, IComparable<TKey> => Create(key, codec, descending: true);

    private static SeekDefinition<T, TKey> Create<T, TKey>(
        Expression<Func<T, TKey>> key, ICursorCodec<TKey> codec, bool descending)
        where T : class
        where TKey : notnull, IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(codec);
        return new(
            source => descending ? source.OrderByDescending(key) : source.OrderBy(key),
            key,
            (parameter, state) => Compare(key, parameter, state, descending),
            (parameter, state) => Equal(key, parameter, state),
            codec);
    }

    internal static Expression Equal<T, TKey>(Expression<Func<T, TKey>> key, ParameterExpression parameter, TKey value) =>
        Expression.Equal(Replace(key, parameter), Parameterize(value));

    internal static Expression Compare<T, TKey>(
        Expression<Func<T, TKey>> key, ParameterExpression parameter, TKey value, bool descending)
        where TKey : IComparable<TKey>
    {
        var left = Replace(key, parameter);
        var right = Parameterize(value);
        var type = typeof(TKey);
        if (type == typeof(int) || type == typeof(long) || type == typeof(decimal) || type == typeof(double)
            || type == typeof(short) || type == typeof(float) || type == typeof(byte) || type == typeof(uint)
            || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte) || type == typeof(DateTime)
            || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(DateOnly) || type == typeof(TimeOnly))
            return descending ? Expression.LessThan(left, right) : Expression.GreaterThan(left, right);

        var method = type.GetMethod(nameof(IComparable<int>.CompareTo), BindingFlags.Public | BindingFlags.Instance,
            binder: null, types: [type], modifiers: null)
            ?? throw new NotSupportedException($"Seek key '{type.Name}' must expose a provider-translatable CompareTo method.");
        var comparison = Expression.Call(left, method, right);
        return descending
            ? Expression.LessThan(comparison, Expression.Constant(0))
            : Expression.GreaterThan(comparison, Expression.Constant(0));
    }

    internal static Expression<Func<T, (TFirst Primary, TSecond Secondary)>> Join<T, TFirst, TSecond>(
        Expression<Func<T, TFirst>> first, Expression<Func<T, TSecond>> second)
    {
        Expression<Func<TFirst, TSecond, (TFirst, TSecond)>> factory = (a, b) => new ValueTuple<TFirst, TSecond>(a, b);
        var body = Substitute(factory.Body, factory.Parameters[0], first.Body);
        body = Substitute(body, factory.Parameters[1], Replace(second, first.Parameters[0]));
        return Expression.Lambda<Func<T, (TFirst, TSecond)>>(body, first.Parameters);
    }

    private static Expression Replace<T, TKey>(Expression<Func<T, TKey>> key, ParameterExpression parameter) =>
        Substitute(key.Body, key.Parameters[0], parameter);

    internal static Expression Substitute(Expression body, ParameterExpression parameter, Expression replacement) =>
        new ParameterSubstitution(parameter, replacement).Visit(body);

    private static MemberExpression Parameterize<TKey>(TKey value) =>
        Expression.Property(Expression.Constant(new BoundaryValue<TKey>(value)), nameof(BoundaryValue<TKey>.Value));

    private sealed class BoundaryValue<TKey>(TKey value)
    {
        public TKey Value { get; } = value;
    }

    private sealed class ParameterSubstitution(ParameterExpression original, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == original ? replacement : node;
    }
}
