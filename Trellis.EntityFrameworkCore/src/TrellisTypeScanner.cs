namespace Trellis.EntityFrameworkCore;

internal enum TrellisValueObjectCategory
{
    Scalar,
    Symbolic,
}

internal readonly record struct TrellisValueObjectInfo(Type ProviderType, TrellisValueObjectCategory Category);

/// <summary>
/// Determines whether a CLR type is a Trellis value object and classifies it by category.
/// </summary>
internal static class TrellisTypeScanner
{
    private static readonly Type s_scalarValueType = typeof(IScalarValue<,>);
    private static readonly Type s_requiredEnumType = typeof(RequiredEnum<>);
    private static readonly Type s_valueObjectType = typeof(ValueObject);

    /// <summary>
    /// Returns the provider type and category for the specified Trellis value object,
    /// or <see langword="null"/> if the type is not a supported Trellis value object.
    /// </summary>
    internal static TrellisValueObjectInfo? FindValueObject(Type type)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == s_requiredEnumType)
                return new(typeof(string), TrellisValueObjectCategory.Symbolic);

            current = current.BaseType;
        }

        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == s_scalarValueType)
                return new(iface.GetGenericArguments()[1], TrellisValueObjectCategory.Scalar);
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the type is a concrete composite value object —
    /// extends <see cref="ValueObject"/> but does not implement <c>IScalarValue</c>
    /// or extend <c>RequiredEnum</c>.
    /// </summary>
    internal static bool IsCompositeValueObject(Type type) =>
        !type.IsAbstract
        && s_valueObjectType.IsAssignableFrom(type)
        && FindValueObject(type) is null;
}