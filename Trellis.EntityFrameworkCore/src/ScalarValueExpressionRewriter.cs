namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// An <see cref="ExpressionVisitor"/> that rewrites scalar value object expressions
/// in LINQ so EF Core can translate them to SQL.
/// </summary>
/// <remarks>
/// <para>
/// EF Core maps scalar value objects via value converters (registered by <see cref="ModelConfigurationBuilderExtensions.ApplyTrellisConventions"/>),
/// but the LINQ translator cannot navigate through value object properties or methods directly.
/// This visitor rewrites three patterns:
/// </para>
/// <list type="table">
/// <listheader><term>Pattern</term><description>Rewrite</description></listheader>
/// <item><term><c>.Value</c> access</term><description><c>entity.Name.Value</c> → <c>Convert(entity.Name, string)</c></description></item>
/// <item><term>String methods</term><description><c>entity.Name.StartsWith("Al")</c> → <c>((string)entity.Name).StartsWith("Al")</c></description></item>
/// <item><term>Provider-type properties</term><description><c>entity.Name.Length</c> → <c>((string)entity.Name).Length</c></description></item>
/// </list>
/// <para>
/// Handles any expression typed as a scalar value object, including direct property access,
/// parameter expressions from projections, and method call results from other rewriters.
/// </para>
/// </remarks>
internal sealed class ScalarValueExpressionRewriter : ExpressionVisitor
{
    /// <summary>
    /// Rewrites an expression tree, converting scalar value object expressions
    /// (<c>.Value</c> access, string methods, and provider-type properties) to forms
    /// that EF Core can translate via registered value converters.
    /// </summary>
    public static Expression Rewrite(Expression expression) =>
        new ScalarValueExpressionRewriter().Visit(expression);

    /// <summary>
    /// Intercepts member access to replace <c>.Value</c> on any expression whose type is a
    /// scalar value object with <c>(TProvider)expr</c> using the implicit operator.
    /// Handles direct property access, parameter expressions, and method call results
    /// (e.g., from MaybeExpressionRewriter's EF.Property output).
    /// </summary>
    protected override Expression VisitMember(MemberExpression node)
    {
        // Pattern 1: expr.Value where expr is a scalar VO → Convert to provider type
        // Handles: entity.Name.Value, parameterExpr.Value, EF.Property(...).Value
        if (node.Member.Name == "Value"
            && node.Expression is not null
            && TrellisTypeScanner.FindValueObject(node.Expression.Type) is { Category: TrellisValueObjectCategory.Scalar } info)
        {
            var visited = Visit(node.Expression);
            var conversionMethod = FindImplicitConversion(node.Expression.Type, info.ProviderType);
            return conversionMethod is not null
                ? Expression.Convert(visited, info.ProviderType, conversionMethod)
                : Expression.Convert(visited, info.ProviderType);
        }

        // Pattern 2: expr.Length (or other provider-type properties) where expr is a scalar VO
        // → ((TProvider)expr).Length
        if (node.Expression is not null
            && node.Member.Name != "Value"
            && TrellisTypeScanner.FindValueObject(node.Expression.Type) is { Category: TrellisValueObjectCategory.Scalar } voInfo)
        {
            var providerProperty = voInfo.ProviderType.GetProperty(node.Member.Name);
            if (providerProperty is not null)
            {
                var visited = Visit(node.Expression);
                var conversionMethod = FindImplicitConversion(node.Expression.Type, voInfo.ProviderType);
                var converted = conversionMethod is not null
                    ? Expression.Convert(visited, voInfo.ProviderType, conversionMethod)
                    : Expression.Convert(visited, voInfo.ProviderType);
                return Expression.Property(converted, providerProperty);
            }
        }

        return base.VisitMember(node);
    }

    /// <summary>
    /// Intercepts method calls on scalar value objects (e.g., <c>entity.Name.StartsWith("Al")</c>)
    /// and rewrites to call the method on the provider type via Convert:
    /// <c>((string)entity.Name).StartsWith("Al")</c>.
    /// </summary>
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // Instance methods on scalar value objects (e.g., Name.StartsWith, Name.Contains)
        // Handles: entity.Name.StartsWith(...), parameterExpr.StartsWith(...)
        if (node.Object is not null
            && TrellisTypeScanner.FindValueObject(node.Object.Type) is { Category: TrellisValueObjectCategory.Scalar } info)
        {
            // Find the equivalent method on the provider type (e.g., string.StartsWith)
            var providerMethod = info.ProviderType.GetMethod(
                node.Method.Name,
                node.Method.GetParameters().Select(p => p.ParameterType).ToArray());

            if (providerMethod is not null)
            {
                var visited = Visit(node.Object);
                var conversionMethod = FindImplicitConversion(node.Object.Type, info.ProviderType);
                var converted = conversionMethod is not null
                    ? Expression.Convert(visited, info.ProviderType, conversionMethod)
                    : Expression.Convert(visited, info.ProviderType);

                var visitedArgs = Visit(node.Arguments);
                return Expression.Call(converted, providerMethod, visitedArgs);
            }
        }

        return base.VisitMethodCall(node);
    }

    /// <summary>
    /// Finds the implicit conversion operator from a ScalarValueObject type to the provider type
    /// by searching the type hierarchy.
    /// </summary>
    private static MethodInfo? FindImplicitConversion(Type sourceType, Type targetType)
    {
        var current = sourceType;
        while (current is not null)
        {
            var method = current.GetMethod("op_Implicit",
                BindingFlags.Public | BindingFlags.Static,
                null,
                [current],
                null);

            if (method is not null && method.ReturnType == targetType)
                return method;

            current = current.BaseType;
        }

        return null;
    }
}