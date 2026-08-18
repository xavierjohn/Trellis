namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

/// <summary>
/// An EF Core <see cref="IEvaluatableExpressionFilterPlugin"/> that prevents funcletization of
/// literal <see cref="Maybe{T}"/> operand shapes so <see cref="MaybeExpressionRewriter"/> can
/// preserve the syntactic distinction between <c>None</c> and <c>Some(value)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> EF Core's parameter extraction (funcletization) lifts closed
/// sub-expressions to <c>QueryParameterExpression</c>s <em>before</em>
/// <c>IQueryExpressionInterceptor.QueryCompilationStarting</c> runs. Without this plugin,
/// <c>Maybe&lt;T&gt;.None</c>, <c>default(Maybe&lt;T&gt;)</c>, and <c>Maybe.From(value)</c> all
/// collapse to opaque parameters of type <see cref="Maybe{T}"/>, erasing the difference between
/// "no value" and "some captured value". The previous behavior conservatively translated every
/// such parameter to typed null, which silently miss-queried
/// <c>c.Phone == Maybe.From(value)</c> to <c>_phone IS NULL</c>.
/// </para>
/// <para>
/// <b>What it blocks.</b> Only the three literal operand shapes the rewriter knows how to
/// translate:
/// <list type="bullet">
/// <item><description><c>Maybe&lt;T&gt;.None</c> — static property access on the closed generic type (declared <c>public static Maybe&lt;T&gt; None =&gt; default;</c>).</description></item>
/// <item><description><c>default(Maybe&lt;T&gt;)</c> — <see cref="DefaultExpression"/> of type <c>Maybe&lt;T&gt;</c>.</description></item>
/// <item><description><c>Maybe.From(value)</c> / <c>Maybe&lt;T&gt;.From(value)</c> — static factory call returning <c>Maybe&lt;T&gt;</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>What it does not block.</b> Captured local variables of type <see cref="Maybe{T}"/>
/// remain evaluatable and funcletize to <c>QueryParameterExpression</c>s as before;
/// <see cref="MaybeExpressionRewriter"/> throws a clear <see cref="InvalidOperationException"/>
/// when it encounters such a parameter, naming the supported inline alternatives. This trades
/// the historic silent miss-query for an explicit, actionable failure.
/// </para>
/// <para>
/// Registered as a singleton via <see cref="MaybeEvaluatableExpressionFilterExtension"/>;
/// consumers opt in by calling
/// <see cref="DbContextOptionsBuilderExtensions.AddTrellisInterceptors{TContext}(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder{TContext})"/>.
/// </para>
/// </remarks>
internal sealed class MaybeEvaluatableExpressionFilterPlugin : IEvaluatableExpressionFilterPlugin
{
    /// <inheritdoc />
    public bool IsEvaluatableExpression(Expression expression) =>
        expression switch
        {
            // Maybe<T>.None — static property access (Expression == null for static members).
            MemberExpression { Expression: null, Member.Name: "None" } m when IsMaybeType(m.Type) => false,

            // default(Maybe<T>)
            DefaultExpression d when IsMaybeType(d.Type) => false,

            // Maybe.From(value) — static factory on the non-generic Maybe class.
            // Maybe<T>.From(value) — static factory on the closed generic type (if it exists).
            MethodCallExpression mc when IsMaybeFromCall(mc) => false,

            _ => true,
        };

    private static bool IsMaybeType(System.Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Maybe<>);

    private static bool IsMaybeFromCall(MethodCallExpression call)
    {
        if (call.Method.Name != "From" || !call.Method.IsStatic || !IsMaybeType(call.Type))
            return false;

        var declaring = call.Method.DeclaringType;
        if (declaring is null)
            return false;

        // Maybe.From<T>(T) on the non-generic Maybe class.
        if (declaring == typeof(Maybe))
            return true;

        // Maybe<T>.From(T) on the closed generic Maybe<T> class, if such a static factory exists.
        if (declaring.IsGenericType && declaring.GetGenericTypeDefinition() == typeof(Maybe<>))
            return true;

        return false;
    }
}