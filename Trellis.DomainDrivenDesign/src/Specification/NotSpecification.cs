namespace Trellis;

using System.Linq.Expressions;

/// <summary>
/// Negates a specification.
/// This specification is satisfied when the inner specification is not satisfied.
/// </summary>
/// <typeparam name="T">The type of entity this specification applies to.</typeparam>
internal sealed class NotSpecification<T>(Specification<T> inner)
    : Specification<T>
{
    private readonly Specification<T> _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <inheritdoc />
    public override Expression<Func<T, bool>> ToExpression()
    {
        var innerExpr = _inner.ToExpression();
        var param = Expression.Parameter(typeof(T));
        var body = Expression.Not(Expression.Invoke(innerExpr, param));
        return Expression.Lambda<Func<T, bool>>(body, param);
    }
}