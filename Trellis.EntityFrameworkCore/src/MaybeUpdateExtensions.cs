namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
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

        Expression<Func<TInner>> capturedValue = () => value;
        InvokeSetProperty(updateSettersBuilder, propertySelector, capturedValue.Body);
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

        InvokeSetProperty(updateSettersBuilder, propertySelector, value: null);
        return updateSettersBuilder;
    }

    private static void InvokeSetProperty<TEntity, TInner>(
        UpdateSettersBuilder<TEntity> updateSettersBuilder,
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
        Expression? value)
        where TEntity : class
        where TInner : notnull
    {
        var descriptor = MaybePropertyResolver.Resolve(propertySelector);

        // Maybe<T> where T is a composite owned type (e.g., Money) cannot be updated via
        // ExecuteUpdate because the backing field is an owned navigation, not a scalar property.
        // Use tracked entity updates (load, modify, SaveChanges) instead.
        if (TrellisTypeScanner.IsCompositeValueObject(descriptor.InnerType))
            throw new InvalidOperationException(
                $"Cannot use ExecuteUpdate on Maybe<{descriptor.InnerType.Name}> property '{descriptor.PropertyName}'. " +
                $"{descriptor.InnerType.Name} is a composite owned type and is mapped as an ownership navigation, not a scalar column. " +
                "Use tracked entity updates (load the entity, set the property, call SaveChangesAsync) instead.");

        var parameter = propertySelector.Parameters[0];

        // SetMaybeValue supplies a TInner expression to match EF's unwrapped setter type.
        // SetMaybeNone uses the storage type to represent null: Nullable<TInner> for structs, TInner for references.
        var effectiveType = value?.Type ?? descriptor.StoreType;

        var propertyLambda = BuildPropertyLambda<TEntity>(parameter, descriptor.StorageMemberName, effectiveType);
        var valueLambda = Expression.Lambda(propertyLambda.Type, value ?? Expression.Constant(null, effectiveType), parameter);
        var expressionMethod = SetPropertyMethodCache<TEntity>.ExpressionValueDefinition.MakeGenericMethod(effectiveType);

        expressionMethod.Invoke(updateSettersBuilder, [propertyLambda, valueLambda]);
    }

    private static LambdaExpression BuildPropertyLambda<TEntity>(
        ParameterExpression parameter,
        string storageMemberName,
        Type effectiveType)
        where TEntity : class
    {
        var efPropertyMethod = s_efPropertyMethodInfo.MakeGenericMethod(effectiveType);
        var body = Expression.Call(
            efPropertyMethod,
            Expression.Convert(parameter, typeof(object)),
            Expression.Constant(storageMemberName));

        var delegateType = typeof(Func<,>).MakeGenericType(typeof(TEntity), effectiveType);
        return Expression.Lambda(delegateType, body, parameter);
    }

    private static readonly MethodInfo s_efPropertyMethodInfo =
        typeof(EF).GetMethod(nameof(EF.Property))!;

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
    }
}