namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// An EF Core query expression interceptor that enables natural scalar value object
/// syntax in LINQ queries — comparisons, string methods, and properties translate to SQL
/// without requiring <c>.Value</c>.
/// </summary>
/// <remarks>
/// <para>
/// Register via <c>optionsBuilder.AddTrellisInterceptors()</c> — this registers both
/// the <see cref="MaybeQueryInterceptor"/> and this interceptor.
/// </para>
/// </remarks>
/// <example>
/// With this interceptor registered, these LINQ expressions work directly:
/// <code>
/// // Natural value object syntax — no .Value needed
/// context.Customers.Where(c =&gt; c.Name == "Alice");
/// context.Customers.Where(c =&gt; c.Name.StartsWith("Al"));
/// context.Customers.Where(c =&gt; c.Name.Length &gt; 3);
/// context.Customers.OrderBy(c =&gt; c.Name);
///
/// // Specifications with domain types
/// public override Expression&lt;Func&lt;TodoItem, bool&gt;&gt; ToExpression() =&gt;
///     todo =&gt; todo.Status == TodoStatus.Active &amp;&amp; todo.DueDate &lt; _asOf;
/// </code>
/// </example>
public sealed class ScalarValueQueryInterceptor : IQueryExpressionInterceptor
{
    /// <summary>
    /// Rewrites the query expression tree before compilation, converting scalar value object
    /// expressions to forms that EF Core can translate to SQL.
    /// </summary>
    public Expression QueryCompilationStarting(
        Expression queryExpression,
        QueryExpressionEventData eventData) =>
        ScalarValueExpressionRewriter.Rewrite(queryExpression);
}