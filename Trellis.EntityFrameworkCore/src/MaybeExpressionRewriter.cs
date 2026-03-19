namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// An <see cref="ExpressionVisitor"/> that rewrites <see cref="Maybe{T}"/> property accesses
/// in LINQ expression trees to use the underlying EF Core storage member via <see cref="EF.Property{TProperty}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Because <see cref="MaybeConvention"/> ignores <see cref="Maybe{T}"/> CLR properties and maps
/// only the generated <c>_camelCase</c> backing field, EF Core cannot translate direct LINQ
/// references to <see cref="Maybe{T}"/> properties. This visitor transparently rewrites such
/// references so that specifications, inline LINQ, and generic repository patterns work without
/// requiring explicit <c>WhereHasValue</c> / <c>WhereLessThan</c> extension methods.
/// </para>
/// <para>Supported patterns:</para>
/// <list type="table">
/// <listheader><term>Source expression</term><description>Rewritten to</description></listheader>
/// <item><term><c>entity.Phone</c></term><description><c>EF.Property&lt;T?&gt;(entity, "_phone")</c></description></item>
/// <item><term><c>entity.Phone.HasValue</c></term><description><c>EF.Property&lt;T?&gt;(entity, "_phone") != null</c></description></item>
/// <item><term><c>entity.Phone.HasNoValue</c></term><description><c>EF.Property&lt;T?&gt;(entity, "_phone") == null</c></description></item>
/// <item><term><c>entity.Phone.Value</c></term><description><c>EF.Property&lt;T?&gt;(entity, "_phone")</c> (strip accessor)</description></item>
/// <item><term><c>entity.Phone.GetValueOrDefault(d)</c></term><description><c>EF.Property&lt;T?&gt;(entity, "_phone") ?? d</c></description></item>
/// </list>
/// </remarks>
internal sealed class MaybeExpressionRewriter : ExpressionVisitor
{
    private static readonly Type s_maybeOpenGeneric = typeof(Maybe<>);

    private static readonly MethodInfo s_efPropertyMethod =
        typeof(EF).GetMethod(nameof(EF.Property))!;

    /// <summary>
    /// Rewrites an expression tree, replacing all <see cref="Maybe{T}"/> property accesses
    /// with their EF Core storage member equivalents.
    /// </summary>
    public static Expression Rewrite(Expression expression) =>
        new MaybeExpressionRewriter().Visit(expression);

    /// <summary>
    /// Intercepts member access expressions to detect <see cref="Maybe{T}"/> patterns:
    /// <c>entity.Phone</c>, <c>entity.Phone.HasValue</c>, <c>entity.Phone.HasNoValue</c>, <c>entity.Phone.Value</c>.
    /// </summary>
    protected override Expression VisitMember(MemberExpression node)
    {
        // Pattern: entity.MaybeProperty.HasValue → EF.Property != null
        // Pattern: entity.MaybeProperty.HasNoValue → EF.Property == null
        // Pattern: entity.MaybeProperty.Value → EF.Property (strip accessor)
        if (node.Expression is MemberExpression innerMember
            && innerMember.Member is PropertyInfo innerProp
            && IsMaybeType(innerProp.PropertyType))
        {
            var efPropertyAccess = BuildEfPropertyAccess(innerMember);
            if (efPropertyAccess is null)
                return base.VisitMember(node);

            return node.Member.Name switch
            {
                nameof(Maybe<object>.HasValue) => Expression.NotEqual(
                    efPropertyAccess,
                    Expression.Constant(null, efPropertyAccess.Type)),

                nameof(Maybe<object>.HasNoValue) => Expression.Equal(
                    efPropertyAccess,
                    Expression.Constant(null, efPropertyAccess.Type)),

                "Value" => efPropertyAccess,

                _ => base.VisitMember(node)
            };
        }

        // Pattern: entity.MaybeProperty (bare access — rewrite to EF.Property)
        if (node.Member is PropertyInfo prop && IsMaybeType(prop.PropertyType))
        {
            var efPropertyAccess = BuildEfPropertyAccess(node);
            if (efPropertyAccess is not null)
                return efPropertyAccess;
        }

        return base.VisitMember(node);
    }

    /// <summary>
    /// Intercepts method calls to detect <see cref="Maybe{T}.GetValueOrDefault(T)"/>
    /// and rewrite to <c>EF.Property ?? defaultValue</c>.
    /// </summary>
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // Pattern: entity.MaybeProperty.GetValueOrDefault(defaultValue)
        //       → EF.Property<T?>(entity, "_field") ?? defaultValue
        if (node.Method.Name == nameof(Maybe<object>.GetValueOrDefault)
            && node.Object is MemberExpression memberExpr
            && memberExpr.Member is PropertyInfo prop
            && IsMaybeType(prop.PropertyType))
        {
            var efPropertyAccess = BuildEfPropertyAccess(memberExpr);
            if (efPropertyAccess is not null && node.Arguments.Count == 1)
            {
                var defaultValue = Visit(node.Arguments[0]);
                return Expression.Coalesce(efPropertyAccess, defaultValue);
            }
        }

        return base.VisitMethodCall(node);
    }

    /// <summary>
    /// Intercepts binary expressions to handle <see cref="Maybe{T}"/> operator overloads
    /// (== and !=) by rewriting the <see cref="Maybe{T}"/> operand.
    /// </summary>
    protected override Expression VisitBinary(BinaryExpression node)
    {
        // For == and != with Maybe<T> custom operators, EF Core won't recognize them.
        // After visiting children (which may rewrite Maybe members), rebuild the binary
        // if the original used a custom operator method on Maybe<T>.
        if (node.Method is not null && node.Method.DeclaringType is { } declaring
            && declaring.IsGenericType && declaring.GetGenericTypeDefinition() == s_maybeOpenGeneric)
        {
            var left = Visit(node.Left);
            var right = Visit(node.Right);

            // Rebuild as a simple comparison without the custom operator method
            return node.NodeType switch
            {
                ExpressionType.Equal => Expression.Equal(left, right),
                ExpressionType.NotEqual => Expression.NotEqual(left, right),
                _ => node.Update(left, node.Conversion, right)
            };
        }

        return base.VisitBinary(node);
    }

    private static MethodCallExpression? BuildEfPropertyAccess(MemberExpression maybeMemberExpr)
    {
        if (maybeMemberExpr.Member is not PropertyInfo maybeProp)
            return null;

        if (!IsMaybeType(maybeProp.PropertyType))
            return null;

        // The entity expression (e.g., the parameter 'order' in 'order.SubmittedAt')
        var entityExpr = maybeMemberExpr.Expression;
        if (entityExpr is null)
            return null;

        var innerType = maybeProp.PropertyType.GetGenericArguments()[0];
        var storeType = innerType.IsValueType
            ? typeof(Nullable<>).MakeGenericType(innerType)
            : innerType;

        var storageMemberName = MaybeFieldNaming.ToStorageMemberName(maybeProp.Name);

        var genericMethod = s_efPropertyMethod.MakeGenericMethod(storeType);
        return Expression.Call(
            genericMethod,
            Expression.Convert(entityExpr, typeof(object)),
            Expression.Constant(storageMemberName));
    }

    private static bool IsMaybeType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == s_maybeOpenGeneric;
}
