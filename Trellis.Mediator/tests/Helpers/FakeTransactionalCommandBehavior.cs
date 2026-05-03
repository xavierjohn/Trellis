// Stand-in for the real TransactionalCommandBehavior from Trellis.EntityFrameworkCore.
// Trellis.Mediator detects the real type by full name when ordering pipeline registrations,
// so this fake must live in the same namespace with the same simple name to exercise that
// path. The Trellis.Mediator test project does not reference Trellis.EntityFrameworkCore, so
// this stand-in does not conflict with the real type.
namespace Trellis.EntityFrameworkCore;

using global::Mediator;
using Trellis;

public sealed class TransactionalCommandBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : global::Mediator.IMessage
    where TResponse : IResult
{
    public ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
        => next(message, cancellationToken);
}
