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
        // Defense in depth: the validation pipeline (IValidate on the command) normally
        // short-circuits on an empty batch, but a handler must still fail soft when invoked
        // directly (e.g., from a test or a misconfigured pipeline) rather than throwing.
        if (command.Lines.Count == 0)
        {
            return ValueTask.FromResult(Result.Fail<BatchTransferReceipt>(
                new Error.UnprocessableContent(EquatableArray.Create(
                [
                    new FieldViolation(
                        InputPointer.ForProperty(nameof(command.Lines)),
                        "batch.empty")
                    { Detail = "At least one line is required." },
                ]))));
        }

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
        if (!totalResult.TryGetValue(out var total, out var totalError))
            return ValueTask.FromResult(Result.Fail<BatchTransferReceipt>(totalError));

        return ValueTask.FromResult(Result.Ok(
            new BatchTransferReceipt(command.Metadata.Reference, command.Lines.Count, total)));
    }
}
