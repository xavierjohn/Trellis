namespace Trellis.Analyzers;

using Microsoft.CodeAnalysis;

/// <summary>
/// Shared extension methods for type symbol analysis across Trellis analyzers.
/// </summary>
internal static class TypeSymbolExtensions
{
    /// <summary>
    /// Checks if the type is Result&lt;T&gt; from Trellis namespace.
    /// </summary>
    internal static bool IsResultType(this ITypeSymbol? typeSymbol) =>
        typeSymbol is INamedTypeSymbol { Name: "Result", TypeArguments.Length: 1 } namedType &&
        namedType.ContainingNamespace?.ToDisplayString() == "Trellis";

    /// <summary>
    /// Checks if the type is Maybe&lt;T&gt; from Trellis namespace.
    /// </summary>
    internal static bool IsMaybeType(this ITypeSymbol? typeSymbol) =>
        typeSymbol is INamedTypeSymbol { Name: "Maybe", TypeArguments.Length: 1 } namedType &&
        namedType.ContainingNamespace?.ToDisplayString() == "Trellis";

    /// <summary>
    /// Checks if the type is Error or a derived error type from Trellis namespace.
    /// </summary>
    internal static bool IsErrorOrDerivedType(this ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
            return false;

        // Direct match
        if (typeSymbol.Name == "Error" &&
            typeSymbol.ContainingNamespace?.ToDisplayString() == "Trellis")
            return true;

        // Check base type hierarchy
        for (var baseType = typeSymbol.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            if (baseType.Name == "Error" &&
                baseType.ContainingNamespace?.ToDisplayString() == "Trellis")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the type is Task&lt;T&gt; or ValueTask&lt;T&gt; from System.Threading.Tasks.
    /// </summary>
    internal static bool IsTaskType(this ITypeSymbol? typeSymbol) =>
        typeSymbol is INamedTypeSymbol { TypeArguments.Length: 1 } namedType &&
        namedType.Name is "Task" or "ValueTask" &&
        namedType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";

    /// <summary>
    /// Checks if the type is Task, Task&lt;T&gt;, ValueTask, or ValueTask&lt;T&gt; from System.Threading.Tasks.
    /// Unlike <see cref="IsTaskType"/>, this also matches non-generic Task and ValueTask.
    /// </summary>
    internal static bool IsAnyTaskType(this ITypeSymbol? typeSymbol) =>
        typeSymbol is INamedTypeSymbol namedType &&
        namedType.Name is "Task" or "ValueTask" &&
        namedType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";

    /// <summary>
    /// Checks if the type is Task&lt;Result&lt;T&gt;&gt; or ValueTask&lt;Result&lt;T&gt;&gt;.
    /// </summary>
    internal static bool IsTaskWrappingResult(this ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol { TypeArguments.Length: 1 } namedType)
            return false;

        if (!namedType.IsTaskType())
            return false;

        return namedType.TypeArguments[0].IsResultType();
    }

    /// <summary>
    /// Checks if the type is Task&lt;Result&lt;T&gt;&gt; or ValueTask&lt;Result&lt;T&gt;&gt; and returns the inner type.
    /// </summary>
    internal static bool IsAsyncResultType(this ITypeSymbol? typeSymbol, out string? resultInnerType)
    {
        resultInnerType = null;

        if (typeSymbol is not INamedTypeSymbol { TypeArguments.Length: 1 } namedType)
            return false;

        // Check if it's Task<T> or ValueTask<T> from System.Threading.Tasks
        var isTaskType = (namedType.Name is "Task" or "ValueTask") &&
                         namedType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";

        if (!isTaskType)
            return false;

        // Check if the type argument is Result<T> from Trellis
        var typeArgument = namedType.TypeArguments[0];
        if (typeArgument is INamedTypeSymbol { Name: "Result", TypeArguments.Length: 1 } resultType &&
            resultType.ContainingNamespace?.ToDisplayString() == "Trellis")
        {
            resultInnerType = resultType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the type is Result&lt;Result&lt;T&gt;&gt; (double-wrapped) and returns the inner type name.
    /// </summary>
    internal static bool IsDoubleWrappedResult(this ITypeSymbol? typeSymbol, out string? innerType)
    {
        innerType = null;

        // Check if outer type is Result<T> with exactly 1 type argument
        if (typeSymbol is not INamedTypeSymbol { Name: "Result", TypeArguments.Length: 1 } outerResult ||
            outerResult.ContainingNamespace?.ToDisplayString() != "Trellis")
            return false;

        // Check if inner type is also Result<T>
        var innerTypeSymbol = outerResult.TypeArguments[0];
        if (innerTypeSymbol is INamedTypeSymbol { Name: "Result", TypeArguments.Length: 1 } innerResult &&
            innerResult.ContainingNamespace?.ToDisplayString() == "Trellis")
        {
            innerType = innerResult.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the method is a Trellis extension method (IsExtensionMethod and in the Trellis namespace).
    /// </summary>
    internal static bool IsTrellisExtensionMethod(this IMethodSymbol methodSymbol) =>
        methodSymbol.IsExtensionMethod &&
        methodSymbol.ContainingType?.ContainingNamespace?.ToDisplayString() == "Trellis";

    /// <summary>
    /// Checks if the type is Maybe&lt;Maybe&lt;T&gt;&gt; (double-wrapped) and returns the inner type name.
    /// </summary>
    internal static bool IsDoubleWrappedMaybe(this ITypeSymbol? typeSymbol, out string? innerType)
    {
        innerType = null;

        // Check if outer type is Maybe<T> with exactly 1 type argument
        if (typeSymbol is not INamedTypeSymbol { Name: "Maybe", TypeArguments.Length: 1 } outerMaybe ||
            outerMaybe.ContainingNamespace?.ToDisplayString() != "Trellis")
            return false;

        // Check if inner type is also Maybe<T>
        var innerTypeSymbol = outerMaybe.TypeArguments[0];
        if (innerTypeSymbol is INamedTypeSymbol { Name: "Maybe", TypeArguments.Length: 1 } innerMaybe &&
            innerMaybe.ContainingNamespace?.ToDisplayString() == "Trellis")
        {
            innerType = innerMaybe.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return true;
        }

        return false;
    }
}