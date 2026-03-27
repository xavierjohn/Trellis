namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// An EF Core query expression interceptor that automatically rewrites <c>.Value</c> property
/// accesses on <see cref="ScalarValueObject{TSelf, T}"/> types in LINQ expression trees.
/// </summary>
/// <remarks>
/// <para>
/// When registered, this interceptor allows natural LINQ syntax with scalar value object
/// properties in queries and specifications, using <c>.Value</c> to access the underlying primitive.
/// Without this interceptor, EF Core cannot translate <c>.Value</c> because it doesn't understand
/// the <c>ScalarValueObject</c> property navigation.
/// </para>
/// <para>
/// Register via <c>optionsBuilder.AddTrellisInterceptors()</c> — this registers both
/// the <see cref="MaybeQueryInterceptor"/> and this interceptor.
/// </para>
/// </remarks>
/// <example>
/// With this interceptor registered, these LINQ expressions work directly:
/// <code>
/// // Specifications using .Value on value object properties
/// public override Expression&lt;Func&lt;Customer, bool&gt;&gt; ToExpression() =&gt;
///     c =&gt; c.Name.Value.StartsWith("A");
///
/// // Inline LINQ with .Value
/// context.Customers.Where(c =&gt; c.Name.Value == "Alice");
/// </code>
/// </example>
public sealed class ScalarValueQueryInterceptor : IQueryExpressionInterceptor
{
    /// <summary>
    /// Rewrites the query expression tree before compilation, stripping <c>.Value</c>
    /// access on scalar value object properties.
    /// </summary>
    public Expression QueryCompilationStarting(
        Expression queryExpression,
        QueryExpressionEventData eventData) =>
        ScalarValueExpressionRewriter.Rewrite(queryExpression);
}
