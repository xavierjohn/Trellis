namespace Trellis.Mediator;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Contributes a validation step to the unified validation stage of the Trellis Mediator
/// pipeline (<see cref="ValidationBehavior{TMessage, TResponse}"/>).
/// </summary>
/// <remarks>
/// <para>
/// Implementations are resolved from DI as <c>IEnumerable&lt;IMessageValidator&lt;TMessage&gt;&gt;</c>
/// by <see cref="ValidationBehavior{TMessage, TResponse}"/>. Every registered validator runs
/// before the handler executes; their <see cref="Error.UnprocessableContent"/> failures are
/// aggregated into a single response failure. Any non-<see cref="Error.UnprocessableContent"/>
/// failure short-circuits the stage immediately.
/// </para>
/// <para>
/// This is the extensibility point that lets external packages (e.g.,
/// <c>Trellis.FluentValidation</c>) plug additional validation sources into the pipeline
/// without taking a dependency on a specific message-side interface or validation library
/// from <c>Trellis.Mediator</c>.
/// </para>
/// <para>
/// Implementations should return <c>Result.Ok()</c> when validation passes, and
/// <c>Result.Fail(new Error.UnprocessableContent(...))</c> with field-level violations when it
/// fails. Returning a non-<see cref="Error.UnprocessableContent"/> failure is allowed but
/// will short-circuit subsequent validators in the same request.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The message type to validate.</typeparam>
public interface IMessageValidator<in TMessage>
    where TMessage : global::Mediator.IMessage
{
    /// <summary>
    /// Validates the message asynchronously.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// <c>Result.Ok()</c> on success, or a failure <see cref="IResult"/> describing the violations.
    /// Field-level violations should be wrapped in an <see cref="Error.UnprocessableContent"/>
    /// so the pipeline can aggregate them across multiple validators.
    /// </returns>
    ValueTask<IResult> ValidateAsync(TMessage message, CancellationToken cancellationToken);
}
