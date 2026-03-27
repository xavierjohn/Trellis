namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// An EF Core query expression interceptor that automatically rewrites <see cref="Maybe{T}"/>
/// property accesses in LINQ expression trees to their underlying storage member equivalents.
/// </summary>
/// <remarks>
/// <para>
/// When registered, this interceptor allows natural LINQ syntax with <see cref="Maybe{T}"/>
/// properties in queries and specifications, without requiring explicit extension methods
/// like <c>WhereHasValue</c> or <c>WhereLessThan</c>.
/// </para>
/// <para>
/// Register by calling <c>optionsBuilder.AddInterceptors(new MaybeQueryInterceptor())</c>.
/// </para>
/// </remarks>
/// <example>
/// With this interceptor registered, these LINQ expressions work directly:
/// <code>
/// // Natural LINQ — no explicit extension methods needed
/// context.Customers.Where(c => c.Phone.HasValue)
/// context.Orders.Where(o => o.SubmittedAt.GetValueOrDefault(DateTime.MaxValue) &lt; cutoff)
///
/// // Specifications with Maybe&lt;T&gt; properties
/// public override Expression&lt;Func&lt;Order, bool&gt;&gt; ToExpression() =>
///     order => order.Status == OrderStatus.Submitted
///           &amp;&amp; order.SubmittedAt.GetValueOrDefault(DateTime.MaxValue) &lt; _cutoff;
/// </code>
/// </example>
public sealed class MaybeQueryInterceptor : IQueryExpressionInterceptor
{
    /// <summary>
    /// Rewrites the query expression tree before compilation, replacing <see cref="Maybe{T}"/>
    /// property accesses with EF Core-translatable storage member references.
    /// </summary>
    public Expression QueryCompilationStarting(
        Expression queryExpression,
        QueryExpressionEventData eventData) =>
        MaybeExpressionRewriter.Rewrite(queryExpression);
}