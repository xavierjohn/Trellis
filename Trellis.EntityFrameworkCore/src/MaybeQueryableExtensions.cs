namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// LINQ query extensions for <see cref="Maybe{T}"/> properties backed by generated nullable storage members.
/// These methods rewrite the expression tree to target the underlying <c>_camelCase</c> storage member
/// via <see cref="EF.Property{TProperty}"/>, enabling EF Core to translate the query to SQL.
/// </summary>
/// <remarks>
/// <para>
/// Because the <see cref="MaybeConvention"/> ignores the <see cref="Maybe{T}"/> CLR property,
/// EF Core cannot translate direct LINQ references to it.
/// These extension methods provide a strongly-typed alternative to raw <c>EF.Property</c> calls.
/// </para>
/// <code>
/// // Instead of:
/// context.Customers.Where(c => EF.Property&lt;PhoneNumber?&gt;(c, "_phone") == null)
///
/// // Use:
/// context.Customers.WhereNone(c => c.Phone)
/// </code>
/// </remarks>
public static class MaybeQueryableExtensions
{
    private static readonly MethodInfo OrderByMethodDefinition = GetQueryableOrderingMethod(nameof(Queryable.OrderBy));
    private static readonly MethodInfo OrderByDescendingMethodDefinition = GetQueryableOrderingMethod(nameof(Queryable.OrderByDescending));
    private static readonly MethodInfo ThenByMethodDefinition = GetQueryableOrderingMethod(nameof(Queryable.ThenBy));
    private static readonly MethodInfo ThenByDescendingMethodDefinition = GetQueryableOrderingMethod(nameof(Queryable.ThenByDescending));

    /// <summary>
    /// Filters the query to entities where the <see cref="Maybe{T}"/> property has no value
    /// (mapped storage field IS NULL).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="propertySelector">
    /// An expression selecting the <see cref="Maybe{T}"/> property (e.g., <c>c =&gt; c.Phone</c>).
    /// </param>
    /// <returns>A filtered queryable where the mapped storage field is NULL.</returns>
    /// <example>
    /// <code>
    /// var customersWithoutPhone = await context.Customers
    ///     .WhereNone(c => c.Phone)
    ///     .ToListAsync(ct);
    /// </code>
    /// </example>
    public static IQueryable<TEntity> WhereNone<TEntity, TInner>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector)
        where TEntity : class
        where TInner : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(propertySelector);

        var predicate = BuildNullCheck<TEntity, TInner>(propertySelector, isNullCheck: true);
        return source.Where(predicate);
    }

    /// <summary>
    /// Filters the query to entities where the <see cref="Maybe{T}"/> property has a value
    /// (mapped storage field IS NOT NULL).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="propertySelector">
    /// An expression selecting the <see cref="Maybe{T}"/> property (e.g., <c>c =&gt; c.Phone</c>).
    /// </param>
    /// <returns>A filtered queryable where the mapped storage field is NOT NULL.</returns>
    /// <example>
    /// <code>
    /// var customersWithPhone = await context.Customers
    ///     .WhereHasValue(c => c.Phone)
    ///     .ToListAsync(ct);
    /// </code>
    /// </example>
    public static IQueryable<TEntity> WhereHasValue<TEntity, TInner>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector)
        where TEntity : class
        where TInner : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(propertySelector);

        var predicate = BuildNullCheck<TEntity, TInner>(propertySelector, isNullCheck: false);
        return source.Where(predicate);
    }

    /// <summary>
    /// Filters the query to entities where the <see cref="Maybe{T}"/> property equals the
    /// specified value (mapped storage field = <paramref name="value"/>).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="propertySelector">
    /// An expression selecting the <see cref="Maybe{T}"/> property (e.g., <c>c =&gt; c.Phone</c>).
    /// </param>
    /// <param name="value">The value to compare against.</param>
    /// <returns>A filtered queryable where the mapped storage field equals the value.</returns>
    /// <example>
    /// <code>
    /// var phone = PhoneNumber.Create("+15550100");
    /// var matches = await context.Customers
    ///     .WhereEquals(c => c.Phone, phone)
    ///     .ToListAsync(ct);
    /// </code>
    /// </example>
    public static IQueryable<TEntity> WhereEquals<TEntity, TInner>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
        TInner value)
        where TEntity : class
        where TInner : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(propertySelector);

        var descriptor = MaybePropertyResolver.Resolve(propertySelector);
        var parameter = propertySelector.Parameters[0];
        var efProperty = MaybePropertyResolver.BuildEfPropertyAccess(parameter, descriptor);

        Expression valueExpr = typeof(TInner).IsValueType
            ? Expression.Convert(Expression.Constant(value), descriptor.StoreType)
            : Expression.Constant(value, descriptor.StoreType);

        var equals = Expression.Equal(efProperty, valueExpr);
        var lambda = Expression.Lambda<Func<TEntity, bool>>(equals, parameter);

        return source.Where(lambda);
    }

    /// <summary>
    /// Filters the query to entities where the <see cref="Maybe{T}"/> property is less than
    /// the specified value (mapped storage field &lt; <paramref name="value"/>).
    /// Entities where the property has no value (NULL) are excluded.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>. Must implement <see cref="IComparable{T}"/>.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="propertySelector">
    /// An expression selecting the <see cref="Maybe{T}"/> property (e.g., <c>o =&gt; o.SubmittedAt</c>).
    /// </param>
    /// <param name="value">The value to compare against.</param>
    /// <returns>A filtered queryable where the mapped storage field is less than the value.</returns>
    /// <example>
    /// Find orders submitted before a cutoff date:
    /// <code>
    /// var cutoff = DateTime.UtcNow.AddDays(-7);
    /// var overdueOrders = await context.Orders
    ///     .WhereLessThan(o => o.SubmittedAt, cutoff)
    ///     .ToListAsync(ct);
    /// </code>
    /// </example>
    public static IQueryable<TEntity> WhereLessThan<TEntity, TInner>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
        TInner value)
        where TEntity : class
        where TInner : notnull, IComparable<TInner> =>
        WhereComparison(source, propertySelector, value, ExpressionType.LessThan);

    /// <summary>
    /// Filters the query to entities where the <see cref="Maybe{T}"/> property is less than or equal to
    /// the specified value (mapped storage field &lt;= <paramref name="value"/>).
    /// Entities where the property has no value (NULL) are excluded.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>. Must implement <see cref="IComparable{T}"/>.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="propertySelector">
    /// An expression selecting the <see cref="Maybe{T}"/> property.
    /// </param>
    /// <param name="value">The value to compare against.</param>
    /// <returns>A filtered queryable where the mapped storage field is less than or equal to the value.</returns>
    public static IQueryable<TEntity> WhereLessThanOrEqual<TEntity, TInner>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
        TInner value)
        where TEntity : class
        where TInner : notnull, IComparable<TInner> =>
        WhereComparison(source, propertySelector, value, ExpressionType.LessThanOrEqual);

    /// <summary>
    /// Filters the query to entities where the <see cref="Maybe{T}"/> property is greater than
    /// the specified value (mapped storage field &gt; <paramref name="value"/>).
    /// Entities where the property has no value (NULL) are excluded.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>. Must implement <see cref="IComparable{T}"/>.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="propertySelector">
    /// An expression selecting the <see cref="Maybe{T}"/> property.
    /// </param>
    /// <param name="value">The value to compare against.</param>
    /// <returns>A filtered queryable where the mapped storage field is greater than the value.</returns>
    public static IQueryable<TEntity> WhereGreaterThan<TEntity, TInner>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
        TInner value)
        where TEntity : class
        where TInner : notnull, IComparable<TInner> =>
        WhereComparison(source, propertySelector, value, ExpressionType.GreaterThan);

    /// <summary>
    /// Filters the query to entities where the <see cref="Maybe{T}"/> property is greater than or equal to
    /// the specified value (mapped storage field &gt;= <paramref name="value"/>).
    /// Entities where the property has no value (NULL) are excluded.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>. Must implement <see cref="IComparable{T}"/>.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="propertySelector">
    /// An expression selecting the <see cref="Maybe{T}"/> property.
    /// </param>
    /// <param name="value">The value to compare against.</param>
    /// <returns>A filtered queryable where the mapped storage field is greater than or equal to the value.</returns>
    public static IQueryable<TEntity> WhereGreaterThanOrEqual<TEntity, TInner>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
        TInner value)
        where TEntity : class
        where TInner : notnull, IComparable<TInner> =>
        WhereComparison(source, propertySelector, value, ExpressionType.GreaterThanOrEqual);

    private static IQueryable<TEntity> WhereComparison<TEntity, TInner>(
        IQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
        TInner value,
        ExpressionType comparisonType)
        where TEntity : class
        where TInner : notnull, IComparable<TInner>
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(propertySelector);

        var descriptor = MaybePropertyResolver.Resolve(propertySelector);
        var parameter = propertySelector.Parameters[0];
        var efProperty = MaybePropertyResolver.BuildEfPropertyAccess(parameter, descriptor);

        Expression valueExpr = typeof(TInner).IsValueType
            ? Expression.Convert(Expression.Constant(value), descriptor.StoreType)
            : Expression.Constant(value, descriptor.StoreType);

        var comparison = Expression.MakeBinary(comparisonType, efProperty, valueExpr);
        var lambda = Expression.Lambda<Func<TEntity, bool>>(comparison, parameter);

        return source.Where(lambda);
    }

    /// <summary>
    /// Orders the query ascending by the mapped storage field for the selected <see cref="Maybe{T}"/> property.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="propertySelector">An expression selecting the <see cref="Maybe{T}"/> property.</param>
    /// <returns>An ordered queryable.</returns>
    public static IOrderedQueryable<TEntity> OrderByMaybe<TEntity, TInner>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector)
        where TEntity : class
        where TInner : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(propertySelector);

        return ApplyOrdering(source, propertySelector, OrderByMethodDefinition);
    }

    /// <summary>
    /// Orders the query descending by the mapped storage field for the selected <see cref="Maybe{T}"/> property.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="propertySelector">An expression selecting the <see cref="Maybe{T}"/> property.</param>
    /// <returns>An ordered queryable.</returns>
    public static IOrderedQueryable<TEntity> OrderByMaybeDescending<TEntity, TInner>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector)
        where TEntity : class
        where TInner : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(propertySelector);

        return ApplyOrdering(source, propertySelector, OrderByDescendingMethodDefinition);
    }

    /// <summary>
    /// Adds an ascending secondary ordering using the mapped storage field for the selected <see cref="Maybe{T}"/> property.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>.</typeparam>
    /// <param name="source">The ordered queryable source.</param>
    /// <param name="propertySelector">An expression selecting the <see cref="Maybe{T}"/> property.</param>
    /// <returns>An ordered queryable.</returns>
    public static IOrderedQueryable<TEntity> ThenByMaybe<TEntity, TInner>(
        this IOrderedQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector)
        where TEntity : class
        where TInner : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(propertySelector);

        return ApplyOrdering(source, propertySelector, ThenByMethodDefinition);
    }

    /// <summary>
    /// Adds a descending secondary ordering using the mapped storage field for the selected <see cref="Maybe{T}"/> property.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>.</typeparam>
    /// <param name="source">The ordered queryable source.</param>
    /// <param name="propertySelector">An expression selecting the <see cref="Maybe{T}"/> property.</param>
    /// <returns>An ordered queryable.</returns>
    public static IOrderedQueryable<TEntity> ThenByMaybeDescending<TEntity, TInner>(
        this IOrderedQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector)
        where TEntity : class
        where TInner : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(propertySelector);

        return ApplyOrdering(source, propertySelector, ThenByDescendingMethodDefinition);
    }

    private static Expression<Func<TEntity, bool>> BuildNullCheck<TEntity, TInner>(
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
        bool isNullCheck)
        where TEntity : class
        where TInner : notnull
    {
        var descriptor = MaybePropertyResolver.Resolve(propertySelector);
        var parameter = propertySelector.Parameters[0];
        var efProperty = MaybePropertyResolver.BuildEfPropertyAccess(parameter, descriptor);

        var nullConstant = Expression.Constant(null, descriptor.StoreType);
        var comparison = isNullCheck
            ? Expression.Equal(efProperty, nullConstant)
            : Expression.NotEqual(efProperty, nullConstant);

        return Expression.Lambda<Func<TEntity, bool>>(comparison, parameter);
    }

    private static IOrderedQueryable<TEntity> ApplyOrdering<TEntity, TInner>(
        IQueryable<TEntity> source,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
        MethodInfo methodDefinition)
        where TEntity : class
        where TInner : notnull
    {
        var keySelector = MaybePropertyResolver.BuildStorageMemberLambda(propertySelector);
        var method = methodDefinition.MakeGenericMethod(typeof(TEntity), keySelector.ReturnType);

        return (IOrderedQueryable<TEntity>)method.Invoke(null, [source, keySelector])!;
    }

    private static MethodInfo GetQueryableOrderingMethod(string methodName) =>
        typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method.Name == methodName
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 2);
}