namespace Trellis;

using System.Linq.Expressions;

/// <summary>
/// Base class for the Specification pattern. Encapsulates a business rule
/// as a composable, storage-agnostic expression tree.
/// </summary>
/// <typeparam name="T">The type of entity this specification applies to.</typeparam>
/// <remarks>
/// <para>
/// The Specification pattern enables:
/// <list type="bullet">
/// <item>Encapsulating business rules as reusable, named objects</item>
/// <item>Composing complex rules from simple ones using <see cref="And"/>, <see cref="Or"/>, and <see cref="Not"/></item>
/// <item>Passing specifications to repositories for server-side filtering via expression trees</item>
/// <item>In-memory evaluation via <see cref="IsSatisfiedBy"/></item>
/// </list>
/// </para>
/// <para>
/// Specifications are storage-agnostic domain concepts. They produce expression trees
/// that LINQ providers (such as EF Core 8+) can translate to SQL. Repository interfaces
/// accept <c>Specification&lt;T&gt;</c> and the ACL layer applies them to queries.
/// </para>
/// </remarks>
/// <example>
/// Define a specification:
/// <code><![CDATA[
/// public class OverdueOrderSpec(DateTimeOffset now) : Specification<Order>
/// {
///     public override Expression<Func<Order, bool>> ToExpression() =>
///         order => order.Status == OrderStatus.Submitted
///               && order.SubmittedAt < now.AddDays(-30);
/// }
/// ]]></code>
/// Compose specifications:
/// <code><![CDATA[
/// var spec = new OverdueOrderSpec(now).And(new HighValueOrderSpec(500m));
/// var orders = await repository.ListAsync(spec, ct);
/// ]]></code>
/// In-memory evaluation:
/// <code><![CDATA[
/// if (spec.IsSatisfiedBy(order))
///     // order matches the specification
/// ]]></code>
/// </example>
public abstract class Specification<T>
{
    /// <summary>
    /// The expression tree representing this specification's business rule.
    /// Consumers (repositories, LINQ providers) use this to filter data.
    /// </summary>
    /// <returns>An expression tree that can be compiled for in-memory evaluation
    /// or translated by a LINQ provider for server-side filtering.</returns>
    public abstract Expression<Func<T, bool>> ToExpression();

    /// <summary>
    /// Evaluates this specification against an in-memory instance.
    /// </summary>
    /// <param name="entity">The entity to evaluate against this specification.</param>
    /// <returns><c>true</c> if the entity satisfies this specification; otherwise, <c>false</c>.</returns>
    public bool IsSatisfiedBy(T entity) =>
        ToExpression().Compile()(entity);

    /// <summary>Combines this specification with another using logical AND.</summary>
    /// <param name="other">The specification to combine with.</param>
    /// <returns>A new specification that is satisfied only when both specifications are satisfied.</returns>
    public Specification<T> And(Specification<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new AndSpecification<T>(this, other);
    }

    /// <summary>Combines this specification with another using logical OR.</summary>
    /// <param name="other">The specification to combine with.</param>
    /// <returns>A new specification that is satisfied when either specification is satisfied.</returns>
    public Specification<T> Or(Specification<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new OrSpecification<T>(this, other);
    }

    /// <summary>Negates this specification.</summary>
    /// <returns>A new specification that is satisfied when this specification is not satisfied.</returns>
    public Specification<T> Not() =>
        new NotSpecification<T>(this);

    /// <summary>
    /// Implicit conversion to <see cref="Expression{TDelegate}"/> for seamless LINQ integration.
    /// Enables: <c>query.Where(spec)</c> without calling <see cref="ToExpression"/>.
    /// </summary>
    /// <param name="spec">The specification to convert.</param>
    /// <returns>The expression tree representing this specification's business rule.</returns>
    public static implicit operator Expression<Func<T, bool>>(Specification<T> spec) =>
        spec.ToExpression();
}