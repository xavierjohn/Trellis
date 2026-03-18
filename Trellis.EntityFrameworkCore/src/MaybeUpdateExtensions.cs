namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;

/// <summary>
/// ExecuteUpdate helpers for setting and clearing mapped <see cref="Maybe{T}"/> properties via CLR selectors.
/// </summary>
public static class MaybeUpdateExtensions
{
    /// <summary>
    /// Sets a <see cref="Maybe{T}"/> property to a value inside an <c>ExecuteUpdate</c> call.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>.</typeparam>
    /// <param name="updateSettersBuilder">The update builder.</param>
    /// <param name="propertySelector">An expression selecting the <see cref="Maybe{T}"/> property.</param>
    /// <param name="value">The value to assign.</param>
    /// <returns>The same update builder for chaining additional Maybe update helpers.</returns>
    public static UpdateSettersBuilder<TEntity> SetMaybeValue<TEntity, TInner>(
        this UpdateSettersBuilder<TEntity> updateSettersBuilder,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
        TInner value)
        where TEntity : class
        where TInner : notnull
    {
        ArgumentNullException.ThrowIfNull(updateSettersBuilder);
        ArgumentNullException.ThrowIfNull(propertySelector);
        ArgumentNullException.ThrowIfNull(value);

        InvokeSetProperty(updateSettersBuilder, propertySelector, value, clearValue: false);
        return updateSettersBuilder;
    }

    /// <summary>
    /// Clears a <see cref="Maybe{T}"/> property to None inside an <c>ExecuteUpdate</c> call.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TInner">The type wrapped in <see cref="Maybe{T}"/>.</typeparam>
    /// <param name="updateSettersBuilder">The update builder.</param>
    /// <param name="propertySelector">An expression selecting the <see cref="Maybe{T}"/> property.</param>
    /// <returns>The same update builder for chaining additional Maybe update helpers.</returns>
    public static UpdateSettersBuilder<TEntity> SetMaybeNone<TEntity, TInner>(
        this UpdateSettersBuilder<TEntity> updateSettersBuilder,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector)
        where TEntity : class
        where TInner : notnull
    {
        ArgumentNullException.ThrowIfNull(updateSettersBuilder);
        ArgumentNullException.ThrowIfNull(propertySelector);

        InvokeSetProperty<TEntity, TInner>(updateSettersBuilder, propertySelector, default, clearValue: true);
        return updateSettersBuilder;
    }

    private static void InvokeSetProperty<TEntity, TInner>(
        UpdateSettersBuilder<TEntity> updateSettersBuilder,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
        TInner? value,
        bool clearValue)
        where TEntity : class
        where TInner : notnull
    {
        var descriptor = MaybePropertyResolver.Resolve(propertySelector);
        var propertyLambda = MaybePropertyResolver.BuildStorageMemberLambda(propertySelector);
        var propertyType = descriptor.StoreType;

        if (!clearValue && typeof(TInner).IsValueType)
        {
            var constantValue = CreateStoreValue<TInner>(propertyType, value!);
            var method = SetPropertyMethodCache<TEntity>.ConstantValueDefinition.MakeGenericMethod(propertyType);

            method.Invoke(updateSettersBuilder, [propertyLambda, constantValue]);
            return;
        }

        var valueLambda = BuildValueLambda<TEntity, TInner>(propertySelector.Parameters[0], descriptor, value, clearValue);
        var expressionMethod = SetPropertyMethodCache<TEntity>.ExpressionValueDefinition.MakeGenericMethod(propertyType);

        expressionMethod.Invoke(updateSettersBuilder, [propertyLambda, valueLambda]);
    }

    private static object? CreateStoreValue<TInner>(Type storeType, TInner value)
        where TInner : notnull
    {
        if (!storeType.IsValueType || Nullable.GetUnderlyingType(storeType) is null)
            return value;

        return Activator.CreateInstance(storeType, value);
    }

    private static LambdaExpression BuildValueLambda<TEntity, TInner>(
        ParameterExpression parameter,
        MaybePropertyDescriptor descriptor,
        TInner? value,
        bool clearValue)
        where TEntity : class
        where TInner : notnull
    {
        Expression body = clearValue
            ? Expression.Constant(null, descriptor.StoreType)
            : typeof(TInner).IsValueType
                ? Expression.Convert(Expression.Constant(value, typeof(TInner)), descriptor.StoreType)
                : Expression.Constant(value, descriptor.StoreType);

        var delegateType = typeof(Func<,>).MakeGenericType(typeof(TEntity), descriptor.StoreType);
        return Expression.Lambda(delegateType, body, parameter);
    }

    private static class SetPropertyMethodCache<TEntity> where TEntity : class
    {
        internal static readonly MethodInfo ExpressionValueDefinition = typeof(UpdateSettersBuilder<TEntity>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method =>
                method.Name == nameof(UpdateSettersBuilder<TEntity>.SetProperty)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 2
                && method.GetParameters()[1].ParameterType.IsGenericType
                && method.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>));

        internal static readonly MethodInfo ConstantValueDefinition = typeof(UpdateSettersBuilder<TEntity>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method =>
                method.Name == nameof(UpdateSettersBuilder<TEntity>.SetProperty)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 2
                && !method.GetParameters()[1].ParameterType.IsGenericType);
    }
}