namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// An <see cref="ExpressionVisitor"/> that rewrites <c>.Value</c> property access on
/// <see cref="ScalarValueObject{TSelf, T}"/> types in LINQ expression trees.
/// </summary>
/// <remarks>
/// <para>
/// EF Core maps scalar value objects via value converters (registered by <see cref="ModelConfigurationBuilderExtensions.ApplyTrellisConventions"/>),
/// but the LINQ translator cannot navigate through the <c>.Value</c> property on <c>ScalarValueObject</c>.
/// This visitor replaces <c>.Value</c> access with a <c>Convert</c> expression using the implicit operator,
/// which EF Core translates via the registered value converter.
/// </para>
/// <para>
/// Handles <c>.Value</c> access on any expression typed as a scalar value object, including:
/// direct property access (<c>entity.Name.Value</c>), parameter expressions from projections
/// (<c>Select(c =&gt; c.Name).Where(n =&gt; n.Value == "Alice")</c>), and method call results
/// from other rewriters such as <see cref="MaybeExpressionRewriter"/> (<c>Maybe&lt;PhoneNumber&gt;.Value.Value</c>).
/// </para>
/// </remarks>
internal sealed class ScalarValueExpressionRewriter : ExpressionVisitor
{
    /// <summary>
    /// Rewrites an expression tree, replacing <c>.Value</c> access on scalar value object properties
    /// with a <c>Convert</c> expression that uses the implicit operator to the provider type.
    /// EF Core recognizes Convert expressions and translates them using the registered value converter.
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