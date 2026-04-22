namespace Trellis.Showcase.Application.Features.SubmitBatchTransfers;

using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Trellis;
using Trellis.Primitives;

/// <summary>
/// Handler for <see cref="SubmitBatchTransfersCommand"/>. By the time this runs the pipeline
/// has already executed <c>IValidate</c> AND every registered FluentValidation rule —
/// any failure short-circuited before reaching here, so the handler can focus purely on the
/// business work: total the line amounts and emit a receipt.
/// </summary>
public sealed class SubmitBatchTransfersHandler
    : ICommandHandler<SubmitBatchTransfersCommand, Result<BatchTransferReceipt>>
{
    public ValueTask<Result<BatchTransferReceipt>> Handle(
        SubmitBatchTransfersCommand command,
        System.Threading.CancellationToken cancellationToken)
    {
        var currency = command.Lines[0].Amount.Currency.Value;
        var sum = 0m;
        foreach (var line in command.Lines)
        {
            if (!string.Equals(line.Amount.Currency.Value, currency, System.StringComparison.Ordinal))
            {
                return ValueTask.FromResult(Result.Fail<BatchTransferReceipt>(
                    new Error.UnprocessableContent(EquatableArray.Create(
                    [
                        new FieldViolation(InputPointer.ForProperty(nameof(command.Lines)), "batch.mixed-currency")
                        { Detail = "All lines in a batch must share a single currency." },
                    ]))));
            }

            sum += line.Amount.Amount;
        }

        var totalResult = Money.TryCreate(sum, currency);
        if (totalResult.TryGetError(out var err))
            return ValueTask.FromResult(Result.Fail<BatchTransferReceipt>(err));

        return ValueTask.FromResult(Result.Ok(
            new BatchTransferReceipt(command.Metadata.Reference, command.Lines.Count, totalResult.Value)));
    }
}
