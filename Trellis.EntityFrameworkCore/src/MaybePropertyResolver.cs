namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

internal readonly record struct MaybePropertyDescriptor(
    PropertyInfo Property,
    string PropertyName,
    string StorageMemberName,
    Type InnerType,
    Type StoreType);

internal static class MaybePropertyResolver
{
    private static readonly Type s_maybeOpenGenericType = typeof(Maybe<>);

    internal static IEnumerable<MaybePropertyDescriptor> GetMaybeProperties(Type clrType) =>
        clrType
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(IsMaybeProperty)
            .Select(CreateDescriptor);

    internal static bool IsMaybeProperty(PropertyInfo property) =>
        IsMaybeType(property.PropertyType);

    internal static bool IsMaybeType(Type type) =>
        type.IsGenericType
        && type.GetGenericTypeDefinition() == s_maybeOpenGenericType;

    internal static MaybePropertyDescriptor Resolve(LambdaExpression propertySelector)
    {
        var parameter = propertySelector.Parameters.Single();

        if (TryGetProperty(propertySelector.Body, parameter, out var property) && IsMaybeProperty(property))
            return CreateDescriptor(property);

        throw new ArgumentException(
            "Expression must be a direct Maybe<T> property access on the lambda parameter (e.g., e => e.Phone).",
            nameof(propertySelector));
    }

    internal static IReadOnlyList<string> ResolveMappedPropertyNames(LambdaExpression propertySelector) =>
        ResolveReferencedProperties(propertySelector)
            .Select(ResolveMappedPropertyName)
            .ToArray();

    internal static IReadOnlyList<PropertyInfo> ResolveReferencedProperties(LambdaExpression propertySelector)
    {
        var properties = new List<PropertyInfo>();

        CollectReferencedProperties(
            propertySelector.Body,
            propertySelector.Parameters.Single(),
            properties,
            nameof(propertySelector));

        if (properties.Count == 0)
        {
            throw new ArgumentException(
                "Expression must be a direct property access or anonymous object of direct property accesses on the lambda parameter (e.g., e => e.Phone or e => new { e.Status, e.SubmittedAt }).",
                nameof(propertySelector));
        }

        return properties;
    }

    internal static LambdaExpression BuildStorageMemberLambda<TEntity, TInner>(
        Expression<Func<TEntity, Maybe<TInner>>> propertySelector)
        where TEntity : class
        where TInner : notnull
    {
        var descriptor = Resolve(propertySelector);
        var body = BuildEfPropertyAccess(propertySelector.Parameters[0], descriptor);
        var delegateType = typeof(Func<,>).MakeGenericType(typeof(TEntity), descriptor.StoreType);

        return Expression.Lambda(delegateType, body, propertySelector.Parameters);
    }

    internal static Expression BuildEfPropertyAccess(
        ParameterExpression parameter,
        MaybePropertyDescriptor descriptor)
    {
        var genericMethod = s_efPropertyMethodInfo.MakeGenericMethod(descriptor.StoreType);

        return Expression.Call(
            genericMethod,
            Expression.Convert(parameter, typeof(object)),
            Expression.Constant(descriptor.StorageMemberName));
    }

    internal static string ResolveMappedPropertyName(PropertyInfo property) =>
        IsMaybeProperty(property)
            ? CreateDescriptor(property).StorageMemberName
            : property.Name;

    internal static MaybePropertyDescriptor Describe(PropertyInfo property) =>
        CreateDescriptor(property);

    internal static FieldInfo? FindStorageMember(Type clrType, MaybePropertyDescriptor descriptor)
    {
        for (var currentType = descriptor.Property.DeclaringType; currentType is not null; currentType = currentType.BaseType)
        {
            if (!currentType.IsAssignableFrom(clrType))
                continue;

            var field = currentType.GetField(
                descriptor.StorageMemberName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field is not null)
                return field;
        }

        return null;
    }

    private static readonly MethodInfo s_efPropertyMethodInfo =
        typeof(EF).GetMethod(nameof(EF.Property))!;

    private static MaybePropertyDescriptor CreateDescriptor(PropertyInfo property)
    {
        if (!IsMaybeProperty(property))
            throw new ArgumentException($"Property '{property.Name}' is not of type Maybe<T>.", nameof(property));

        var innerType = property.PropertyType.GetGenericArguments()[0];
        var storageMemberName = MaybeFieldNaming.ToStorageMemberName(property.Name);
        var storeType = innerType.IsValueType
            ? typeof(Nullable<>).MakeGenericType(innerType)
            : innerType;

        return new MaybePropertyDescriptor(property, property.Name, storageMemberName, innerType, storeType);
    }

    private static void CollectReferencedProperties(
        Expression expression,
        ParameterExpression parameter,
        ICollection<PropertyInfo> properties,
        string parameterName)
    {
        expression = UnwrapConvert(expression);

        switch (expression)
        {
            case MemberExpression { Member: PropertyInfo property } memberExpression
                when IsDirectPropertyAccess(memberExpression, parameter, property):
                properties.Add(property);
                return;

            case NewExpression newExpression:
                foreach (var argument in newExpression.Arguments)
                    CollectReferencedProperties(argument, parameter, properties, parameterName);

                return;

            default:
                throw new ArgumentException(
                    "Expression must be a direct property access or anonymous object of direct property accesses on the lambda parameter.",
                    parameterName);
        }
    }

    private static bool TryGetProperty(
        Expression expression,
        ParameterExpression parameter,
        out PropertyInfo property)
    {
        expression = UnwrapConvert(expression);

        if (expression is MemberExpression { Member: PropertyInfo propertyInfo } memberExpression
            && IsDirectPropertyAccess(memberExpression, parameter, propertyInfo))
        {
            property = propertyInfo;
            return true;
        }

        property = null!;
        return false;
    }

    private static bool IsDirectPropertyAccess(
        MemberExpression memberExpression,
        ParameterExpression parameter,
        PropertyInfo property)
    {
        var target = UnwrapConvert(memberExpression.Expression!);

        return ReferenceEquals(target, parameter)
               && property.DeclaringType?.IsAssignableFrom(parameter.Type) == true;
    }

    private static Expression UnwrapConvert(Expression expression)
    {
        while (expression is UnaryExpression
            {
                NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked,
                Operand: { } operand
            })
        {
            expression = operand;
        }

        return expression;
    }
}