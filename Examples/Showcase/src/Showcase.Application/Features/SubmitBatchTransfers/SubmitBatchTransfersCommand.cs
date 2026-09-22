namespace Trellis.Showcase.Application.Features.SubmitBatchTransfers;

using System.Collections.Generic;
using global::Mediator;
using Trellis;
using Trellis.Mediator;
using Trellis.Primitives;
using Trellis.Showcase.Domain.ValueObjects;

/// <summary>
/// Submit a batch of transfers from a single source account to many recipients.
///
/// <para>
/// This feature is the canonical Showcase demonstration of the Trellis Mediator
/// validation pipeline composing TWO independent validation sources for the same message:
/// </para>
/// <list type="number">
///   <item><description>The compile-time <see cref="IValidate"/> contract on the command — used
///   for cross-cutting business invariants that are awkward to express in FluentValidation
///   (here: the batch must be non-empty and no line may target the source account).</description></item>
///   <item><description>A FluentValidation <c>AbstractValidator&lt;SubmitBatchTransfersCommand&gt;</c>
///   registered in DI — used for property-shaped rules including a NESTED-property rule
///   (<c>Metadata.Reference</c>) and an INDEXER rule (<c>Lines[i].Memo</c>).</description></item>
/// </list>
/// <para>
/// Both sources fire on the same request; the
/// <see cref="ValidationBehavior{TMessage, TResponse}"/> aggregates every
/// <see cref="Error.InvalidInput"/> failure into a single response with one combined
/// <see cref="FieldViolation"/> list. Nested and indexer property names from FluentValidation
/// are translated to RFC&#8239;6901 JSON Pointers (<c>/metadata/reference</c>,
/// <c>/lines/0/memo</c>) by the FluentValidation adapter.
/// </para>
/// </summary>
public sealed record SubmitBatchTransfersCommand(
    AccountId FromId,
    BatchMetadata Metadata,
    IReadOnlyList<BatchTransferLine> Lines)
    : ICommand<Result<BatchTransferReceipt>>, IValidate
{
    public IResult Validate() =>
        Result.Ensure(Lines.Count > 0,
                () => Error.InvalidInput.ForField(nameof(Lines), "batch.empty", "At least one line is required."))
            .Check(_ => Lines.TraverseAll((line, index) =>
                Result.Ensure(line.ToAccountId != FromId,
                    () => Error.InvalidInput.ForField(
                        InputPointer.Root.AppendProperty(nameof(Lines)).AppendIndex(index)
                            .AppendProperty(nameof(BatchTransferLine.ToAccountId)),
                        "batch.self-transfer",
                        "A line may not target the source account."))));
}

public sealed record BatchMetadata(string Reference, string Description);

public sealed record BatchTransferLine(AccountId ToAccountId, Money Amount, string Memo);

public sealed record BatchTransferReceipt(string Reference, int LineCount, Money TotalAmount);