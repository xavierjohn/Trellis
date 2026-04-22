namespace Trellis.Mediator;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using global::Mediator;

/// <summary>
/// Unified validation stage of the Trellis Mediator pipeline. Runs the compile-time
/// <see cref="IValidate"/> contract (when the message implements it) and every
/// <see cref="IMessageValidator{TMessage}"/> registered for <typeparamref name="TMessage"/>
/// in DI, aggregates all <see cref="Error.UnprocessableContent"/> failures into a single
/// response failure, and short-circuits the pipeline before the handler is invoked.
/// </summary>
/// <remarks>
/// <para>
/// The behavior runs for every message — including messages that do not implement
/// <see cref="IValidate"/> and have no registered <see cref="IMessageValidator{TMessage}"/> —
/// in which case it is a no-op pass-through with one DI resolve and one type test.
/// </para>
/// <para>
/// Failure aggregation rules:
/// <list type="bullet">
///   <item><description>Multiple <see cref="Error.UnprocessableContent"/> failures (from
///   <see cref="IValidate"/> and any number of <see cref="IMessageValidator{TMessage}"/>
///   instances) are merged into a single <see cref="Error.UnprocessableContent"/> whose
///   <see cref="Error.UnprocessableContent.Fields"/> contains every reported violation.</description></item>
///   <item><description>A non-<see cref="Error.UnprocessableContent"/> failure (e.g.,
///   <see cref="Error.Conflict"/>, <see cref="Error.Forbidden"/>) returned by any validation
///   source short-circuits the stage immediately and that failure is propagated as-is, with
///   no further validators consulted.</description></item>
/// </list>
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">
/// The response type, constrained to <see cref="IResult"/> and <see cref="IFailureFactory{TSelf}"/>
/// so the behavior can construct typed failures without reflection.
/// </typeparam>
public sealed class ValidationBehavior<TMessage, TResponse>(
    IEnumerable<IMessageValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : global::Mediator.IMessage
    where TResponse : IResult, IFailureFactory<TResponse>
{
    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        List<FieldViolation>? violations = null;

        if (message is IValidate validatable)
        {
            var validateResult = validatable.Validate();
            if (validateResult.TryGetError(out var error))
            {
                if (error is Error.UnprocessableContent upc)
                    violations = [.. upc.Fields.Items];
                else
                    return TResponse.CreateFailure(error);
            }
        }

        foreach (var validator in validators)
        {
            var externalResult = await validator
                .ValidateAsync(message, cancellationToken)
                .ConfigureAwait(false);

            if (!externalResult.TryGetError(out var error))
                continue;

            if (error is Error.UnprocessableContent upc)
            {
                violations ??= [];
                violations.AddRange(upc.Fields.Items);
                continue;
            }

            return TResponse.CreateFailure(error);
        }

        if (violations is { Count: > 0 })
            return TResponse.CreateFailure(new Error.UnprocessableContent(EquatableArray.Create(violations.ToArray())));

        return await next(message, cancellationToken).ConfigureAwait(false);
    }
}
