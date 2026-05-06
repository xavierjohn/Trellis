namespace Trellis.FluentValidation;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using global::FluentValidation;
using Trellis;
using Trellis.Mediator;

/// <summary>
/// Adapts FluentValidation <see cref="IValidator{T}"/> implementations into the Trellis
/// Mediator validation stage. Resolves every <c>IValidator&lt;TMessage&gt;</c> registered in DI,
/// runs them, and returns an aggregated <see cref="Error.UnprocessableContent"/> failure when
/// any validator reports errors.
/// </summary>
/// <remarks>
/// <para>
/// Registered as an open-generic <see cref="IMessageValidator{TMessage}"/> by
/// <see cref="FluentValidationServiceCollectionExtensions.AddTrellisFluentValidation(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
/// When no <see cref="IValidator{T}"/> is registered for a given <typeparamref name="TMessage"/>,
/// the injected sequence is empty and the adapter returns success without allocating violations.
/// </para>
/// <para>
/// FluentValidation property names that include member chains (<c>Address.PostCode</c>) or
/// indexers (<c>Items[0].Sku</c>) are translated to RFC 6901 JSON Pointers
/// (<c>/Address/PostCode</c>, <c>/Items/0/Sku</c>) so they round-trip correctly through
/// <see cref="InputPointer"/>.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The message type the contained validators target.</typeparam>
public sealed class FluentValidationMessageValidatorAdapter<TMessage>
    : IMessageValidator<TMessage>
    where TMessage : global::Mediator.IMessage
{
    private readonly IEnumerable<IValidator<TMessage>> _validators;

    /// <summary>
    /// Initializes a new instance of <see cref="FluentValidationMessageValidatorAdapter{TMessage}"/>.
    /// </summary>
    /// <param name="validators">
    /// The injected sequence of <see cref="IValidator{T}"/> implementations to run against each
    /// message. Typically resolved from DI as <c>IEnumerable&lt;IValidator&lt;TMessage&gt;&gt;</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="validators"/> is null.</exception>
    public FluentValidationMessageValidatorAdapter(IEnumerable<IValidator<TMessage>> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);
        _validators = validators;
    }

    /// <inheritdoc />
    public async ValueTask<IResult> ValidateAsync(
        TMessage message,
        CancellationToken cancellationToken)
    {
        List<FieldViolation>? violations = null;

        foreach (var validator in _validators)
        {
            var validationResult = await validator
                .ValidateAsync(message, cancellationToken)
                .ConfigureAwait(false);

            if (validationResult.IsValid)
                continue;

            violations ??= [];
            foreach (var failure in validationResult.Errors)
            {
                var rawName = string.IsNullOrWhiteSpace(failure.PropertyName)
                    ? typeof(TMessage).Name
                    : failure.PropertyName;
                var pointerPath = JsonPointerNormalizer.ToJsonPointer(rawName);
                var reasonCode = string.IsNullOrWhiteSpace(failure.ErrorCode)
                    ? "validation.error"
                    : failure.ErrorCode;
                violations.Add(new FieldViolation(new InputPointer(pointerPath), reasonCode)
                {
                    Detail = failure.ErrorMessage,
                });
            }
        }

        if (violations is { Count: > 0 })
            return Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(violations.ToArray())));

        return Result.Ok();
    }
}