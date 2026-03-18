namespace Trellis.Asp.Validation;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Trellis.Asp.ModelBinding;

/// <summary>
/// Helper class for detecting and working with <see cref="IScalarValue{TSelf, TPrimitive}"/> types.
/// Centralizes reflection logic to avoid duplication across converters, model binders, and configuration.
/// </summary>
internal static class ScalarValueTypeHelper
{
    /// <summary>
    /// Checks if the given type implements <see cref="IScalarValue{TSelf, TPrimitive}"/>
    /// where TSelf is the type itself (CRTP pattern).
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is a scalar value object, false otherwise.</returns>
    public static bool IsScalarValue([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return GetScalarValueInterface(type) is not null;
    }

    /// <summary>
    /// Gets the <see cref="IScalarValue{TSelf, TPrimitive}"/> interface implemented by the type,
    /// or null if the type doesn't implement it correctly.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>The interface type if found, null otherwise.</returns>
    /// <remarks>
    /// This method verifies the CRTP pattern by ensuring the first generic argument
    /// of the interface matches the type itself.
    /// </remarks>
    public static Type? GetScalarValueInterface([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                                i.GetGenericTypeDefinition() == typeof(IScalarValue<,>) &&
                                i.GetGenericArguments()[0] == type);
    }

    /// <summary>
    /// Gets the primitive type (TPrimitive) from a scalar value object type.
    /// </summary>
    /// <param name="valueType">The value object type.</param>
    /// <returns>The primitive type, or null if the type is not a scalar value object.</returns>
    public static Type? GetPrimitiveType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type valueType)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        var interfaceType = GetScalarValueInterface(valueType);
        return interfaceType?.GetGenericArguments()[1];
    }

    /// <summary>
    /// Creates an instance of a generic type parameterized with a value object type and its primitive type.
    /// </summary>
    /// <typeparam name="TResult">The expected result type (usually an interface like IModelBinder or JsonConverter).</typeparam>
    /// <param name="genericTypeDefinition">The open generic type (e.g., typeof(SomeClass&lt;,&gt;)).</param>
    /// <param name="valueObjectType">The value object type (first type argument).</param>
    /// <param name="primitiveType">The primitive type (second type argument).</param>
    /// <returns>An instance of the constructed generic type, or null if creation fails.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "MakeGenericType is used with known converter/binder types")]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Types are preserved by ASP.NET Core serialization infrastructure")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Not compatible with Native AOT")]
    public static TResult? CreateGenericInstance<TResult>(
        Type genericTypeDefinition,
        Type valueObjectType,
        Type primitiveType)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(genericTypeDefinition);
        ArgumentNullException.ThrowIfNull(valueObjectType);
        ArgumentNullException.ThrowIfNull(primitiveType);

        try
        {
            var constructedType = genericTypeDefinition.MakeGenericType(valueObjectType, primitiveType);
            return Activator.CreateInstance(constructedType) as TResult;
        }
        catch (Exception)
        {
            // Return null if type construction fails (e.g., constraint violations)
            return null;
        }
    }

    /// <summary>
    /// Invokes <c>TryCreate(string?, string?)</c> on a scalar value type and returns
    /// the validation errors as a dictionary suitable for <c>Results.ValidationProblem</c>.
    /// </summary>
    /// <param name="scalarValueType">The scalar value type to validate against.</param>
    /// <param name="rawValue">The raw string value that failed binding.</param>
    /// <param name="parameterName">The parameter/field name for error messages.</param>
    /// <returns>
    /// A dictionary of field names to error messages if validation fails, or null if
    /// the <c>TryCreate</c> method is not found or the value is valid.
    /// </returns>
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "TryCreate is always present on IScalarValue types generated by the source generator.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Not compatible with Native AOT — reflection-based fallback only.")]
    public static IDictionary<string, string[]>? GetValidationErrors(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)] Type scalarValueType,
        string? rawValue,
        string? parameterName)
    {
        var stringTryCreateMethod = scalarValueType.GetMethod(
            "TryCreate",
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(string), typeof(string)],
            null);

        if (stringTryCreateMethod is not null)
            return InvokeTryCreate(stringTryCreateMethod, [rawValue, parameterName], parameterName);

        var primitiveType = GetPrimitiveType(scalarValueType);
        if (primitiveType is null)
            return null;

        var conversionResult = ConvertRawValueToPrimitive(rawValue, primitiveType, parameterName);
        if (conversionResult is IDictionary<string, string[]> conversionErrors)
            return conversionErrors;

        var primitiveTryCreateMethod = scalarValueType.GetMethod(
            "TryCreate",
            BindingFlags.Public | BindingFlags.Static,
            null,
            [primitiveType, typeof(string)],
            null);

        if (primitiveTryCreateMethod is null)
            return null;

        return InvokeTryCreate(primitiveTryCreateMethod, [conversionResult, parameterName], parameterName);
    }

    private static IDictionary<string, string[]>? InvokeTryCreate(MethodInfo tryCreateMethod, object?[] args, string? parameterName)
    {
        object? result;
        try
        {
            result = tryCreateMethod.Invoke(null, args);
        }
        catch (Exception)
        {
            return null;
        }

        return ExtractErrors(result, parameterName);
    }

    private static IDictionary<string, string[]>? ExtractErrors(object? result, string? parameterName)
    {
        if (result is not IResult { IsFailure: true } failure)
            return null;

        if (failure.Error is ValidationError validationError)
            return validationError.ToDictionary();

        var fieldName = parameterName ?? string.Empty;
        return new Dictionary<string, string[]>
        {
            [fieldName] = [failure.Error.Detail]
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Primitive converter generic method is invoked only with known scalar primitive types discovered at runtime.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection-based validation fallback is not Native AOT compatible.")]
    private static object? ConvertRawValueToPrimitive(string? rawValue, Type primitiveType, string? parameterName)
    {
        var convertMethod = typeof(PrimitiveConverter)
            .GetMethod(nameof(PrimitiveConverter.ConvertToPrimitive), BindingFlags.Public | BindingFlags.Static);

        if (convertMethod is null)
            return null;

        object? conversionResult;
        try
        {
            conversionResult = convertMethod.MakeGenericMethod(primitiveType).Invoke(null, [rawValue]);
        }
        catch (Exception)
        {
            return null;
        }

        if (conversionResult is IResult { IsFailure: true } failure)
        {
            var fieldName = parameterName ?? string.Empty;
            var detail = string.IsNullOrEmpty(rawValue)
                ? $"'{fieldName}' is required."
                : failure.Error.Detail;

            return new Dictionary<string, string[]>
            {
                [fieldName] = [detail]
            };
        }

        var valueProperty = conversionResult?.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        return valueProperty?.GetValue(conversionResult);
    }

    /// <summary>
    /// Checks if the given type is <see cref="Maybe{T}"/> where T implements <see cref="IScalarValue{TSelf, TPrimitive}"/>.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is Maybe wrapping a scalar value object, false otherwise.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Inner type of Maybe<T> is preserved by JSON serialization infrastructure")]
    public static bool IsMaybeScalarValue(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var innerType = GetMaybeInnerType(type);
        return innerType is not null && IsScalarValue(innerType);
    }

    /// <summary>
    /// Gets the inner type T from <see cref="Maybe{T}"/>, or null if the type is not a Maybe.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>The inner type T, or null if the type is not <see cref="Maybe{T}"/>.</returns>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
    [UnconditionalSuppressMessage("Trimming", "IL2063", Justification = "Generic type arguments of Maybe<T> are preserved by JSON serialization infrastructure")]
    public static Type? GetMaybeInnerType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!type.IsGenericType)
            return null;

        if (type.GetGenericTypeDefinition() != typeof(Maybe<>))
            return null;

        return type.GetGenericArguments()[0];
    }

    /// <summary>
    /// Gets the primitive type from <see cref="Maybe{T}"/> where T implements <see cref="IScalarValue{TSelf, TPrimitive}"/>.
    /// </summary>
    /// <param name="type">The Maybe type to inspect.</param>
    /// <returns>The primitive type, or null if the type is not a Maybe wrapping a scalar value.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Value object types are preserved by JSON serialization infrastructure")]
    public static Type? GetMaybePrimitiveType(Type type)
    {
        var innerType = GetMaybeInnerType(type);
        return innerType is null ? null : GetPrimitiveType(innerType);
    }
}