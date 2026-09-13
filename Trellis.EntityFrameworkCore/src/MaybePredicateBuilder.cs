namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;

internal static class MaybePredicateBuilder
{
    internal static Expression Build(Expression storage, LambdaExpression predicate)
    {
        var parameter = predicate.Parameters[0];
        Expression value = parameter.Type.IsValueType
            ? Expression.Property(storage, "Value")
            : storage.Type == parameter.Type
                ? storage
                : Expression.Convert(storage, parameter.Type);

        var body = new ParameterReplacer(parameter, value).Visit(predicate.Body);
        var hasValue = Expression.NotEqual(storage, Expression.Constant(null, storage.Type));
        return Expression.AndAlso(hasValue, body);
    }

    private sealed class ParameterReplacer(ParameterExpression target, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == target ? replacement : node;
    }
}
