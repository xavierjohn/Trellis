namespace Trellis.FluentValidation;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using global::FluentValidation;
using global::Mediator;
using Trellis;

/// <summary>
/// Pipeline behavior that runs every <see cref="IValidator{T}"/> registered for
/// <typeparamref name="TMessage"/> in the DI container, aggregates their failures into a single
/// <see cref="Error.UnprocessableContent"/>, and short-circuits the pipeline on validation failure.
/// </summary>
/// <remarks>
/// <para>
/// Registered as an open-generic <see cref="IPipelineBehavior{TMessage, TResponse}"/> by
/// <see cref="FluentValidationServiceCollectionExtensions.AddTrellisFluentValidation(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
/// When no <see cref="IValidator{T}"/> is registered for a given <typeparamref name="TMessage"/>,
/// the injected <see cref="IEnumerable{T}"/> resolves to an empty sequence and the behavior is
/// a no-op pass-through, so registering it for every message in the pipeline is cheap.
/// </para>
/// <para>
/// This behavior is complementary to the <c>ValidationBehavior</c> in <c>Trellis.Mediator</c> which
/// invokes the compile-time <c>IValidate</c> interface. Both can coexist; the IValidate behavior
/// runs first (registered earlier), then this behavior runs FluentValidation discovery.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">
/// The response type, constrained to <see cref="IResult"/> and <see cref="IFailureFactory{TSelf}"/>
/// so the behavior can construct a typed failure without reflection.
/// </typeparam>
public sealed class FluentValidationBehavior<TMessage, TResponse>(
    IEnumerable<IValidator<TMessage>> validators)
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

        foreach (var validator in validators)
        {
            var validationResult = await validator
                .ValidateAsync(message, cancellationToken)
                .ConfigureAwait(false);

            if (validationResult.IsValid)
                continue;

            violations ??= [];
            foreach (var failure in validationResult.Errors)
            {
                var propertyName = string.IsNullOrWhiteSpace(failure.PropertyName)
                    ? typeof(TMessage).Name
                    : failure.PropertyName;
                var reasonCode = string.IsNullOrWhiteSpace(failure.ErrorCode)
                    ? "validation.error"
                    : failure.ErrorCode;
                violations.Add(new FieldViolation(InputPointer.ForProperty(propertyName), reasonCode)
                {
                    Detail = failure.ErrorMessage,
                });
            }
        }

        if (violations is { Count: > 0 })
            return TResponse.CreateFailure(new Error.UnprocessableContent(EquatableArray.Create(violations.ToArray())));

        return await next(message, cancellationToken).ConfigureAwait(false);
    }
}
