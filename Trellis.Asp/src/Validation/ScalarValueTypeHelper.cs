namespace Trellis.Asp.Validation;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "Reflection-enabled fallback only. Native AOT returns null before MakeGenericType; callers then rely on source-generated converters/binders.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Reflection-enabled fallback only. Types come from ASP.NET Core metadata or explicit model-binding inputs.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Guarded by RuntimeFeature.IsDynamicCodeSupported; Native AOT returns null before constructing a closed generic type.")]
    public static TResult? CreateGenericInstance<TResult>(
        Type genericTypeDefinition,
        Type valueObjectType,
        Type primitiveType)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(genericTypeDefinition);
        ArgumentNullException.ThrowIfNull(valueObjectType);
        ArgumentNullException.ThrowIfNull(primitiveType);

        if (!RuntimeFeature.IsDynamicCodeSupported)
            return null;

        try
        {
            var constructedType = genericTypeDefinition.MakeGenericType(valueObjectType, primitiveType);
            return Activator.CreateInstance(constructedType) as TResult;
        }
        catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or TypeLoadException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // Covers MakeGenericType (ArgumentException, TypeLoadException, NotSupportedException)
            // and Activator.CreateInstance (MemberAccessException includes MissingMethodException,
            // TargetInvocationException when ctor throws, InvalidOperationException for abstract types)
            return null;
        }
    }

    /// <summary>
    /// Creates an instance of a single-type-argument generic type via reflection, returning null
    /// when dynamic code is not supported or construction fails. Same fail-soft semantics as the
    /// two-type-argument overload above. Used by the Maybe&lt;TPrimitive&gt; binder factory path
    /// where the closed generic takes only the inner primitive type as its single type argument.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "Reflection-enabled fallback only. Native AOT returns null before MakeGenericType; callers then rely on source-generated converters/binders.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Reflection-enabled fallback only. Types come from ASP.NET Core metadata or explicit model-binding inputs.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Guarded by RuntimeFeature.IsDynamicCodeSupported; Native AOT returns null before constructing a closed generic type.")]
    public static TResult? CreateGenericInstance<TResult>(
        Type genericTypeDefinition,
        Type typeArgument)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(genericTypeDefinition);
        ArgumentNullException.ThrowIfNull(typeArgument);

        if (!RuntimeFeature.IsDynamicCodeSupported)
            return null;

        try
        {
            var constructedType = genericTypeDefinition.MakeGenericType(typeArgument);
            return Activator.CreateInstance(constructedType) as TResult;
        }
        catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or TypeLoadException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
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
    /// <remarks>
    /// This is a projection of <see cref="GetValidationError"/>, not a second implementation.
    /// The flattening to strings happens here and only here, so a caller that can carry
    /// structure gets it by calling the structured method instead.
    /// </remarks>
    public static IDictionary<string, string[]>? GetValidationErrors(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)] Type scalarValueType,
        string? rawValue,
        string? parameterName)
    {
        var error = GetValidationError(scalarValueType, rawValue, parameterName);
        return error is null ? null : ToModelStateErrors(error, parameterName);
    }

    /// <summary>
    /// Invokes <c>TryCreate</c> on a scalar value type and returns the failure as a structured
    /// <see cref="Error"/>, preserving reason codes, arguments and per-field pointers.
    /// </summary>
    /// <param name="scalarValueType">The scalar value type to validate against.</param>
    /// <param name="rawValue">The raw string value that failed binding.</param>
    /// <param name="parameterName">The parameter/field name for error messages.</param>
    /// <returns>The validation failure, or <see langword="null"/> when the value is valid or
    /// no usable <c>TryCreate</c> overload exists.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "TryCreate is always present on IScalarValue types generated by the source generator.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Not compatible with Native AOT — reflection-based fallback only.")]
    public static Error? GetValidationError(
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
        {
            var stringError = InvokeTryCreate(stringTryCreateMethod, [rawValue, parameterName]);
            if (stringError is not null || GetPrimitiveType(scalarValueType) == typeof(string))
                return stringError;
        }

        var primitiveType = GetPrimitiveType(scalarValueType);
        if (primitiveType is null)
            return null;

        var conversionResult = ConvertRawValueToPrimitive(rawValue, primitiveType, parameterName, out var conversionError);
        if (conversionError is not null)
            return conversionError;

        var primitiveTryCreateMethod = scalarValueType.GetMethod(
            "TryCreate",
            BindingFlags.Public | BindingFlags.Static,
            null,
            [primitiveType, typeof(string)],
            null);

        if (primitiveTryCreateMethod is null)
            return null;

        return InvokeTryCreate(primitiveTryCreateMethod, [conversionResult, parameterName]);
    }

    /// <summary>
    /// Flattens a structured failure onto the MVC <c>ModelState</c> shape.
    /// </summary>
    public static Dictionary<string, string[]> ToModelStateErrors(Error error, string? parameterName)
    {
        if (error is Error.InvalidInput unprocessable && unprocessable.Fields.Items.Length > 0)
        {
            return unprocessable.Fields
                .Items
                .GroupBy(fv => JsonPointerToMvc.Translate(fv.Field.Path))
                .ToDictionary(g => g.Key, g => g.Select(fv => fv.Detail ?? fv.ReasonCode).ToArray());
        }

        return new Dictionary<string, string[]>
        {
            [parameterName ?? string.Empty] = [error.Detail ?? error.Code]
        };
    }

    private static Error? InvokeTryCreate(MethodInfo tryCreateMethod, object?[] args)
    {
        object? result;
        try
        {
            result = tryCreateMethod.Invoke(null, args);
        }
        catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or TypeLoadException or ArgumentException or InvalidOperationException)
        {
            return null;
        }

        return result is IResult failure && failure.TryGetError(out var failureError) ? failureError : null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Primitive converter generic method is invoked only with known scalar primitive types discovered at runtime.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection-based validation fallback is not Native AOT compatible.")]
    private static object? ConvertRawValueToPrimitive(string? rawValue, Type primitiveType, string? parameterName, out Error? conversionError)
    {
        conversionError = null;

        var convertMethod = typeof(PrimitiveConverter)
            .GetMethod(nameof(PrimitiveConverter.ConvertToPrimitive), BindingFlags.Public | BindingFlags.Static);

        if (convertMethod is null)
            return null;

        object? conversionResult;
        try
        {
            conversionResult = convertMethod.MakeGenericMethod(primitiveType).Invoke(null, [rawValue]);
        }
        catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or TypeLoadException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }

        if (conversionResult is IResult failure && failure.TryGetError(out var convError))
        {
            var fieldName = parameterName ?? string.Empty;
            var detail = string.IsNullOrEmpty(rawValue)
                ? $"'{fieldName}' is required."
                : convError!.Detail ?? convError.Code;

            conversionError = new Error.InvalidInput(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(fieldName), ViolationProjection.LegacyUnspecifiedCode, Detail: detail)));
            return null;
        }

        // Extract the value via TryGetValue(out _) since the public Value property was removed.
        return ExtractSuccessValue(conversionResult);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "TryGetValue is preserved by being public on IResult<TValue> implementations used here.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "TryGetValue is preserved by being public on IResult<TValue> implementations used here.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection-based validation fallback is not Native AOT compatible.")]
    private static object? ExtractSuccessValue(object? conversionResult)
    {
        if (conversionResult is null)
            return null;

        var tryGetValue = conversionResult.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
            {
                if (method.Name != "TryGetValue")
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsByRef;
            });

        if (tryGetValue is null)
            return null;

        var args = new object?[] { null };
        var ok = tryGetValue.Invoke(conversionResult, args) as bool?;
        return ok == true ? args[0] : null;
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